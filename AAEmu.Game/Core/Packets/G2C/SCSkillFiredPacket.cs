using System;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Announces the fire stage of a skill. Field order and the 10 ms wire-time
/// encoding are matched to the target x2game.dll serializer.
/// </summary>
public class SCSkillFiredPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Debug;

    /// <summary>Melee auto attack - the one skill whose animation is weapon dependent.</summary>
    private const uint MeleeAttackSkillId = 2;

    // Melee does not use the skill template's flat FireAnimId: the client only accepts a
    // swing id belonging to the equipped weapon, and silently plays nothing for anything
    // else - which is why melee dealt damage with no visible swing while ranged skills,
    // which do use the template value, animated fine.
    //
    // The ids come from the weapon's holdable record: anim_r1/r2/r3 for the right hand and
    // anim_l1/l2/l3 for the left, with the *_ratio fields weighting the first two. Another
    // build hardcoded {3,87}/{4,88}/{7,95}/{1,2} - those are literally holdable rows 1, 3
    // and 0 of this table, so reading the table covers every weapon rather than four.
    /// <summary>Holdable id used when nothing is equipped.</summary>
    private const uint FistHoldableId = 0;

    private const int MainHandSlot = 15;
    private const int OffHandSlot = 16;

    private enum SwingHand { Right, Left }

    private readonly uint _id;
    private readonly ushort _tl;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly SkillObject _skillObject;
    private readonly Skill _skill;
    private readonly Unit _casterUnit;
    private readonly Character _character;

    private readonly Holdable _mainHand;
    private readonly Holdable _offHand;

    /// <summary>Delay before the server applies the effects, in milliseconds.</summary>
    public int ComputedDelay { get; set; }

    /// <summary>Optional f/c/e/p/d block; defaults to empty.</summary>
    public SkillExtraData ExtraData { get; set; } = SkillExtraData.Default;

    /// <summary>Bit 0 of the trailing packed flag byte.</summary>
    public bool FlagA { get; set; }

    /// <summary>Bit 1 of the trailing packed flag byte.</summary>
    public bool FlagB { get; set; }

    public SCSkillFiredPacket(
        uint id,
        ushort tl,
        SkillCaster caster,
        SkillCastTarget target,
        Skill skill,
        SkillObject skillObject,
        BaseUnit casterUnit = null)
        : base(SCOffsets.SCSkillFiredPacket, 5)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
        _casterUnit = casterUnit as Unit;
        _character = casterUnit as Character;

        if (_skill.Template.Id != MeleeAttackSkillId || _character == null)
            return;

        _mainHand = GetHoldable(_character.Equipment.GetItemBySlot(MainHandSlot));
        _offHand = GetHoldable(_character.Equipment.GetItemBySlot(OffHandSlot));

        // A shield sits in the off hand without being something to swing with.
        if (_character.Buffs.CheckBuff((uint)BuffConstants.EquipShield))
            _offHand = null;

        // A two-handed weapon is held in the main hand and leaves the off hand unused.
        if (_character.Buffs.CheckBuff((uint)BuffConstants.EquipTwoHanded))
            _offHand = null;
    }

    private static Holdable GetHoldable(Item item)
    {
        return (item?.Template as WeaponTemplate)?.HoldableTemplate;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // In this protocol generation the skill type is carried by the PISC
        // block near the end, while "sid" contains only the transient id.
        stream.Write(_tl);
        stream.Write(_caster);
        stream.Write(_target);
        stream.Write(_skillObject);
        stream.Write(_skillObject.InputDirection);

        if (_skill.Template.Id == MeleeAttackSkillId && _casterUnit != null)
        {
            WriteMeleeAttackTail(stream);
            return stream;
        }

        stream.Write(ToWireTime(ComputedDelay));
        stream.Write(ToWireTime(_skill.Template.ChannelingTime));

        ExtraData.Write(stream);
        stream.WritePisc(_id, _skill.Template.FireAnimId);
        WriteTrailingFlag(stream);
        return stream;
    }

    private void WriteMeleeAttackTail(PacketStream stream)
    {
        // While auto attacking, the client runs the swing loop on its own and expects no
        // per-hit animation or timing here.
        if (_casterUnit.IsAutoAttack)
        {
            WriteTail(stream, 0, 0);
            return;
        }

        WriteTail(stream, (short)ToWireTime(ComputedDelay), PickSwingAnimation());
    }

    /// <summary>
    /// Alternates hands when dual wielding, then picks one of that hand's three swings using
    /// the holdable's ratio weights. Nothing equipped falls back to the bare-hand holdable.
    /// </summary>
    private long PickSwingAnimation()
    {
        var hand = SwingHand.Right;
        if (_mainHand == null && _offHand != null)
        {
            hand = SwingHand.Left;
        }
        else if (_mainHand != null && _offHand != null)
        {
            // Dual wield: keep the hands strictly alternating rather than random, which is
            // what a swing sequence looks like in game.
            hand = _casterUnit.NextSwingUsesOffHand ? SwingHand.Left : SwingHand.Right;
            _casterUnit.NextSwingUsesOffHand = !_casterUnit.NextSwingUsesOffHand;
        }

        var holdable = hand == SwingHand.Left ? _offHand : _mainHand;
        holdable ??= ItemManager.Instance.GetHoldable(FistHoldableId);
        if (holdable == null)
            return _skill.Template.FireAnimId;

        var (first, firstRatio, second, secondRatio, third) = hand == SwingHand.Left
            ? (holdable.AnimL1Id, holdable.AnimL1Ratio, holdable.AnimL2Id, holdable.AnimL2Ratio, holdable.AnimL3Id)
            : (holdable.AnimR1Id, holdable.AnimR1Ratio, holdable.AnimR2Id, holdable.AnimR2Ratio, holdable.AnimR3Id);

        var roll = Rand.Next(0, 100);
        if (first > 0 && roll < firstRatio)
            return first;
        if (second > 0 && roll < firstRatio + secondRatio)
            return second;
        if (third > 0)
            return third;

        return first > 0 ? first : _skill.Template.FireAnimId;
    }

    private void WriteTail(PacketStream stream, short effectDelay, long fireAnimId)
    {
        stream.Write(effectDelay);
        stream.Write((short)0);
        ExtraData.Write(stream);
        stream.WritePisc(_id, fireAnimId);
        WriteTrailingFlag(stream);
    }

    /// <summary>Two booleans packed into one byte: bit0 and bit1.</summary>
    private void WriteTrailingFlag(PacketStream stream)
    {
        stream.Write((byte)((FlagA ? 0x01 : 0) | (FlagB ? 0x02 : 0)));
    }

    private static ushort ToWireTime(int milliseconds)
    {
        return (ushort)Math.Clamp(milliseconds / 10, 0, ushort.MaxValue);
    }
}
