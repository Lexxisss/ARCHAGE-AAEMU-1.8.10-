using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>Escape skill: move the character to the nearest Nui/respawn point.</summary>
public class Escape : SpecialEffectAction
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

        var portal = PortalManager.Instance.GetClosestReturnPortal(character);
        var destination = CharacterTeleportManager.FromPortal(portal, character.Transform.WorldId);
        if (destination == null)
        {
            Logger.Warn("Escape: no respawn point found for character={0}, world={1}, zone={2}",
                character.Id, character.Transform.WorldId, character.Transform.ZoneId);
            return;
        }

        Logger.Info("Escape: character={0}, portal={1}, destination=({2:F1},{3:F1},{4:F1})",
            character.Id, portal.Id, destination.X, destination.Y, destination.Z);

        if (!CharacterTeleportManager.Teleport(character, destination, TeleportReason.MoveToLocation))
            return;

        // The target DB description specifies a 30-minute cooldown after a
        // successful Escape. cooldown_time is zero because this is owned by the
        // server-side special action rather than the generic skill row.
        character.Cooldowns.AddCooldown(skill.Template.Id, 30u * 60u * 1000u);
    }
}
