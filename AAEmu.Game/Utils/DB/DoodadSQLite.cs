using System;
using System.Collections.Generic;
using System.IO;

using AAEmu.Commons.IO;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.Utils.DB;

/// <summary>
/// Dedicated, read-only connection for the ArcheAge 10.8 doodad database.
/// Doodad templates, function groups, functions, phase functions and every
/// supported detail table are loaded exclusively from Data/base.sqlite3.
/// This connection is intentionally independent from the server's global
/// SQLite/MySQL data sources.
/// </summary>
public static class DoodadSQLite
{
    public const string DatabaseFile = "base.sqlite3";

    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly object SchemaLock = new();
    private static HashSet<string> _tables;

    public static string DatabasePath => Path.Combine(FileManager.AppPath, "Data", DatabaseFile);

    public static SqliteConnection CreateConnection()
    {
        var path = DatabasePath;
        if (!File.Exists(path))
        {
            Logger.Fatal("Doodad database does not exist: {0}", path);
            throw new FileNotFoundException("Doodad database does not exist", path);
        }

        // Use the builder instead of a file: URI so Windows drive letters and
        // spaces in the server path are handled correctly. This connector is
        // fully independent from the global game-data connections.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA query_only = ON; PRAGMA foreign_keys = OFF;";
            command.ExecuteNonQuery();
        }

        EnsureSchemaCache(connection);
        return connection;
    }

    public static bool TableExists(SqliteConnection connection, string table)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (string.IsNullOrWhiteSpace(table))
            return false;

        EnsureSchemaCache(connection);
        lock (SchemaLock)
            return _tables.Contains(table);
    }

    public static bool ColumnExists(SqliteConnection connection, string table, string column)
    {
        if (!TableExists(connection, table) || string.IsNullOrWhiteSpace(column))
            return false;

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void EnsureSchemaCache(SqliteConnection connection)
    {
        if (_tables != null)
            return;

        lock (SchemaLock)
        {
            if (_tables != null)
                return;

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type IN ('table','view')";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                tables.Add(reader.GetString(0));

            _tables = tables;
            Logger.Info("Doodad SQLite source initialized: {0} tables from {1}", tables.Count, DatabasePath);
        }
    }
}
