namespace AAEmu.Game.Models.Game.Skills.Templates;

/// <summary>
/// Descriptor from combat_resources. RecoveryCycle is expressed in milliseconds.
/// </summary>
public sealed class CombatResourceTemplate
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public uint BuffId { get; set; }
    public long DefaultPoint { get; set; }
    public long MaxPoint { get; set; }
    public int RecoveryCycle { get; set; }
    public long CombatRecoveryAmount { get; set; }
    public long PeaceRecoveryAmount { get; set; }
    public long EtcRecoveryAmount { get; set; }
    public uint EtcRecoveryStateId { get; set; }
    public uint SendTypeId { get; set; }
    public uint ResourceBuffConditionId { get; set; }
}
