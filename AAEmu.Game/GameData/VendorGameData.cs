using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Merchant;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// Immutable vendor catalogue loaded from 1.8.1.0-Kakao-KR.sqlite.
/// No per-request SQL is performed. base.sqlite3 is not consulted here because
/// both required flattened joins exist in the target client database.
/// </summary>
[GameData]
public class VendorGameData : Singleton<VendorGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, List<Merchants>> _goodsByNpc = new();
    private Dictionary<uint, List<MerchantPacks>> _goodsByPack = new();
    private Dictionary<(uint npcId, uint itemId, byte gradeId), Merchants> _goodsLookup = new();

    public int MerchantCount => _goodsByNpc.Count;
    public int MerchantGoodsCount => _goodsByNpc.Values.Sum(x => x.Count);
    public int PackCount => _goodsByPack.Count;
    public int PackGoodsCount => _goodsByPack.Values.Sum(x => x.Count);

    public IReadOnlyList<Merchants> GetMerchantGoods(uint npcId)
    {
        return _goodsByNpc.TryGetValue(npcId, out var goods) ? goods : Array.Empty<Merchants>();
    }

    public IReadOnlyList<MerchantPacks> GetMerchantPacks(uint packId)
    {
        return _goodsByPack.TryGetValue(packId, out var goods) ? goods : Array.Empty<MerchantPacks>();
    }

    public bool TryGetMerchantGood(uint npcId, uint itemId, byte gradeId, out Merchants good)
    {
        if (_goodsLookup.TryGetValue((npcId, itemId, gradeId), out good))
            return true;

        // Some requests use grade 0 while the authoritative catalogue fixes a grade.
        // Only accept this fallback when the item is unambiguous for the NPC.
        if (gradeId == 0 && _goodsByNpc.TryGetValue(npcId, out var goods))
        {
            var candidates = goods.Where(x => x.ItemId == itemId).Take(2).ToArray();
            if (candidates.Length == 1)
            {
                good = candidates[0];
                return true;
            }
        }

        good = null;
        return false;
    }

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _goodsByNpc = new Dictionary<uint, List<Merchants>>();
        _goodsByPack = new Dictionary<uint, List<MerchantPacks>>();
        _goodsLookup = new Dictionary<(uint npcId, uint itemId, byte gradeId), Merchants>();

        LoadNpcGoods(connection);
        LoadPackGoods(connection);

        foreach (var pair in _goodsByNpc)
            pair.Value.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));
        foreach (var pair in _goodsByPack)
            pair.Value.Sort((a, b) => a.DisplayOrder.CompareTo(b.DisplayOrder));

        Logger.Info(
            "Vendor catalogue loaded from {0}: {1} NPCs/{2} goods, {3} packs/{4} goods",
            SQLite.TargetClientDatabase, MerchantCount, MerchantGoodsCount, PackCount, PackGoodsCount);
    }

    private void LoadNpcGoods(SqliteConnection connection)
    {
        const string table = "zzz_custom_join__merchants__merchant_packs__merchant_goods";
        EnsureMainTableExists(connection, table);

        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT rowid AS display_order,
       merchants__npc_id,
       merchant_goods__item_id,
       merchant_goods__grade_id,
       merchant_packs__kind_id,
       merchant_goods__cost,
       merchant_packs__item_point_id,
       merchant_packs__item_point_icon,
       merchant_packs__item_point_icon_key,
       merchant_goods__id,
       merchant_goods__purchase_type_id,
       merchant_goods__purchase_limit
FROM main.{table}
ORDER BY rowid";

        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var good = new Merchants
            {
                NpcId = reader.GetUInt32("merchants__npc_id"),
                ItemId = reader.GetUInt32("merchant_goods__item_id"),
                GradeId = reader.GetByte("merchant_goods__grade_id"),
                KindId = reader.GetByte("merchant_packs__kind_id"),
                Cost = reader.GetInt32("merchant_goods__cost"),
                ItemPointId = reader.GetUInt32("merchant_packs__item_point_id"),
                ItemPointIcon = reader.GetString("merchant_packs__item_point_icon", string.Empty),
                ItemPointIconKey = reader.GetString("merchant_packs__item_point_icon_key", string.Empty),
                GoodsId = reader.GetUInt32("merchant_goods__id"),
                PurchaseTypeId = reader.GetByte("merchant_goods__purchase_type_id"),
                PurchaseLimit = reader.GetInt32("merchant_goods__purchase_limit"),
                DisplayOrder = reader.GetInt32("display_order")
            };

            if (good.NpcId == 0 || good.ItemId == 0)
                continue;

            if (!_goodsByNpc.TryGetValue(good.NpcId, out var goods))
            {
                goods = new List<Merchants>();
                _goodsByNpc.Add(good.NpcId, goods);
            }
            goods.Add(good);

            var key = (good.NpcId, good.ItemId, good.GradeId);
            if (!_goodsLookup.TryAdd(key, good))
            {
                var existing = _goodsLookup[key];
                if (existing.GoodsId != good.GoodsId || existing.Cost != good.Cost || existing.ItemPointId != good.ItemPointId)
                    Logger.Warn(
                        "Ambiguous merchant good npc={0}, item={1}, grade={2}: goods {3} and {4}",
                        good.NpcId, good.ItemId, good.GradeId, existing.GoodsId, good.GoodsId);
            }
        }
    }

    private void LoadPackGoods(SqliteConnection connection)
    {
        const string table = "zzz_custom_join__merchant_packs__merchant_goods";
        EnsureMainTableExists(connection, table);

        using var command = connection.CreateCommand();
        command.CommandText = $@"
SELECT rowid AS display_order,
       merchant_packs__id,
       merchant_packs__kind_id,
       merchant_goods__item_id,
       merchant_goods__grade_id,
       merchant_goods__cost,
       merchant_packs__item_point_id,
       merchant_packs__item_point_icon,
       merchant_packs__item_point_icon_key,
       merchant_goods__id,
       merchant_goods__purchase_type_id,
       merchant_goods__purchase_limit
FROM main.{table}
ORDER BY rowid";

        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var packId = reader.GetUInt32("merchant_packs__id");
            var good = new MerchantPacks(packId)
            {
                ItemId = reader.GetUInt32("merchant_goods__item_id"),
                GradeId = reader.GetByte("merchant_goods__grade_id"),
                KindId = reader.GetByte("merchant_packs__kind_id"),
                Cost = reader.GetInt32("merchant_goods__cost"),
                ItemPointId = reader.GetUInt32("merchant_packs__item_point_id"),
                ItemPointIcon = reader.GetString("merchant_packs__item_point_icon", string.Empty),
                ItemPointIconKey = reader.GetString("merchant_packs__item_point_icon_key", string.Empty),
                GoodsId = reader.GetUInt32("merchant_goods__id"),
                PurchaseTypeId = reader.GetByte("merchant_goods__purchase_type_id"),
                PurchaseLimit = reader.GetInt32("merchant_goods__purchase_limit"),
                DisplayOrder = reader.GetInt32("display_order")
            };

            if (packId == 0 || good.ItemId == 0)
                continue;

            if (!_goodsByPack.TryGetValue(packId, out var goods))
            {
                goods = new List<MerchantPacks>();
                _goodsByPack.Add(packId, goods);
            }
            goods.Add(good);
        }
    }

    private static void EnsureMainTableExists(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM main.sqlite_master WHERE type = 'table' AND name = $name LIMIT 1";
        command.Parameters.AddWithValue("$name", table);
        if (command.ExecuteScalar() == null)
            throw new InvalidDataException(
                $"Required vendor table main.{table} is missing from {SQLite.TargetClientDatabase}; " +
                "fallback use is intentionally forbidden while the authoritative relationship cannot be proven.");
    }

    public void PostLoad()
    {
    }
}
