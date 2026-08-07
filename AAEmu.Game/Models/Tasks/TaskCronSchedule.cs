using System;
using System.Globalization;

namespace AAEmu.Game.Models.Tasks;

/// <summary>
/// The little of cron the game actually writes: seconds, minutes, hours, day of month, month,
/// day of week and year, in that order, with <c>*</c> or <c>?</c> for "any".
/// </summary>
/// <remarks>
/// The scheduler used to carry Quartz for this and nothing else. Four shapes are produced in
/// total - every day at midnight, every day at a time, one date a year, and one weekday - and all
/// of them use plain numbers. Ranges, steps and lists are refused rather than half-understood: a
/// schedule that is silently misread is worse than one that says it cannot be read.
/// </remarks>
public sealed class TaskCronSchedule
{
    private const int AnyValue = -1;

    private readonly int _second;
    private readonly int _minute;
    private readonly int _hour;
    private readonly int _dayOfMonth;
    private readonly int _month;
    private readonly int _dayOfWeek;

    public string Expression { get; }

    private TaskCronSchedule(string expression, int second, int minute, int hour, int dayOfMonth, int month, int dayOfWeek)
    {
        Expression = expression;
        _second = second;
        _minute = minute;
        _hour = hour;
        _dayOfMonth = dayOfMonth;
        _month = month;
        _dayOfWeek = dayOfWeek;
    }

    public static bool TryParse(string expression, out TaskCronSchedule schedule)
    {
        schedule = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        var fields = expression.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is < 6 or > 7)
            return false;

        if (!TryField(fields[0], 0, 59, out var second) ||
            !TryField(fields[1], 0, 59, out var minute) ||
            !TryField(fields[2], 0, 23, out var hour) ||
            !TryField(fields[3], 1, 31, out var dayOfMonth) ||
            !TryField(fields[4], 1, 12, out var month) ||
            !TryField(fields[5], 0, 7, out var dayOfWeek))
        {
            return false;
        }

        // The year field, when present, is only ever "*" in the data. Anything narrower would
        // need real handling, so refuse it instead of ignoring it.
        if (fields.Length == 7 && fields[6] != "*" && fields[6] != "?")
            return false;

        // A time with no date part at all would match nothing.
        if (second == AnyValue || minute == AnyValue || hour == AnyValue)
            return false;

        if (dayOfWeek == 7)
            dayOfWeek = 0; // both spellings of Sunday

        schedule = new TaskCronSchedule(expression, second, minute, hour, dayOfMonth, month, dayOfWeek);
        return true;
    }

    private static bool TryField(string field, int min, int max, out int value)
    {
        value = AnyValue;
        if (field is "*" or "?")
            return true;

        if (!int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            return false;

        if (parsed < min || parsed > max)
            return false;

        value = parsed;
        return true;
    }

    /// <summary>The first moment strictly after <paramref name="after"/> that this matches.</summary>
    /// <returns>Null when nothing matches within four years, which means the expression is impossible.</returns>
    public DateTime? GetNextOccurrence(DateTime after)
    {
        var candidate = new DateTime(after.Year, after.Month, after.Day, _hour, _minute, _second, DateTimeKind.Utc);
        if (candidate <= after)
            candidate = candidate.AddDays(1);

        // Four years so that a 29 February date is reachable.
        var limit = after.AddYears(4);
        while (candidate <= limit)
        {
            if (Matches(candidate))
                return candidate;

            candidate = candidate.AddDays(1);
        }

        return null;
    }

    private bool Matches(DateTime moment)
    {
        if (_month != AnyValue && moment.Month != _month)
            return false;

        if (_dayOfMonth != AnyValue && moment.Day != _dayOfMonth)
            return false;

        if (_dayOfWeek != AnyValue && (int)moment.DayOfWeek != _dayOfWeek)
            return false;

        return true;
    }
}
