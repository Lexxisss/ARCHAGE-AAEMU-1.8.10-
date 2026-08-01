using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Why a recipe could not be carried out.
/// </summary>
/// <remarks>
/// One primary reason, then a list of additional ones of which the client reads no more than
/// twenty - the count drives its loop, so a longer list would leave it reading past the message.
/// Receiving this also stops the client's batch loop, which is how one refused cycle ends a run.
/// </remarks>
public class SCCraftFailedPacket : GamePacket
{
    /// <summary>The client reads no more additional reasons than this.</summary>
    public const int MaxAdditionalTypes = 20;

    private readonly uint _type;
    private readonly uint[] _additionalTypes;

    public SCCraftFailedPacket(uint type, uint[] additionalTypes = null)
        : base(SCOffsets.SCCraftFailedPacket, 5)
    {
        _type = type;
        _additionalTypes = additionalTypes ?? Array.Empty<uint>();
    }

    /// <summary>Kept for callers that pass a value and a repeat count rather than a list.</summary>
    public SCCraftFailedPacket(uint type, uint repeatedType, int count)
        : this(type, BuildRepeated(repeatedType, count))
    {
    }

    public override PacketStream Write(PacketStream stream)
    {
        var count = Math.Min(_additionalTypes.Length, MaxAdditionalTypes);

        stream.Write(_type);       // primary failure type
        stream.Write((uint)count); // count, at most 20
        for (var i = 0; i < count; i++)
            stream.Write(_additionalTypes[i]);

        return stream;
    }

    private static uint[] BuildRepeated(uint value, int count)
    {
        count = Math.Clamp(count, 0, MaxAdditionalTypes);
        var values = new uint[count];
        Array.Fill(values, value);
        return values;
    }
}
