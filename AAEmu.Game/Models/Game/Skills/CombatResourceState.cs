using System;

namespace AAEmu.Game.Models.Game.Skills;

public sealed class CombatResourceState
{
    public long Point { get; set; }
    public DateTime LastRecoveryTime { get; set; }
    public uint LastUpdateTime { get; set; }
}
