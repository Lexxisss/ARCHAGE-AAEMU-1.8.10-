using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPlotEventPacket : GamePacket
{
    private const int OptionalValueCount = 13;

    private readonly ushort _tl;
    private readonly uint _eventId;
    private readonly uint _skillId;
    private readonly PlotObject _caster;
    private readonly PlotObject _target;
    private readonly ulong _itemId;
    private readonly uint _castingObjId;
    private readonly uint _castingTimeMs;
    private readonly uint _channelingObjId;
    private readonly uint _channelingTimeMs;
    private readonly IReadOnlyList<uint> _targetUnitIds;
    private readonly PlotEventFlags _flags;
    private readonly IReadOnlyList<int> _values;
    private readonly byte _inputDirection;

    public SCPlotEventPacket(ushort tl, uint eventId, uint skillId, PlotObject caster, PlotObject target,
        uint castingObjId, uint castingTimeMs, uint channelingObjId, uint channelingTimeMs,
        PlotEventFlags flags, ulong itemId = 0L, IReadOnlyList<uint> targetUnitIds = null,
        IReadOnlyList<int> values = null, byte inputDirection = 0)
        : base(SCOffsets.SCPlotEventPacket, 5)
    {
        _tl = tl;
        _eventId = eventId;
        _skillId = skillId;
        _caster = caster;
        _target = target;
        _itemId = itemId;
        _castingObjId = castingObjId;
        _castingTimeMs = castingTimeMs;
        _channelingObjId = channelingObjId;
        _channelingTimeMs = channelingTimeMs;
        _targetUnitIds = targetUnitIds ?? [];
        _flags = flags;
        _values = values ?? [];
        _inputDirection = inputDirection;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Full 1.8.1.0 layout: target x2game.dll 0x399EB9A0.
        stream.Write(_tl);      // tl:u16
        stream.Write(_eventId); // eventId:u32
        stream.Write(_skillId); // skillId:u32
        stream.Write(_caster);  // PlotObject
        stream.Write(_target);  // PlotObject
        stream.Write(_itemId);  // itemObjId:u64

        // The client keeps casting and channeling as two independent references.
        // Both durations are u16 values in 10 ms units on the wire. Target x2game.dll
        // 0x399EBAAA/0x399EBB07 call 0x399CD9B0; that helper keeps a u32 millisecond
        // value in memory but serializes it through PacketSerializer slot +0x88 (u16).
        // Writing u32 here shifts targetUnitCount and the flags byte by four bytes.
        stream.WriteBc(_castingObjId);
        stream.Write(EncodeMilliseconds(_castingTimeMs));
        stream.WriteBc(_channelingObjId);
        stream.Write(EncodeMilliseconds(_channelingTimeMs));

        var targetUnitCount = (byte)Math.Min(_targetUnitIds.Count, byte.MaxValue);
        stream.Write(targetUnitCount);
        for (var i = 0; i < targetUnitCount; i++)
            stream.WriteBc(_targetUnitIds[i]);

        stream.Write((byte)_flags);
        if ((_flags & PlotEventFlags.HasValues) != 0)
        {
            // Target client always reads exactly thirteen i32 runtime plot values when bit 3 is set.
            // They are not SkillControllerTemplate.Value1..Value13 and are not required by Leap.
            for (var i = 0; i < OptionalValueCount; i++)
                stream.Write(i < _values.Count ? _values[i] : 0);
        }

        stream.Write(_inputDirection);
        return stream;
    }

    private static ushort EncodeMilliseconds(uint milliseconds)
    {
        var wireUnits = milliseconds / 10u;
        return (ushort)Math.Min(wireUnits, ushort.MaxValue);
    }
}
