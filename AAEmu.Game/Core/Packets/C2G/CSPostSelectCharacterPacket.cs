using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Confirms the lobby character after the initial character-state response.
/// The opaque tail remains diagnostic until its fields are identified in x2game.dll.
/// </summary>
public class CSPostSelectCharacterPacket : GamePacket
{
    public CSPostSelectCharacterPacket() : base(CSOffsets.CSPostSelectCharacterPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var characterId = stream.LeftBytes >= sizeof(uint) ? stream.ReadUInt32() : 0;
        var extra = stream.LeftBytes > 0 ? stream.ReadBytes(stream.LeftBytes) : Array.Empty<byte>();
        var isKnownCharacter = characterId != 0 && Connection.Characters.ContainsKey(characterId);

        Logger.Info(
            "CSPostSelectCharacter: accountId={0} activeCharacterId={1} characterId={2} known={3} extraLen={4} extra={5}",
            Connection.AccountId,
            Connection.ActiveChar?.Id ?? 0,
            characterId,
            isKnownCharacter,
            extra.Length,
            Convert.ToHexString(extra));
    }
}
