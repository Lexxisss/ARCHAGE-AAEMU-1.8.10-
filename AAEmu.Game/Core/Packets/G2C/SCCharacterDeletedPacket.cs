using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterDeletedPacket : GamePacket
{
    /// <summary>Client-side bound on the encoded name.</summary>
    private const int MaxNameBytes = 128;

    private readonly uint _characterId;
    private readonly string _characterName;

    public SCCharacterDeletedPacket(uint characterId, string characterName) : base(SCOffsets.SCCharacterDeletedPacket, 5)
    {
        _characterId = characterId;
        _characterName = characterName;
    }

    /// <summary>
    /// Verified against the target client: u64 characterId, then a u16 byte length and that
    /// many raw name bytes, with no terminator. The id was being written as a u32, leaving
    /// the packet four bytes short and shifting the name that follows it.
    /// </summary>
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)_characterId);

        // The client bounds the name at 128 bytes and rejects anything longer.
        var name = _characterName ?? string.Empty;
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        if (nameBytes.Length > MaxNameBytes)
        {
            Logger.Warn($"SCCharacterDeleted name for characterId {_characterId} is {nameBytes.Length} bytes, truncating to {MaxNameBytes}");
            System.Array.Resize(ref nameBytes, MaxNameBytes);
        }

        stream.Write(nameBytes, true);
        return stream;
    }
}
