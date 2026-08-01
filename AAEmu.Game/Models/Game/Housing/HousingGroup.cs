using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// The rules a plot of land is governed by: what may be built on it, and by whom.
/// </summary>
public class HousingGroup
{
    public uint Id { get; set; }
    public string Name { get; set; }

    /// <summary>Weeks of unpaid tax the group tolerates before the building is at risk.</summary>
    public int AllowedTaxDelayWeek { get; set; }

    public bool CanExtend { get; set; }

    /// <summary>Only a player who owns no building at all may build here.</summary>
    public bool Houseless { get; set; }

    /// <summary>
    /// When set, only a player who already owns a building of this category may build here - the
    /// group exists as an annex to something they own rather than on its own.
    /// </summary>
    public uint ExistingCategoryId { get; set; }

    /// <summary>
    /// The building categories this group accepts, each with how many of it one player may raise
    /// in the group. Zero means no limit of its own.
    /// </summary>
    public Dictionary<uint, int> AllowedCategories { get; } = new();
}
