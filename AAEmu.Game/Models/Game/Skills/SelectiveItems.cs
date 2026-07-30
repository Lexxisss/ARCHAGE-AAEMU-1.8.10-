using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace AAEmu.Game.Models.Game.Skills;

public class SelectiveItems
{
    public string Effect { get; set; }
    public int Select { get; set; }
    public int ConsumeItemCount { get; set; }
    public List<ItemSelections> ItemSelections { get; set; }

    public SelectiveItems(JObject obj)
    {
        Effect = obj.GetValue("effect")?.ToString() ?? string.Empty;
        Select = obj.GetValue("select")?.ToObject<int>() ?? 0;
        ConsumeItemCount = obj.GetValue("consume_item_count")?.ToObject<int>() ?? 0;
        ItemSelections = obj.GetValue("list")?.ToObject<List<ItemSelections>>() ?? new List<ItemSelections>();
    }
}
