using System;
using System.Collections.Generic;
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
    private HashSet<uint> _itemsWithoutArmorVisual;
    private Dictionary<uint, List<uint>> _tccLookup;
    // you can provide a seed here if you want NPCs to more reliable retain their appearance between reboots, or leave out the seed to get it random every time
    private Random _loadCustomRandom = new Random(330995);
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
        if (template == null)
            return;

        if (!_itemBodyParts.TryGetValue(template.ModelId, out var slots))
            return;

        // A zero face_id means "use the character model's default face", not
        // "use the first item_body_parts row". Row ordering is not semantic
        // and selected test/mannequin heads for several humanoid models.
        var faceItemId = custom?.FaceId ?? 0;
        var hasCharacterDefaults = _defaultFaceItemsByModel.TryGetValue(template.ModelId, out var defaultFaceItemId);
        if (faceItemId == 0 && hasCharacterDefaults)
            faceItemId = defaultFaceItemId;

        ApplyExplicitBodyPart(template, slots, EquipmentItemSlotType.Face,
            faceItemId, useFirstFallback: !hasCharacterDefaults);
        ApplyExplicitBodyPart(template, slots, EquipmentItemSlotType.Hair,
            custom?.HairId ?? template.HairId, useFirstFallback: !hasCharacterDefaults);
        ApplyExplicitBodyPart(template, slots, EquipmentItemSlotType.Horns,
            custom?.HornId ?? 0);
        ApplyExplicitBodyPart(template, slots, EquipmentItemSlotType.Tail,
            custom?.TailId ?? 0);
        ApplyExplicitBodyPart(template, slots, EquipmentItemSlotType.Body,
            custom?.BodyId ?? 0, useFirstFallback: true);
    }

    private void ApplyExplicitBodyPart(NpcTemplate template, Dictionary<uint, List<BodyPartTemplate>> slots,
        EquipmentItemSlotType slot, uint itemId, bool useFirstFallback = false, bool useLastFallback = false)
    {
        var bodyIndex = (int)slot - 23;
        if (bodyIndex < 0 || bodyIndex >= template.BodyItems.Length)
            return;

        template.BodyItems[bodyIndex] = default;
        if (!slots.TryGetValue((uint)slot, out var candidates))
        {
            if (itemId != 0)
                Logger.Warn($"NPC custom slot missing: npc={template.Id}, model={template.ModelId}, slot={slot}, item={itemId}");
            return;
        }

        var match = itemId != 0 ? candidates.FirstOrDefault(x => x.ItemId == itemId) : null;
        if (match == null && useFirstFallback)
            match = candidates.FirstOrDefault(x => !x.NpcOnly) ?? candidates.FirstOrDefault();
        if (match == null && useLastFallback)
            match = candidates.LastOrDefault(x => !x.NpcOnly) ?? candidates.LastOrDefault();

        if (match == null)
        {
            if (itemId != 0)
                Logger.Warn($"NPC custom body part not found: npc={template.Id}, model={template.ModelId}, slot={slot}, item={itemId}");
            return;
        }

        template.BodyItems[bodyIndex] = (match.ItemId, match.NpcOnly);
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

        if (template.TotalCustomId == 0)
        {
            // load random hairstyles
            var templ = LoadCustom(template);
            template.HairId = templ.HairId;
            template.HornId = templ.HornId;
            template.ModelParams = templ.ModelParams;
            template.BodyItems = templ.BodyItems;
        }

        // LoadCustom can replace the resolved appearance for templates without
        // a fixed total_custom_id, so copy it only after that resolution.
        npc.ModelParams = template.NoApplyTotalCustom
            ? new UnitCustomModelParams(UnitCustomModelType.None)
            : template.ModelParams ?? new UnitCustomModelParams(UnitCustomModelType.None);

        SetEquipItemTemplate(npc, template.Items.Headgear, EquipmentItemSlot.Head);
        SetEquipItemTemplate(npc, template.Items.Necklace, EquipmentItemSlot.Neck);
        SetEquipItemTemplate(npc, template.Items.Shirt, EquipmentItemSlot.Chest);
        SetEquipItemTemplate(npc, template.Items.Belt, EquipmentItemSlot.Waist);
        SetEquipItemTemplate(npc, template.Items.Pants, EquipmentItemSlot.Legs);
        SetEquipItemTemplate(npc, template.Items.Gloves, EquipmentItemSlot.Hands);
        SetEquipItemTemplate(npc, template.Items.Shoes, EquipmentItemSlot.Feet);
        SetEquipItemTemplate(npc, template.Items.Bracelet, EquipmentItemSlot.Arms);
        SetEquipItemTemplate(npc, template.Items.Back, EquipmentItemSlot.Back);
        SetEquipItemTemplate(npc, template.Items.Undershirts, EquipmentItemSlot.Undershirt);
        SetEquipItemTemplate(npc, template.Items.Underpants, EquipmentItemSlot.Underpants);
        SetEquipItemTemplate(npc, template.Items.Mainhand, EquipmentItemSlot.Mainhand);
        SetEquipItemTemplate(npc, template.Items.Offhand, EquipmentItemSlot.Offhand);
        SetEquipItemTemplate(npc, template.Items.Ranged, EquipmentItemSlot.Ranged);
        SetEquipItemTemplate(npc, template.Items.Musical, EquipmentItemSlot.Musical);
        SetEquipItemTemplate(npc, template.Items.Cosplay, EquipmentItemSlot.Cosplay);
        SetEquipItemTemplate(npc, template.Items.Stabilizer, EquipmentItemSlot.Stabilizer);

        for (var i = 0; i < 7; i++)
        {
            EquipmentItemSlot slot = (EquipmentItemSlot)(i + 19);
            if ((slot == EquipmentItemSlot.Hair) && (template.ModelParams != null))
                SetEquipItemTemplate(npc, template.HairId, EquipmentItemSlot.Hair);
            else
                SetEquipItemTemplate(npc, template.BodyItems[i].ItemId, slot, 0, template.BodyItems[i].NpcOnly);
        }

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

    private NpcTemplate LoadCustom(NpcTemplate template)
    {
        // This temporary object carries only the resolved appearance. It still
        // must retain the source model id: ApplyExplicitCustomBodyParts indexes
        // item_body_parts by model_id. Leaving it at zero removed Face and Body
        // from every random-custom NPC (total_custom_id == 0), producing black
        // skin, TEST materials and headless models for both genders.
        var _template = new NpcTemplate
        {
            ModelId = template.ModelId,
            CharRaceId = template.CharRaceId,
            Gender = template.Gender
        };
        var totalCustomId = template.TotalCustomId;

        if (totalCustomId != 0 || template.FactionId == 115 || template.FactionId == 116) // 115 - Monstrosity, 116 - Animal
        {
            return template;
        }

        //Logger.Info("Loading random npc {0} custom templates...", template.ModelId);
        var modelParamsId = 0u;
        switch ((Race)template.CharRaceId)
        {
            case Race.None:
            case Race.Nuian: // Nuian male
                modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)10 : (byte)11;
                break;
            case Race.Dwarf: // Dwarf male
                modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)14 : (byte)15;
                break;
            case Race.Elf: // Elf male
                modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)16 : (byte)17;
                break;
            case Race.Hariharan: // Hariharan male
                modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)18 : (byte)19;
                break;
            case Race.Ferre: // Ferre male
                modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)20 : (byte)21;
                break;
            case Race.Warborn: // Warborn male
                modelParamsId = (Gender)template.Gender == Gender.Male ? (byte)24 : (byte)25;
                break;
            case Race.Fairy:
                break;
            case Race.Returned:
                break;
            default:
                break;
        }

        // choose randomly from the client total-custom list. modelParamsId is
        // only assigned to supported humanoid races above, so no foreign
        // ModelManager/ActorModel check is required here.
        if (modelParamsId != 0)
        {
            // Get all possible hair item_ids that match this model
            var hairsForThisModel = new List<uint>();
            foreach (var item in ItemManager.Instance.GetAllItems())
                if ((item is BodyPartTemplate bpt) && (bpt.ModelId == template.ModelId) && (bpt.SlotTypeId == (uint)EquipmentItemSlotType.Hair))
                    hairsForThisModel.Add(bpt.ItemId);

            if (hairsForThisModel.Count > 0)
            {
                // TODO: Slow, but I don't know of a better way to do this atm
                var possibleTotalCustoms = (from tc in _totalCharacterCustoms
                                            where (tc.Value.ModelId == modelParamsId) && (hairsForThisModel.Contains(tc.Value.HairId))
                                            select tc.Value.Id).ToList();

                // If anything in result, pick something random from it
                if (possibleTotalCustoms.Count > 0)
                {
                    var r = _loadCustomRandom.Next(possibleTotalCustoms.Count);
                    totalCustomId = possibleTotalCustoms[r];
                }
                else
                {
                    Logger.Trace($"No compatible TotalCharacterCustoms hair found for NPC: {template.Id}");
                }
            }
        }
        else
        {
            return template;
        }

        TotalCharacterCustom selectedCustom = null;
        if (totalCustomId > 0)
        {
            var tc = _totalCharacterCustoms[totalCustomId];
            selectedCustom = tc;

            _template.HairId = tc.HairId;
            _template.HornId = tc.HornId;

            _template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Face);
            _template.ModelParams
                .SetModelId(tc.ModelId)
                .SetBodyNormalMapId(tc.BodyNormalMapId)
                .SetBodyNormalMapWeight(tc.BodyNormalMapWeight)
                .SetDefaultHairColor(tc.DefaultHairColor)
                .SetHairColorId(tc.HairColorId)
                .SetHornColorId(tc.HornColorId)
                .SetSkinColorId(tc.SkinColorId)
                .SetTwoToneFirstWidth(tc.TwoToneFirstWidth)
                .SetTwoToneHair(tc.TwoToneHairColor)
                .SetTwoToneSecondWidth(tc.TwoToneSecondWidth);

            _template.ModelParams.Face.MovableDecalAssetId = tc.FaceMovableDecalAssetId;
            _template.ModelParams.Face.MovableDecalScale = tc.FaceMovableDecalScale;
            _template.ModelParams.Face.MovableDecalRotate = tc.FaceMovableDecalRotate;
            _template.ModelParams.Face.MovableDecalMoveX = tc.FaceMovableDecalMoveX;
            _template.ModelParams.Face.MovableDecalMoveY = tc.FaceMovableDecalMoveY;

            _template.ModelParams.Face.SetFixedDecalAsset(0, tc.FaceFixedDecalAsset0Id, tc.FaceFixedDecalAsset0Weight);
            _template.ModelParams.Face.SetFixedDecalAsset(1, tc.FaceFixedDecalAsset1Id, tc.FaceFixedDecalAsset1Weight);
            _template.ModelParams.Face.SetFixedDecalAsset(2, tc.FaceFixedDecalAsset2Id, tc.FaceFixedDecalAsset2Weight);
            _template.ModelParams.Face.SetFixedDecalAsset(3, tc.FaceFixedDecalAsset3Id, tc.FaceFixedDecalAsset3Weight);
            _template.ModelParams.Face.SetFixedDecalAsset(4, tc.FaceFixedDecalAsset4Id, tc.FaceFixedDecalAsset4Weight);
            _template.ModelParams.Face.SetFixedDecalAsset(5, tc.FaceFixedDecalAsset5Id, tc.FaceFixedDecalAsset5Weight);

            _template.ModelParams.Face.DiffuseMapId = tc.FaceDiffuseMapId;
            _template.ModelParams.Face.NormalMapId = tc.FaceNormalMapId;
            _template.ModelParams.Face.EyelashMapId = tc.FaceEyelashMapId;
            _template.ModelParams.Face.LipColor = tc.LipColor;
            _template.ModelParams.Face.LeftPupilColor = tc.LeftPupilColor;
            _template.ModelParams.Face.RightPupilColor = tc.RightPupilColor;
            _template.ModelParams.Face.EyebrowColor = tc.EyebrowColor;
            _template.ModelParams.Face.MovableDecalWeight = tc.FaceMovableDecalWeight;
            _template.ModelParams.Face.NormalMapWeight = tc.FaceNormalMapWeight;
            _template.ModelParams.Face.DecoColor = tc.DecoColor;
            _template.ModelParams.Face.Modifier = tc.Modifier;

            _template.Name = tc.Name;
            _template.NpcOnly = tc.NpcOnly;
            _template.OwnerTypeId = tc.OwnerTypeId;
        }
        else
        {
            _template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Skin);
        }

        ApplyExplicitCustomBodyParts(_template, selectedCustom);

        //Logger.Info("Loaded npc {0} random hair {1} and hairColor {2}", template.ModelId, _template.HairId, _template.ModelParams.HairColorId);

        return _template;
    }

    public void Load()
    {
        if (_loaded)
            return;

        _templates = new Dictionary<uint, NpcTemplate>();
        _tccLookup = new Dictionary<uint, List<uint>>();
        _totalCharacterCustoms = new Dictionary<uint, TotalCharacterCustom>();
        _itemBodyParts = new Dictionary<uint, Dictionary<uint, List<BodyPartTemplate>>>();
        _defaultFaceItemsByModel = new Dictionary<uint, uint>();
        _itemsWithoutArmorVisual = new HashSet<uint>();

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
                        // The 1.8 client stores the complete 128-byte facial/body
                        // morph block here. Sending an empty byte array makes the
                        // client fall back to placeholder custom materials (the
                        // visible TEST skin) and loses the NPC's head geometry.
                        // All target rows currently contain exactly 128 bytes, but
                        // keep the guard so malformed future data cannot corrupt
                        // the variable-length FaceModel field in SCUnitState.
                        if (!reader.IsDBNull("modifier") && reader.GetValue("modifier") is byte[] modifierBlob && modifierBlob.Length == 128)
                            custom.Modifier = modifierBlob;
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

                        custom.Name = string.Empty; // Client 1.8 has no localized/server name column.
                        custom.NpcOnly = reader.GetBoolean("npcOnly", true);
                        custom.OwnerTypeId = reader.GetUInt32("owner_type_id", 0);

                        // 3030 old
                        //reader.GetBytes("modifier", 0, custom.Modifier, 0, 128);
                        // 3030 new
                        //var blob = (string)reader.GetValue("modifier");
                        //if (blob != null)
                        //{
                        //    custom.Modifier = Helpers.StringToByteArray(blob);
                        //}
                        if (!reader.IsDBNull("modifier"))
                        {
                            var blob = reader.GetValue("modifier");
                            if (blob is byte[] modifier)
                                custom.Modifier = modifier;
                        }

                        _totalCharacterCustoms.Add(custom.Id, custom);
                    }
                }

                // Create a cached reference list by Model ID
                foreach (var c in _totalCharacterCustoms)
                {
                    if (!_tccLookup.ContainsKey(c.Value.ModelId))
                        _tccLookup.Add(c.Value.ModelId, new List<uint>());
                    _tccLookup[c.Value.ModelId].Add(c.Value.Id);
                }

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

                // Ferre player defaults (20117/20118) use the character-only
                // odd-eye face assets. When used as an NPC body part, the base
                // face remains visible below the total-custom morph. Resolve
                // the race-matching NPC base face from item_body_parts instead.
                command.CommandText = @"
                    SELECT ibp.model_id, MIN(ibp.item_id) AS face_item_id
                    FROM item_body_parts ibp
                    INNER JOIN characters c ON c.model_id = ibp.model_id
                    WHERE c.char_race_id = 6
                      AND ibp.slot_type_id = 23
                      AND ibp.npc_only = 0
                      AND LOWER(ibp.face_mask) LIKE '%ferre%'
                    GROUP BY ibp.model_id";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                        _defaultFaceItemsByModel[reader.GetUInt32("model_id")] = reader.GetUInt32("face_item_id");
                }

                // Some NPC cloth packs reference intentionally invisible or
                // incomplete armor records. Sending such an item makes this
                // client render its black/missing-material placeholder. With
                // no usable default or model-specific asset, absence of the
                // equipment entry is the equivalent intended visual state.
                command.CommandText = @"
                    SELECT ar.item_id
                    FROM item_armors ar
                    LEFT JOIN armor_assets aa ON aa.id = ar.asset_id
                    WHERE ar.asset2_id = 0
                      AND aa.slot_type_id IN (3, 4, 5, 6, 7)
                      AND (ar.asset_id = 0 OR (
                          COALESCE(aa.default_asset_id, 0) = 0
                          AND NOT EXISTS (
                              SELECT 1
                              FROM item_armor_assets ia
                              WHERE ia.armor_asset_id = ar.asset_id)))";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                        _itemsWithoutArmorVisual.Add(reader.GetUInt32("item_id"));
                }

                command.CommandText = "SELECT * FROM item_body_parts";
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
                        bodyParts.Add(bp);

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
                        template.Name = LocalizationManager.Instance.Get("npcs", "name", template.Id);
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
                        template.TotalCustomId = reader.GetUInt32("total_custom_id", 0);
                        using (var command2 = connection.CreateCommand())
                        {
                            command2.CommandText = "SELECT char_race_id, char_gender_id FROM characters WHERE model_id = @model_id";
                            command2.Parameters.AddWithValue("model_id", template.ModelId);
                            command2.Prepare();
                            using (var sqliteReader2 = command2.ExecuteReader())
                            using (var reader2 = new SQLiteWrapperReader(sqliteReader2))
                            {
                                if (reader2.Read())
                                {
                                    template.Race = reader2.GetByte("char_race_id", 0);
                                    template.Gender = reader2.GetByte("char_gender_id", 0);
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

                        TotalCharacterCustom selectedCustom = null;
                        if ((template.TotalCustomId > 0) && _totalCharacterCustoms.TryGetValue(template.TotalCustomId, out var tc))
                        {
                            selectedCustom = tc;
                            template.HairId = tc.HairId;
                            template.HornId = tc.HornId;

                            template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Face);
                            template.ModelParams
                                .SetModelId(tc.ModelId)
                                .SetBodyNormalMapId(tc.BodyNormalMapId)
                                .SetBodyNormalMapWeight(tc.BodyNormalMapWeight)
                                .SetDefaultHairColor(tc.DefaultHairColor)
                                .SetHairColorId(tc.HairColorId)
                                .SetHornColorId(tc.HornColorId)
                                .SetSkinColorId(tc.SkinColorId)
                                .SetTwoToneFirstWidth(tc.TwoToneFirstWidth)
                                .SetTwoToneHair(tc.TwoToneHairColor)
                                .SetTwoToneSecondWidth(tc.TwoToneSecondWidth);

                            template.ModelParams.Face.MovableDecalAssetId = tc.FaceMovableDecalAssetId;
                            template.ModelParams.Face.MovableDecalScale = tc.FaceMovableDecalScale;
                            template.ModelParams.Face.MovableDecalRotate = tc.FaceMovableDecalRotate;
                            template.ModelParams.Face.MovableDecalMoveX = tc.FaceMovableDecalMoveX;
                            template.ModelParams.Face.MovableDecalMoveY = tc.FaceMovableDecalMoveY;

                            template.ModelParams.Face.SetFixedDecalAsset(0, tc.FaceFixedDecalAsset0Id, tc.FaceFixedDecalAsset0Weight);
                            template.ModelParams.Face.SetFixedDecalAsset(1, tc.FaceFixedDecalAsset1Id, tc.FaceFixedDecalAsset1Weight);
                            template.ModelParams.Face.SetFixedDecalAsset(2, tc.FaceFixedDecalAsset2Id, tc.FaceFixedDecalAsset2Weight);
                            template.ModelParams.Face.SetFixedDecalAsset(3, tc.FaceFixedDecalAsset3Id, tc.FaceFixedDecalAsset3Weight);
                            template.ModelParams.Face.SetFixedDecalAsset(4, tc.FaceFixedDecalAsset4Id, tc.FaceFixedDecalAsset4Weight);
                            template.ModelParams.Face.SetFixedDecalAsset(5, tc.FaceFixedDecalAsset5Id, tc.FaceFixedDecalAsset5Weight);

                            template.ModelParams.Face.DiffuseMapId = tc.FaceDiffuseMapId;
                            template.ModelParams.Face.NormalMapId = tc.FaceNormalMapId;
                            template.ModelParams.Face.EyelashMapId = tc.FaceEyelashMapId;
                            template.ModelParams.Face.LipColor = tc.LipColor;
                            template.ModelParams.Face.LeftPupilColor = tc.LeftPupilColor;
                            template.ModelParams.Face.RightPupilColor = tc.RightPupilColor;
                            template.ModelParams.Face.EyebrowColor = tc.EyebrowColor;
                            template.ModelParams.Face.MovableDecalWeight = tc.FaceMovableDecalWeight;
                            template.ModelParams.Face.NormalMapWeight = tc.FaceNormalMapWeight;
                            template.ModelParams.Face.DecoColor = tc.DecoColor;
                            template.ModelParams.Face.Modifier = tc.Modifier;
                            // reader2.GetBytes("modifier", 0, template.ModelParams.Face.Modifier, 0, 128);

                            template.Name = tc.Name;
                            template.NpcOnly = tc.NpcOnly;
                            template.OwnerTypeId = tc.OwnerTypeId;
                        }
                        else
                        {
                            template.ModelParams = new UnitCustomModelParams(UnitCustomModelType.Skin);
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

    private void SetEquipItemTemplate(Npc npc, uint templateId, EquipmentItemSlot slot, byte grade = 0, bool npcOnly = false)
    {
        if (npcOnly && npc.Equipment.GetItemBySlot((int)slot) != null)
            return;

        if (templateId > 0 && _itemsWithoutArmorVisual.Contains(templateId))
            templateId = 0;

        Item item = null;
        if (templateId > 0)
        {
            item = ItemManager.Instance.Create(templateId, 1, grade, false);
            item.SlotType = SlotType.Equipment;
            item.Slot = (int)slot;
        }

        // npc.Equip[(int)slot] = item;
        npc.Equipment.AddOrMoveExistingItem(0, item, (int)slot);
    }

    public void BindSkillsToTemplate(uint templateId, List<NpcSkill> skills)
    {
        if (!_templates.ContainsKey(templateId))
            return;

        _templates[templateId].BindSkills(skills);
    }
}
