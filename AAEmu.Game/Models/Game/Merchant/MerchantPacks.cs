using System.Collections.Generic;
using System.Linq;

namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// One goods row belonging to a reusable merchant pack.
/// </summary>
public class MerchantPacks
{
    public uint PackId { get; set; }
    public uint GoodsId { get; set; }
    public uint ItemId { get; set; }
    public byte GradeId { get; set; }
    public byte KindId { get; set; }
    public int Cost { get; set; }
    public uint ItemPointId { get; set; }
    public string ItemPointIcon { get; set; } = string.Empty;
    public string ItemPointIconKey { get; set; } = string.Empty;
    public byte PurchaseTypeId { get; set; }
    public int PurchaseLimit { get; set; }
    public int DisplayOrder { get; set; }

    // Retained for older callers that still build a pack object incrementally.
    public List<MerchantGoodsItem> Items { get; set; }

    public MerchantPacks(uint packId)
    {
        PackId = packId;
        Items = new List<MerchantGoodsItem>();
    }

    public bool SellsItem(uint itemTemplateId)
    {
        return ItemId == itemTemplateId || Items.Any(x => x.ItemId == itemTemplateId);
    }

    public void AddItemToStock(uint itemTemplateId, byte itemGrade)
    {
        if (SellsItem(itemTemplateId))
            return;

        Items.Add(new MerchantGoodsItem
        {
            ItemId = itemTemplateId,
            Grade = itemGrade
        });
    }
}
