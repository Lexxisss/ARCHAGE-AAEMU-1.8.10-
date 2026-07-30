using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Publishes whether a unit's equipment information is visible.
/// Target 10.8.1.0 wire body: Bc unit id followed by one boolean.
/// </summary>
public class SCUnitOpenEquipInfoPacket : GamePacket
{
    private readonly uint _unitId;
    private readonly bool _open;

    public SCUnitOpenEquipInfoPacket(uint unitId, bool open)
        : base(SCOffsets.SCUnitOpenEquipInfoPacket, 5)
    {
        _unitId = unitId;
        _open = open;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_unitId);
        stream.Write(_open);
        return stream;
    }
}
