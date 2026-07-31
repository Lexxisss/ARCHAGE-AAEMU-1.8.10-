using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Target 1.8.1.0 Create action for SCItemTaskSuccessPacket.
/// Serializer: x2game.dll 0x39A8BA70.
/// </summary>
public class ItemAdd : ItemTask
{
    private readonly Item _item;

    public ItemAdd(Item item)
    {
        _type = ItemAction.Create;
        _item = item;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        // The target client uses a compact create delta here. The complete item
        // representation is carried by inventory/acquisition packets when needed.
        stream.Write((byte)_item.SlotType); // type   : u8
        stream.Write((byte)_item.Slot);     // index  : u8
        stream.Write(_item.Id);             // id     : u64
        stream.Write(_item.Count);          // amount : i32
        stream.Write(_item.TemplateId);     // type   : u32 (item template)

        return stream;
    }
}
