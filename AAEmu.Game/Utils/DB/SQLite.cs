using System;
using System.Collections.Generic;
using System.IO;

using AAEmu.Commons.IO;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.Utils.DB;

public static class SQLite
{
    public const string TargetClientDatabase = "1.8.1.0-Kakao-KR.sqlite";
    public const string FallbackClientDatabase = "base.sqlite3";

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Opens an immutable SQLite database, the target client's own unless told otherwise.
    /// </summary>
    /// <remarks>
    /// It used to be an older client's database that answered when nobody said which, and a
    /// loader that forgot to choose quietly read data for a game this server does not serve.
    /// Forgetting now lands on the right one.
    /// </remarks>
    public static SqliteConnection CreateConnection(string directory = "Data", string sqlite = TargetClientDatabase)
    {
        var dbPath = Path.Combine(FileManager.AppPath, directory, sqlite);
        if (!File.Exists(dbPath))
        {
            Logger.Fatal("Server database does not exist: {0} !", dbPath);
            throw new FileNotFoundException("Server database does not exist: " + dbPath);
        }
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();

            // Protect main and every subsequently attached database. ATTACH itself is
            // still allowed while INSERT/UPDATE/DELETE and schema changes are rejected.
            using (var queryOnly = connection.CreateCommand())
            {
                queryOnly.CommandText = "PRAGMA query_only = ON";
                queryOnly.ExecuteNonQuery();
            }

            // The helper client DB is intentionally attached after main. SQLite resolves
            // unqualified table names in main first and only falls back when the target DB
            // does not contain that table. Both client databases stay read-only.
            if (string.Equals(sqlite, TargetClientDatabase, StringComparison.OrdinalIgnoreCase))
            {
                var fallbackPath = Path.Combine(FileManager.AppPath, directory, FallbackClientDatabase);
                if (!File.Exists(fallbackPath))
                    throw new FileNotFoundException("Fallback client database does not exist: " + fallbackPath);

                // Microsoft.Data.Sqlite pooling can return a physical connection on which
                // this alias is still attached. ATTACH is connection-scoped, so make it
                // idempotent instead of failing the next target-client loader.
                var fallbackAttached = false;
                using (var databaseList = connection.CreateCommand())
                {
                    databaseList.CommandText = "PRAGMA database_list";
                    using var reader = databaseList.ExecuteReader();
                    while (reader.Read())
                    {
                        if (string.Equals(reader.GetString(1), "client_fallback", StringComparison.Ordinal))
                        {
                            fallbackAttached = true;
                            break;
                        }
                    }
                }

                if (!fallbackAttached)
                {
                    using var attach = connection.CreateCommand();
                    attach.CommandText = "ATTACH DATABASE $fallbackPath AS client_fallback";
                    attach.Parameters.Add("$fallbackPath", SqliteType.Text).Value = Path.GetFullPath(fallbackPath);
                    attach.ExecuteNonQuery();
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error on SQLite connect: {0}", e.Message);
            throw;
        }

        return connection;
    }

    public static SqliteConnection CreateTargetClientConnection()
    {
        return CreateConnection("Data", TargetClientDatabase);
    }

    public static SqliteConnection CreateFallbackClientConnection()
    {
        return CreateConnection("Data", FallbackClientDatabase);
    }

    /// <summary>
    /// Opens the isolated, read-only virtual catalogue used by the skill system.
    /// Target client data wins; fallback data only fills missing tables, rows, or NULL columns.
    /// </summary>
    public static SqliteConnection CreateSkillConnection()
    {
        var dataDirectory = Path.Combine(FileManager.AppPath, "Data");
        return SkillSQLiteCatalog.Create(
            Path.Combine(dataDirectory, TargetClientDatabase),
            Path.Combine(dataDirectory, FallbackClientDatabase));
    }

    /// <summary>
    /// The server-side supplement tables. These now come from the fallback client database.
    /// </summary>
    /// <remarks>
    /// Of the sixty-five tables the old supplement carried, fifty-five are in the fallback with
    /// the same columns; of the ten that are not, this server reads exactly one - and that one
    /// asks whether it is there first. Two columns differ, and the reader returns a default for
    /// a column it cannot find rather than failing.
    /// </remarks>
    public static SqliteConnection CreateServerConnection()
    {
        return CreateConnection("Data", FallbackClientDatabase);
    }

    /// <summary>
    /// Whether a table exists in any database open on this connection.
    /// </summary>
    /// <remarks>
    /// The client this server targets does not carry every table an older build had, and a
    /// loader written against the older one has to be able to ask before it reads. Every
    /// attached database is consulted, so a table the target lacks is still found in the
    /// fallback the target connection attaches beside it.
    /// </remarks>
    public static bool TableExists(SqliteConnection connection, string table)
    {
        if (connection == null || string.IsNullOrWhiteSpace(table))
            return false;

        var schemas = new List<string>();
        using (var databaseList = connection.CreateCommand())
        {
            databaseList.CommandText = "PRAGMA database_list";
            using var reader = databaseList.ExecuteReader();
            while (reader.Read())
                schemas.Add(reader.GetString(1));
        }

        foreach (var schema in schemas)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT 1 FROM \"{schema.Replace("\"", "\"\"")}\".sqlite_master " +
                "WHERE type IN ('table','view') AND name = $name LIMIT 1";
            command.Parameters.Add("$name", SqliteType.Text).Value = table;
            if (command.ExecuteScalar() != null)
                return true;
        }

        return false;
    }
}
