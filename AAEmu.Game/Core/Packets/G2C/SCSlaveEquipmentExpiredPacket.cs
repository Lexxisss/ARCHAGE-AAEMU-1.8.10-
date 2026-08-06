using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Marks one ship/vehicle equipment slot as expired.</summary>
public class SCSlaveEquipmentExpiredPacket : GamePacket
{
    private readonly ushort _tl;
    private readonly SlotType _slotType;
    private readonly byte _slotIndex;

    public SCSlaveEquipmentExpiredPacket(ushort tl, SlotType slotType, byte slotIndex)
        : base(SCOffsets.SCSlaveEquipmentExpiredPacket, 5)
    {
        _tl = tl;
        _slotType = slotType;
        _slotIndex = slotIndex;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);
        stream.Write((byte)_slotType);
        stream.Write(_slotIndex);
        return stream;
    }
}
