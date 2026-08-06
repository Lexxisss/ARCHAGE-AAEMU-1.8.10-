using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

using SlaveUnit = AAEmu.Game.Models.Game.Units.Slave;

namespace AAEmu.Game.Models.Tasks.Slave;

/// <summary>Publishes the slave world state after the client-side portal animation interval.</summary>
public sealed class CompleteSlaveSpawnTask : Task
{
    private readonly SlaveUnit _slave;

    public CompleteSlaveSpawnTask(SlaveUnit slave)
    {
        _slave = slave;
    }

    public override void Execute()
    {
        SlaveManager.Instance.CompleteSpawnPublication(_slave);
    }
}
