using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTargetCharacterConnectionRestrictPacket : GamePacket
{
    public CSTargetCharacterConnectionRestrictPacket() : base(CSOffsets.CSTargetCharacterConnectionRestrictPacket, 5) { }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);
    }
}
