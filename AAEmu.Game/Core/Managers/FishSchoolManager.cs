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

    public void Initialize()
    {
        Logger.Info("Initialising FishSchool Manager...");
    }

    // Fish schools are data-driven. The target database attaches DoodadFuncFishSchool to every
    // valid school phase and points that function at an npc_spawner_id. Template IDs are not fixed.
    public static bool IsFishSchool(Doodad doodad)
    {
        if (doodad?.Template == null)
            return false;

        return DoodadManager.Instance.GetDoodadPhaseFuncs(doodad.FuncGroupId)
            .Any(func => func.FuncType == nameof(DoodadFuncFishSchool));
    }

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
