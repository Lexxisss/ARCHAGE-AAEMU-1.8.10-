using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.Slave;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

#pragma warning disable CA2000 // Dispose objects before losing scope

using NLog;
using Newtonsoft.Json.Linq;

namespace AAEmu.Game.Core.Managers;

public class SlaveManager : Singleton<SlaveManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private Dictionary<uint, SlaveTemplate> _slaveTemplates;
    private Dictionary<uint, Slave> _activeSlaves;
    private List<Slave> _testSlaves;
    private Dictionary<uint, Slave> _tlSlaves;
    public Dictionary<uint, Dictionary<AttachPointKind, WorldSpawnPosition>> _attachPoints;
    public Dictionary<uint, List<SlaveInitialItems>> _slaveInitialItems; // PackId and List<Slot/ItemData>

    // Client slave-equipment metadata. The full database also contains kind/pack requirement
    // graphs; for the runtime wire layer we need the authoritative set of slave equipment item
    // templates and the protocol slots exposed by each slave.
    private HashSet<uint> _slaveEquipmentItemTemplates;
    private Dictionary<uint, HashSet<byte>> _slaveEquipmentSlots;
    private Dictionary<uint, Dictionary<byte, SlaveEquipmentSlotInfo>> _slaveEquipmentSlotInfo;
    private Dictionary<ulong, SlaveEquipmentSpawnInfo> _slaveEquipmentGradeSpawns;

    private sealed class SlaveEquipmentSlotInfo
    {
        public AttachPointKind AttachPoint { get; init; }
        public int RequireBuffTagId { get; init; }
        public int RequireSlotId { get; init; }
    }

    private sealed class SlaveEquipmentSpawnInfo
    {
        public uint DoodadId { get; init; }
        public uint ChildSlaveId { get; init; }
    }

    //public Dictionary<uint, SlaveMountSkills> _slaveMountSkills;
    public Dictionary<uint, List<uint>> _slaveMountSkills;
    public Dictionary<uint, uint> _repairableSlaves; // SlaveId, RepairEffectId

    private object _slaveListLock;

    public bool Exist(uint templateId)
    {
        return _slaveTemplates.ContainsKey(templateId);
    }

    public SlaveTemplate GetSlaveTemplate(uint id)
    {
        return _slaveTemplates.TryGetValue(id, out var template) ? template : null;
    }

    public Slave GetActiveSlaveByOwnerObjId(uint objId)
    {
        lock (_slaveListLock)
            return _activeSlaves.TryGetValue(objId, out var slave) ? slave : null;
    }

    /// <summary>
    /// Returns a list of all Slaves of specific SlaveKind
    /// </summary>
    /// <param name="kind"></param>
    /// <param name="worldId">When set, only return from specific world</param>
    /// <returns></returns>
    public IEnumerable<Slave> GetActiveSlavesByKind(SlaveKind kind, uint worldId = uint.MaxValue)
    {
        lock (_slaveListLock)
        {
            if (worldId >= uint.MaxValue)
                return _activeSlaves.Select(i => i.Value).Where(s => s.Template.SlaveKind == kind);

            return _activeSlaves.Select(i => i.Value).Where(s => (s.Template.SlaveKind == kind) && (s.Transform.WorldId == worldId));
        }
    }

    /// <summary>
    /// Returns a list of all Slaves of specific SlaveKind
    /// </summary>
    /// <param name="kinds"></param>
    /// <param name="worldId">When set, only return from specific world</param>
    /// <returns></returns>
    public IEnumerable<Slave> GetActiveSlavesByKinds(SlaveKind[] kinds, uint worldId = uint.MaxValue)
    {
        lock (_slaveListLock)
        {
            if (worldId >= uint.MaxValue)
                return _activeSlaves.Where(s => kinds.Contains(s.Value.Template.SlaveKind))
                    .Select(s => s.Value);

            return _activeSlaves.Where(s => kinds.Contains(s.Value.Template.SlaveKind))
                .Where(s => s.Value?.Transform.WorldId == worldId)
                .Select(s => s.Value);
        }
    }

    public Slave GetActiveSlaveByObjId(uint objId)
    {
        lock (_slaveListLock)
        {
            foreach (var slave in _activeSlaves.Values.Where(slave => slave.ObjId == objId))
            {
                return slave;
            }
        }
        return null;
    }
    public Slave GetTestSlaveByObjId(uint objId)
    {
        lock (_slaveListLock)
        {
            foreach (var slave in _testSlaves.Where(slave => slave.ObjId == objId))
            {
                return slave;
            }
        }
        return null;
    }
 
    public Slave GetSlaveByObjId(uint objId)
    {
        lock (_slaveListLock)
        {
            foreach (var slave in _activeSlaves.Values.Where(slave => slave.ObjId == objId))
            {
                return slave;
            }

            foreach (var slave in _testSlaves.Where(slave => slave.ObjId == objId))
            {
                return slave;
            }
        }
        return null;
    }

    /* Unused
    private Slave GetActiveSlaveByTlId(uint tlId)
    {
        lock (_slaveListLock)
        {
            foreach (var slave in _activeSlaves.Values)
            {
                if (slave.TlId == tlId) return slave;
            }
        }

        return null;
    }*/

    ///// <summary>
    ///// Get mount skill associated with slaveMountSkillId
    ///// </summary>
    ///// <param name="slaveMountSkillId"></param>
    ///// <returns></returns>
    //public uint GetSlaveMountSkillFromId(uint slaveMountSkillId)
    //{
    //    return _slaveMountSkills.TryGetValue(slaveMountSkillId, out var res) ? res.MountSkillId : 0;
    //}

    /// <summary>
    /// Gets a list of all mount skills for a given slave type
    /// </summary>
    /// <param name="slaveTemplateId"></param>
    /// <returns></returns>
    public List<uint> GetSlaveMountSkillList(uint slaveTemplateId)
    {
        foreach (var skills in _slaveMountSkills)
            if (skills.Key == slaveTemplateId)
                return skills.Value;

        return null;
    }

    /// <summary>Returns true when the client database classifies the item as slave equipment.</summary>
    public bool IsKnownSlaveEquipmentItem(uint itemTemplateId)
    {
        return _slaveEquipmentItemTemplates?.Contains(itemTemplateId) == true;
    }

    /// <summary>
    /// Validates the safe, recovered part of a ship/vehicle equipment request.
    /// </summary>
    /// <remarks>
    /// The client database contains additional pack, kind and requirement graphs whose exact
    /// runtime precedence is not fully recovered. We therefore reject non-slave items and
    /// out-of-range slots, while treating a missing direct slave_equip_slots row as diagnostic
    /// rather than fatal: dual slots and pack-provided slots legitimately omit a direct row.
    /// </remarks>
    public bool CanEquipSlaveItem(uint slaveTemplateId, uint itemTemplateId, int targetSlot)
    {
        if (targetSlot < 0 || targetSlot >= SlaveEquipmentContainer.ProtocolSlotCount)
            return false;

        if (!IsKnownSlaveEquipmentItem(itemTemplateId))
        {
            Logger.Warn(
                "Slave equipment rejected: slave={0}, itemTemplate={1}, slot={2} is not present in item_slave_equipments",
                slaveTemplateId, itemTemplateId, targetSlot);
            return false;
        }

        if (_slaveEquipmentSlots != null &&
            _slaveEquipmentSlots.TryGetValue(slaveTemplateId, out var slots) &&
            !slots.Contains((byte)targetSlot))
        {
            Logger.Debug(
                "Slave equipment slot {0} is not a direct slave_equip_slots row for slave {1}; " +
                "allowing known slave equipment because dual/pack slots are represented indirectly",
                targetSlot, slaveTemplateId);
        }

        return true;
    }

    private static ulong MakeEquipmentGradeKey(uint itemTemplateId, byte grade)
    {
        return ((ulong)itemTemplateId << 8) | grade;
    }

    private bool TryGetEquipmentAttachPoint(uint slaveTemplateId, byte slot, out AttachPointKind attachPoint)
    {
        attachPoint = AttachPointKind.None;
        return _slaveEquipmentSlotInfo != null &&
               _slaveEquipmentSlotInfo.TryGetValue(slaveTemplateId, out var slots) &&
               slots.TryGetValue(slot, out var slotInfo) &&
               (attachPoint = slotInfo.AttachPoint) != AttachPointKind.None;
    }

    private SlaveEquipmentSpawnInfo ResolveEquipmentSpawn(Item item, SlaveEquipmentTemplate template)
    {
        if (item != null && _slaveEquipmentGradeSpawns != null &&
            _slaveEquipmentGradeSpawns.TryGetValue(MakeEquipmentGradeKey(item.TemplateId, item.Grade), out var gradeSpawn))
            return gradeSpawn;

        return new SlaveEquipmentSpawnInfo
        {
            DoodadId = template?.DoodadId ?? 0,
            ChildSlaveId = template?.ChildSlaveId ?? 0
        };
    }

    /// <summary>
    /// Rebuilds the actual world components represented by EquipmentSlave item slots.
    /// The parent SCUnitState carries item instances, while sails/cannons/rudders are separate
    /// attached Doodad or Slave world objects selected by item_slave_equipments and grade spawns.
    /// </summary>
    public void SynchronizeEquipmentComponents(Slave slave)
    {
        if (slave?.Equipment == null || slave.Template == null || slave.Transform == null)
            return;

        // The ids of what was taken off are handed back only once everything that stays has been
        // put on again. Freeing them here let a lamp spawned two lines later take the object id of
        // the sail that had just been removed, and the client was told, in one breath, that the id
        // was gone, that it was a new doodad, and that it was still the old rig - so the sail it
        // had been told to remove stayed on the ship.
        var releasedIds = RemoveEquipmentComponents(slave);

        var spawnedDoodads = 0;
        var spawnedSlaves = 0;
        foreach (var item in slave.Equipment.GetSlottedItemsList()
                     .Where(x => x != null)
                     .OrderBy(x => x.Slot))
        {
            if (item.Slot < 0 || item.Slot >= SlaveEquipmentContainer.ProtocolSlotCount)
                continue;

            var slot = (byte)item.Slot;
            if (!TryGetEquipmentAttachPoint(slave.TemplateId, slot, out var attachPoint))
            {
                Logger.Warn(
                    "Slave equipment component skipped: parent={0}/{1}, slot={2}, item={3}; " +
                    "slave_equip_slots has no attach point",
                    slave.TemplateId, slave.ObjId, slot, item.TemplateId);
                continue;
            }

            if (item.Template is not SlaveEquipmentTemplate equipmentTemplate)
            {
                Logger.Warn(
                    "Slave equipment component skipped: parent={0}/{1}, slot={2}, item={3}; " +
                    "template class is {4}",
                    slave.TemplateId, slave.ObjId, slot, item.TemplateId,
                    item.Template?.GetType().Name ?? "null");
                continue;
            }

            var spawn = ResolveEquipmentSpawn(item, equipmentTemplate);
            if (spawn.DoodadId != 0 && spawn.ChildSlaveId != 0)
            {
                Logger.Error(
                    "Slave equipment item resolves to both doodad and slave: parent={0}, slot={1}, item={2}, " +
                    "doodad={3}, childSlave={4}",
                    slave.TemplateId, slot, item.TemplateId, spawn.DoodadId, spawn.ChildSlaveId);
                continue;
            }

            if (spawn.DoodadId != 0)
            {
                var component = SpawnEquipmentDoodad(slave, item, slot, attachPoint, spawn.DoodadId,
                    equipmentTemplate.DoodadScale <= 0f ? 1f : equipmentTemplate.DoodadScale);
                if (component != null)
                {
                    slave.EquipmentDoodads[slot] = component;
                    spawnedDoodads++;
                }
                continue;
            }

            if (spawn.ChildSlaveId != 0)
            {
                var component = SpawnEquipmentSlave(slave, item, slot, attachPoint, spawn.ChildSlaveId);
                if (component != null)
                {
                    slave.EquipmentSlaves[slot] = component;
                    spawnedSlaves++;
                }
                continue;
            }

            Logger.Warn(
                "Slave equipment item has no runtime component: parent={0}/{1}, slot={2}, item={3}, grade={4}",
                slave.TemplateId, slave.ObjId, slot, item.TemplateId, item.Grade);
        }

        foreach (var releasedId in releasedIds)
            ObjectIdManager.Instance.ReleaseId(releasedId);

        Logger.Info(
            "Slave equipment components synchronized: parent={0}/{1}, dbId={2}, items={3}, doodads={4}, childSlaves={5}",
            slave.TemplateId, slave.ObjId, slave.Id, slave.Equipment.Items.Count, spawnedDoodads, spawnedSlaves);
    }

    private Doodad SpawnEquipmentDoodad(
        Slave parent, Item equipmentItem, byte slot, AttachPointKind attachPoint, uint doodadId, float scale)
    {
        var doodadTemplate = DoodadManager.Instance.GetTemplate(doodadId);
        if (doodadTemplate == null)
        {
            Logger.Error(
                "Slave equipment doodad template missing: parent={0}, slot={1}, item={2}, doodad={3}",
                parent.TemplateId, slot, equipmentItem.TemplateId, doodadId);
            return null;
        }

        var doodad = new Doodad
        {
            ObjId = ObjectIdManager.Instance.GetNextId(),
            TemplateId = doodadId,
            OwnerObjId = parent.Summoner?.ObjId ?? parent.OwnerObjId,
            ParentObjId = parent.ObjId,
            AttachPoint = attachPoint,
            OwnerId = parent.OwnerId,
            PlantTime = parent.SpawnTime,
            OwnerType = DoodadOwnerType.Slave,
            OwnerDbId = parent.Id,
            Template = doodadTemplate,
            Data = (byte)attachPoint,
            ParentObj = parent,
            Faction = parent.Faction,
            Type2 = 1u,
            Spawner = null,
            IsPersistent = false
        };

        doodad.SetScale(scale);
        doodad.FuncGroupId = doodad.GetFuncGroupId();
        doodad.Transform = parent.Transform.CloneAttached(doodad);
        doodad.Transform.Parent = parent.Transform;
        if (equipmentItem.HasFlag(ItemFlag.HasUCC) && equipmentItem.UccId > 0)
            doodad.UccId = equipmentItem.UccId;

        ApplyAttachPointLocation(parent, doodad, attachPoint);
        parent.AttachedDoodads.Add(doodad);

        // Initialise before publication so SCDoodadCreated contains the active FuncGroupId and
        // non-zero TimeLeft. Sending create first made the growth task run server-side while the
        // target client still displayed an idle, uncharged breathing device.
        doodad.InitDoodad(false);
        doodad.Spawn();

        Logger.Info(
            "Slave equipment doodad spawned: parent={0}/{1}, slot={2}, item={3}, grade={4}, attach={5}, doodad={6}/{7}",
            parent.TemplateId, parent.ObjId, slot, equipmentItem.TemplateId, equipmentItem.Grade,
            (int)attachPoint, doodadId, doodad.ObjId);
        return doodad;
    }

    private Slave SpawnEquipmentSlave(
        Slave parent, Item equipmentItem, byte slot, AttachPointKind attachPoint, uint childTemplateId)
    {
        var childTemplate = GetSlaveTemplate(childTemplateId);
        if (childTemplate == null)
        {
            Logger.Error(
                "Slave equipment child template missing: parent={0}, slot={1}, item={2}, childSlave={3}",
                parent.TemplateId, slot, equipmentItem.TemplateId, childTemplateId);
            return null;
        }

        var childTlId = (ushort)TlIdManager.Instance.GetNextId();
        var childObjId = ObjectIdManager.Instance.GetNextId();
        var child = new Slave
        {
            TlId = childTlId,
            ObjId = childObjId,
            ParentObj = parent,
            TemplateId = childTemplate.Id,
            Name = childTemplate.Name,
            Level = (byte)childTemplate.Level,
            ModelId = childTemplate.ModelId,
            Template = childTemplate,
            Hp = 1,
            Mp = 1,
            ModelParams = new UnitCustomModelParams(),
            Faction = parent.Faction,
            // Attached sail/cannon components are world children, not independently owned personal
            // slaves. Sending the parent's summoner/db identity in SCSlaveStatus makes the target
            // client replace its current controllable slave with this child component.
            Id = 0,
            Summoner = null,
            OwnerObjId = parent.ObjId,
            OwnerId = parent.OwnerId,
            SpawnTime = DateTime.UtcNow,
            AttachPointId = (sbyte)attachPoint,
            Skills = new List<uint>()
        };

        var childSkills = MateManager.Instance.GetMateSkills(childTemplate.Id);
        if (childSkills is { Count: > 0 })
            child.Skills.AddRange(childSkills);

        ApplySlaveBonuses(child);
        child.Hp = child.MaxHp;
        child.Mp = child.MaxMp;
        child.Transform = parent.Transform.CloneDetached(child);
        child.Transform.Parent = parent.Transform;
        ApplyAttachPointLocation(parent, child, attachPoint);

        parent.AttachedSlaves.Add(child);
        lock (_slaveListLock)
            _tlSlaves[child.TlId] = child;
        child.Spawn();
        child.PostUpdateCurrentHp(child, 0, child.Hp, KillReason.Unknown);

        Logger.Info(
            "Slave equipment child spawned: parent={0}/{1}, slot={2}, item={3}, grade={4}, attach={5}, child={6}/{7}, tl={8}",
            parent.TemplateId, parent.ObjId, slot, equipmentItem.TemplateId, equipmentItem.Grade,
            (int)attachPoint, childTemplateId, child.ObjId, child.TlId);
        return child;
    }

    /// <summary>
    /// Takes every runtime component off the slave and returns the object ids it freed, for the
    /// caller to hand back once the components that stay have been spawned again.
    /// </summary>
    private List<uint> RemoveEquipmentComponents(Slave slave)
    {
        var releasedIds = new List<uint>();

        foreach (var pair in slave.EquipmentDoodads.ToList())
        {
            var doodad = pair.Value;
            slave.AttachedDoodads.Remove(doodad);
            doodad.IsPersistent = false;
            doodad.Delete();
            releasedIds.Add(doodad.ObjId);
        }
        slave.EquipmentDoodads.Clear();

        foreach (var pair in slave.EquipmentSlaves.ToList())
        {
            var child = pair.Value;
            slave.AttachedSlaves.Remove(child);
            lock (_slaveListLock)
                _tlSlaves.Remove(child.TlId);
            child.Delete();
            TlIdManager.Instance.ReleaseId(child.TlId);
            releasedIds.Add(child.ObjId);
        }
        slave.EquipmentSlaves.Clear();

        return releasedIds;
    }

    public void UnbindSlave(Character character, uint tlId, AttachUnitReason reason)
    {
        Slave slave;
        lock (_slaveListLock)
            slave = _tlSlaves[tlId];
        var attachPoint = slave.AttachedCharacters.FirstOrDefault(x => x.Value == character).Key;
        if (attachPoint != default)
        {
            slave.AttachedCharacters.Remove(attachPoint);
            character.Transform.Parent = null;
            character.Transform.StickyParent = null;
        }

        character.Buffs.TriggerRemoveOn(BuffRemoveOn.Unmount);
        character.AttachedPoint = AttachPointKind.None;

        character.BroadcastPacket(new SCUnitDetachedPacket(character.ObjId, reason), true);
    }

    public void BindSlave(Character character, uint objId, AttachPointKind attachPoint, AttachUnitReason bondKind)
    {
        // Check if the target spot is already taken
        Slave slave;
        lock (_slaveListLock)
            slave = _tlSlaves.FirstOrDefault(x => x.Value.ObjId == objId).Value;
        //var slave = GetActiveSlaveByObjId(objId);
        if (slave == null)
        {
            Logger.Warn($"BindSlave: no slave with objId {objId} for {character.Name} ({character.ObjId})");
            return;
        }

        // A refusal here is invisible from the outside: the client asked to sit down, got nothing
        // back, and goes on believing it is standing while the seat is held on this side. Nobody
        // then asks to get up from a seat they do not think they occupy, so the seat stays taken
        // for good. That deserves to say so in the log.
        if (slave.AttachedCharacters.TryGetValue(attachPoint, out var seatedCharacter))
        {
            Logger.Warn($"BindSlave: {attachPoint} of slave {slave.Name} ({slave.ObjId}) is already held by " +
                        $"{seatedCharacter?.Name} ({seatedCharacter?.ObjId}), refusing {character.Name} ({character.ObjId})");
            return;
        }

        character.BroadcastPacket(new SCUnitAttachedPacket(character.ObjId, attachPoint, bondKind, objId), true);
        character.AttachedPoint = attachPoint;
        switch (attachPoint)
        {
            case AttachPointKind.Driver:
                character.BroadcastPacket(new SCSlaveBoundPacket(character.Id, objId, (byte)character.Transform.WorldId), true);
                break;
        }

        slave.AttachedCharacters.Add(attachPoint, character);
        character.Transform.Parent = slave.Transform;
        // TODO: move to attach point's position
        character.Transform.Local.SetPosition(0, 0, 0, 0, 0, 0);
    }

    public void BindSlave(GameConnection connection, uint tlId)
    {
        var unit = connection.ActiveChar;
        Slave slave;
        lock (_slaveListLock)
            slave = _tlSlaves[tlId];

        BindSlave(unit, slave.ObjId, AttachPointKind.Driver, AttachUnitReason.NewMaster);
    }

    // TODO - GameConnection connection
    /// <summary>
    /// Removes a slave from the world
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="objId"></param>
    public void Delete(Character owner, uint objId)
    {
        var activeSlaveInfo = GetActiveSlaveByObjId(objId);
        var testSlaveInfo = GetTestSlaveByObjId(objId);
        // replace Slave with test ones from Mirage
        activeSlaveInfo ??= testSlaveInfo;
        if (activeSlaveInfo == null) return;

        Logger.Info(
            "Slave delete requested: owner={0}, template={1}, objId={2}, tl={3}, dbId={4}, " +
            "attachedDoodads={5}, attachedSlaves={6}, equipmentDoodads={7}, equipmentSlaves={8}",
            owner?.Id ?? 0, activeSlaveInfo.TemplateId, activeSlaveInfo.ObjId, activeSlaveInfo.TlId,
            activeSlaveInfo.Id, activeSlaveInfo.AttachedDoodads.Count, activeSlaveInfo.AttachedSlaves.Count,
            activeSlaveInfo.EquipmentDoodads.Count, activeSlaveInfo.EquipmentSlaves.Count);

        activeSlaveInfo.Save();
        // Remove passengers
        foreach (var character in activeSlaveInfo.AttachedCharacters.Values.ToList())
            UnbindSlave(character, activeSlaveInfo.TlId, AttachUnitReason.SlaveBinding);

        // Check if one of the ordinary cargo doodads is holding an item. Runtime equipment
        // components are classified separately and must never block recall even if a template or
        // later code happens to populate their item-looking fields.
        var equipmentDoodadIds = activeSlaveInfo.EquipmentDoodads.Values
            .Where(component => component != null)
            .Select(component => component.ObjId)
            .ToHashSet();
        foreach (var doodad in activeSlaveInfo.AttachedDoodads)
        {
            if (equipmentDoodadIds.Contains(doodad.ObjId))
                continue;

            if ((doodad.ItemId != 0) || (doodad.ItemTemplateId != 0))
            {
                Logger.Warn(
                    "Slave delete blocked by loaded doodad: slave={0}/{1}, doodad={2}/{3}, " +
                    "itemId={4}, itemTemplateId={5}, attach={6}",
                    activeSlaveInfo.TemplateId, activeSlaveInfo.ObjId, doodad.TemplateId, doodad.ObjId,
                    doodad.ItemId, doodad.ItemTemplateId, (int)doodad.AttachPoint);
                owner?.SendErrorMessage(ErrorMessageType.SlaveEquipmentLoadedItem); // TODO: Do we need this error? Client already mentions it.
                return; // don't allow un-summon if some it's holding a item (should be a trade-pack)
            }
        }

        var despawnDelayedTime = DateTime.UtcNow.AddSeconds(activeSlaveInfo.Template.PortalTime - 0.5f);

        activeSlaveInfo.Transform.DetachAll();

        foreach (var doodad in activeSlaveInfo.AttachedDoodads)
        {
            // Note, we un-check the persistent flag here, or else the doodad will delete itself from DB as well
            // This is not desired for player owned slaves
            if (owner != null)
                doodad.IsPersistent = false;
            doodad.Despawn = despawnDelayedTime;
            SpawnManager.Instance.AddDespawn(doodad);
            // doodad.Delete();
        }

        foreach (var attachedSlave in activeSlaveInfo.AttachedSlaves)
        {
            lock (_slaveListLock)
                _tlSlaves.Remove(attachedSlave.TlId);
            attachedSlave.Despawn = despawnDelayedTime;
            SpawnManager.Instance.AddDespawn(attachedSlave);
            //attachedSlave.Delete();
        }

        // These maps only classify runtime parts; the objects themselves are already scheduled
        // through AttachedDoodads/AttachedSlaves above.
        activeSlaveInfo.EquipmentDoodads.Clear();
        activeSlaveInfo.EquipmentSlaves.Clear();

        var world = WorldManager.Instance.GetWorld(activeSlaveInfo.Transform.WorldId);
        if (world != null && activeSlaveInfo.Template.IsABoat())
            world.Physics.RemoveShip(activeSlaveInfo);
        owner?.BroadcastPacket(new SCSlaveDespawnPacket(objId), true);
        owner?.BroadcastPacket(new SCSlaveRemovedPacket(owner.ObjId, activeSlaveInfo.TlId), true);
        lock (_slaveListLock)
        {
            _tlSlaves.Remove(activeSlaveInfo.TlId);
            if (testSlaveInfo == null)
            {
                if (owner != null)
                    _activeSlaves.Remove(owner.ObjId); // remove only the ones that we spawn from items
            }
            else
            {
                _testSlaves.Remove(activeSlaveInfo); // remove only the ones that we spawn from Mirage.
            }
        }

        activeSlaveInfo.Despawn = DateTime.UtcNow.AddSeconds(activeSlaveInfo.Template.PortalTime + 0.5f);
        SpawnManager.Instance.AddDespawn(activeSlaveInfo);
    }

    /// <summary>
    /// Slave created from spawn effect since this is a test vehicle from Mirage
    /// </summary>
    /// <param name="SubType">TemplateId</param>
    /// <param name="hideSpawnEffect"></param>
    /// <param name="positionOverride"></param>
    public Slave Create(uint SubType, Transform positionOverride = null)
    {
        var slave = Create(null, null, SubType, null, positionOverride);
        if (slave == null) return null;
        _testSlaves.Add(slave);

        return slave;
    }

    /// <summary>
    /// Slave created from spawn effect
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="skillData"></param>
    /// <param name="hideSpawnEffect"></param>
    /// <param name="positionOverride"></param>
    public void Create(Character owner, SkillItem skillData, Transform positionOverride = null)
    {
        var activeSlaveInfo = GetActiveSlaveByOwnerObjId(owner.ObjId);
        if (activeSlaveInfo != null)
        {
            activeSlaveInfo.Save();
            // TODO: If too far away, don't delete
            Delete(owner, activeSlaveInfo.ObjId);
            // return;
        }

        var item = owner.Inventory.GetItemById(skillData.ItemId);
        if (item == null) return;

        var itemTemplate = (SummonSlaveTemplate)ItemManager.Instance.GetTemplate(item.TemplateId);
        if (itemTemplate == null) return;

        Create(owner, null, itemTemplate.SlaveId, item, positionOverride); // TODO replace the underlying code with this call
    }

    // added "/slave spawn <templateId>" to be called from the script command
    /// <summary>
    /// Slave created by player or spawn effect, use either useSpawner or templateId
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="useSpawner"></param>
    /// <param name="templateId"></param>
    /// <param name="item"></param>
    /// <param name="hideSpawnEffect"></param>
    /// <param name="positionOverride"></param>
    /// <returns>Newly created Slave</returns>
    public Slave Create(Character owner, SlaveSpawner useSpawner, uint templateId, Item item = null, Transform positionOverride = null)
    {
        var slaveTemplate = GetSlaveTemplate(useSpawner?.UnitId ?? templateId);
        if (slaveTemplate == null) return null;

        var tlId = (ushort)TlIdManager.Instance.GetNextId();
        var objId = ObjectIdManager.Instance.GetNextId();

        using var spawnPos = positionOverride ?? new Transform(null);
        var spawnOffsetPos = new Vector3();

        var dbId = 0u;
        var slaveName = string.Empty;
        var slaveHp = 1;
        var slaveMp = 1;
        var isLoadedPlayerSlave = false;
        if ((owner?.Id > 0) && (item?.Id > 0))
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            // Sorting required to make make sure parenting doesn't produce invalid parents (normally)

            command.CommandText = "SELECT * FROM slaves  WHERE (owner = @playerId) AND (item_id = @itemId) LIMIT 1";
            command.Parameters.AddWithValue("playerId", owner.Id);
            command.Parameters.AddWithValue("itemId", item.Id);
            command.Prepare();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                dbId = reader.GetUInt32("id");
                // var slaveItemId = reader.GetUInt32("item_id");
                // var slaveOwnerId = reader.GetUInt32("owner");
                slaveName = reader.GetString("name");
                // var slaveCreatedAt = reader.GetDateTime("created_at");
                // var slaveUpdatedAt = reader.GetDateTime("updated_at");
                slaveHp = reader.GetInt32("hp");
                slaveMp = reader.GetInt32("mp");
                // Coords are saved, but not really used when summoning and are only required to show vehicle
                // location after a server restart (if it was still summoned)
                // var slaveX = reader.GetFloat("x");
                // var slaveY = reader.GetFloat("y");
                // var slaveZ = reader.GetFloat("z");
                isLoadedPlayerSlave = true;
                break;
            }
        }

        // TODO: Attach Slave's DbId to the Item Details
        // We currently fake the DbId using TlId instead

        if (spawnPos.Local.IsOrigin())
        {
            if (owner == null && useSpawner == null)
            {
                Logger.Warn($"Tried creating a slave without a defined position, either use a Owner, Spawner or PositionOverride");
                return null;
            }

            if (useSpawner != null)
            {
                spawnPos.ApplyWorldSpawnPosition(useSpawner.Position, WorldManager.DefaultInstanceId);
            }
            else
            {
                spawnPos.ApplyWorldTransformToLocalPosition(owner.Transform, owner.InstanceId);
            }

            // If no spawn position override has been provided, then handle normal spawning algorithm

            // owner.SendMessage("SlaveSpawnOffset: x:{0} y:{1}", slaveTemplate.SpawnXOffset, slaveTemplate.SpawnYOffset);
            if (owner != null)
            {
                spawnPos.Local.AddDistanceToFront(Math.Clamp(slaveTemplate.SpawnYOffset, 5f, 50f));
            }
            // INFO: Seems like X offset is defined as the size of the vehicle summoned, but visually it's nicer if we just ignore this 
            // spawnPos.Local.AddDistanceToRight(slaveTemplate.SpawnXOffset);
            if (slaveTemplate.IsABoat())
            {
                // If we're spawning a boat, put it at the water level regardless of our own height
                // TODO: if not at ocean level, get actual target location water body height (for example rivers)
                var world = WorldManager.Instance.GetWorld(spawnPos.WorldId);
                if (world == null)
                {
                    Logger.Fatal($"Unable to find world to spawn in {spawnPos.WorldId}");
                    return null;
                }

                var worldWaterLevel = world.Water.GetWaterSurface(spawnPos.World.Position);
                spawnPos.Local.SetHeight(worldWaterLevel);

                // temporary grab ship information so that we can use it to find a suitable spot in front to summon it
                var tempShipModel = ModelManager.Instance.GetShipModel(slaveTemplate.ModelId);
                if (tempShipModel == null)
                {
                    Logger.Error(
                        "Cannot summon boat slave {0} ({1}): model {2} does not resolve to ShipModel",
                        slaveTemplate.Name, slaveTemplate.Id, slaveTemplate.ModelId);
                    owner?.SendErrorMessage(ErrorMessageType.SlaveCannotSpawn);
                    TlIdManager.Instance.ReleaseId(tlId);
                    ObjectIdManager.Instance.ReleaseId(objId);
                    return null;
                }

                var minDepth = tempShipModel.MassBoxSizeZ - tempShipModel.MassCenterZ + 1f;

                // Somehow take into account where the ship will end up related to it's mass center (also check boat physics)
                spawnOffsetPos.Z += (tempShipModel.MassCenterZ < 0f ? (tempShipModel.MassCenterZ / 2f) : 0f) - tempShipModel.KeelHeight;

                for (var inFront = 0f; inFront < (50f + tempShipModel.MassBoxSizeX); inFront += 1f)
                {
                    using var depthCheckPos = spawnPos.CloneDetached();
                    depthCheckPos.Local.AddDistanceToFront(inFront);
                    var floorHeight = WorldManager.Instance.GetHeight(depthCheckPos);
                    if (floorHeight > 0f)
                    {
                        var surfaceHeight = world.Water.GetWaterSurface(depthCheckPos.World.Position);
                        var delta = surfaceHeight - floorHeight;
                        if (delta > minDepth)
                        {
                            //owner.SendMessage("Extra inFront = {0}, required Depth = {1}", inFront, minDepth);
                            spawnPos.Dispose();

                            spawnPos.ApplyWorldTransformToLocalPosition(depthCheckPos);
                            break;
                        }
                    }
                }

                spawnPos.Local.Position += spawnOffsetPos;
            }
            else
            {
                // If a land vehicle, put it a the ground level of it's target spawn location
                // TODO: check for maximum height difference for summoning
                var h = WorldManager.Instance.GetHeight(spawnPos);
                if (h > 0f)
                    spawnPos.Local.SetHeight(h);
            }

            // Always spawn horizontal(level) and 90° CCW of the player
            spawnPos.Local.SetRotation(0f, 0f, owner?.Transform.World.Rotation.Z + MathF.PI / 2 ?? useSpawner.Position.Yaw);
        }

        if (item is SummonSlave cooldownItem &&
            ((cooldownItem.IsDestroyed > 0) || (cooldownItem.RepairStartTime > DateTime.MinValue)))
        {
            var secondsLeft = (cooldownItem.RepairStartTime.AddMinutes(10) - DateTime.UtcNow).TotalSeconds;
            if (secondsLeft > 0.0)
            {
                // Slave was destroyed and is on cooldown.
                owner?.SendErrorMessage(
                    ErrorMessageType.SlaveSpawnErrorNeedRepairTime,
                    (uint)Math.Round(secondsLeft));
                return null;
            }
        }

        // A new player slave needs its persistent id before the source item is announced. Sending
        // SlaveDbId=0 and allocating the id afterwards leaves the summon item detached from the
        // equipment container and the client-side slave record that use that id.
        if ((owner?.Id > 0) && (dbId <= 0))
            dbId = CharacterIdManager.Instance.GetNextId(); // dbId = SlaveIdManager.Instance.GetNextId();

        var summonedSlave = new Slave();
        summonedSlave.TlId = tlId;
        summonedSlave.ObjId = objId;
        summonedSlave.TemplateId = slaveTemplate.Id;
        summonedSlave.Name = string.IsNullOrWhiteSpace(slaveName) ? slaveTemplate.Name : slaveName;
        summonedSlave.Level = (byte)slaveTemplate.Level;
        summonedSlave.ModelId = slaveTemplate.ModelId;
        summonedSlave.Template = slaveTemplate;
        summonedSlave.Hp = slaveHp;
        summonedSlave.Mp = slaveMp;
        summonedSlave.ModelParams = new UnitCustomModelParams();
        summonedSlave.Faction = owner?.Faction ?? FactionManager.Instance.GetFaction(slaveTemplate.FactionId);
        summonedSlave.Id = dbId;
        summonedSlave.Summoner = owner;
        summonedSlave.OwnerObjId = owner?.ObjId ?? 0;
        summonedSlave.OwnerId = owner?.Id ?? 0;
        summonedSlave.SummoningItem = item;
        summonedSlave.SpawnTime = DateTime.UtcNow;
        summonedSlave.Spawner = useSpawner;
        summonedSlave.Skills = new List<uint>();

        // Ships and land vehicles use the client-side slave equipment container (0xF2), not the
        // character equipment container (0x01). Persist it by the slave DB id so custom sails,
        // cannons and other rigging survive despawn/re-summon and server restarts.
        if (owner != null && dbId > 0)
        {
            var persistentEquipment = ItemManager.Instance.GetItemContainerForCharacter(
                owner.Id, SlotType.EquipmentSlave, dbId);
            if (persistentEquipment is not SlaveEquipmentContainer slaveEquipment)
            {
                Logger.Error(
                    "Persistent equipment container {0} for slave {1} was {2}, expected SlaveEquipmentContainer",
                    persistentEquipment.ContainerId, dbId, persistentEquipment.GetType().Name);
                return null;
            }

            slaveEquipment.Wearer = summonedSlave;
            slaveEquipment.MateId = dbId;
            summonedSlave.Equipment = slaveEquipment;
        }
        else
        {
            summonedSlave.Equipment = new SlaveEquipmentContainer(owner?.Id ?? 0, summonedSlave, false);
        }

        var slaveSkills = MateManager.Instance.GetMateSkills(slaveTemplate.Id);
        if (slaveSkills is { Count: > 0 })
            summonedSlave.Skills.AddRange(slaveSkills);

        ApplySlaveBonuses(summonedSlave);

        var createdInitialEquipment = false;
        if (summonedSlave.Equipment.Items.Count == 0 &&
            _slaveInitialItems.TryGetValue(summonedSlave.Template.SlaveInitialItemPackId, out var itemPack))
        {
            foreach (var initialItem in itemPack)
            {
                var newItem = ItemManager.Instance.Create(initialItem.itemId, 1, 0, true);
                if (newItem == null || !summonedSlave.Equipment.AddOrMoveExistingItem(
                        ItemTaskType.Invalid, newItem, initialItem.equipSlotId))
                {
                    Logger.Error(
                        "Failed to place initial slave equipment: slave={0}, pack={1}, item={2}, slot={3}",
                        summonedSlave.TemplateId, summonedSlave.Template.SlaveInitialItemPackId,
                        initialItem.itemId, initialItem.equipSlotId);
                    if (newItem?.Id > 0)
                        ItemManager.Instance.ReleaseId(newItem.Id);
                }
                else
                {
                    createdInitialEquipment = true;
                }
            }
        }
        else if (summonedSlave.Equipment.Items.Count > 0)
        {
            Logger.Debug(
                "Loaded {0} persisted equipment items for slave dbId={1}, template={2}",
                summonedSlave.Equipment.Items.Count, summonedSlave.Id, summonedSlave.TemplateId);
        }

        var equipmentModifierCount = summonedSlave.RebuildEquipmentBonuses();
        var equipmentBuffCount = summonedSlave.RebuildEquipmentBuffs();
        Logger.Debug(
            "Applied {0} item unit modifiers and {1} grade equip buffs from {2} equipment entries " +
            "to slave {3}/{4}",
            equipmentModifierCount, equipmentBuffCount, summonedSlave.Equipment.Items.Count,
            summonedSlave.TemplateId, summonedSlave.Id);

        if (!isLoadedPlayerSlave)
        {
            summonedSlave.Hp = summonedSlave.MaxHp;
            summonedSlave.Mp = summonedSlave.MaxMp;
        }

        summonedSlave.Hp = Math.Min(summonedSlave.Hp, summonedSlave.MaxHp);
        summonedSlave.Mp = Math.Min(summonedSlave.Mp, summonedSlave.MaxMp);

        // Reset HP on "dead" vehicles
        if (summonedSlave.Hp <= 0)
            summonedSlave.Hp = summonedSlave.MaxHp;

        summonedSlave.Transform = spawnPos.CloneDetached(summonedSlave);

        // The source item, the persistent 0xF2 container and its initial equipment must reach
        // MySQL before the client sees a summon success. They all share the same dbSlaveId.
        if (item is SummonSlave slaveSummonItem)
        {
            slaveSummonItem.SlaveType = 0x02;
            slaveSummonItem.SlaveDbId = dbId;
            slaveSummonItem.SummonLocation = spawnPos.World.Position;
            slaveSummonItem.RepairStartTime = DateTime.MinValue;
            slaveSummonItem.IsDirty = true;
        }

        if (owner != null && dbId > 0 &&
            !SaveManager.Instance.SaveItemsForOwner(
                owner.Id,
                createdInitialEquipment
                    ? $"create initial slave equipment dbSlaveId={dbId}"
                    : $"bind slave source item dbSlaveId={dbId}"))
        {
            Logger.Error(
                "Cannot publish slave summon because its source item/equipment was not persisted: " +
                "owner={0}, slave={1}, container={2}",
                owner.Id, dbId, summonedSlave.Equipment.ContainerId);
            spawnPos.Dispose();
            TlIdManager.Instance.ReleaseId(tlId);
            ObjectIdManager.Instance.ReleaseId(objId);
            return null;
        }

        // The target client can resolve ownership/control while processing the creation and
        // status packets. Publish only after the parent is authoritative in both registries and,
        // for ships, in the physics world. Previously this happened after SCUnitState and after all
        // child parts, so the client had no valid controllable parent at the decisive moment.
        lock (_slaveListLock)
        {
            _tlSlaves[summonedSlave.TlId] = summonedSlave;
            if (owner != null)
                _activeSlaves[owner.ObjId] = summonedSlave;
        }

        if (item is SummonSlave)
        {
            owner?.SendPacket(new SCItemTaskSuccessPacket(
                ItemTaskType.UpdateSummonMateItem,
                new ItemUpdate(item),
                new List<ulong>()));
        }

        Logger.Info(
            "Slave summon ready: owner={0}, template={1}, kind={2}, objId={3}, tl={4}, dbId={5}, " +
            "sourceItem={6}, equipmentContainer={7}, equipmentCount={8}",
            owner?.Id ?? 0, summonedSlave.TemplateId, summonedSlave.Template.SlaveKind,
            summonedSlave.ObjId, summonedSlave.TlId, summonedSlave.Id,
            summonedSlave.SummoningItem?.Id ?? 0,
            summonedSlave.Equipment.ContainerType,
            summonedSlave.Equipment.Items.Count);

        // SCSlaveCreated starts the client-side portal effect. Retail does not publish the
        // actual unit state until the template portal interval has elapsed; publishing it in the
        // same tick makes both vehicles and ships pop into existence without the vortex.
        owner?.BroadcastPacket(new SCSlaveCreatedPacket(
            owner.ObjId, summonedSlave.TlId, summonedSlave.ObjId, owner.Id, owner.Name), true);

        spawnPos.Dispose();
        var portalDelay = Math.Max(0f, summonedSlave.Template.PortalTime);
        if (portalDelay > 0.05f)
        {
            TaskManager.Instance.Schedule(
                new CompleteSlaveSpawnTask(summonedSlave),
                TimeSpan.FromSeconds(portalDelay));
        }
        else
        {
            CompleteSpawnPublication(summonedSlave);
        }

        return summonedSlave;
    }

    /// <summary>
    /// Publishes a slave after the client has had time to play the portal-spawn effect announced
    /// by <see cref="SCSlaveCreatedPacket"/>.
    /// </summary>
    public void CompleteSpawnPublication(Slave summonedSlave)
    {
        if (summonedSlave == null)
            return;

        lock (_slaveListLock)
        {
            if (!_tlSlaves.TryGetValue(summonedSlave.TlId, out var registered) ||
                !ReferenceEquals(registered, summonedSlave))
            {
                Logger.Debug(
                    "Delayed slave publication skipped because the summon was recalled: template={0}, objId={1}, tl={2}",
                    summonedSlave.TemplateId, summonedSlave.ObjId, summonedSlave.TlId);
                return;
            }
        }

        var owner = summonedSlave.Summoner;
        var item = summonedSlave.SummoningItem;

        if (summonedSlave.Template.IsABoat())
        {
            var world = WorldManager.Instance.GetWorld(summonedSlave.Transform.WorldId);
            if (world == null)
            {
                Logger.Error(
                    "Cannot register boat physics: slave={0}, objId={1}, world={2} was not found",
                    summonedSlave.TemplateId, summonedSlave.ObjId, summonedSlave.Transform.WorldId);
            }
            else
            {
                world.Physics.AddShip(summonedSlave);
            }
        }

        summonedSlave.Spawn();

        // Equipment entries in the parent state are inventory records only. The target database
        // resolves each occupied slot to a separate attached doodad or child slave, which must be
        // published as world objects before the ship becomes functionally complete.
        SynchronizeEquipmentComponents(summonedSlave);

        // If this was a previously saved slave, load doodads from DB and spawn them
        var doodadSpawnCount = SpawnManager.Instance.SpawnPersistentDoodads(DoodadOwnerType.Slave, (int)summonedSlave.Id, summonedSlave, true);
        Logger.Debug($"Loaded {doodadSpawnCount} doodads from DB for Slave {summonedSlave.ObjId} (Db: {summonedSlave.Id}");

        // Create all remaining doodads that where not previously loaded
        foreach (var doodadBinding in summonedSlave.Template.DoodadBindings)
        {
            // If this AttachPoint has already been spawned, skip it's creation
            if (summonedSlave.AttachedDoodads.Any(d => d.AttachPoint == doodadBinding.AttachPointId) ||
                summonedSlave.AttachedSlaves.Any(x => x.AttachPointId == (sbyte)doodadBinding.AttachPointId))
                continue;

            var doodad = new Doodad();
            doodad.ObjId = ObjectIdManager.Instance.GetNextId();
            doodad.TemplateId = doodadBinding.DoodadId;
            doodad.OwnerObjId = owner?.ObjId ?? 0;
            doodad.ParentObjId = summonedSlave.ObjId;
            doodad.AttachPoint = doodadBinding.AttachPointId;
            doodad.OwnerId = owner?.Id ?? 0;
            doodad.PlantTime = summonedSlave.SpawnTime;
            doodad.OwnerType = DoodadOwnerType.Slave;
            doodad.OwnerDbId = summonedSlave.Id;
            doodad.Template = DoodadManager.Instance.GetTemplate(doodadBinding.DoodadId);
            doodad.Data = (byte)doodadBinding.AttachPointId; // copy of AttachPointId
            doodad.ParentObj = summonedSlave;
            doodad.Faction = summonedSlave.Faction;
            doodad.Type2 = 1u; // Flag: No idea why it's 1 for slave's doodads, seems to be 0 for everything else
            doodad.Spawner = null;

            doodad.SetScale(doodadBinding.Scale);

            doodad.FuncGroupId = doodad.GetFuncGroupId();
            doodad.Transform = summonedSlave.Transform.CloneAttached(doodad);
            doodad.Transform.Parent = summonedSlave.Transform;

            // NOTE: In 1.2 we can't replace slave parts like sail, so just apply it to all of the doodads on spawn)
            // Should probably have a check somewhere if a doodad can have the UCC applied or not
            if (item != null && item.HasFlag(ItemFlag.HasUCC) && (item.UccId > 0))
                doodad.UccId = item.UccId;

            ApplyAttachPointLocation(summonedSlave, doodad, doodadBinding.AttachPointId);

            summonedSlave.AttachedDoodads.Add(doodad);

            // Slave template bindings are also built directly and need their phase/timer in the
            // initial create record. This covers default breathing devices as well as equipment
            // slots resolved to doodads.
            doodad.InitDoodad(false);
            doodad.Spawn();

            // Only set IsPersistent if the binding is defined as such
            if ((owner?.Id > 0) && (item?.Id > 0) && (doodadBinding.Persist))
            {
                doodad.IsPersistent = true;
                doodad.Save();
            }
        }

        foreach (var slaveBinding in summonedSlave.Template.SlaveBindings)
        { 
            if (slaveBinding.OwnerType != "Slave")
                continue;
            if (summonedSlave.AttachedDoodads.Any(d => d.AttachPoint == slaveBinding.AttachPointId) ||
                summonedSlave.AttachedSlaves.Any(x => x.AttachPointId == (sbyte)slaveBinding.AttachPointId))
                continue;
            var childSlaveTemplate = GetSlaveTemplate(slaveBinding.SlaveId);
            var childTlId = (ushort)TlIdManager.Instance.GetNextId();
            var childObjId = ObjectIdManager.Instance.GetNextId();
            var childSlave = new Slave();
            childSlave.TlId = childTlId;
            childSlave.ObjId = childObjId;
            childSlave.ParentObj = summonedSlave;
            childSlave.TemplateId = childSlaveTemplate.Id;
            childSlave.Name = childSlaveTemplate.Name;
            childSlave.Level = (byte)childSlaveTemplate.Level;
            childSlave.ModelId = childSlaveTemplate.ModelId;
            childSlave.Template = childSlaveTemplate;
            childSlave.Hp = 1;
            childSlave.Mp = 1;
            childSlave.ModelParams = new UnitCustomModelParams();
            childSlave.Faction = owner?.Faction ?? summonedSlave.Faction;
            // Attached runtime components are not persistent personal slaves. Keep ownership
            // fields for faction/interaction checks, but send no summoner/db identity in status.
            childSlave.Id = 0;
            childSlave.Summoner = null;
            childSlave.OwnerObjId = owner?.ObjId ?? 0;
            childSlave.OwnerId = owner?.Id ?? 0;
            //childSlave.SummoningItem = item;
            childSlave.SpawnTime = DateTime.UtcNow;
            childSlave.AttachPointId = (sbyte)slaveBinding.AttachPointId;
            childSlave.OwnerObjId = summonedSlave.ObjId;

            ApplySlaveBonuses(childSlave);

            childSlave.Hp = childSlave.MaxHp;
            childSlave.Mp = childSlave.MaxMp;
            childSlave.Transform = summonedSlave.Transform.CloneDetached(childSlave);
            childSlave.Transform.Parent = summonedSlave.Transform;

            ApplyAttachPointLocation(summonedSlave, childSlave, slaveBinding.AttachPointId);

            summonedSlave.AttachedSlaves.Add(childSlave);
            lock (_slaveListLock)
                _tlSlaves.Add(childSlave.TlId, childSlave);
            childSlave.Spawn();
            childSlave.PostUpdateCurrentHp(childSlave, 0, childSlave.Hp, KillReason.Unknown);
        }

        owner?.SendPacket(new SCMySlavePacket(summonedSlave.ObjId, summonedSlave.TlId, summonedSlave.Name,
            summonedSlave.TemplateId,
            summonedSlave.Hp, summonedSlave.MaxHp,
            summonedSlave.Transform.World.Position.X,
            summonedSlave.Transform.World.Position.Y,
            summonedSlave.Transform.World.Position.Z
        ));

        // Save to DB
        summonedSlave.Save();

        summonedSlave.PostUpdateCurrentHp(summonedSlave, 0, summonedSlave.Hp, KillReason.Unknown);
        UpdateSlaveRepairPoints(summonedSlave);

    }

    /// <summary>
    /// Use loaded attachPoint location and apply them depending on the slave and point
    /// </summary>
    /// <param name="slave">Owner</param>
    /// <param name="baseUnit">GameObject to apply to</param>
    /// <param name="attachPoint">Location to apply</param>
    private void ApplyAttachPointLocation(Slave slave, GameObject baseUnit, AttachPointKind attachPoint)
    {
        if (_attachPoints.ContainsKey(slave.ModelId))
        {
            if (_attachPoints[slave.ModelId].ContainsKey(attachPoint))
            {
                baseUnit.Transform = slave.Transform.CloneAttached(baseUnit);
                baseUnit.Transform.Parent = slave.Transform;
                baseUnit.Transform.Local.Translate(_attachPoints[slave.ModelId][attachPoint].AsPositionVector());
                baseUnit.Transform.Local.SetRotation(
                    _attachPoints[slave.ModelId][attachPoint].Roll,
                    _attachPoints[slave.ModelId][attachPoint].Pitch,
                    _attachPoints[slave.ModelId][attachPoint].Yaw);
                Logger.Debug($"Model id: {slave.ModelId} attachment {attachPoint} => pos {_attachPoints[slave.ModelId][attachPoint]} = {baseUnit.Transform}");
                return;
            }
            else
            {
                Logger.Warn($"Model id: {slave.ModelId} incomplete attach point information");
            }
        }
        else
        {
            Logger.Warn($"Model id: {slave.ModelId} has no attach point information");
        }
    }

    /// <summary>
    /// Applies buff ans bonuses to Slave
    /// </summary>
    /// <param name="summonedSlave"></param>
    private static void ApplySlaveBonuses(Slave summonedSlave)
    {
        // Add Passive buffs
        foreach (var buff in summonedSlave.Template.PassiveBuffs)
        {
            var passive = SkillManager.Instance.GetPassiveBuffTemplate(buff.PassiveBuffId);
            summonedSlave.Buffs.AddBuff(passive.BuffId, summonedSlave);
        }

        // Add Normal initial buffs
        foreach (var buff in summonedSlave.Template.InitialBuffs)
            summonedSlave.Buffs.AddBuff(buff.BuffId, summonedSlave);

        // Apply bonuses
        foreach (var bonusTemplate in summonedSlave.Template.Bonuses)
        {
            var bonus = new Bonus();
            bonus.Template = bonusTemplate;
            bonus.Value = bonusTemplate.Value; // TODO using LinearLevelBonus
            summonedSlave.AddBonus(0, bonus);
        }
    }

    public void LoadSlaveAttachmentPointLocations()
    {
        Logger.Info("Loading Slave Model Attach Points...");

        var filePath = Path.Combine(FileManager.AppPath, "Data", "slave_attach_points.json");
        var contents = FileManager.GetFileContents(filePath);
        if (string.IsNullOrWhiteSpace(contents))
            throw new IOException($"File {filePath} doesn't exist or is empty.");

        _attachPoints = new Dictionary<uint, Dictionary<AttachPointKind, WorldSpawnPosition>>();

        var loadedSets = 0;
        var loadedModels = 0;
        var loadedPoints = 0;
        var skippedSets = 0;

        try
        {
            var root = JArray.Parse(contents);
            foreach (var token in root)
            {
                if (token is not JObject set)
                {
                    skippedSets++;
                    Logger.Warn("Slave attach-point entry is not an object and was skipped");
                    continue;
                }

                var modelToken = set["ModelId"];
                var pointsToken = set["AttachPoints"];
                if (modelToken == null || pointsToken == null)
                {
                    skippedSets++;
                    Logger.Warn("Slave attach-point entry is missing ModelId or AttachPoints and was skipped");
                    continue;
                }

                var modelIds = new List<uint>();
                if (modelToken.Type == JTokenType.Array)
                {
                    foreach (var modelIdToken in modelToken.Children())
                    {
                        if (uint.TryParse(modelIdToken.ToString(), out var modelId))
                            modelIds.Add(modelId);
                        else
                            Logger.Warn("Invalid slave model id '{0}' in {1}", modelIdToken, filePath);
                    }
                }
                else if (uint.TryParse(modelToken.ToString(), out var singleModelId))
                {
                    // Backward compatibility with the old schema: "ModelId": 128.
                    modelIds.Add(singleModelId);
                }

                if (modelIds.Count == 0)
                {
                    skippedSets++;
                    Logger.Warn("Slave attach-point entry has no valid model ids and was skipped");
                    continue;
                }

                var positions = pointsToken.ToObject<Dictionary<AttachPointKind, WorldSpawnPosition>>();
                if (positions == null || positions.Count == 0)
                {
                    skippedSets++;
                    Logger.Warn("Slave attach-point entry for model(s) {0} has no valid points and was skipped",
                        string.Join(",", modelIds));
                    continue;
                }

                // JSON stores Euler angles in degrees; transforms use radians.
                foreach (var position in positions.Values)
                {
                    position.Roll = position.Roll.DegToRad();
                    position.Pitch = position.Pitch.DegToRad();
                    position.Yaw = position.Yaw.DegToRad();
                }

                foreach (var modelId in modelIds.Distinct())
                {
                    if (!_attachPoints.TryGetValue(modelId, out var targetPoints))
                    {
                        targetPoints = new Dictionary<AttachPointKind, WorldSpawnPosition>();
                        _attachPoints.Add(modelId, targetPoints);
                        loadedModels++;
                    }
                    else
                    {
                        Logger.Warn("Slave model {0} appears more than once; attachment points will be merged", modelId);
                    }

                    foreach (var (attachPoint, position) in positions)
                    {
                        if (targetPoints.ContainsKey(attachPoint))
                            Logger.Warn("Slave model {0} attach point {1} is duplicated; using the last value",
                                modelId, attachPoint);

                        // Each model gets its own mutable transform data, even when several model ids
                        // share one coordinate set in the new JSON schema.
                        targetPoints[attachPoint] = position.Clone();
                    }

                    loadedPoints += positions.Count;
                }

                loadedSets++;
            }
        }
        catch (Exception e)
        {
            _attachPoints.Clear();
            throw new InvalidDataException(
                $"Failed to load {filePath}. Supported schemas are ModelId as a number or an array, with an AttachPoints object.",
                e);
        }

        if (_attachPoints.Count == 0)
            throw new InvalidDataException($"File {filePath} did not contain any usable slave attachment points.");

        Logger.Info(
            "Slave model attach points loaded: sets={0}, models={1}, points={2}, skippedSets={3}",
            loadedSets, loadedModels, loadedPoints, skippedSets);
    }

    public void Load()
    {
        _slaveListLock = new object();
        _slaveTemplates = new Dictionary<uint, SlaveTemplate>();
        lock (_slaveListLock)
        {
            _activeSlaves = new Dictionary<uint, Slave>();
            _testSlaves = new List<Slave>();
            _tlSlaves = new Dictionary<uint, Slave>();
        }
        _slaveInitialItems = new Dictionary<uint, List<SlaveInitialItems>>();
        _slaveEquipmentItemTemplates = new HashSet<uint>();
        _slaveEquipmentSlots = new Dictionary<uint, HashSet<byte>>();
        _slaveEquipmentSlotInfo = new Dictionary<uint, Dictionary<byte, SlaveEquipmentSlotInfo>>();
        _slaveEquipmentGradeSpawns = new Dictionary<ulong, SlaveEquipmentSpawnInfo>();
        //_slaveMountSkills = new Dictionary<uint, SlaveMountSkills>();
        _slaveMountSkills = new Dictionary<uint, List<uint>>();
        _repairableSlaves = new Dictionary<uint, uint>();

        #region SQLLite

        using (var connection2 = SQLite.CreateServerConnection())
        using (var connection = SQLite.CreateTargetClientConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM slaves";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SlaveTemplate
                        {
                            Id = reader.GetUInt32("id"),
                            Name =
                                LocalizationManager.Instance.Get("slaves", "name", reader.GetUInt32("id"),
                                    reader.GetString("name")),
                            ModelId = reader.GetUInt32("model_id"),
                            Mountable = reader.GetBoolean("mountable"),
                            SpawnXOffset = reader.GetFloat("spawn_x_offset"),
                            SpawnYOffset = reader.GetFloat("spawn_y_offset"),
                            FactionId = reader.GetUInt32("faction_id", 0),
                            Level = reader.GetUInt32("level"),
                            Cost = reader.GetInt32("cost"),
                            SlaveKind = (SlaveKind)reader.GetUInt32("slave_kind_id"),
                            SpawnValidAreaRange = reader.GetUInt32("spawn_valid_area_range", 0),
                            SlaveInitialItemPackId = reader.GetUInt32("slave_initial_item_pack_id", 0),
                            SlaveCustomizingId = reader.GetUInt32("slave_customizing_id", 0),
                            Customizable = reader.GetBoolean("customizable", false),
                            PortalTime = reader.GetFloat("portal_time"),
                            PortalSpawnFxId = reader.GetUInt32("portal_spawn_fx_id", 0),
                            PortalDespawnFxId = reader.GetUInt32("portal_despawn_fx_id", 0),
                            PortalScale = reader.GetFloat("portal_scale", 1f),
                            Hp25DoodadCount = reader.GetInt32("hp25_doodad_count"),
                            Hp50DoodadCount = reader.GetInt32("hp50_doodad_count"),
                            Hp75DoodadCount = reader.GetInt32("hp75_doodad_count"),
                        };
                        _slaveTemplates.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type='Slave'";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var slaveId = reader.GetUInt32("owner_id");
                        if (!_slaveTemplates.TryGetValue(slaveId, out var slaveTemplate))
                            continue;
                        var template = new BonusTemplate();
                        template.Attribute = (UnitAttribute)reader.GetByte("unit_attribute_id");
                        template.ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id");
                        template.Value = reader.GetInt32("value");
                        template.LinearLevelBonus = reader.GetInt32("linear_level_bonus");
                        slaveTemplate.Bonuses.Add(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM slave_initial_items";
                command.Prepare();

                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var ItemPackId = reader.GetUInt32("slave_initial_item_pack_id");
                        var SlotId = reader.GetByte("equip_slot_id");
                        var item = reader.GetUInt32("item_id");

                        // Initial equipment is authoritative even when an old client table omits
                        // the matching item_slave_equipments row (item 43000 is such a case).
                        _slaveEquipmentItemTemplates.Add(item);

                        if (_slaveInitialItems.TryGetValue(ItemPackId, out var key))
                        {
                            key.Add(new SlaveInitialItems() { slaveInitialItemPackId = ItemPackId, equipSlotId = SlotId, itemId = item });
                        }
                        else
                        {
                            var newPack = new List<SlaveInitialItems>();
                            var newKey = new SlaveInitialItems
                            {
                                slaveInitialItemPackId = ItemPackId,
                                equipSlotId = SlotId,
                                itemId = item
                            };
                            newPack.Add(newKey);

                            _slaveInitialItems.Add(ItemPackId, newPack);
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT item_id FROM item_slave_equipments";
                command.Prepare();

                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                    _slaveEquipmentItemTemplates.Add(reader.GetUInt32("item_id"));
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT slave_id, equip_slot_id, attach_point_id, require_buff_tag_id, require_slot_id " +
                    "FROM slave_equip_slots";
                command.Prepare();

                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var slaveId = reader.GetUInt32("slave_id");
                    var slot = reader.GetByte("equip_slot_id");
                    if (!_slaveEquipmentSlots.TryGetValue(slaveId, out var slots))
                    {
                        slots = new HashSet<byte>();
                        _slaveEquipmentSlots.Add(slaveId, slots);
                    }
                    slots.Add(slot);

                    if (!_slaveEquipmentSlotInfo.TryGetValue(slaveId, out var definitions))
                    {
                        definitions = new Dictionary<byte, SlaveEquipmentSlotInfo>();
                        _slaveEquipmentSlotInfo.Add(slaveId, definitions);
                    }
                    definitions[slot] = new SlaveEquipmentSlotInfo
                    {
                        AttachPoint = (AttachPointKind)reader.GetInt32("attach_point_id"),
                        RequireBuffTagId = reader.GetInt32("require_buff_tag_id"),
                        RequireSlotId = reader.GetInt32("require_slot_id")
                    };
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT item_id, item_grade_id, doodad_id, slave_id " +
                    "FROM item_slave_equipment_grade_spawns";
                command.Prepare();

                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var itemId = reader.GetUInt32("item_id");
                    var grade = reader.GetByte("item_grade_id");
                    _slaveEquipmentGradeSpawns[MakeEquipmentGradeKey(itemId, grade)] =
                        new SlaveEquipmentSpawnInfo
                        {
                            DoodadId = reader.GetUInt32("doodad_id"),
                            ChildSlaveId = reader.GetUInt32("slave_id")
                        };
                }
            }

            Logger.Info(
                "Loaded slave equipment metadata: itemTemplates={0}, slaveSlotSets={1}, slotDefinitions={2}, gradeSpawns={3}",
                _slaveEquipmentItemTemplates.Count, _slaveEquipmentSlots.Count,
                _slaveEquipmentSlotInfo.Sum(x => x.Value.Count), _slaveEquipmentGradeSpawns.Count);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM slave_initial_buffs";
                command.Prepare();

                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SlaveInitialBuffs();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.SlaveId = reader.GetUInt32("slave_id");
                        template.BuffId = reader.GetUInt32("buff_id");
                        if (_slaveTemplates.ContainsKey(template.SlaveId))
                        {
                            _slaveTemplates[template.SlaveId].InitialBuffs.Add(template);
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM slave_passive_buffs";
                command.Prepare();

                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SlavePassiveBuffs();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.OwnerId = reader.GetUInt32("owner_id");
                        template.OwnerType = reader.GetString("owner_type");
                        template.PassiveBuffId = reader.GetUInt32("passive_buff_id");
                        if (_slaveTemplates.ContainsKey(template.OwnerId))
                        {
                            _slaveTemplates[template.OwnerId].PassiveBuffs.Add(template);
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM slave_doodad_bindings";
                command.Prepare();

                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SlaveDoodadBindings();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.OwnerId = reader.GetUInt32("owner_id");
                        template.OwnerType = reader.GetString("owner_type");
                        template.AttachPointId = (AttachPointKind)reader.GetInt32("attach_point_id");
                        template.DoodadId = reader.GetUInt32("doodad_id");
                        template.Persist = reader.GetBoolean("persist", true);
                        template.Scale = reader.GetFloat("scale");
                        if (_slaveTemplates.ContainsKey(template.OwnerId))
                        {
                            _slaveTemplates[template.OwnerId].DoodadBindings.Add(template);
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM slave_healing_point_doodads";
                command.Prepare();

                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SlaveDoodadBindings();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.OwnerId = reader.GetUInt32("owner_id");
                        template.OwnerType = reader.GetString("owner_type");
                        template.AttachPointId = (AttachPointKind)reader.GetInt32("attach_point_id");
                        template.DoodadId = reader.GetUInt32("doodad_id");
                        template.Persist = false;
                        template.Scale = 1f;
                        if (_slaveTemplates.ContainsKey(template.OwnerId))
                        {
                            _slaveTemplates[template.OwnerId].HealingPointDoodads.Add(template);
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM slave_bindings";
                command.Prepare();

                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SlaveBindings();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.OwnerId = reader.GetUInt32("owner_id");
                        template.OwnerType = reader.GetString("owner_type");
                        template.AttachPointId = (AttachPointKind)reader.GetUInt32("attach_point_id");
                        template.SlaveId = reader.GetUInt32("slave_id");

                        if (_slaveTemplates.ContainsKey(template.OwnerId))
                        {
                            _slaveTemplates[template.OwnerId].SlaveBindings.Add(template);
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM slave_drop_doodads";
                command.Prepare();

                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SlaveDropDoodad();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.OwnerId = reader.GetUInt32("owner_id");
                        template.OwnerType = reader.GetString("owner_type");
                        template.DoodadId = reader.GetUInt32("doodad_id");
                        template.Count = reader.GetUInt32("count");
                        template.Radius = reader.GetFloat("radius");
                        template.OnWater = reader.GetBoolean("on_water", true);

                        if (template.OwnerType != "Slave")
                        {
                            Logger.Warn($"Non slave-owned drops defined in slave_drop_doodads table");
                            continue;
                        }
                        if (_slaveTemplates.ContainsKey(template.OwnerId))
                        {
                            _slaveTemplates[template.OwnerId].SlaveDropDoodads.Add(template);
                        }
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM slave_mount_skills";
                command.Prepare();

                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SlaveMountSkills();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.SlaveId = reader.GetUInt32("slave_id");
                        template.MountSkillId = reader.GetUInt32("mount_skill_id");
                        if (_slaveMountSkills.TryGetValue(template.SlaveId, out var value))
                        {
                            if (!value.Contains(template.MountSkillId))
                                value.Add(template.MountSkillId);
                            else
                                Logger.Warn($"Duplicate entry for slave_mount_skills");
                        }
                        else
                            _slaveMountSkills.Add(template.SlaveId, [template.MountSkillId]);
                    }
                }
            }

            // Which ship repairs with which effect is listed by the old server supplement alone,
            // and this one is a real gap rather than an empty table: it holds twenty-eight rows.
            // The effects themselves are in the client database as repair_slave_effects, but
            // nothing there says which ship uses which - no table and no column under any name
            // mentioning repair - so the pairing has nowhere else to come from and belongs in
            // this server's own data. Until it is put there, a database without the table
            // leaves the list empty rather than failing to start.
            if (SQLite.TableExists(connection2, "repairable_slaves"))
            {
                using var command = connection2.CreateCommand();
                command.CommandText = "SELECT * FROM repairable_slaves";
                command.Prepare();

                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    if (!_repairableSlaves.TryAdd(reader.GetUInt32("slave_id"),
                            reader.GetUInt32("repair_slave_effect_id")))
                        Logger.Warn($"Duplicate entry for repairable_slaves");
                }
            }

        }
        #endregion

        LoadSlaveAttachmentPointLocations();
    }

    public static void Initialize()
    {
        var sendMySlaveTask = new SendMySlaveTask();
        TaskManager.Instance.Schedule(sendMySlaveTask, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    public void SendMySlavePacketToAllOwners()
    {
        Dictionary<uint, Slave> slaveList = null;
        lock (_slaveListLock)
            slaveList = _activeSlaves;

        foreach (var (ownerObjId, slave) in slaveList)
        {
            var owner = WorldManager.Instance.GetCharacterByObjId(ownerObjId);
            owner?.SendPacket(new SCMySlavePacket(slave.ObjId, slave.TlId, slave.Name, slave.TemplateId,
                slave.Hp, slave.MaxHp,
                slave.Transform.World.Position.X,
                slave.Transform.World.Position.Y,
                slave.Transform.World.Position.Z));
        }
    }

    public Slave GetIsMounted(uint objId, out AttachPointKind attachPoint)
    {
        attachPoint = AttachPointKind.None;
        lock (_slaveListLock)
        {
            foreach (var slave in _activeSlaves.Values)
                foreach (var unit in slave.AttachedCharacters)
                {
                    if (unit.Value.ObjId == objId)
                    {
                        attachPoint = unit.Key;
                        return slave;
                    }
                }
        }

        return null;
    }

    /// <summary>
    /// Finds a spawned slave by its database id.
    /// </summary>
    public Slave GetActiveSlaveByDbId(uint dbId)
    {
        foreach (var slave in _activeSlaves.Values)
            if (slave.Id == dbId)
                return slave;

        return null;
    }

    /// <summary>
    /// Renames a spawned slave and tells everyone who can see it.
    /// </summary>
    /// <returns>The slave, or null when the name is unusable or the caller does not own it.</returns>
    public Slave RenameSlave(GameConnection connection, ushort tlId, string newName)
    {
        var owner = connection?.ActiveChar;
        if (owner == null)
            return null;

        if (string.IsNullOrWhiteSpace(newName))
        {
            Logger.Warn($"{owner.Name} tried to give a slave an empty name");
            return null;
        }

        if (!_tlSlaves.TryGetValue(tlId, out var slave) || slave.Summoner?.ObjId != owner.ObjId)
        {
            Logger.Warn($"{owner.Name} tried to rename a slave they do not own (tl {tlId})");
            return null;
        }

        slave.Name = newName.Trim();
        owner.BroadcastPacket(new SCUnitNameChangedPacket(slave.ObjId, slave.Name), true);

        return slave;
    }

    /// <summary>
    /// Handles the summon item for a slave being destroyed.
    /// </summary>
    /// <remarks>
    /// The slave and its summon item are two halves of one thing: the item carries the
    /// database id of the vehicle it summons. Destroying the item without removing the row
    /// left the vehicle behind in the database forever, owned by an item that no longer
    /// exists, and it would come back on the next load.
    /// </remarks>
    /// <returns>True when a stored slave was actually removed.</returns>
    public bool OnDeleteSlaveItem(uint slaveDbId)
    {
        if (slaveDbId == 0)
            return false;

        // Despawn it first if the player currently has it out.
        var active = GetActiveSlaveByDbId(slaveDbId);
        if (active?.Summoner != null)
            RemoveActiveSlave(active.Summoner, active.TlId);

        return DeleteSlaveById(slaveDbId);
    }

    /// <summary>Removes one stored slave row.</summary>
    public bool DeleteSlaveById(uint dbId)
    {
        if (dbId == 0)
            return false;

        try
        {
            using var connection = MySQL.CreateConnection();
            using var command = connection.CreateCommand();
            command.Connection = connection;
            command.CommandText = "DELETE FROM slaves WHERE `id` = @id";
            command.Parameters.AddWithValue("@id", dbId);
            command.Prepare();
            return command.ExecuteNonQuery() > 0;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to delete slave {0} from the database", dbId);
            return false;
        }
    }

    public void RemoveActiveSlave(Character character, ushort slaveTlId)
    {
        if (_tlSlaves.TryGetValue(slaveTlId, out var slave))
        {
            if (slave.Summoner?.ObjId != character.ObjId)
            {
                Logger.Warn($"Non-owner is trying to desummon a slave {character.Name} => {slave.Name} (ObjId: {slave.ObjId})");
                return;
            }
        }
        else
        {
            return;
        }

        Delete(character, slave.ObjId);
        // slave.Delete();
    }

    public void RidersEscape(Character player, SkillCastPositionTarget skillCastPositionTarget)
    {
        var mySlave = GetActiveSlaveByOwnerObjId(player.ObjId);
        if (mySlave == null)
        {
            Logger.Warn($"{player.Name} using Rider's Escape with no slave active!");
            return;
        }

        // NOTE: ObjId and TlId gets retained during Rider's Escape

        // Despawn effect
        mySlave.BroadcastPacket(new SCSlaveDespawnPacket(mySlave.ObjId), true);
        mySlave.BroadcastPacket(new SCSlaveRemovedPacket(mySlave.ObjId, mySlave.TlId), true);
        mySlave.SendPacket(new SCUnitsRemovedPacket(new[] { mySlave.ObjId }));

        // Move location
        mySlave.SetPosition(skillCastPositionTarget.PosX, skillCastPositionTarget.PosY, skillCastPositionTarget.PosZ, 0f, 0f, skillCastPositionTarget.PosRot);
        // Without this offset, it just doesn't feel right
        mySlave.Transform.Local.AddDistanceToFront(mySlave.Template.SpawnXOffset / 2f);
        mySlave.Transform.Local.AddDistanceToRight(mySlave.Template.SpawnYOffset / 2f);

        // Respawn effect
        mySlave.Hide(); // Hide is needed for it's internals
        mySlave.Spawn();
        //mySlave.SendPacket(new SCUnitStatePacket(mySlave));
        //mySlave.SendPacket(new SCUnitPointsPacket(mySlave.ObjId, mySlave.Hp, mySlave.Mp));
        //mySlave.SendPacket(new SCSlaveStatePacket(mySlave.ObjId, mySlave.TlId, mySlave.Summoner.Name, mySlave.Summoner.ObjId, mySlave.Id));
    }

    public void UpdateSlaveRepairPoints(Slave slave)
    {
        var hpPercent = slave.Hp * 100 / slave.MaxHp;

        var repairPoints = 0;
        if (hpPercent is < 100 and >= 75)
            repairPoints = slave.Template.Hp75DoodadCount;
        else if (hpPercent is < 75 and >= 50)
            repairPoints = slave.Template.Hp50DoodadCount;
        else if (hpPercent is < 50 and >= 25)
            repairPoints = slave.Template.Hp25DoodadCount;
        else if (hpPercent < 25)
            repairPoints = slave.Template.HealingPointDoodads.Count; // Use max points or Hp 25% ?

        // Get Current Count
        var currentHealPoints = new List<Doodad>();
        var unUsedHealPoints = new List<AttachPointKind>();
        foreach (var healBinding in slave.Template.HealingPointDoodads)
            unUsedHealPoints.Add(healBinding.AttachPointId);

        foreach (var doodad in slave.AttachedDoodads)
        {
            if ((doodad.AttachPoint < AttachPointKind.HealPoint0) || (doodad.AttachPoint > AttachPointKind.HealPoint9))
                continue;
            currentHealPoints.Add(doodad);
            unUsedHealPoints.Remove(doodad.AttachPoint);
        }

        var pointsToAdd = repairPoints - currentHealPoints.Count;
        if (pointsToAdd < 0)
        {
            // We have too many points, remove some
            for (var iRemove = pointsToAdd; iRemove < 0; iRemove++)
            {
                var i = Random.Shared.Next(currentHealPoints.Count);
                var doodad = currentHealPoints[i];
                if (doodad == null)
                    continue;

                doodad.Hide();
                doodad.Despawn = DateTime.UtcNow;
                SpawnManager.Instance.AddDespawn(doodad);
                slave.AttachedDoodads.Remove(doodad);
                currentHealPoints.Remove(doodad);
                unUsedHealPoints.Add(doodad.AttachPoint);
                doodad.Delete();
            }
        }

        if ((pointsToAdd > 0) && (unUsedHealPoints.Count > 0))
        {
            // We don't have enough points, add some
            for (var iAdd = 0; (iAdd < pointsToAdd) && (unUsedHealPoints.Count > 0); iAdd++)
            {
                // pick a random spot
                var wreckPointLocation = unUsedHealPoints[Random.Shared.Next(unUsedHealPoints.Count)];
                unUsedHealPoints.Remove(wreckPointLocation);
                var healBinding = slave.Template.HealingPointDoodads.FirstOrDefault(p => p.AttachPointId == wreckPointLocation);
                if (healBinding == null)
                {
                    Logger.Error($"Somehow failed to grab a healing point {wreckPointLocation} for {slave.TemplateId}");
                    return;
                }

                var wreckArea = new Doodad();
                wreckArea.ObjId = ObjectIdManager.Instance.GetNextId();
                wreckArea.TemplateId = healBinding.DoodadId;
                wreckArea.OwnerObjId = slave.OwnerObjId;
                wreckArea.ParentObjId = slave.ObjId;
                wreckArea.AttachPoint = wreckPointLocation;
                wreckArea.OwnerId = slave.Summoner?.Id ?? 0;
                wreckArea.PlantTime = DateTime.UtcNow;
                wreckArea.OwnerType = DoodadOwnerType.Slave;
                wreckArea.OwnerDbId = slave.Id;
                wreckArea.Template = DoodadManager.Instance.GetTemplate(healBinding.DoodadId);
                wreckArea.Data = (byte)wreckPointLocation; // copy of AttachPointId
                wreckArea.ParentObj = slave;
                wreckArea.Faction = slave.Faction; // FactionManager.Instance.GetFaction(FactionsEnum.Friendly),
                wreckArea.Type2 = 1u; // Flag: No idea why it's 1 for slave's doodads, seems to be 0 for everything else
                wreckArea.Spawner = null;
                wreckArea.IsPersistent = false;

                wreckArea.SetScale(1f);
                ApplyAttachPointLocation(slave, wreckArea, wreckPointLocation);

                wreckArea.FuncGroupId = wreckArea.GetFuncGroupId();

                slave.AttachedDoodads.Add(wreckArea);
                currentHealPoints.Add(wreckArea);
                wreckArea.Spawn();
            }
        }
    }

    public void RemoveAndDespawnAllActiveOwnedSlaves(Character owner)
    {
        var activeSlaveInfo = GetActiveSlaveByOwnerObjId(owner.ObjId);
        if (activeSlaveInfo != null)
        {
            activeSlaveInfo.Save();
            Delete(owner, activeSlaveInfo.ObjId);
        }
    }

    /// <summary>
    /// RemoveAndDespawnTestSlave - deleting Mirage's test transport
    /// </summary>
    /// <param name="ObjId"></param>
    /// <returns></returns>
    public void RemoveAndDespawnTestSlave(Character owner, uint slaveObjId)
    {
        Delete(owner, slaveObjId);
    }
}
