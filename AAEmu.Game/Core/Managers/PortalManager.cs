using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Exceptions;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.OpenPortal;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.World;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using NLog;

using Portal = AAEmu.Game.Models.Game.Portal;

namespace AAEmu.Game.Core.Managers;

public class PortalManager : Singleton<PortalManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    //private Dictionary<uint, uint> _allDistrictPortalsKey;
    //private Dictionary<uint, Portal> _allDistrictPortals;

    private Dictionary<uint, List<Portal>> _recalls;
    private Dictionary<uint, uint> _recallsKey;
    private Dictionary<uint, Portal> _respawns;
    private Dictionary<uint, uint> _respawnsKey;
    private Dictionary<uint, Portal> _worldgates;
    private Dictionary<uint, uint> _worldgatesKey;

    private Dictionary<uint, OpenPortalReagents> _openPortalInlandReagents;
    private Dictionary<uint, OpenPortalReagents> _openPortalOutlandReagents;
    private Dictionary<uint, DistrictReturnPoints> _districtReturnPoints;

    //public Portal GetPortalBySubZoneId(uint subZoneId)
    //{
    //    return _allDistrictPortals != null && _allDistrictPortals.ContainsKey(subZoneId)
    //        ? _allDistrictPortals[subZoneId]
    //        : null;
    //}

    //public Portal GetPortalById(uint id)
    //{
    //    return _allDistrictPortalsKey != null && _allDistrictPortalsKey.ContainsKey(id)
    //        ? _allDistrictPortals.ContainsKey(_allDistrictPortalsKey[id])
    //            ? _allDistrictPortals[_allDistrictPortalsKey[id]]
    //            : null
    //        : null;
    //}

    public List<Portal> GetRecallBySubZoneId(uint subZoneId)
    {
        return _recalls != null && _recalls.TryGetValue(subZoneId, out var recall)
            ? recall
            : null;
    }

    public Portal GetRecallById(uint returnPointId)
    {
        if (_recallsKey == null || !_recallsKey.ContainsKey(returnPointId)) { return null; }
        if (!_recalls.ContainsKey(_recallsKey[returnPointId])) { return null; }

        return _recalls[_recallsKey[returnPointId]].FirstOrDefault(portal => portal.Id == returnPointId);
    }

    public Portal GetRespawnBySubZoneId(uint subZoneId)
    {
        return _respawns != null && _respawns.TryGetValue(subZoneId, out var respawn)
            ? respawn
            : null;
    }

    public Portal GetRespawnById(uint id)
    {
        return _respawnsKey != null && _respawnsKey.ContainsKey(id)
            ? _respawns.ContainsKey(_respawnsKey[id])
                ? _respawns[_respawnsKey[id]]
                : null
            : null;
    }

    public Portal GetWorldgatesBySubZoneId(uint subZoneId)
    {
        return _worldgates != null && _worldgates.TryGetValue(subZoneId, out var worldgate)
            ? worldgate
            : null;
    }

    public Portal GetWorldgatesById(uint id)
    {
        return _worldgatesKey != null && _worldgatesKey.ContainsKey(id)
            ? _worldgates.ContainsKey(_worldgatesKey[id])
                ? _worldgates[_worldgatesKey[id]]
                : null
            : null;
    }

    /// <summary>
    /// GetDistrictReturnPoint - вернуть точку возврата для соответствующего DistrictId
    /// </summary>
    /// <param name="districtId"></param>
    /// <returns>ReturnPointId</returns>
    public uint GetDistrictReturnPoint(uint districtId)
    {
        return (
            from point in _districtReturnPoints
            where point.Value.DistrictId == districtId
            select point.Value.ReturnPointId).FirstOrDefault();
    }

    /// <summary>
    /// GetDistrictReturnPoint - вернуть точку возврата для соответствующего DistrictId и FactionId, так как точки для фракций могут быть разные
    /// </summary>
    /// <param name="districtId"></param>
    /// <param name="factionId"></param>
    /// <returns>ReturnPointId</returns>
    public uint GetDistrictReturnPoint(uint districtId, uint factionId)
    {
        return (
            from point in _districtReturnPoints
            where point.Value.DistrictId == districtId
            where point.Value.FactionId == factionId
            select point.Value.ReturnPointId).FirstOrDefault();
    }

    public void Load()
    {
        _openPortalInlandReagents = new Dictionary<uint, OpenPortalReagents>();
        _openPortalOutlandReagents = new Dictionary<uint, OpenPortalReagents>();
        //_allDistrictPortals = new Dictionary<uint, Portal>();
        //_allDistrictPortalsKey = new Dictionary<uint, uint>();
        _districtReturnPoints = new Dictionary<uint, DistrictReturnPoints>();

        _recalls = new Dictionary<uint, List<Portal>>();
        _respawns = new Dictionary<uint, Portal>();
        _worldgates = new Dictionary<uint, Portal>();
        _recallsKey = new Dictionary<uint, uint>();
        _respawnsKey = new Dictionary<uint, uint>();
        _worldgatesKey = new Dictionary<uint, uint>();

        Logger.Info("Loading Portals ...");

        #region FileManager

        //var filePath = Path.Combine(FileManager.AppPath, "Data", "Portal", "SubZonePortalCoords.json");
        //if (!File.Exists(filePath))
        //    throw new IOException($"File {filePath} doesn't exists !");

        //var contents = FileManager.GetFileContents(filePath);

        //if (string.IsNullOrWhiteSpace(contents))
        //    throw new IOException($"File {filePath} is empty !");

        //if (JsonHelper.TryDeserializeObject(contents, out List<Portal> portals, out _))
        //    foreach (var portal in portals)
        //    {
        //        if (!_allDistrictPortals.ContainsKey(portal.SubZoneId))
        //        {
        //            _allDistrictPortals.Add(portal.SubZoneId, portal);
        //        }
        //        if (!_allDistrictPortalsKey.ContainsKey(portal.Id))
        //        {
        //            _allDistrictPortalsKey.Add(portal.Id, portal.SubZoneId);
        //        }

        //        _recalls.Add(portal.SubZoneId, portal);
        //    }
        //else
        //    throw new Exception($"PortalManager: Parse {filePath} file");

        //Logger.Info("Loaded {0} District Portals", _allDistrictPortals.Count);

        var filePath = Path.Combine(FileManager.AppPath, "Data", "Portal", "recalls.json");
        if (!File.Exists(filePath))
            throw new IOException($"File {filePath} doesn't exists !");

        var contents = FileManager.GetFileContents(filePath);

        if (string.IsNullOrWhiteSpace(contents))
            throw new IOException($"File {filePath} is empty !");

        if (JsonHelper.TryDeserializeObject(contents, out List<Portal> recalls, out _))
            foreach (var recall in recalls)
            {
                var rp = new List<Portal>();
                if (!_recalls.ContainsKey(recall.SubZoneId))
                {
                    rp.Add(recall);
                    _recalls.Add(recall.SubZoneId, rp);
                }
                else
                {
                    _recalls[recall.SubZoneId].Add(recall);
                }

                if (!_recallsKey.ContainsKey(recall.Id))
                {
                    _recallsKey.Add(recall.Id, recall.SubZoneId);
                }
                else
                {
                    //
                }
            }
        else
            throw new GameException($"PortalManager: Parse {filePath} file");

        Logger.Info("Loaded {0} Recall Portals", _recalls.Count);

        filePath = Path.Combine(FileManager.AppPath, "Data", "Portal", "respawns.json");
        if (!File.Exists(filePath))
            throw new IOException($"File {filePath} doesn't exists !");

        contents = FileManager.GetFileContents(filePath);

        if (string.IsNullOrWhiteSpace(contents))
            throw new IOException($"File {filePath} is empty !");

        if (JsonHelper.TryDeserializeObject(contents, out List<Portal> respawns, out _))
            foreach (var respawn in respawns)
            {
                if (_respawns.ContainsKey(respawn.SubZoneId))
                {
                    //
                }
                _respawns.Add(respawn.SubZoneId, respawn);
                _respawnsKey.Add(respawn.Id, respawn.SubZoneId);
            }
        else
            throw new GameException($"PortalManager: Parse {filePath} file");

        Logger.Info("Loaded {0} Respawn Portals", _respawns.Count);

        filePath = Path.Combine(FileManager.AppPath, "Data", "Portal", "worldgates.json");
        if (!File.Exists(filePath))
            throw new IOException($"File {filePath} doesn't exists !");

        contents = FileManager.GetFileContents(filePath);

        if (string.IsNullOrWhiteSpace(contents))
            throw new IOException($"File {filePath} is empty !");

        if (JsonHelper.TryDeserializeObject(contents, out List<Portal> worldgates, out _))
            foreach (var worldgate in worldgates)
            {
                // A destination outside the main world names its world instead of numbering it,
                // because the number is only a position in the order the client's worlds are
                // enumerated. A name that resolves to nothing is dropped: a portal pointing at
                // the wrong world would land the player in open ground somewhere else entirely.
                if (!string.IsNullOrEmpty(worldgate.World) && worldgate.WorldId == 0)
                {
                    var namedWorld = WorldManager.Instance.GetWorlds()
                        .FirstOrDefault(w => string.Equals(w.Name, worldgate.World, StringComparison.OrdinalIgnoreCase));
                    if (namedWorld == null)
                    {
                        Logger.Warn(
                            "Worldgate {0} ({1}) names world '{2}', which this client does not have; skipped",
                            worldgate.Id, worldgate.Name, worldgate.World);
                        continue;
                    }

                    worldgate.WorldId = namedWorld.Id;
                }

                _worldgates.Add(worldgate.SubZoneId, worldgate);
                _worldgatesKey.Add(worldgate.Id, worldgate.SubZoneId);
            }
        else
            throw new GameException($"PortalManager: Parse {filePath} file");

        Logger.Info("Loaded {0} Worldgate Portals", _worldgates.Count);

        #endregion

        #region Sqlite

        using (var connection = SQLite.CreateConnection())
        {
            // NOTE - priority -> to remove item from inventory first
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM open_portal_inland_reagents";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var template = new OpenPortalReagents
                    {
                        Id = reader.GetUInt32("id"),
                        OpenPortalEffectId = reader.GetUInt32("open_portal_effect_id"),
                        ItemId = reader.GetUInt32("item_id"),
                        Amount = reader.GetInt32("amount"),
                        Priority = reader.GetInt32("priority")
                    };
                    _openPortalInlandReagents.Add(template.Id, template);
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM open_portal_outland_reagents";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var template = new OpenPortalReagents
                    {
                        Id = reader.GetUInt32("id"),
                        OpenPortalEffectId = reader.GetUInt32("open_portal_effect_id"),
                        ItemId = reader.GetUInt32("item_id"),
                        Amount = reader.GetInt32("amount"),
                        Priority = reader.GetInt32("priority")
                    };
                    _openPortalOutlandReagents.Add(template.Id, template);
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM district_return_points";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var template = new DistrictReturnPoints
                    {
                        Id = reader.GetUInt32("id"),
                        DistrictId = reader.GetUInt32("district_id"),
                        FactionId = reader.GetUInt32("faction_id"),
                        ReturnPointId = reader.GetUInt32("return_point_id")
                    };
                    _districtReturnPoints.TryAdd(template.Id, template);
                }
            }
        }
        Logger.Info("Loaded Portal Info");
        #endregion
    }

    public static bool CheckItemAndRemove(Character owner, uint itemId, int amount)
    {
        if (!owner.Inventory.CheckItems(SlotType.Inventory, itemId, amount)) return false;
        owner.Inventory.Bag.ConsumeItem(ItemTaskType.Teleport, itemId, amount, null);
        return true;
        /*
        var items = owner.Inventory.RemoveItem(itemId, amount);
        var tasks = new List<ItemTask>();
        foreach (var (item, count) in items)
        {
            if (item.Count == 0)
                tasks.Add(new ItemRemove(item));
            else
                tasks.Add(new ItemCountUpdate(item, -count));
        }
        owner.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Teleport, tasks, new List<ulong>()));
        return true;
        */
    }

    private bool CheckCanOpenPortal(Character owner, uint targetZoneId)
    {
        var targetContinent = ZoneManager.Instance.GetTargetIdByZoneId(targetZoneId);
        var ownerContinent = ZoneManager.Instance.GetTargetIdByZoneId(owner.Transform.ZoneId);

        if (targetContinent == ownerContinent)
        {
            foreach (var (_, value) in _openPortalInlandReagents)
            {
                if (CheckItemAndRemove(owner, value.ItemId, value.Amount)) return true;
            }
        }
        else
        {
            foreach (var (_, value) in _openPortalOutlandReagents)
            {
                if (CheckItemAndRemove(owner, value.ItemId, value.Amount)) return true;
            }
        }
        return false; // Not enough items
    }

    private static void MakePortal(Unit owner, bool isExit, Portal portalInfo, SkillObjectUnk1 portalEffectObj)
    {
        // 3891 - Portal Entrance
        // 6949 - Portal Exit
        var portalPointDestination = new Transform(null, null,
            WorldManager.Instance.GetWorldByZone(portalInfo.ZoneId).Id, portalInfo.ZoneId,
            WorldManager.DefaultInstanceId, portalInfo.X, portalInfo.Y, portalInfo.Z,
            0f, 0f, portalInfo.ZRot);
        var portalPointLocation = new Transform(null, null,
            owner.Transform.WorldId, owner.Transform.ZoneId, owner.Transform.InstanceId,
            portalEffectObj.X, portalEffectObj.Y, portalEffectObj.Z,
            owner.Transform.World.Rotation.X, owner.Transform.World.Rotation.Y, owner.Transform.World.Rotation.Z);
        var templateId = isExit ? 6949u : 3891u; // TODO - better way? maybe not hardcoded
        var template = NpcManager.Instance.GetTemplate(templateId);
        var portalUnitModel = new Models.Game.Units.Portal
        {
            ObjId = ObjectIdManager.Instance.GetNextId(),
            OwnerId = ((Character)owner).Id,
            TemplateId = templateId,
            Template = template,
            ModelId = template.ModelId,
            Faction = owner.Faction, // INFO - FactionManager.Instance.GetFaction(template.FactionId)
            Level = template.Level,
            Transform = isExit ? portalPointDestination : portalPointLocation,
            Name = portalInfo.Name,
            Hp = 955, // BUG - portal.MaxHp does not work 1.0
            Mp = 290, // TODO - portal.MaxMp
            TeleportPosition = portalPointDestination
        };
        portalUnitModel.Spawn();

        var killTask = new KillPortalTask(portalUnitModel);
        TaskManager.Instance.Schedule(killTask, TimeSpan.FromSeconds(30));
    }

    public void OpenPortal(Character owner, SkillObjectUnk1 portalEffectObj)
    {
        var portalInfo = owner.Portals.GetPortalInfo((uint)portalEffectObj.Id);
        if (!CheckCanOpenPortal(owner, portalInfo.ZoneId)) return;

        MakePortal(owner, false, portalInfo, portalEffectObj);   // Entrance (green)
        MakePortal(owner, true, portalInfo, portalEffectObj);    // Exit (yellow)
    }

    public static void UsePortal(Character character, uint objId)
    {
        // TODO - Cooldown between portals
        var portalInfo = (Models.Game.Units.Portal)WorldManager.Instance.GetNpc(objId);
        if (portalInfo == null) return;

        var destination = new WorldSpawnPosition
        {
            WorldId = portalInfo.TeleportPosition.WorldId,
            ZoneId = portalInfo.TeleportPosition.ZoneId,
            X = portalInfo.TeleportPosition.World.Position.X,
            Y = portalInfo.TeleportPosition.World.Position.Y,
            Z = portalInfo.TeleportPosition.World.Position.Z,
            Yaw = portalInfo.TeleportPosition.World.Rotation.Z.DegToRad()
        };

        CharacterTeleportManager.Teleport(character, destination, TeleportReason.Portal,
            portalInfo.TeleportPosition.InstanceId);
    }

    public static void DeletePortal(Character owner, byte type, uint id)
    {
        var isPrivate = type != 1;
        var portalInfo = owner.Portals.GetPortalInfo(id);
        if (portalInfo == null) return;
        owner.Portals.RemoveFromBookPortal(portalInfo, isPrivate);
    }

    public Portal GetClosestReturnPortal(Character character)
    {
        if (character == null || _respawns == null || _respawns.Count == 0)
            return null;

        // The sub-zone mapping is authoritative when present. Falling back to a raw nearest-point
        // search across every world can select a Nui from another continent/instance whose local
        // coordinates merely happen to be closer.
        if (_respawns.TryGetValue(character.SubZoneId, out var localRespawn) &&
            (localRespawn.WorldId == 0 || localRespawn.WorldId == character.Transform.WorldId))
            return localRespawn;

        var characterPosition = character.Transform.World.Position;
        var bestDistance = float.MaxValue;
        Portal closest = null;

        foreach (var value in _respawns.Values)
        {
            if (value == null ||
                (value.WorldId != 0 && value.WorldId != character.Transform.WorldId))
                continue;

            var portalPosition = new Vector3(value.X, value.Y, value.Z);
            var distance = MathUtil.CalculateDistance(characterPosition, portalPosition);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            closest = value;
        }

        return closest;
    }
}
