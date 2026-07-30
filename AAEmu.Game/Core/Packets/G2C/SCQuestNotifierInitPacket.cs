using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8 quest-notifier refresh. The target client consumes one Boolean field
/// and rebuilds quest markers/notifiers when this packet is handled.
/// </summary>
public sealed class SCQuestNotifierInitPacket : GamePacket
{
    private readonly bool _init;

    public SCQuestNotifierInitPacket(bool init) : base(SCOffsets.SCQuestNotifierInitPacket, 5)
    {
        _init = init;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_init);
        return stream;
    }
}
