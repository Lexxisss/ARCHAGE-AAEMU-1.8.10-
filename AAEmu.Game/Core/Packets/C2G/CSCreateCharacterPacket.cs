using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateCharacterPacket : GamePacket
{
    public CSCreateCharacterPacket() : base(CSOffsets.CSCreateCharacterPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var name = stream.ReadString();
        var race = stream.ReadByte();
        var gender = stream.ReadByte();
        var items = new uint[7];
        for (var i = 0; i < 7; i++)
            items[i] = stream.ReadUInt32();

        var customModel = new UnitCustomModelParams();
        customModel.Read(stream);

        var ability1 = stream.ReadByte();
        var ability2 = stream.ReadByte();
        var ability3 = stream.ReadByte();
        var level = stream.ReadByte();
        // Confirmed in compiled x2game.dll serializer 0x399EF5F0: name, byte race,
        // byte gender, 7 uint item templates, model params, 3 byte abilities,
        // byte level, int introZoneId.
        var introZoneId = stream.ReadInt32();

        Logger.Info(
            "CSCreateCharacter 0x{0:X3}: name={1}, race={2}, gender={3}, abilities={4}/{5}/{6}, requestedLevel={7}, introZoneId={8}, remaining={9}",
            TypeId,
            name,
            race,
            gender,
            ability1,
            ability2,
            ability3,
            level,
            introZoneId,
            stream.LeftBytes);

        CharacterManager.Instance.Create(Connection, name, race, gender, items, customModel, ability1, ability2, ability3, level);
    }
}
