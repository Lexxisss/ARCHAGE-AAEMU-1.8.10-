using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.World;

using Newtonsoft.Json;

using NLog;

using static System.String;

namespace AAEmu.Game.Models.Game.NPChar;

public class NpcSpawner : Spawner<Npc>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private List<Npc> _spawned; // the list of Npc's that have been shown
    private Npc _lastSpawn;      // the last of the displayed Npc
    private int _scheduledCount; // already scheduled to show Npc
    private int _spawnCount;     // have already shown so many Npc
    private int _logicalPopulation;
    private readonly Dictionary<uint, int> _memberSpawnCounts = new();
    private readonly Dictionary<uint, uint> _logicalMemberByNpcObjId = new();

    // Which post each npc of this placement is standing at, so that two of them never take the
    // same one. A post carries the way it is faced, so sharing one leaves other posts empty and
    // both occupants facing a way that only makes sense where they are not standing.
    private readonly Dictionary<uint, int> _pointByNpcObjId = new();
    private readonly HashSet<int> _takenPoints = new();
    private int _lastPointIndex = -1;
    private WorldSpawnPosition _basePosition;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    [DefaultValue(1f)]
    public uint Count { get; set; }
    public List<uint> NpcSpawnerIds { get; set; }
    private bool _isScheduled { get; set; }
    public NpcSpawnerTemplate Template { get; set; } // npcSpawnerId(NpcSpawnerTemplateId), template
    public NpcSpawnerPlacement Placement { get; set; }

    public NpcSpawner()
    {
        _isScheduled = false; // Npc isn't on the schedule
        _spawned = new List<Npc>();
        Count = 1;
        NpcSpawnerIds = new List<uint>();
        Template = null;
        _lastSpawn = null;
    }

    /// <summary>
    /// Show all Npcs
    /// </summary>
    /// <returns></returns>
    public List<Npc> SpawnAll(bool beginning = false)
    {
        if (DoSpawnSchedule(true))
        {
            return null; // if npcs are delayed to show later
        }
        DoSpawn(true, beginning); // show all Npc, first start server

        return _spawned;
    }

    /// <summary>
    /// Show one Npc
    /// </summary>
    /// <param name="objId"></param>
    /// <returns></returns>
    public override Npc Spawn(uint objId)
    {
        if (DoSpawnSchedule())
        {
            return null; // if npcs are delayed to show later
        }
        DoSpawn(); // show one Npc
        return _lastSpawn;
    }

    public override void Despawn(Npc npc)
    {
        npc.Delete();
        RemoveExactLogicalPopulation(npc);

        if (npc.Transform.WorldId > 0)
        {
            // Temporary range for instanced worlds
            var dungeon = IndunManager.Instance.GetDungeonByWorldId(npc.Transform.WorldId);

            if (dungeon is not null)
            {
                dungeon.UnregisterNpcEvents(npc);
            }
        }

        if (npc.Respawn == DateTime.MinValue)
        {
            npc.Spawner._spawned.Remove(npc);
            ObjectIdManager.Instance.ReleaseId(npc.ObjId);
            npc.Spawner._spawnCount--;
        }

        if (npc.Spawner._lastSpawn == null || npc.Spawner._lastSpawn.ObjId == npc.ObjId)
        {
            npc.Spawner._lastSpawn = npc.Spawner._spawned.Count != 0 ? npc.Spawner._spawned[^1] : null;
        }
    }

    public void ClearLastSpawnCount()
    {
        _spawnCount = 0;
    }
    public void DecreaseCount(Npc npc)
    {
        RemoveExactLogicalPopulation(npc);
        npc.Spawner._spawnCount--;
        npc.Spawner._spawned.Remove(npc);
        if (npc.Spawner.RespawnTime > 0 && npc.Spawner._spawnCount + npc.Spawner._scheduledCount < npc.Spawner.Count)
        {
            npc.Respawn = DateTime.UtcNow.AddSeconds(npc.Spawner.RespawnTime);
            SpawnManager.Instance.AddRespawn(npc);
            npc.Spawner._scheduledCount++;
        }

        npc.Despawn = DateTime.UtcNow.AddSeconds(npc.Spawner.DespawnTime);
        SpawnManager.Instance.AddDespawn(npc);
    }

    public void DespawnWithRespawn(Npc npc)
    {
        npc.Delete();
        RemoveExactLogicalPopulation(npc);
        npc.Spawner._spawnCount--;
        npc.Spawner._spawned.Remove(npc);
        if (npc.Spawner.RespawnTime > 0 && npc.Spawner._spawnCount + npc.Spawner._scheduledCount < npc.Spawner.Count)
        {
            npc.Respawn = DateTime.UtcNow.AddSeconds(npc.Spawner.RespawnTime);
            SpawnManager.Instance.AddRespawn(npc);
            npc.Spawner._scheduledCount++;
        }
    }

    /// <summary>
    /// Do despawn
    /// </summary>
    /// <param name="npc"></param>
    /// <param name="all">to show everyone or not</param>
    public void DoDespawn(Npc npc, bool all = false)
    {
        if (npc == null) { return; }

        if (npc.IsInBattle)
        {
            return;
        }
        if (all)
        {
            for (var i = 0; i < _spawnCount; i++)
            {
                Despawn(npc.Spawner._lastSpawn);
            }
        }
        else
        {
            Despawn(npc);
        }
    }

    /// <summary>
    /// Do spawn Npc
    /// </summary>
    /// <param name="all">to show everyone or not</param>
    /// <param name="beginning">if this is the first start of the server</param>
    public void DoSpawn(bool all = false, bool beginning = false)
    {
        if (Placement != null)
        {
            DoExactSpawnerSpawn(all);
            return;
        }

        // проверим, что взяли все спавнеры
        // check what all spawners took
        var spawnerIds = NpcGameData.Instance.GetSpawnerIds(UnitId);
        var npcSpawnerIds = spawnerIds.Count > NpcSpawnerIds.Count ? spawnerIds : NpcSpawnerIds;

        // Select an NPC to spawn based on the spawnerId in npc_spawner_npcs
        var npcs = new List<Npc>();
        var delnpcs = new List<Npc>();

        foreach (var spawnerId in npcSpawnerIds)
        {
            var template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerId);
            if (template == null)
            {
                // Select an NPC to spawn based on the spawnerId in npc_spawner_npcs
                foreach (var nsn in Template.Npcs.Where(nsn => nsn.MemberId == UnitId))
                {
                    npcs = nsn.Spawn(this, all ? Template.MaxPopulation : 1);
                    if (npcs == null) { return; }
                    break;
                }
            }
            else
            {
                // если это первый старт сервера, то спавним только - NpcSpawnerCategory.Autocreated;
                // if this is the first start of the server, then only spawn - NpcSpawnerCategory.Autocreated;
                if (template.NpcSpawnerCategoryId != NpcSpawnerCategory.Autocreated && npcSpawnerIds.Count > 1 && beginning)
                {
                    continue;
                }
                // если это обычный спавн Npc, то пропускаем NpcSpawnerCategory.Autocreated
                // if it's a normal Npc spawn then skip NpcSpawnerCategory.Autocreated
                if (template.NpcSpawnerCategoryId == NpcSpawnerCategory.Autocreated && npcSpawnerIds.Count > 1 && !beginning)
                {
                    continue;
                }

                var suspendSpawnCount = template.SuspendSpawnCount > 0 ? template.SuspendSpawnCount : 1;
                var maxPopulation = template.MaxPopulation;
                var testRadiusPc = template.TestRadiusPc;
                var quantity = suspendSpawnCount;
                //var playerCount = 0u;

                // проверим есть ли рядом игроки
                // see if there are any players around
                //if (_lastSpawn != null)
                //{
                //    playerCount = (uint)WorldManager.GetAround<Character>(_lastSpawn, testRadiusPc).Count;
                //}

                // если рядом игроки, то увеличим количество Npc
                // if there are players around, we'll increase the number of Npc
                //if (playerCount > 1) { quantity = suspendSpawnCount * playerCount; }

                // проверим, что бы количество было не более максимальной популяции
                // check that the number is not more than the maximum population
                if (quantity > maxPopulation) { quantity = maxPopulation; }

                // если не хотим спавнить всех
                // if we don't want to spawn everyone
                if (!all) { quantity = 1; }

                // Check if we did not go over MaxPopulation Spawn Count
                if (_spawnCount > maxPopulation)
                {
                    Logger.Trace($"Let's not spawn Npc templateId {UnitId} from spawnerId {Template.Id} since exceeded MaxPopulation {maxPopulation}");
                    return;
                }

                foreach (var nsn in template.Npcs.Where(nsn => nsn.MemberId == UnitId))
                {
                    npcs = nsn.Spawn(this, quantity, maxPopulation);
                    break;
                }
                if (npcs == null) { continue; }
            }

            if (npcs.Count == 0)
            {
                Logger.Error($"Can't spawn npc {UnitId} from spawnerId {Template.Id}");
                continue;
            }

            delnpcs.AddRange(npcs);

            _spawned.AddRange(npcs);

            if (_scheduledCount > 0)
            {
                _scheduledCount -= npcs.Count;
            }

            _spawnCount += npcs.Count;

            if (_spawnCount < 0)
            {
                _spawnCount = 0;
            }

            _lastSpawn = _spawned[^1];
            //_lastSpawn.Spawner = this;
        }

        if (_isScheduled)
        {
            DoDespawnSchedule(_lastSpawn, all);
        }

        // удалим чуть позже всех лишних Npc, оставим только одного
        var deleteCount = delnpcs.Count - 1;
        if (deleteCount > 1)
        {
            for (var i = 0; i < deleteCount; i++)
            {
                Logger.Trace($"Let's schedule npc removal {UnitId} from spawnerId {Template.Id}");
                DoDespawnSchedule(delnpcs[i], false, 60); // через 1 минуту
            }
        }

        if (IsNullOrEmpty(FollowPath)) { return; }

        foreach (var npc in npcs)
        {
            if (npc.IsInPatrol) { return; }
            npc.IsInPatrol = true;
            npc.Simulation.RunningMode = false;
            npc.Simulation.Cycle = true;
            npc.Simulation.MoveToPathEnabled = false;
            npc.Simulation.MoveFileName = FollowPath;
            npc.Simulation.GoToPath(npc, true);
        }
    }

    private void DoExactSpawnerSpawn(bool all)
    {
        if (Template?.Npcs == null || Template.Npcs.Count == 0)
            return;

        var quantity = all
            ? Math.Max(Template.SuspendSpawnCount, Math.Max(Template.MinPopulation, 1u))
            : 1u;
        quantity = Math.Min(quantity, Template.MaxPopulation);

        for (var i = 0u; i < quantity && _logicalPopulation < Template.MaxPopulation; i++)
        {
            var member = SelectWeightedMember();
            if (member == null)
                break;

            List<Npc> npcs = null;
            for (var attempt = 0; attempt < 5 && (npcs == null || npcs.Count == 0); attempt++)
            {
                Position = SelectSpawnPosition();
                npcs = member.Spawn(this, 1, Template.MaxPopulation);
            }
            if (npcs == null || npcs.Count == 0)
                continue;

            _spawned.AddRange(npcs);
            _spawnCount += npcs.Count;
            _logicalPopulation++;
            _memberSpawnCounts[member.Id] = _memberSpawnCounts.GetValueOrDefault(member.Id) + 1;
            _logicalMemberByNpcObjId[npcs[0].ObjId] = member.Id;
            if (_lastPointIndex >= 0)
            {
                _takenPoints.Add(_lastPointIndex);
                _pointByNpcObjId[npcs[0].ObjId] = _lastPointIndex;
                _lastPointIndex = -1;
            }
            if (_scheduledCount > 0)
                _scheduledCount = Math.Max(0, _scheduledCount - npcs.Count);
            _lastSpawn = npcs[^1];

            if (!IsNullOrEmpty(FollowPath))
            {
                foreach (var npc in npcs)
                {
                    if (npc.IsInPatrol)
                        continue;
                    npc.IsInPatrol = true;
                    npc.Simulation.RunningMode = false;
                    npc.Simulation.Cycle = true;
                    npc.Simulation.MoveToPathEnabled = false;
                    npc.Simulation.MoveFileName = FollowPath;
                    npc.Simulation.GoToPath(npc, true);
                }
            }
        }
    }

    private NpcSpawnerNpc SelectWeightedMember()
    {
        var totalWeight = Template.Npcs.Sum(npc => Math.Max(0f, npc.Weight));
        if (totalWeight <= 0f)
            return Template.Npcs[0];

        NpcSpawnerNpc selected = null;
        var bestComparison = float.NegativeInfinity;
        foreach (var npc in Template.Npcs)
        {
            var desiredShare = Math.Max(0f, npc.Weight) / totalWeight;
            var actualShare = _logicalPopulation > 0
                ? (float)_memberSpawnCounts.GetValueOrDefault(npc.Id) / _logicalPopulation
                : 0f;
            var comparison = desiredShare - actualShare + Rand.Next(0f, 0.001f);
            if (comparison > bestComparison)
            {
                bestComparison = comparison;
                selected = npc;
            }
        }
        return selected;
    }

    private WorldSpawnPosition SelectSpawnPosition()
    {
        // Start from where the placement actually is, not from wherever the last npc of this
        // spawner happened to be put: Position is overwritten on every spawn, so measuring the
        // next one from it let the whole group wander and carry the previous one's facing.
        _basePosition ??= Position.Clone();
        _lastPointIndex = -1;
        var result = _basePosition.Clone();
        if (Placement.SpawnAreaType == "area" && Placement.Triangles.Count > 0)
        {
            var totalAreaRate = Placement.Triangles.Sum(triangle => Math.Max(0f, triangle.AreaRate));
            var roll = Rand.Next(0f, totalAreaRate > 0f ? totalAreaRate : Placement.Triangles.Count);
            var selected = Placement.Triangles[^1];
            foreach (var triangle in Placement.Triangles)
            {
                roll -= totalAreaRate > 0f ? Math.Max(0f, triangle.AreaRate) : 1f;
                if (roll <= 0f)
                {
                    selected = triangle;
                    break;
                }
            }

            var random1 = Rand.Next(0f, 1f);
            var random2 = Rand.Next(0f, 1f);
            var r1 = MathF.Min(random1, random2);
            var r2 = MathF.Max(random1, random2);
            var point = r1 * selected.V1 + (r2 - r1) * selected.V2 + (1f - r2) * selected.V3;
            result.X = point.X;
            result.Y = point.Y;
            result.Z = point.Z;
            return result;
        }

        if (Placement.Points.Count > 0)
        {
            var index = SelectFreePointIndex();
            var point = Placement.Points[index];
            _lastPointIndex = index;
            result.X = point.Position.X;
            result.Y = point.Position.Y;
            result.Z = point.Position.Z;
            result.Yaw = point.ZRotation;
        }
        return result;
    }

    /// <summary>
    /// A post of this placement that nobody is standing at, or any of them once they are all
    /// taken. Drawing with replacement stacked npcs two to a post and left others empty, and
    /// since the post is what says which way to face, the ones doubled up faced the wrong way
    /// for where they stood.
    /// </summary>
    private int SelectFreePointIndex()
    {
        if (_takenPoints.Count >= Placement.Points.Count)
            return Rand.Next(Placement.Points.Count);

        var free = new List<int>(Placement.Points.Count - _takenPoints.Count);
        for (var i = 0; i < Placement.Points.Count; i++)
            if (!_takenPoints.Contains(i))
                free.Add(i);

        return free.Count > 0 ? free[Rand.Next(free.Count)] : Rand.Next(Placement.Points.Count);
    }

    private void RemoveExactLogicalPopulation(Npc npc)
    {
        if (Placement == null || !_logicalMemberByNpcObjId.Remove(npc.ObjId, out var memberId))
            return;
        // Give the post back, or the placement runs out of them and starts doubling up again.
        if (_pointByNpcObjId.Remove(npc.ObjId, out var pointIndex))
            _takenPoints.Remove(pointIndex);
        _logicalPopulation = Math.Max(0, _logicalPopulation - 1);
        if (_memberSpawnCounts.TryGetValue(memberId, out var count))
            _memberSpawnCounts[memberId] = Math.Max(0, count - 1);
    }

    /// <summary>
    /// Do schedule spawn Npc
    /// </summary>
    /// <param name="all">to show everyone or not</param>
    private bool DoSpawnSchedule(bool all = false)
    {
        // TODO Check if delay is OK
        if (Template == null)
        {
            // no spawner for TemplateId
            Logger.Warn($"Can't spawn npc {UnitId} from spawnerId {Id}");
            return true;
        }

        // Check if population is within bounds
        if ((Placement == null ? _spawnCount : _logicalPopulation) >= Template.MaxPopulation)
        {
            Logger.Trace($"Let's not spawn Npc templateId {UnitId} from spawnerId {Template.Id} since exceeded MaxPopulation");
            return true;
        }

        #region Schedule

        _isScheduled = false;
        // Check if Time Of Day matches Template.StartTime or Template.EndTime
        if (Template.StartTime > 0.0f | Template.EndTime > 0.0f)
        {
            var curTime = TimeManager.Instance.GetTime();
            if (!TimeSpan.FromHours(curTime).IsBetween(TimeSpan.FromHours(Template.StartTime), TimeSpan.FromHours(Template.EndTime)))
            {
                var start = (int)Math.Round(Template.StartTime);
                if (start == 0) { start = 24; }
                var delay = start - curTime;
                if (delay < 0f)
                {
                    delay = curTime + delay;
                }
                delay = delay * 60f * 10f;
                if (delay < 1f)
                {
                    delay = 5f;
                }
                _isScheduled = true; // Npc is on the schedule
                TaskManager.Instance.Schedule(new NpcSpawnerDoSpawnTask(this), TimeSpan.FromSeconds(delay));
                // Reschedule when OK
                return true;
            }
        }
        // First, let's check if the schedule has such an spawnerId
        else if (GameScheduleManager.Instance.CheckSpawnerInScheduleSpawners((int)Template.Id))
        {
            _isScheduled = true; // Npc is on the schedule

            // if there is, we'll check the time for the spawning
            if (GameScheduleManager.Instance.CheckSpawnerInGameSchedules((int)Template.Id))
            {
                // есть в расписании, надо спавнить сейчас
                // is in the schedule, we need to spawn now
                return false;
            }

            // есть в расписании, надо запланировать
            // is on the schedule, needs to be scheduled
            var cronExpression = GameScheduleManager.Instance.GetCronRemainingTime((int)Template.Id, true);

            if (cronExpression is "" or "0 0 0 0 0 ?")
            {
                Logger.Warn($"DoSpawnSchedule: Can't reschedule spawn npc {UnitId} from spawnerId {Template.Id}");
                Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                return false;
            }

            try
            {
                TaskManager.Instance.CronSchedule(new NpcSpawnerDoSpawnTask(this), cronExpression);
            }
            catch (Exception)
            {
                Logger.Warn($"DoSpawnSchedule: Can't reschedule spawn npc {UnitId} from spawnerId {Template.Id}");
                Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                return false;
            }

            return true; // Reschedule when OK
            // couldn't find it on the schedule, but it should have been!
            // no entries found for this unit in Game_Schedule table
        }

        #endregion Schedule

        return false;
    }

    /// <summary>
    /// Do schedule despawn Npc
    /// </summary>
    /// <param name="npc"></param>
    /// <param name="all">to show everyone or not</param>
    /// <param name="timeToDespawn"></param>
    private void DoDespawnSchedule(Npc npc, bool all = false, float timeToDespawn = 0)
    {
        #region Schedule

        // удалим по запросу
        if (timeToDespawn > 0)
        {
            TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(timeToDespawn));
            return; // Reschedule when OK
        }
        // Check if Time Of Day matches Template.StartTime or Template.EndTime

        if (Template.StartTime > 0.0f | Template.EndTime > 0.0f)
        {
            var curTime = TimeManager.Instance.GetTime();
            if (TimeSpan.FromHours(curTime).IsBetween(TimeSpan.FromHours(Template.StartTime), TimeSpan.FromHours(Template.EndTime)))
            {
                var end = (int)Math.Round(Template.EndTime);
                if (end == 0) { end = 24; }
                var delay = end - curTime;
                if (delay < 0f)
                {
                    delay = curTime + delay;
                }
                delay = delay * 60f * 10f;
                if (delay < 1f)
                {
                    delay = 5f;
                }
                TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(delay));

                return; // Reschedule when OK
            }
        }
        // First, let's check if the schedule has such an Template.Id
        else if (GameScheduleManager.Instance.CheckSpawnerInScheduleSpawners((int)Template.Id))
        {
            var cronExpression = GameScheduleManager.Instance.GetCronRemainingTime((int)Template.Id, true);

            if (cronExpression is "" or "0 0 0 0 0 ?")
            {
                Logger.Warn($"DoDespawnSchedule: Can't reschedule despawn npc {UnitId} from spawnerId {Template.Id}");
                Logger.Warn($"DoDespawnSchedule: cronExpression {cronExpression}");
                return;
            }

            try
            {
                TaskManager.Instance.CronSchedule(new NpcSpawnerDoDespawnTask(npc), cronExpression);
            }
            catch (Exception)
            {
                Logger.Warn($"DoDespawnSchedule: Can't reschedule despawn npc {UnitId} from spawnerId {Template.Id}");
                Logger.Warn($"DoDespawnSchedule: cronExpression {cronExpression}");
                return;
            }

            return; // Reschedule when OK
        }

        #endregion Schedule

        DoDespawn(npc, all);
        DoSpawnSchedule(all);
    }

    public void DoEventSpawn()
    {
        // TODO Check if delay is OK
        if (Template == null)
        {
            // no spawner for TemplateId
            Logger.Error("Can't spawn npc {0} from spawnerId {1}", UnitId, Id);
            return;
        }

        // Check if population is within bounds
        if (_spawnCount >= Template.MaxPopulation)
        {
            return;
        }

        // Check if we did not go over Suspend Spawn Count
        if (Template.SuspendSpawnCount > 0 && _spawnCount > Template.SuspendSpawnCount)
        {
            //Logger.Debug("DoSpawn: Npc TemplateId {0}, spawnerId {1} reschedule next time...", UnitId, Id);
            //TaskManager.Instance.Schedule(new NpcSpawnerDoSpawnTask(this), TimeSpan.FromSeconds(60));
            return;
        }

        // Select an NPC to spawn based on the Template.Id in npc_spawner_npcs
        var n = new List<Npc>();
        foreach (var nsn in Template.Npcs)
        {
            if (nsn.MemberId != UnitId) { continue; }
            n = nsn.Spawn(this);
            break;
        }

        try
        {
            foreach (var npc in n)
            {
                _spawned.Add(npc);
            }
        }
        catch (Exception)
        {
            Logger.Error("Can't spawn npc {0} from spawnerId {1}", UnitId, Template.Id);
        }

        if (n.Count == 0)
        {
            Logger.Error("Can't spawn npc {0} from spawnerId {1}", UnitId, Template.Id);
            return;
        }
        _lastSpawn = n[^1];
        if (_scheduledCount > 0)
        {
            _scheduledCount -= n.Count;
        }
        _spawnCount = _spawned.Count;
        if (_spawnCount < 0)
        {
            _spawnCount = 0;
        }
    }

    public void DoSpawnEffect(uint spawnerId, SpawnEffect effect, BaseUnit caster, BaseUnit target)
    {
        var template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerId);
        if (template.Npcs == null)
        {
            return;
        }

        var n = new List<Npc>();
        foreach (var nsn in template.Npcs)
        {
            if (nsn == null || nsn.MemberId != UnitId)
                continue;

            n = nsn.Spawn(this, template.MaxPopulation);
            break;
        }

        try
        {
            if (n == null) { return; }

            foreach (var npc in n)
            {
                if (npc.Spawner != null)
                {
                    npc.Spawner.RespawnTime = 0; // don't respawn
                }

                if (effect.UseSummonerFaction)
                {
                    npc.Faction = target is Npc ? target.Faction : caster.Faction;
                }

                if (effect.UseSummonerAggroTarget && !effect.UseSummonerFaction)
                {
                    // TODO : Pick random target off of Aggro table ?

                    // Npc attacks the character
                    if (target is Npc)
                    {
                        npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, (Unit)target, 1);
                    }
                    else
                    {
                        npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, (Unit)caster, 1);
                    }

                    npc.Ai.OnAggroTargetChanged();
                    //npc.Ai.GoToCombat();
                }

                if (effect.LifeTime > 0)
                {
                    TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(effect.LifeTime));
                }
            }
        }
        catch (Exception)
        {
            Logger.Error("Can't spawn npc {0} from spawner {1}", UnitId, template.Id);
            return;
        }
        if (n.Count == 0)
        {
            Logger.Error("Can't spawn npc {0} from spawner {1}", UnitId, template.Id);
            return;
        }
        _spawned.AddRange(n);
        if (_scheduledCount > 0)
        {
            _scheduledCount -= n.Count;
        }
        _spawnCount = _spawned.Count;
        if (_spawnCount < 0)
        {
            _spawnCount = 0;
        }
        _lastSpawn = n[^1];
        _lastSpawn.Spawner = this;
    }

    public void ClearSpawnCount()
    {
        _spawnCount = 0;
        _logicalPopulation = 0;
        _memberSpawnCounts.Clear();
        _logicalMemberByNpcObjId.Clear();
        _pointByNpcObjId.Clear();
        _takenPoints.Clear();
        _lastPointIndex = -1;
        _spawned = new List<Npc>();
        _lastSpawn = null;
    }

    public bool CanSpawn()
    {
        return (Placement == null ? _spawnCount : _logicalPopulation) < Template.MaxPopulation;
    }

    public Unit GetLastSpawn()
    {
        return _lastSpawn;
    }
}
