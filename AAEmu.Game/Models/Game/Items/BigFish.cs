using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class BigFish : Backpack
{
    public override ItemDetailType DetailType => ItemDetailType.BigFish;
    public override uint DetailBytesLength => 16;

    public float Weight { get; set; }
    public float Length { get; set; }
    public bool DetailWasMigrated { get; set; }

    public BigFish()
    {
    }

    public BigFish(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    public void UpdateDetailBytes()
    {
        var bytes = new byte[(int)DetailBytesLength];
        Buffer.BlockCopy(BitConverter.GetBytes(Weight), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(Length), 0, bytes, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(Helpers.UnixTime(CreateTime)), 0, bytes, 8, 8);
        Detail = bytes;
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;

        Weight = stream.ReadSingle();
        Length = stream.ReadSingle();
        CreateTime = stream.ReadDateTime();
        UpdateDetailBytes();
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(Weight);
        stream.Write(Length);
        stream.Write(CreateTime);
    }
}
