using System;
using System.Collections.Generic;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.FishSchools;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class FishDetailsGameData : Singleton<FishDetailsGameData>, IGameDataLoader
{
    private Dictionary<uint, FishDetails> _fishDetails;

    public void Load(SqliteConnection connection, SqliteConnection connection2)
    {
        _fishDetails = new Dictionary<uint, FishDetails>();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, item_id, min_weight, max_weight, min_length, max_length FROM fish_details";
        command.Prepare();
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        while (reader.Read())
        {
            var template = new FishDetails
            {
                Id = reader.GetInt32("id"),
                ItemId = reader.GetUInt32("item_id"),
                MinWeight = reader.GetInt32("min_weight"),
                MaxWeight = reader.GetInt32("max_weight"),
                MinLength = reader.GetInt32("min_length"),
                MaxLength = reader.GetInt32("max_length")
            };

            _fishDetails.TryAdd(template.ItemId, template);
        }
    }

    public bool IsFish(uint templateId)
    {
        return _fishDetails != null && _fishDetails.ContainsKey(templateId);
    }

    public void Initialize(BigFish fish)
    {
        if (fish == null || !IsFish(fish.TemplateId))
            return;

        fish.CreateTime = fish.CreateTime == default ? DateTime.UtcNow : fish.CreateTime;
        (fish.Length, fish.Weight) = GetFishSize(fish.TemplateId);
        fish.UpdateDetailBytes();
    }

    public BigFish Create(uint templateId, int count = 1, byte grade = 0, bool generateId = true)
    {
        if (!IsFish(templateId))
            return null;

        return ItemManager.Instance.Create(templateId, count, grade, generateId) as BigFish;
    }

    public BigFish Create(Item item)
    {
        if (item == null || !IsFish(item.TemplateId))
            return null;

        var fish = new BigFish(item.Id, item.Template, item.Count)
        {
            OwnerId = item.OwnerId,
            SlotType = item.SlotType,
            Slot = item.Slot,
            Grade = item.Grade,
            ItemFlags = item.ItemFlags,
            MadeUnitId = item.MadeUnitId,
            WorldId = item.WorldId,
            CreateTime = item.CreateTime == default ? DateTime.UtcNow : item.CreateTime
        };
        Initialize(fish);
        return fish;
    }

    public (float Length, float Weight) GetFishSize(uint templateId)
    {
        if (!IsFish(templateId))
            return (0f, 0f);

        var detail = _fishDetails[templateId];
        var length = Rand.Next(detail.MinLength, detail.MaxLength + 1);
        var lengthRange = Math.Max(1, detail.MaxLength - detail.MinLength);
        var normalizedLength = Math.Clamp((length - detail.MinLength) / (float)lengthRange, 0f, 1f);
        var weight = Lerp(detail.MinWeight, detail.MaxWeight, normalizedLength);

        return (length, weight);
    }

    public float GetFishLength(uint templateId)
    {
        if (!IsFish(templateId))
            return 0f;
        var detail = _fishDetails[templateId];
        return Rand.Next(detail.MinLength, detail.MaxLength + 1);
    }

    public float GetFishWeight(uint templateId)
    {
        if (!IsFish(templateId))
            return 0f;
        var detail = _fishDetails[templateId];
        return Rand.Next(detail.MinWeight, detail.MaxWeight + 1);
    }

    public float GetFishWeight(uint templateId, float amount)
    {
        if (!IsFish(templateId))
            return 0f;
        return Lerp(_fishDetails[templateId].MinWeight, _fishDetails[templateId].MaxWeight, Math.Clamp(amount, 0f, 1f));
    }

    private static float Lerp(float v1, float v2, float t)
    {
        return v1 + (v2 - v1) * t;
    }

    public void PostLoad()
    {
    }
}
