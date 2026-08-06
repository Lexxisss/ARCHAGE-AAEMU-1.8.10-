using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Links a spawned slave to the item that summoned it.</summary>
/// <remarks>
/// Target wire: <c>unitId:bc, itemId:u64, health:i32, slot.type:u8, slot.index:u8</c>.
/// </remarks>
public class SCUpdateSlaveSourceItemPacket : GamePacket
{
    private readonly uint _unitId;
    private readonly ulong _itemId;
    private readonly int _health;
    private readonly SlotType _slotType;
    private readonly byte _slotIndex;

    public SCUpdateSlaveSourceItemPacket(
        uint unitId, ulong itemId, int health, SlotType slotType, byte slotIndex)
        : base(SCOffsets.SCUpdateSlaveSourceItemPacket, 5)
    {
        _unitId = unitId;
        _itemId = itemId;
        _health = health;
        _slotType = slotType;
        _slotIndex = slotIndex;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(_unitId);
        stream.Write(_itemId);
        stream.Write(_health);
        stream.Write((byte)_slotType);
        stream.Write(_slotIndex);
        return stream;
    }
}
