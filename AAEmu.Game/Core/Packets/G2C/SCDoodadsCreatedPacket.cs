using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Target 10.8 SC_DOODADS_CREATED (0x0198): UInt8 count followed by
/// count variable-length DoodadInfo records. The target client buffer contains
/// room for 30 records, so a packet must never exceed 30.
/// </summary>
public class SCDoodadsCreatedPacket : GamePacket
{
    private readonly Doodad[] _doodads;
    public const int MaxCountPerPacket = 30;

    public SCDoodadsCreatedPacket(Doodad[] doodads)
        : base(SCOffsets.SCDoodadsCreatedPacket, 5)
    {
        ArgumentNullException.ThrowIfNull(doodads);
        if (doodads.Length > MaxCountPerPacket)
            throw new ArgumentOutOfRangeException(
                nameof(doodads),
                doodads.Length,
                $"SC_DOODADS_CREATED target limit is {MaxCountPerPacket}");

        _doodads = doodads;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_doodads.Length);
        foreach (var doodad in _doodads)
        {
            if (doodad == null)
                throw new InvalidOperationException("SC_DOODADS_CREATED contains a null DoodadInfo record");
            doodad.Write(stream);
        }

        return stream;
    }
}
