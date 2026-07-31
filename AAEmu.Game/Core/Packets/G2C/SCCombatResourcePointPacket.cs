using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 1.8.1.0 layout recovered from x2game.dll:
/// bc, type, point(int64), updateTime(uint32).
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
