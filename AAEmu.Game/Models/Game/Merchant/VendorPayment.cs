using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Merchant;

public enum VendorPaymentKind : byte
{
    Money,
    Honor,
    Vocation,
    Item
}

public readonly record struct VendorPayment(
    VendorPaymentKind Kind,
    ShopCurrencyType ClientCurrency,
    uint ItemCurrencyId,
    int UnitPrice)
{
    public long GetTotal(int count) => checked((long)UnitPrice * count);
}
