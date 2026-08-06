using System;
using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game;

/// <summary>
/// One ship/vehicle equipment swap. Both items are snapshots of the two slots before the swap.
/// </summary>
public class SlaveEquipmentDelta
{
    public Item ItemAtSourceBefore { get; set; }
    public Item ItemAtDestinationBefore { get; set; }
    public SlotType SourceType { get; set; }
    public byte SourceIndex { get; set; }
    public SlotType DestType { get; set; }
    public byte DestIndex { get; set; }
    public long ExpireTime { get; set; }
}

/// <summary>
/// Equipment-change set used by both CSChangeSlaveEquipment and SCSlaveEquipmentChanged.
/// </summary>
/// <remarks>
/// Recovered target layout:
/// <c>ownerPersistentId:i64, tl:u16, dbSlaveId:u32, bts:bool, num:u8</c>, followed by at most
/// three records. Each record is <c>Item, Item, EquipSlot, EquipSlot, expireTime:i64</c>.
/// </remarks>
public class SlaveEquipment : PacketMarshaler
{
    public const int MaxRecords = 3;
    public const byte MaxSlotIndex = 34;

    public long OwnerPersistentId { get; set; }
    public ushort Tl { get; set; }
    public uint DbSlaveId { get; set; }
    public bool Bts { get; set; }
    public byte WireCount { get; private set; }
    public List<SlaveEquipmentDelta> Changes { get; } = new();

    public override void Read(PacketStream stream)
    {
        OwnerPersistentId = stream.ReadInt64();
        Tl = stream.ReadUInt16();
        DbSlaveId = stream.ReadUInt32();
        Bts = stream.ReadBoolean();

        var wireCount = stream.ReadByte();
        WireCount = wireCount;
        var count = Math.Min(wireCount, (byte)MaxRecords);

        for (var i = 0; i < wireCount; i++)
        {
            var first = new Item();
            first.Read(stream);
            var second = new Item();
            second.Read(stream);

            var sourceType = (SlotType)stream.ReadByte();
            var sourceIndex = stream.ReadByte();
            var destType = (SlotType)stream.ReadByte();
            var destIndex = stream.ReadByte();
            var expireTime = stream.ReadInt64();

            if (i >= count)
                continue;

            Changes.Add(new SlaveEquipmentDelta
            {
                ItemAtSourceBefore = first.TemplateId == 0 ? null : first,
                ItemAtDestinationBefore = second.TemplateId == 0 ? null : second,
                SourceType = sourceType,
                SourceIndex = sourceIndex,
                DestType = destType,
                DestIndex = destIndex,
                ExpireTime = expireTime
            });
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        var num = Math.Min(Changes.Count, MaxRecords);

        stream.Write(OwnerPersistentId);
        stream.Write(Tl);
        stream.Write(DbSlaveId);
        stream.Write(Bts);
        stream.Write((byte)num);

        for (var i = 0; i < num; i++)
        {
            var change = Changes[i];

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
