using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Models.Game.Taxations;

namespace AAEmu.Game.Models.Game.Housing;

public class HousingTemplate
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint CategoryId { get; set; }
    public uint MainModelId { get; set; }
    public uint DoorModelId { get; set; }
    public uint StairModelId { get; set; }
    public bool AutoZ { get; set; }
    public bool GateExists { get; set; }
    public int Hp { get; set; }
    public uint RepairCost { get; set; }
    /// <summary>The size row this design points at; the garden radius comes from it.</summary>
    public uint HousingSizeId { get; set; }
    public float GardenRadius { get; set; }
    public string Family { get; set; }
    public Taxation Taxation { get; set; }
    public uint GuardTowerSettingId { get; set; }
    public float CinemaRadius { get; set; }
    public float AutoZOffsetX { get; set; }
    public float AutoZOffsetY { get; set; }
    public float AutoZOffsetZ { get; set; }
    public float Alley { get; set; }
    public float ExtraHeightAbove { get; set; }
    public float ExtraHeightBelow { get; set; }
    public uint DecoLimit { get; set; }
    public uint AbsoluteDecoLimit { get; set; }
    public uint HousingDecoLimitId { get; set; }
    public bool IsSellable { get; set; }
    public bool HeavyTax { get; set; }
    public bool AlwaysPublic { get; set; }

    /// <summary>Number of decal slots every building carries in its state block.</summary>
    public const int UccSlotCount = 5;

    /// <summary>What turning a building of this design costs, and how much of it.</summary>
    public uint RotateItemId { get; set; }
    public int RotateItemCount { get; set; }

    /// <summary>
    /// The kind of decal each of the five slots takes, in the order the state block carries them:
    /// floor, outer wall, roof, top, wall.
    /// </summary>
    /// <remarks>
    /// The design names its five kinds separately and the state block carries five anonymous
    /// records, so which record is which surface is an assumption - the order the fields are
    /// declared in. Nothing turns on it until a decal is actually applied, because the slot's own
    /// identity is sent alongside and stays empty while the server has nowhere to keep one.
    /// </remarks>
    public int[] UccKinds { get; } = new int[UccSlotCount];

    /// <summary>Scale of each slot, in the same order as <see cref="UccKinds"/>.</summary>
    public int[] UccScales { get; } = new int[UccSlotCount];

    public Dictionary<int, HousingBuildStep> BuildSteps { get; set; }
    public HousingBindingDoodad[] HousingBindingDoodad { get; set; }

    /// <summary>
    /// The stage a building of this design starts at, or -1 for a design that is finished the
    /// moment it is placed.
    /// </summary>
    /// <remarks>
    /// The stage is the ordinal the design's own rows carry, which is not the same as their
    /// position in the table - taking zero for granted finishes a building on the spot whenever
    /// the design happens to number its stages from one.
    /// </remarks>
    public int FirstBuildStep => BuildSteps.Count == 0 ? -1 : BuildSteps.Keys.Min();

    public HousingTemplate()
    {
        BuildSteps = new Dictionary<int, HousingBuildStep>();
    }
}
