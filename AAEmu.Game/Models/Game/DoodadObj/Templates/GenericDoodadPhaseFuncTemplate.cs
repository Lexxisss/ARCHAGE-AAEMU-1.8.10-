using System.Collections.Concurrent;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Templates;

/// <summary>
/// Non-destructive compatibility implementation for a target 10.8 phase
/// function whose exact server behavior is not yet implemented. The record is
/// still loaded from Data/base.sqlite3 and remains visible in diagnostics, but
/// it does not invent a phase change or deny an interaction.
/// </summary>
public sealed class GenericDoodadPhaseFuncTemplate : DoodadPhaseFuncTemplate
{
    /// <summary>Types already announced, so the gap is reported once rather than per doodad.</summary>
    private static readonly ConcurrentDictionary<string, bool> Announced = new();

    public string ActualFuncType { get; init; } = string.Empty;

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        // What is worth knowing is which types are missing, and that is one line each. Saying it
        // again for every doodad that reaches one buried the rest of the log: a single phase
        // function used by 250 doodads announced itself 250 times during the spawn pass.
        if (Announced.TryAdd(ActualFuncType, true))
        {
            Logger.Warn(
                "Target 10.8 generic doodad phase fallback: type={0} is not implemented; " +
                "first seen on doodad {1} (template {2}), func id {3}. Further uses log at trace.",
                ActualFuncType,
                owner?.ObjId ?? 0,
                owner?.TemplateId ?? 0,
                Id);
        }
        else
        {
            Logger.Trace(
                "Target 10.8 generic doodad phase fallback: type={0}, id={1}, doodad={2}, template={3}",
                ActualFuncType,
                Id,
                owner?.ObjId ?? 0,
                owner?.TemplateId ?? 0);
        }

        return false;
    }
}
