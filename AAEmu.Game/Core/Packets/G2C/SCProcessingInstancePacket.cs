using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public enum ProcessingInstanceState : uint
{
    Waiting = 0,
    UsingIndunTicket = 1
}

public class SCProcessingInstancePacket : GamePacket
{
    private readonly uint _zoneId;
    private readonly ProcessingInstanceState _state;

    public SCProcessingInstancePacket(uint zoneId, ProcessingInstanceState state = ProcessingInstanceState.Waiting)
        : base(SCOffsets.SCProcessingInstancePacket, 5)
    {
        _zoneId = zoneId;
        _state = state;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Target 1.8.1 serializer x2game.dll 0x399C7700:
        //   +0x10 -> zoneId:u32 (0x399C7710)
        //   +0x14 -> state:u32  (0x399C7730)
        stream.Write(_zoneId);
        stream.Write((uint)_state);
        return stream;
    }
}
