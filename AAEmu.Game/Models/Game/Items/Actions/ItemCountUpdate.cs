namespace AAEmu.Game.Models.Game.Items.Actions;

/// <summary>
/// Adds or subtracts units from an existing item stack.
/// </summary>
/// <remarks>
/// This used to emit action 4 with a <c>templateId:u32, amount:i64</c> body. Action 4 does
/// exist on this client, but it is a currency/resource path that never touches an inventory
/// slot - it carries neither a slot nor an item id, so there is nothing for it to adjust.
/// Every caller here means "change the count of this item in this slot", which is action 5
/// with a signed delta.
///
/// That single mistake accounted for the stack split producing a wrong remainder, a merge
/// leaving the source untouched, and a partial consume not decrementing: all of them route
/// through this class.
/// </remarks>
public class ItemCountUpdate : ItemAdd
{
    public ItemCountUpdate(Item item, int count)
        : base(item, count)
    {
    }

    public ItemCountUpdate(Item item, int count, SlotType slotType, byte slot)
        : base(item, count, slotType, slot)
    {
    }
}
