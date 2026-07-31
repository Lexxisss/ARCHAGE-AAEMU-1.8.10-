using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game;

/// <summary>
/// One equipment change on a ship or land vehicle: the item leaving a slot, the item arriving,
/// and where each came from.
/// </summary>
/// <remarks>
/// The items use the ordinary item encoding, not a slave-specific one.
/// </remarks>
public class SlaveEquipmentDelta
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
/// A set of equipment changes for one slave, as both directions carry it.
/// </summary>
/// <remarks>
/// The client clamps the record count to three; anything beyond that is discarded rather than
/// rejected, so the server has to split larger changes across messages itself.
///
/// This used to be a fixed pair of items with no record count at all, and its owner id was
/// 32 bits where the wire has 64.
/// </remarks>
public class SlaveEquipment : PacketMarshaler
{
    /// <summary>The client caps the record count here.</summary>
    public const int MaxRecords = 3;

    /// <summary>Persistent character id of the owner, not a world object id.</summary>
    public long OwnerPersistentId { get; set; }

    public ushort Tl { get; set; }
    public uint DbSlaveId { get; set; }

    /// <summary>Exact meaning unresolved; carried through unchanged.</summary>
    public bool Bts { get; set; }

    public List<SlaveEquipmentDelta> Changes { get; } = new();

    public override PacketStream Write(PacketStream stream)
    {
        var num = Math.Min(Changes.Count, MaxRecords);

        stream.Write(OwnerPersistentId); // ownerPersistentId : i64
        stream.Write(Tl);                // tl                : u16
        stream.Write(DbSlaveId);         // dbSlaveId         : u32
        stream.Write(Bts);               // bts               : bool
        stream.Write((byte)num);         // num               : u8, clamped to 3 by the client

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
