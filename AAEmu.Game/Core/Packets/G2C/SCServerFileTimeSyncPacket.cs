using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Synchronizes the world file-time clock after FinishState(7).
/// Target body: DateTime (8 bytes) + timezone bias in minutes (4 bytes).
/// </summary>
public class SCServerFileTimeSyncPacket : GamePacket
{
    private readonly DateTime _utcTime;
    private readonly int _timeZoneBias;

    public SCServerFileTimeSyncPacket()
        : base(SCOffsets.SCServerFileTimeSyncPacket, 5)
    {
        _utcTime = DateTime.UtcNow;
        _timeZoneBias = -(int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_utcTime);
        stream.Write(_timeZoneBias);
        return stream;
    }
}
