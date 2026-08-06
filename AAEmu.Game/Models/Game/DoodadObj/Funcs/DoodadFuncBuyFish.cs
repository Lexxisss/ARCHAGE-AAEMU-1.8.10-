using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncBuyFish : DoodadFuncTemplate
{
    public uint ItemId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncBuyFish");

        if (caster is not Character character)
            return;

        var backpack = character.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
        if (backpack == null)
        {
            character.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
            return;
        }

        if (!DoodadManager.Instance.CanBuyFish(Id, backpack.TemplateId))
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        owner.ItemTemplateId = backpack.TemplateId;

        var payout = backpack.Template?.Refund ?? 0;
        if (payout <= 0)
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        if (character.Equipment.ConsumeItem(ItemTaskType.SellBackpack, backpack.TemplateId, 1, backpack) != 1)
            return;

        character.AddMoney(SlotType.Inventory, payout, ItemTaskType.SellBackpack);
    }
}
