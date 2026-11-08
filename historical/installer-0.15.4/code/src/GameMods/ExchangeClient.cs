using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.General.Currency;
using Gatekeeper.Items;
using Gatekeeper.MainMenuScripts.Database.ItemsDatabaseController;
using Gatekeeper.Mods;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class ExchangeClient
{
    private sealed class Pending(string id, ItemDatabaseInfo info, CharManager player, int price)
    {
        internal readonly string Id = id;
        internal readonly ItemDatabaseInfo Info = info;
        internal readonly CharManager Player = player;
        internal readonly int Price = price;
        internal ShopPayment? Payment;
        internal ShopWallet? Wallet;
        internal int BeforeSpent, BeforeCount;
        internal bool DebitClaimed, RefundClaimed, Refunding, AwaitDelivery, Unresolved, CancelRequested;
        internal float Since = Time.unscaledTime;
        internal float NextAck;
    }
    private static readonly Dictionary<int, (ExchangeQuote quote, float at)> quotes = new();
    private static readonly HashSet<int> wanted = new();
    private static Pending? pending;
    private static float nextQuotes;
    internal static string Visit { get; private set; } = "";
    internal static bool Busy => pending != null || ExchangeSales.ClientBusy;
    internal static bool CanClose => (pending == null || pending.Unresolved) && ExchangeSales.CanClose;
    internal static void SetVisit(string visit)
    {
        if (Visit == visit) return;
        Visit = visit; Invalidate();
    }
    internal static void Invalidate() { quotes.Clear(); wanted.Clear(); nextQuotes = 0; }
    internal static ExchangeQuote? Quote(ItemDatabaseInfo item)
    {
        var id = (int)item.ItemId;
        if (quotes.TryGetValue(id,out var saved) && Time.unscaledTime - saved.at < 2) return saved.quote;
        if (Visit.Length > 0) wanted.Add(id);
        return null;
    }
    private static void Send(ExchangeMessage message)
    {
        message.Visit = Visit;
        SlotNetwork.Request(new() { Kind = "exchange", Shop = message });
    }
    private static void State(Pending p, string kind, int spent = 0, int count = 0) => Send(new() { Kind = kind,
        Id = p.Id, Item = (int)p.Info.ItemId, Character = p.Player.NetworkObject.ObjectId, Spent = spent, Count = count });
    internal static void Buy(ItemDatabaseInfo info)
    {
        if (Busy || !ShopUI.InRestArea || !ShopUI.LocallyEligible(info) || !SlotNetwork.Ready || Visit.Length == 0) return;
        var player = PlayableCharactersHolder.MyCharacter;
        var quote = Quote(info);
        if (player == null || !player.IsOwner || !player.IsActiveAndAlive || quote == null || quote.Price < 0) return;
        var wallet = ShopWallet.For(player,CurrencyType.Prism);
        var funds = wallet?.Read();
        if (funds == null || !new ShopPayment(funds.Value,quote.Price).CanStart)
        { ShopUI.SetMessage("Not enough Prisms, or your wallet is still syncing."); return; }
        pending = new(Guid.NewGuid().ToString("N"),info,player,quote.Price);
        ShopUI.SetMessage("Confirming the current item price and purchase...");
        try { Send(new() { Kind = "buy", Id = pending.Id, Item = (int)info.ItemId, Price = quote.Price, Spent = funds.Value.ClientSpent }); }
        catch (Exception ex) { Fail(pending,ex.Message); }
    }
    internal static void Receive(ExchangeMessage message)
    {
        if (message.Kind.StartsWith("sale",StringComparison.Ordinal)) { ExchangeSales.ReceiveClient(message); return; }
        if (message.Quotes != null && message.Visit == Visit && message.Quotes.Length <= ModProtocol.QuoteBatch)
            foreach (var quote in message.Quotes) quotes[quote.Item] = (quote,Time.unscaledTime);
        if (message.Kind == "quotes") return;
        var p = pending;
        if (p == null || message.Id != p.Id) return;
        if (message.Kind == "reject") { ShopUI.SetMessage(message.Note); pending = null; return; }
        if (message.Kind == "done")
        {
            ShopUI.SetMessage("Purchased " + p.Info.itemNameKey.ToString() + ".");
            Plugin.Logger.LogInfo($"Exchange owner confirmed {p.Id}: {p.Info.ItemId}, price {p.Price}; native item delivery and visible wallet matched.");
            pending = null; Invalidate(); return;
        }
        if (message.Kind == "cancelled") { ShopUI.SetMessage(message.Note); pending = null; Invalidate(); return; }
        if (message.Kind == "error") { Fail(p,message.Note); return; }
        if (message.Kind is not ("debit" or "refund" or "delivered")) return;
        var player = PlayableCharactersHolder.MyCharacter;
        if (player == null || !player.IsOwner || player.Pointer != p.Player.Pointer || player.NetworkObject.ObjectId != message.Character ||
            message.Item != (int)p.Info.ItemId || message.Price != p.Price) { Fail(p,"Owner or purchase receipt mismatch."); return; }
        p.BeforeSpent = message.Spent; p.BeforeCount = message.Count;
        p.Wallet ??= ShopWallet.For(player,CurrencyType.Prism);
        if (p.Wallet == null) { Fail(p,"Prism wallet unavailable."); return; }
        try
        {
            if (message.Kind == "debit" && !p.DebitClaimed && !p.Refunding)
            {
                p.DebitClaimed = true; // Claim before native callbacks, including partial failures.
                var funds = p.Wallet.Read();
                p.Payment = new(funds,p.Price);
                if (p.CancelRequested || !ShopUI.InRestArea || !player.IsActiveAndAlive || !ShopUI.LocallyEligible(p.Info) || p.Info.IsMaxStacksOrHigher(player.ItemManager.GetItemQuantity(p.Info.ItemId)) ||
                    funds.ClientSpent != p.BeforeSpent || !p.Payment.CanStart)
                { Cancel(p); return; }
                if (p.Price > 0) p.Wallet.Spend(p.Price);
                p.Since = Time.unscaledTime;
                ShopUI.SetMessage("Confirming payment with the host...");
            }
            else if (message.Kind == "refund" && !p.RefundClaimed)
            {
                p.RefundClaimed = true; p.Refunding = true; p.DebitClaimed = true; p.Since = Time.unscaledTime;
                var observed = (long)p.Wallet.Read().ClientSpent - p.BeforeSpent;
                if (observed < 0 || observed > p.Price) { Fail(p,"Unexpected spending prevents an exact refund."); return; }
                if (observed > 0) p.Wallet.Spend(-(int)observed);
                ShopUI.SetMessage("Purchase cancelled. Confirming your refund...");
            }
            else if (message.Kind == "delivered") p.AwaitDelivery = true;
            Tick();
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning("Exchange owner wallet: " + ex.Message);
            if (!p.AwaitDelivery && !p.Refunding) Cancel(p); else Fail(p,ex.Message);
        }
    }
    private static void Cancel(Pending p)
    {
        if (p.CancelRequested) return;
        p.CancelRequested = true; p.Since = Time.unscaledTime;
        State(p,"cancel"); ShopUI.SetMessage("Cancelling purchase and checking your wallet...");
    }
    private static void Fail(Pending p, string reason)
    {
        p.Unresolved = true;
        ShopUI.SetMessage("Purchase verification needs the mod log before buying again.");
        Plugin.Logger.LogError($"Exchange owner {p.Id}: {reason}");
    }
    internal static void Tick()
    {
        ExchangeSales.ClientTick();
        if (!Plugin.RestAreaScene) SetVisit("");
        if (ShopUI.IsOpen && ShopUI.InRestArea && SlotNetwork.Ready && Visit.Length > 0 && wanted.Count > 0 && Time.unscaledTime >= nextQuotes)
        {
            var batch = wanted.Take(ModProtocol.QuoteBatch).ToArray(); foreach (var id in batch) wanted.Remove(id);
            nextQuotes = Time.unscaledTime + .2f; Send(new() { Kind = "quotes", Items = batch });
        }
        var p = pending;
        if (p == null || p.Unresolved) return;
        try
        {
            if (p.Wallet != null)
            {
                var funds = p.Wallet.Read();
                if (p.Refunding && funds.Agrees && funds.ClientSpent == p.BeforeSpent)
                { if (Time.unscaledTime >= p.NextAck) { p.NextAck = Time.unscaledTime + .35f; State(p,"refundAck",funds.ClientSpent); } return; }
                if (p.Payment?.Charged(funds) == true && !p.CancelRequested)
                {
                    if (!p.AwaitDelivery) { if (Time.unscaledTime >= p.NextAck) { p.NextAck = Time.unscaledTime + .35f; State(p,"debitAck",funds.ClientSpent); } }
                    else
                    {
                        var manager = p.Player.ItemManager;
                        if (manager.GetItemQuantity(p.Info.ItemId) > p.BeforeCount && manager._itemsSync.TryGetValue(p.Info.ItemId,out var count) && count > p.BeforeCount && Time.unscaledTime >= p.NextAck)
                        { p.NextAck = Time.unscaledTime + .35f; State(p,"received",funds.ClientSpent,count); }
                    }
                }
            }
            if (Time.unscaledTime - p.Since > 12)
            {
                if (!p.AwaitDelivery && !p.CancelRequested && !p.Refunding) Cancel(p);
                else if (Time.unscaledTime - p.Since > 35) Fail(p,"Host confirmation timed out; no repeat charge or speculative grant was attempted.");
            }
        }
        catch (Exception ex) { Fail(p,ex.Message); }
    }
    internal static void Reset()
    {
        if (pending != null) Plugin.Current?.Log.LogWarning($"Network ended with local exchange {pending.Id}; no automatic repeat transaction.");
        pending = null; SetVisit(""); Invalidate(); ShopUI.Close(force:true);
        ExchangeSales.ResetClient();
    }
}
