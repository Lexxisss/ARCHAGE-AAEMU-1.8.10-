using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Merchant;

public static class VendorPriceResolver
{
    public static bool TryResolvePurchase(
        Merchants good,
        ShopCurrencyType requestedCurrency,
        out VendorPayment payment,
        out string failureReason)
    {
        payment = default;
        failureReason = string.Empty;

        if (good == null || good.ItemId == 0)
        {
            failureReason = "missing_merchant_good";
            return false;
        }

        // Merchant packs with item_point_id use an item token. The client-provided
        // currency byte cannot redirect these goods to money/honor/vocation.
        if (good.ItemPointId != 0)
        {
            if (good.Cost <= 0)
            {
                failureReason = "invalid_item_point_cost";
                return false;
            }

            payment = new VendorPayment(VendorPaymentKind.Item, requestedCurrency, good.ItemPointId, good.Cost);
            return true;
        }

        // A non-zero merchant_goods.cost is the authoritative shop override and
        // represents the normal money price in this client schema.
        if (good.Cost < 0)
        {
            failureReason = "negative_merchant_cost";
            return false;
        }

        if (good.Cost > 0)
        {
            if (requestedCurrency != ShopCurrencyType.Money)
            {
                failureReason = "merchant_cost_requires_money";
                return false;
            }

            payment = new VendorPayment(VendorPaymentKind.Money, ShopCurrencyType.Money, 0, good.Cost);
            return true;
        }

        if (requestedCurrency is not (ShopCurrencyType.Money or ShopCurrencyType.Honor or ShopCurrencyType.VocationBadges))
        {
            failureReason = "unsupported_currency";
            return false;
        }

        if (!ItemManager.Instance.TryGetShopPrice(good.ItemId, requestedCurrency, out var price, out _) || price < 0)
        {
            failureReason = "price_not_found";
            return false;
        }

        var kind = requestedCurrency switch
        {
            ShopCurrencyType.Money => VendorPaymentKind.Money,
            ShopCurrencyType.Honor => VendorPaymentKind.Honor,
            ShopCurrencyType.VocationBadges => VendorPaymentKind.Vocation,
            _ => VendorPaymentKind.Money
        };
        payment = new VendorPayment(kind, requestedCurrency, 0, price);
        return true;
    }

    public static bool TryResolveMoneyRefund(uint itemId, byte gradeId, int count, out long refund, out string failureReason)
    {
        refund = 0;
        failureReason = string.Empty;

        if (count <= 0)
        {
            failureReason = "invalid_count";
            return false;
        }

        if (!ItemManager.Instance.TryGetShopPrice(itemId, ShopCurrencyType.Money, out _, out var baseRefund))
        {
            failureReason = "refund_not_found";
            return false;
        }

        var grade = ItemManager.Instance.GetGradeTemplate(gradeId);
        if (grade == null)
        {
            failureReason = "grade_not_found";
            return false;
        }

        try
        {
            // Match client/server integer behavior without float accumulation.
            var perItem = checked((long)baseRefund * grade.RefundMultiplier / 100L);
            refund = checked(perItem * count);
            return refund >= 0;
        }
        catch (System.OverflowException)
        {
            failureReason = "refund_overflow";
            return false;
        }
    }
}
