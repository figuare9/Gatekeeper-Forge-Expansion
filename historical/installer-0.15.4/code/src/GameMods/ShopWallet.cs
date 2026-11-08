using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Currency;
using Gatekeeper.Currency.MonolithKey;
using Gatekeeper.Currency.Omni;
using Gatekeeper.Currency.Prism;
using Gatekeeper.Currency.Scrap;
using Gatekeeper.General.Currency;
using Gatekeeper.Mods;

namespace Gatekeeper.CustomMods;

internal sealed record ShopWallet(Func<ShopFunds> Read, Action<int> Spend)
{
    private static ShopWallet Network(NetworkCurrencyHolder holder, Func<int> displayed, Action<int> spend) =>
        new(() => new(displayed(), holder.CurrentValue, holder.SpentValue, holder._spentSync.Value), spend);

    internal static ShopWallet? For(CharManager player, CurrencyType currency) => currency switch
    {
        CurrencyType.Prism when player.PrismHolder != null && PrismsManager.Instance != null =>
            Network(player.PrismHolder, () => PrismsManager.Instance.CurrentAmount, amount => PrismsManager.Instance.ClientRemove(amount)),
        CurrencyType.Scrap when player.ScrapHolder != null && ScrapManager.Instance != null =>
            Network(player.ScrapHolder, () => ScrapManager.Instance.CurrentAmount, amount => ScrapManager.Instance.ClientRemove(amount)),
        CurrencyType.MonolithKey when player.MonolithKeyHolder != null && MonolithKeysManager.Instance != null =>
            Network(player.MonolithKeyHolder, () => MonolithKeysManager.Instance.CurrentAmount, amount => MonolithKeysManager.Instance.ClientRemove(amount)),
        CurrencyType.Omni when player.OmniHolder != null && OmniManager.Instance != null =>
            new(() => new(OmniManager.Instance.CurrentAmount, player.OmniHolder.CurrentValue, player.OmniHolder.SpentValue,
                player.OmniHolder.SpentValue), amount => OmniManager.Instance.ClientRemove(amount)),
        _ => null
    };
}
