using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game;

/// <summary>
/// One equipment change on a mate: what each of the two slots held before it happened.
/// </summary>
/// <remarks>
/// Both items are a snapshot taken before the change, not one slot's earlier and later state.
/// On success the client puts the first into the destination and the second back into the
/// source, so a swap, an equip and an unequip are all the same shape with different contents.
///
/// The items use the ordinary item encoding, not a mate-specific one.
/// </remarks>
public class MateEquipmentDelta
{
    /// <summary>What the inventory side held before the change.</summary>
    public Item ItemAtSourceBefore { get; set; }

    /// <summary>What the mate's slot held before the change; the client puts this in the source.</summary>
    public Item ItemAtDestinationBefore { get; set; }

    public SlotType SourceType { get; set; }
    public byte SourceIndex { get; set; }
    public SlotType DestType { get; set; }
    public byte DestIndex { get; set; }
    public long ExpireTime { get; set; }
}

/// <summary>
/// A set of equipment changes for one mate, as both directions carry it.
/// </summary>
/// <remarks>
/// The same shape the slave set has, with the mate's own header and its own cap: the client
/// clamps the record count to two here, one fewer than for a ship. Anything beyond that is
/// discarded rather than rejected, so larger changes have to be split across messages.
///
/// The reply used to be a fixed single record with the count written as a constant, which is
/// only ever right for a request that carried one.
/// </remarks>
public class MateEquipment : PacketMarshaler
{
    /// <summary>The client caps the record count here.</summary>
    public const int MaxRecords = 2;

    /// <summary>
    /// The last slot index the client will accept for a mate. Past it, the neighbouring flags
    /// and expiry messages write through a pointer they never obtained.
    /// </summary>
    public const byte MaxSlotIndex = 0x22;

    /// <summary>Persistent character id of the owner, not a world object id.</summary>
    public long OwnerPersistentId { get; set; }

    public ushort Tl { get; set; }

    /// <summary>The serializer's own label for this is the generic <c>type</c>.</summary>
    public uint MateType { get; set; }

    /// <summary>Exact meaning unresolved; carried through unchanged.</summary>
    public bool Bts { get; set; }

    public List<MateEquipmentDelta> Changes { get; } = new();

    public override PacketStream Write(PacketStream stream)
    {
        var num = Math.Min(Changes.Count, MaxRecords);

        stream.Write(OwnerPersistentId); // ownerPersistentId : i64
        stream.Write(Tl);                // tl                : u16
        stream.Write(MateType);          // type              : u32
        stream.Write(Bts);               // bts               : bool
        stream.Write((byte)num);         // num               : u8, clamped to 2 by the client

        for (var i = 0; i < num; i++)
        {
            var change = Changes[i];

            // An empty side is a four-byte zero template id and nothing more, which the reader
            // has its own branch for.
            if (change.ItemAtSourceBefore == null)
                stream.Write(0);
            else
                stream.Write(change.ItemAtSourceBefore);

            if (change.ItemAtDestinationBefore == null)
                stream.Write(0);
            else
                stream.Write(change.ItemAtDestinationBefore);

            stream.Write((byte)change.SourceType);
            stream.Write(change.SourceIndex);
            stream.Write((byte)change.DestType);
            stream.Write(change.DestIndex);
            stream.Write(change.ExpireTime);
        }

        return stream;
    }
}
