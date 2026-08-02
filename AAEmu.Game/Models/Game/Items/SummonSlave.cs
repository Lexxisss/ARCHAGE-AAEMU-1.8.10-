using System;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class SummonSlave : Item
{
    private DateTime _repairStartTime;
    public override ItemDetailType DetailType => ItemDetailType.Slave;

    /// <summary>
    /// The client reads a fixed 33 bytes after the detail discriminator for this variant.
    /// </summary>
    /// <remarks>
    /// The old figure of 29 came from the branch that writes no repair time: it wrote a bare
    /// int where the other branch writes a full timestamp, so the record's length depended on
    /// whether the vehicle happened to be under repair. Every item after a wrecked one in the
    /// same message was read four bytes out of place.
    /// </remarks>
    public override uint DetailBytesLength => 33;

    public byte SlaveType { get; set; } // Not sure about this, captures show 2 here
    public uint SlaveDbId { get; set; }
    public byte IsDestroyed { get; set; }

    public DateTime RepairStartTime
    {
        get => _repairStartTime;
        set
        {
            _repairStartTime = value;
            if (value > DateTime.MinValue)
                IsDestroyed = 0;
        }
    }

    // TODO: Actually use this location for saving the data in ItemDetails
    public Vector3 SummonLocation { get; set; }

    public SummonSlave()
    {
    }

    public SummonSlave(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        SlaveType = stream.ReadByte(); // Type? (2 = slave?)
        SlaveDbId = stream.ReadBc();   // DbId
        IsDestroyed = stream.ReadByte();

        var repairStart = stream.ReadInt64(); // repairStartTime, a timestamp like every other one
        RepairStartTime = repairStart == 0 ? DateTime.MinValue : Helpers.UnixTime(repairStart);

        _ = stream.ReadInt32();        // recovery counter; anything but zero reads as recovering
        _ = stream.ReadBytes(16);      // summon-location constraint, not decoded
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(SlaveType);
        stream.WriteBc(SlaveDbId);
        stream.Write(IsDestroyed);

        // Both branches have to be the same width - this is a timestamp field, and an empty one
        // is a zero timestamp, not a shorter record.
        if (RepairStartTime == DateTime.MinValue)
            stream.Write(0L);
        else
            stream.Write(RepairStartTime);

        stream.Write(0); // If this is anything besides 0, it will count as being in recovering (negative at that)

        // The following 16 bytes somehow determine where a Vehicle is allowed to be summoned
        // TODO: Get real live data capture of this value being set
        // TODO: Get this from having a vehicle out when maintenance starts
        stream.Write(0);
        stream.Write(0);
        stream.Write(0);
        stream.Write(0);
    }
}
