using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Which quests the player has already finished.
/// </summary>
/// <remarks>
/// The client keeps these apart from the quests it is currently carrying, in a list of its own, and
/// consults it to decide whether someone still has anything left to offer. A quest missing from it
/// is a quest the giver keeps advertising - and, since the server knows better and refuses, one
/// nobody can take either. That is what this message caused while it carried something else.
///
/// It is a plain list: how many, then each quest's own number and when it was finished. What used
/// to go out was the shape we store it in rather than the shape it is read in - a block number
/// standing for sixty-four quests and a mask of which of them are done - so the client took a block
/// number for a quest number and a mask for a date, and learned nothing true from either.
///
/// The client holds two hundred at a time, so a longer list has to be split across messages.
/// </remarks>
public class SCCompletedQuestsPacket : GamePacket
{
    /// <summary>Most quests the client will keep from one of these.</summary>
    public const int MaxEntries = 200;

    private readonly uint[] _questIds;

    public SCCompletedQuestsPacket(uint[] questIds) : base(SCOffsets.SCCompletedQuestsPacket, 5)
    {
        _questIds = questIds;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var count = _questIds.Length <= MaxEntries ? _questIds.Length : MaxEntries;

        stream.Write(count);            // count : i32
        for (var i = 0; i < count; i++)
        {
            stream.Write(_questIds[i]); // questId     : u32
            // When it was finished. We keep only the fact, not the date, so this goes out empty
            // rather than invented: anything reckoning from it reads the quest as finished long
            // ago, which is the harmless direction.
            stream.Write(0UL);          // completedAt : u64
        }

        return stream;
    }
}
