using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Models.Game.NPChar;

public class NpcSpawnerNpc : Spawner<Npc>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public uint NpcSpawnerTemplateId { get; set; }
    public uint MemberId { get; set; }
    public string MemberType { get; set; }
    public float Weight { get; set; }

    public NpcSpawnerNpc()
    {
    }

    /// <summary>
    /// Creates a new instance of NpcSpawnerNpcs with a Spawner template id (npc_spanwers)
    /// </summary>
    /// <param name="spawnerTemplateId"></param>
    public NpcSpawnerNpc(uint spawnerTemplateId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
    }

    public NpcSpawnerNpc(uint spawnerTemplateId, uint memberId)
    {
        NpcSpawnerTemplateId = spawnerTemplateId;
        MemberId = memberId;
        UnitId = memberId;
        MemberType = "Npc";
    }

    public List<Npc> Spawn(NpcSpawner npcSpawner, uint quantity = 1, uint maxPopulation = 1)
    {
        switch (MemberType)
        {
            case "Npc":
                return SpawnNpc(npcSpawner, quantity, maxPopulation);
            case "NpcGroup":
                return SpawnNpcGroup(npcSpawner, quantity, maxPopulation);
            default:
                throw new InvalidOperationException($"Tried spawning an unsupported line from NpcSpawnerNpc - Id: {Id}");
        }
    }

    private List<Npc> SpawnNpc(NpcSpawner npcSpawner, uint quantity = 1, uint maxPopulation = 1)
    {
        return SpawnNpc(npcSpawner, MemberId, quantity, maxPopulation, Vector3.Zero);
    }

    private List<Npc> SpawnNpc(NpcSpawner npcSpawner, uint npcTemplateId, uint quantity, uint maxPopulation, Vector3 formationOffset)
    {
        var npcs = new List<Npc>();
        for (var i = 0; i < quantity; i++)
        {
            var npc = NpcManager.Instance.Create(0, npcTemplateId);
            if (npc == null)
            {
                Logger.Warn($"Npc {npcTemplateId}, from spawner Id {npcSpawner.Id} not exist at db");
                return null;
            }

            Logger.Trace($"Spawn npc templateId {npcTemplateId} objId {npc.ObjId} from spawnerId {NpcSpawnerTemplateId}");

            var spawnPosition = npcSpawner.Position.Clone();
            if (formationOffset != Vector3.Zero)
            {
                var sin = MathF.Sin(spawnPosition.Yaw);
                var cos = MathF.Cos(spawnPosition.Yaw);
                spawnPosition.X += formationOffset.X * cos - formationOffset.Y * sin;
                spawnPosition.Y += formationOffset.X * sin + formationOffset.Y * cos;
                spawnPosition.Z += formationOffset.Z;
            }

            if (!npc.CanFly)
            {
                // try to find Z first in GeoData, and then in HeightMaps, if not found, leave Z as it is
                var newZ = WorldManager.Instance.GetHeight(spawnPosition.ZoneId, spawnPosition.X, spawnPosition.Y);
                if (Math.Abs(spawnPosition.Z - newZ) < 1f)
                {
                    spawnPosition.Z = newZ;
                }
            }

            npc.Transform.ApplyWorldSpawnPosition(spawnPosition);
            if (npc.Transform == null)
            {
                Logger.Error($"Can't spawn npc {npcTemplateId} from spawnerId {NpcSpawnerTemplateId}");
                return null;
            }

            npc.Transform.InstanceId = npc.Transform.WorldId;
            npc.InstanceId = npc.Transform.WorldId;

            if (npc.Ai != null)
            {
                npc.Ai.IdlePosition = npc.Transform.CloneDetached();
                npc.Ai.GoToSpawn();
            }

            npc.Spawner = npcSpawner;
            npc.Spawner.RespawnTime = (int)Rand.Next(npc.Spawner.Template.SpawnDelayMin, npc.Spawner.Template.SpawnDelayMax);
            npc.Spawn();

            // check what's nearby
            var aroundNpcs = WorldManager.GetAround<Npc>(npc, 1); // 15
            var count = 0u;
            foreach (var n in aroundNpcs.Where(n => n.TemplateId == npcTemplateId))
            {
                count++;
            }
            if (count > maxPopulation)
            {
                npc.Delete();
                Logger.Trace($"Let's not spawn Npc templateId {npcTemplateId} from spawnerId {NpcSpawnerTemplateId} since exceeded MaxPopulation {maxPopulation}");
                return null;
            }

            var world = WorldManager.Instance.GetWorld(npc.Transform.WorldId);
            world.Events.OnUnitSpawn(world, new OnUnitSpawnArgs { Npc = npc });
            npc.Simulation = new Simulation(npc);
            npcs.Add(npc);
        }

        //Logger.Warn($"Spawned Npcs id={MemberId}, maxPopulation={maxPopulation}...");

        return npcs;
    }

    private List<Npc> SpawnNpcGroup(NpcSpawner npcSpawner, uint quantity = 1, uint maxPopulation = 1)
    {
        var result = new List<Npc>();
        var members = NpcGameData.Instance.GetNpcGroupMembers(MemberId);
        if (members.Count == 0)
        {
            Logger.Warn($"NpcGroup {MemberId}, from spawner Id {npcSpawner.Id} has no members");
            return result;
        }

        for (var i = 0; i < quantity; i++)
        {
            foreach (var member in members)
            {
                var spawned = SpawnNpc(npcSpawner, member.NpcId, 1, uint.MaxValue, member.FormationOffset);
                if (spawned != null)
                    result.AddRange(spawned);
            }
        }
        return result;
    }
}
