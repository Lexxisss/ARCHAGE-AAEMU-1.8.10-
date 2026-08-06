using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class FishingLoot : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.FishingLoot;

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

        Logger.Debug("Special effects: FishingLoot value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

        var zone = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId);
        var zoneGroup = zone == null ? null : ZoneManager.Instance.GetZoneGroupById(zone.GroupId);
        if (zoneGroup == null)
        {
            Logger.Warn("{0} tried to fish outside a configured zone.", character.Name);
            return;
        }

        var targetHeight = target?.Transform.World.Position.Z ?? character.Transform.World.Position.Z;
        switch (targetObj)
        {
            case SkillCastPositionTarget position:
                targetHeight = position.PosZ;
                break;
            case SkillCastPosition2Target position:
                targetHeight = position.PosZ;
                break;
            case SkillCastPosition3Target position:
                targetHeight = position.PosZ;
                break;
        }

        var lootTableId = targetHeight > 101f
            ? zoneGroup.FishingLandLootPackId
            : zoneGroup.FishingSeaLootPackId;
        var pack = LootGameData.Instance.GetPack(lootTableId);
        if (pack?.Loots == null || pack.Loots.Count == 0)
            return;

        var generatedList = pack.GeneratePackNew(character, ActabilityType.Fishing);
        if (!pack.GiveLootPack(character, ItemTaskType.SkillEffectGainItem, generatedList))
            character.SendErrorMessage(ErrorMessageType.BagFull);
    }
}
