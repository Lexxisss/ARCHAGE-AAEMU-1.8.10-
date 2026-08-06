using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>One target-client completed-quest bitset record.</summary>
public readonly struct CompletedQuestBlock
{
    public CompletedQuestBlock(uint index, ulong body)
    {
        Index = index;
        Body = body;
    }

    public uint Index { get; }
    public ulong Body { get; }
}

/// <summary>
/// Target 10.8 completed quest state. The client consumes the same 64-quest
/// blocks used by the character database: count, then idx/body records.
/// </summary>
public sealed class SCCompletedQuestsPacket : GamePacket
{
    public const int MaxEntries = 200;

    private readonly CompletedQuestBlock[] _blocks;

    public SCCompletedQuestsPacket(CompletedQuestBlock[] blocks)
        : base(SCOffsets.SCCompletedQuestsPacket, 5)
    {
        _blocks = blocks ?? [];
    }

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(_blocks.Length, MaxEntries);
        stream.Write(count); // count : i32
        for (var i = 0; i < count; i++)
        {
            stream.Write(_blocks[i].Index); // idx  : u32
            stream.Write(_blocks[i].Body);  // body : u64, one bit per quest
        }

        return stream;
    }
}
