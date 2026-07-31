namespace AAEmu.Game.Models.Game.Skills.Templates;

/// <summary>Maps an ability branch to its primary and secondary combat resources.</summary>
public sealed class CombatResourceGroupTemplate
{
    public uint Id { get; set; }
    public byte AbilityId { get; set; }
    public uint CombatResource1Id { get; set; }
    public uint CombatResource2Id { get; set; }
    public uint CombatResource1UiId { get; set; }
    public uint CombatResource2UiId { get; set; }
    public bool DependentResource1 { get; set; }
    public bool DependentResource2 { get; set; }
    public uint ChangeCombatResource1ConditionId { get; set; }
    public uint ChangeCombatResource2ConditionId { get; set; }
    public uint ChangeCombatResource1Id { get; set; }
    public uint ChangeCombatResource2Id { get; set; }
    public int ShowUpdateTimeCombatResource { get; set; }
    public int ShowUpdateTimeTransformCombatResource { get; set; }
}
