using System;
using System.Collections.Generic;
using System.Numerics;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Summary of buildings, as the client's ownership and tax lists consume them.
/// </summary>
/// <remarks>
/// This is what tells a player which buildings are theirs. The handler sorts records into
/// ownership and permission lists, skips duplicates by handle, and for its own sentinel
/// condition asks the server for that building's tax - so without this message the client
/// never learns it owns anything and never asks about tax at all.
///
/// It replaces the per-building message we used to send, which does not exist in this client.
///
/// The client caps this at twenty records and ignores the rest, so larger sets have to be
/// split across messages.
/// </remarks>
public class SCHouseDataPacket : GamePacket
{
    /// <summary>The client processes at most this many records per message.</summary>
    public const int MaxRecords = 20;

    private readonly IReadOnlyList<House> _houses;

    public SCHouseDataPacket(IReadOnlyList<House> houses) : base(SCOffsets.SCHouseDataPacket, 5)
    {
        _houses = houses ?? Array.Empty<House>();
    }

    public SCHouseDataPacket(House house) : this(house == null ? Array.Empty<House>() : new[] { house })
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(_houses.Count, MaxRecords);
        stream.Write((byte)count); // count : u8

        for (var i = 0; i < count; i++)
        {
            var house = _houses[i];
            var ownerName = NameManager.Instance.GetCharacterName(house.OwnerId) ?? string.Empty;

            stream.Write((uint)house.TlId);      // tl         : u32
            stream.Write((long)house.Id);        // type/id    : i64
            stream.WriteBc(house.ObjId);         // bc         : 3 bytes
            stream.Write(house.AccountId);       // accountId  : u64
            stream.Write(ownerName);             // owner      : string, max 128
            WriteWorldPosition(stream, house.Transform.World.Position);
            stream.Write((int)house.TemplateId); // type2      : i32
            stream.Write((byte)house.Permission);// permission : u8
            stream.Write(house.Name ?? string.Empty); // house : string, max 128
        }

        return stream;
    }

    /// <summary>The 20-byte world position this subsystem uses: <c>i64 x, i64 y, f32 z</c>.</summary>
    private static void WriteWorldPosition(PacketStream stream, Vector3 position)
    {
        stream.Write(Helpers.ConvertLongX(position.X));
        stream.Write(Helpers.ConvertLongY(position.Y));
        stream.Write(position.Z);
    }
}
