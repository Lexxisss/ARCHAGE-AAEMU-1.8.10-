using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.UnitManagers;

public class NpcManager : Singleton<NpcManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded = false;

    private Dictionary<uint, NpcTemplate> _templates;
    private Dictionary<uint, TotalCharacterCustom> _totalCharacterCustoms;
    private Dictionary<uint, Dictionary<uint, List<BodyPartTemplate>>> _itemBodyParts;
    private Dictionary<uint, uint> _defaultFaceItemsByModel;
    private Dictionary<uint, BodyPartTemplate> _defaultBodyItemsByModel;
    private Dictionary<(uint ItemId, uint ModelId), ArmorVisualVariants> _armorVisualVariants;
    private Dictionary<uint, uint> _skinColorModels;
    private Dictionary<uint, uint> _bodyNormalMapModels;
    private Dictionary<uint, uint> _faceDiffuseMapModels;
    private Dictionary<uint, uint> _faceNormalMapModels;
    private Dictionary<uint, uint> _faceEyelashMapModels;
    private Dictionary<uint, uint> _faceDecalModels;

    private sealed class ArmorVisualVariants
    {
        public string PrimaryPath { get; set; } = string.Empty;
        public string SecondaryPath { get; set; } = string.Empty;
    }
    public Dictionary<uint, NpcSpawnerNpc> _npcSpawnerNpc;    // npcSpawnerId, nsn
    public Dictionary<uint, NpcSpawnerTemplate> _npcSpawners; // npcSpawnerId, template
    public Dictionary<uint, List<uint>> _npcMemberAndSpawnerId; // memberId, List<npcSpawnerId>

    public bool Exist(uint templateId)
    {
        return _templates.ContainsKey(templateId);
    }

    public NpcTemplate GetTemplate(uint templateId)
    {
        return _templates.TryGetValue(templateId, out var template) ? template : null;
    }

    public Dictionary<uint, NpcTemplate> GetAllTemplates()
    {
        return _templates;
    }

    public IReadOnlyList<Merchants> GetMerchantGoods(uint npcTemplateId)
    {
        return VendorGameData.Instance.GetMerchantGoods(npcTemplateId);
    }

    public IReadOnlyList<MerchantPacks> GetMerchantPacks(uint packId)
    {
        return VendorGameData.Instance.GetMerchantPacks(packId);
    }

    private void ApplyExplicitCustomBodyParts(NpcTemplate template, TotalCharacterCustom custom)
    {
        if (template == null || !_itemBodyParts.TryGetValue(template.ModelId, out var slots))
            return;

        _totalCharacterCustoms.TryGetValue(template.DefaultCustomId, out var defaultCustom);
        if (defaultCustom?.ModelId != template.ModelId)
            defaultCustom = null;

        _defaultFaceItemsByModel.TryGetValue(template.ModelId, out var defaultFaceItemId);
        _defaultBodyItemsByModel.TryGetValue(template.ModelId, out var standardBody);

        // Face zero means the character model's canonical face. Never use the first
        // row in item_body_parts: for Firran that row has no eye geometry and makes
        // otherwise different NPCs collapse to the same fallback head.
        ApplyResolvedBodyPart(template, slots, EquipmentItemSlotType.Face,
            custom?.FaceId ?? 0, defaultFaceItemId);

        // Hair zero is an explicit bald choice. If a non-zero custom hair is invalid
        // for this model, only then fall back to the same model's default custom hair.
        var hairId = custom?.HairId ?? 0;
        if (hairId == 0)
            ClearResolvedBodyPart(template, EquipmentItemSlotType.Hair);
        else
            ApplyResolvedBodyPart(template, slots, EquipmentItemSlotType.Hair,
                hairId, defaultCustom?.HairId ?? 0);

        ApplyResolvedBodyPart(template, slots, EquipmentItemSlotType.Horns,
            custom?.HornId ?? 0);
        ApplyResolvedBodyPart(template, slots, EquipmentItemSlotType.Tail,
            custom?.TailId ?? 0);

        // body_id=0 is not "no body". It means the canonical nude under-body for
        // this exact model, over which normal armor or a one-piece cosplay is drawn.
        // Select it by its model-bound /nude/ asset path and never by arbitrary row
        // order (which can select a mannequin body instead).
        ApplyResolvedBodyPart(template, slots, EquipmentItemSlotType.Body,
            custom?.BodyId ?? 0,
            defaultCustom?.BodyId ?? 0,
            standardBody?.ItemId ?? 0);

        // The full client DB has a separate slot_type_id=34 body-part path for wings.
        // It is outside Face..Beard and is sent through target SCUnitState slot 34.
        template.WingItem = default;
        var wing = FindCompatibleBodyPart(slots, EquipmentItemSlotType.Wings,
            custom?.WingId ?? 0);
        if (wing != null)
            template.WingItem = (wing.ItemId, wing.NpcOnly);
    }

    private static BodyPartTemplate FindCompatibleBodyPart(
        Dictionary<uint, List<BodyPartTemplate>> slots,
        EquipmentItemSlotType slot,
        params uint[] candidateItemIds)
    {
        if (!slots.TryGetValue((uint)slot, out var candidates))
            return null;

        foreach (var candidateItemId in candidateItemIds)
        {
            if (candidateItemId == 0)
                continue;

            var match = candidates.FirstOrDefault(x => x.ItemId == candidateItemId);
            if (match != null)
                return match;
        }

        return null;
    }

    private static void ClearResolvedBodyPart(NpcTemplate template, EquipmentItemSlotType slot)
    {
        var bodyIndex = (int)slot - (int)EquipmentItemSlotType.Face;
        if (bodyIndex >= 0 && bodyIndex < template.BodyItems.Length)
            template.BodyItems[bodyIndex] = default;
    }

    private void ApplyResolvedBodyPart(
        NpcTemplate template,
        Dictionary<uint, List<BodyPartTemplate>> slots,
        EquipmentItemSlotType slot,
        params uint[] candidateItemIds)
    {
        var bodyIndex = (int)slot - (int)EquipmentItemSlotType.Face;
        if (bodyIndex < 0 || bodyIndex >= template.BodyItems.Length)
            return;

        template.BodyItems[bodyIndex] = default;
        var match = FindCompatibleBodyPart(slots, slot, candidateItemIds);
        if (match == null)
        {
            if (candidateItemIds.Any(x => x != 0))
                Logger.Warn(
                    "NPC model-bound body part not found: npc={0}, model={1}, slot={2}, candidates=[{3}]",
                    template.Id, template.ModelId, slot,
                    string.Join(",", candidateItemIds.Where(x => x != 0)));
            return;
        }

        template.BodyItems[bodyIndex] = (match.ItemId, match.NpcOnly);
    }

    private static bool IsCanonicalNudeBodyAsset(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        var normalized = assetPath.Replace('\\', '/').ToLowerInvariant();
        return normalized.Contains("/nude/") &&
               !normalized.Contains("/maneking/") &&
               !normalized.Contains("/mannequin/");
    }


    private TotalCharacterCustom ResolveCompatibleCustom(NpcTemplate template, out uint resolvedCustomId, out string resolution)
    {
        resolvedCustomId = 0;

        if (template.NoApplyTotalCustom)
        {
            resolution = "suppressed:no_apply_total_custom";
            return null;
        }

        if (!template.IsHumanoidModel)
        {
            resolution = "none:non_humanoid_model";
            return null;
        }

        // Explicit NPC data always wins. It is accepted only when the custom was
        // authored for the exact model/skeleton used by this NPC.
        if (template.TotalCustomId != 0)
        {
            if (_totalCharacterCustoms.TryGetValue(template.TotalCustomId, out var direct))
            {
                if (direct.ModelId == template.ModelId)
                {
                    resolvedCustomId = direct.Id;
                    resolution = "direct:compatible";
                    return direct;
                }

                Logger.Warn(
                    "NPC custom/model mismatch: npc={0}, npcModel={1}, custom={2}, customModel={3}; trying model default",
                    template.Id, template.ModelId, direct.Id, direct.ModelId);
            }
            else
            {
                Logger.Warn(
                    "NPC custom missing: npc={0}, npcModel={1}, custom={2}; trying model default",
                    template.Id, template.ModelId, template.TotalCustomId);
            }

            if (TryResolveModelDefaultCustom(template, out var directFallback))
            {
                resolvedCustomId = directFallback.Id;
                resolution = "fallback:model_default_from_direct";
                return directFallback;
            }

            resolution = "none:incompatible_direct";
            return null;
        }

        if (TryResolveModelDefaultCustom(template, out var modelDefault))
        {
            resolvedCustomId = modelDefault.Id;
            resolution = "model_default:compatible";
            return modelDefault;
        }

        resolution = "none:no_custom";
        return null;
    }

    private bool TryResolveModelDefaultCustom(NpcTemplate template, out TotalCharacterCustom custom)
    {
        custom = null;
        if (template.DefaultCustomId == 0 ||
            !_totalCharacterCustoms.TryGetValue(template.DefaultCustomId, out var candidate) ||
            candidate.ModelId != template.ModelId)
            return false;

        custom = candidate;
        return true;
    }

    private static void LoadModelBoundAssetIndex(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        string tableName,
        Dictionary<uint, uint> destination)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT id, model_id FROM {tableName}";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
            destination[reader.GetUInt32("id", 0)] = reader.GetUInt32("model_id", 0);
    }

    private static bool IsModelBoundAssetCompatible(
        uint assetId,
        uint modelId,
        IReadOnlyDictionary<uint, uint> assetModels)
    {
        if (assetId == 0)
            return true;

        return assetModels.TryGetValue(assetId, out var assetModelId) &&
               (assetModelId == 0 || assetModelId == modelId);
    }

    private uint ResolveSkinColorId(
        NpcTemplate template,
        TotalCharacterCustom selectedCustom,
        TotalCharacterCustom modelDefaultCustom)
    {
        var requestedId = selectedCustom?.SkinColorId ?? 0;
        if (IsModelBoundAssetCompatible(requestedId, template.ModelId, _skinColorModels))
            return requestedId;

        var fallbackId = modelDefaultCustom?.SkinColorId ?? 0;
        if (IsModelBoundAssetCompatible(fallbackId, template.ModelId, _skinColorModels))
        {
            Logger.Warn(
                "NPC skin color/model mismatch corrected: npc={0}, model={1}, requested={2}, fallback={3}",
                template.Id, template.ModelId, requestedId, fallbackId);
            return fallbackId;
        }

        Logger.Warn(
            "NPC skin color/model mismatch has no compatible fallback: npc={0}, model={1}, requested={2}",
            template.Id, template.ModelId, requestedId);
        return 0;
    }

    private uint ResolveOptionalModelBoundAssetId(
        NpcTemplate template,
        string fieldName,
        uint requestedId,
        IReadOnlyDictionary<uint, uint> assetModels)
    {
        if (IsModelBoundAssetCompatible(requestedId, template.ModelId, assetModels))
            return requestedId;

        Logger.Warn(
            "NPC model-bound appearance asset suppressed: npc={0}, model={1}, field={2}, asset={3}",
            template.Id, template.ModelId, fieldName, requestedId);
        return 0;
    }


    private ArmorVisualVariants GetArmorVisualVariants(uint itemId, uint modelId)
    {
        if (_armorVisualVariants.TryGetValue((itemId, modelId), out var exact))
            return exact;

        // Some client-authored equipment intentionally uses model 0 as a generic asset.
        return _armorVisualVariants.TryGetValue((itemId, 0), out var generic) ? generic : null;
    }

    private void ResolveNpcCosplayVisual(NpcTemplate template)
    {
        template.CosplayVisual = 0;
        template.CosplayPrimaryAssetPath = string.Empty;
        template.CosplaySecondaryAssetPath = string.Empty;
        template.CosplayVisualResolution = template.Items.Cosplay == 0
            ? "none:no_cosplay"
            : "primary:no_compatible_asset";

        if (template.Items.Cosplay == 0)
            return;

        var variants = GetArmorVisualVariants(template.Items.Cosplay, template.ModelId);
        if (variants == null)
            return;

        template.CosplayPrimaryAssetPath = variants.PrimaryPath ?? string.Empty;
        template.CosplaySecondaryAssetPath = variants.SecondaryPath ?? string.Empty;

        if (string.IsNullOrWhiteSpace(template.CosplaySecondaryAssetPath))
        {
            template.CosplayVisualResolution = "primary:secondary_missing";
            return;
        }

        // TARGET-DIRECT: cosplay_visual=0 resolves item_armors.asset_id, while 1
        // selects asset2_id when a compatible asset exists. The client DB has no
        // per-NPC cosplay_visual column, so only the explicit *_h secondary naming
        // convention is selected automatically. This fixes hooded NPC costumes
        // without forcing unrelated alternate assets on every costume user.
        var secondaryName = Path.GetFileNameWithoutExtension(template.CosplaySecondaryAssetPath);
        if (secondaryName.EndsWith("_h", StringComparison.OrdinalIgnoreCase))
        {
            template.CosplayVisual = 1;
            template.CosplayVisualResolution = "secondary:hood_suffix";
        }
        else
        {
            template.CosplayVisualResolution = "primary:secondary_not_hood";
        }
    }


    private static float NormalizeDecalWeight(uint assetId, float weight)
    {
        // The client DB commonly stores weight=1 for an empty (id=0) decal slot.
        // Sending that pair verbatim asks the renderer to blend a missing/default
        // resource at full strength, which is the source of phantom facial overlays.
        if (assetId == 0 || float.IsNaN(weight) || float.IsInfinity(weight))
            return 0f;

        return Math.Clamp(weight, 0f, 1f);
    }

    private static float NormalizeMovableDecalScale(uint assetId, float scale)
    {
        if (assetId == 0)
            return 1f;

        return scale > 0f && !float.IsNaN(scale) && !float.IsInfinity(scale) ? scale : 1f;
    }

    public Npc Create(uint objectId, uint id)
    {
        var template = GetTemplate(id);
        if (template == null)
        {
            return null;
        }

        var npc = new Npc();
        npc.ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId();
        npc.TemplateId = id; // duplicate Id
        npc.Id = id;
        npc.Template = template;
        npc.Name = template.Name ?? string.Empty;
        npc.ModelId = template.ModelId;
        // total_custom_id is client appearance data. Copy the resolved skin/face
        // parameters to the live NPC; otherwise SCUnitState emits no material data.
        // Flight/swim capability must be resolved from the target client data.
        // This branch has no ModelManager; keep the unit default instead of using
        // an API from another AAEmu version.
        npc.CanFly = false;
        npc.Faction = FactionManager.Instance.GetFaction(template.FactionId);
        npc.Level = template.Level;
        npc.Patrol = null;

        // Appearance is resolved once while loading the target client DB. An explicit
        // total_custom_id wins. Rows with total_custom_id=0 keep the model default; their
        // DB-authored Head/Cosplay equipment supplies the intended visible variation.
        npc.ModelParams = template.NoApplyTotalCustom
            ? new UnitCustomModelParams(UnitCustomModelType.None)
            : template.ModelParams ?? new UnitCustomModelParams(UnitCustomModelType.None);

        // Do not write the appearance tail into template.ModelParams here. The
        // object belongs to the cached NPC template and is shared by all live
        // instances. SCUnitState builds a deep packet-local copy and supplies the
        // ordinary BaseRace/BaseGender there. In target x2game.dll VisualRace != 0
        // means an active visual-race transformation, not the NPC's normal race.

        SetEquipItemTemplate(npc, template.Items.Headgear, EquipmentItemSlot.Head, template.Items.HeadgearGrade);
        SetEquipItemTemplate(npc, template.Items.Necklace, EquipmentItemSlot.Neck, template.Items.NecklaceGrade);
        SetEquipItemTemplate(npc, template.Items.Shirt, EquipmentItemSlot.Chest, template.Items.ShirtGrade);
        SetEquipItemTemplate(npc, template.Items.Belt, EquipmentItemSlot.Waist, template.Items.BeltGrade);
        SetEquipItemTemplate(npc, template.Items.Pants, EquipmentItemSlot.Legs, template.Items.PantsGrade);
        SetEquipItemTemplate(npc, template.Items.Gloves, EquipmentItemSlot.Hands, template.Items.GlovesGrade);
        SetEquipItemTemplate(npc, template.Items.Shoes, EquipmentItemSlot.Feet, template.Items.ShoesGrade);
        SetEquipItemTemplate(npc, template.Items.Bracelet, EquipmentItemSlot.Arms, template.Items.BraceletGrade);
        SetEquipItemTemplate(npc, template.Items.Back, EquipmentItemSlot.Back, template.Items.BackGrade);
        SetEquipItemTemplate(npc, template.Items.Undershirts, EquipmentItemSlot.Undershirt, template.Items.UndershirtsGrade);
        SetEquipItemTemplate(npc, template.Items.Underpants, EquipmentItemSlot.Underpants, template.Items.UnderpantsGrade);
        SetEquipItemTemplate(npc, template.Items.Mainhand, EquipmentItemSlot.Mainhand, template.Items.MainhandGrade);
        SetEquipItemTemplate(npc, template.Items.Offhand, EquipmentItemSlot.Offhand, template.Items.OffhandGrade);
        SetEquipItemTemplate(npc, template.Items.Ranged, EquipmentItemSlot.Ranged, template.Items.RangedGrade);
        SetEquipItemTemplate(npc, template.Items.Musical, EquipmentItemSlot.Musical, template.Items.MusicalGrade);
        SetEquipItemTemplate(npc, template.Items.Backpack, EquipmentItemSlot.Backpack, template.Items.BackpackGrade);
        SetEquipItemTemplate(npc, template.Items.Cosplay, EquipmentItemSlot.Cosplay, template.Items.CosplayGrade);
        SetEquipItemTemplate(npc, template.Items.Stabilizer, EquipmentItemSlot.Stabilizer, template.Items.StabilizerGrade);

        for (var i = 0; i < 7; i++)
        {
            var slot = (EquipmentItemSlot)(i + 19);
            var bodyItem = template.BodyItems[i];

            // ApplyExplicitCustomBodyParts already resolved each requested part
            // against item_body_parts for this exact model. Use that validated
            // result for Hair as well; using template.HairId directly bypassed
            // the fallback and could attach hair authored for another skeleton.
            SetEquipItemTemplate(npc, bodyItem.ItemId, slot, 0, bodyItem.NpcOnly);
        }

        if (template.WingItem.ItemId != 0)
            SetEquipItemTemplate(npc, template.WingItem.ItemId, EquipmentItemSlot.ProtocolSlot34, 0, template.WingItem.NpcOnly);

        foreach (var buffId in template.Buffs)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(buffId);
            if (buff == null)
            {
                Logger.Warn("BuffId {0} for npc {1} not found", buffId, npc.TemplateId);
                continue;
            }

            var obj = new SkillCasterUnit(npc.ObjId);
            buff.Apply(npc, obj, npc, null, null, new EffectSource(), null, DateTime.UtcNow);
        }

        foreach (var npcPassiveBuff in template.PassiveBuffs)
        {
            var passive = new PassiveBuff() { Template = npcPassiveBuff.PassiveBuff };
            passive.Apply(npc);
        }

        foreach (var bonusTemplate in template.Bonuses)
        {
            var bonus = new Bonus();
            bonus.Template = bonusTemplate;
            bonus.Value = bonusTemplate.Value; // TODO using LinearLevelBonus
            npc.AddBonus(0, bonus);
        }

        npc.Hp = npc.MaxHp;
        npc.Mp = npc.MaxMp;

        if (npc.Template.AiFileId > 0)
        {
            var ai = AIUtils.GetAiByType((AiParamType)npc.Template.AiFileId, npc);
            if (ai == null)
                return npc;

            npc.Ai = ai;
            AIManager.Instance.AddAi(ai);
            npc.Ai.Start();
        }

        return npc;
    }

    public void Load()
    {
        if (_loaded)
            return;

        _templates = new Dictionary<uint, NpcTemplate>();
        _totalCharacterCustoms = new Dictionary<uint, TotalCharacterCustom>();
        _itemBodyParts = new Dictionary<uint, Dictionary<uint, List<BodyPartTemplate>>>();
        _defaultFaceItemsByModel = new Dictionary<uint, uint>();
        _defaultBodyItemsByModel = new Dictionary<uint, BodyPartTemplate>();
        _armorVisualVariants = new Dictionary<(uint ItemId, uint ModelId), ArmorVisualVariants>();
        _skinColorModels = new Dictionary<uint, uint>();
        _bodyNormalMapModels = new Dictionary<uint, uint>();
        _faceDiffuseMapModels = new Dictionary<uint, uint>();
        _faceNormalMapModels = new Dictionary<uint, uint>();
        _faceEyelashMapModels = new Dictionary<uint, uint>();
        _faceDecalModels = new Dictionary<uint, uint>();

        Logger.Info("Loading npc templates...");
        using (var connection = SQLite.CreateTargetClientConnection())
        {
            using (var command = connection.CreateCommand())
            {

                // Pre-Load customs
                command.CommandText = "SELECT * FROM total_character_customs";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var custom = new TotalCharacterCustom();
                        custom.Id = reader.GetUInt32("id", 0);
                        custom.ModelId = reader.GetUInt32("model_id", 0);
                        custom.Name = string.Empty; // Client 1.8 has no localized/server name column.
                        custom.NpcOnly = reader.GetBoolean("npcOnly", true);
                        custom.HairId = reader.GetUInt32("hair_id", 0);
                        custom.HornId = reader.GetUInt32("horn_id", 0);
                        custom.FaceId = reader.GetUInt32("face_id", 0);
                        custom.BodyId = reader.GetUInt32("body_id", 0);
                        custom.TailId = reader.GetUInt32("tail_id", 0);
                        custom.WingId = reader.GetUInt32("wing_id", 0);
                        custom.WingColor = reader.GetUInt32("wing_color", 0);
                        custom.WingScale = reader.GetByte("wing_scale", 100);
                        custom.WingOffsetX = checked((sbyte)reader.GetInt32("wing_offset_x", 0));
                        custom.WingOffsetY = checked((sbyte)reader.GetInt32("wing_offset_y", 0));
                        custom.WingOffsetZ = checked((sbyte)reader.GetInt32("wing_offset_z", 0));
                        custom.BodyNormalMapId = reader.GetUInt32("body_normal_map_id", 0);
                        custom.BodyNormalMapWeight = reader.GetFloat("body_normal_map_weight", 0f);
                        custom.DefaultHairColor = reader.GetUInt32("default_hair_color", 0);
                        custom.HairColorId = reader.GetUInt32("hair_color_id", 0);
                        custom.HornColorId = reader.GetUInt32("horn_color_id", 0);
                        custom.SkinColorId = reader.GetUInt32("skin_color_id", 0);
                        custom.TwoToneFirstWidth = reader.GetFloat("two_tone_first_width", 0f);
                        custom.TwoToneHairColor = reader.GetUInt32("two_tone_hair_color", 0);
                        custom.TwoToneSecondWidth = reader.GetFloat("two_tone_second_width", 0f);
                        custom.FaceMovableDecalAssetId = reader.GetUInt32("face_movable_decal_asset_id", 0);
                        custom.FaceMovableDecalScale = reader.GetFloat("face_movable_decal_scale", 0f);
                        custom.FaceMovableDecalRotate = reader.GetFloat("face_movable_decal_rotate", 0f);
                        custom.FaceMovableDecalMoveX = checked((short)reader.GetInt32("face_movable_decal_move_x", 0));
                        custom.FaceMovableDecalMoveY = checked((short)reader.GetInt32("face_movable_decal_move_y", 0));
                        custom.FaceFixedDecalAsset0Id = reader.GetUInt32("face_fixed_decal_asset_0_id", 0);
                        custom.FaceFixedDecalAsset1Id = reader.GetUInt32("face_fixed_decal_asset_1_id", 0);
                        custom.FaceFixedDecalAsset2Id = reader.GetUInt32("face_fixed_decal_asset_2_id", 0);
                        custom.FaceFixedDecalAsset3Id = reader.GetUInt32("face_fixed_decal_asset_3_id", 0);
                        custom.FaceFixedDecalAsset4Id = reader.GetUInt32("face_fixed_decal_asset_4_id", 0);
                        custom.FaceFixedDecalAsset5Id = reader.GetUInt32("face_fixed_decal_asset_5_id", 0);
                        custom.FaceDiffuseMapId = reader.GetUInt32("face_diffuse_map_id", 0);
                        custom.FaceNormalMapId = reader.GetUInt32("face_normal_map_id", 0);
                        custom.FaceEyelashMapId = reader.GetUInt32("face_eyelash_map_id", 0);
                        custom.LipColor = reader.GetUInt32("lip_color", 0);
                        custom.LeftPupilColor = reader.GetUInt32("left_pupil_color", 0);
                        custom.RightPupilColor = reader.GetUInt32("right_pupil_color", 0);
                        custom.EyebrowColor = reader.GetUInt32("eyebrow_color", 0);
                        // The database stores only the raw 128-byte morph payload.
                        // Normalize malformed rows here; UnitCustomModelParams adds the
                        // target protocol's required ushort length prefix on the wire.
                        if (!reader.IsDBNull("modifier") && reader.GetValue("modifier") is byte[] modifierBlob)
                        {
                            if (modifierBlob.Length == 128)
                                custom.Modifier = modifierBlob;
                            else
                            {
                                custom.Modifier = new byte[128];
                                Array.Copy(modifierBlob, custom.Modifier, Math.Min(modifierBlob.Length, custom.Modifier.Length));
                                Logger.Warn(
                                    "Normalized total_character_customs modifier: id={0}, sourceLength={1}, targetLength=128",
                                    custom.Id, modifierBlob.Length);
                            }
                        }
                        custom.OwnerTypeId = reader.GetUInt32("owner_type_id", 0);
                        custom.FaceMovableDecalWeight = reader.GetFloat("face_movable_decal_weight", 0f);
                        custom.FaceFixedDecalAsset0Weight = reader.GetFloat("face_fixed_decal_asset_0_weight", 0f);
                        custom.FaceFixedDecalAsset1Weight = reader.GetFloat("face_fixed_decal_asset_1_weight", 0f);
                        custom.FaceFixedDecalAsset2Weight = reader.GetFloat("face_fixed_decal_asset_2_weight", 0f);
                        custom.FaceFixedDecalAsset3Weight = reader.GetFloat("face_fixed_decal_asset_3_weight", 0f);
                        custom.FaceFixedDecalAsset4Weight = reader.GetFloat("face_fixed_decal_asset_4_weight", 0f);
                        custom.FaceFixedDecalAsset5Weight = reader.GetFloat("face_fixed_decal_asset_5_weight", 0f);
                        custom.FaceNormalMapWeight = reader.GetFloat("face_normal_map_weight", 0f);
                        custom.DecoColor = reader.GetUInt32("deco_color", 0);

                        _totalCharacterCustoms.Add(custom.Id, custom);
                    }
                }

                // Create a cached reference list by Model ID

                LoadModelBoundAssetIndex(connection, "skin_colors", _skinColorModels);
                LoadModelBoundAssetIndex(connection, "body_normal_maps", _bodyNormalMapModels);
                LoadModelBoundAssetIndex(connection, "face_diffuse_maps", _faceDiffuseMapModels);
                LoadModelBoundAssetIndex(connection, "face_normal_maps", _faceNormalMapModels);
                LoadModelBoundAssetIndex(connection, "face_eyelash_maps", _faceEyelashMapModels);
                LoadModelBoundAssetIndex(connection, "face_decal_assets", _faceDecalModels);

                // Target-client character definitions are the authoritative
                // default face for each humanoid model. NPC total customs use
                // face_id=0 extensively to reference this value implicitly.
                command.CommandText = "SELECT model_id, face_item_id FROM characters WHERE face_item_id IS NOT NULL AND face_item_id <> 0";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                        _defaultFaceItemsByModel[reader.GetUInt32("model_id")] = reader.GetUInt32("face_item_id");
                }

                // Firran used to be steered off the faces the characters table names for them
                // - 20117 and 20118 - onto the lowest-numbered item_body_parts row carrying a
                // ferre face mask, which is 563 for males and 408 for females. Those two rows
                // define no eyes at all:
                //
                //   563 / 408:      left_eye_x/y/width/height = 0, right_eye_* = 0, odd_eye 0
                //   20117 / 20118:  left_eye_x 412, width 100, height 100, odd_eye 1
                //
                // With a face carrying no eye geometry, Firran cannot look like Firran, which
                // is what the wrong-eyes reports were. So the characters table is left as the
                // authoritative default here, for every humanoid model including theirs.
                //
                // The full Face morph and the face body-part must refer to the same model.
                // Keeping characters.face_item_id as the model default gives the modifier blob
                // the exact eye geometry it was authored against; substituting the first row by
                // numeric order recreates the duplicate-head and missing-eye failures.
                //
                // The 8141/8157 pair remains a trap in this table: same slot, ferre model,
                // nuian face mask. Any rule that reintroduces a search here must filter on the
                // mask, not just on the model.

                command.CommandText = @"
                    SELECT ibp.*, COALESCE(ia.path, '') AS asset_path
                    FROM item_body_parts ibp
                    LEFT JOIN item_assets ia ON ia.id = ibp.asset_id
                    ORDER BY ibp.model_id, ibp.slot_type_id, ibp.item_id";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    // Pre-Load body parts
                    while (reader.Read())
                    {
                        var bp = new BodyPartTemplate();
                        var bodyParts = new List<BodyPartTemplate>();
                        var slotBodyParts = new Dictionary<uint, List<BodyPartTemplate>>();

                        bp.ItemId = reader.GetUInt32("item_id", 0);
                        bp.ModelId = reader.GetUInt32("model_id", 0);
                        bp.NpcOnly = reader.GetBoolean("npc_only", true);
                        bp.SlotTypeId = reader.GetUInt32("slot_type_id", 0);
                        bp.AssetPath = reader.GetString("asset_path", string.Empty);
                        bodyParts.Add(bp);

                        if (bp.SlotTypeId == (uint)EquipmentItemSlotType.Body)
                        {
                            if (!_defaultBodyItemsByModel.TryGetValue(bp.ModelId, out var currentBody) ||
                                (!IsCanonicalNudeBodyAsset(currentBody.AssetPath) &&
                                 IsCanonicalNudeBodyAsset(bp.AssetPath)))
                            {
                                _defaultBodyItemsByModel[bp.ModelId] = bp;
                            }
                        }

                        if (!slotBodyParts.ContainsKey(bp.SlotTypeId))
                        {
                            slotBodyParts.Add(bp.SlotTypeId, bodyParts);
                        }
                        else
                        {
                            slotBodyParts[bp.SlotTypeId].Add(bp);
                        }

                        if (!_itemBodyParts.ContainsKey(bp.ModelId))
                        {
                            _itemBodyParts.Add(bp.ModelId, slotBodyParts);
                        }
                        else
                        {
                            if (!_itemBodyParts[bp.ModelId].ContainsKey(bp.SlotTypeId))
                            {
                                _itemBodyParts[bp.ModelId].Add(bp.SlotTypeId, bodyParts);
                            }
                            else
                            {
                                _itemBodyParts[bp.ModelId][bp.SlotTypeId].Add(bp);
                            }
                        }
                    }
                }

                // Cache both armor visual variants by item and model. The target
                // client chooses item_armors.asset_id for cosplay_visual=0 and
                // asset2_id for cosplay_visual=1. This graph is client data, not
                // a server-invented equipment lookup.
                command.CommandText = @"
                    SELECT ia.item_id, a.model_id, a.path, 0 AS visual_variant
                    FROM item_armors ia
                    JOIN item_armor_assets links ON links.armor_asset_id = ia.asset_id
                    JOIN item_assets a ON a.id = links.asset_id
                    UNION ALL
                    SELECT ia.item_id, a.model_id, a.path, 1 AS visual_variant
                    FROM item_armors ia
                    JOIN item_armor_assets links ON links.armor_asset_id = ia.asset2_id
                    JOIN item_assets a ON a.id = links.asset_id
                    WHERE ia.asset2_id IS NOT NULL AND ia.asset2_id <> 0";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var itemId = reader.GetUInt32("item_id", 0);
                        var modelId = reader.GetUInt32("model_id", 0);
                        var path = reader.GetString("path", string.Empty);
                        var visualVariant = reader.GetByte("visual_variant", 0);
                        var key = (itemId, modelId);
                        if (!_armorVisualVariants.TryGetValue(key, out var variants))
                        {
                            variants = new ArmorVisualVariants();
                            _armorVisualVariants.Add(key, variants);
                        }

                        if (visualVariant == 0)
                        {
                            if (string.IsNullOrWhiteSpace(variants.PrimaryPath))
                                variants.PrimaryPath = path;
                        }
                        else
                        {
                            var currentIsHood = Path.GetFileNameWithoutExtension(variants.SecondaryPath)
                                .EndsWith("_h", StringComparison.OrdinalIgnoreCase);
                            var candidateIsHood = Path.GetFileNameWithoutExtension(path)
                                .EndsWith("_h", StringComparison.OrdinalIgnoreCase);
                            if (string.IsNullOrWhiteSpace(variants.SecondaryPath) ||
                                (!currentIsHood && candidateIsHood))
                                variants.SecondaryPath = path;
                        }
                    }
                }

                // NPC rows with total_custom_id=0 intentionally use their model's
                // character default. Do not synthesize a custom from unrelated NPC rows:
                // visible distinctions then come from the DB-authored Head/Cosplay items.

                command.CommandText = "SELECT * from npcs";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    var modelIdOrdinal = sqliteDataReader.GetOrdinal("model_id");
                    var charRaceIdOrdinal = sqliteDataReader.GetOrdinal("char_race_id");
                    var gradeIdOrdinal = sqliteDataReader.GetOrdinal("npc_grade_id");
                    var kindIdOrdinal = sqliteDataReader.GetOrdinal("npc_kind_id");
                    var levelOrdinal = sqliteDataReader.GetOrdinal("level");
                    var templateIdOrdinal = sqliteDataReader.GetOrdinal("npc_template_id");
                    var factionIdOrdinal = sqliteDataReader.GetOrdinal("faction_id");
                    var skippedIncompleteNpcRows = 0;

                    while (reader.Read())
                    {
                        // The 1.8 client database contains incomplete service/placeholder rows in npcs.
                        // They are not spawnable NPC templates and intentionally keep core fields as NULL.
                        // Preserve the client schema: skip those rows instead of inventing default NPC data.
                        if (sqliteDataReader.IsDBNull(modelIdOrdinal) ||
                            sqliteDataReader.IsDBNull(charRaceIdOrdinal) ||
                            sqliteDataReader.IsDBNull(gradeIdOrdinal) ||
                            sqliteDataReader.IsDBNull(kindIdOrdinal) ||
                            sqliteDataReader.IsDBNull(levelOrdinal) ||
                            sqliteDataReader.IsDBNull(templateIdOrdinal) ||
                            sqliteDataReader.IsDBNull(factionIdOrdinal))
                        {
                            skippedIncompleteNpcRows++;
                            continue;
                        }

                        var template = new NpcTemplate();
                        template.Id = reader.GetUInt32("id", 0);
                        var localizedName = LocalizationManager.Instance.Get("npcs", "name", template.Id);
                        template.Name = string.IsNullOrWhiteSpace(localizedName)
                            ? reader.GetString("name", string.Empty)
                            : localizedName;
                        template.CharRaceId = reader.GetInt32("char_race_id", 0);
                        template.NpcGradeId = (NpcGradeType)reader.GetByte("npc_grade_id", 0);
                        template.NpcKindId = (NpcKindType)reader.GetByte("npc_kind_id", 0);
                        var rawLevel = reader.GetInt32("level", 0);
                        if (rawLevel > byte.MaxValue)
                        {
                            var normalizedLevel = rawLevel % 100;
                            Logger.Warn("Normalizing invalid NPC {0} level {1} to {2}", template.Id, rawLevel, normalizedLevel);
                            rawLevel = normalizedLevel;
                        }

                        template.Level = checked((byte)rawLevel);
                        template.NpcTemplateId = (NpcTemplateType)reader.GetByte("npc_template_id", 0);
                        template.ModelId = reader.GetUInt32("model_id", 0);
                        template.FactionId = reader.GetUInt32("faction_id", 0);
                        var rawHeirLevel = reader.GetInt32("heir_level", 0);
                        if (rawHeirLevel is < byte.MinValue or > byte.MaxValue)
                        {
                            Logger.Warn("Invalid NPC {0} heir level {1}; using 0", template.Id, rawHeirLevel);
                            rawHeirLevel = 0;
                        }

                        template.HeirLevel = (uint)rawHeirLevel;
                        template.SkillTrainer = reader.GetBoolean("skill_trainer", true);
                        template.AiFileId = reader.GetInt32("ai_file_id", 0);
                        template.Merchant = reader.GetBoolean("merchant", true);
                        template.NpcNicknameId = reader.GetInt32("npc_nickname_id", 0);
                        template.Auctioneer = reader.GetBoolean("auctioneer", true);
                        template.ShowNameTag = reader.GetBoolean("show_name_tag", true);
                        template.VisibleToCreatorOnly = reader.GetBoolean("visible_to_creator_only", true);
                        template.NoExp = reader.GetBoolean("no_exp", true);
                        template.PetItemId = reader.GetInt32("pet_item_id", 0);
                        template.BaseSkillId = reader.GetInt32("base_skill_id", 0);
                        template.TrackFriendship = reader.GetBoolean("track_friendship", true);
                        template.Priest = reader.GetBoolean("priest", true);
                        //template.NpcTedencyId = reader.GetInt32("npc_tendency_id", 0); // there is no such field in the database for version 3.0.3.0
                        template.Blacksmith = reader.GetBoolean("blacksmith", true);
                        template.Teleporter = reader.GetBoolean("teleporter", true);
                        template.Opacity = reader.GetFloat("opacity", 0f);
                        template.AbilityChanger = reader.GetBoolean("ability_changer", true);
                        template.Scale = reader.GetFloat("scale", 0f);
                        template.SightRangeScale = reader.GetFloat("sight_range_scale", 0f);
                        template.SightFovScale = reader.GetFloat("sight_fov_scale", 0f);
                        //template.MilestoneId = reader.GetInt32("milestone_id", 0); // there is no such field in the database for version 3.0.3.0
                        template.AttackStartRangeScale = reader.GetFloat("attack_start_range_scale", 0f);
                        template.Aggression = reader.GetBoolean("aggression", true);
                        template.ExpMultiplier = reader.GetFloat("exp_multiplier", 0f);
                        template.ExpAdder = reader.GetInt32("exp_adder", 0);
                        template.Stabler = reader.GetBoolean("stabler", true);
                        template.AcceptAggroLink = reader.GetBoolean("accept_aggro_link", true);
                        //template.RecrutingBattlefieldId = reader.GetInt32("recruiting_battle_field_id", 0); // there is no such field in the database for version 3.0.3.0
                        template.ReturnDistance = reader.GetFloat("return_distance", 0f);
                        template.NpcAiParamId = reader.GetInt32("npc_ai_param_id", 0);
                        template.NonPushableByActor = reader.GetBoolean("non_pushable_by_actor", true);
                        template.Banker = reader.GetBoolean("banker", true);
                        template.AggroLinkSpecialRuleId = reader.GetInt32("aggro_link_special_rule_id", 0);
                        template.AggroLinkHelpDist = reader.GetFloat("aggro_link_help_dist", 0f);
                        template.AggroLinkSightCheck = reader.GetBoolean("aggro_link_sight_check", true);
                        template.Expedition = reader.GetBoolean("expedition", true);
                        template.HonorPoint = reader.GetInt32("honor_point", 0);
                        template.Trader = reader.GetBoolean("trader", true);
                        template.AggroLinkSpecialGuard = reader.GetBoolean("aggro_link_special_guard", true);
                        template.AggroLinkSpecialIgnoreNpcAttacker = reader.GetBoolean("aggro_link_special_ignore_npc_attacker", true);
                        template.AbsoluteReturnDistance = reader.GetFloat("absolute_return_distance", 0f);
                        template.Repairman = reader.GetBoolean("repairman", true);
                        template.ActivateAiAlways = reader.GetBoolean("activate_ai_always", true);
                        template.Specialty = reader.GetBoolean("specialty", true);
                        template.SpecialtyCoinId = reader.GetUInt32("specialty_coin_id", 0);
                        template.UseRangeMod = reader.GetBoolean("use_range_mod", true);
                        template.NpcPostureSetId = reader.GetInt32("npc_posture_set_id", 0);
                        template.MateEquipSlotPackId = reader.GetInt32("mate_equip_slot_pack_id", 0);
                        template.MateKindId = reader.GetInt32("mate_kind_id", 0);
                        template.EngageCombatGiveQuestId = reader.GetUInt32("engage_combat_give_quest_id", 0);
                        template.NoApplyTotalCustom = reader.GetBoolean("no_apply_total_custom", true);
                        template.BaseSkillStrafe = reader.GetBoolean("base_skill_strafe", true);
                        template.BaseSkillDelay = reader.GetFloat("base_skill_delay", 0f);
                        template.NpcInteractionSetId = reader.GetInt32("npc_interaction_set_id", 0);
                        template.UseAbuserList = reader.GetBoolean("use_abuser_list", true);
                        template.ReturnWhenEnterHousingArea = reader.GetBoolean("return_when_enter_housing_area", true);
                        template.LookConverter = reader.GetBoolean("look_converter", true);
                        template.UseDDCMSMountSkill = reader.GetBoolean("use_ddcms_mount_skill", true);
                        template.CrowdEffect = reader.GetBoolean("crowd_effect", true);

                        //var bodyPack = reader.GetInt32("equip_bodies_id", 0); // there is no such field in the database for version 3.0.3.0
                        var clothPack = reader.GetInt32("equip_cloths_id", 0);
                        var weaponPack = reader.GetInt32("equip_weapons_id", 0);
                        template.EquipClothsId = clothPack > 0 ? (uint)clothPack : 0u;
                        template.EquipWeaponsId = weaponPack > 0 ? (uint)weaponPack : 0u;
                        template.TotalCustomId = reader.GetUInt32("total_custom_id", 0);
                        using (var command2 = connection.CreateCommand())
                        {
                            command2.CommandText = "SELECT char_race_id, char_gender_id, default_custom_id FROM characters WHERE model_id = @model_id";
                            command2.Parameters.AddWithValue("model_id", template.ModelId);
                            command2.Prepare();
                            using (var sqliteReader2 = command2.ExecuteReader())
                            using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
                            {
                                if (reader2.Read())
                                {
                                    template.Race = reader2.GetByte("char_race_id", 0);
                                    template.Gender = reader2.GetByte("char_gender_id", 0);

                                    // The look this model wears when nothing else is chosen for it,
                                    // and whether the model is a person at all: a model with no row
                                    // here is a beast or a machine, and has no face to describe.
                                    template.IsHumanoidModel = true;
                                    template.DefaultCustomId = reader2.GetUInt32("default_custom_id", 0);
                                }
                            }
                        }

                        _templates.Add(template.Id, template);

                        if (clothPack > 0)
                        {
                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText = "SELECT * FROM equip_pack_cloths WHERE id=@id";
                                command2.Parameters.AddWithValue("id", clothPack);
                                command2.Prepare();
                                using (var sqliteReader2 = command2.ExecuteReader())
                                using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
                                {
                                    while (reader2.Read())
                                    {
                                        template.Items.Headgear = reader2.GetUInt32("headgear_id", 0);
                                        template.Items.HeadgearGrade = reader2.GetByte("headgear_grade_id", 0);
                                        template.Items.Necklace = reader2.GetUInt32("necklace_id", 0);
                                        template.Items.NecklaceGrade = reader2.GetByte("necklace_grade_id", 0);
                                        template.Items.Shirt = reader2.GetUInt32("shirt_id", 0);
                                        template.Items.ShirtGrade = reader2.GetByte("shirt_grade_id", 0);
                                        template.Items.Belt = reader2.GetUInt32("belt_id", 0);
                                        template.Items.BeltGrade = reader2.GetByte("belt_grade_id", 0);
                                        template.Items.Pants = reader2.GetUInt32("pants_id", 0);
                                        template.Items.PantsGrade = reader2.GetByte("pants_grade_id", 0);
                                        template.Items.Gloves = reader2.GetUInt32("glove_id", 0);
                                        template.Items.GlovesGrade = reader2.GetByte("glove_grade_id", 0);
                                        template.Items.Shoes = reader2.GetUInt32("shoes_id", 0);
                                        template.Items.ShoesGrade = reader2.GetByte("shoes_grade_id", 0);
                                        template.Items.Bracelet = reader2.GetUInt32("bracelet_id", 0);
                                        template.Items.BraceletGrade = reader2.GetByte("bracelet_grade_id", 0);
                                        template.Items.Back = reader2.GetUInt32("back_id", 0);
                                        template.Items.BackGrade = reader2.GetByte("back_grade_id", 0);
                                        template.Items.Backpack = reader2.GetUInt32("backpack_id", 0);
                                        template.Items.BackpackGrade = reader2.GetByte("backpack_grade_id", 0);
                                        template.Items.Cosplay = reader2.GetUInt32("cosplay_id", 0);
                                        template.Items.CosplayGrade = reader2.GetByte("cosplay_grade_id", 0);
                                        template.Items.Undershirts = reader2.GetUInt32("undershirt_id", 0);
                                        template.Items.UndershirtsGrade = reader2.GetByte("undershirt_grade_id", 0);
                                        template.Items.Underpants = reader2.GetUInt32("underpants_id", 0);
                                        template.Items.UnderpantsGrade = reader2.GetByte("underpants_grade_id", 0);
                                        template.Items.Stabilizer = reader2.GetUInt32("stabilizer_id", 0);
                                        template.Items.StabilizerGrade = reader2.GetByte("stabilizer_grade_id", 0);
                                    }
                                }
                            }
                        }

                        if (weaponPack > 0)
                        {
                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText = "SELECT * FROM equip_pack_weapons WHERE id=@id";
                                command2.Parameters.AddWithValue("id", weaponPack);
                                command2.Prepare();
                                using (var sqliteReader2 = command2.ExecuteReader())
                                using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
                                {
                                    while (reader2.Read())
                                    {
                                        template.Items.Mainhand = reader2.GetUInt32("mainhand_id", 0);
                                        template.Items.MainhandGrade = reader2.GetByte("mainhand_grade_id", 0);
                                        template.Items.Offhand = reader2.GetUInt32("offhand_id", 0);
                                        template.Items.OffhandGrade = reader2.GetByte("offhand_grade_id", 0);
                                        template.Items.Ranged = reader2.GetUInt32("ranged_id", 0);
                                        template.Items.RangedGrade = reader2.GetByte("ranged_grade_id", 0);
                                        template.Items.Musical = reader2.GetUInt32("musical_id", 0);
                                        template.Items.MusicalGrade = reader2.GetByte("musical_grade_id", 0);
                                    }
                                }
                            }
                        }

                        ResolveNpcCosplayVisual(template);

                        // Resolve appearance only inside the NPC model. A direct custom from a
                        // different model is not harmless: its face maps, decals and body parts
                        // address another skeleton and produce TEST materials, duplicate fallback
                        // heads, or missing geometry.
                        var selectedCustom = ResolveCompatibleCustom(
                            template, out var resolvedCustomId, out var customResolution);
                        template.ResolvedCustomId = resolvedCustomId;
                        template.ResolvedCustomModelId = selectedCustom?.ModelId ?? 0;
                        template.CustomResolution = customResolution;
                        template.HairId = selectedCustom?.HairId ?? 0;
                        template.HornId = selectedCustom?.HornId ?? 0;

                        if (selectedCustom != null)
                        {
                            var tc = selectedCustom;
                            _totalCharacterCustoms.TryGetValue(template.DefaultCustomId, out var modelDefaultCustom);
                            if (modelDefaultCustom?.ModelId != template.ModelId)
                                modelDefaultCustom = null;

                            var skinColorId = ResolveSkinColorId(template, tc, modelDefaultCustom);
                            var bodyNormalMapId = ResolveOptionalModelBoundAssetId(
                                template, "body_normal_map_id", tc.BodyNormalMapId, _bodyNormalMapModels);
                            var faceDiffuseMapId = ResolveOptionalModelBoundAssetId(
                                template, "face_diffuse_map_id", tc.FaceDiffuseMapId, _faceDiffuseMapModels);
                            var faceNormalMapId = ResolveOptionalModelBoundAssetId(
                                template, "face_normal_map_id", tc.FaceNormalMapId, _faceNormalMapModels);
                            var faceEyelashMapId = ResolveOptionalModelBoundAssetId(
                                template, "face_eyelash_map_id", tc.FaceEyelashMapId, _faceEyelashMapModels);
                            var movableDecalId = ResolveOptionalModelBoundAssetId(
                                template, "face_movable_decal_asset_id", tc.FaceMovableDecalAssetId, _faceDecalModels);
                            var fixedDecalIds = new[]
                            {
                                ResolveOptionalModelBoundAssetId(template, "face_fixed_decal_asset_0_id", tc.FaceFixedDecalAsset0Id, _faceDecalModels),
                                ResolveOptionalModelBoundAssetId(template, "face_fixed_decal_asset_1_id", tc.FaceFixedDecalAsset1Id, _faceDecalModels),
                                ResolveOptionalModelBoundAssetId(template, "face_fixed_decal_asset_2_id", tc.FaceFixedDecalAsset2Id, _faceDecalModels),
                                ResolveOptionalModelBoundAssetId(template, "face_fixed_decal_asset_3_id", tc.FaceFixedDecalAsset3Id, _faceDecalModels),
                                ResolveOptionalModelBoundAssetId(template, "face_fixed_decal_asset_4_id", tc.FaceFixedDecalAsset4Id, _faceDecalModels),
                                ResolveOptionalModelBoundAssetId(template, "face_fixed_decal_asset_5_id", tc.FaceFixedDecalAsset5Id, _faceDecalModels)
                            };

                            template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Face);
                            template.ModelParams
                                .SetBodyDiffuseOrModelDefaultId(0) // target +0x0C is not tc.ModelId; target custom copier leaves it at default
                                .SetBodyNormalMapId(bodyNormalMapId)
                                .SetBodyNormalMapWeight(tc.BodyNormalMapWeight)
                                .SetDefaultHairColor(tc.DefaultHairColor)
                                .SetHairColorId(tc.HairColorId)
                                .SetHornColorId(tc.HornColorId)
                                .SetSkinColorId(skinColorId)
                                .SetTwoToneFirstWidth(tc.TwoToneFirstWidth)
                                .SetTwoToneHair(tc.TwoToneHairColor)
                                .SetTwoToneSecondWidth(tc.TwoToneSecondWidth);

                            template.ModelParams.Face.MovableDecalAssetId = movableDecalId;
                            template.ModelParams.Face.MovableDecalScale = NormalizeMovableDecalScale(movableDecalId, tc.FaceMovableDecalScale);
                            template.ModelParams.Face.MovableDecalRotate = tc.FaceMovableDecalRotate;
                            template.ModelParams.Face.MovableDecalMoveX = tc.FaceMovableDecalMoveX;
                            template.ModelParams.Face.MovableDecalMoveY = tc.FaceMovableDecalMoveY;

                            template.ModelParams.Face.SetFixedDecalAsset(0, fixedDecalIds[0], NormalizeDecalWeight(fixedDecalIds[0], tc.FaceFixedDecalAsset0Weight));
                            template.ModelParams.Face.SetFixedDecalAsset(1, fixedDecalIds[1], NormalizeDecalWeight(fixedDecalIds[1], tc.FaceFixedDecalAsset1Weight));
                            template.ModelParams.Face.SetFixedDecalAsset(2, fixedDecalIds[2], NormalizeDecalWeight(fixedDecalIds[2], tc.FaceFixedDecalAsset2Weight));
                            template.ModelParams.Face.SetFixedDecalAsset(3, fixedDecalIds[3], NormalizeDecalWeight(fixedDecalIds[3], tc.FaceFixedDecalAsset3Weight));
                            template.ModelParams.Face.SetFixedDecalAsset(4, fixedDecalIds[4], NormalizeDecalWeight(fixedDecalIds[4], tc.FaceFixedDecalAsset4Weight));
                            template.ModelParams.Face.SetFixedDecalAsset(5, fixedDecalIds[5], NormalizeDecalWeight(fixedDecalIds[5], tc.FaceFixedDecalAsset5Weight));

                            template.ModelParams.Face.DiffuseMapId = faceDiffuseMapId;
                            template.ModelParams.Face.NormalMapId = faceNormalMapId;
                            template.ModelParams.Face.EyelashMapId = faceEyelashMapId;
                            template.ModelParams.Face.LipColor = tc.LipColor;
                            template.ModelParams.Face.LeftPupilColor = tc.LeftPupilColor;
                            template.ModelParams.Face.RightPupilColor = tc.RightPupilColor;
                            template.ModelParams.Face.EyebrowColor = tc.EyebrowColor;
                            template.ModelParams.Face.MovableDecalWeight = NormalizeDecalWeight(movableDecalId, tc.FaceMovableDecalWeight);
                            template.ModelParams.Face.NormalMapWeight = tc.FaceNormalMapWeight;
                            template.ModelParams.Face.DecoColor = tc.DecoColor;
                            template.ModelParams.Face.Modifier = tc.Modifier;
                            template.ModelParams.Face.WingColor = tc.WingColor;
                            template.ModelParams.Face.WingScale = tc.WingScale;
                            template.ModelParams.Face.WingOffsetX = tc.WingOffsetX;
                            template.ModelParams.Face.WingOffsetY = tc.WingOffsetY;
                            template.ModelParams.Face.WingOffsetZ = tc.WingOffsetZ;

                            // Preserve the localized name loaded from npcs/localized_texts.
                            // total_character_customs has no server/runtime name in 1.8.1.0.
                            template.NpcOnly = tc.NpcOnly;
                            template.OwnerTypeId = tc.OwnerTypeId;
                        }
                        else
                        {
                            // ext=0: no foreign/default material override. Model-bound face/body
                            // parts are still resolved below, so the NPC keeps a valid skeleton.
                            template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.None);
                        }

                        if (template.NpcPostureSetId > 0)
                        {
                            using (var command2 = connection.CreateCommand())
                            {
                                command2.CommandText = "SELECT * FROM npc_postures WHERE npc_posture_set_id=@id";
                                command2.Parameters.AddWithValue("id", template.NpcPostureSetId);
                                command2.Prepare();
                                using (var sqliteReader2 = command2.ExecuteReader())
                                using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
                                {
                                    if (reader2.Read())
                                        template.AnimActionId = reader2.GetUInt32("anim_action_id", 0);
                                }
                            }
                        }

                        ApplyExplicitCustomBodyParts(template, selectedCustom);
                    }

                    if (skippedIncompleteNpcRows > 0)
                        Logger.Info("Skipped {0} incomplete client NPC placeholder rows", skippedIncompleteNpcRows);
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type='Npc'";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var npcId = reader.GetUInt32("owner_id", 0);
                        if (!_templates.ContainsKey(npcId))
                            continue;
                        var npc = _templates[npcId];
                        var template = new BonusTemplate();
                        template.Attribute = (UnitAttribute)reader.GetByte("unit_attribute_id", 0);
                        template.ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id", 0);
                        template.Value = reader.GetInt32("value", 0);
                        template.LinearLevelBonus = reader.GetInt32("linear_level_bonus", 0);
                        npc.Bonuses.Add(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM npc_initial_buffs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("npc_id", 0);
                        var buffId = reader.GetUInt32("buff_id", 0);
                        if (!_templates.ContainsKey(id))
                            continue;
                        var template = _templates[id];
                        template.Buffs.Add(buffId);
                    }
                }
            }

            Logger.Info(
                "Merchant catalogue ready from {0}: {1} NPCs/{2} goods, {3} packs/{4} goods",
                SQLite.TargetClientDatabase,
                VendorGameData.Instance.MerchantCount,
                VendorGameData.Instance.MerchantGoodsCount,
                VendorGameData.Instance.PackCount,
                VendorGameData.Instance.PackGoodsCount);
            Logger.Info($"Loaded {_templates.Count} npc templates");
        }

        NpcGameData.Instance.LoadMemberAndSpawnerTemplateIds();

        _loaded = true;
    }

    public void LoadAiParams()
    {
        foreach (var npc in _templates.Values)
        {
            npc.AiParams = AiGameData.Instance.GetAiParamsForId((uint)npc.NpcAiParamId);
        }
    }

    /// <summary>
    /// Builds a transient runtime identity for an NPC visual item without
    /// consuming the persistent player-item ID allocator.
    /// </summary>
    /// <remarks>
    /// NPC equipment descriptors are never saved to the player items table.
    /// Allocating every spawned NPC part through ItemIdManager exhausted its
    /// current BitSet window and made new-character starter equipment fail with
    /// "Ran out of valid Id's". The target wire field is u64, so combine the
    /// live NPC object id and slot into a stable, non-zero identity that cannot
    /// collide between slots of the same live object.
    /// </remarks>
    private static ulong BuildNpcEquipmentRuntimeId(Npc npc, EquipmentItemSlot slot)
    {
        return ((ulong)npc.ObjId << 32) | ((uint)slot + 1u);
    }

    private static Item CreateClientOnlyNpcEquipmentDescriptor(
        uint templateId,
        byte grade,
        EquipmentItemSlot slot)
    {
        var wireSlot = Protocol1810EquipmentLayout.ToWireSlot((int)slot);
        Item item = Protocol1810EquipmentLayout.IsNpcFullItemWireSlot(wireSlot)
            ? new EquipItem()
            : new Item();

        item.TemplateId = templateId;
        item.Count = 1;
        item.Grade = grade;
        item.CreateTime = DateTime.UtcNow;
        item.UnsecureTime = DateTime.MinValue;
        item.UnpackTime = DateTime.MinValue;
        item.ChargeUseSkillTime = DateTime.MinValue;
        return item;
    }

    private void SetEquipItemTemplate(Npc npc, uint templateId, EquipmentItemSlot slot, byte grade = 0, bool npcOnly = false)
    {
        if (templateId == 0)
            return;

        if (npcOnly && npc.Equipment.GetItemBySlot((int)slot) != null)
            return;

        // ItemManager loads the common item layer plus its subtype tables. A small
        // set of valid client-authored NPC-only equipment rows exists only in the
        // subtype table; Create() therefore returns null even though the target
        // client can resolve and render the template from its complete DB. For NPC
        // spawn serialization, compact slots need template/id/grade only and full
        // cosplay slots need a normal Equipment detail block. Build that transient
        // descriptor instead of silently dropping the visual item.
        var item = ItemManager.Instance.Create(templateId, 1, grade, false);
        var usedClientOnlyDescriptor = false;
        if (item == null)
        {
            item = CreateClientOnlyNpcEquipmentDescriptor(templateId, grade, slot);
            usedClientOnlyDescriptor = true;
        }

        item.Id = BuildNpcEquipmentRuntimeId(npc, slot);
        item.SlotType = SlotType.Equipment;
        item.Slot = (int)slot;

        if (!npc.Equipment.AddOrMoveExistingItem(0, item, (int)slot))
        {
            Logger.Error(
                "Failed to equip NPC visual item: npc={0}, slot={1}, template={2}, grade={3}, clientOnly={4}",
                npc.TemplateId, slot, templateId, grade, usedClientOnlyDescriptor ? 1 : 0);
            return;
        }

        if (usedClientOnlyDescriptor)
        {
            Logger.Warn(
                "Using client-only NPC equipment descriptor: npc={0}, slot={1}, wireSlot={2}, template={3}, grade={4}",
                npc.TemplateId, slot, Protocol1810EquipmentLayout.ToWireSlot((int)slot), templateId, grade);
        }
    }


    public void BindSkillsToTemplate(uint templateId, List<NpcSkill> skills)
    {
        if (!_templates.ContainsKey(templateId))
            return;

        _templates[templateId].BindSkills(skills);
    }
}
