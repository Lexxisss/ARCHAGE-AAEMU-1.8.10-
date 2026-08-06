using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using MateUnit = AAEmu.Game.Models.Game.Units.Mate;

namespace AAEmu.Game.Models.Game.Skills.Plots;

/// <summary>
/// A unit requirement attached to a PlotCondition in the target database.
/// Only requirement kinds whose meaning is proven by the target data are evaluated;
/// unknown kinds fail closed and are reported once by PlotDiagnostics.
/// </summary>
public sealed class PlotUnitRequirement
{
    public bool DisplayMessage { get; set; }
    public int KindId { get; set; }
    public int Value1 { get; set; }
    public int Value2 { get; set; }
    public int Value3 { get; set; }

    public bool Check(BaseUnit source, BaseUnit target)
    {
        var unit = target ?? source;
        switch (KindId)
        {
            // PC/race requirement. value1=0 is used by "is PC race?" checks.
            case 3:
                return unit is Character raceCharacter &&
                       (Value1 == 0 || (int)raceCharacter.Race == Value1);

            // Character gender.
            case 4:
                return unit is Character genderCharacter &&
                       (int)genderCharacter.Gender == Value1;

            // Specific equipped item template.
            case 9:
                return unit is Character equippedCharacter &&
                       equippedCharacter.Inventory.GetItemsCount(SlotType.Equipment, (uint)Value1) > 0;

            // Item template present in the character inventory.
            case 10:
                return unit is Character inventoryCharacter &&
                       inventoryCharacter.Inventory.GetItemsCount((uint)Value1) > 0;

            // Combat-state flag (0 = out of combat, 1 = in combat).
            case 12:
                return unit is Unit combatUnit && combatUnit.IsInBattle == (Value1 != 0);

            // Exact active buff id. This is the dominant PlotCondition unit requirement
            // and is paired with target/source selectors by plot_event_conditions.
            case 15:
                return unit?.Buffs.GetEffectFromBuffId((uint)Value1) != null;

            // Target health percentage range (inclusive). The DB names for this kind are
            // explicit ("target health check", "health <= 50%", etc.).
            case 26:
                if (unit is not Unit hpUnit || hpUnit.MaxHp <= 0)
                    return false;
                var hpPercent = (int)(hpUnit.Hp * 100L / hpUnit.MaxHp);
                return hpPercent >= Value1 && hpPercent <= Value2;

            // Unit family selector, numbered as BaseUnitType and not as anything of our own.
            // The target data settles it: the events this gates are named for what they ask
            // about - "pve check" and "enemy target check" ask for 1, "is it a slave" for 2,
            // "is it housing" for 3, and the pet checks for 5. Reading 1 as a player was what
            // made every "pve check" false against an NPC, and with it the whole damage branch
            // of two dozen plots.
            case 38:
                return (BaseUnitType)Value1 switch
                {
                    BaseUnitType.Character => unit is Character,
                    BaseUnitType.Npc => unit is Npc,
                    BaseUnitType.Slave => unit is Slave,
                    BaseUnitType.Housing => unit is House,
                    BaseUnitType.Transfer => unit is Transfer,
                    BaseUnitType.Mate => unit is MateUnit,
                    _ => false
                };

            // Completed quest id.
            case 31:
                return unit is Character completedQuestCharacter &&
                       completedQuestCharacter.Quests.HasQuestCompleted((uint)Value1);

            // Active quest id.
            case 32:
                return unit is Character activeQuestCharacter &&
                       activeQuestCharacter.Quests.HasQuest((uint)Value1);

            // Target faction/mother-faction id.
            case 40:
                return FactionMatches(unit, (uint)Value1);

            // Source/user faction checks. Both kinds are present with the same proven
            // Nuia/Haranya/Pirate faction ids in the target DB.
            case 42:
            case 56:
                return FactionMatches(source, (uint)Value1);

            // Inventory item count. value2 is the required amount in target records.
            case 74:
                return unit is Character itemCharacter &&
                       itemCharacter.Inventory.GetItemsCount((uint)Value1) >= System.Math.Max(1, Value2);

            // Current zone-group id. Target records use zone-group values, not raw zone keys.
            case 101:
                return unit?.Transform != null &&
                       ZoneManager.Instance.GetZoneByKey(unit.Transform.ZoneId)?.GroupId == (uint)Value1;

            // At least one free normal-inventory slot.
            case 106:
                return unit is Character freeSlotCharacter &&
                       freeSlotCharacter.Inventory.FreeSlotCount(SlotType.Inventory) > 0;

            // Health threshold variants used by linked-skill condition records.
            case 138:
                return CheckHealthThreshold(unit, Value2, lessOrEqual: true);
            case 139:
                return CheckHealthThreshold(unit, Value2, lessOrEqual: false);

            default:
                PlotDiagnostics.UnsupportedUnitRequirement(KindId, Value1, Value2, Value3);
                return false;
        }
    }

    private static bool FactionMatches(BaseUnit unit, uint expected)
    {
        if (unit?.Faction == null)
            return false;
        var actual = unit.Faction.MotherId != 0 ? unit.Faction.MotherId : unit.Faction.Id;
        return actual == expected;
    }

    private static bool CheckHealthThreshold(BaseUnit unit, int threshold, bool lessOrEqual)
    {
        if (unit is not Unit hpUnit || hpUnit.MaxHp <= 0)
            return false;
        var hpPercent = (int)(hpUnit.Hp * 100L / hpUnit.MaxHp);
        return lessOrEqual ? hpPercent <= threshold : hpPercent >= threshold;
    }
}
