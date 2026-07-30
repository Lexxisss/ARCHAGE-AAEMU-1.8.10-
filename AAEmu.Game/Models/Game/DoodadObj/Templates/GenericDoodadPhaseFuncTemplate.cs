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
    public string ActualFuncType { get; init; } = string.Empty;

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Warn(
            "Target 10.8 generic doodad phase fallback: type={0}, id={1}, doodad={2}, template={3}",
            ActualFuncType,
            Id,
            owner?.ObjId ?? 0,
            owner?.TemplateId ?? 0);

        return false;
    }
}
