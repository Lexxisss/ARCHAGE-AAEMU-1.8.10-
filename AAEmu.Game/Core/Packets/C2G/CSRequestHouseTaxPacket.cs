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
        // The whole request, byte for byte. Two of these have now been read by the same code and
        // come out differently - one gave a sensible handle and object, the next a handle of four
        // billion - so the reading below is worth no more than a guess until the bytes have been
        // looked at rather than argued about.
        var raw = new byte[stream.Count - stream.Pos];
        for (var i = 0; i < raw.Length; i++)
            raw[i] = stream.ReadByte();
        Logger.Info("RequestHouseTax raw {0} bytes: {1}", raw.Length, BitConverter.ToString(raw));

        if (raw.Length < 5)
            return;

        // Two bytes for the handle, three for the object, and eight more that have been zero in
        // every request seen. Read as four the handle swallowed the front of the object and came
        // out in the billions; two bytes give a handle that follows the building clicked on and an
        // object in the range this world hands out.
        var tl = (ushort)(raw[0] | (raw[1] << 8));
        var objId = (uint)(raw[2] | (raw[3] << 8) | (raw[4] << 16));

        Logger.Debug($"RequestHouseTax, Tl: {tl}, objId: {objId}");

        HousingManager.Instance.HouseTaxInfo(Connection, tl, objId);
    }
}
