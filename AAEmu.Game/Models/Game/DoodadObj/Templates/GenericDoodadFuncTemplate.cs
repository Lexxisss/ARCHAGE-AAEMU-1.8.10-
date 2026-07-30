using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Templates;

/// <summary>
/// Safe compatibility implementation for a target 10.8 doodad function whose
/// detail table is present in Data/base.sqlite3 but whose dedicated server-side
/// behavior has not yet been implemented. It never deletes a doodad and only
/// permits a positive next-phase transition already declared by doodad_funcs.
/// Client-side UI functions can therefore complete their interaction handshake
/// without pulling data from an older database or silently using a 5.x class.
/// </summary>
public sealed class GenericDoodadFuncTemplate : DoodadFuncTemplate
{
    public string ActualFuncType { get; init; } = string.Empty;

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Warn(
            "Target 10.8 generic doodad function fallback: type={0}, id={1}, doodad={2}, template={3}, skill={4}, nextPhase={5}",
            ActualFuncType,
            Id,
            owner?.ObjId ?? 0,
            owner?.TemplateId ?? 0,
            skillId,
            nextPhase);

        // -1 commonly means finish/delete in older implementations. Never
        // perform that destructive action from an inferred fallback.
        if (owner != null && nextPhase > 0)
            owner.ToNextPhase = true;
    }
}
