using System;

namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// One plot of land a building may stand on.
/// </summary>
/// <remarks>
/// The record carries no shape. Where a plot begins and ends lives in the client's own world
/// data, which is why the client can draw the grid and refuse a placement before it sends
/// anything, and why the server can only tell which zone a plot belongs to. What is here is the
/// part that matters for a decision: whether the plot is open at all, and which group of rules
/// it is governed by.
///
/// <see cref="Name"/> is a zone name - the same string zones carry - so all the plots of a zone
/// are the ones sharing its name.
/// </remarks>
public class HousingAreas
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint GroupId { get; set; }

    /// <summary>Whether the plot is in use at all. A plot that is not stays empty for everyone.</summary>
    public bool Activated { get; set; }

    /// <summary>
    /// When the plot opens, or <see cref="DateTime.MinValue"/> for one that was never scheduled.
    /// Every date in the shipped data is long past; the check is here because the field is.
    /// </summary>
    public DateTime OpensAt { get; set; }

    public bool IsOpen(DateTime now) => Activated && OpensAt <= now;
}
