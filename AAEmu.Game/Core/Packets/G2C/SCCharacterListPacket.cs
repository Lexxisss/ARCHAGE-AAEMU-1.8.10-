using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterListPacket : GamePacket
{
    private readonly bool _last;
    private readonly Character[] _characters;

    public SCCharacterListPacket(bool last, Character[] characters) : base(SCOffsets.SCCharacterListPacket, 5)
    {
        _last = last;
        _characters = characters;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_last);
        stream.Write((byte)_characters.Length);
        foreach (var character in _characters)
        {
            var startOffset = stream.Count;
            character.WriteCharacterList1810(stream);
            Logger.Info(
                "SCCharacterList 0x{0:X3}: charId={1}, dynamically serialized entryLength={2}",
                TypeId,
                character.Id,
                stream.Count - startOffset);
        }

        return stream;
    }
}
