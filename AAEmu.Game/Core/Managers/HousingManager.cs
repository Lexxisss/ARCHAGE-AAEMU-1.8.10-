using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Housing;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using MySql.Data.MySqlClient;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class HousingManager : Singleton<HousingManager>
{
    private const uint ForSaleMarkerDoodadId = 6760;
    private const int HoursForFailedTaxToReturnHouse = 22;
    private const double CopperPerCertificate = 1000000.0; // For older versions of AA, 1 sale certificate / 100g

    /// <summary>
    /// How much tax is multiplied by, keyed by the number of heavily taxed buildings it starts at.
    /// </summary>
    /// <remarks>
    /// Kept sorted so a lookup can walk it and stop at the last row the owner's count reaches.
    /// </remarks>
    private static readonly SortedDictionary<int, float> HeavyTaxes = new();

    /// <summary>How many pieces of one kind of furniture a design's limit list allows.</summary>
    private static readonly Dictionary<(uint LimitId, uint GroupId), int> DecoGroupLimits = new();
    private const int TaxPaysForDays = 7; // Number of days 1 week worth of tax pays for
    private Dictionary<uint, House> _houses;
    private Dictionary<ushort, House> _housesTl; // TODO or so mb tlId is id in the active zone? or type of house
    private Dictionary<uint, HousingDecoration> _housingDecorations;
    private List<ItemHousingDecoration> _housingItemHousingDecorations;
    private List<HousingItemHousings> _housingItemHousings;
    private Dictionary<uint, HousingTemplate> _housingTemplates;

    /// <summary>Plots of land, grouped by the name of the zone they sit in.</summary>
    private Dictionary<string, List<HousingAreas>> _housingAreasByZone;

    /// <summary>The rule groups plots point at, keyed by their own id.</summary>
    private Dictionary<uint, HousingGroup> _housingGroups;

    /// <summary>Every conversion of one design into another, keyed by its own id.</summary>
    private Dictionary<uint, HousingRebuilding> _housingRebuildings;

    /// <summary>Materials a rebuild consumes, grouped by rebuild id.</summary>
    private Dictionary<uint, List<HousingRebuildingMaterial>> _housingRebuildingMaterials;

    private bool _isCheckingTaxTiming;
    private List<uint> _removedHousings;

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private static readonly string[] DecorationPersistenceColumns =
    {
        "id", "owner_id", "owner_type", "attach_point", "template_id", "current_phase_id",
        "plant_time", "growth_time", "phase_time", "x", "y", "z", "roll", "pitch", "yaw",
        "scale", "item_id", "house_id", "parent_doodad", "item_template_id", "item_container_id", "data"
    };

    /// <summary>
    /// Ensures that the server database can persist player-placed housing decorations.
    /// Housing furniture uses the general persistent <c>doodads</c> table with
    /// <c>owner_type = Housing</c> and <c>house_id</c> equal to the owning house DB id.
    /// </summary>
    private static void EnsureDecorationPersistenceSchema()
    {
        using var connection = MySQL.CreateConnection();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS `doodads` (
  `id` int unsigned NOT NULL AUTO_INCREMENT,
  `owner_id` int DEFAULT NULL COMMENT 'Character DB Id',
  `owner_type` tinyint unsigned NOT NULL DEFAULT '255',
  `attach_point` int unsigned NOT NULL DEFAULT '0' COMMENT 'Slot this doodad fits in on the owner',
  `template_id` int unsigned NOT NULL,
  `current_phase_id` int unsigned NOT NULL DEFAULT '0',
  `plant_time` datetime NOT NULL,
  `growth_time` datetime NOT NULL,
  `phase_time` datetime NOT NULL,
  `x` float NOT NULL DEFAULT '0',
  `y` float NOT NULL DEFAULT '0',
  `z` float NOT NULL DEFAULT '0',
  `roll` float NOT NULL DEFAULT '0',
  `pitch` float NOT NULL DEFAULT '0',
  `yaw` float NOT NULL DEFAULT '0',
  `scale` float NOT NULL DEFAULT '1',
  `item_id` bigint unsigned NOT NULL DEFAULT '0' COMMENT 'Item DB Id of the associated item',
  `house_id` int unsigned NOT NULL DEFAULT '0' COMMENT 'House DB Id if it is on actual house land',
  `parent_doodad` int unsigned NOT NULL DEFAULT '0' COMMENT 'Parent doodad DB Id',
  `item_template_id` int unsigned NOT NULL DEFAULT '0',
  `item_container_id` bigint unsigned NOT NULL DEFAULT '0',
  `data` int NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`),
  KEY `idx_doodads_owner_house` (`owner_type`,`house_id`),
  KEY `idx_doodads_parent` (`parent_doodad`),
  KEY `idx_doodads_item` (`item_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8 COMMENT='Persistent doodads, including housing decoration';";
            command.ExecuteNonQuery();
        }

        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = @"
SELECT `COLUMN_NAME`
FROM `INFORMATION_SCHEMA`.`COLUMNS`
WHERE `TABLE_SCHEMA` = DATABASE() AND `TABLE_NAME` = 'doodads';";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                existingColumns.Add(reader.GetString(0));
        }

        var missingColumns = DecorationPersistenceColumns.Where(column => !existingColumns.Contains(column)).ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException(
                "The server table `doodads` is missing columns required for housing decoration persistence: " +
                string.Join(", ", missingColumns) +
                ". Apply SQL/updates/2026-08-03_aaemu_game_house_decor_persistence.sql.");
        }

        Logger.Info("Housing decoration persistence schema ready: table=doodads, owner_type={0}",
            (byte)DoodadOwnerType.Housing);
    }

    /// <summary>
    /// Gets all houses for a given Account
    /// </summary>
    /// <param name="values"></param>
    /// <param name="accountId"></param>
    /// <returns></returns>
    public int GetByAccountId(Dictionary<uint, House> values, ulong accountId)
    {
        foreach (var (id, house) in _houses)
            if (house.AccountId == accountId)
                values.Add(id, house);
        return values.Count;
    }

    /// <summary>
    /// Gets all houses owned by Character
    /// </summary>
    /// <param name="values"></param>
    /// <param name="characterId"></param>
    /// <returns></returns>
    public int GetByCharacterId(Dictionary<uint, House> values, uint characterId)
    {
        foreach (var (id, house) in _houses)
            if (house.OwnerId == characterId)
                values.Add(id, house);
        return values.Count;
    }

    /// <summary>
    /// Creates House and set it's untouchable buff
    /// </summary>
    /// <param name="templateId"></param>
    /// <param name="factionId"></param>
    /// <param name="objectId"></param>
    /// <param name="tlId"></param>
    /// <returns></returns>
    private House Create(uint templateId, uint factionId, uint objectId = 0, ushort tlId = 0)
    {
        if (!_housingTemplates.TryGetValue(templateId, out var template))
            return null;

        var house = new House
        {
            TlId = tlId > 0 ? tlId : (ushort)HousingTldManager.Instance.GetNextId(),
            ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId(),
            Template = template,
            TemplateId = template.Id, // duplicate Id
            Id = template.Id,
            Faction = FactionManager.Instance.GetFaction(factionId),
            Name = LocalizationManager.Instance.Get("housings", "name", template.Id)
        };
        house.Hp = house.MaxHp;
        // Force public on always public properties on create
        if (template.AlwaysPublic)
            house.Permission = HousingPermission.Public;

        SetUntouchable(house, true);

        return house;
    }

    /// <summary>
    /// Load housing definitions, player houses and starts tax check timer
    /// </summary>
    /// <exception cref="IOException"></exception>
    public void Load()
    {
        EnsureDecorationPersistenceSchema();

        _housingTemplates = new Dictionary<uint, HousingTemplate>();
        _houses = new Dictionary<uint, House>();
        _housesTl = new Dictionary<ushort, House>();
        _removedHousings = new List<uint>();
        _housingItemHousings = new List<HousingItemHousings>();
        _housingDecorations = new Dictionary<uint, HousingDecoration>();
        _housingItemHousingDecorations = new List<ItemHousingDecoration>();
        _housingRebuildings = new Dictionary<uint, HousingRebuilding>();
        _housingRebuildingMaterials = new Dictionary<uint, List<HousingRebuildingMaterial>>();
        _housingAreasByZone = new Dictionary<string, List<HousingAreas>>(StringComparer.OrdinalIgnoreCase);
        _housingGroups = new Dictionary<uint, HousingGroup>();

        // var houseTaxes = new Dictionary<uint, HouseTax>();

        using (var connection = SQLite.CreateTargetClientConnection())
        {
            Logger.Info("Loading Housing Information ...");

            // The surcharge for owning several heavily taxed buildings.
            HeavyTaxes.Clear();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT count, multiplier FROM heavy_taxes ORDER BY count ASC";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                    HeavyTaxes[reader.GetInt32("count")] = reader.GetFloat("multiplier");
            }

            // How much of each kind of furniture a design holds. The client works this out from the
            // same rows without being told, so we have to reach the same answer.
            DecoGroupLimits.Clear();
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT housing_deco_limit_id, deco_actability_group_id, count FROM housing_deco_limit_elems";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                {
                    var key = (reader.GetUInt32("housing_deco_limit_id"), reader.GetUInt32("deco_actability_group_id"));
                    DecoGroupLimits[key] = reader.GetInt32("count");
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_housings";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new HousingItemHousings();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.Item_Id = reader.GetUInt32("item_id");
                        template.Design_Id = reader.GetUInt32("design_id");
                        _housingItemHousings.Add(template);
                    }
                }
            }

            Logger.Info("Loading Housing Templates...");

            var filePath = Path.Combine(FileManager.AppPath, "Data", "housing_bindings.json");
            var contents = FileManager.GetFileContents(filePath);
            if (string.IsNullOrWhiteSpace(contents))
                throw new IOException(
                    $"File {filePath} doesn't exists or is empty.");

            if (JsonHelper.TryDeserializeObject(contents, out List<HousingBindingTemplate> binding, out _))
                Logger.Info("Housing bindings loaded...");
            else
            {
                Logger.Warn("Housing bindings not loaded...");
                binding = new List<HousingBindingTemplate>();
            }

            // housing_bindings.json only names a subset of designs. Complete/rebuild/special-project
            // variants commonly reuse the exact same client model and sockets but have another housing
            // id, so an exact-id lookup silently gave every child (0,0,0). That is why plant-heavy
            // houses put all plants in their centre and why their doors/nameplate appeared dead: the
            // clickable fixtures existed, but were buried at the model origin.
            //
            // Build both indices up front. Exact id wins. A main-model fallback is safe only when the
            // model maps to one unambiguous binding map; ambiguous models are deliberately left
            // unresolved rather than guessed.
            var housingMainModelById = new Dictionary<uint, uint>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, main_model_id FROM housings";
                command.Prepare();
                using var reader = new SQLiteWrapperReader(command.ExecuteReader());
                while (reader.Read())
                    housingMainModelById[reader.GetUInt32("id")] = reader.GetUInt32("main_model_id");
            }

            var bindingByHousingId = new Dictionary<uint, HousingBindingTemplate>();
            var bindingByMainModelId = new Dictionary<uint, HousingBindingTemplate>();
            var ambiguousBindingModels = new HashSet<uint>();
            foreach (var bindingTemplate in binding)
            {
                if (bindingTemplate?.TemplateId == null)
                    continue;

                foreach (var sourceHousingId in bindingTemplate.TemplateId)
                {
                    if (!bindingByHousingId.TryAdd(sourceHousingId, bindingTemplate))
                    {
                        Logger.Warn(
                            "Duplicate housing binding coordinate map for design {0}; keeping the first map",
                            sourceHousingId);
                        continue;
                    }

                    if (!housingMainModelById.TryGetValue(sourceHousingId, out var sourceMainModelId))
                        continue;

                    if (ambiguousBindingModels.Contains(sourceMainModelId))
                        continue;

                    if (bindingByMainModelId.TryGetValue(sourceMainModelId, out var existing) &&
                        !ReferenceEquals(existing, bindingTemplate))
                    {
                        bindingByMainModelId.Remove(sourceMainModelId);
                        ambiguousBindingModels.Add(sourceMainModelId);
                        Logger.Warn("Housing binding model {0} has more than one coordinate map; model fallback disabled",
                            sourceMainModelId);
                    }
                    else
                    {
                        bindingByMainModelId[sourceMainModelId] = bindingTemplate;
                    }
                }
            }

            // A design no longer carries its garden radius itself - it points at a size row that
            // does. Reading the old column found nothing, which is how every building ended up
            // with a garden of zero.
            var gardenRadiusBySize = new Dictionary<uint, float>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, garden_radius FROM housing_sizes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                        gardenRadiusBySize[reader.GetUInt32("id")] = reader.GetFloat("garden_radius");
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM housings";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new HousingTemplate();
                        template.Id = reader.GetUInt32("id");
                        template.Name = LocalizationManager.Instance.Get("housings", "name", template.Id, reader.GetString("name"));
                        template.CategoryId = reader.GetUInt32("category_id");
                        template.MainModelId = reader.GetUInt32("main_model_id");
                        template.DoorModelId = reader.GetUInt32("door_model_id", 0);
                        template.StairModelId = reader.GetUInt32("stair_model_id", 0);
                        template.AutoZ = reader.GetBoolean("auto_z", true);
                        template.GateExists = reader.GetBoolean("gate_exists", true);
                        template.Hp = reader.GetInt32("hp");
                        template.RepairCost = reader.GetUInt32("repair_cost");
                        template.HousingSizeId = reader.GetUInt32("housing_size_id", 0);
                        template.GardenRadius = gardenRadiusBySize.GetValueOrDefault(template.HousingSizeId, 0f);
                        template.RotateItemId = reader.GetUInt32("rotate_item_id", 0);
                        template.RotateItemCount = reader.GetInt32("rotate_item_count", 0);

                        // Ordered by the place each decal goes on the building, because that number
                        // travels with the slot and is what the client sorts them by. The order the
                        // columns happen to be declared in is not it, and was what these were read
                        // in for a while.
                        template.UccKinds[0] = reader.GetInt32("ucc_kind_wall", 0);    // 1 wall
                        template.UccKinds[1] = reader.GetInt32("ucc_kind_floor", 0);   // 2 floor
                        template.UccKinds[2] = reader.GetInt32("ucc_kind_top", 0);     // 3 top
                        template.UccKinds[3] = reader.GetInt32("ucc_kind_outwall", 0); // 4 outer wall
                        template.UccKinds[4] = reader.GetInt32("ucc_kind_roof", 0);    // 5 roof
                        template.UccScales[0] = reader.GetInt32("ucc_scale_wall", 0);
                        template.UccScales[1] = reader.GetInt32("ucc_scale_floor", 0);
                        template.UccScales[2] = reader.GetInt32("ucc_scale_top", 0);
                        template.UccScales[3] = reader.GetInt32("ucc_scale_outwall", 0);
                        template.UccScales[4] = reader.GetInt32("ucc_scale_roof", 0);
                        template.Family = reader.GetString("family");
                        var taxationId = reader.GetUInt32("taxation_id");
                        template.Taxation = TaxationsManager.Instance.taxations.ContainsKey(taxationId) ? TaxationsManager.Instance.taxations[taxationId] : null;
                        template.GuardTowerSettingId = reader.GetUInt32("guard_tower_setting_id", 0);
                        template.CinemaRadius = reader.GetFloat("cinema_radius");
                        template.AutoZOffsetX = reader.GetFloat("auto_z_offset_x");
                        template.AutoZOffsetY = reader.GetFloat("auto_z_offset_y");
                        template.AutoZOffsetZ = reader.GetFloat("auto_z_offset_z");
                        template.Alley = reader.GetFloat("alley");
                        template.ExtraHeightAbove = reader.GetFloat("extra_height_above");
                        template.ExtraHeightBelow = reader.GetFloat("extra_height_below");
                        template.DecoLimit = reader.GetUInt32("deco_limit");
                        template.AbsoluteDecoLimit = reader.GetUInt32("absolute_deco_limit");
                        template.HousingDecoLimitId = reader.GetUInt32("housing_deco_limit_id", 0);
                        template.IsSellable = reader.GetBoolean("is_sellable", true);
                        template.HeavyTax = reader.GetBoolean("heavy_tax", true);
                        template.AlwaysPublic = reader.GetBoolean("always_public", true);
                        _housingTemplates.Add(template.Id, template);

                        var bindingSourceHousingId = template.Id;
                        bindingByHousingId.TryGetValue(template.Id, out var templateBindings);
                        if (templateBindings == null &&
                            !ambiguousBindingModels.Contains(template.MainModelId) &&
                            bindingByMainModelId.TryGetValue(template.MainModelId, out var modelBindings))
                        {
                            templateBindings = modelBindings;
                            bindingSourceHousingId = modelBindings.TemplateId.FirstOrDefault();
                            Logger.Info(
                                "Housing binding fallback: design={0}, model={1}, sourceDesign={2}, family={3}",
                                template.Id, template.MainModelId, bindingSourceHousingId, template.Family);
                        }

                        using (var command2 = connection.CreateCommand())
                        {
                            command2.CommandText = "SELECT * FROM housing_binding_doodads WHERE housing_id=@housing_id";
                            command2.Parameters.AddWithValue("housing_id", template.Id);
                            command2.Prepare();
                            using (var reader2 = new SQLiteWrapperReader(command2.ExecuteReader()))
                            {
                                var doodads = new List<HousingBindingDoodad>();
                                while (reader2.Read())
                                {
                                    var bindingDoodad = new HousingBindingDoodad
                                    {
                                        AttachPointId = (AttachPointKind)reader2.GetUInt32("attach_point_id"),
                                        DoodadId = reader2.GetUInt32("doodad_id")
                                    };

                                    if (templateBindings?.AttachPointId != null &&
                                        templateBindings.AttachPointId.TryGetValue(bindingDoodad.AttachPointId, out var pos))
                                    {
                                        bindingDoodad.Position = NormalizeHousingBindingPosition(
                                            pos, template.Id, bindingSourceHousingId, bindingDoodad.AttachPointId);
                                    }
                                    else
                                    {
                                        // Keep the object visible for diagnostics, but never hide the missing source:
                                        // zero is the model origin and is not a valid generic socket position.
                                        bindingDoodad.Position = new WorldSpawnPosition();
                                        Logger.Warn(
                                            "Housing binding position missing: design={0}, model={1}, attachPoint={2}, doodad={3}",
                                            template.Id, template.MainModelId, bindingDoodad.AttachPointId,
                                            bindingDoodad.DoodadId);
                                    }

                                    doodads.Add(bindingDoodad);
                                }

                                template.HousingBindingDoodad = doodads.ToArray();
                            }
                        }
                    }
                }
            }

            Logger.Info($"Loaded Housing Templates {_housingTemplates.Count}");

            LoadHousingAreas(connection);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM housing_build_steps";
                command.Prepare();
                var buildStepCount = 0;
                var buildStepDesignsMissing = 0;
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var housingId = reader.GetUInt32("housing_id");
                        if (!_housingTemplates.ContainsKey(housingId))
                        {
                            buildStepDesignsMissing++;
                            continue;
                        }

                        var template = new HousingBuildStep
                        {
                            Id = reader.GetUInt32("id"), 
                            HousingId = housingId, 
                            Step = reader.GetInt16("step"), 
                            ModelId = reader.GetUInt32("model_id"),
                            SkillId = reader.GetUInt32("skill_id"),
                            NumActions = reader.GetInt32("num_actions")
                        };

                        _housingTemplates[housingId].BuildSteps.Add(template.Step, template);
                        buildStepCount++;
                    }
                }

                // Without stages every design is finished the moment it is placed, which is the
                // hardest thing to tell apart from a design that genuinely has none.
                if (buildStepCount == 0)
                    Logger.Warn("No housing build steps loaded - every building will be placed already finished");
                else
                    Logger.Info($"Loaded Housing Build Steps {buildStepCount}" +
                                (buildStepDesignsMissing > 0 ? $", {buildStepDesignsMissing} skipped for unknown designs" : ""));
            }

            Logger.Info("Loaded Decoration Templates...");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM housing_decorations";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new HousingDecoration();
                        template.Id = reader.GetUInt32("id");
                        //template.Name = reader.GetString("name"); // there is no such field in the database for version 3.0.3.0
                        template.AllowOnFloor = reader.GetBoolean("allow_on_floor", true);
                        template.AllowOnWall = reader.GetBoolean("allow_on_wall", true);
                        template.AllowOnCeiling = reader.GetBoolean("allow_on_ceiling", true);
                        template.DoodadId = reader.GetUInt32("doodad_id");
                        template.AllowPivotOnGarden = reader.GetBoolean("allow_pivot_on_garden", true);
                        template.ActabilityGroupId = !reader.IsDBNull("actability_group_id") ? reader.GetUInt32("actability_group_id") : 0;
                        template.ActabilityUp = !reader.IsDBNull("actability_up") ? reader.GetUInt32("actability_up") : 0;
                        template.DecoActAbilityGroupId = !reader.IsDBNull("deco_actability_group_id") ? reader.GetUInt32("deco_actability_group_id") : 0;
                        template.AllowMeshOnGarden = reader.GetBoolean("allow_mesh_on_garden", true);

                        _housingDecorations.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM item_housing_decorations";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ItemHousingDecoration();
                        //template.Id = reader.GetUInt32("id"); // there is no such field in the database for version 3.0.3.0
                        template.ItemId = reader.GetUInt32("item_id");
                        template.DesignId = reader.GetUInt32("design_id");
                        template.Restore = reader.GetBoolean("restore", true);

                        _housingItemHousingDecorations.Add(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM housing_rebuildings";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new HousingRebuilding
                        {
                            Id = reader.GetUInt32("id"),
                            HousingId = reader.GetUInt32("housing_id"),
                            SkillId = reader.GetUInt32("skill_id"),
                            ActabilityGroupId = reader.GetUInt32("actability_group_id", 0),
                            LaborPower = reader.GetInt32("labor_power", 0),
                            Name = reader.GetString("name", string.Empty),
                            ChangePointDesc = reader.GetString("change_point_desc", string.Empty)
                        };

                        _housingRebuildings[template.Id] = template;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM housing_rebuilding_materials";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var material = new HousingRebuildingMaterial
                        {
                            Id = reader.GetUInt32("id"),
                            HousingRebuildingId = reader.GetUInt32("housing_rebuilding_id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Count = reader.GetInt32("count", 0)
                        };

                        if (!_housingRebuildingMaterials.TryGetValue(material.HousingRebuildingId, out var list))
                        {
                            list = new List<HousingRebuildingMaterial>();
                            _housingRebuildingMaterials.Add(material.HousingRebuildingId, list);
                        }

                        list.Add(material);
                    }
                }
            }

            Logger.Info("Loaded {0} housing rebuildings with {1} material groups",
                _housingRebuildings.Count, _housingRebuildingMaterials.Count);
        }

        Logger.Info("Loading Player Buildings ...");
        using (var connection = MySQL.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.CommandText = "SELECT * FROM housings";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var templateId = reader.GetUInt32("template_id");
                        var factionId = reader.GetUInt32("faction_id");
                        var house = Create(templateId, factionId);
                        house.Id = reader.GetUInt32("id");
                        house.AccountId = reader.GetUInt64("account_id");
                        house.OwnerId = reader.GetUInt32("owner");
                        house.CoOwnerId = reader.GetUInt32("co_owner");
                        house.Name = reader.GetString("name");
                        house.Transform = new Transform(house, null,
                            new Vector3(reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z")),
                            new Vector3(reader.GetFloat("roll"), reader.GetFloat("pitch"), reader.GetFloat("yaw"))
                        );
                        house.Transform.ZoneId = WorldManager.Instance.GetZoneId(house.Transform.WorldId, house.Transform.World.Position.X, house.Transform.World.Position.Y);
                        house.CurrentStep = reader.GetInt32("current_step");
                        house.NumAction = reader.GetInt32("current_action");
                        house.Permission = (HousingPermission)reader.GetByte("permission");
                        house.PlaceDate = reader.GetDateTime("place_date");
                        house.ProtectionEndDate = reader.GetDateTime("protected_until");
                        house.SellToPlayerId = reader.GetUInt32("sell_to");
                        house.SellPrice = reader.GetUInt32("sell_price");
                        house.AllowRecover = reader.GetBoolean("allow_recover");
                        _houses.Add(house.Id, house);
                        _housesTl.Add(house.TlId, house);

                        // Manually placed houses (or after upgrading MySQL), will get 2 weeks for free as to not immediately trigger them into demolition
                        if (house.PlaceDate == house.ProtectionEndDate)
                            house.ProtectionEndDate = house.PlaceDate.AddDays(14);

                        UpdateTaxInfo(house);
                        house.IsDirty = false;
                    }
                }
            }
        }

        Logger.Info($"Loaded {_houses.Count} Player Buildings");

        var houseCheckTask = new HousingTaxTask();
        TaskManager.Instance.Schedule(houseCheckTask, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));

        Logger.Info("Started Housing Tax Timer");
    }

    /// <summary>
    /// Saves player housing information
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="transaction"></param>
    /// <returns></returns>
    public (int, int) Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        var deleteCount = 0;
        lock (_removedHousings)
        {
            if (_removedHousings.Count > 0)
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText =
                        $"DELETE FROM housings WHERE id IN({string.Join(",", _removedHousings)})";
                    command.Prepare();
                    command.ExecuteNonQuery();
                    deleteCount++;
                }

                _removedHousings.Clear();
            }
        }

        var updateCount = 0;
        foreach (var house in _houses.Values)
            if (house.Save(connection, transaction))
                updateCount++;

        return (updateCount, deleteCount);
    }

    /// <summary>
    /// Spawn all houses
    /// </summary>
    public void SpawnAll()
    {
        foreach (var house in _houses.Values)
            house.Spawn();
    }

    /// <summary>
    /// Sets or removes the untouchable buff for the house
    /// </summary>
    /// <param name="house"></param>
    /// <param name="isUntouchable"></param>
    private static void SetUntouchable(House house, bool isUntouchable)
    {
        if (isUntouchable)
        {
            if (house.Buffs.CheckBuff((uint)BuffConstants.Untouchable))
                return;

            // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
            var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.Untouchable);
            if (protectionBuffTemplate != null)
            {
                var casterObj = new SkillCasterUnit(house.ObjId);
                house.Buffs.AddBuff(new Buff(house, house, casterObj,
                    protectionBuffTemplate, null, DateTime.UtcNow));
            }
            else
            {
                Logger.Error("Unable to find Untouchable buff template");
            }
        }
        else
        {
            // Remove Untouchable if it's enabled
            if (house.Buffs.CheckBuff((uint)BuffConstants.Untouchable))
                house.Buffs.RemoveBuff((uint)BuffConstants.Untouchable);
        }
    }

    /// <summary>
    /// Sets or removes the removal debuff for demolishing houses
    /// </summary>
    /// <param name="house"></param>
    /// <param name="isDeteriorating"></param>
    private static void SetRemovalDebuff(House house, bool isDeteriorating)
    {
        if (isDeteriorating)
        {
            if (!house.Buffs.CheckBuff((uint)BuffConstants.RemovalDebuff))
            {
                // Permanent Untouchable buff, should only be removed when failed tax payment, or demolishing by hand
                var protectionBuffTemplate = SkillManager.Instance.GetBuffTemplate((uint)BuffConstants.RemovalDebuff);
                if (protectionBuffTemplate != null)
                {
                    var casterObj = new SkillCasterUnit(house.ObjId);
                    house.Buffs.AddBuff(new Buff(house, house, casterObj,
                        protectionBuffTemplate, null, DateTime.UtcNow));
                }
                else
                {
                    Logger.Error("Unable to find Removal Debuff template");
                }
            }
        }
        else
        {
            // Remove Untouchable if it's enabled
            if (house.Buffs.CheckBuff((uint)BuffConstants.RemovalDebuff))
                house.Buffs.RemoveBuff((uint)BuffConstants.RemovalDebuff);
        }
    }

    /// <summary>
    /// Loads the plots of land and the rules that govern them.
    /// </summary>
    /// <remarks>
    /// None of this was read before, which is why the server accepted a building anywhere at all.
    /// A plot carries no shape here - see <see cref="HousingAreas"/> - so what this buys is the
    /// zone a placement lands in and everything that follows from the plots of that zone.
    /// </remarks>
    private static WorldSpawnPosition NormalizeHousingBindingPosition(
        WorldSpawnPosition source, uint designId, uint sourceDesignId, AttachPointKind attachPoint)
    {
        var result = source.Clone();

        // Two extracted binding groups contain values such as 4205.623, 8391.406 and 12593.98.
        // Their neighbouring sockets and the repeated 4.2/8.4/12.59 pattern show a lost decimal
        // separator during the old extraction. These are local house coordinates, so kilometre-high
        // offsets are impossible. Restore the intended metres without changing legitimate large
        // buildings (the normal data stays far below this threshold).
        if (MathF.Abs(result.Z) > 1000f)
        {
            var encodedZ = result.Z;
            result.Z /= 1000f;
            Logger.Warn(
                "Housing binding Z normalized: design={0}, sourceDesign={1}, attachPoint={2}, {3} -> {4}",
                designId, sourceDesignId, attachPoint, encodedZ, result.Z);
        }

        return result;
    }

    private void LoadHousingAreas(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM housing_groups";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var group = new HousingGroup
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name"),
                        AllowedTaxDelayWeek = reader.GetInt32("allowed_tax_delay_week", 0),
                        CanExtend = reader.GetBoolean("can_extend", true),
                        Houseless = reader.GetBoolean("houseless", true),
                        ExistingCategoryId = reader.GetUInt32("existing_category_id", 0)
                    };

                    _housingGroups[group.Id] = group;
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM housing_group_categories";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var groupId = reader.GetUInt32("housing_group_id");
                    if (!_housingGroups.TryGetValue(groupId, out var group))
                        continue;

                    group.AllowedCategories[reader.GetUInt32("category_id")] = reader.GetInt32("max_construct_count", 0);
                }
            }
        }

        var areaCount = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM housing_areas";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var area = new HousingAreas
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name") ?? string.Empty,
                        GroupId = reader.GetUInt32("housing_group_id"),
                        Activated = reader.GetBoolean("activated", true),
                        OpensAt = ReadAreaOpeningDate(reader)
                    };

                    if (!_housingAreasByZone.TryGetValue(area.Name, out var areas))
                    {
                        areas = new List<HousingAreas>();
                        _housingAreasByZone.Add(area.Name, areas);
                    }

                    areas.Add(area);
                    areaCount++;
                }
            }
        }

        if (areaCount == 0)
            Logger.Warn("No housing areas loaded - placement cannot be checked against the world");
        else
            Logger.Info($"Loaded Housing Areas {areaCount} over {_housingAreasByZone.Count} zones, {_housingGroups.Count} groups");
    }

    /// <summary>
    /// Whether a design may be raised at a spot, by design id.
    /// </summary>
    /// <remarks>
    /// For callers that have to know before they spend anything - a rebuild consumes materials
    /// and demolishes the old building before the new one is placed, so it has to ask first.
    /// </remarks>
    /// <param name="replacing">
    /// A building that is about to make way for this one. It is left out of the ownership counts,
    /// or a rebuild on land that admits one building per player would refuse itself.
    /// </param>
    public bool CanPlaceDesign(Character character, uint designId, float x, float y, out ErrorMessageType error,
        House replacing = null)
    {
        if (!_housingTemplates.TryGetValue(designId, out var design))
        {
            error = ErrorMessageType.HouseCannotCreate;
            return false;
        }

        return ValidatePlacement(character, design, x, y, out error, replacing);
    }

    /// <summary>
    /// Decides whether a design may be raised where the player asked for it.
    /// </summary>
    /// <remarks>
    /// The client refuses a bad placement on its own and sends nothing, so a request that arrives
    /// here has already passed its checks. That is a reason not to expect refusals, not a reason
    /// to trust the request: nothing stops a crafted one.
    ///
    /// The check is as fine as the data allows, which is the zone. A plot's outline lives in the
    /// client's world data, so the server cannot tell one plot of a zone from another - what it
    /// can tell is which zone the placement lands in, which plots that zone holds, and everything
    /// the rules of those plots then say. A placement passes if any plot of the zone would accept
    /// it; the refusal reported is the one from the plot that got furthest.
    /// </remarks>
    /// <returns>False when the placement is refused; the reason is in <paramref name="error"/>.</returns>
    private bool ValidatePlacement(Character character, HousingTemplate design, float x, float y,
        out ErrorMessageType error, House replacing = null)
    {
        error = ErrorMessageType.NoHousingArea;

        if (_housingAreasByZone.Count == 0)
            return true; // nothing loaded to check against; the log at startup already said so

        var zoneKey = WorldManager.Instance.GetZoneId(character.Transform.WorldId, x, y);
        var zone = ZoneManager.Instance.GetZoneByKey(zoneKey);
        if (zone == null || string.IsNullOrEmpty(zone.Name) ||
            !_housingAreasByZone.TryGetValue(zone.Name, out var areas) || areas.Count == 0)
        {
            Logger.Info($"Placement refused: no housing plots in zone {zone?.Name ?? "?"} (key {zoneKey})");
            return false;
        }

        var now = DateTime.UtcNow;
        var owned = new Dictionary<uint, House>();
        GetByCharacterId(owned, character.Id);
        var ownedHouses = owned.Values.Where(h => h != replacing).ToList();
        var ownsAnyHouse = ownedHouses.Count > 0;
        ErrorMessageType? refusal = null;

        foreach (var area in areas)
        {
            if (!_housingGroups.TryGetValue(area.GroupId, out var group))
                continue;

            // Least specific first, so the reason we keep is the one from the plot that got
            // furthest through the rules - the closest thing to an answer for the player.
            if (!group.AllowedCategories.TryGetValue(design.CategoryId, out var maxCount))
            {
                refusal ??= ErrorMessageType.HouseCannotLoacateInvalidCategoryArea;
                continue;
            }

            if (!area.Activated)
            {
                refusal = ErrorMessageType.HousingAreaNotActivated;
                continue;
            }

            if (area.OpensAt > now)
            {
                refusal = ErrorMessageType.HousingAreaNotOpen;
                continue;
            }

            if (group.Houseless && ownsAnyHouse)
            {
                refusal = ErrorMessageType.HouseCannotOwnMoreHouselessCondition;
                continue;
            }

            if (group.ExistingCategoryId > 0 &&
                !ownedHouses.Any(h => h.Template?.CategoryId == group.ExistingCategoryId))
            {
                refusal = ErrorMessageType.HouseCannotOwnMoreExistingCategoryCondition;
                continue;
            }

            if (maxCount > 0 && CountOwnedInZone(ownedHouses, zone.Name, design.CategoryId) >= maxCount)
            {
                refusal = ErrorMessageType.HouseCannotConstructInAreaByMaxConstructCount;
                continue;
            }

            return true;
        }

        error = refusal ?? ErrorMessageType.NoHousingArea;
        Logger.Info($"Placement refused in zone {zone.Name} for design {design.Id} (category {design.CategoryId}): {error}");
        return false;
    }

    /// <summary>
    /// How many buildings of one category the player already has in a zone. The count is per
    /// zone rather than per plot for the same reason the checks are - the plot cannot be told.
    /// </summary>
    private static int CountOwnedInZone(List<House> ownedHouses, string zoneName, uint categoryId)
    {
        var count = 0;
        foreach (var house in ownedHouses)
        {
            if (house.Template?.CategoryId != categoryId)
                continue;

            var zone = ZoneManager.Instance.GetZoneByKey(
                WorldManager.Instance.GetZoneId(
                    house.Transform.WorldId,
                    house.Transform.World.Position.X,
                    house.Transform.World.Position.Y));

            if (string.Equals(zone?.Name, zoneName, StringComparison.OrdinalIgnoreCase))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Reads a plot's opening date, which is kept as separate fields and left at zero for a plot
    /// that was never scheduled.
    /// </summary>
    private static DateTime ReadAreaOpeningDate(SQLiteWrapperReader reader)
    {
        var year = reader.GetInt32("at_year", 0);
        if (year <= 0)
            return DateTime.MinValue;

        var month = Math.Clamp(reader.GetInt32("at_month", 1), 1, 12);
        var day = Math.Clamp(reader.GetInt32("at_day", 1), 1, DateTime.DaysInMonth(year, month));
        var hour = Math.Clamp(reader.GetInt32("at_hour", 0), 0, 23);
        var minute = Math.Clamp(reader.GetInt32("at_min", 0), 0, 59);

        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Sends tax information about a house
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="designId"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    public void ConstructHouseTax(GameConnection connection, uint designId, float x, float y, float z)
    {
        // TODO validation position and some range...

        var houseTemplate = _housingTemplates[designId];

        CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out var heavyTaxHouseCount, out var normalTaxHouseCount, out _, out _);

        var baseTax = (int)(houseTemplate.Taxation?.Tax ?? 0);
        var depositTax = baseTax * 2;

        connection.SendPacket(
            new SCConstructHouseTaxPacket(designId,
                heavyTaxHouseCount,
                normalTaxHouseCount,
                houseTemplate.HeavyTax,
                baseTax,
                depositTax,
                totalTaxAmountDue
            )
        );
    }

    /// <summary>
    /// Request house tax information (using name plaque of a house)
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    public void HouseTaxInfo(GameConnection connection, uint tlId, uint objId)
    {
        House house = null;
        if (tlId <= ushort.MaxValue)
            _housesTl.TryGetValue((ushort)tlId, out house);

        if (house == null && objId > 0)
            house = _houses.Values.FirstOrDefault(h => h.ObjId == objId);

        if (house == null)
        {
            Logger.Warn("HouseTaxInfo: house not found, tl={0}, objId={1}", tlId, objId);
            return;
        }

        // Asking about a building's tax means having the building, which is the nearest thing to
        // proof that it was registered - there is no message that says so outright. The state goes
        // again here, for the case where the timed delivery lost its race and was dropped in
        // silence. It describes rather than creates, so saying it twice costs nothing.
        connection.SendPacket(new SCHouseStatePacket(house));

        // What a building costs to put up is not what its owner owes afterwards. The two were the
        // same number here, so a house announced the price of its own construction - deposit and
        // all, already paid - as a debt, the moment it was finished.
        CalculateBuildingTaxInfo(house.AccountId, house.Template, false, out var totalTaxAmountDue, out _, out _, out _, out _);

        var baseTax = (int)(house.Template.Taxation?.Tax ?? 0);
        var depositTax = baseTax * 2;

        var now = DateTime.UtcNow;
        var isTaxDue = house.TaxDueDate <= now;
        var weeksWithoutPay = isTaxDue
            ? Math.Max(0, (int)Math.Floor((now - house.TaxDueDate).TotalDays / TaxPaysForDays))
            : 0;
        var amountDue = isTaxDue
            ? (long)totalTaxAmountDue * (weeksWithoutPay + 1L)
            : 0L;
        var isAlreadyPaid = !isTaxDue;

        Logger.Info(
            "HouseTaxInfo: house={0} tl={1} design={2} baseTax={3} deposit={4} weeklyTax={5} " +
            "amountDue={6} protectionEnd={7} taxDue={8} isAlreadyPaid={9} weeksWithoutPay={10} heavy={11}",
            house.Id, house.TlId, house.TemplateId, baseTax, depositTax, totalTaxAmountDue, amountDue,
            house.ProtectionEndDate, house.TaxDueDate, isAlreadyPaid, weeksWithoutPay,
            house.Template.HeavyTax);

        connection.SendPacket(
            new SCHouseTaxInfoPacket(
                house.TlId,
                0,  // TODO: implement when castles are added
                0,
                depositTax, // this is used in the help text on (?) when you hover your mouse over it to display deposit tax for this building
                amountDue, // current unpaid amount; zero during the paid protection period
                house.TaxDueDate,
                isAlreadyPaid,
                (byte)Math.Clamp(weeksWithoutPay, 0, byte.MaxValue),
                0,   // weeksPrepay: a single byte on the wire, so -1 is not expressible here
                house.Template.HeavyTax
            )
        );
    }

    /// <summary>
    /// Start building a house at target location using design
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="designId"></param>
    /// <param name="posX"></param>
    /// <param name="posY"></param>
    /// <param name="posZ"></param>
    /// <param name="zRot"></param>
    /// <param name="itemId"></param>
    /// <param name="moneyAmount"></param>
    /// <param name="ht">
    /// Carried through from the request. The client always sends zero - it has no sender path
    /// that fills this in - so it is not the housing type and placement must not be refused over
    /// it. The type the client expects back in the state block comes from the design instead.
    /// </param>
    public void Build(GameConnection connection, uint designId, float posX, float posY, float posZ, float zRot,
        ulong itemId, long moneyAmount, int ht)
    {
        // TODO validate house by range...
        // TODO remove itemId
        // TODO minus moneyAmount

        var sourceDesignItem = connection.ActiveChar.Inventory.GetItemById(itemId);
        if ((sourceDesignItem == null) || (sourceDesignItem.OwnerId != connection.ActiveChar.Id))
        {
            // Invalid itemId supplied or the id is not owned by the user
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagInvalidItem);
            return;
        }

        // var zoneId = WorldManager.Instance.GetZoneId(connection.ActiveChar.Transform.WorldId, posX, posY);

        // A design the server does not know is a refusal, not an exception. Placement has no
        // reply of its own - the client is told about a refusal through the general error
        // message, with the reason it names for a building it could not create.
        if (!_housingTemplates.TryGetValue(designId, out var houseTemplate))
        {
            Logger.Warn($"Build: no housing design {designId} for player {connection.ActiveChar.Name}");
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotCreate);
            return;
        }

        // Before anything is taken from the player: what the land allows.
        if (!ValidatePlacement(connection.ActiveChar, houseTemplate, posX, posY, out var placementError))
        {
            connection.ActiveChar.SendErrorMessage(placementError);
            return;
        }
        CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out _, out _, out _, out _);

        if (moneyAmount != totalTaxAmountDue)
            Logger.Warn("Build tax quote mismatch: design={0}, client={1}, server={2}; charging authoritative server value",
                designId, moneyAmount, totalTaxAmountDue);

        if (!ChargePlacementTax(connection, houseTemplate, totalTaxAmountDue))
            return;

        if (connection.ActiveChar.Inventory.Bag.ConsumeItem(ItemTaskType.HouseBuilding, sourceDesignItem.TemplateId, 1, sourceDesignItem) <= 0)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.BagInvalidItem);
            return;
        }

        // Spawn the actual house
        var house = Create(designId, connection.ActiveChar.Faction.Id);

        // Fallback for un-translated buildings (en_us)
        if (house.Name == string.Empty)
        {
            var fakeLocalizedName = LocalizationManager.Instance.Get("items", "name", sourceDesignItem.Template.Id, houseTemplate.Name);
            if (fakeLocalizedName.EndsWith(" Design"))
                fakeLocalizedName = fakeLocalizedName.Replace(" Design", "");
            house.Name = fakeLocalizedName;
        }

        house.Id = HousingIdManager.Instance.GetNextId();
        house.Transform.Local.SetPosition(posX, posY, posZ);
        house.Transform.Local.SetZRotation(zRot);

        house.OwnerId = connection.ActiveChar.Id;
        house.CoOwnerId = connection.ActiveChar.Id;
        house.AccountId = connection.AccountId;
        house.Ht = ht;
        house.Permission = HousingPermission.Private;
        house.AllowRecover = true;
        house.PlaceDate = DateTime.UtcNow;
        house.ProtectionEndDate = DateTime.UtcNow.AddDays(TaxPaysForDays * 2);

        // Last, because a design with no stages is finished on the spot and builds its doors and
        // chests here - and those take the building's ownership as it stands at that moment.
        //
        // Having no stages is a real case, but it is also exactly what an unloaded build-step
        // table looks like from the outside, hence the log.
        house.CurrentStep = house.Template.FirstBuildStep;
        if (house.CurrentStep == -1)
            Logger.Info($"Build: design {designId} has no construction stages, house {house.Id} is placed finished");
        else
            Logger.Debug($"Build: house {house.Id} design {designId} starts at stage {house.CurrentStep} of {house.Template.BuildSteps.Count}, model {house.ModelId}, {house.AllAction} actions");

        _houses.Add(house.Id, house);
        _housesTl.Add(house.TlId, house);

        // The placement reply is not a message of its own. The client expects, in this order:
        // the generic scene object for the foundation, then the house state once that object
        // exists, then the build progress. Spawn covers the first two - it is what makes the
        // building visible and sends its state - so nothing may be sent before it.
        //
        // A "my house" message used to go out here, ahead of the spawn. No such packet exists
        // in this client, so it carried a placeholder opcode; encoding it threw and took the
        // rest of the placement with it, which is why the building appeared and vanished.
        house.Spawn();

        // Exactly what the spawn message carried, because a building that does not appear leaves
        // nothing else to go on: the client refuses one silently and says nothing back.
        Logger.Info($"Build: spawned house {house.Id} objId={house.ObjId}, tl={house.TlId}, " +
                    $"design={house.TemplateId}, step={house.CurrentStep}, model={house.ModelId}, " +
                    $"world={house.Transform.WorldId}, zone={house.Transform.ZoneId}, " +
                    $"pos=({house.Transform.World.Position.X:F1},{house.Transform.World.Position.Y:F1},{house.Transform.World.Position.Z:F1}), " +
                    $"doodads={house.AttachedDoodads.Count}");

        // Step 8: the ownership summary. Without it the client never learns the building is
        // its own, and never asks for its tax either - the request comes from this handler.
        connection.ActiveChar.SendPacket(new SCHouseDataPacket(house));

        connection.ActiveChar.SendPacket(new SCHouseBuildProgressPacket(
            house.TlId,
            house.ModelId,
            house.AllAction,
            house.CurrentStep == -1 ? house.AllAction : house.CurrentAction));

        UpdateTaxInfo(house);
    }

    /// <summary>
    /// Update house permission settings
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="permission"></param>
    public void ChangeHousePermission(GameConnection connection, ushort tlId, HousingPermission permission)
    {
        if (!_housesTl.TryGetValue(tlId, out var house))
            return; // invalid house

        if (house.OwnerId != connection.ActiveChar.Id)
            return; // not the owner
        
        house.Permission = permission;
        house.BroadcastPacket(new SCHousePermissionChangedPacket(tlId, (byte)permission), false);
    }

    /// <summary>
    /// Rename house
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="tlId"></param>
    /// <param name="name"></param>
    public void ChangeHouseName(GameConnection connection, ushort tlId, string name)
    {
        if (!_housesTl.TryGetValue(tlId, out var house))
            return;

        if (house.OwnerId != connection.ActiveChar.Id)
            return;

        house.Name = string.Concat(name.Substring(0, 1).ToUpper(), name.AsSpan(1));
        house.IsDirty = true; // Manually set the IsDirty on House level
        connection.SendPacket(new SCUnitNameChangedPacket(house.ObjId, house.Name));
    }

    /// <summary>
    /// Takes the up-front tax a placement or a rebuild owes, in certificates where the feature
    /// is enabled and in gold otherwise.
    /// </summary>
    /// <returns>False when the player cannot pay; an error has already been sent to them.</returns>
    private static bool ChargePlacementTax(GameConnection connection, HousingTemplate template, int totalTaxAmountDue)
    {
        // Buildings are paid for in tax certificates, not coin. Charging coin was the branch this
        // took whenever the certificate feature was switched off, and off is where it sits by
        // default, so every building anyone has put up here has been paid for in gold.
        var totalCertsCost = CertificatesFor(template, totalTaxAmountDue);
        if (totalCertsCost > 0)
        {
            var userTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Inventory, Item.TaxCertificate);
            var userBoundTaxCount = connection.ActiveChar.Inventory.GetItemsCount(SlotType.Inventory, Item.BoundTaxCertificate);
            var totalUserTaxCount = userTaxCount + userBoundTaxCount;

            if (totalCertsCost > totalUserTaxCount)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
                return false;
            }

            // Bound certificates first, so the player keeps the tradeable ones.
            var consumedCerts = totalCertsCost;
            var c = consumedCerts;
            if (userBoundTaxCount > 0 && c > 0)
            {
                if (c > userBoundTaxCount)
                    c = userBoundTaxCount;
                connection.ActiveChar.Inventory.Bag.ConsumeItem(ItemTaskType.HouseCreation, Item.BoundTaxCertificate, c, null);
                consumedCerts -= c;
            }

            c = consumedCerts;
            if (userTaxCount > 0 && c > 0)
            {
                if (c > userTaxCount)
                    c = userTaxCount;
                connection.ActiveChar.Inventory.Bag.ConsumeItem(ItemTaskType.HouseCreation, Item.TaxCertificate, c, null);
                consumedCerts -= c;
            }

            if (consumedCerts != 0)
                Logger.Error($"Something went wrong when paying tax for new building for player {connection.ActiveChar.Name}");

            return true;
        }

        if (totalTaxAmountDue > connection.ActiveChar.Money)
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.MailNotEnoughMoneyToPayTaxes);
            return false;
        }

        connection.ActiveChar.SubtractMoney(SlotType.Inventory, totalTaxAmountDue, ItemTaskType.HouseCreation);
        return true;
    }

    /// <summary>
    /// Turns an already placed building.
    /// </summary>
    /// <remarks>
    /// The client blocks this locally for a building the player does not own, or is standing
    /// too far from, so a request that reaches here has already passed those on its side. That
    /// is a reason not to expect refusals, not a reason to skip checking.
    ///
    /// Turning a building is not free: the design names the certificate it costs and how many.
    /// Some designs name none, and those turn for nothing.
    /// </remarks>
    public void RotateHouse(GameConnection connection, uint objId, float zRot, float height)
    {
        var character = connection?.ActiveChar;
        if (character == null)
            return;

        House house = null;
        foreach (var candidate in _houses.Values)
        {
            if (candidate.ObjId != objId)
                continue;
            house = candidate;
            break;
        }

        if (house == null || house.OwnerId != character.Id)
        {
            character.SendErrorMessage(ErrorMessageType.HouseCannotRotate);
            return;
        }

        if (!ChargeRotationCost(character, house))
            return;

        house.Transform.Local.SetZRotation(zRot);
        house.IsDirty = true;

        house.BroadcastPacket(new SCHouseRotatedPacket(house.ObjId, zRot), true);
    }

    /// <summary>
    /// Takes what turning a building costs.
    /// </summary>
    /// <remarks>
    /// The design carries both halves of the price. A design that names no item, or names one but
    /// asks for none of it, turns for free - both are common in the shipped data.
    /// </remarks>
    /// <returns>False when the player cannot pay; they have already been told.</returns>
    private static bool ChargeRotationCost(Character character, House house)
    {
        var itemId = house.Template?.RotateItemId ?? 0;
        var count = house.Template?.RotateItemCount ?? 0;
        if (itemId == 0 || count <= 0)
            return true;

        if (!character.Inventory.CheckItems(SlotType.Inventory, itemId, count))
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        if (character.Inventory.Bag.ConsumeItem(ItemTaskType.HouseBuilding, itemId, count, null) > 0)
            return true;

        Logger.Error($"RotateHouse: failed to take {count} of item {itemId} from {character.Name}");
        character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
        return false;
    }

    /// <summary>
    /// Finds the rebuild offered by a given skill that produces a given design.
    /// </summary>
    /// <remarks>
    /// A rebuild is identified by that pair rather than by the target design alone: several
    /// skills can lead to the same design from different starting buildings.
    /// </remarks>
    /// <returns>The rebuild id, or 0 when the skill does not offer that design.</returns>
    public uint GetHousingRebuildingId(uint skillId, uint housingId)
    {
        foreach (var rebuilding in _housingRebuildings.Values)
        {
            if (rebuilding.SkillId == skillId && rebuilding.HousingId == housingId)
                return rebuilding.Id;
        }

        return 0;
    }

    /// <summary>
    /// Materials a rebuild consumes. Empty when the rebuild is unknown or free.
    /// </summary>
    public IReadOnlyList<HousingRebuildingMaterial> GetMaterialsByHousingRebuildingId(uint housingRebuildingId)
    {
        return _housingRebuildingMaterials.TryGetValue(housingRebuildingId, out var materials)
            ? materials
            : Array.Empty<HousingRebuildingMaterial>();
    }

    /// <summary>
    /// Removes the old building immediately before a rebuild puts a new one in its place.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Demolish"/>: an ordinary demolition expires the protection,
    /// rewrites the tax state and mails the contents back to the owner. None of that is wanted
    /// here, because the building is being replaced rather than lost and the replacement
    /// carries its own fresh tax period. Doing both would bill the player twice and post them
    /// a refund for a house they still have.
    /// </remarks>
    public void DemolishBeforeRebuilding(GameConnection connection, House house)
    {
        if (house == null || !_houses.ContainsKey(house.Id))
        {
            connection?.ActiveChar?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return;
        }

        if (connection != null && house.OwnerId != connection.ActiveChar.Id)
        {
            connection.ActiveChar?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return;
        }

        var ownerChar = WorldManager.Instance.GetCharacterById(house.OwnerId);

        house.OwnerId = 0;
        house.CoOwnerId = 0;
        house.AccountId = 0;
        house.SellPrice = 0;
        house.SellToPlayerId = 0;
        house.Permission = HousingPermission.Public;
        house.IsDirty = true;

        ownerChar?.SendPacket(new SCHouseRemovedPacket(house.TlId));

        _removedHousings.Add(house.Id);
        RemoveDeadHouse(house);
    }

    /// <summary>
    /// Replaces an existing building with another design at the same spot.
    /// </summary>
    /// <remarks>
    /// The caller is expected to have checked and consumed the rebuild materials and to have
    /// called <see cref="DemolishBeforeRebuilding"/> first. Tax is charged the same way a new
    /// placement charges it, because the replacement starts a fresh protection period.
    ///
    /// No design item is consumed here - a rebuild is paid for in materials, not in a design.
    /// </remarks>
    /// <param name="oldHouseName">Carried over so the building keeps its name across the swap.</param>
    public House Rebuild(GameConnection connection, uint designId, float posX, float posY, float posZ, float zRot,
        string oldHouseName)
    {
        if (connection?.ActiveChar == null)
            return null;

        if (!_housingTemplates.TryGetValue(designId, out var houseTemplate))
        {
            connection.ActiveChar.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return null;
        }

        // The land is not checked here. By the time a rebuild reaches this point the old building
        // has already been torn down and the materials are already spent, so a refusal would
        // leave the player with neither. The check belongs before any of that - see
        // <see cref="CanPlaceDesign"/> and its caller.
        CalculateBuildingTaxInfo(connection.ActiveChar.AccountId, houseTemplate, true, out var totalTaxAmountDue, out _, out _, out _, out _);

        if (!ChargePlacementTax(connection, houseTemplate, totalTaxAmountDue))
            return null;

        var house = Create(designId, connection.ActiveChar.Faction.Id);
        house.Id = HousingIdManager.Instance.GetNextId();
        house.Transform.Local.SetPosition(posX, posY, posZ);
        house.Transform.Local.SetZRotation(zRot);

        if (!string.IsNullOrEmpty(oldHouseName))
            house.Name = oldHouseName;

        house.OwnerId = connection.ActiveChar.Id;
        house.CoOwnerId = connection.ActiveChar.Id;
        house.AccountId = connection.AccountId;
        house.Ht = 0;
        house.Permission = HousingPermission.Private;
        house.AllowRecover = true;
        house.PlaceDate = DateTime.UtcNow;
        house.ProtectionEndDate = DateTime.UtcNow.AddDays(TaxPaysForDays * 2);

        house.CurrentStep = house.Template.FirstBuildStep;

        _houses.Add(house.Id, house);
        _housesTl.Add(house.TlId, house);

        // Same ordering as a fresh placement: the scene object and its state come from Spawn,
        // and the build progress follows once the object exists.
        house.Spawn();

        connection.ActiveChar.SendPacket(new SCHouseDataPacket(house));

        connection.ActiveChar.SendPacket(new SCHouseBuildProgressPacket(
            house.TlId,
            house.ModelId,
            house.AllAction,
            house.CurrentStep == -1 ? house.AllAction : house.CurrentAction));

        UpdateTaxInfo(house);

        return house;
    }

    /// <summary>
    /// Start demolishing of a house
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="house"></param>
    /// <param name="failedToPayTax"></param>
    /// <param name="forceRestoreAllDecor"></param>
    public void Demolish(GameConnection connection, House house, bool failedToPayTax, bool forceRestoreAllDecor)
    {
        if (!_houses.ContainsKey(house.Id))
        {
            connection?.ActiveChar?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return;
        }
        // Check if owner
        if (connection is null || house.OwnerId == connection.ActiveChar.Id)
        {
            // VERIFY: check if tax paid, cannot manually demolish or sell a house with unpaid taxes ?
            // Note - ZeromusXYZ: I'm disabling this "feature", as it would prevent you from demolishing freshly placed buildings that you want to move 
            /*
            if (house.TaxDueDate <= DateTime.UtcNow)
            {
                connection.ActiveChar.SendErrorMessage(ErrorMessageType.HouseCannotDemolishUnpaidTax);
                return;
            }
            */
            var ownerChar = WorldManager.Instance.GetCharacterById(house.OwnerId);

            // Mark it as expired protection
            house.ProtectionEndDate = DateTime.UtcNow.AddSeconds(-1);
            // Make sure to call UpdateTaxInfo first to remove tax-rated mails of this house
            UpdateTaxInfo(house);
            // Return items to player by mail
            ReturnHouseItemsToOwner(house, failedToPayTax, forceRestoreAllDecor, null);

            // Remove owner
            house.OwnerId = 0;
            house.CoOwnerId = 0;
            house.AccountId = 0;
            house.SellPrice = 0;
            house.SellToPlayerId = 0;
            house.Permission = HousingPermission.Public;
            house.BroadcastPacket(new SCHouseDemolishedPacket(house.TlId), false);

            ownerChar?.SendPacket(new SCHouseRemovedPacket(house.TlId));
            // Make killable
            UpdateHouseFaction(house, FactionsEnum.Monstrosity);

            SetForSaleMarkers(house, false);

            house.IsDirty = true;

            // TODO: better house killing handling
            _removedHousings.Add(house.Id);
        }
        else
        {
            // Non-owner should not be able to press demolish
            connection.ActiveChar?.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
        }
    }

    /// <summary>
    /// Fully removes a house from the world
    /// </summary>
    /// <param name="house"></param>
    public void RemoveDeadHouse(House house)
    {
        // Remove house from housing tables
        _removedHousings.Add(house.Id);
        _houses.Remove(house.Id);
        _housesTl.Remove(house.TlId);
        HousingTldManager.Instance.ReleaseId(house.TlId);
        HousingIdManager.Instance.ReleaseId(house.Id);
        // TODO: not sure how to handle this, just instant delete it for now
        house.Delete();
        // TODO: Add to despawn handler
        //house.Despawn = DateTime.UtcNow.AddSeconds(20);
        //SpawnManager.Instance.AddDespawn(house);
    }

    /// <summary>
    /// Helper function to calculate due tax
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="newHouseTemplate"></param>
    /// <param name="buildingNewHouse"></param>
    /// <param name="totalTaxToPay"></param>
    /// <param name="heavyHouseCount"></param>
    /// <param name="normalHouseCount"></param>
    /// <param name="hostileTaxRate"></param>
    /// <param name="oneWeekTaxCount"></param>
    /// <returns></returns>
    /// <summary>How much the tax is multiplied by for someone who owns this many heavily taxed buildings.</summary>
    /// <remarks>
    /// Read from the design data's own threshold table: the highest row whose count the owner has
    /// reached. A row of zero means no surcharge, which is what the first two rows carry, so the
    /// answer stays at one until the third building.
    /// </remarks>
    private static float GetHeavyTaxMultiplier(int heavyHouseCount)
    {
        var surcharge = 0f;
        foreach (var row in HeavyTaxes)
        {
            if (row.Key > heavyHouseCount)
                break;
            surcharge = row.Value;
        }

        // The table's number is what is added, not what the tax is multiplied by: the two on the
        // third building means two hundred percent on top, so three times over, and the five the
        // table settles at means six.
        return 1f + surcharge;
    }

    /// <summary>How many tax certificates an amount of tax money costs, for this design's own rate.</summary>
    /// <remarks>
    /// The design names both what one period costs in money and what it costs in certificates, and
    /// the two are authored independently: a hundred thousand buys one certificate on one design,
    /// a hundred and fifty thousand buys two on another, three million buys one. No single divisor
    /// fits, which is why the three that were invented for this - ten thousand, five thousand and
    /// a million - could not all have been right, and were not.
    ///
    /// So the certificates are counted in periods: what the money comes to, over what one period
    /// costs, times what one period costs in certificates.
    ///
    /// This is our reading of the data and not something anyone has found in the client. What is
    /// established is only that buildings are paid for in certificates rather than coin, and that
    /// two other kinds of building divide the count again by numbers we do not have. Until those
    /// turn up this is what we charge, and it is at least built from the design's own figures
    /// rather than a constant somebody liked.
    /// </remarks>
    private static int CertificatesFor(HousingTemplate template, int taxAmount)
    {
        var tax = template.Taxation?.Tax ?? 0;
        var seals = template.Taxation?.SealCount ?? 0;
        if (tax == 0 || seals == 0 || taxAmount <= 0)
            return 0;

        var periods = (double)taxAmount / tax;
        return (int)Math.Ceiling(periods * seals);
    }

    public bool CalculateBuildingTaxInfo(ulong accountId, HousingTemplate newHouseTemplate, bool buildingNewHouse, out int totalTaxToPay, out int heavyHouseCount, out int normalHouseCount, out int hostileTaxRate, out int oneWeekTaxCount)
    {
        totalTaxToPay = 0;
        heavyHouseCount = 0;
        normalHouseCount = 0;
        hostileTaxRate = 0; // NOTE: When castles are added, this needs to be updated depending on ruling guild's settings
        oneWeekTaxCount = 0;

        if (newHouseTemplate?.Taxation == null)
        {
            Logger.Warn("CalculateBuildingTaxInfo: design {0} has no taxation row", newHouseTemplate?.Id ?? 0);
            return false;
        }

        var userHouses = new Dictionary<uint, House>();
        GetByAccountId(userHouses, accountId);

        // Count the houses on this account. An empty collection is valid: this is the first house.
        foreach (var h in userHouses)
        {
            if (h.Value.Template.HeavyTax)
                heavyHouseCount++;
            else
                normalHouseCount++;
        }

        // If this is for a new building, add 1 to count
        if (buildingNewHouse)
        {
            if (newHouseTemplate.HeavyTax)
                heavyHouseCount++;
            else
                normalHouseCount++;
        }

        // The surcharge for owning several heavily taxed buildings is a table, not a formula: each
        // row names a number of buildings and the multiplier that applies from there up, and the
        // highest row the count reaches wins. The first two rows carry no surcharge at all, which
        // is why nothing happens until the third building.
        //
        // What stood here was `min(count, 10) * 0.5`, whose origin nobody recorded and which agrees
        // with the table nowhere: at three buildings it charged one and a half times where the
        // table asks for two.
        var taxMultiplier = newHouseTemplate.HeavyTax ? GetHeavyTaxMultiplier(heavyHouseCount) : 1f;

        totalTaxToPay = oneWeekTaxCount = (int)Math.Ceiling(newHouseTemplate.Taxation.Tax * taxMultiplier);

        // If this is a new house, add the deposit (base tax * 2)
        if (buildingNewHouse)
            totalTaxToPay += (int)(newHouseTemplate.Taxation.Tax * 2);

        return true;
    }

    /// <summary>
    /// This function updates related tax mails of a house (if needed)
    /// </summary>
    /// <param name="house"></param>
    public static void UpdateTaxInfo(House house)
    {
        var isDemolished = (house.ProtectionEndDate <= DateTime.UtcNow);
        var isTaxDue = (house.TaxDueDate <= DateTime.UtcNow);

        // Update Buffs (if needed)
        SetUntouchable(house, !isDemolished);
        SetRemovalDebuff(house, isDemolished);

        if (house.OwnerId <= 0)
            return;

        // If expired, start demolition debuffs
        if (isDemolished)
        {
            MailManager.Instance.DeleteHouseMails(house.Id);
        }
        else
        if (isTaxDue)
        {
            // TODO: update corresponding mails if needed (like update weeks unpaid etc)
            var allMails = MailManager.Instance.GetMyHouseMails(house.Id);

            if (allMails.Count <= 0)
            {
                // Create new tax mail
                var newMail = new MailForTax(house);
                newMail.FinalizeMail();
                newMail.Send();
                Logger.Trace($"New Tax Mail sent for {house.Name} owned by {house.OwnerId}");
            }
            else
            {
                foreach (var mail in allMails)
                {
                    MailForTax.UpdateTaxInfo(mail, house);
                    Logger.Trace($"Tax Mail {mail.Id} updated for {house.Name} ({house.Id}) owned by {house.OwnerId}");
                }
            }
        }
    }

    /// <summary>
    /// Adds a week to the protection end date (pay 1 week's tax)
    /// </summary>
    /// <param name="house"></param>
    /// <returns></returns>
    public static bool PayWeeklyTax(House house)
    {
        house.ProtectionEndDate = house.ProtectionEndDate.AddDays(TaxPaysForDays);
        return true;
    }

    /// <summary>
    /// Get house by DB Id
    /// </summary>
    /// <param name="houseId"></param>
    /// <returns></returns>
    public House GetHouseById(uint houseId)
    {
        return _houses.GetValueOrDefault(houseId);
    }

    /// <summary>
    /// Get house by TlId
    /// </summary>
    /// <param name="houseTlId"></param>
    /// <returns></returns>
    private House GetHouseByTlId(ushort houseTlId)
    {
        return _housesTl.GetValueOrDefault(houseTlId);
    }

    /// <summary>
    /// Changes the faction of the house
    /// </summary>
    /// <param name="house"></param>
    /// <param name="factionId"></param>
    private static void UpdateHouseFaction(House house, uint factionId)
    {
        house.BroadcastPacket(new SCUnitFactionChangedPacket(house.ObjId, house.Name, house.Faction?.Id ?? 0, factionId, false), true);
        house.Faction = FactionManager.Instance.GetFaction(factionId);
    }

    /// <summary>
    /// Helper function for when the owning character changes faction
    /// </summary>
    /// <param name="characterId"></param>
    /// <param name="factionId"></param>
    public void UpdateOwnedHousingFaction(uint characterId, uint factionId)
    {
        // TODO: Does this also need to be done when temporary changing factions? (like arena)
        var myHouses = new Dictionary<uint, House>();
        GetByCharacterId(myHouses, characterId);
        foreach (var h in myHouses)
            if ((h.Value.Faction == null) || (h.Value.Faction.Id != factionId))
                UpdateHouseFaction(h.Value, factionId);
    }

    /// <summary>
    /// Returns furniture of a house that's being demolished or sold
    /// </summary>
    /// <param name="house"></param>
    /// <param name="failedToPayTax">Set true if demilishing due to failed tax, this adds a delay to the mail</param>
    /// <param name="forceRestoreAllDecor">For GM commands or server merges. Will try to send ALL placed furniture if set to true, even those that normally don't get returned.</param>
    /// <param name="newOwner">New owner Character if buying, otherwise leave null</param>
    private void ReturnHouseItemsToOwner(House house, bool failedToPayTax, bool forceRestoreAllDecor, ICharacter newOwner)
    {
        if (house.OwnerId <= 0)
            return;

        var returnedItems = new List<Item>();
        var returnedMoney = 0;

        // If returning items because of a new House Owner, then don't include the design
        if (newOwner == null)
        {
            // TODO: proper grades for design
            // TODO for future versions: Support Full-Kit demolition
            var designItemId = GetItemIdByDesign(house.Template.Id);
            var designItem = ItemManager.Instance.Create(designItemId, 1, 0);
            var designTemplate = ItemManager.Instance.GetTemplate(designItemId);
            if (designTemplate != null && designItem != null)
            {
                designItem.Grade = (designTemplate.FixedGrade >= 0) ? (byte)designTemplate.FixedGrade : (byte)0;
                designItem.OwnerId = house.OwnerId;
                designItem.SlotType = SlotType.Mail;
                returnedItems.Add(designItem);
            }

            // Return taxes
            if (!failedToPayTax)
            {
                // What was paid for one period comes back, counted the design's own way rather
                // than through a divisor somebody picked.
                var refundedCertificates = CertificatesFor(house.Template, (int)(house.Template.Taxation?.Tax ?? 0));
                if (refundedCertificates > 0)
                {
                    var taxItem = ItemManager.Instance.Create(Item.BoundTaxCertificate, refundedCertificates, 0);
                    taxItem.OwnerId = house.OwnerId;
                    taxItem.SlotType = SlotType.Mail;
                    returnedItems.Add(taxItem);
                }
                else
                {
                    returnedMoney = (int)(house.Template.Taxation.Tax * 2);
                }
            }
        }

        var furniture = WorldManager.Instance.GetDoodadByHouseDbId(house.Id);
        foreach (var f in furniture)
        {
            // Ignore attached objects (those are doors/windows etc)
            if (f.AttachPoint != AttachPointKind.None)
                continue;

            // Ignore for sale signs
            if (f.TemplateId == ForSaleMarkerDoodadId)
                continue;

            var decoDesign = GetDecorationDesignFromDoodadId(f.TemplateId);
            if (decoDesign == null)
            {
                // Is not furniture, probably plants or backpacks
                f.Transform.DetachAll();
                f.ParentObjId = 0;
                f.ParentObj = null;
                f.OwnerDbId = 0;
                // TODO: probably needs to send a packet as well here
                continue;
            }

            var decoInfo = _housingItemHousingDecorations.FirstOrDefault(x => x.DesignId == decoDesign.Id);
            if (decoInfo == null)
            {
                // No design info for this item ? Just detach it for now
                f.Transform.DetachAll();
                f.ParentObjId = 0;
                f.ParentObj = null;
                f.OwnerDbId = 0;
                Logger.Warn($"ReturnHouseItemsToOwner - Furniture doesn't have design info for Doodad Id:{f.ObjId} Template:{f.TemplateId}");
                continue;
            }

            var thisDoodadsItem = ItemManager.Instance.GetItemByItemId(f.ItemId);
            var returnedThisItem = false;

            var wantReturned = ((newOwner == null) && decoInfo.Restore) || forceRestoreAllDecor;

            // If item is bound, always return it owner
            if (f.ItemId > 0)
            {
                var item = ItemManager.Instance.GetItemByItemId(f.ItemId);
                if (item.ItemFlags.HasFlag(ItemFlag.SoulBound))
                    wantReturned = true;
            }

            // If this doodad is a Coffer and has a ItemContainer attached, also return all item of that container
            if ((f is DoodadCoffer coffer) && (f.GetItemContainerId() > 0))
            {
                // TODO: Check if items should stay in the coffer when house is sold.
                // Move it to new owner's SystemContainer first so they don't get destroyed
                var ownerSystemContainer = ItemManager.Instance.GetItemContainerForCharacter(house.OwnerId, SlotType.System);
                for (var i = coffer.ItemContainer.Items.Count - 1; i >= 0; i--)
                {
                    var cofferItem = coffer.ItemContainer.Items[i];
                    //if (cofferItem.HasFlag(ItemFlag.SoulBound) || forceRestoreAllDecor)
                    {
                        ownerSystemContainer?.AddOrMoveExistingItem(ItemTaskType.Invalid, cofferItem);
                        returnedItems.Add(cofferItem);
                    }
                }
            }

            // If the decoration item isn't marked as Restore, then just delete it (and it's possibly attached item)
            if (!wantReturned)
            {
                // Non-restore-able item
                if (newOwner == null)
                {
                    // Just delete the doodad and attached item if no new owner
                    // Delete the attached item
                    if (f.ItemId != 0)
                        thisDoodadsItem._holdingContainer?.ConsumeItem(ItemTaskType.Invalid,
                            thisDoodadsItem.TemplateId, thisDoodadsItem.Count, thisDoodadsItem);

                    // Is furniture, but doesn't restore, destroy it
                    f.Transform.DetachAll();
                    f.ItemId = 0;
                    f.Delete();
                }
                else
                {
                    // Move the doodad and item to the new owner
                    if (f.ItemId != 0)
                    {
                        // If a single item is attached, change it's owner and location
                        var item = ItemManager.Instance.GetItemByItemId(f.ItemId);
                        newOwner.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.Invalid, item);
                    }
                    // Change doodad owner
                    f.OwnerId = newOwner.Id;
                }

                continue;
            }

            // Item needs to be actually returned, so let's do that
            if (f.ItemId > 0)
            {
                // Ignore if it's not in a System container for whatever reason
                if (thisDoodadsItem is { SlotType: SlotType.System })
                {
                    returnedItems.Add(thisDoodadsItem);
                    returnedThisItem = true;
                    f.ItemId = 0; // don't auto-delete
                }
            }
            else
            if (f.ItemTemplateId > 0)
            {
                // try to stack stackable items
                var oldItem = returnedItems.FirstOrDefault(x => (x.TemplateId == f.ItemTemplateId) && (x.Count < x.Template.MaxCount));

                if (oldItem != null)
                {
                    oldItem.Count++;
                }
                else
                {
                    // It's a new one, add an item slot
                    var furnitureItem = ItemManager.Instance.Create(f.ItemTemplateId, 1, 0);
                    var furnitureTemplate = ItemManager.Instance.GetTemplate(f.ItemTemplateId);
                    furnitureItem.Grade = (furnitureTemplate.FixedGrade >= 0) ? (byte)furnitureTemplate.FixedGrade : (byte)0;
                    furnitureItem.OwnerId = house.OwnerId;
                    furnitureItem.SlotType = SlotType.Mail;
                    returnedItems.Add(furnitureItem);
                }
                returnedThisItem = true;
            }
            else
            {
                // Not sure what happened here, just ignore it
                continue;
            }

            // Set new doodad owner if needed
            if (newOwner != null)
                f.OwnerId = newOwner.Id;

            if ((newOwner == null) || returnedThisItem)
            {
                f.Transform.DetachAll();
                f.Delete();
            }
        }

        // TODO: Grab a list of items in chests

        // TODO: Proper Mail handler
        BaseMail newMail = null;
        for (var i = 0; i < returnedItems.Count; i++)
        {
            // Split items into mails of maximum 10 attachments
            if ((i % 10) == 0)
            {
                // TODO: proper mail handler
                newMail = new BaseMail
                {
                    MailType = MailType.Demolish,
                    ReceiverName = NameManager.Instance.GetCharacterName(house.OwnerId), // Doesn't seem like this needs to be set
                    Header =
                    {
                        ReceiverId = house.OwnerId, 
                        SenderId = 0, 
                        SenderName = ".houseDemolish", 
                        Extra = house.Id
                    },
                    Title = "title",
                    Body = { 
                        Text = "body", // Yes, that's indeed what it needs to be set to
                        SendDate = DateTime.UtcNow,
                        RecvDate = DateTime.UtcNow.AddHours(failedToPayTax ? HoursForFailedTaxToReturnHouse : 0)
                    }
                };
            }
            // Only attach money to first mail
            if ((returnedMoney > 0) && (i == 0))
                newMail.AttachMoney(returnedMoney);

            // If player is loaded in at the moment (which he/she should be anyway), directly manipulate the inventory
            // If not, only change the container
            var onlineOwner = WorldManager.Instance.GetCharacterById((uint)returnedItems[i].OwnerId);
            if (onlineOwner != null)
                onlineOwner.Inventory.MailAttachments.AddOrMoveExistingItem(ItemTaskType.Invalid, returnedItems[i]);
            else
                returnedItems[i].SlotType = SlotType.Mail;

            // Attach item
            newMail.Body.Attachments.Add(returnedItems[i]);

            // Send on last or 10th item of the mail
            if (((i % 10) == 9) || (i == returnedItems.Count - 1))
                newMail.Send();
        }

        if (newMail != null)
        {
            Logger.Trace($"Demolition mail sent to {newMail.ReceiverName}");
        }
    }

    /// <summary>
    /// Get house design by item template
    /// </summary>
    /// <param name="itemId"></param>
    /// <returns></returns>
    private uint GetDesignByItemId(uint itemId)
    {
        var design = _housingItemHousings.FirstOrDefault(h => h.Item_Id == itemId);
        return design?.Design_Id ?? 0;
    }

    /// <summary>
    /// Get original item template based on house design
    /// </summary>
    /// <param name="designId"></param>
    /// <returns></returns>
    private uint GetItemIdByDesign(uint designId)
    {
        var design = _housingItemHousings.FirstOrDefault(h => h.Design_Id == designId);
        return design?.Item_Id ?? 0;
    }

    /// <summary>
    /// Helper function to calculate how many Appraisal Certificates are needed to sell a house at a given price
    /// </summary>
    /// <param name="house"></param>
    /// <param name="salePrice"></param>
    /// <returns></returns>
    private static int CalculateSaleCertifcates(House house, uint salePrice)
    {
        // NOTE: In earlier AA, you need 1 appraisal certificate for every 100 gold of sales price
        // TODO: In later versions, this depends on the building-type/size
        var certAmount = (int)Math.Ceiling(salePrice / CopperPerCertificate);
        if (certAmount < 1)
            certAmount = 1;
        return certAmount;
    }

    /// <summary>
    /// Sets or removes For Sale Signs on the property
    /// </summary>
    /// <param name="house"></param>
    /// <param name="isForSale"></param>
    private static void SetForSaleMarkers(House house, bool isForSale)
    {
        if (isForSale)
        {
            for (var postId = 0; postId < 4; postId++)
            {
                var xMultiplier = (postId % 2) == 0 ? -1 : 1f;
                var yMultiplier = (postId / 2) == 0 ? -1 : 1f;
                var zRot = ((135f + (90f * postId) % 360)).DegToRad();

                var doodad = DoodadManager.Instance.Create(0, ForSaleMarkerDoodadId, null, true);
                // location
                doodad.Transform.Local.SetPosition(
                    (house.Template.GardenRadius * xMultiplier) + house.Transform.World.Position.X,
                    (house.Template.GardenRadius * yMultiplier) + house.Transform.World.Position.Y,
                    +house.Transform.World.Position.Z);
                // adjust height to the floor
                doodad.Transform.Local.SetHeight(WorldManager.Instance.GetHeight(doodad.Transform));
                doodad.Transform.Local.SetZRotation(zRot);
                doodad.ItemTemplateId = 0; // designId;
                doodad.ItemId = 0;
                doodad.OwnerId = 0;
                doodad.ParentObjId = 0;
                doodad.ParentObj = null;
                doodad.UccId = 0;
                doodad.AttachPoint = AttachPointKind.None;
                doodad.OwnerType = DoodadOwnerType.Housing;
                doodad.OwnerDbId = house.Id;
                doodad.InitDoodad();

                doodad.Spawn();
            }
        }
        else
        {
            // Get all doodads related to this house
            var thisHouseSalePosts = WorldManager.Instance.GetDoodadByHouseDbId(house.Id);
            for (var c = thisHouseSalePosts.Count - 1; c >= 0; c--)
            {
                var doodad = thisHouseSalePosts[c];
                // If it's a for sale sign, remove it
                if (doodad.TemplateId == ForSaleMarkerDoodadId)
                {
                    house.AttachedDoodads.Remove(doodad);
                    doodad.Delete();
                }
            }
        }
    }

    /// <summary>
    /// Puts up a house for sale
    /// </summary>
    /// <param name="house"></param>
    /// <param name="price"></param>
    /// <param name="buyerId">Use CharacterId for selling to a specific person</param>
    /// <param name="seller">Current owner of the property (needed to manipulate inventory)</param>
    /// <returns></returns>
    public static bool SetForSale(House house, uint price, uint buyerId, Character seller)
    {
        if (house == null)
            return false;

        if (!house.Template.IsSellable)
            return false;

        // Check if buyer exists (we just check if the name exists)
        var buyerName = NameManager.Instance.GetCharacterName(buyerId);
        if ((buyerId != 0) && (buyerName == null))
            return false;

        buyerName ??= "";

        // Using the GM command does not send the seller (uses null), and thus will not require certificates
        if (seller != null)
        {
            var certAmount = CalculateSaleCertifcates(house, price);
            if (seller.Inventory.Bag.ConsumeItem(ItemTaskType.BuyHouse, Item.AppraisalCertificate, certAmount, null) != certAmount)
            {
                seller.SendErrorMessage(ErrorMessageType.HouseCannotSellAsNotEnoughSeal);
                return false;
            }
        }

        house.SellPrice = price;
        house.SellToPlayerId = buyerId;

        house.BroadcastPacket(new SCHouseSetForSalePacket(house.TlId, price, house.SellToPlayerId, buyerName, house.Name), false);
        SetForSaleMarkers(house, true);

        return true;
    }

    public bool SetForSale(ushort houseTlId, uint price, uint buyerId, Character seller) => SetForSale(GetHouseByTlId(houseTlId), price, buyerId, seller);

    /// <summary>
    /// Cancels a sale
    /// </summary>
    /// <param name="house"></param>
    /// <param name="returnCertificates"></param>
    /// <returns></returns>
    public static bool CancelForSale(House house, bool returnCertificates = true)
    {
        if (house.SellPrice <= 0)
            return true;
        var certAmount = CalculateSaleCertifcates(house, house.SellPrice);
        var owner = WorldManager.Instance.GetCharacterById(house.OwnerId);

        house.SellPrice = 0;
        house.SellToPlayerId = 0;
        // Can only return certificates if owner is online and is the one resetting the sale
        if ((certAmount > 0) && (returnCertificates) && (owner != null))
        {
            if (owner.Inventory.MailAttachments.AcquireDefaultItemEx(ItemTaskType.Invalid,
                Item.AppraisalCertificate, certAmount, -1, out var addedItems, out _, 0))
            {
                // Mail container is set up to never update existing items, so we can discard that result
                var mail = new BaseMail
                {
                    MailType = MailType.HousingSale, 
                    Header =
                    {
                        ReceiverId = house.OwnerId, 
                        SenderName = ".houseSellCancel"
                    }, 
                    ReceiverName = NameManager.Instance.GetCharacterName(house.OwnerId),
                    Title = "title(" + ZoneManager.Instance.GetZoneByKey(house.Transform.ZoneId)?.GroupId.ToString() + ",'" + house.Name + "')",
                    Body =
                    {
                        Text = "body('" + house.Name + "', " + Item.AppraisalCertificate.ToString() + ", " + certAmount.ToString() + ")"
                    }
                };
                mail.Body.Attachments.AddRange(addedItems);
                mail.Body.SendDate = DateTime.UtcNow;
                mail.Body.RecvDate = DateTime.UtcNow.AddMilliseconds(1);
                mail.Send();
            }
            else
            {
                // Failed to create Appraisal certificate ?
                Logger.Warn("CancelForSale - Failed to create Appraisal Certificates for mail");
                return false;
            }
        }

        house.BroadcastPacket(new SCHouseResetForSalePacket(house.TlId, house.Name), false);
        SetForSaleMarkers(house, false);

        return true;
    }

    public bool CancelForSale(ushort houseTlId, bool returnCertificates = true) => CancelForSale(GetHouseByTlId(houseTlId), returnCertificates);

    /// <summary>
    /// Updates all furniture on the house to a new owner and broadcasts packets for it
    /// </summary>
    /// <param name="house"></param>
    /// <param name="characterId"></param>
    /// <returns>The number of items that have their owner information updated</returns>
    private static uint UpdateFurnitureOwner(House house, uint characterId)
    {
        uint res = 0;
        var furnitureList = WorldManager.Instance.GetDoodadByHouseDbId(house.Id);
        foreach (var furniture in furnitureList)
        {
            if (furniture.AttachPoint != AttachPointKind.None)
                continue;
            furniture.OwnerId = characterId;
            furniture.Save();
            furniture.BroadcastPacket(new SCDoodadOriginatorPacket(furniture.ObjId, characterId, 0), true);
            res++;
        }
        return res;
    }

    /// <summary>
    /// Buys the house using money amount
    /// </summary>
    /// <param name="houseTlId"></param>
    /// <param name="money"></param>
    /// <param name="character"></param>
    /// <returns>Returns true if successful</returns>
    public bool BuyHouse(ushort houseTlId, uint money, Character character)
    {
        var house = GetHouseByTlId(houseTlId);

        if (house == null)
        {
            // Invalid house
            character.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }

        if (house.SellPrice <= 0)
        {
            // House wasn't for sale
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotForSale);
            return false;
        }

        if (house.SellPrice != money)
        {
            // House price changed
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsSaleInfoChanged);
            return false;
        }

        if ((house.SellToPlayerId != 0) && (house.SellToPlayerId != character.Id))
        {
            // Not a valid buyer
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotDesignatedBuyer);
            return false;
        }

        if (house.OwnerId == character.Id)
        {
            // Cannot buy own building
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsOwner);
            return false;
        }

        // NOTE: check tax due maybe ?

        if (!character.SubtractMoney(SlotType.Inventory, (int)house.SellPrice, ItemTaskType.BuyHouse))
        {
            // Not enough money
            character.SendErrorMessage(ErrorMessageType.HouseCannotBuyAsNotEnoughMoney);
            return false;
        }

        var previousOwner = house.OwnerId;
        var previousOwnerName = NameManager.Instance.GetCharacterName(previousOwner);

        // Mail confirmation mail to new owner
        var newOwnerMail = new BaseMail
        {
            MailType = MailType.HousingSale, 
            Header =
            {
                ReceiverId = character.Id, 
                SenderName = ".houseBought"
            }, 
            ReceiverName = character.Name,
            Title = "title(" + ZoneManager.Instance.GetZoneByKey(house.Transform.ZoneId)?.GroupId.ToString() + ",'" + house.Name + "')",
            Body =
            {
                Text = "body('" + previousOwnerName + "', '" + house.Name + "', " + house.SellPrice.ToString() + ")",
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow.AddMilliseconds(1)
            }
        };
        newOwnerMail.Send();

        // Send sales money to previous owner
        var profitMail = new BaseMail
        {
            MailType = MailType.HousingSale, 
            Header =
            {
                ReceiverId = previousOwner, 
                SenderName = ".houseSold"
            }, 
            ReceiverName = previousOwnerName,
            Title = "title('" + character.Name + "','" + house.Name + "')",
            Body =
            {
                Text = "body('" + character.Name + "', '" + house.Name + "', " + house.SellPrice.ToString() + ")",
                CopperCoins = (int)house.SellPrice, // add the money
                SendDate = DateTime.UtcNow,
                RecvDate = DateTime.UtcNow.AddMilliseconds(1)
            }
        };
        profitMail.Send();

        ReturnHouseItemsToOwner(house, false, false, character);

        // Set new owner info
        house.SellPrice = 0;
        house.SellToPlayerId = 0;
        house.AccountId = character.AccountId;
        house.OwnerId = character.Id;
        house.CoOwnerId = character.Id; // not entirely sure if this actually needs to change
        house.Permission = house.Template.AlwaysPublic ? HousingPermission.Public : HousingPermission.Private;
        UpdateHouseFaction(house, character.Faction.Id);
        UpdateTaxInfo(house); // send tax due mails etc if needed ...

        // TODO: broadcast changes
        house.BroadcastPacket(
            new SCHouseSoldPacket(house.TlId, previousOwner, character.Id, character.AccountId, character.Name,
                house.Name), false);

        SetForSaleMarkers(house, false);

        character.SendPacket(new SCHouseDataPacket(house));
        var oldOwner = WorldManager.Instance.GetCharacterById(previousOwner);
        if ((oldOwner != null) && (oldOwner.IsOnline))
            oldOwner.SendPacket(new SCHouseRemovedPacket(house.TlId));

        UpdateFurnitureOwner(house, character.Id);

        house.IsDirty = true;

        return true;
    }

    /// <summary>
    /// Ticker function for checking all houses if they need tax mails sent
    /// </summary>
    public void CheckHousingTaxes()
    {
        if (_isCheckingTaxTiming)
            return;
        _isCheckingTaxTiming = true;
        try
        {
            // Logger.Trace("CheckHousingTaxes");
            var expiredHouseList = new List<House>();
            foreach (var house in _houses)
            {
                if ((house.Value?.ProtectionEndDate <= DateTime.UtcNow) && (house.Value?.OwnerId > 0))
                    expiredHouseList.Add(house.Value);
                UpdateTaxInfo(house.Value);
            }
            foreach (var house in expiredHouseList)
            {
                Demolish(null, house, true, false);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e);
        }

        _isCheckingTaxTiming = false;
    }

    /// <summary>
    /// Get decoration design by Id
    /// </summary>
    /// <param name="designId"></param>
    /// <returns></returns>
    private HousingDecoration GetDecorationDesignFromId(uint designId)
    {
        return _housingDecorations.GetValueOrDefault(designId);
    }

    /// <summary>
    /// Get decoration design from it's doodad counterpart
    /// </summary>
    /// <param name="doodadId"></param>
    /// <returns></returns>
    private HousingDecoration GetDecorationDesignFromDoodadId(uint doodadId)
    {
        var deco = _housingDecorations.FirstOrDefault(x => x.Value.DoodadId == doodadId).Value;
        return default ? null : deco;
    }

    /// <summary>
    /// Whether the building will take one more piece of furniture of this kind.
    /// </summary>
    /// <remarks>
    /// Furniture is limited twice over: by how much a building holds altogether, and by how much of
    /// each kind it holds - so many chairs, so many trees, so many of whatever else the design
    /// groups together. The second limit is per group and the client enforces it from its own data
    /// without being told, so the server has to reach the same answer from the same three tables or
    /// the two will disagree about what fits.
    ///
    /// The extra room a player can buy applies to the building's total and does not raise any of
    /// the per-group limits.
    ///
    /// A design with no limit list, or a kind with no row in it, is left alone: silence there means
    /// no limit rather than a limit of none.
    /// </remarks>
    private bool HasRoomForDecorationGroup(House house, HousingDecoration decoration)
    {
        var limitId = house?.Template?.HousingDecoLimitId ?? 0;
        var groupId = decoration?.DecoActAbilityGroupId ?? 0;
        if (limitId == 0 || groupId == 0)
            return true;

        if (!DecoGroupLimits.TryGetValue((limitId, groupId), out var allowed))
            return true;

        var placed = 0;
        foreach (var furniture in WorldManager.Instance.GetDoodadByHouseDbId(house.Id))
        {
            var design = GetDecorationDesignFromDoodadId(furniture.TemplateId);
            if (design != null && design.DecoActAbilityGroupId == groupId)
                placed++;
        }

        if (placed < allowed)
            return true;

        Logger.Info(
            "DecorateHouse: building {0} already holds {1} of group {2}, which is all it takes",
            house.Id, placed, groupId);
        return false;
    }

    /// <summary>
    /// Places a piece of furniture at a given location, using item and design
    /// </summary>
    /// <param name="player"></param>
    /// <param name="houseTlId"></param>
    /// <param name="designId"></param>
    /// <param name="pos"></param>
    /// <param name="quat"></param>
    /// <param name="parentObjId"></param>
    /// <param name="itemId"></param>
    /// <returns></returns>
    public bool DecorateHouse(Character player, uint houseId, uint designId, Vector3 pos, Quaternion quat, uint parentObjId, ulong itemId)
    {
        // Check Player
        if (player == null)
            return false;

        // Check Item
        var item = ItemManager.Instance.GetItemByItemId(itemId);
        if ((item == null) || (item.OwnerId != player.Id))
        {
            // Invalid Item
            return false;
        }

        // The request names the building by its own id, not by the handle the rest of this
        // subsystem is keyed on. The handle is tried as well, and loudly, because the two are
        // small numbers on a young server and were indistinguishable until now - if that line ever
        // appears in the log, the request means the handle after all and this reads it wrongly.
        var house = GetHouseById(houseId);
        if (house == null)
        {
            house = houseId <= ushort.MaxValue ? GetHouseByTlId((ushort)houseId) : null;
            if (house != null)
                Logger.Warn(
                    "DecorateHouse: no building with id {0}, but one with that handle - the request names the handle, not the id",
                    houseId);
        }

        if (house == null)
        {
            // Invalid House
            player.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
            return false;
        }

        var itemUcc = UccManager.Instance.GetUccFromItem(item);

        // Create decoration doodad
        var decorationDesign = GetDecorationDesignFromId(designId);
        if (decorationDesign == null)
        {
            Logger.Warn("DecorateHouse: unknown housing decoration design {0}", designId);
            player.SendErrorMessage(ErrorMessageType.FailedToUseItem);
            return false;
        }

        if (!HasRoomForDecorationGroup(house, decorationDesign))
        {
            player.SendErrorMessage(ErrorMessageType.HouseCannotDecorate);
            return false;
        }

        // TODO: Validate if designId is correct for the given item
        /*
        if (item.TemplateId != decorationDesign.ItemTemplateId)
        {
            player.SendErrorMessage(ErrorMessageType.FailedToUseItem);
            return false;
        }
        */

        GameObject decorationParent = house;
        if ((parentObjId != 0) && (parentObjId != house.ObjId))
        {
            var parentDoodad = WorldManager.Instance.GetDoodad(parentObjId);
            if ((parentDoodad == null) || (parentDoodad.OwnerDbId != house.Id))
            {
                Logger.Warn(
                    "DecorateHouse: invalid parent objId={0} for house={1}; parent is missing or belongs to another house",
                    parentObjId, house.Id);
                player.SendErrorMessage(ErrorMessageType.InvalidHouseInfo);
                return false;
            }

            decorationParent = parentDoodad;
        }

        var doodad = DoodadManager.Instance.Create(0, decorationDesign.DoodadId, house, true);
        if (doodad == null)
        {
            Logger.Error("DecorateHouse: doodad template {0} for design {1} is missing", decorationDesign.DoodadId,
                designId);
            player.SendErrorMessage(ErrorMessageType.FailedToUseItem);
            return false;
        }

        doodad.Transform.Parent = decorationParent.Transform;
        doodad.Transform.Local.SetPosition(pos.X, pos.Y, pos.Z);
        doodad.Transform.Local.ApplyFromQuaternion(quat);
        doodad.ItemTemplateId = item.TemplateId;
        doodad.ItemId = (item.Template.MaxCount <= 1) ? itemId : 0;
        doodad.OwnerDbId = house.Id;

        if (house.Id > 0 && item is BigFish fish)
        {
            var weight = (short)fish.Weight;
            var length = (short)fish.Length;
            doodad.Data = (length << 16) + weight;
        }

        doodad.OwnerId = player.Id;
        doodad.ParentObjId = decorationParent.ObjId;
        doodad.ParentObj = decorationParent;
        doodad.AttachPoint = AttachPointKind.None;
        doodad.OwnerType = DoodadOwnerType.Housing;
        doodad.UccId = itemUcc?.Id ?? 0;
        doodad.IsPersistent = true;

        if (doodad is DoodadCoffer coffer)
            coffer.InitializeCoffer(player.Id);

        doodad.InitDoodad();

        bool itemStored;
        if (item.Template.MaxCount > 1)
        {
            // Stackable decorations do not keep a unique item row; persist the item template instead.
            itemStored = player.Inventory.Bag.ConsumeItem(ItemTaskType.DoodadCreate, item.TemplateId, 1, item) == 1;
        }
        else
        {
            // Keep the exact item in the system container so crafter/UCC/bind information survives a restart.
            itemStored = player.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.DoodadCreate, item);
        }

        if (!itemStored)
        {
            doodad.ItemId = 0;
            doodad.ItemTemplateId = 0;
            doodad.IsPersistent = false;
            doodad.Delete();
            Logger.Warn("DecorateHouse: failed to reserve item {0} for house {1}", itemId, house.Id);
            return false;
        }

        try
        {
            // Save before publishing to the client: a visible decoration must already have a durable DB row.
            doodad.Save();
            doodad.Spawn();
        }
        catch (Exception ex)
        {
            Logger.Error(ex,
                "DecorateHouse: failed to persist decoration design={0}, doodadTemplate={1}, house={2}",
                designId, doodad.TemplateId, house.Id);

            var restored = item.Template.MaxCount > 1
                ? player.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Invalid, item.TemplateId, 1, item.Grade,
                    player.Id)
                : player.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, item);

            if (!restored)
                Logger.Error("DecorateHouse: failed to roll item {0} back to the player's bag", itemId);

            // Prevent Doodad.Delete() from deleting the item that was just restored.
            doodad.ItemId = 0;
            doodad.ItemTemplateId = 0;
            doodad.Delete();
            return false;
        }

        Logger.Info(
            "House decoration persisted: dbId={0}, objId={1}, house={2}, owner={3}, design={4}, template={5}, parentObj={6}, local=({7:F3},{8:F3},{9:F3})",
            doodad.DbId, doodad.ObjId, house.Id, player.Id, designId, doodad.TemplateId, doodad.ParentObjId,
            pos.X, pos.Y, pos.Z);
        return true;
    }

    /// <summary>
    /// Toggles the allow furniture recovery flag
    /// </summary>
    /// <param name="character"></param>
    /// <param name="houseTl"></param>
    public void HousingToggleAllowRecover(Character character, ushort houseTl)
    {
        var house = GetHouseByTlId(houseTl);
        if (house == null)
            return;
        if (character.Id != house.OwnerId)
            return;
        house.AllowRecover = !house.AllowRecover;
        house.BroadcastPacket(new SCHousingRecoverTogglePacket(house.TlId, house.AllowRecover), false);
    }

    /// <summary>
    /// Returns a house where the given position falls within boundaries of the house 
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns>Target House or Null</returns>
    public House GetHouseAtLocation(float x, float y)
    {
        // TODO: Check if all houses actually use a square shape aligned to grid
        // TODO: Add world and/or instance checks
        foreach (var h in _houses)
        {
            var house = h.Value;
            var r = house.Template.GardenRadius;
            var bounds = new RectangleF(house.Transform.World.Position.X - r, house.Transform.World.Position.Y - r,
                r * 2f, r * 2f);
            if (bounds.Contains(x, y))
                return house;
        }
        return null;
    }
}
