namespace AAEmu.Game.Models.Spheres;

public class SphereBuffs
{
    public uint Id { get; set; }
    public uint BuffId { get; set; }

    /// <summary>
    /// The buff to take away when the unit leaves. Zero means the sphere takes nothing back:
    /// the harbour's Moored buff names itself here, Ezi's Divine Protection names nothing.
    /// </summary>
    public uint RemoveOnLeaveBuffId { get; set; }

    /// <summary>
    /// Whether a unit's pet is given the same buff as its owner.
    /// </summary>
    public bool AndPet { get; set; }
}
