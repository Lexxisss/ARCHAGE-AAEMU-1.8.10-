using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Recipes the player just pinned and unpinned, as two independent lists.
/// </summary>
/// <remarks>
/// The client does not send this at all when both lists are empty, and holds its own state pending
/// until it hears back. Each list is capped on its side at thirty; the cap is enforced here too,
/// because the count drives the read.
/// </remarks>
public class CSUpdateFavoriteCraftsPacket : GamePacket
{
    public CSUpdateFavoriteCraftsPacket() : base(CSOffsets.CSUpdateFavoriteCraftsPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var added = ReadCraftList(stream);
        var removed = ReadCraftList(stream);

        Logger.Debug("UpdateFavoriteCrafts, added: {0}, removed: {1}", added.Count, removed.Count);

        Connection.ActiveChar?.FavoriteCrafts?.Update(added, removed);
    }

    private static List<uint> ReadCraftList(PacketStream stream)
    {
        var count = stream.ReadUInt32();
        if (count > CharacterFavoriteCrafts.MaxPerRequest)
            count = CharacterFavoriteCrafts.MaxPerRequest;

        var crafts = new List<uint>((int)count);
        for (var i = 0u; i < count; i++)
            crafts.Add(stream.ReadUInt32());

        return crafts;
    }
}
