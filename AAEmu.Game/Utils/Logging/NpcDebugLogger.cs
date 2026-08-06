using System;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Utils.Logging;

/// <summary>
/// Writes NPC appearance/presentation diagnostics only to Logs/NpcDebug.
/// The dedicated NLog rule is final, so these verbose lines do not pollute
/// Server.log or the normal console log.
/// </summary>
public static class NpcDebugLogger
{
    private static readonly Logger Logger = LogManager.GetLogger("NpcDebug");

    public static void Snapshot(string stage, Character viewer, Npc npc)
    {
        if (npc == null)
            return;

        try
        {
            var template = npc.Template;
            var items = npc.Equipment.GetSlottedItemsList();
            var equipped = string.Join(",", items
                .Take(Protocol1810EquipmentLayout.SlotCount)
                .Select((item, serverSlot) => item == null
                    ? null
                    : $"s{serverSlot}:{(EquipmentItemSlot)serverSlot}->w{Protocol1810EquipmentLayout.ToWireSlot(serverSlot)}=" +
                      $"tpl{item.TemplateId}/iid{item.Id}/g{item.Grade}/type{item.GetType().Name}")
                .Where(value => value != null));

            if (string.IsNullOrEmpty(equipped))
                equipped = "none";

            var initialIds = string.Join(",", items
                .Take(Protocol1810EquipmentLayout.SlotCount)
                .Select((item, serverSlot) =>
                {
                    if (item == null)
                        return null;

                    var wireSlot = Protocol1810EquipmentLayout.ToWireSlot(serverSlot);
                    return Protocol1810EquipmentLayout.IsBodyPartWireSlot(wireSlot)
                        ? null
                        : $"w{wireSlot}=iid{item.Id}";
                })
                .Where(value => value != null));

            if (string.IsNullOrEmpty(initialIds))
                initialIds = "none";

            var bodyParts = string.Join(",", template.BodyItems
                .Select((part, index) => part.ItemId == 0
                    ? null
                    : $"{index + (int)EquipmentItemSlot.Face}:{(EquipmentItemSlot)(index + (int)EquipmentItemSlot.Face)}=tpl{part.ItemId}/npcOnly{(part.NpcOnly ? 1 : 0)}")
                .Where(value => value != null));

            if (template.WingItem.ItemId != 0)
                bodyParts = bodyParts == ""
                    ? $"34:Wings=tpl{template.WingItem.ItemId}/npcOnly{(template.WingItem.NpcOnly ? 1 : 0)}"
                    : bodyParts + $",34:Wings=tpl{template.WingItem.ItemId}/npcOnly{(template.WingItem.NpcOnly ? 1 : 0)}";

            if (string.IsNullOrEmpty(bodyParts))
                bodyParts = "implicit/default";

            var hasHead = npc.Equipment.GetItemBySlot((int)EquipmentItemSlot.Head) != null;
            var hasCosplay = npc.Equipment.GetItemBySlot((int)EquipmentItemSlot.Cosplay) != null;
            // Mirror SCUnitState's packet-local NPC appearance selection so the
            // diagnostic reports the bytes actually written on the wire.
            var sourceModelParams = npc.ModelParams ?? new UnitCustomModelParams(UnitCustomModelType.None);
            var wireModelParams = new UnitCustomModelParams(UnitCustomModelType.None);
            if (npc.ModelId is 10 or 11 or 14 or 15 or 16 or 17 or 18 or 19 or 20 or 21 or 24 or 25)
                wireModelParams = sourceModelParams.CloneForNpcWire(template.Race, template.Gender);

            var npcWireExt = (byte)wireModelParams.Type;
            var appearanceWireLength = wireModelParams.Write(new PacketStream()).Count;
            // V17 sends no post-state SCUnitVisualOptions packet.
            byte visualMask = 0;
            var viewerInfo = viewer == null
                ? "viewer=none"
                : $"viewerObj={viewer.ObjId}|viewerId={viewer.Id}|viewerName={Clean(viewer.Name)}";

            Logger.Info(
                "stage={0}|{1}|npcId={2}|obj={3}|runtimeName={4}|templateName={5}|showNameTag={6}|model={7}|race={8}|gender={9}|customDirect={10}|customResolved={11}|customResolvedModel={12}|customDefault={13}|customResolution={14}|noApply={15}|appearanceType={16}|npcWireExt={37}|appearanceSlot0C={17}|modifierLength={18}|appearanceWireLength={19}|hairColor={20}|defaultHairColor={21}|twoToneHair={22}|modifierHash=0x{23:X8}|clothPack={24}|weaponPack={25}|visualMask=0x{26:X2}|headFlag={27}|cosplayFlag={28}|cosplayVisual={29}|cosplayVisualResolution={30}|cosplayPrimary={31}|cosplaySecondary={32}|equip=[{33}]|initialIds=[{34}]|body=[{35}]|pos={36}|wireBaseRace={38}|wireBaseGender={39}|wireVisualRace={40}|wireVisualGender={41}",
                Clean(stage), viewerInfo, npc.TemplateId, npc.ObjId, Clean(npc.Name), Clean(template.Name),
                template.ShowNameTag ? 1 : 0, npc.ModelId, template.Race, template.Gender,
                template.TotalCustomId, template.ResolvedCustomId, template.ResolvedCustomModelId,
                template.DefaultCustomId, Clean(template.CustomResolution),
                template.NoApplyTotalCustom ? 1 : 0, npc.ModelParams?.Type,
                npc.ModelParams?.BodyDiffuseOrModelDefaultId ?? 0,
                npc.ModelParams?.Face?.Modifier?.Length ?? 0,
                appearanceWireLength,
                npc.ModelParams?.HairColorId ?? 0,
                npc.ModelParams?.DefaultHairColor ?? 0,
                npc.ModelParams?.TwoToneHair ?? 0,
                HashModifier(npc.ModelParams?.Face?.Modifier),
                template.EquipClothsId, template.EquipWeaponsId,
                visualMask, hasHead ? 1 : 0, hasCosplay ? 1 : 0, 0,
                Clean(template.CosplayVisualResolution), Clean(template.CosplayPrimaryAssetPath),
                Clean(template.CosplaySecondaryAssetPath), equipped, initialIds, bodyParts,
                Clean(npc.Transform?.ToString()), npcWireExt,
                wireModelParams.Face?.BaseRace ?? 0,
                wireModelParams.Face?.BaseGender ?? 0,
                wireModelParams.Face?.VisualRace ?? 0,
                wireModelParams.Face?.VisualGender ?? 0);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "stage={0}|npc debug snapshot failed", Clean(stage));
        }
    }

    private static uint HashModifier(byte[] modifier)
    {
        if (modifier == null)
            return 0;

        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var value in modifier)
        {
            hash ^= value;
            hash *= prime;
        }

        return hash;
    }

    private static string Clean(string value)
    {
        return string.IsNullOrEmpty(value)
            ? ""
            : value.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
    }
}
