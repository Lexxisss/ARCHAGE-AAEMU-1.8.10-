using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Target 1.8.1.0 AddStack action for SCItemTaskSuccessPacket.
/// Serializer: x2game.dll 0x39A8B920.
/// </summary>
public class ItemCountUpdate : ItemTask
{
    private readonly Item _item;
    private readonly long _count;

    /// <summary>
    /// Adds or subtracts units from an existing item stack.
    /// </summary>
    public ItemCountUpdate(Item item, int count)
    {
        _type = ItemAction.AddStack;
        _item = item;
        _count = count;
    }

    public override PacketStream Write(PacketStream stream)
    {
        base.Write(stream);
        stream.Write(_item.TemplateId); // type   : u32
        stream.Write(_count);           // amount : i64
        return stream;
    }
}
