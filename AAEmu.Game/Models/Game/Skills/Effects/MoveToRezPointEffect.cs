using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>Effect-template counterpart of Escape/MoveToRezPoint.</summary>
public class MoveToRezPointEffect : EffectTemplate
{
    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        var character = target as Character ?? caster as Character;
        if (character == null)
            return;

        var portal = PortalManager.Instance.GetClosestReturnPortal(character);
        var destination = CharacterTeleportManager.FromPortal(portal, character.Transform.WorldId);
        if (destination == null)
        {
            Logger.Warn("MoveToRezPointEffect: no respawn point for character={0}", character.Id);
            return;
        }

        CharacterTeleportManager.Teleport(character, destination, TeleportReason.MoveToLocation);
    }
}
