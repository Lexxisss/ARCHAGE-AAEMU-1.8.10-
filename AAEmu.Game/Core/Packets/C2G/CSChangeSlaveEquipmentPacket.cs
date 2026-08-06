using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Installs, removes or swaps equipment on a ship or land vehicle.
/// </summary>
public class CSChangeSlaveEquipmentPacket : GamePacket
{
    /// <summary>
    /// "Moored" in the client database - the harbour buff whose description is
    /// "You can customize ship components".
    /// </summary>
    private const uint MooredBuffId = 13817;

    public CSChangeSlaveEquipmentPacket() : base(CSOffsets.CSChangeSlaveEquipmentPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var request = new SlaveEquipment();
        request.Read(stream);

        Logger.Debug(
            "ChangeSlaveEquipment: owner={0}, tl={1}, dbSlaveId={2}, bts={3}, wireCount={4}, parsed={5}",
            request.OwnerPersistentId, request.Tl, request.DbSlaveId, request.Bts,
            request.WireCount, request.Changes.Count);

        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        if (request.WireCount == 0)
        {
            Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, true));
            return;
        }

        if (request.WireCount > SlaveEquipment.MaxRecords)
        {
            Logger.Warn(
                "ChangeSlaveEquipment: client sent {0} records, target accepts at most {1}",
                request.WireCount, SlaveEquipment.MaxRecords);
            Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
            return;
        }

        var slave = SlaveManager.Instance.GetActiveSlaveByOwnerObjId(character.ObjId);
        if (slave == null || slave.TlId != request.Tl)
        {
            Logger.Warn(
                "ChangeSlaveEquipment: no active slave for owner={0}, tl={1}",
                character.Id, request.Tl);
            Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
            return;
        }

        if (request.OwnerPersistentId != character.Id ||
            (request.DbSlaveId != 0 && request.DbSlaveId != slave.Id))
        {
            Logger.Warn(
                "ChangeSlaveEquipment ownership mismatch: packetOwner={0}, actualOwner={1}, " +
                "packetDbSlave={2}, actualDbSlave={3}",
                request.OwnerPersistentId, character.Id, request.DbSlaveId, slave.Id);
            Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
            return;
        }

        if (slave.Equipment == null ||
            slave.Equipment.ContainerId == 0 ||
            slave.Equipment.ContainerType != SlotType.EquipmentSlave ||
            slave.Equipment.OwnerId != character.Id ||
            slave.Equipment.MateId != slave.Id)
        {
            Logger.Error(
                "ChangeSlaveEquipment: slave {0}/{1} has no valid persistent equipment container " +
                "(container={2}, type={3}, owner={4}, mate={5})",
                slave.TemplateId, slave.Id, slave.Equipment?.ContainerId ?? 0,
                slave.Equipment?.ContainerType ?? SlotType.None,
                slave.Equipment?.OwnerId ?? 0, slave.Equipment?.MateId ?? 0);
            Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
            return;
        }

        // A ship is refitted where the harbour allows it and nowhere else. The lighthouse spheres
        // hang the Moored buff on anything of the right faction that sails in, and the client
        // database says what it is for in as many words: "You can customize ship components".
        // Land vehicles are not moored anywhere and keep the old behaviour. A server whose client
        // data places no such sphere cannot enforce this, and refusing every change there would be
        // worse than not checking at all.
        if ((slave.Template?.IsABoat() ?? false) &&
            SphereBuffManager.Instance.IsBuffPlacedInWorld(MooredBuffId) &&
            !slave.Buffs.CheckBuff(MooredBuffId))
        {
            Logger.Debug(
                "ChangeSlaveEquipment: slave {0}/{1} is not moored, refit refused",
                slave.TemplateId, slave.Id);
            Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
            return;
        }

        var operations = new List<EquipmentOperation>(request.Changes.Count);
        var usedBagSlots = new HashSet<byte>();
        var usedSlaveSlots = new HashSet<byte>();

        foreach (var change in request.Changes)
        {
            var sourceIsInventory = change.SourceType == SlotType.Inventory;
            var destIsInventory = change.DestType == SlotType.Inventory;
            var sourceIsSlave = IsSlaveContainer(change.SourceType);
            var destIsSlave = IsSlaveContainer(change.DestType);

            if (!((sourceIsInventory && destIsSlave) || (sourceIsSlave && destIsInventory)))
            {
                Logger.Warn(
                    "ChangeSlaveEquipment: unsupported slot pair {0}:{1} <-> {2}:{3}",
                    change.SourceType, change.SourceIndex, change.DestType, change.DestIndex);
                Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
                return;
            }

            var bagIndex = sourceIsInventory ? change.SourceIndex : change.DestIndex;
            var slaveIndex = sourceIsSlave ? change.SourceIndex : change.DestIndex;
            var packetBagItem = sourceIsInventory
                ? change.ItemAtSourceBefore
                : change.ItemAtDestinationBefore;
            var packetSlaveItem = sourceIsSlave
                ? change.ItemAtSourceBefore
                : change.ItemAtDestinationBefore;

            if (slaveIndex > SlaveEquipment.MaxSlotIndex ||
                !usedBagSlots.Add(bagIndex) ||
                !usedSlaveSlots.Add(slaveIndex))
            {
                Logger.Warn(
                    "ChangeSlaveEquipment: invalid or repeated slots inventory={0}, slave={1}",
                    bagIndex, slaveIndex);
                Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
                return;
            }

            var bagItem = character.Inventory.Bag.GetItemBySlot(bagIndex);
            var slaveItem = slave.Equipment.GetItemBySlot(slaveIndex);

            if (!SnapshotMatches(packetBagItem, bagItem) ||
                !SnapshotMatches(packetSlaveItem, slaveItem))
            {
                Logger.Warn(
                    "ChangeSlaveEquipment stale snapshot: bag {0} packet={1} actual={2}; " +
                    "slave {3} packet={4} actual={5}",
                    bagIndex, packetBagItem?.Id ?? 0, bagItem?.Id ?? 0,
                    slaveIndex, packetSlaveItem?.Id ?? 0, slaveItem?.Id ?? 0);
                Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
                return;
            }

            if (bagItem == null && slaveItem == null)
            {
                Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
                return;
            }

            // If the bag side is occupied the swap puts that item onto the slave. Validate it.
            if (bagItem != null &&
                !SlaveManager.Instance.CanEquipSlaveItem(
                    slave.TemplateId, bagItem.TemplateId, slaveIndex))
            {
                Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
                return;
            }

            operations.Add(new EquipmentOperation(
                change, sourceIsInventory, bagIndex, slaveIndex, bagItem, slaveItem));
        }

        var reply = new SlaveEquipment
        {
            OwnerPersistentId = character.Id,
            Tl = slave.TlId,
            DbSlaveId = slave.Id,
            Bts = request.Bts
        };

        var applied = new List<EquipmentOperation>(operations.Count);
        var allTasks = new List<ItemTask>();

        foreach (var operation in operations)
        {
            bool moved;
            if (operation.BagItem != null)
            {
                moved = character.Inventory.SplitOrMoveItemEx(
                    ItemTaskType.Invalid,
                    character.Inventory.Bag,
                    slave.Equipment,
                    operation.BagItem.Id,
                    SlotType.Inventory,
                    operation.BagIndex,
                    operation.SlaveItem?.Id ?? 0,
                    SlotType.EquipmentSlave,
                    operation.SlaveIndex);
            }
            else
            {
                moved = character.Inventory.SplitOrMoveItemEx(
                    ItemTaskType.Invalid,
                    slave.Equipment,
                    character.Inventory.Bag,
                    operation.SlaveItem.Id,
                    SlotType.EquipmentSlave,
                    operation.SlaveIndex,
                    0,
                    SlotType.Inventory,
                    operation.BagIndex);
            }

            if (!moved)
            {
                Logger.Error(
                    "ChangeSlaveEquipment mutation failed after validation: slave={0}, bagSlot={1}, slaveSlot={2}",
                    slave.Id, operation.BagIndex, operation.SlaveIndex);

                RollBack(character, slave, applied);
                Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
                return;
            }

            applied.Add(operation);

            reply.Changes.Add(new SlaveEquipmentDelta
            {
                ItemAtSourceBefore = operation.SourceIsInventory
                    ? operation.BagItem
                    : operation.SlaveItem,
                ItemAtDestinationBefore = operation.SourceIsInventory
                    ? operation.SlaveItem
                    : operation.BagItem,
                SourceType = operation.Change.SourceType,
                SourceIndex = operation.Change.SourceIndex,
                DestType = operation.Change.DestType,
                DestIndex = operation.Change.DestIndex,
                ExpireTime = operation.Change.ExpireTime
            });

            var slaveWireType = operation.SourceIsInventory
                ? operation.Change.DestType
                : operation.Change.SourceType;

            if (operation.BagItem != null)
                allTasks.Add(new ItemRemove(
                    operation.BagItem, SlotType.Inventory, operation.BagIndex));
            if (operation.SlaveItem != null)
                allTasks.Add(new ItemRemove(
                    operation.SlaveItem, slaveWireType, operation.SlaveIndex));

            var bagAfter = character.Inventory.Bag.GetItemBySlot(operation.BagIndex);
            var slaveAfter = slave.Equipment.GetItemBySlot(operation.SlaveIndex);
            if (bagAfter != null)
                allTasks.Add(new ItemGain(
                    bagAfter, SlotType.Inventory, operation.BagIndex));
            if (slaveAfter != null)
                allTasks.Add(new ItemGain(
                    slaveAfter, slaveWireType, operation.SlaveIndex));
        }

        // The in-memory swap is not acknowledged until both the owner's bag and the slave's
        // 0xF2 container have been committed in one MySQL transaction. This keeps the exact item
        // instance (id, grade, durability/details, UCC and expiry) attached to this dbSlaveId.
        if (!SaveManager.Instance.SaveItemsForOwner(
                character.Id,
                $"slave equipment change dbSlaveId={slave.Id}"))
        {
            Logger.Error(
                "ChangeSlaveEquipment persistence failed; rolling back owner={0}, slave={1}, records={2}",
                character.Id, slave.Id, operations.Count);
            RollBack(character, slave, applied);
            Connection.SendPacket(new SCSlaveEquipmentChangedPacket(request, false));
            return;
        }

        if (allTasks.Count > 0)
            Connection.SendPacket(new SCItemTaskSuccessPacket(
                ItemTaskType.SwapItems, allTasks, []));

        Connection.SendPacket(new SCSlaveEquipmentChangedPacket(reply, true));

        var changed = new (byte slot, Item item)[operations.Count];
        for (var i = 0; i < operations.Count; i++)
        {
            var slot = operations[i].SlaveIndex;
            changed[i] = (slot, slave.Equipment.GetItemBySlot(slot));
        }

        character.BroadcastPacket(
            new SCUnitEquipmentsChangedPacket(
                slave.ObjId,
                changed,
                useSlaveProtocolSlots: true),
            true);

        // The slot/item delta alone does not create or remove the physical sail, cannon, rudder,
        // lamp, etc. Rebuild the attached doodad/child-slave components from the committed state.
        SlaveManager.Instance.SynchronizeEquipmentComponents(slave);

        Logger.Info(
            "Slave equipment changed and persisted: owner={0}, slave={1}/{2}, container={3}, " +
            "records={4}, equippedItems={5}",
            character.Id, slave.TemplateId, slave.Id, slave.Equipment.ContainerId,
            operations.Count, slave.Equipment.Items.Count);
    }

    private static bool IsSlaveContainer(SlotType slotType)
    {
        return slotType is SlotType.EquipmentSlave or SlotType.EquipmentSlavePreliminary;
    }

    private static bool SnapshotMatches(Item packetItem, Item serverItem)
    {
        if (packetItem == null || packetItem.TemplateId == 0)
            return serverItem == null;

        return serverItem != null &&
               packetItem.Id == serverItem.Id &&
               packetItem.TemplateId == serverItem.TemplateId;
    }

    private static void RollBack(
        Models.Game.Char.Character character,
        Models.Game.Units.Slave slave,
        List<EquipmentOperation> applied)
    {
        for (var i = applied.Count - 1; i >= 0; i--)
        {
            var operation = applied[i];
            var currentSlaveItem = slave.Equipment.GetItemBySlot(operation.SlaveIndex);
            var currentBagItem = character.Inventory.Bag.GetItemBySlot(operation.BagIndex);

            if (currentSlaveItem != null)
            {
                character.Inventory.SplitOrMoveItemEx(
                    ItemTaskType.Invalid,
                    slave.Equipment,
                    character.Inventory.Bag,
                    currentSlaveItem.Id,
                    SlotType.EquipmentSlave,
                    operation.SlaveIndex,
                    currentBagItem?.Id ?? 0,
                    SlotType.Inventory,
                    operation.BagIndex);
            }
            else if (currentBagItem != null)
            {
                character.Inventory.SplitOrMoveItemEx(
                    ItemTaskType.Invalid,
                    character.Inventory.Bag,
                    slave.Equipment,
                    currentBagItem.Id,
                    SlotType.Inventory,
                    operation.BagIndex,
                    0,
                    SlotType.EquipmentSlave,
                    operation.SlaveIndex);
            }
        }
    }

    private sealed record EquipmentOperation(
        SlaveEquipmentDelta Change,
        bool SourceIsInventory,
        byte BagIndex,
        byte SlaveIndex,
        Item BagItem,
        Item SlaveItem);
}
