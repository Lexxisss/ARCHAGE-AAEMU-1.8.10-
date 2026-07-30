using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Target 10.8 doodad_func_private_coffers implementation loaded from
/// Data/base.sqlite3. Unlike the phase-level DoodadFuncCoffer, this is a normal
/// selectable world interaction.
/// </summary>
public sealed class DoodadFuncPrivateCoffer : DoodadFuncTemplate
{
    public int Capacity { get; init; }
    public bool IsManikin { get; init; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character || owner is not DoodadCoffer coffer)
            return;

        Logger.Debug(
            "DoodadFuncPrivateCoffer: doodad={0}, capacity={1}, isManikin={2}, skill={3}",
            owner.ObjId,
            Capacity,
            IsManikin,
            skillId);

        owner.ToNextPhase = false;
        if (coffer.OpenedBy?.Id == character.Id)
            DoodadManager.CloseCofferDoodad(character, owner.ObjId);
        else
            DoodadManager.OpenCofferDoodad(character, owner.ObjId);
    }
}
