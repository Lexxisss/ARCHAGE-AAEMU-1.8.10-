using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDoodadOriginatorPacket : GamePacket
{
    private readonly uint _objId;
    private readonly ulong _originatorId;
    private readonly uint _faction;

    public SCDoodadOriginatorPacket(uint objId, ulong originatorId, uint faction) : base(SCOffsets.SCDoodadOriginatorPacket, 5)
    {
        _objId = objId;
        _originatorId = originatorId;
        _faction = faction;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_objId);
        stream.Write(_originatorId);
        stream.Write(_faction);

        return stream;
    }
}
