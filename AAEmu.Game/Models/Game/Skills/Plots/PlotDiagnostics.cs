using System.Collections.Concurrent;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots;

/// <summary>
/// Centralised, de-duplicated diagnostics for data-driven plot content.
/// Unknown data must never disappear silently: one precise warning is emitted per
/// unique descriptor while the caller decides whether to fail closed or continue.
/// </summary>
public static class PlotDiagnostics
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private static readonly ConcurrentDictionary<string, byte> Seen = new();

    public static void MissingEffect(string actualType, uint actualId, uint plotId, uint eventId, uint skillId)
    {
        WarnOnce(
            $"effect:{actualType}:{actualId}:{plotId}:{eventId}:{skillId}",
            "Plot effect template missing: type={0}, actualId={1}, plot={2}, event={3}, skill={4}",
            actualType,
            actualId,
            plotId,
            eventId,
            skillId);
    }

    public static void UnsupportedCondition(int kindId, uint conditionId, int p1, int p2, int p3, int p4)
    {
        WarnOnce(
            $"condition:{kindId}:{conditionId}:{p1}:{p2}:{p3}:{p4}",
            "Unsupported plot condition rejected (fail-closed): condition={0}, kind={1}, params=[{2},{3},{4},{5}]",
            conditionId,
            kindId,
            p1,
            p2,
            p3,
            p4);
    }

    public static void UnsupportedController(uint templateId, uint kindId)
    {
        WarnOnce(
            $"controller:{templateId}:{kindId}",
            "Unsupported skill controller: template={0}, kind={1}",
            templateId,
            kindId);
    }

    public static void UnsupportedUnitRequirement(int kindId, int value1, int value2, int value3)
    {
        WarnOnce(
            $"unit-req:{kindId}:{value1}:{value2}:{value3}",
            "Unsupported PlotCondition unit requirement rejected (fail-closed): kind={0}, values=[{1},{2},{3}]",
            kindId,
            value1,
            value2,
            value3);
    }

    private static void WarnOnce(string key, string message, params object[] args)
    {
        if (Seen.TryAdd(key, 0))
            Logger.Warn(message, args);
    }
}
