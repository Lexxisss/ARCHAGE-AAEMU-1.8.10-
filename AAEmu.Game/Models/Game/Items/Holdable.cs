using AAEmu.Game.Models.Game.Formulas;

namespace AAEmu.Game.Models.Game.Items;

public class Holdable
{
    public uint Id { get; set; }
    public uint KindId { get; set; }
    public int Speed { get; set; }
    public int ExtraDamagePierceFactor { get; set; }
    public int ExtraDamageSlashFactor { get; set; }
    public int ExtraDamageBluntFactor { get; set; }
    public int MaxRange { get; set; }
    public int Angle { get; set; }
    public int EnchantedDps1000 { get; set; }
    public uint SlotTypeId { get; set; }
    public int DamageScale { get; set; }
    public Formula FormulaDps { get; set; }
    public Formula FormulaMDps { get; set; }
    public Formula FormulaArmor { get; set; }
    public Formula FormulaHDps { get; set; }
    public int MinRange { get; set; }
    public int SheathePriority { get; set; }
    public float DurabilityRatio { get; set; }
    public int RenewCategory { get; set; }
    public int ItemProcId { get; set; }
    public int StatMultiplier { get; set; }

    /// <summary>
    /// Swing animation ids for this weapon type, and the percentage weights that pick
    /// between the first two. The third entry is the remaining/fallback variant.
    /// These are what the client accepts as a fireAnimId for a melee attack - the hardcoded
    /// tables another build used are simply rows 0, 1 and 3 of this table copied by hand.
    /// </summary>
    public int AnimL1Id { get; set; }
    public int AnimL1Ratio { get; set; }
    public int AnimL2Id { get; set; }
    public int AnimL2Ratio { get; set; }
    public int AnimL3Id { get; set; }
    public int AnimR1Id { get; set; }
    public int AnimR1Ratio { get; set; }
    public int AnimR2Id { get; set; }
    public int AnimR2Ratio { get; set; }
    public int AnimR3Id { get; set; }
}
