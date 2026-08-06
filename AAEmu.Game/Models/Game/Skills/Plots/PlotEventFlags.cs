using System;

namespace AAEmu.Game.Models.Game.Skills.Plots;

/// <summary>
/// Bit field serialized by SCPlotEventPacket (0x0338).
/// </summary>
/// <remarks>
/// Target x2game.dll 0x399EBBA8-0x399EBC3F unpacks these four bits into
/// independent booleans. The client plot handler at 0x397DEE10 uses bit 1
/// to execute the event effects and bit 2 to commit the last event.
/// </remarks>
[Flags]
public enum PlotEventFlags : byte
{
    None = 0,

    /// <summary>
    /// A casting reference and casting duration are present.
    /// </summary>
    Casting = 1 << 0,

    /// <summary>
    /// The plot-event conditions succeeded and the client may execute its effects.
    /// </summary>
    ConditionOk = 1 << 1,

    /// <summary>
    /// This is the final event in the current plot branch.
    /// </summary>
    Last = 1 << 2,

    /// <summary>
    /// The packet contains the fixed block of thirteen signed runtime plot values.
    /// The target client does not use this block as Leap-controller geometry.
    /// </summary>
    HasValues = 1 << 3
}
