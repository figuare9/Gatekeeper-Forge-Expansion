using Gatekeeper.Char_Scripts.General;
using Gatekeeper.EnvironmentStuff.Obelisks;
using Gatekeeper.EnvironmentStuff.Pedestals;
using Gatekeeper.General.Currency;
using Gatekeeper.Items;
using Gatekeeper.MainMenuScripts.Database.ItemsDatabaseController;
using Gatekeeper.Mods;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class ExchangePricing
{
    internal static ExchangeQuote? Quote(ItemDatabaseInfo item, CharManager player)
    {
        if (!Plugin.RestAreaGameplay || !ExchangeRules.AllowedType((int)item.itemType)) return null;
        var pricing = ObelisksDataManager.Instance;
        if (pricing == null || pricing.pedestalInfo == null) return null;
        // Quiet Bay pedestals use their configured currency, not the obelisk's.
        var currency = pricing.pedestalInfo.ObeliskCurrency;
        if (currency != CurrencyType.Prism) return null;
        var pedestal = UnityEngine.Object.FindObjectsOfType<Pedestal>().FirstOrDefault(p => p != null &&
            p.gameObject.scene.isLoaded && p.IsSpawned && p.PickedUpItem != null && p.ItemType == item.itemType && WorldShop.EmporiumAnchor != null &&
            p.gameObject.scene.handle == WorldShop.EmporiumAnchor.gameObject.scene.handle);
        var basePrice = pedestal != null ? pedestal._activateCostSync.Value : ExchangeRules.PedestalBase(pricing.CalculatePedestalPrice(item.itemType));
        // On a host, use this buyer's authoritative modifier table. The owner's
        // native ItemPrice getter reads the matching replicated local table.
        var modifier = player.ItemManager._itemModificationsSync.TryGetValue(ItemParamModificationType.TradersCodexDiscountPerc, out var discount) ? discount : 0;
        var native = ExchangeRules.PlayerPrice(basePrice, modifier);
        var total = ExchangeRules.Markup(native);
        return total < 0 ? null : new() { Item = (int)item.ItemId, Native = native, Price = total, Sale = ExchangeRules.SalePrice(native), Currency = (int)currency };
    }
}
