using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// NULL-safe data-driven representation of one row from a quest_act_* table.
/// </summary>
public sealed class QuestActDefinition
{
    private static readonly Regex WordBoundary = new("([a-z0-9])([A-Z])", RegexOptions.Compiled);
    private static readonly Regex AcronymBoundary = new("(.)([A-Z][a-z]+)", RegexOptions.Compiled);
    private readonly Dictionary<string, object> _values;

    public uint Id { get; }
    public string DetailType { get; }
    public string TableName { get; }
    public IReadOnlyDictionary<string, object> Values => _values;

    public QuestActDefinition(uint id, string detailType, string tableName, Dictionary<string, object> values)
    {
        Id = id;
        DetailType = detailType ?? string.Empty;
        TableName = tableName ?? string.Empty;
        _values = values ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    public bool Has(string name) => _values.ContainsKey(name);
    public object Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    public string GetString(string name, string defaultValue = "")
    {
        var value = Get(name);
        return value == null ? defaultValue : Convert.ToString(value, CultureInfo.InvariantCulture) ?? defaultValue;
    }

    public int GetInt32(string name, int defaultValue = 0)
    {
        var value = Get(name);
        if (value == null)
            return defaultValue;
        try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
        catch { return defaultValue; }
    }

    public uint GetUInt32(string name, uint defaultValue = 0)
    {
        var value = Get(name);
        if (value == null)
            return defaultValue;
        try
        {
            var number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return number <= 0 ? (number == 0 ? 0u : defaultValue) : checked((uint)number);
        }
        catch { return defaultValue; }
    }

    public long GetInt64(string name, long defaultValue = 0)
    {
        var value = Get(name);
        if (value == null)
            return defaultValue;
        try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
        catch { return defaultValue; }
    }

    public bool GetBoolean(string name, bool defaultValue = false)
    {
        var value = Get(name);
        if (value == null)
            return defaultValue;
        if (value is bool boolean)
            return boolean;
        if (value is string text && bool.TryParse(text, out var parsed))
            return parsed;
        try { return Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0; }
        catch { return defaultValue; }
    }

    public static string GetTableName(string detailType)
    {
        if (string.IsNullOrWhiteSpace(detailType))
            return string.Empty;

        var snake = AcronymBoundary.Replace(detailType, "$1_$2");
        snake = WordBoundary.Replace(snake, "$1_$2").ToLowerInvariant();

        if (snake.EndsWith("ability", StringComparison.Ordinal))
            return snake[..^1] + "ies";
        if (snake.EndsWith("y", StringComparison.Ordinal))
            return snake[..^1] + "ies";
        if (snake.EndsWith("s", StringComparison.Ordinal) ||
            snake.EndsWith("x", StringComparison.Ordinal) ||
            snake.EndsWith("ch", StringComparison.Ordinal) ||
            snake.EndsWith("sh", StringComparison.Ordinal))
            return snake + "es";
        return snake + "s";
    }
}
