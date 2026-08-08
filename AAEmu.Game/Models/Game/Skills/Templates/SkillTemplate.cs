using System.Collections.Generic;
using AAEmu.Game.Models.Game.Animation;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class SkillTemplate
{
    public uint Id { get; set; }
    public int Cost { get; set; }
    public bool Show { get; set; }
    public uint StartAnimId { get; set; }
    public uint FireAnimId { get; set; }
    public uint ChannelingAnimId { get; set; }
    public uint TwoHandFireAnimId { get; set; }
    public uint DualWieldFireAnimId { get; set; }
    public uint StringInstrumentFireAnimId { get; set; }
    public uint StringInstrumentStartAnimId { get; set; }
    public uint PercussionInstrumentFireAnimId { get; set; }
    public uint PercussionInstrumentStartAnimId { get; set; }
    public uint TubeInstrumentFireAnimId { get; set; }
    public uint TubeInstrumentStartAnimId { get; set; }
    public uint ShotGunFireAnimId { get; set; }
    public uint ShotGunStartAnimId { get; set; }
    public Anim FireAnim { get; set; }
    public byte AbilityId { get; set; }
    public int ManaCost { get; set; }
    public int TimingId { get; set; }
    public uint CooldownTime { get; set; }
    public int CastingTime { get; set; }
    public bool IgnoreGlobalCooldown { get; set; }
    public int EffectDelay { get; set; }
    public float EffectSpeed { get; set; }
    public int EffectRepeatCount { get; set; }
    public int EffectRepeatTick { get; set; }
    public int ActiveWeaponId { get; set; }
    public SkillTargetType TargetType { get; set; }
    public SkillTargetSelection TargetSelection { get; set; }
    public SkillTargetRelation TargetRelation { get; set; }
    public int TargetAreaCount { get; set; }
    public int TargetAreaRadius { get; set; }
    public bool TargetSiege { get; set; }
    public int WeaponSlotForAngleId { get; set; }
    public int TargetAngle { get; set; }
    public int WeaponSlotForRangeId { get; set; }
    public int MinRange { get; set; }
    public int MaxRange { get; set; }
    public bool KeepStealth { get; set; }
    public int Aggro { get; set; }
    public int ChannelingTime { get; set; }
    public int ChannelingTick { get; set; }
    public int ChannelingMana { get; set; }
    public uint ChannelingTargetBuffId { get; set; }
    public int TargetAreaAngle { get; set; }
    public int AbilityLevel { get; set; }
    public uint ChannelingDoodadId { get; set; }
    public int CooldownTagId { get; set; }
    public uint SkillControllerId { get; set; }
    public int RepeatCount { get; set; }
    public int RepeatTick { get; set; }
    public uint ToggleBuffId { get; set; }
    public uint ChannelingBuffId { get; set; }
    public int ReagentCorpseStatusId { get; set; }
    public int LevelStep { get; set; }
    public float ValidHeight { get; set; }
    public float TargetValidHeight { get; set; }
    public bool AutoLearn { get; set; }
    public bool NeedLearn { get; set; }
    public uint MainhandToolId { get; set; }
    public uint OffhandToolId { get; set; }
    public int FrontAngle { get; set; }
    public float ManaLevelMd { get; set; }
    public bool Unmount { get; set; }
    public uint DamageTypeId { get; set; }
    public bool AllowToPrisoner { get; set; }
    public uint MilestoneId { get; set; }
    public bool MatchAnimation { get; set; }
    public bool MatchAnimationCount { get; set; }
    public bool CanActiveWeaponWithoutAnim { get; set; }
    public Plot Plot { get; set; }
    public bool UseAnimTime { get; set; }
    public int ConsumeLaborPower { get; set; }
    public bool SourceStun { get; set; }
    public int CastingInc { get; set; }
    public bool CastingCancelable { get; set; }
    public bool CastingDelayable { get; set; }
    public bool ChannelingCancelable { get; set; }
    public float TargetOffsetAngle { get; set; }
    public float TargetOffsetDistance { get; set; }
    public int ActabilityGroupId { get; set; }
    public bool PlotOnly { get; set; }
    public bool SkillControllerAtEnd { get; set; }
    public bool EndSkillController { get; set; }
    public bool OrUnitReqs { get; set; }
    public bool DefaultGcd { get; set; }
    public bool KeepManaRegen { get; set; }
    public int CrimePoint { get; set; }
    public bool LevelRuleNoConsideration { get; set; }
    public bool UseWeaponCooldownTime { get; set; }
    public int CombatDiceId { get; set; }
    public uint CombatResourceId { get; set; }
    public int MinCombatResource { get; set; }
    public int MaxCombatResource { get; set; }
    public bool CheckObstacle { get; set; }
    public float PitchAngle { get; set; }
    public bool ValidHeightEdgeToEdge { get; set; }
    public int CustomGcd { get; set; }
    public bool CancelOngoingBuffs { get; set; }
    public uint CancelOngoingBuffExceptionTagId { get; set; }
    public bool SourceCannotUseWhileWalk { get; set; }
    public bool SourceMountMate { get; set; }
    public bool CheckTerrain { get; set; }
    public int GainLifePoint { get; set; }
    public bool AutoReUse { get; set; }
    public int AutoReUseDelay { get; set; }
    public int SkillPoints { get; set; }
    public int DoodadHitFamily { get; set; }
    public int WeaponSlotForAutoAttackId { get; set; }
    public bool FirstReagentOnly { get; set; }
    public List<SkillEffect> Effects { get; set; }

    // --- БИТОВАЯ МАСКА И ПРОПЕРТИ НА ОСНОВЕ НЕЁ ---
    public SkillUseCondition UseConditionBits { get; set; }
    public bool TargetDead { get => UseConditionBits.HasFlag(SkillUseCondition.TargetDead); set { if (value) UseConditionBits |= SkillUseCondition.TargetDead; else UseConditionBits &= ~SkillUseCondition.TargetDead; } }
    public bool SourceDead { get => UseConditionBits.HasFlag(SkillUseCondition.SourceDead); set { if (value) UseConditionBits |= SkillUseCondition.SourceDead; else UseConditionBits &= ~SkillUseCondition.SourceDead; } }
    public bool SourceMount { get => UseConditionBits.HasFlag(SkillUseCondition.SourceMount); set { if (value) UseConditionBits |= SkillUseCondition.SourceMount; else UseConditionBits &= ~SkillUseCondition.SourceMount; } }
    public bool StopCastingOnBigHit { get => UseConditionBits.HasFlag(SkillUseCondition.StopCastingOnBigHit); set { if (value) UseConditionBits |= SkillUseCondition.StopCastingOnBigHit; else UseConditionBits &= ~SkillUseCondition.StopCastingOnBigHit; } }
    public bool StopChannelingOnBigHit { get => UseConditionBits.HasFlag(SkillUseCondition.StopChannelingOnBigHit); set { if (value) UseConditionBits |= SkillUseCondition.StopChannelingOnBigHit; else UseConditionBits &= ~SkillUseCondition.StopChannelingOnBigHit; } }
    public bool TargetAlive { get => UseConditionBits.HasFlag(SkillUseCondition.TargetAlive); set { if (value) UseConditionBits |= SkillUseCondition.TargetAlive; else UseConditionBits &= ~SkillUseCondition.TargetAlive; } }
    public bool TargetWater { get => UseConditionBits.HasFlag(SkillUseCondition.TargetWater); set { if (value) UseConditionBits |= SkillUseCondition.TargetWater; else UseConditionBits &= ~SkillUseCondition.TargetWater; } }
    public bool TargetOnlyWater { get => UseConditionBits.HasFlag(SkillUseCondition.TargetOnlyWater); set { if (value) UseConditionBits |= SkillUseCondition.TargetOnlyWater; else UseConditionBits &= ~SkillUseCondition.TargetOnlyWater; } }
    public bool SourceNotSwim { get => UseConditionBits.HasFlag(SkillUseCondition.SourceNotSwim); set { if (value) UseConditionBits |= SkillUseCondition.SourceNotSwim; else UseConditionBits &= ~SkillUseCondition.SourceNotSwim; } }
    public bool TargetPreoccupied { get => UseConditionBits.HasFlag(SkillUseCondition.TargetPreoccupied); set { if (value) UseConditionBits |= SkillUseCondition.TargetPreoccupied; else UseConditionBits &= ~SkillUseCondition.TargetPreoccupied; } }
    public bool StopChannelingOnStartSkill { get => UseConditionBits.HasFlag(SkillUseCondition.StopChannelingOnStartSkill); set { if (value) UseConditionBits |= SkillUseCondition.StopChannelingOnStartSkill; else UseConditionBits &= ~SkillUseCondition.StopChannelingOnStartSkill; } }
    public bool StopCastingByTurn { get => UseConditionBits.HasFlag(SkillUseCondition.StopCastingByTurn); set { if (value) UseConditionBits |= SkillUseCondition.StopCastingByTurn; else UseConditionBits &= ~SkillUseCondition.StopCastingByTurn; } }
    public bool TargetMyNpc { get => UseConditionBits.HasFlag(SkillUseCondition.TargetMyNpc); set { if (value) UseConditionBits |= SkillUseCondition.TargetMyNpc; else UseConditionBits &= ~SkillUseCondition.TargetMyNpc; } }
    public bool TargetFishing { get => UseConditionBits.HasFlag(SkillUseCondition.TargetFishing); set { if (value) UseConditionBits |= SkillUseCondition.TargetFishing; else UseConditionBits &= ~SkillUseCondition.TargetFishing; } }
    public bool SourceNoSlave { get => UseConditionBits.HasFlag(SkillUseCondition.SourceNoSlave); set { if (value) UseConditionBits |= SkillUseCondition.SourceNoSlave; else UseConditionBits &= ~SkillUseCondition.SourceNoSlave; } }
    public bool SourceNotCollided { get => UseConditionBits.HasFlag(SkillUseCondition.SourceNotCollided); set { if (value) UseConditionBits |= SkillUseCondition.SourceNotCollided; else UseConditionBits &= ~SkillUseCondition.SourceNotCollided; } }

    public SkillTemplate()
    {
        Effects = new List<SkillEffect>();
    }
}
