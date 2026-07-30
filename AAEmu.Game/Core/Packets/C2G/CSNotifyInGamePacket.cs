using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Legacy symbol retained for source compatibility. Its target 10.8 opcode is
/// not confirmed and it is intentionally not registered.
/// </summary>
public class CSNotifyInGamePacket : GamePacket
{
    public CSNotifyInGamePacket() : base(CSOffsets.CSNotifyInGamePacket, 5) { }

    public override void Read(PacketStream stream)
    {
        if (stream.LeftBytes > 0)
            stream.ReadBytes(stream.LeftBytes);
    }
}
