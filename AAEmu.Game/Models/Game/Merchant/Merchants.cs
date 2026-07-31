using System.Collections.Generic;
using System.Linq;

namespace AAEmu.Game.Models.Game.Merchant;

/// <summary>
/// One authoritative merchant-goods row resolved from the target client database.
/// The client database is the source of truth for membership, grade, price override,
/// special item currency and purchase restrictions.
/// </summary>
public class Merchants
{
    public uint NpcId { get; set; }
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

    public static bool SellsItem(uint itemTemplateId, IReadOnlyCollection<Merchants> items)
    {
        return items?.Any(x => x.ItemId == itemTemplateId) == true;
    }
}
