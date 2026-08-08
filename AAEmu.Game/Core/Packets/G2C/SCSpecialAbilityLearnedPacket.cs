using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 1.8.1.0 SCSpecialAbilityLearnedPacket.
/// TARGET serializer 0x399D2FF0 and dedicated serializer 0x39C24500 write exactly one u8 ability id.
/// The dedicated server owns the S->C learned notification path; no separate C->S special-ability packet is required.
/// </summary>
public sealed class SCSpecialAbilityLearnedPacket : GamePacket
{
    private readonly AbilityType _ability;

    public SCSpecialAbilityLearnedPacket(AbilityType ability)
        : base(SCOffsets.SCSpecialAbilityLearnedPacket, 5)
    {
        _ability = ability;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_ability);
        return stream;
    }
}
