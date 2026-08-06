using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Skills;

public class SelectiveItems
{
    public uint Id { get; set; }
    public uint SkillId { get; set; }
    public int ConsumeItemCount { get; set; }
    public bool IsMulti { get; set; }
    public int SelectCount { get; set; }
    public List<ItemSelections> ItemSelections { get; } = new();
}
