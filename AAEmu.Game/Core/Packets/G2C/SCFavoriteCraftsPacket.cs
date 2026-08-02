using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The player's complete set of pinned recipes.
/// </summary>
/// <remarks>
/// This is not a delta - the client replaces its whole cache from it, so it has to carry
/// everything the player has pinned, every time.
/// </remarks>
public class SCFavoriteCraftsPacket : GamePacket
{
    private readonly uint[] _craftIds;

    public SCFavoriteCraftsPacket(uint[] craftIds) : base(SCOffsets.SCFavoriteCraftsPacket, 5)
    {
        _craftIds = craftIds ?? Array.Empty<uint>();
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)_craftIds.Length); // count
        foreach (var craftId in _craftIds)
            stream.Write(craftId);            // crafts.id

        return stream;
    }
}
