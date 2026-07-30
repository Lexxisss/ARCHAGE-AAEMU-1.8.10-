using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCResultRestrictCheckPacket : GamePacket
{
    private readonly uint _characterId;
    private readonly byte _code;
    private readonly byte _result;

    public SCResultRestrictCheckPacket(uint characterId, byte code, byte result) : base(SCOffsets.SCResultRestrictCheckPacket, 5)
    {
        _characterId = characterId;
        _code = code;
        _result = result;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // 10.8.1.0 widened the character id in this response to uint64.
        // The two trailing bytes are ordered result, then request code.
        stream.Write((ulong)_characterId);
        stream.Write(_result);
        stream.Write(_code);
        return stream;
    }
}
