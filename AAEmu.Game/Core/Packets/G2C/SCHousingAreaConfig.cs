using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// When each housing area opens for building.
/// </summary>
/// <remarks>
/// The client keeps this as a map and consults it before it will even send a placement request:
/// it finds the area's key for where the player is standing, and if that entry names a date it
/// refuses until that date has passed, showing its own "area not activated" message. So a wrong
/// entry here stops a building from ever being attempted, silently as far as the server is
/// concerned.
///
/// A year of zero means no date restriction at all, which is what an always-open area wants. We
/// were sending the current year and the current time, which only worked because that moment had
/// just passed.
///
/// It carries no geometry. Where the plots are comes from the client's own world data; this only
/// says when they are open.
/// </remarks>
public class SCHousingAreaConfig : GamePacket
{
    private readonly bool _protectOwner;
    private readonly DateTime _time;
    private readonly int _year;
    private readonly int _month;
    private readonly int _day;
    private readonly int _hour;
    private readonly int _min;

    /// <param name="protectOwner">Stored by the client, but nothing in this version reads it.</param>
    /// <param name="opensAt">
    /// When the area opens, or <see cref="DateTime.MinValue"/> for an area that is always open -
    /// which sends a year of zero and switches the client's date check off entirely.
    /// </param>
    public SCHousingAreaConfig(bool protectOwner, DateTime opensAt) : base(SCOffsets.SCHousingAreaConfig, 5)
    {
        _protectOwner = protectOwner;

        if (opensAt == DateTime.MinValue)
        {
            _time = DateTime.MinValue;
            _year = 0;
            return;
        }

        _time = opensAt;
        _year = opensAt.Year;
        _month = opensAt.Month;
        _day = opensAt.Day;
        _hour = opensAt.Hour;
        _min = opensAt.Minute;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(1u);            // count : u32, one entry follows

        // One entry, 33 bytes. The key names which area it applies to; zero is the default the
        // client falls back to when it finds no exact match, which is what makes a single entry
        // cover the whole world.
        stream.Write(0);             // key            : i32
        stream.Write(_protectOwner); // protectOwner   : bool, stored but unread in this version
        stream.Write(_time);         // activationTime : u64
        stream.Write(_year);         // i32, zero switches the date check off
        stream.Write(_month);        // i32
        stream.Write(_day);          // i32
        stream.Write(_hour);         // i32
        stream.Write(_min);          // i32

        return stream;
    }
}
