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
                        COALESCE(target.visible_order, fallback.visible_order) AS visible_order
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
                        template.MainGrade = reader.GetBoolean("main_grade");

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
