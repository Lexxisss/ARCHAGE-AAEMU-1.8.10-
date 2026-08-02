using System;
using System.Collections.Generic;
using System.IO;

using AAEmu.Commons.IO;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.Utils.DB;

/// <summary>
/// Dedicated read-only data source for the complete quest system.
/// The primary database below is authoritative. The fallback is consulted only
/// when the primary does not contain the requested table at all; rows are never
/// merged between the two. This connector is intentionally independent from
/// DoodadSQLite and from the legacy global SQLite connector.
/// </summary>
/// <remarks>
/// Quests read from the base database at the moment, with the target client's own database behind
/// it. That is the reverse of every other subsystem here and it is meant to be temporary - the two
/// names below are the whole of it, so swapping them back is the entire change.
///
/// Which one is opened decides far more than the two lookups that name it: it becomes SQLite's
/// <c>main</c>, and the sixty-odd quest queries that name their tables plainly resolve there first
/// and reach the other only for a table the first has never heard of.
/// </remarks>
public static class QuestSQLite
{
    public const string PrimaryDatabaseFile = "base.sqlite3";
    public const string FallbackDatabaseFile = "1.8.1.0-Kakao-KR.sqlite";
    public const string FallbackAlias = "quest_fallback";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly object SchemaLock = new();
    private static HashSet<string> _primaryTables;
    private static HashSet<string> _fallbackTables;

    public static string PrimaryDatabasePath => Path.Combine(FileManager.AppPath, "Data", PrimaryDatabaseFile);
    public static string FallbackDatabasePath => Path.Combine(FileManager.AppPath, "Data", FallbackDatabaseFile);

    public static SqliteConnection CreateConnection()
    {
        if (!File.Exists(PrimaryDatabasePath))
            throw new FileNotFoundException("Quest primary database does not exist", PrimaryDatabasePath);
        if (!File.Exists(FallbackDatabasePath))
            throw new FileNotFoundException("Quest fallback database does not exist", FallbackDatabasePath);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = PrimaryDatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"ATTACH DATABASE $fallback AS {FallbackAlias}";
            command.Parameters.AddWithValue("$fallback", FallbackDatabasePath);
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA main.query_only = ON; PRAGMA foreign_keys = OFF;";
            command.ExecuteNonQuery();
        }

        EnsureSchemaCache(connection);
        return connection;
    }

    public static bool PrimaryTableExists(SqliteConnection connection, string table)
    {
        EnsureSchemaCache(connection);
        lock (SchemaLock)
            return _primaryTables.Contains(table);
    }

    public static bool FallbackTableExists(SqliteConnection connection, string table)
    {
        EnsureSchemaCache(connection);
        lock (SchemaLock)
            return _fallbackTables.Contains(table);
    }

    public static bool TableExists(SqliteConnection connection, string table) =>
        PrimaryTableExists(connection, table) || FallbackTableExists(connection, table);

    /// <summary>Resolves one complete table. Rows are never merged between databases.</summary>
    public static string ResolveTable(SqliteConnection connection, string table)
    {
        if (string.IsNullOrWhiteSpace(table))
            throw new ArgumentException("Table name is empty", nameof(table));

        var escaped = QuoteIdentifier(table);
        if (PrimaryTableExists(connection, table))
            return $"main.{escaped}";
        if (FallbackTableExists(connection, table))
            return $"{FallbackAlias}.{escaped}";

        throw new InvalidOperationException($"Quest table '{table}' is absent in both quest databases");
    }

    public static bool ColumnExists(SqliteConnection connection, string table, string column)
    {
        if (!TableExists(connection, table) || string.IsNullOrWhiteSpace(column))
            return false;

        var schema = PrimaryTableExists(connection, table) ? "main" : FallbackAlias;
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {schema}.table_info({QuoteIdentifier(table)})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static void EnsureSchemaCache(SqliteConnection connection)
    {
        if (_primaryTables != null && _fallbackTables != null)
            return;

        lock (SchemaLock)
        {
            if (_primaryTables != null && _fallbackTables != null)
                return;

            _primaryTables = ReadTables(connection, "main");
            _fallbackTables = ReadTables(connection, FallbackAlias);
            Logger.Info(
                "Quest SQLite initialized: reading from {0} ({1} tables), falling back to {2} ({3} tables)",
                PrimaryDatabaseFile,
                _primaryTables.Count,
                FallbackDatabaseFile,
                _fallbackTables.Count);
        }
    }

    private static HashSet<string> ReadTables(SqliteConnection connection, string schema)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM {schema}.sqlite_master WHERE type IN ('table','view')";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }
}
