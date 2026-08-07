using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// One player's crafting: the request the client sends, everything the server checks before it
/// believes it, and the transaction that turns materials and labour into products.
/// </summary>
/// <remarks>
/// The client checks all of this too and refuses to send when it fails. That is a reason not to
/// expect refusals, not a reason to trust the request - the count in particular is a batch counter
/// the client keeps for itself, not permission to produce that many at once.
///
/// One request is one cycle. The client sends the next itself once the previous one lands.
/// </remarks>
public class CharacterCraft
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>Beyond this the request is refused rather than clamped.</summary>
    private const int MaxRequestedCount = 1000;

    /// <summary>How far a player may stand from the station they are using.</summary>
    private const float MaxStationDistance = 8f;

    /// <summary>The scale a product's rate is on: at or above this it always drops.</summary>
    private const int ProductRateScale = 100;

    private int _count;
    private Craft _craft;
    private uint _doodadId;

    public Character Owner { get; set; }
    public bool IsCrafting { get; set; }

    public CharacterCraft(Character owner) => Owner = owner;

    /// <summary>
    /// Starts one cycle of a recipe, if everything it asks for is actually there.
    /// </summary>
    public void Craft(Craft craft, int count, uint doodadId)
    {
        if (!Validate(craft, count, doodadId, out var error))
        {
            Fail(craft?.Id ?? 0, error);
            return;
        }

        _craft = craft;
        _count = count;
        _doodadId = doodadId;
        IsCrafting = true;

        var skillTemplate = SkillManager.Instance.GetSkillTemplate(craft.SkillId);
        if (skillTemplate == null)
        {
            // A recipe with no skill still has to produce something, or a whole family of them
            // silently does nothing at all.
            Logger.Debug($"Craft {craft.Id} has no skill {craft.SkillId}, completing without a cast");
            EndCraft();
            return;
        }

        var caster = SkillCaster.GetByType(SkillCasterType.Unit);
        caster.ObjId = Owner.ObjId;

        var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
        target.ObjId = doodadId;

        new Skill(skillTemplate).Use(Owner, caster, target);
    }

    /// <summary>
    /// Everything that has to hold before the player is charged anything.
    /// </summary>
    /// <remarks>
    /// Cheapest and most certain checks first, so a malformed request is turned away before
    /// anything reaches the inventory or the world.
    /// </remarks>
    private bool Validate(Craft craft, int count, uint doodadId, out ErrorMessageType error)
    {
        error = ErrorMessageType.CraftInvalidCraftType;

        if (craft == null)
            return false;

        if (count <= 0 || count > MaxRequestedCount)
        {
            error = ErrorMessageType.CraftInvalidAmount;
            return false;
        }

        if (Owner == null || Owner.IsDead)
        {
            error = ErrorMessageType.CraftCantActAnyMore;
            return false;
        }

        // The station. A recipe that names one may only be worked at that one, within reach.
        var doodad = WorldManager.Instance.GetDoodad(doodadId);
        if (craft.ReqDoodadId > 0)
        {
            if (doodad == null)
            {
                error = ErrorMessageType.CraftLocatingUnitIsNotExist;
                return false;
            }

            if (doodad.TemplateId != craft.ReqDoodadId)
            {
                error = ErrorMessageType.CraftToolRequired;
                return false;
            }
        }

        if (doodad != null)
        {
            if (doodad.Transform.WorldId != Owner.Transform.WorldId ||
                MathUtil.CalculateDistance(Owner.Transform.World.Position, doodad.Transform.World.Position, true) > MaxStationDistance)
            {
                error = ErrorMessageType.CraftLocatingUnitIsNotExist;
                return false;
            }

            if (!HasPermission(doodad))
            {
                error = ErrorMessageType.CraftPermissionDeny;
                return false;
            }
        }

        if (Owner.LaborPower < craft.Cost)
        {
            error = ErrorMessageType.NotEnoughLaborPower;
            return false;
        }

        foreach (var material in craft.CraftMaterials)
        {
            if (Owner.Inventory.GetItemsCount(material.ItemId) < material.Amount)
            {
                error = ErrorMessageType.CraftMaterialRequired;
                return false;
            }
        }

        // Room for the outputs, counted against every product the recipe can roll rather than the
        // ones it happens to roll, so a cycle never half-completes for want of a slot.
        if (Owner.Inventory.FreeSlotCount(SlotType.Inventory) < craft.CraftProducts.Count)
        {
            error = ErrorMessageType.CraftUpdaetInventory;
            return false;
        }

        return true;
    }

    /// <summary>Whether the player may use this station.</summary>
    private bool HasPermission(Doodad doodad)
    {
        if (Owner == null)
            return false;

        switch (doodad.FuncPermission)
        {
            case DoodadFuncPermission.Any:
            case DoodadFuncPermission.Permission1:
            case DoodadFuncPermission.Permission2:
            case DoodadFuncPermission.OwnerOnly:
            case DoodadFuncPermission.Permission4:
            case DoodadFuncPermission.OwnerRaidMembers:
                return true;

            case DoodadFuncPermission.SameAccount:
                if (doodad.OwnerType != DoodadOwnerType.Character)
                    return true;
                return WorldManager.Instance.GetCharacterById(doodad.OwnerId)?.AccountId == Owner.AccountId;

            case DoodadFuncPermission.ZoneResidents:
                var zoneGroup = ZoneManager.Instance.GetZoneByKey(doodad.Transform.ZoneId)?.GroupId ?? 0;
                var playerHouses = new Dictionary<uint, House>();
                if (HousingManager.Instance.GetByAccountId(playerHouses, Owner.AccountId) <= 0)
                    return false;

                foreach (var (_, playerHouse) in playerHouses)
                {
                    var houseZoneGroup = ZoneManager.Instance.GetZoneByKey(playerHouse.Transform.ZoneId)?.GroupId ?? 0;
                    if (houseZoneGroup == zoneGroup)
                        return true;
                }

                return false;

            default:
                Logger.Warn($"Unknown station permission {doodad.FuncPermission} on {doodad.ObjId}, refusing");
                return false;
        }
    }

    /// <summary>
    /// Finishes one cycle: takes the materials and the labour, hands over what the recipe rolled.
    /// </summary>
    /// <remarks>
    /// Materials come out before products go in. The other way round - which is how this used to
    /// run - a player whose materials could not be taken kept both sides of the trade.
    /// </remarks>
    public void EndCraft()
    {
        _count--;
        IsCrafting = false;

        if (_craft == null)
        {
            CancelCraft();
            return;
        }

        var craft = _craft;

        // Checked again: a cycle finishes a cast away from its request, and an inventory can
        // change in between.
        if (!Validate(craft, Math.Max(_count + 1, 1), _doodadId, out var error))
        {
            Fail(craft.Id, error);
            CancelCraft();
            return;
        }

        var products = RollProducts(craft);

        foreach (var material in craft.CraftMaterials)
        {
            if (Owner.Inventory.Bag.ConsumeItem(ItemTaskType.CraftActSaved, material.ItemId, material.Amount, null) > 0)
                continue;

            Logger.Error($"Craft {craft.Id}: could not take {material.Amount} of item {material.ItemId} from {Owner.Name}");
            Fail(craft.Id, ErrorMessageType.CraftMaterialRequired);
            CancelCraft();
            return;
        }

        foreach (var product in products)
        {
            if (ItemManager.Instance.IsAutoEquipTradePack(product.ItemId))
            {
                if (!Owner.Inventory.TryEquipNewBackPack(ItemTaskType.CraftPickupProduct, product.ItemId, product.Amount, -1, Owner.Id))
                {
                    Fail(craft.Id, ErrorMessageType.CraftUpdaetInventory);
                    CancelCraft();
                    return;
                }

                continue;
            }

            Owner.Inventory.Bag.AcquireDefaultItem(ItemTaskType.CraftActSaved, product.ItemId, product.Amount, -1, Owner.Id);
        }

        // Labour last, so a cycle that could not be completed has not charged for itself. The
        // recipe's own cost is what counts - it was never read at all, so crafting was free.
        if (craft.Cost > 0)
            Owner.ChangeLabor((short)-craft.Cost, (int)craft.AcId);

        QuestManager.Instance.DoOnCraftEvents(Owner, craft.Id);

        if (_count > 0)
            ScheduleCraft();
        else
            CancelCraft();
    }

    /// <summary>
    /// Chooses which of a recipe's products this cycle yields.
    /// </summary>
    /// <remarks>
    /// A product row carries a rate, and that rate is what proves a recipe can have chancy or
    /// several outputs. Everything at or above full rate always drops. Every row used to be handed
    /// out unconditionally, so a recipe with a rare extra output produced it every single time.
    ///
    /// The roll is the server's; nothing from the client reaches it.
    /// </remarks>
    private static List<CraftProduct> RollProducts(Craft craft)
    {
        var chosen = new List<CraftProduct>(craft.CraftProducts.Count);

        foreach (var product in craft.CraftProducts)
        {
            if (product.Amount <= 0)
                continue;

            if (product.Rate >= ProductRateScale)
            {
                chosen.Add(product);
                continue;
            }

            if (product.Rate > 0 && Rand.Next(ProductRateScale) < product.Rate)
                chosen.Add(product);
        }

        return chosen;
    }

    private void Fail(uint craftId, ErrorMessageType error)
    {
        Logger.Debug($"Craft {craftId} refused for {Owner?.Name}: {error}");
        Owner?.SendPacket(new SCCraftFailedPacket((uint)error, craftId, 1));
    }

    private void ScheduleCraft()
    {
        var newCraft = new CraftTask(Owner, _craft.Id, _doodadId, _count);
        var skillTemplate = SkillManager.Instance.GetSkillTemplate(_craft.SkillId);
        var cooldown = skillTemplate?.CooldownTime ?? 0;
        var timeToGlobalCooldown = Owner.GlobalCooldown - DateTime.UtcNow;
        var nextCraftDelay = timeToGlobalCooldown.TotalMilliseconds > cooldown
            ? timeToGlobalCooldown
            : TimeSpan.FromMilliseconds(cooldown);
        TaskManager.Instance.Schedule(newCraft, nextCraftDelay, null, 1);
    }

    public void CancelCraft()
    {
        IsCrafting = false;
        _craft = null;
        _count = 0;
        _doodadId = 0;

        if (Owner == null)
            return;

        // Read it once - a skill finishing on another thread clears this between the test and
        // the assignment that used to follow it.
        var skillTask = Owner.SkillTask;
        if (skillTask != null)
            skillTask.Skill.Cancelled = true;
        Owner.InterruptSkills();
    }
}
