using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.Skills;

public class ItemSelections
{
    [JsonProperty("item")]
    public uint Item { get; set; }

    [JsonProperty("count")]
    public int Count { get; set; }
}
