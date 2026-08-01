namespace AAEmu.Game.Models.Game.Crafts;

/*
    Material required in a craft.
*/
public class CraftMaterial
{
    public uint Id { get; set; }
    public uint CraftId { get; set; }
    public uint ItemId { get; set; }
    public int Amount { get; set; }
    public bool MainGrade { get; set; }

    /// <summary>
    /// Grade the material has to be at least. Kept even though nothing acts on it yet: dropping
    /// a content field is how a rule quietly stops existing.
    /// </summary>
    public int RequireGrade { get; set; }

    /// <summary>Whether a higher grade than required is accepted.</summary>
    public bool UpperGrade { get; set; }
}
