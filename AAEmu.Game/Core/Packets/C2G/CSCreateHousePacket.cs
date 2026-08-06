using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCreateHousePacket : GamePacket
{
    public CSCreateHousePacket() : base(CSOffsets.CSCreateHousePacket, 5)
    {
        //
    }

    /// <summary>
    /// House placement request. The decoded field prefix is 48 bytes:
    ///
    ///     designType:i32, x:i64, y:i64, z:f32, zRot:f32, item:u64, moneyAmount:i64, ht:i32
    ///
    /// The money field is 64 bits, not 32. Reading it as an int left everything after it
    /// shifted by four, so <c>ht</c> picked up the high half of the money and a ninth field
    /// was invented past the end of the decoded fields to absorb the difference. Any remaining
    /// bytes are transport padding and are consumed without assigning them invented semantics.
    /// </summary>
    public override void Read(PacketStream stream)
    {
        var designId = stream.ReadUInt32();
        var x = Helpers.ConvertLongX(stream.ReadInt64());
        var y = Helpers.ConvertLongY(stream.ReadInt64());
        var z = stream.ReadSingle();
        var zRot = stream.ReadSingle();
        var itemId = stream.ReadUInt64();
        var moneyAmount = stream.ReadInt64();
        var ht = stream.ReadInt32();

        var unknownTailLength = stream.Count - stream.Pos;
        if (unknownTailLength > 0)
        {
            var unknownTail = new byte[unknownTailLength];
            for (var i = 0; i < unknownTail.Length; i++)
                unknownTail[i] = stream.ReadByte();
            Logger.Debug("{0} transport padding ({1} bytes): {2}",
                nameof(CSCreateHousePacket),
                unknownTail.Length,
                BitConverter.ToString(unknownTail));
        }

        Logger.Debug($"CreateHouse, Id: {designId}, X: {x}, Y: {y}, Z: {z}, ZRot: {zRot}, Money: {moneyAmount}, Ht: {ht}");

        HousingManager.Instance.Build(
            Connection,
            designId, x, y, z, zRot,
            itemId, moneyAmount, ht
        );
    }
}
