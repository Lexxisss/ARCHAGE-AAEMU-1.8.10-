using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRequestHouseTaxPacket : GamePacket
{
    public CSRequestHouseTaxPacket() : base(CSOffsets.CSRequestHouseTaxPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var tl = stream.ReadUInt16();
        var objId = stream.ReadBc();

        var tailLength = stream.Count - stream.Pos;
        if (tailLength > 0)
        {
            var tail = new byte[tailLength];
            for (var i = 0; i < tail.Length; i++)
                tail[i] = stream.ReadByte();
            var nonZeroPadding = Array.FindIndex(tail, value => value != 0);
            if (nonZeroPadding >= 0)
                Logger.Warn("RequestHouseTax unexpected padding {0} bytes: {1}", tail.Length, BitConverter.ToString(tail));
            else
                Logger.Trace("RequestHouseTax transport padding: {0} zero bytes", tail.Length);
        }

        Logger.Debug("RequestHouseTax, Tl: {0}, objId: {1}", tl, objId);
        HousingManager.Instance.HouseTaxInfo(Connection, tl, objId);
    }
}
