using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items.Containers;

/// <summary>
/// Persistent equipment container used by ships and land vehicles.
/// </summary>
/// <remarks>
/// The target client keeps slave equipment in container 0xF2, independently from character
/// equipment (0x01) and mate equipment. Target x2game.dll iterates the full 35-slot array (0..34) for BaseUnitType.Slave.
/// </remarks>
public class SlaveEquipmentContainer : EquipmentContainer
{
    public const int ProtocolSlotCount = 35;

    /// <summary>The summoned runtime slave wearing this container, when it is currently spawned.</summary>
    public Slave Wearer { get; set; }

    public SlaveEquipmentContainer(uint ownerId, SlotType containerType, bool createWithNewId)
        : base(ownerId, containerType, createWithNewId)
    {
        ContainerSize = ProtocolSlotCount;
    }

    public SlaveEquipmentContainer(uint ownerId, Slave wearer, bool createWithNewId)
        : this(ownerId, SlotType.EquipmentSlave, createWithNewId)
    {
        Wearer = wearer;
        MateId = wearer?.Id ?? 0;
    }

    public override bool CanAccept(Item item, int targetSlot)
    {
        if (item == null)
            return true;

        if (targetSlot < 0 || targetSlot >= ProtocolSlotCount)
        {
            Logger.Warn(
                "Slave equipment rejected item {0} ({1}): slot {2} is outside 0..{3}",
                item.Id, item.TemplateId, targetSlot, ProtocolSlotCount - 1);
            return false;
        }

        // A persistent container can be loaded before the runtime Slave exists. Defer the
        // template-specific check until the wearer is attached during summon.
        if (Wearer == null)
            return true;

        return SlaveManager.Instance.CanEquipSlaveItem(Wearer.TemplateId, item.TemplateId, targetSlot);
    }

    // Slave stat bonuses are calculated by SlaveManager from the active slave. Do not let the
    // inherited player-owned container apply ship gear to the character.
    protected override void ApplyGearBonuses(Item itemAdded, Item itemRemoved)
    {
    }
}
