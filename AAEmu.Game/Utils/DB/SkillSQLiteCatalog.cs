using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.Utils.DB;

/// <summary>
/// Creates a read-only virtual catalogue for the skill system.
///
/// The target client database is authoritative. The fallback database is used only when
/// a table, row, or column value is absent from the target database. Both physical files
/// are attached read-only and are never modified. TEMP views are created in an in-memory
/// connection, so existing server-wide SQLite connections remain untouched.
/// </summary>
internal static class SkillSQLiteCatalog
{
    private const string TargetSchema = "skill_target";
    private const string FallbackSchema = "skill_fallback";

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private sealed record ColumnInfo(string Name, int PrimaryKeyOrder);

    public static SqliteConnection Create(string targetPath, string fallbackPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackPath);

        if (!File.Exists(targetPath))
            throw new FileNotFoundException("Target client database does not exist: " + targetPath, targetPath);
        if (!File.Exists(fallbackPath))
            throw new FileNotFoundException("Fallback client database does not exist: " + fallbackPath, fallbackPath);

        // Only the transient catalogue is writable while its TEMP views are being built.
        // Absolute paths are used for ATTACH because file: URI handling is provider- and
        // connection-flag-dependent on Windows. This method issues only schema reads and
        // TEMP VIEW statements, then makes the entire returned connection query-only.
        var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
        connection.Open();

        try
        {
            AttachReadOnly(connection, TargetSchema, targetPath);
            AttachReadOnly(connection, FallbackSchema, fallbackPath);
            BuildViews(connection);

            // Prevent accidental writes after the virtual catalogue has been prepared.
            using var queryOnly = connection.CreateCommand();
            queryOnly.CommandText = "PRAGMA query_only = ON";
            queryOnly.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void AttachReadOnly(SqliteConnection connection, string schema, string path)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"ATTACH DATABASE $path AS {QuoteIdentifier(schema)}";
        command.Parameters.Add("$path", SqliteType.Text).Value = Path.GetFullPath(path);
        command.ExecuteNonQuery();
    }

    private static void BuildViews(SqliteConnection connection)
    {
        var targetTables = ReadTables(connection, TargetSchema);
        var fallbackTables = ReadTables(connection, FallbackSchema);
        var allTables = targetTables
            .Union(fallbackTables, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mergedCount = 0;
        var targetOnlyCount = 0;
        var fallbackOnlyCount = 0;

        foreach (var table in allTables)
        {
            var hasTarget = targetTables.Contains(table);
            var hasFallback = fallbackTables.Contains(table);

            if (hasTarget && hasFallback)
            {
                CreateMergedView(connection, table);
                mergedCount++;
            }
            else if (hasTarget)
            {
                CreatePassthroughView(connection, table, TargetSchema);
                targetOnlyCount++;
            }
            else
            {
                CreatePassthroughView(connection, table, FallbackSchema);
                fallbackOnlyCount++;
            }
        }

        Logger.Info(
            "Skill SQLite catalogue ready: {0} merged tables, {1} target-only tables, {2} fallback-only tables",
            mergedCount,
            targetOnlyCount,
            fallbackOnlyCount);
    }

    private static HashSet<string> ReadTables(SqliteConnection connection, string schema)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM {QuoteIdentifier(schema)}.sqlite_master " +
                              "WHERE type IN ('table', 'view') AND name NOT LIKE 'sqlite_%'";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    private static List<ColumnInfo> ReadColumns(SqliteConnection connection, string schema, string table)
    {
        var columns = new List<ColumnInfo>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {QuoteIdentifier(schema)}.table_info({QuoteIdentifier(table)})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            columns.Add(new ColumnInfo(reader.GetString(1), reader.GetInt32(5)));
        return columns;
    }

    private static void CreatePassthroughView(SqliteConnection connection, string table, string schema)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TEMP VIEW {QuoteIdentifier(table)} AS " +
                              $"SELECT * FROM {QuoteIdentifier(schema)}.{QuoteIdentifier(table)}";
        command.ExecuteNonQuery();
    }

    private static void CreateMergedView(SqliteConnection connection, string table)
    {
        var targetColumns = ReadColumns(connection, TargetSchema, table);
        var fallbackColumns = ReadColumns(connection, FallbackSchema, table);

        if (targetColumns.Count == 0)
        {
            CreatePassthroughView(connection, table, FallbackSchema);
            return;
        }
        if (fallbackColumns.Count == 0)
        {
            CreatePassthroughView(connection, table, TargetSchema);
            return;
        }

        var targetColumnNames = new HashSet<string>(targetColumns.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        var fallbackColumnNames = new HashSet<string>(fallbackColumns.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        var allColumns = targetColumns.Select(x => x.Name)
            .Concat(fallbackColumns.Select(x => x.Name).Where(x => !targetColumnNames.Contains(x)))
            .ToArray();

        var keys = SelectIdentityColumns(targetColumns, fallbackColumns);
        if (keys.Count == 0)
        {
            CreateUnionView(connection, table, allColumns, targetColumnNames, fallbackColumnNames);
            return;
        }

        var join = string.Join(" AND ", keys.Select(key =>
            $"m.{QuoteIdentifier(key)} IS f.{QuoteIdentifier(key)}"));

        var targetProjection = string.Join(", ", allColumns.Select(column =>
        {
            var inTarget = targetColumnNames.Contains(column);
            var inFallback = fallbackColumnNames.Contains(column);
            var alias = QuoteIdentifier(column);

            if (inTarget && inFallback && !keys.Contains(column, StringComparer.OrdinalIgnoreCase))
                return $"COALESCE(m.{alias}, f.{alias}) AS {alias}";
            if (inTarget)
                return $"m.{alias} AS {alias}";
            return $"f.{alias} AS {alias}";
        }));

        var fallbackProjection = string.Join(", ", allColumns.Select(column =>
        {
            var alias = QuoteIdentifier(column);
            return fallbackColumnNames.Contains(column)
                ? $"f.{alias} AS {alias}"
                : $"NULL AS {alias}";
        }));

        var sql = new StringBuilder();
        sql.Append("CREATE TEMP VIEW ").Append(QuoteIdentifier(table)).Append(" AS ")
            .Append("SELECT ").Append(targetProjection)
            .Append(" FROM ").Append(QuoteIdentifier(TargetSchema)).Append('.').Append(QuoteIdentifier(table)).Append(" AS m ")
            .Append("LEFT JOIN ").Append(QuoteIdentifier(FallbackSchema)).Append('.').Append(QuoteIdentifier(table)).Append(" AS f ON ")
            .Append(join)
            .Append(" UNION ALL SELECT ").Append(fallbackProjection)
            .Append(" FROM ").Append(QuoteIdentifier(FallbackSchema)).Append('.').Append(QuoteIdentifier(table)).Append(" AS f ")
            .Append("WHERE NOT EXISTS (SELECT 1 FROM ")
            .Append(QuoteIdentifier(TargetSchema)).Append('.').Append(QuoteIdentifier(table)).Append(" AS m WHERE ")
            .Append(join).Append(')');

        using var command = connection.CreateCommand();
        command.CommandText = sql.ToString();
        command.ExecuteNonQuery();
    }

    private static void CreateUnionView(
        SqliteConnection connection,
        string table,
        IReadOnlyList<string> allColumns,
        ISet<string> targetColumns,
        ISet<string> fallbackColumns)
    {
        static string Projection(
            IReadOnlyList<string> columns,
            ISet<string> available,
            ISet<string> authoritativeColumns,
            string alias)
        {
            return string.Join(", ", columns.Select(column =>
            {
                var quoted = QuoteIdentifier(column);
                return available.Contains(column) && authoritativeColumns.Contains(column)
                    ? $"{alias}.{quoted} AS {quoted}"
                    : $"NULL AS {quoted}";
            }));
        }

        var sharedColumns = new HashSet<string>(
            targetColumns.Where(fallbackColumns.Contains),
            StringComparer.OrdinalIgnoreCase);

        // With no trustworthy row key, merge by exact target-schema row equality.
        // Fallback-only columns are intentionally NULL in this case: using their values
        // would make otherwise identical rows different and double-apply modifiers.
        // When schemas share nothing, preserve both sources with UNION ALL instead.
        var setOperator = sharedColumns.Count > 0 ? "UNION" : "UNION ALL";
        var targetProjection = Projection(allColumns, targetColumns, targetColumns, "m");
        var fallbackProjection = sharedColumns.Count > 0
            ? Projection(allColumns, fallbackColumns, sharedColumns, "f")
            : Projection(allColumns, fallbackColumns, fallbackColumns, "f");

        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TEMP VIEW {QuoteIdentifier(table)} AS " +
                              $"SELECT {targetProjection} " +
                              $"FROM {QuoteIdentifier(TargetSchema)}.{QuoteIdentifier(table)} AS m " +
                              setOperator + " " +
                              $"SELECT {fallbackProjection} " +
                              $"FROM {QuoteIdentifier(FallbackSchema)}.{QuoteIdentifier(table)} AS f";
        command.ExecuteNonQuery();
    }

    private static List<string> SelectIdentityColumns(
        IReadOnlyList<ColumnInfo> targetColumns,
        IReadOnlyList<ColumnInfo> fallbackColumns)
    {
        var targetNames = new HashSet<string>(targetColumns.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
        var fallbackNames = new HashSet<string>(fallbackColumns.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);

        var declaredTargetPrimaryKey = targetColumns
            .Where(x => x.PrimaryKeyOrder > 0)
            .OrderBy(x => x.PrimaryKeyOrder)
            .Select(x => x.Name)
            .ToList();
        if (declaredTargetPrimaryKey.Count > 0
            && declaredTargetPrimaryKey.All(fallbackNames.Contains))
            return declaredTargetPrimaryKey;

        var declaredFallbackPrimaryKey = fallbackColumns
            .Where(x => x.PrimaryKeyOrder > 0)
            .OrderBy(x => x.PrimaryKeyOrder)
            .Select(x => x.Name)
            .ToList();
        if (declaredFallbackPrimaryKey.Count > 0
            && declaredFallbackPrimaryKey.All(targetNames.Contains))
            return declaredFallbackPrimaryKey;

        if (targetNames.Contains("id") && fallbackNames.Contains("id"))
            return new List<string> { "id" };

        // Do not invent a compound identity from every *_id column. Tables such as
        // unit_modifiers and skill_modifiers legitimately contain several rows with
        // the same owner/attribute tuple. Treating that tuple as a key creates an
        // expensive many-to-many join and duplicates modifiers during startup.
        return new List<string>();
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
