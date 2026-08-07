using System;
using System.Collections.Generic;
using System.ComponentModel;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.World;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj;

public class DoodadSpawner : Spawner<Doodad>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public float Scale { get; set; }
    public Doodad Last { get; set; }

    private List<Doodad> _spawned;
    private int _scheduledCount;
    private int _spawnCount;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    [DefaultValue(1f)]
    public uint Count { get; set; } = 1;
    private bool _permanent { get; set; }
    public List<uint> RelatedIds { get; set; }
    //---
    public uint RespawnDoodadTemplateId { get; set; }

    public DoodadSpawner()
    {
        _permanent = true; // Doodad not on the schedule.
        _spawned = new List<Doodad>();
        Count = 1;
        Last = new Doodad();
        Scale = 1f;
    }

    public DoodadSpawner(uint id, uint unitId, WorldSpawnPosition position)
    {
        Id = id;
        UnitId = unitId;
        Position = position;
    }

    /// <summary>
    /// Spawn a doodad in the world with a character as owner
    /// </summary>
    /// <param name="objId">instance id of the doodad</param>
    /// <param name="itemId">template id of the doodad</param>
    /// <param name="charId">instance id of the character</param>
    /// <returns>Created doodad reference</returns>
    public override Doodad Spawn(uint objId, ulong itemId, uint charId) //Mostly used for player created spawns
    {
        _permanent = true; // Doodad not on the schedule.
        _spawned = new List<Doodad>();
        Count = 1;
        Last = new Doodad();
        var character = WorldManager.Instance.GetCharacterByObjId(charId);
        var doodad = DoodadManager.Instance.Create(objId, UnitId, character);

        if (doodad == null)
        {
            Logger.Warn("Doodad {0}, from spawn not exist at db", UnitId);
            return null;
        }

        doodad.Spawner = this;
        doodad.Transform.ApplyWorldSpawnPosition(Position);
        doodad.QuestGlow = 0u; // TODO: make this OOP
        doodad.ItemId = itemId;

        // TODO for test
        doodad.PlantTime = DateTime.UtcNow;

        if (Scale > 0)
        {
            doodad.SetScale(Scale);
        }

        if (doodad.Transform == null)
        {
            Logger.Error("Can't spawn doodad {1} from spawn {0}", Id, UnitId);
            return null;
        }

        Last = doodad;
        DoSpawn();// schedule check and spawn
        return doodad;
    }

    /// <summary>
    /// Spawn a doodad (mostly used by respawns)
    /// </summary>
    /// <param name="objId"></param>
    /// <returns></returns>
    public override Doodad Spawn(uint objId) // TODO: clean up each doodad uses the same call
    {
        _permanent = true; // Doodad not on the schedule.
        _spawned = new List<Doodad>();
        Count = 1;
        Last = new Doodad();

        if (objId != 0) { return null; }

        var newUnitId = RespawnDoodadTemplateId > 0 ? RespawnDoodadTemplateId : UnitId;
        RespawnDoodadTemplateId = 0; // reset it after 1 spawn

        var doodad = DoodadManager.Instance.Create(objId, newUnitId);
        if (doodad == null)
        {
            Logger.Warn("Doodad Temaplte {0}, used in Spawn() does not exist in db", newUnitId);
            return null;
        }

        doodad.Spawner = this;
        doodad.Transform.ApplyWorldSpawnPosition(Position);
        // TODO for test
        doodad.PlantTime = DateTime.UtcNow;
        if (Scale > 0)
        {
            doodad.SetScale(Scale);
        }

        if (doodad.Transform == null)
        {
            Logger.Error("Can't spawn doodad {1} from spawn {0}", Id, newUnitId);
            return null;
        }

        Last = doodad;
        DoSpawn();// schedule check and spawn
        return doodad;
    }

    public override void Despawn(Doodad doodad)
    {
        doodad.Delete();

        if (doodad.Respawn == DateTime.MinValue)
        {
            ObjectIdManager.Instance.ReleaseId(doodad.ObjId);
        }

        Last = null;
    }

    public void DecreaseCount(Doodad doodad)
    {
        if (RespawnTime > 0)
        {
            doodad.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
            SpawnManager.Instance.AddRespawn(doodad);
        }
        else
        {
            Last = null;
        }

        doodad.Delete();
    }

    public void DoDespawn(Doodad doodad)
    {
        #region Schedule
        // First, let's check if the schedule has such an spawnerId
        if (GameScheduleManager.Instance.CheckDoodadInScheduleSpawners((int)doodad.TemplateId))
        {
            // While the window is open the doodad stays; ask again when it is due to close.
            if (GameScheduleManager.Instance.CheckDoodadInGameSchedules(doodad.TemplateId))
            {
                var delay = GameScheduleManager.Instance.GetRemainingTimeDoodad((int)doodad.TemplateId, false);

                // Same trap as on the spawn side: a zero here rescheduled this despawn for now,
                // and it spun. Without an end time there is nothing to wait for, so leave the
                // doodad standing rather than churn.
                if (delay <= TimeSpan.Zero)
                {
                    Logger.Debug(
                        "DoDespawn: Doodad TemplateId {0} is scheduled but has no end time; leaving it in place",
                        doodad.TemplateId);
                    return;
                }

                Logger.Debug("DoDespawn: Doodad TemplateId {0} stays for another {1}", doodad.TemplateId, delay);
                TaskManager.Instance.Schedule(new DoodadSpawnerDoDespawnTask(doodad), delay);
                return; // Reschedule when OK
            }

            // The window has closed. Take the doodad away and wait for the next opening, rather
            // than leaving it standing until the server restarts.
            Despawn(doodad);
            var nextStart = GameScheduleManager.Instance.GetRemainingTimeDoodad((int)doodad.TemplateId, true);
            if (nextStart > TimeSpan.Zero)
            {
                Logger.Debug("DoDespawn: Doodad TemplateId {0} removed, next window in {1}", doodad.TemplateId, nextStart);
                TaskManager.Instance.Schedule(new DoodadSpawnerDoSpawnTask(this), nextStart);
            }

            return;
        }
        #endregion Schedule

        Despawn(doodad);
        Logger.Debug("DoDespawn: Doodad TemplateId {0}, objId {1} FuncGroupId {2} spawn [2] reschedule next time...", UnitId, Last.ObjId, Last.FuncGroupId);
        TaskManager.Instance.Schedule(new DoodadSpawnerDoSpawnTask(this), TimeSpan.FromSeconds(1));
    }

    public void DoSpawn()
    {
        #region Schedule
        // First, let's check if the schedule has such an spawnerId
        if (GameScheduleManager.Instance.CheckDoodadInScheduleSpawners((int)UnitId))
        {
            // CheckDoodadInGameSchedules answers whether the window is open right now, and the
            // two branches used to be the wrong way round: a doodad whose event was running was
            // postponed, and one whose event was over was placed. Festival decorations stood in
            // the world all year and vanished during the festival.
            if (GameScheduleManager.Instance.CheckDoodadInGameSchedules(UnitId))
            {
                // The window is open, so the doodad belongs in the world now. It is not permanent:
                // the despawn chain below will take it away when the window closes.
                _permanent = false;
            }
            else
            {
                var delay = GameScheduleManager.Instance.GetRemainingTimeDoodad((int)UnitId, true);

                // A doodad can be listed in the doodad schedule and absent from the spawner one,
                // and then the remaining time comes back as zero - which used to reschedule this
                // very method for right now, over and over, as fast as the task manager would run
                // it. Treat a delay that says nothing as no schedule at all and place the doodad,
                // which is what happens for anything outside a schedule anyway.
                if (delay <= TimeSpan.Zero)
                {
                    Logger.Debug(
                        "DoSpawn: Doodad TemplateId {0} is scheduled but has no start time; spawning it",
                        UnitId);
                }
                else
                {
                    _permanent = false; // Doodad on the schedule.
                    Logger.Debug("DoSpawn: Doodad TemplateId {0} waits {1} for its next window", UnitId, delay);
                    TaskManager.Instance.Schedule(new DoodadSpawnerDoSpawnTask(this), delay);
                    return; // Reschedule when OK
                }
            }
        }
        #endregion Schedule

        Last.Spawn(); // initialize Doodad with the initial phase and display it on the terrain

        var world = WorldManager.Instance.GetWorld(Last.Transform.WorldId);
        if (Last.Transform.WorldId > 0)
        {
            // Temporary range for instanced worlds
            var dungeon = IndunManager.Instance.GetDungeonByWorldId(Last.Transform.WorldId);

            if (dungeon is not null)
            {
                //dungeon.RegisterIndunEvents();
                world.Events.OnDoodadSpawn(world, new OnDoodadSpawnArgs { Doodad = Last });
            }
        }

        _spawned.Add(Last);
        if (!_permanent)
        {
            Logger.Debug("DoSpawn: Doodad TemplateId {0}, objId {1} FuncGroupId {2} despawn [2] reschedule next time...", UnitId, Last.ObjId, Last.FuncGroupId);
            TaskManager.Instance.Schedule(new DoodadSpawnerDoDespawnTask(Last), TimeSpan.FromSeconds(1));
        }

        if (_scheduledCount > 0)
        {
            _scheduledCount--;
        }
        _spawnCount = _spawned.Count;
        if (_spawnCount < 0)
        {
            _spawnCount = 0;
        }
    }
}
