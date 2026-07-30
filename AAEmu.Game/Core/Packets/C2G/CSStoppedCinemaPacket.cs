using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Target 10.8.1 world-entry state-6 notification. Despite the legacy name,
/// the observed body is not empty: it starts with CharacterVisualOptions and
/// currently has a 16-byte target-only tail under reverse-engineering.
/// </summary>
public class CSStoppedCinemaPacket : GamePacket
{
    public CSStoppedCinemaPacket() : base(CSOffsets.CSStoppedCinemaPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var visualOptions = new CharacterVisualOptions();
        visualOptions.Read(stream);
        var targetTail = stream.ReadBytes(stream.LeftBytes);

        if (Connection.ActiveChar != null)
            Connection.ActiveChar.VisualOptions = visualOptions;

        Logger.Info(
            "CSStoppedCinema 0x114: captured target visual options, tailLen={0}, tail={1}",
            targetTail.Length,
            Convert.ToHexString(targetTail));
    }
}
