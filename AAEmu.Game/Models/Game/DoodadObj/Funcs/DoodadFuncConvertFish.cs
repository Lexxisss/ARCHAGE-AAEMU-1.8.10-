using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncConvertFish : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncConvertFish");

        if (caster is not Character character)
            return;

        var backpack = character.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
        if (backpack == null)
        {
            character.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
            return;
        }

        if (!DoodadManager.Instance.TryGetConvertedFishItem(Id, backpack.TemplateId, out var convertItemId) ||
            convertItemId == 0)
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        if (character.Inventory.Bag.SpaceLeftForItem(convertItemId) < 1)
        {
            character.SendErrorMessage(ErrorMessageType.BagFull);
            return;
        }

        if (character.Equipment.ConsumeItem(ItemTaskType.Fishing, backpack.TemplateId, 1, backpack) != 1)
            return;

        if (!character.Inventory.TryAddNewItem(ItemTaskType.Fishing, convertItemId, 1))
            Logger.Error("DoodadFuncConvertFish: failed to add conversion item {0} after consuming fish {1} for {2}",
                convertItemId, backpack.TemplateId, character.Name);
    }
}
