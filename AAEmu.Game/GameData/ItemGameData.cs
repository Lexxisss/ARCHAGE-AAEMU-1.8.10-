using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class ItemGameData : Singleton<ItemGameData>, IGameDataLoader
{
    private Dictionary<uint, Dictionary<byte, Dictionary<byte, uint>>> _itemGradeBuffs;

    public BuffTemplate GetItemBuff(uint itemId, byte gradeId, byte numPieces = 1)
    {
        if (_itemGradeBuffs.TryGetValue(itemId, out var itemGradeBuffs))
            if (itemGradeBuffs.TryGetValue(gradeId, out var pieceBuffs))
                if (pieceBuffs.TryGetValue(numPieces, out var buffId))
                    return SkillManager.Instance.GetBuffTemplate(buffId);
        return null;
    }

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _itemGradeBuffs = new Dictionary<uint, Dictionary<byte, Dictionary<byte, uint>>>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_grade_buffs";
            command.Prepare();
            using (var sqliteReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteReader))
            {
                while (reader.Read())
                {
                    var itemId = reader.GetUInt32("item_id");
                    var itemGrade = reader.GetByte("item_grade_id");
                    var buffId = reader.GetUInt32("buff_id");
                    var numPieces = reader.GetByte("num_pieces");

                    // The target table contains placeholder rows for removed records.
                    if (itemId == 0 || buffId == 0 || numPieces == 0)
                        continue;

                    if (!_itemGradeBuffs.ContainsKey(itemId))
                        _itemGradeBuffs.Add(itemId, new Dictionary<byte, Dictionary<byte, uint>>());

                    if (!_itemGradeBuffs[itemId].ContainsKey(itemGrade))
                        _itemGradeBuffs[itemId].Add(itemGrade, new Dictionary<byte, uint>());

                    if (!_itemGradeBuffs[itemId][itemGrade].TryAdd(numPieces, buffId))
                    {
                        throw new System.IO.InvalidDataException(
                            $"Duplicate item grade buff: item={itemId}, grade={itemGrade}, pieces={numPieces}");
                    }
                }
            }
        }
    }

    public void PostLoad()
    {

    }
}
