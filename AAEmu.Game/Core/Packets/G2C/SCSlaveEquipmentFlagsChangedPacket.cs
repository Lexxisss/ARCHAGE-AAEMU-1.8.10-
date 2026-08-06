using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Changes the client-side flags of one ship/vehicle equipment slot.</summary>
public class SCSlaveEquipmentFlagsChangedPacket : GamePacket
{
    private readonly ushort _tl;
    private readonly SlotType _slotType;
    private readonly byte _slotIndex;
    private readonly byte _flags;

    public SCSlaveEquipmentFlagsChangedPacket(
        ushort tl, SlotType slotType, byte slotIndex, byte flags)
        : base(SCOffsets.SCSlaveEquipmentFlagsChangedPacket, 5)
    {
        _tl = tl;
        _slotType = slotType;
        _slotIndex = slotIndex;
        _flags = flags;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);
        stream.Write((byte)_slotType);
        stream.Write(_slotIndex);
        stream.Write(_flags);
        return stream;
    }
}
