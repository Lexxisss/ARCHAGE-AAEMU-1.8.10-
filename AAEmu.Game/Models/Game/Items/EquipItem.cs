using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class EquipItem : Item
{
    public override ItemDetailType DetailType => ItemDetailType.Equipment;
    public override uint DetailBytesLength => 48;

    public virtual int Str => 0;
    public virtual int Dex => 0;
    public virtual int Sta => 0;
    public virtual int Int => 0;
    public virtual int Spi => 0;
    public virtual byte MaxDurability => 0;

    public int RepairCost
    {
        get
        {
            var template = (EquipItemTemplate)Template;
            var grade = ItemManager.Instance.GetGradeTemplate(Grade);
            var cost = ItemManager.Instance.GetDurabilityRepairCostFactor() * 0.0099999998f * (1f - Durability * 1f / MaxDurability) * template.Price;
            cost = cost * grade.RefundMultiplier * 0.0099999998f;
            cost = (float)Math.Ceiling(cost);
            if (cost < 0 || cost < int.MinValue || cost > int.MaxValue)
                cost = 0;
            return (int)cost;
        }
    }

    public EquipItem()
    {
        GemIds = new uint[18];
    }

    public EquipItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
        GemIds = new uint[18];
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        Durability = stream.ReadByte();       // durability
        ChargeCount = stream.ReadInt16();     // chargeCount
        ChargeTime = stream.ReadDateTime();   // chargeTime
        TemperPhysical = stream.ReadUInt16(); // scaledA
        TemperMagical = stream.ReadUInt16();  // scaledB
        ChargeProcTime = stream.ReadDateTime();
        MappingFailBonus = stream.ReadByte();
        ElementLevel = stream.ReadByte();
        var gemValues = stream.ReadPiscW(18);
        GemIds = Array.ConvertAll(gemValues, value => checked((uint)value));
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(Durability);     // durability
        stream.Write(ChargeCount);    // chargeCount
        stream.Write(ChargeTime);     // chargeTime
        stream.Write(TemperPhysical); // scaledA
        stream.Write(TemperMagical);  // scaledB
        stream.Write(ChargeProcTime);
        stream.Write(MappingFailBonus);
        stream.Write(ElementLevel);
        var gemValues = Array.ConvertAll(GemIds, value => (long)value);
        stream.WritePiscW(gemValues.Length, gemValues);
    }
}
