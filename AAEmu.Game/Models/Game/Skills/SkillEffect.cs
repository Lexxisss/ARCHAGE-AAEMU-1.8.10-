using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

public class SkillEffect
{
    public uint Id { get; set; }
    public uint EffectId { get; set; }
    public EffectTemplate Template { get; set; }
    public int Weight { get; set; }
    public byte StartLevel { get; set; }
    public byte EndLevel { get; set; }
    public bool Friendly { get; set; }
    public bool NonFriendly { get; set; }
    public uint TargetBuffTagId { get; set; }
    public uint TargetNoBuffTagId { get; set; }
    public uint SourceBuffTagId { get; set; }
    public uint SourceNoBuffTagId { get; set; }
    public int Chance { get; set; }
    public int StartCastingUseChance { get; set; }
    public int EndCastingUseChance { get; set; }
    public int StartCombatResource { get; set; }
    public int EndCombatResource { get; set; }
    public uint TargetCombatResourceId { get; set; }
    public bool ExecuteEffectOnFire { get; set; }
    public int SourceBuffStackCountMin { get; set; }
    public int SourceBuffStackCountMax { get; set; }
    public int TargetBuffStackCountMin { get; set; }
    public int TargetBuffStackCountMax { get; set; }
    public int SourceExceptBuffStackCountMin { get; set; }
    public int SourceExceptBuffStackCountMax { get; set; }
    public int TargetExceptBuffStackCountMin { get; set; }
    public int TargetExceptBuffStackCountMax { get; set; }
    public uint SynergyTextId { get; set; }
    public bool Front { get; set; }
    public bool Back { get; set; }
    public uint TargetNpcTagId { get; set; }
    public SkillEffectApplicationMethod ApplicationMethod { get; set; }
    public bool ConsumeSourceItem { get; set; }
    public uint ConsumeItemId { get; set; }
    public int ConsumeItemCount { get; set; }
    public bool AlwaysHit { get; set; }
    public uint ItemSetId { get; set; }
    public bool InteractionSuccessHit { get; set; }
    public bool CheckNoSourceTagSrc { get; set; }
    public bool CheckNoTargetTagSrc { get; set; }
    public bool CheckSourceTagSrc { get; set; }
    public bool CheckTargetTagSrc { get; set; }
}
