using System;
using System.Linq;

using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Game.Items.Containers;

public class MateEquipmentContainer : EquipmentContainer
{
    public MateEquipmentContainer(uint ownerId, SlotType containerType, bool createWithNewId) : base(ownerId, containerType, createWithNewId)
    {
        // Fancy way of getting the last enum value + 1 for equipment slots
        ContainerSize = (int)Enum.GetValues(typeof(EquipmentItemSlot)).Cast<EquipmentItemSlot>().Max() + 1;
    }

    public override void OnEnterContainer(Item item, ItemContainer lastContainer)
    {
        Logger.Debug($"mate OnEnterContainer: mateId={MateId}, slot={item?.Slot}, template={item?.TemplateId}, itemId={item?.Id}");
        base.OnEnterContainer(item, lastContainer);
    }

    public override void OnLeaveContainer(Item item, ItemContainer newContainer)
    {
        Logger.Debug($"mate OnLeaveContainer: mateId={MateId}, slot={item?.Slot}, template={item?.TemplateId}, itemId={item?.Id}");
        base.OnLeaveContainer(item, newContainer);
    }

    /// <summary>
    /// A mate's gear belongs to the mate, not to the player who owns the container.
    /// </summary>
    /// <remarks>
    /// The inherited behaviour recalculated the *player's* gear bonuses, so putting a saddle on
    /// a mount refreshed the rider's own equipment buffs and gave the mount nothing. Mates have
    /// no stat pipeline of their own yet, so for now the honest thing is to apply the change to
    /// nobody: wrong bonuses on the wrong unit are worse than none.
    /// </remarks>
    protected override void ApplyGearBonuses(Item itemAdded, Item itemRemoved)
    {
    }
}
