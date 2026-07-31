using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
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

    // Swing animation id -> the effect delay the client expects for it. Melee does not use
    // the skill template's flat FireAnimId: the client picks a swing animation per hit from
    // the set belonging to the equipped weapons, and ignores an id outside that set. That
    // is why melee dealt damage but played no swing, while ranged skills - which do use the
    // template value - animated correctly.
    private static readonly Dictionary<int, int> FireAnimRightHand = new() { { 3, 46 }, { 87, 35 } };
    private static readonly Dictionary<int, int> FireAnimLeftHand = new() { { 4, 45 }, { 88, 35 } };
    private static readonly Dictionary<int, int> FireAnimTwoHand = new() { { 7, 45 }, { 95, 45 }, { 139, 45 } };
    private static readonly Dictionary<int, int> FireAnimFist = new() { { 1, 26 }, { 2, 80 } };
    private static readonly Dictionary<int, int> FireAnimNpc = new() { { 1, 37 }, { 2, 80 } };

    private const int MainHandSlot = 15;
    private const int OffHandSlot = 16;

    private readonly uint _id;
    private readonly ushort _tl;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly SkillObject _skillObject;
    private readonly Skill _skill;
    private readonly Unit _casterUnit;
    private readonly Character _character;

    private readonly bool _rightHand;
    private readonly bool _leftHand;
    private readonly bool _twoHand;
    private readonly bool _fist;

    /// <summary>Delay before the server applies the effects, in milliseconds.</summary>
    public int ComputedDelay { get; set; }

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

        _rightHand = _character.Equipment.GetItemBySlot(MainHandSlot) != null;
        _leftHand = _character.Equipment.GetItemBySlot(OffHandSlot) != null;

        // A shield occupies the off hand without being a weapon to swing with.
        if (_character.Buffs.CheckBuff((uint)BuffConstants.EquipShield))
            _leftHand = false;

        if (_character.Buffs.CheckBuff((uint)BuffConstants.EquipTwoHanded))
        {
            _twoHand = true;
            _rightHand = false;
            _leftHand = false;
        }

        _fist = !_twoHand && !_rightHand && !_leftHand;
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

        // Optional f/c/e/p block. Zero means that no optional fields follow.
        stream.Write((byte)0);
        stream.WritePisc(_id, _skill.Template.FireAnimId);
        stream.Write((byte)0); // trailing target flag
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

        var animTable = GetFireAnimTable();
        var animId = GetNextAnimationId(animTable);
        animTable.TryGetValue(animId, out var animDelay);
        WriteTail(stream, (short)animDelay, animId);
    }

    private void WriteTail(PacketStream stream, short effectDelay, long fireAnimId)
    {
        stream.Write(effectDelay);
        stream.Write((short)0);
        stream.Write((byte)0); // optional f/c/e/p block, zero means nothing follows
        stream.WritePisc(_id, fireAnimId);
        stream.Write((byte)0); // trailing target flag
    }

    private Dictionary<int, int> GetFireAnimTable()
    {
        if (_character == null)
            return FireAnimNpc;
        if (_twoHand)
            return FireAnimTwoHand;
        if (_fist)
            return FireAnimFist;

        var table = new Dictionary<int, int>();
        // Right hand alone, or right hand with a shield, swings from the right-hand set.
        if (_rightHand)
            Merge(table, FireAnimRightHand);
        // An off hand weapon contributes its own swings; with an empty right hand the
        // character alternates the off hand with an unarmed swing.
        if (_leftHand)
        {
            if (!_rightHand)
                Merge(table, FireAnimFist);
            Merge(table, FireAnimLeftHand);
        }

        return table.Count > 0 ? table : FireAnimFist;
    }

    private int GetNextAnimationId(Dictionary<int, int> animTable)
    {
        var queue = _casterUnit.FireAnimQueue;
        if (queue == null || queue.Count == 0)
        {
            var rng = new Random();
            queue = new Queue<int>(animTable.Keys.OrderBy(_ => rng.Next()));
            _casterUnit.FireAnimQueue = queue;
        }

        return queue.Count > 0 ? queue.Dequeue() : 0;
    }

    private static void Merge(Dictionary<int, int> target, Dictionary<int, int> source)
    {
        foreach (var kvp in source)
            target[kvp.Key] = kvp.Value;
    }

    private static ushort ToWireTime(int milliseconds)
    {
        return (ushort)Math.Clamp(milliseconds / 10, 0, ushort.MaxValue);
    }
}
