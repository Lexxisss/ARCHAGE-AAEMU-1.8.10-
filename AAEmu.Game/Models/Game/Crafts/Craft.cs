using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Crafts;

/*
    Data relating to a craft.
*/
public class Craft
{
    public uint Id { get; set; }
    public int CastDelay { get; set; }
    public uint ToolId { get; set; }
    public uint SkillId { get; set; }
    public uint WiId { get; set; }
    public uint MilestoneId { get; set; }
    public uint ReqDoodadId { get; set; }
    public bool NeedBind { get; set; }
    public uint AcId { get; set; }
    public int ActabilityLimit { get; set; }
    public bool ShowUpperCraft { get; set; }
    public int RecommendLevel { get; set; }
    public int VisibleOrder { get; set; }

    /// <summary>
    /// Labour one cycle of this recipe costs. Server-side, not a display value: it was never
    /// read, so crafting was free.
    /// </summary>
    public int Cost { get; set; }

    /// <summary>Whether the recipe may be published as an order for somebody else to fill.</summary>
    public bool Orderable { get; set; }

    /// <summary>
    /// When set, the proficiency limit is the only thing gating the recipe - level and other
    /// requirements do not apply.
    /// </summary>
    public bool UseOnlyActability { get; set; }

    public uint ProductsPackId { get; set; }
    public uint CraftCCategoryId { get; set; }
    public uint CraftDCategoryId { get; set; }
    public string Title { get; set; }

    public List<CraftProduct> CraftProducts { get; set; }
    public List<CraftMaterial> CraftMaterials { get; set; }
    public bool IsPack { get; set; }

    public bool ResultsInBackpack
    {
        get
        {
            return CraftProducts.Select(product => ItemManager.Instance.GetTemplate(product.ItemId))
                .OfType<BackpackTemplate>().Any();
        }
    }

    public Craft()
    {
        CraftProducts = new List<CraftProduct>();
        CraftMaterials = new List<CraftMaterial>();
    }
}
