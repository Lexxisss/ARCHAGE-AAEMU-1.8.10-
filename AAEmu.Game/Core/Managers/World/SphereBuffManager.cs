using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Hands out the buffs that belong to a place: the harbour that lets a ship be refitted, the
/// prison that marks a trespasser, and every other sphere the client database describes as a
/// SphereBuff.
/// </summary>
/// <remarks>
/// The client database says which buff a sphere carries and who may receive it, but not where the
/// sphere is. The volume lives in the client's level design, in the same shape the quest spheres
/// use: <c>game/worlds/&lt;world&gt;/level_design/zone/&lt;zoneKey&gt;/world_server/quest_area_sphere.g</c>,
/// where each block names the sphere by its id in <c>stype</c>. It is read through
/// <see cref="ClientFileManager"/>, so a packed game_pak serves it like any other client file.
/// </remarks>
public class SphereBuffManager : Singleton<SphereBuffManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private const string SphereFileName = "quest_area_sphere.g";

    /// <summary>Areas by world id: only worlds that hold any are present.</summary>
    private Dictionary<uint, List<SphereBuffArea>> _areasByWorld = new();

    /// <summary>Which spheres each unit is currently standing in, by object id.</summary>
    private readonly Dictionary<uint, HashSet<uint>> _inside = new();

    private readonly HashSet<uint> _loadedBuffIds = new();

    private readonly object _lock = new();

    public void Load()
    {
        var areas = new Dictionary<uint, List<SphereBuffArea>>();
        var withoutDefinition = 0;

        foreach (var world in WorldManager.Instance.GetWorlds())
        {
            var zoneRoot = Path.Combine("game", "worlds", world.Name, "level_design", "zone");
            var files = ClientFileManager.GetFilesInDirectory(zoneRoot, SphereFileName, true);

            foreach (var fileName in files)
            {
                // <zoneKey>/world_server/<file>: the zone key is two directories up.
                var zoneDirectory = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(fileName)));
                if (!uint.TryParse(zoneDirectory, out var zoneKey))
                {
                    Logger.Warn("Unable to parse the zone key from {0}", fileName);
                    continue;
                }

                var contents = ClientFileManager.GetFileAsString(fileName);
                if (string.IsNullOrWhiteSpace(contents))
                    continue;

                foreach (var volume in SphereVolumeReader.Read(contents))
                {
                    var area = BuildArea(world.Id, zoneKey, volume.SphereId, volume.Position, volume.Radius);
                    if (area == null)
                    {
                        withoutDefinition++;
                        continue;
                    }

                    if (!areas.TryGetValue(world.Id, out var worldAreas))
                    {
                        worldAreas = new List<SphereBuffArea>();
                        areas.Add(world.Id, worldAreas);
                    }

                    worldAreas.Add(area);
                }
            }
        }

        lock (_lock)
        {
            _areasByWorld = areas;
            _inside.Clear();
            _loadedBuffIds.Clear();
            foreach (var area in areas.Values.SelectMany(list => list))
                _loadedBuffIds.Add(area.BuffId);
        }

        var total = areas.Values.Sum(list => list.Count);
        Logger.Info(
            "Loaded {0} buff spheres across {1} worlds ({2} volumes belong to spheres of another kind)",
            total, areas.Count, withoutDefinition);
    }

    public void Initialize()
    {
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(500), true);
    }

    /// <summary>
    /// Whether any placed sphere hands out this buff. A server without the client's level design
    /// loads none, and callers that gate on such a buff can tell that apart from a unit simply
    /// not having it.
    /// </summary>
    public bool IsBuffPlacedInWorld(uint buffId)
    {
        lock (_lock)
        {
            return _loadedBuffIds.Contains(buffId);
        }
    }

    public void Tick(TimeSpan delta)
    {
        try
        {
            lock (_lock)
            {
                if (_areasByWorld.Count == 0)
                    return;

                var seen = new HashSet<uint>();

                // Ships are what the harbour spheres are for, but a sphere buffs whatever stands
                // in it, so players are walked too. One pass each, and only the worlds that hold
                // a sphere cost anything.
                foreach (var character in WorldManager.Instance.GetAllCharacters())
                {
                    if (character?.Transform != null &&
                        _areasByWorld.TryGetValue(character.Transform.WorldId, out var areas))
                        UpdateUnit(character, areas, seen);
                }

                foreach (var slave in WorldManager.Instance.GetAllSlaves())
                {
                    if (slave?.Transform != null &&
                        _areasByWorld.TryGetValue(slave.Transform.WorldId, out var areas))
                        UpdateUnit(slave, areas, seen);
                }

                // A unit that is gone takes its buffs with it; drop what we remembered about it.
                foreach (var objId in _inside.Keys.Where(objId => !seen.Contains(objId)).ToList())
                    _inside.Remove(objId);
            }
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "Error while updating sphere buffs");
        }
    }

    private void UpdateUnit(Unit unit, List<SphereBuffArea> areas, HashSet<uint> seen)
    {
        if (unit?.Transform == null || unit.IsDead)
            return;

        seen.Add(unit.ObjId);

        var position = unit.Transform.World.Position;
        if (!_inside.TryGetValue(unit.ObjId, out var previous))
        {
            previous = new HashSet<uint>();
            _inside.Add(unit.ObjId, previous);
        }

        var current = new HashSet<uint>();
        foreach (var area in areas)
        {
            if (!area.Contains(position) || !area.Accepts(unit))
                continue;

            current.Add(area.SphereId);
            if (previous.Contains(area.SphereId))
                continue;

            Apply(unit, area);
        }

        foreach (var area in areas)
        {
            if (current.Contains(area.SphereId) || !previous.Contains(area.SphereId))
                continue;

            Withdraw(unit, area);
        }

        _inside[unit.ObjId] = current;
    }

    private static void Apply(Unit unit, SphereBuffArea area)
    {
        if (!unit.Buffs.CheckBuff(area.BuffId))
            unit.Buffs.AddBuff(area.BuffId, unit);

        if (area.AndPet)
        {
            foreach (var mate in MateManager.Instance.GetActiveMates(unit.ObjId) ?? new List<Mate>())
            {
                if (mate != null && !mate.Buffs.CheckBuff(area.BuffId))
                    mate.Buffs.AddBuff(area.BuffId, mate);
            }
        }

        Logger.Trace(
            "Sphere {0} ({1}) gave buff {2} to {3}",
            area.SphereId, area.Name, area.BuffId, unit.ObjId);
    }

    private static void Withdraw(Unit unit, SphereBuffArea area)
    {
        // Zero means the sphere takes nothing back on the way out.
        if (area.RemoveOnLeaveBuffId == 0)
            return;

        unit.Buffs.RemoveBuff(area.RemoveOnLeaveBuffId);

        if (area.AndPet)
        {
            foreach (var mate in MateManager.Instance.GetActiveMates(unit.ObjId) ?? new List<Mate>())
                mate?.Buffs.RemoveBuff(area.RemoveOnLeaveBuffId);
        }

        Logger.Trace(
            "Sphere {0} ({1}) took buff {2} back from {3}",
            area.SphereId, area.Name, area.RemoveOnLeaveBuffId, unit.ObjId);
    }

    private static SphereBuffArea BuildArea(uint worldId, uint zoneKey, uint sphereId, Vector3 localPosition, float radius)
    {
        var sphere = SphereGameData.Instance.GetSphere(sphereId);
        if (sphere == null || sphere.SphereDetailType != "SphereBuff")
            return null;

        var sphereBuff = SphereGameData.Instance.GetSphereBuff(sphere.SphereDetailId);
        if (sphereBuff == null || sphereBuff.BuffId == 0)
            return null;

        var world = ZoneManager.ConvertToWorldCoordinates(zoneKey, localPosition);

        return new SphereBuffArea
        {
            SphereId = sphereId,
            Name = sphere.Name,
            WorldId = worldId,
            ZoneKey = zoneKey,
            Position = world,
            Radius = radius,
            BuffId = sphereBuff.BuffId,
            RemoveOnLeaveBuffId = sphereBuff.RemoveOnLeaveBuffId,
            AndPet = sphereBuff.AndPet,
            OrUnitReqs = sphere.OrUnitReqs,
            Requirements = SphereGameData.Instance.GetSphereUnitRequirements(sphereId)
        };
    }

}
