using System;
using System.Linq;

using AAEmu.Commons.Exceptions;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartSkillPacket : GamePacket
{
    public CSStartSkillPacket() : base(CSOffsets.CSStartSkillPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var skillId = stream.ReadUInt32();

        var skillCasterType = stream.ReadByte();
        if (!Enum.IsDefined(typeof(SkillCasterType), skillCasterType))
        {
            Logger.Warn("StartSkill: unsupported caster type {0} for skill {1}", skillCasterType, skillId);
            return;
        }
        var skillCaster = SkillCaster.GetByType((SkillCasterType)skillCasterType);
        skillCaster.Read(stream);

        var skillCastTargetType = stream.ReadByte();
        if (!Enum.IsDefined(typeof(SkillCastTargetType), skillCastTargetType))
        {
            Logger.Warn("StartSkill: unsupported target type {0} for skill {1}", skillCastTargetType, skillId);
            return;
        }
        var skillCastTarget = SkillCastTarget.GetByType((SkillCastTargetType)skillCastTargetType);
        skillCastTarget.Read(stream);

        var flag = stream.ReadByte();
        var objectType = (SkillObjectType)(flag & 0x3F);
        var skillObject = SkillObject.GetByType(objectType);
        skillObject.Flag80 = (flag & 0x80) != 0;
        skillObject.Flag40 = (flag & 0x40) != 0;

        // An object type we have no layout for falls back to the plain SkillObject, which
        // reads nothing - what both 5.0 and the working 1.8 build do. Rejecting the cast
        // outright instead meant every skill carrying an unmapped object type was dropped,
        // which is what broke all doodad interaction (type 28).
        if (objectType != SkillObjectType.None)
        {
            try
            {
                skillObject.Read(stream);
            }
            catch (MarshalException)
            {
                // A payload that does not match our layout must not take the session down
                // with it: opening the coin purse sends a type 4 body shorter than the 20
                // bytes we expect, and the exception propagated all the way up to
                // GameProtocolHandler, which responds by shutting the connection down.
                Logger.Warn(
                    "StartSkill: skill object type {0} payload for skill {1} does not match the expected layout, ignoring the object",
                    objectType,
                    skillId);
                skillObject = SkillObject.GetByType(SkillObjectType.None);
                skillObject.Flag80 = (flag & 0x80) != 0;
                skillObject.Flag40 = (flag & 0x40) != 0;
            }
        }

        // x2game.dll serializes this byte after every SkillObject payload,
        // including SkillObjectType.None. It is named inputDirection in RTTI.
        if (stream.LeftBytes > 0)
            skillObject.InputDirection = stream.ReadByte();

        var template = SkillManager.Instance.GetSkillTemplate(skillId);
        if (template == null)
        {
            Logger.Warn("StartSkill: unknown skill template {0}; request rejected", skillId);
            return;
        }

        Logger.Debug(
            "StartSkill: skill={0}, flag={1}, caster={2}, target={3}",
            skillId,
            flag,
            skillCaster.ObjId,
            skillCastTarget.ObjId);

        if (skillCaster is SkillCasterMount mountCaster)
        {
            UseMountSkill(template, mountCaster, skillCastTarget, skillObject);
            return;
        }

        if (skillCaster is SkillCasterUnit && skillCaster.ObjId != Connection.ActiveChar.ObjId)
        {
            Logger.Warn(
                "StartSkill: character {0} ({1}) attempted to cast skill {2} as unit {3}",
                Connection.ActiveChar.Name,
                Connection.ActiveChar.ObjId,
                skillId,
                skillCaster.ObjId);
            return;
        }

        if (skillCaster is SkillItem itemCaster)
        {
            UseItemSkill(template, itemCaster, skillCastTarget, skillObject);
            return;
        }

        var isServerProvidedSkill = SkillManager.Instance.IsDefaultSkill(skillId)
                                    || SkillManager.Instance.IsCommonSkill(skillId);
        var isLearnedSkill = Connection.ActiveChar.Skills.Skills.ContainsKey(skillId);
        var isLearnedVariant = skillId > 0 && Connection.ActiveChar.Skills.IsVariantOfSkill(skillId);

        if (!isServerProvidedSkill && !isLearnedSkill && !isLearnedVariant)
        {
            Logger.Warn(
                "StartSkill: character {0} ({1}) attempted to use unauthorized skill {2}",
                Connection.ActiveChar.Name,
                Connection.ActiveChar.ObjId,
                skillId);
            return;
        }

        ExecuteSkill(
            template,
            Connection.ActiveChar,
            skillCaster,
            skillCastTarget,
            skillObject,
            isLearnedSkill ? Connection.ActiveChar : null);
    }

    private void UseMountSkill(
        SkillTemplate template,
        SkillCasterMount skillCaster,
        SkillCastTarget skillCastTarget,
        SkillObject skillObject)
    {
        var caster = WorldManager.Instance.GetBaseUnit(skillCaster.ObjId);
        if (caster is not Mate && caster is not Slave)
        {
            Logger.Warn("StartSkill: mount/slave caster {0} was not found for skill {1}", skillCaster.ObjId, template.Id);
            return;
        }

        var controlledByCharacter = caster switch
        {
            Mate mate => mate.OwnerObjId == Connection.ActiveChar.ObjId
                         || mate.Passengers.Values.Any(passenger => passenger.ObjId == Connection.ActiveChar.ObjId),
            Slave slave => slave.OwnerObjId == Connection.ActiveChar.ObjId
                           || slave.Summoner?.ObjId == Connection.ActiveChar.ObjId
                           || slave.AttachedCharacters.Values.Any(character => character.ObjId == Connection.ActiveChar.ObjId),
            _ => false
        };
        if (!controlledByCharacter)
        {
            Logger.Warn(
                "StartSkill: character {0} ({1}) does not control mount/slave {2} for skill {3}",
                Connection.ActiveChar.Name,
                Connection.ActiveChar.ObjId,
                skillCaster.ObjId,
                template.Id);
            return;
        }

        var attachedSkillId = MateManager.Instance.GetMountAttachedSkills(
            template.Id,
            Connection.ActiveChar.AttachedPoint);

        var result = ExecuteSkill(template, caster, skillCaster, skillCastTarget, skillObject);
        if (result != SkillResult.Success || attachedSkillId == 0)
            return;

        var riderTarget = Connection.ActiveChar.CurrentTarget as Unit;
        Connection.ActiveChar.UseSkill(attachedSkillId, riderTarget ?? Connection.ActiveChar);
    }

    private void UseItemSkill(
        SkillTemplate template,
        SkillItem skillCaster,
        SkillCastTarget skillCastTarget,
        SkillObject skillObject)
    {
        if (skillCaster.ObjId != 0 && skillCaster.ObjId != Connection.ActiveChar.ObjId)
        {
            Logger.Warn(
                "StartSkill: character {0} ({1}) attempted to use item skill {2} as object {3}",
                Connection.ActiveChar.Name,
                Connection.ActiveChar.ObjId,
                template.Id,
                skillCaster.ObjId);
            return;
        }

        var item = Connection.ActiveChar.Inventory.GetItemById(skillCaster.ItemId);
        if (item == null || item.TemplateId != skillCaster.ItemTemplateId)
        {
            Logger.Warn(
                "StartSkill: item {0} (template {1}) was not found or does not match skill {2}",
                skillCaster.ItemId,
                skillCaster.ItemTemplateId,
                template.Id);
            return;
        }

        // BindOnPickup is retained for house-fireplace portal items used by this server branch.
        if (template.Id != item.Template.UseSkillId && item.Template.BindType != ItemBindType.BindOnPickup)
        {
            Logger.Warn(
                "StartSkill: item {0} does not authorize skill {1} (configured skill: {2})",
                item.Id,
                template.Id,
                item.Template.UseSkillId);
            return;
        }

        ExecuteSkill(template, Connection.ActiveChar, skillCaster, skillCastTarget, skillObject);
    }

    private SkillResult ExecuteSkill(
        SkillTemplate template,
        BaseUnit caster,
        SkillCaster skillCaster,
        SkillCastTarget skillCastTarget,
        SkillObject skillObject,
        Unit owner = null)
    {
        var skill = new Skill(template, owner);
        var result = skill.Use(caster, skillCaster, skillCastTarget, skillObject);
        if (result != SkillResult.Success)
        {
            Logger.Debug(
                "StartSkill rejected: skill={0}, caster={1}, target={2}, result={3}",
                template.Id,
                skillCaster.ObjId,
                skillCastTarget.ObjId,
                result);
        }
        return result;
    }
}
