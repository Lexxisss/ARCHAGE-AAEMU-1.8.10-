using System.Linq;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncFishSchool : DoodadPhaseFuncTemplate
{
    public uint NpcSpawnerId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Debug($"DoodadFuncFishSchool NpcSpawnerId={NpcSpawnerId}");

        var existingNpc = WorldManager.Instance.GetAllNpcs()
            .FirstOrDefault(npc => npc.Spawner?.Id == NpcSpawnerId &&
                                   npc.Transform.WorldId == owner.Transform.WorldId &&
                                   npc.Transform.InstanceId == owner.Transform.InstanceId &&
                                   MathUtil.CalculateDistance(npc.Transform.World.Position, owner.Transform.World.Position, true) <= 20f);
        if (existingNpc != null)
        {
            Logger.Debug("DoodadFuncFishSchool: spawner {0} already has live npc objId={1}", NpcSpawnerId, existingNpc.ObjId);
            return false;
        }

        var npcSpawnerNpc = NpcGameData.Instance.GetNpcSpawnerNpc(NpcSpawnerId);
        if (npcSpawnerNpc == null)
        {
            Logger.Warn($"DoodadFuncFishSchool: Npc for SpawnerId={NpcSpawnerId} doesn't exist");
            return false;
        }
        var unitId = npcSpawnerNpc.MemberId;

        var spawner = SpawnManager.Instance.GetNpcSpawner(NpcSpawnerId, (byte)caster.Transform.WorldId);
        if (spawner.Count == 0)
        {
            spawner.Add(new NpcSpawner());
            //var npcSpawnersIds = new List<uint> { NpcSpawnerId };//NpcGameData.Instance.GetSpawnerIds(unitId);
            spawner[0].UnitId = unitId;
            spawner[0].Id = NpcSpawnerId;
            spawner[0].NpcSpawnerIds = new List<uint> { NpcSpawnerId };
            spawner[0].Template = NpcGameData.Instance.GetNpcSpawnerTemplate(NpcSpawnerId);
            if (spawner[0].Template == null) { return false; }

            if (spawner[0].Template.Npcs.Count == 0)
            {
                spawner[0].Template.Npcs = new List<NpcSpawnerNpc>();
                var nsn = NpcGameData.Instance.GetNpcSpawnerNpc(NpcSpawnerId);
                if (nsn == null) { return false; }
                spawner[0].Template.Npcs.Add(nsn);
            }
            if (spawner[0].Template.Npcs == null) { return false; }

            spawner[0].Template.Npcs[0].MemberId = unitId;
            spawner[0].Template.Npcs[0].UnitId = unitId;

        }
        using var spawnPos = owner.Transform.Clone();
        //spawnPos.World.AddDistanceToFront(3f);
        //spawnPos.World.SetHeight(WorldManager.Instance.GetHeight(spawnPos));
        spawner[0].Position = spawnPos.CloneAsSpawnPosition();
        var npc = spawner[0].Spawn(0);
        if (npc != null)
            npc.Spawner.RespawnTime = 0; // no automatic respawn for the hooked fish

        return false;
    }
}
