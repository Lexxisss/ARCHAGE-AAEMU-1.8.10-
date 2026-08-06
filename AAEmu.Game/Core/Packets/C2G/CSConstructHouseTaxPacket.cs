using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSConstructHouseTaxPacket : GamePacket
{
    public CSConstructHouseTaxPacket() : base(CSOffsets.CSConstructHouseTaxPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var designId = stream.ReadUInt32(); // type(id)
        var x = Helpers.ConvertLongX(stream.ReadInt64());
        var y = Helpers.ConvertLongY(stream.ReadInt64());
        var z = stream.ReadSingle();

        var unknownTailLength = stream.Count - stream.Pos;
        if (unknownTailLength > 0)
        {
            var unknownTail = new byte[unknownTailLength];
            for (var i = 0; i < unknownTail.Length; i++)
                unknownTail[i] = stream.ReadByte();
            Logger.Debug("{0} transport padding ({1} bytes): {2}",
                nameof(CSConstructHouseTaxPacket),
                unknownTail.Length,
                BitConverter.ToString(unknownTail));
        }

        Logger.Debug("ConstructHouseTax");
        HousingManager.Instance.ConstructHouseTax(Connection, designId, x, y, z);
    }
}
