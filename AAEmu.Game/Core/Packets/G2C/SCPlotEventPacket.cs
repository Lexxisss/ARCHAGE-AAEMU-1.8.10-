using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPlotEventPacket : GamePacket
{
    private readonly ushort _tl;
    private readonly uint _eventId;
    private readonly uint _skillId;
    private readonly PlotObject _caster;
    private readonly PlotObject _target;
    private readonly uint _objId;
    private readonly ushort _castingTime;
    private readonly byte _flag;
    private readonly ulong _itemId;
    private readonly IReadOnlyList<uint> _targetUnitIds;
    private readonly byte _inputDirection;

    public SCPlotEventPacket(ushort tl, uint eventId, uint skillId, PlotObject caster, PlotObject target,
        uint objId, ushort castingTime, byte flag, ulong itemId = 0L,
        IReadOnlyList<uint> targetUnitIds = null, byte inputDirection = 0)
        : base(SCOffsets.SCPlotEventPacket, 5)
    {
        _tl = tl;
        _eventId = eventId;
        _skillId = skillId;
        _caster = caster;
        _target = target;
        _objId = objId;
        _castingTime = castingTime;
        _flag = flag;
        _itemId = itemId;
        _targetUnitIds = targetUnitIds ?? [];
        _inputDirection = inputDirection;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);      // tl
        stream.Write(_eventId); // eventId
        stream.Write(_skillId); // skillId
        stream.Write(_caster);  // PlotObj
                                // type(b) Unit | Position
                                // casterId(bc) | XYZ
        stream.Write(_target);  // PlotObj
                                // type(b) Unit | Position
                                // targetId(bc) | XYZ
        stream.Write(_itemId);  // itemObjId
        stream.WriteBc(_objId); // обычно 0, но иногда нужно вставлять casterId(bc)
        stream.Write(_castingTime); // msec, castingTime / 10
        stream.WriteBc(0);      // objId
        stream.Write((short)0); // msec
        var targetUnitCount = (byte)System.Math.Min(_targetUnitIds.Count, byte.MaxValue);
        stream.Write(targetUnitCount); // targetUnitCount
        for (var i = 0; i < targetUnitCount; i++)
            stream.WriteBc(_targetUnitIds[i]);
        stream.Write(_flag);
        if (((_flag >> 3) & 1) == 1)
        {
            for (var i = 0; i < 13; i++) // flag = 8
                stream.Write(0); // v
        }

        // Target x2game.dll serializes inputDirection after flag/optional values.
        stream.Write(_inputDirection);
        return stream;
    }
}
