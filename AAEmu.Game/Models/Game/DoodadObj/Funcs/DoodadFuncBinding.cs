using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Sets recall point of caster
/// </summary>
public class DoodadFuncBinding : DoodadFuncTemplate
{
    public uint DistrictId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character) { return; }

        var returnPointId = PortalManager.Instance.GetDistrictReturnPoint(DistrictId, character.Faction.Id);
        if (returnPointId == 0)
        {
            Logger.Warn("DoodadFuncBinding: no return point for district={0}, faction={1}",
                DistrictId, character.Faction.Id);
            return;
        }

        var portal = PortalManager.Instance.GetRecallById(returnPointId);
        if (portal == null)
        {
            Logger.Warn("DoodadFuncBinding: recall point {0} for district {1} was not loaded",
                returnPointId, DistrictId);
            return;
        }

        // One character has exactly one active Recall destination. Assigning another Memory Tome
        // overwrites this field and the same row is persisted immediately.
        character.ReturnDistrictId = DistrictId;
        character.Portals.Send();
        if (!SaveManager.Instance.SaveCharacter(character, "set Recall district"))
            Logger.Error("DoodadFuncBinding: failed to persist Recall district for character={0}", character.Id);
        else
            Logger.Info("Recall point persisted: character={0}, district={1}, returnPoint={2}",
                character.Id, DistrictId, returnPointId);

        owner.ToNextPhase = true;
    }
}
