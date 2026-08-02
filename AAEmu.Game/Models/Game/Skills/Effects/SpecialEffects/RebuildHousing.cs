using System;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Converts an existing building into another design at the same spot.
/// </summary>
/// <remarks>
/// The rebuild is chosen by the pair of the skill being cast and the design the client asks
/// for, and it is paid for in materials rather than with a design item.
///
/// The client sends the target design and the placement in the skill's housing-placement
/// object. There is no separate rebuild-specific object type in this branch, so the first
/// field of that object is read as the design id here. If a distinct object type turns out to
/// exist, this is the place it has to be read from instead.
/// </remarks>
public class RebuildHousing : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Character character)
            return;

        Logger.Debug("Special effects: RebuildHousing value1 {0}, value2 {1}, value3 {2}, value4 {3}",
            value1, value2, value3, value4);

        if (target is not House house)
        {
            Logger.Debug("RebuildHousing: target is not a building");
            return;
        }

        if (castObj is not CastSkill castSkill)
        {
            Logger.Debug("RebuildHousing: cast source is not a skill");
            return;
        }

        if (skillObject is not SkillObjectHousingPlacement placement)
        {
            Logger.Debug("RebuildHousing: expected a housing placement object, got {0}",
                skillObject?.GetType().Name ?? "none");
            return;
        }

        // Only the owner may rebuild.
        if (house.OwnerId != character.Id)
        {
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return;
        }

        var targetDesignId = placement.Subtype;
        var rebuildingId = HousingManager.Instance.GetHousingRebuildingId(castSkill.SkillId, targetDesignId);
        if (rebuildingId == 0)
        {
            Logger.Debug("RebuildHousing: skill {0} does not offer design {1}", castSkill.SkillId, targetDesignId);
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return;
        }

        // Ask about the land before anything is spent. Rebuilding tears the old building down and
        // consumes the materials first, so a refusal after that point would leave the player with
        // neither building nor materials.
        var housePosition = house.Transform.World.Position;
        if (!HousingManager.Instance.CanPlaceDesign(character, targetDesignId, housePosition.X, housePosition.Y,
                out var placementError, house))
        {
            character.SendErrorMessage(placementError);
            return;
        }

        var materials = HousingManager.Instance.GetMaterialsByHousingRebuildingId(rebuildingId);

        // Check every material before consuming any of them, so a partial payment is impossible.
        if (materials.Any(material => !character.Inventory.CheckItems(SlotType.Inventory, material.ItemId, material.Count)))
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
            return;
        }

        foreach (var material in materials)
        {
            if (character.Inventory.Bag.ConsumeItem(ItemTaskType.HouseBuilding, material.ItemId, material.Count, null) > 0)
                continue;

            Logger.Error("RebuildHousing: failed to consume material {0} x{1} for {2}",
                material.ItemId, material.Count, character.Name);
            character.SendErrorMessage(ErrorMessageType.BagInvalidItem);
            return;
        }

        // Keep the name, and reuse the old building's own spot rather than trusting the
        // coordinates the client sent with the placement object.
        var oldName = house.Name;
        var position = house.Transform.World.Position;
        var zRot = house.Transform.World.Rotation.Z;

        HousingManager.Instance.DemolishBeforeRebuilding(character.Connection, house);
        HousingManager.Instance.Rebuild(character.Connection, targetDesignId,
            position.X, position.Y, position.Z, zRot, oldName);
    }
}
