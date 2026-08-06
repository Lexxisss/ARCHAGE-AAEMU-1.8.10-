using System;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class SkillControllerTemplate : EffectTemplate
{
    public uint KindId { get; set; }
    public int[] Value { get; set; }
    public byte ActiveWeaponId { get; set; }
    public uint EndSkillId { get; set; }
    public uint EndAnimId { get; set; }
    public uint StartAnimId { get; set; }
    public string StrValue1 { get; set; }
    public uint TransitionAnim1Id { get; set; }
    public uint TransitionAnim2Id { get; set; }
    public override bool OnActionTime { get; }

    public SkillControllerTemplate()
    {
        Value = new int[15];
    }

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj,
        EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
    {
        if (caster is not Unit owner || target?.Transform == null)
        {
            Logger.Warn(
                "SkillController {0}: invalid owner/target, caster={1}, target={2}",
                Id,
                caster?.ObjId ?? 0,
                target?.ObjId ?? 0);
            return;
        }

        var controller = SkillController.CreateSkillController(this, owner, target);
        if (controller == null)
        {
            Logger.Warn(
                "SkillController {0}: unsupported kind={1}, caster={2}, target={3}",
                Id,
                KindId,
                owner.ObjId,
                target.ObjId);
            return;
        }

        owner.ActiveSkillController?.End();
        owner.ActiveSkillController = controller;
        controller.Execute();

        Logger.Debug(
            "SkillController started: template={0}, kind={1}, owner={2}, target={3}, startAnim={4}, duration={5}, distance={6}",
            Id,
            KindId,
            owner.ObjId,
            target.ObjId,
            StartAnimId,
            Value.Length > 2 ? Value[2] : 0,
            Value.Length > 3 ? Value[3] : 0);
    }
}
