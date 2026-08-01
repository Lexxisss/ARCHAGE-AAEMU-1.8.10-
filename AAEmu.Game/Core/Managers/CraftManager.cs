using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class CraftManager : Singleton<CraftManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Craft> _crafts;

    public void Load()
    {
        _crafts = new Dictionary<uint, Craft>();
        Logger.Info("Loading crafts...");

        using (var connection = SQLite.CreateTargetClientConnection())
        {
            /* Crafts */
            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"SELECT
                        target.id,
                        COALESCE(target.cast_delay, fallback.cast_delay) AS cast_delay,
                        COALESCE(target.skill_id, fallback.skill_id, 0) AS skill_id,
                        COALESCE(target.wi_id, fallback.wi_id) AS wi_id,
                        COALESCE(target.req_doodad_id, fallback.req_doodad_id, 0) AS req_doodad_id,
                        COALESCE(target.actability_limit, fallback.actability_limit) AS actability_limit,
                        COALESCE(target.recommend_level, fallback.recommend_level) AS recommend_level,
                        COALESCE(target.visible_order, fallback.visible_order) AS visible_order,
                        COALESCE(target.cost, fallback.cost, 0) AS cost,
                        COALESCE(target.orderable, fallback.orderable, 0) AS orderable,
                        COALESCE(target.use_only_actability, fallback.use_only_actability, 0) AS use_only_actability,
                        COALESCE(target.products_pack_id, fallback.products_pack_id, 0) AS products_pack_id,
                        COALESCE(target.craft_c_category_id, fallback.craft_c_category_id, 0) AS craft_c_category_id,
                        COALESCE(target.craft_d_category_id, fallback.craft_d_category_id, 0) AS craft_d_category_id,
                        COALESCE(target.title, fallback.title, '') AS title
                    FROM crafts AS target
                    LEFT JOIN client_fallback.crafts AS fallback ON fallback.id = target.id
                    WHERE COALESCE(target.cast_delay, fallback.cast_delay) IS NOT NULL
                      AND COALESCE(target.wi_id, fallback.wi_id) IS NOT NULL
                      AND COALESCE(target.actability_limit, fallback.actability_limit) IS NOT NULL
                      AND COALESCE(target.recommend_level, fallback.recommend_level) IS NOT NULL
                      AND COALESCE(target.visible_order, fallback.visible_order) IS NOT NULL";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new Craft();
                        template.Id = reader.GetUInt32("id");
                        template.CastDelay = reader.GetInt32("cast_delay");
                        template.SkillId = reader.GetUInt32("skill_id", 0);
                        template.WiId = reader.GetUInt32("wi_id");
                        //template.MilestoneId = reader.GetUInt32("milestone_id", 0); // there is no such field in the database for version 3.0.3.0
                        template.ReqDoodadId = reader.GetUInt32("req_doodad_id", 0);
                        template.ActabilityLimit = reader.GetInt32("actability_limit");
                        template.RecommendLevel = reader.GetInt32("recommend_level");
                        template.VisibleOrder = reader.GetInt32("visible_order");

                        // Server-significant, not decoration: the labour a cycle costs, whether
                        // the recipe may be ordered from somebody else, and whether proficiency
                        // is the only gate. None of it was being read.
                        template.Cost = reader.GetInt32("cost", 0);
                        template.Orderable = reader.GetBoolean("orderable", true);
                        template.UseOnlyActability = reader.GetBoolean("use_only_actability", true);
                        template.ProductsPackId = reader.GetUInt32("products_pack_id", 0);
                        template.CraftCCategoryId = reader.GetUInt32("craft_c_category_id", 0);
                        template.CraftDCategoryId = reader.GetUInt32("craft_d_category_id", 0);
                        template.Title = reader.GetString("title") ?? string.Empty;

                        _crafts.Add(template.Id, template);
                    }
                }
            }

            /* Craft products (item you get at the end) */
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM craft_products";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var craftId = reader.GetUInt32("craft_id");
                        if (!_crafts.ContainsKey(craftId))
                            continue;

                        var template = new CraftProduct();
                        template.Id = reader.GetUInt32("id");
                        template.CraftId = reader.GetUInt32("craft_id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Amount = reader.GetInt32("amount", 1); //We always want to produce at least 1 item ?
                        template.Rate = reader.GetInt32("rate");
                        template.ShowLowerCrafts = false;
                        template.UseGrade = reader.GetBoolean("use_grade");
                        template.ItemGradeId = reader.GetUInt32("item_grade_id");

                        _crafts[template.CraftId].CraftProducts.Add(template);
                    }
                }
            }

            /* Craft products (item you get at the end) */
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM craft_materials";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var craftId = reader.GetUInt32("craft_id");
                        if (!_crafts.ContainsKey(craftId))
                            continue;

                        var template = new CraftMaterial();
                        template.Id = reader.GetUInt32("id");
                        template.CraftId = craftId;
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Amount = reader.GetInt32("amount", 1); //We always want to cost at least 1 item ?
                        template.MainGrade = reader.GetBoolean("main_grade", true);
                        template.RequireGrade = reader.GetInt32("require_grade", 0);
                        template.UpperGrade = reader.GetBoolean("upper_grade", true);

                        _crafts[craftId].CraftMaterials.Add(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM craft_pack_crafts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var craftId = reader.GetUInt32("craft_id");
                        if (!_crafts.ContainsKey(craftId))
                            continue;
                        _crafts[craftId].IsPack = true;
                    }
                }
            }
        }

        Logger.Info("Loaded crafts", _crafts.Count);
    }

    public Craft GetCraftById(uint craftId)
    {
        return _crafts[craftId];
    }
}
