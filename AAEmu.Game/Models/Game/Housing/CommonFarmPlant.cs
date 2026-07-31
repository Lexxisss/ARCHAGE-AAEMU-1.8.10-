namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// One plant or plot on common farm land, as the client's farm list carries it.
/// </summary>
/// <remarks>
/// The two leading type fields share a generic label in the client and their individual
/// meanings are not established - one of them is expected to be the plant's template and the
/// other its instance or plot, but that has not been proven, so they are kept as-is rather
/// than named into something we would then have to trust.
/// </remarks>
public class CommonFarmPlant
{
    public int Type0 { get; set; }
    public int Type1 { get; set; }

    /// <summary>Growth state. Paired with <see cref="PlantTime"/> the client can run its own timer.</summary>
    public int Growing { get; set; }

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>When the plant was placed; the client times growth from this.</summary>
    public ulong PlantTime { get; set; }
}
