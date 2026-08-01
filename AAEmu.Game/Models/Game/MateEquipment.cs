using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game;

/// <summary>
/// One equipment change on a mate: the item leaving a slot, the item arriving, and where each
/// came from.
/// </summary>
/// <remarks>
/// The items use the ordinary item encoding, not a mate-specific one.
/// </remarks>
public class MateEquipmentDelta
{
    public Item Before { get; set; }
    public Item After { get; set; }
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

            if (change.Before == null)
                stream.Write(0);
            else
                stream.Write(change.Before);

            if (change.After == null)
                stream.Write(0);
            else
                stream.Write(change.After);

            stream.Write((byte)change.SourceType);
            stream.Write(change.SourceIndex);
            stream.Write((byte)change.DestType);
            stream.Write(change.DestIndex);
            stream.Write(change.ExpireTime);
        }

        return stream;
    }
}
