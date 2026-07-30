using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8 SC_DOODAD_INTERACTION_CALLBACK (0x00C2).
/// </summary>
public class SCDoodadInteractionCallbackPacket : GamePacket
{
    private readonly uint _doodadObjId;
    private readonly uint _type;
    private readonly uint _duration;

    public SCDoodadInteractionCallbackPacket(uint doodadObjId, uint type, uint duration)
        : base(SCOffsets.SCDoodadInteractionCallbackPacket, 5)
    {
        _doodadObjId = doodadObjId;
        _type = type;
        _duration = duration;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_doodadObjId);
        stream.Write(_type);
        stream.Write(_duration);
        return stream;
    }
}
