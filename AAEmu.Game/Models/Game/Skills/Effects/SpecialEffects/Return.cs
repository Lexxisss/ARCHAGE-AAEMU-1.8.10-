using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>Recall/worldgate return through the same authoritative teleport path as resurrection.</summary>
public class Return : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Character character)
            return;

        Portal portal;
        if (value1 == 0)
        {
            var returnPointId = PortalManager.Instance.GetDistrictReturnPoint(
                character.ReturnDistrictId, character.Faction.Id);
            portal = returnPointId == 0 ? null : PortalManager.Instance.GetRecallById(returnPointId);
        }
        else
        {
            portal = PortalManager.Instance.GetWorldgatesById((uint)value1);
        }

        if (portal == null && value1 != 0 && character.MainWorldPosition != null)
        {
            var mainWorldDestination = character.MainWorldPosition.CloneAsSpawnPosition();
            Logger.Info(
                "Return: worldgate {0} not mapped; returning character={1} to saved main-world position",
                value1,
                character.Id);
            CharacterTeleportManager.Teleport(
                character,
                mainWorldDestination,
                TeleportReason.MoveToLocation,
                character.MainWorldPosition.InstanceId);
            return;
        }

        if (portal == null)
        {
            Logger.Warn("Return: destination not found, character={0}, value1={1}, district={2}, faction={3}",
                character.Id, value1, character.ReturnDistrictId, character.Faction.Id);
            // Re-send the selected recall point. A client without this state keeps Recall disabled.
            character.Portals.Send();
            return;
        }

        var destination = CharacterTeleportManager.FromPortal(portal, character.Transform.WorldId);
        Logger.Info("Return: character={0}, portal={1}, destination=({2:F1},{3:F1},{4:F1})",
            character.Id, portal.Id, destination.X, destination.Y, destination.Z);
        CharacterTeleportManager.Teleport(character, destination, TeleportReason.MoveToLocation);
    }
}
