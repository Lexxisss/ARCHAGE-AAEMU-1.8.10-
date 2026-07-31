using System.Collections.Generic;

using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions;

public abstract class ItemTask : PacketMarshaler
{
    /// <summary>
    /// Log type this client expects per action, taken from live captures (pcap-analysis
    /// op10B samples, over every packet that carries at least one task):
    /// action 1 -> 0 (15 samples), action 5 -> 3 (44), action 6 -> 1 (48), action 10 -> 0 (2).
    ///
    /// Nothing but SwapSlot was setting this field, so every other action went out as
    /// UpdateOnly - the equivalent of 5.0's SetTlogT was dropped somewhere along the way.
    /// A Create announced with UpdateOnly is not a pairing the client acts on, which fits
    /// the symptom exactly: neither picking an item up nor consuming one changed the bag
    /// until the player forced a refresh.
    ///
    /// Actions with no capture coverage keep UpdateOnly rather than a guessed value.
    /// </summary>
    private static readonly Dictionary<ItemAction, ItemTaskLogType> ObservedLogTypes = new()
    {
        { ItemAction.ChangeMoneyAmount, ItemTaskLogType.UpdateOnly },
        { ItemAction.Create, ItemTaskLogType.MoveItem },
        { ItemAction.Take, ItemTaskLogType.GainItem },
        // No direct observation for Remove, but it is the mirror of Take - both carry the
        // full record, one introducing the item and one taking it away - and the 5.0 branch
        // agrees on RemoveItem here, unlike the two pairings it gets wrong.
        { ItemAction.Remove, ItemTaskLogType.RemoveItem }
    };

    protected ItemAction _type;

    /// <summary>Set to override the mapping above; null means derive it from the action.</summary>
    protected ItemTaskLogType? _logType;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_type);            // action
        stream.Write((byte)ResolveLogType()); // tLogt
        return stream;
    }

    private ItemTaskLogType ResolveLogType()
    {
        if (_logType.HasValue)
            return _logType.Value;

        return ObservedLogTypes.TryGetValue(_type, out var logType)
            ? logType
            : ItemTaskLogType.UpdateOnly;
    }
}
