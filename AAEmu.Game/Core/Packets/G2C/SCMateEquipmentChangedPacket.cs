using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMateEquipmentChangedPacket : GamePacket
{
    private readonly ushort _tlId;
    private readonly long _characterId;
    private readonly uint _passengerId;
    private readonly bool _bts;
    private readonly byte _num;
    private readonly (SlotType type, byte slot, Item item) _itemA;
    private readonly (SlotType type, byte slot, Item item) _itemB;

    public SCMateEquipmentChangedPacket((SlotType type, byte slot, Item item) itemA, (SlotType type, byte slot, Item item) itemB, ushort tlId, long characterId, uint passengerId, bool bts) : base(SCOffsets.SCMateEquipmentChangedPacket, 5)
    {
        _itemA = itemA;
        _itemB = itemB;
        _tlId = tlId;
        _characterId = characterId;
        _passengerId = passengerId;
        _bts = bts;
        _num = 1; // all time == 1
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_characterId); // ownerPersistentId : i64
        stream.Write(_tlId);        // tl
        stream.Write(_passengerId); // type
        stream.Write(_bts);         // bts
        stream.Write(_num);         // num

        if (_itemA.item == null)
            stream.Write(0);
        else
            stream.Write(_itemA.item);

        if (_itemB.item == null)
            stream.Write(0);
        else
            stream.Write(_itemB.item);

        stream.Write((byte)_itemA.type); // sourceType
        stream.Write(_itemA.slot);       // sourceIndex
        stream.Write((byte)_itemB.type); // destType
        stream.Write(_itemB.slot);       // destIndex
        stream.Write(0L);                // expireTime : i64, part of every record

        stream.Write(true); // success; the handler reconciles its model against this

        return stream;
    }
}