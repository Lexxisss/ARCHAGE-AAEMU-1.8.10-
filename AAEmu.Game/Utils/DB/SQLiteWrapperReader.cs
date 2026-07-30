using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.Utils.DB;

/// <summary>
/// Thin SQLite reader wrapper used by the game-data loaders.
///
/// SQLite databases used by different ArcheAge client versions contain many
/// nullable columns. All non-nullable getters therefore return a predictable
/// default value when the database value is NULL instead of allowing
/// Microsoft.Data.Sqlite to throw InvalidOperationException.
///
/// Callers that need to distinguish NULL from a real zero/false/empty value
/// must call IsDBNull(column) before reading it.
/// </summary>
public sealed class SQLiteWrapperReader : IDisposable
{
    private readonly SqliteDataReader _reader;
    private readonly Dictionary<string, int> _ordinal;

    public SQLiteWrapperReader(SqliteDataReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _ordinal = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    public bool Read() => _reader.Read();

    public object GetValue(string column)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal) ? DBNull.Value : _reader.GetValue(ordinal);
    }

    public bool GetBoolean(string column)
    {
        var ordinal = GetOrdinal(column);
        return !IsDBNull(ordinal) && _reader.GetBoolean(ordinal);
    }

    /// <summary>
    /// Reads a boolean. When <paramref name="fromString"/> is true, values
    /// "t", "true" and "1" are treated as true. NULL always becomes false.
    /// </summary>
    public bool GetBoolean(string column, bool fromString)
    {
        if (!fromString)
            return GetBoolean(column);

        var ordinal = GetOrdinal(column);
        if (IsDBNull(ordinal))
            return false;

        var value = Convert.ToString(_reader.GetValue(ordinal))?.Trim();
        return string.Equals(value, "t", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || value == "1";
    }

    public byte GetByte(string column) => GetByte(column, 0);

    public byte GetByte(string column, byte defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (IsDBNull(ordinal))
            return defaultValue;

        return unchecked((byte)_reader.GetInt64(ordinal));
    }

    public long GetBytes(string column, long fieldOffset, byte[] buffer, int bufferOffset, int length)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal)
            ? 0
            : _reader.GetBytes(ordinal, fieldOffset, buffer, bufferOffset, length);
    }

    public char GetChar(string column) => GetChar(column, '\0');

    public char GetChar(string column, char defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal) ? defaultValue : _reader.GetChar(ordinal);
    }

    public long GetChars(string column, long fieldOffset, char[] buffer, int bufferOffset, int length)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal)
            ? 0
            : _reader.GetChars(ordinal, fieldOffset, buffer, bufferOffset, length);
    }

    public Guid GetGuid(string column) => GetGuid(column, Guid.Empty);

    public Guid GetGuid(string column, Guid defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal) ? defaultValue : _reader.GetGuid(ordinal);
    }

    public short GetInt16(string column) => GetInt16(column, 0);

    public short GetInt16(string column, short defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal)
            ? defaultValue
            : unchecked((short)_reader.GetInt64(ordinal));
    }

    public ushort GetUInt16(string column) => GetUInt16(column, 0);

    public ushort GetUInt16(string column, ushort defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal)
            ? defaultValue
            : unchecked((ushort)_reader.GetInt64(ordinal));
    }

    public int GetInt32(string column) => GetInt32(column, 0);

    public int GetInt32(string column, int defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (IsDBNull(ordinal))
            return defaultValue;

        // Same integer conversion behavior as the old Sqlite.Core wrapper.
        return unchecked((int)_reader.GetInt64(ordinal));
    }

    public uint GetUInt32(string column) => GetUInt32(column, 0);

    public uint GetUInt32(string column, uint defaultValue)
    {
        var ordinal = GetOrdinal(column);
        if (IsDBNull(ordinal))
            return defaultValue;

        return unchecked((uint)_reader.GetInt64(ordinal));
    }

    public long GetInt64(string column) => GetInt64(column, 0L);

    public long GetInt64(string column, long defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal) ? defaultValue : _reader.GetInt64(ordinal);
    }

    public ulong GetUInt64(string column) => GetUInt64(column, 0UL);

    public ulong GetUInt64(string column, ulong defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal)
            ? defaultValue
            : unchecked((ulong)_reader.GetInt64(ordinal));
    }

    public float GetFloat(string column) => GetFloat(column, 0f);

    public float GetFloat(string column, float defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal) ? defaultValue : _reader.GetFloat(ordinal);
    }

    public double GetDouble(string column) => GetDouble(column, 0d);

    public double GetDouble(string column, double defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal) ? defaultValue : _reader.GetDouble(ordinal);
    }

    public string GetString(string column) => GetString(column, string.Empty);

    public string GetString(string column, string defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal) ? defaultValue : _reader.GetString(ordinal);
    }

    public decimal GetDecimal(string column) => GetDecimal(column, 0m);

    public decimal GetDecimal(string column, decimal defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal) ? defaultValue : _reader.GetDecimal(ordinal);
    }

    public DateTime GetDateTime(string column) => GetDateTime(column, DateTime.MinValue);

    public DateTime GetDateTime(string column, DateTime defaultValue)
    {
        var ordinal = GetOrdinal(column);
        return IsDBNull(ordinal) ? defaultValue : _reader.GetDateTime(ordinal);
    }

    public bool IsDBNull(string column)
    {
        return IsDBNull(GetOrdinal(column));
    }

    public int GetOrdinal(string column)
    {
        if (_ordinal.TryGetValue(column, out var cachedOrdinal))
            return cachedOrdinal;

        int ordinal = -1;
        try
        {
            ordinal = _reader.GetOrdinal(column);
        }
        catch (ArgumentOutOfRangeException)
        {
        }
        _ordinal.Add(column, ordinal);
        return ordinal;
    }

    private bool IsDBNull(int ordinal)
    {
        return ordinal == -1 || _reader.IsDBNull(ordinal);
    }

    public void Dispose()
    {
        _ordinal.Clear();
        _reader.Dispose();
    }
}
