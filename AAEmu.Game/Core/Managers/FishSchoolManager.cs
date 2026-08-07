using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class FishSchoolManager : Singleton<FishSchoolManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Templates that name DoodadFuncFishSchool in any of their phases.
    /// </summary>
    /// <remarks>
    /// Worked out once from the doodad data rather than asking each doodad about the phase it
    /// happens to be standing in. A school of the two common kinds spends most of its life in
    /// phases holding nothing but a timer and only reaches a fishing phase for a while at a time,
    /// so the per-phase test counted almost none of them at startup and left the fish finder
    /// blind to the rest. It also spared the radar a walk over every doodad in the world - all
    /// hundred and thirty-seven thousand of them - on each of its ticks.
    /// </remarks>
    private static HashSet<uint> _fishSchoolTemplateIds = new();

    public void Initialize()
    {
        _fishSchoolTemplateIds =
            DoodadManager.Instance.GetTemplateIdsWithPhaseFunc(nameof(DoodadFuncFishSchool));

        Logger.Info("Initialising FishSchool Manager... {0} fish-school doodad templates",
            _fishSchoolTemplateIds.Count);
    }

    public static bool IsFishSchool(Doodad doodad)
        => doodad != null && _fishSchoolTemplateIds.Contains(doodad.TemplateId);

    public void Load(uint worldId)
    {
        var count = WorldManager.Instance.GetAllDoodads()?
            .Count(doodad => doodad.Transform.WorldId == worldId && IsFishSchool(doodad)) ?? 0;
        Logger.Info("Loaded {0} fish-school doodads for worldId={1}", count, worldId);
    }

    public List<Doodad> GetAllFishSchools()
    {
        // Query the live world rather than caching object references: school doodads change phase,
        // despawn and respawn while the server is running.
        return WorldManager.Instance.GetAllDoodads()?
            .Where(IsFishSchool)
            .ToList() ?? new List<Doodad>();
    }
}
