using System;

namespace AAEmu.Game.Models.Game.Skills;

[Flags]
public enum SkillUseCondition : long
{
    None = 0,
    TargetDead = 1L << 0,
    SourceDead = 1L << 1,
    SourceMount = 1L << 2,
    StopCastingOnBigHit = 1L << 3,
    StopChannelingOnBigHit = 1L << 4,
    TargetAlive = 1L << 5,
    TargetWater = 1L << 6,
    TargetOnlyWater = 1L << 7,
    SourceNotSwim = 1L << 8,
    TargetPreoccupied = 1L << 9,
    StopChannelingOnStartSkill = 1L << 10,
    StopCastingByTurn = 1L << 11,
    TargetMyNpc = 1L << 12,
    TargetFishing = 1L << 13,
    SourceNoSlave = 1L << 14,
    SourceNotCollided = 1L << 15
}
