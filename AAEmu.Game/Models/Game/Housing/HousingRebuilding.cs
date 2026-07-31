namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// One available conversion of an existing building into another design.
/// </summary>
/// <remarks>
/// A rebuild is identified by the pair of the skill used and the design being converted to,
/// which is why both are kept here rather than only the target design.
/// </remarks>
public class HousingRebuilding
{
    public uint Id { get; set; }

    /// <summary>The design this rebuild produces.</summary>
    public uint HousingId { get; set; }

    /// <summary>The skill that offers this rebuild.</summary>
    public uint SkillId { get; set; }

    public uint ActabilityGroupId { get; set; }
    public int LaborPower { get; set; }
    public string Name { get; set; }
    public string ChangePointDesc { get; set; }
}

/// <summary>One material a rebuild consumes.</summary>
public class HousingRebuildingMaterial
{
    public uint Id { get; set; }
    public uint HousingRebuildingId { get; set; }
    public uint ItemId { get; set; }
    public int Count { get; set; }
}
