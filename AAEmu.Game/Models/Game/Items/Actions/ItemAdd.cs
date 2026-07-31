using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Action 5 for SCItemTaskSuccessPacket: adjusts the amount held in a slot the client
/// already knows about. Serializer: x2game.dll 0x39A8BA70.
/// </summary>
/// <remarks>
/// The amount is a **delta**, not the slot's new total. Measured in game: consuming one from
/// a stack of three and sending the new total of 2 left the client showing 5, and the same
/// again from two showed 3 - it adds what we send to what it already has. Sending a total
/// therefore inflates the stack every time, and only a relog or container resync hides it.
/// </remarks>
public class ItemAdd : ItemTask
{
    private readonly Item _item;
    private readonly int _amount;

    /// <param name="item">Item whose slot is being adjusted.</param>
    /// <param name="amount">Signed change in units - negative when consuming.</param>
    public ItemAdd(Item item, int amount)
    {
        _type = ItemAction.Create;
        _item = item;
        _amount = amount;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);

        stream.Write((byte)_item.SlotType); // type   : u8
        stream.Write((byte)_item.Slot);     // index  : u8
        stream.Write(_item.Id);             // id     : u64
        stream.Write(_amount);              // amount : i32, signed delta
        stream.Write(_item.TemplateId);     // type   : u32 (item template)

        return stream;
    }
}
