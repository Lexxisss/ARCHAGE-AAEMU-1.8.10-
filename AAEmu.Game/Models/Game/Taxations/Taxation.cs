namespace AAEmu.Game.Models.Game.Taxations;

public class Taxation
{
    public uint Id { get; set; }
    public uint Tax { get; set; }
    public bool Show { get; set; }

    /// <summary>
    /// How many tax certificates one period of this tax costs.
    /// </summary>
    /// <remarks>
    /// Authored beside the money amount rather than derived from it - the two do not stand in any
    /// fixed ratio, so no single divisor turns one into the other. Three of them were invented at
    /// various times and none was right.
    /// </remarks>
    public uint SealCount { get; set; }
}
