using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 1.8.1.0 factory 0x393670B0 assigns opcode 0x304. Serializer
/// 0x399DB4D0 writes bc objId, uint32 type, int64 point, uint32 updateTime.
/// DEV 0x39C654A0 independently names the same fields and uses the same widths/order.
/// updateTime is stored separately from point; its exact clock origin is not proven.
/// </summary>
public sealed class SCCombatResourcePointPacket : GamePacket
{
    private readonly uint _objId;
    private readonly uint _type;
    private readonly long _point;
    private readonly uint _updateTime;

    public SCCombatResourcePointPacket(uint objId, uint type, long point, uint updateTime)
        : base(SCOffsets.SCCombatResourcePointPacket, 5)
    {
        _objId = objId;
        _type = type;
        _point = point;
        _updateTime = updateTime;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objId);
        stream.Write(_type);
        stream.Write(_point);
        stream.Write(_updateTime);
        return stream;
    }
}
