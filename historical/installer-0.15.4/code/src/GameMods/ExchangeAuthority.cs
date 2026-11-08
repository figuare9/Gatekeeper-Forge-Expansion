using FishNet.Connection;
using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Char_Scripts.General.Network;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Items;
using Gatekeeper.MainMenuScripts.Database.ItemsDatabaseController;
using Gatekeeper.Mods;
using Gatekeeper.General.Predictor;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class ExchangeAuthority
{
    private sealed class Pending(ExchangeOrder order, CharManager player, NetworkConnection owner, ItemDatabaseInfo info)
    {
        internal readonly ExchangeOrder Order = order;
        internal readonly CharManager Player = player;
        internal readonly NetworkConnection Owner = owner;
        internal readonly ItemDatabaseInfo Info = info;
        internal float Since = Time.unscaledTime;
    }
    private static readonly Dictionary<int, Pending> orders = new();
    private static readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
    internal static string Visit { get; private set; } = "";
    private static int visitScene=-1;
    internal static bool Busy(int owner) => orders.ContainsKey(owner) || ExchangeSales.ServerBusy(owner);
    private static ItemDatabaseInfo? Info(int id) => DatabaseInfoProvider.Items != null &&
        DatabaseInfoProvider.Items.ItemInfos.TryGetValue((ItemID)id, out var info) && info != null &&
        ExchangeRules.AllowedType((int)info.itemType) ? info : null;
    private static int Count(CharManager player, ItemID id) => player.ItemManager._itemsSync.TryGetValue(id, out var count) ? count : 0;
    private static int Spent(Pending p) => p.Player.PrismHolder._spentSync.Value;
    private static void Send(NetworkConnection owner, ExchangeMessage message)
    {
        message.Visit = Visit;
        SlotNetwork.Send(owner, new() { Kind = "exchange", Shop = message });
    }
    private static bool Nearby(CharManager? player, int owner) => Plugin.ShopActive && Plugin.RestAreaGameplay && WorldShop.Exists &&
        player != null && player.OwnerId == owner && player.IsServerStarted && player.IsActiveAndAlive && WorldShop.DistanceSquared(player) <= SlotRules.Reach * SlotRules.Reach;

    internal static void Receive(NetworkConnection sender, ExchangeMessage message)
    {
        if (!SlotNetwork.IsHost) return;
        if (ExchangeSales.ReceiveServer(sender,message)) return;
        if (message.Kind == "quotes")
        {
            if (Visit.Length == 0 || message.Visit != Visit || message.Items == null || message.Items.Length > ModProtocol.QuoteBatch) return;
            var player = PlayableCharactersHolder.GetCharacterByClientId(sender.ClientId);
            if (!Nearby(player, sender.ClientId)) return;
            var quotes = message.Items.Distinct().Select(id =>
            {
                var info = Info(id);
                var quote = info != null ? ExchangePricing.Quote(info,player!) : null;
                if (quote == null) return new ExchangeQuote { Item=id };
                if (!CannonRegistry.HostEligible(info!,sender.ClientId)) quote.Price=-1;
                if (Count(player!,info!.ItemId)<=0) quote.Sale=-1;
                return quote;
            }).ToArray();
            Send(sender, new() { Kind = "quotes", Quotes = quotes }); return;
        }
        if (message.Kind == "buy")
        {
            try { Start(sender, message); }
            catch (Exception ex)
            {
                Plugin.Logger.LogWarning("Exchange request: " + ex);
                if (orders.TryGetValue(sender.ClientId,out var started) && started.Order.Id == message.Id) Refund(started);
                else Send(sender,new() { Kind = "reject", Id = message.Id, Note = "The current shop price is unavailable. Please try again." });
            }
            return;
        }
        if (!orders.TryGetValue(sender.ClientId, out var pending) || message.Id != pending.Order.Id ||
            message.Character != pending.Player.NetworkObject.ObjectId) return;
        var order = pending.Order;
        if (message.Kind == "cancel") { Refund(pending); return; }
        if (message.Kind == "debitAck" && order.Stage == ExchangeStage.AwaitDebit)
        {
            if (!Nearby(pending.Player, order.Owner) || !CannonRegistry.HostEligible(pending.Info,order.Owner) || pending.Info.IsMaxStacksOrHigher(Count(pending.Player, pending.Info.ItemId)))
            { Refund(pending); return; }
            if (pending.Player.PrismHolder._accumulatedSync.Value < 0 ||
                (long)pending.Player.PrismHolder.InitialValue + pending.Player.PrismHolder._accumulatedSync.Value - Spent(pending) < 0) return;
            if (order.ClaimGrant(sender.ClientId, message.Id, message.Spent, Spent(pending)))
            {
                pending.Since = Time.unscaledTime;
                try
                {
                    var grant = new Il2CppSystem.Collections.Generic.List<NetworkItemData>();
                    grant.Add(new NetworkItemData(pending.Info.ItemId, 1));
                    pending.Player.ItemManager.ServerAndOwnerAddItems(grant, ItemSource.Other, true);
                }
                catch (Exception ex) { Fail(pending, "Item delivery needs reconciliation: " + ex.Message); }
            }
        }
        else if (message.Kind == "received" && order.Complete(sender.ClientId, message.Id, message.Count, Count(pending.Player,pending.Info.ItemId), Spent(pending)))
        {
            Send(sender, new() { Kind = "done", Id = order.Id, Item = order.Item, Price = order.Price });
            Plugin.Logger.LogInfo($"Co-op exchange settled {order.Id}: owner {order.Owner}, item {pending.Info.ItemId}, charged {order.Price}, stacks {order.BeforeCount} -> {Count(pending.Player,pending.Info.ItemId)}.");
            orders.Remove(order.Owner);
        }
        else if (message.Kind == "refundAck" && order.CompleteRefund(sender.ClientId, message.Id, message.Spent, Spent(pending)))
        {
            Send(sender, new() { Kind = "cancelled", Id = order.Id, Note = "Purchase cancelled; payment restored." });
            orders.Remove(order.Owner);
        }
    }
    private static void Start(NetworkConnection sender, ExchangeMessage request)
    {
        void Reject(string note, ExchangeQuote? quote = null) => Send(sender, new() { Kind = "reject", Id = request.Id,
            Note = note, Quotes = quote == null ? null : new[] { quote } });
        if (!SlotRules.IsRequestId(request.Id)) return;
        if (seen.Contains(request.Id)) { SendCurrent(sender); return; }
        if (Busy(sender.ClientId) || SlotAuthority.BusyFor(sender.ClientId)) { Reject("Finish your current transaction first."); return; }
        var player = PlayableCharactersHolder.GetCharacterByClientId(sender.ClientId);
        if (Visit.Length == 0 || request.Visit != Visit || !Nearby(player,sender.ClientId)) { Reject("Walk up to the Item Exchange in Quiet Bay or Quiet Place."); return; }
        var info = Info(request.Item);
        if (info == null || !CannonRegistry.HostEligible(info,sender.ClientId)) { Reject("This item is unavailable in the host's unlocked item pool, or Cannon Wheel is still locked for you."); return; }
        var count = Count(player!,info.ItemId);
        if (info.IsMaxStacksOrHigher(count)) { Reject("Item stack limit reached."); return; }
        var quote = ExchangePricing.Quote(info,player!);
        if (quote == null || request.Price != quote.Price) { Reject("The live price changed. Check the new price and buy again.",quote); return; }
        var holder = player!.PrismHolder;
        if (holder == null || !holder.IsServerStarted || holder._spentSync.Value != request.Spent ||
            (long)holder.InitialValue + holder._accumulatedSync.Value - holder._spentSync.Value < quote.Price ||
            (long)holder._spentSync.Value + quote.Price > int.MaxValue)
        { Reject("Not enough Prisms, or your wallet is still syncing."); return; }
        var order = new ExchangeOrder(request.Id,sender.ClientId,request.Item,quote.Price,request.Spent,count);
        var pending = new Pending(order,player,sender,info);
        orders.Add(sender.ClientId,pending); seen.Add(request.Id);
        SendState(pending);
        Plugin.Logger.LogInfo($"Exchange approved {order.Id}: owner {order.Owner}, {info.ItemId}, native {quote.Native}, +26.5% {quote.Price}; awaiting visible-wallet debit.");
    }
    private static void Refund(Pending p)
    {
        if (!p.Order.Refund()) return;
        p.Since = Time.unscaledTime; SendState(p);
    }
    private static void Fail(Pending p, string reason)
    {
        p.Order.Fail();
        Plugin.Logger.LogError($"Exchange {p.Order.Id}, owner {p.Order.Owner}: {reason}. No duplicate grant or speculative refund attempted.");
        Send(p.Owner,new() { Kind = "error", Id = p.Order.Id, Note = "Purchase needs reconciliation; see the mod log before buying again." });
    }
    private static void SendState(Pending p)
    {
        var order = p.Order;
        Send(p.Owner,new() { Kind = order.Stage switch { ExchangeStage.AwaitDebit => "debit", ExchangeStage.Refunding => "refund",
            ExchangeStage.AwaitReceipt => "delivered", _ => "pending" }, Id = order.Id, Item = order.Item, Price = order.Price,
            Spent = order.BeforeSpent, Count = order.BeforeCount, Character = p.Player.NetworkObject.ObjectId });
    }
    internal static void SendCurrent(NetworkConnection owner)
    { if (orders.TryGetValue(owner.ClientId,out var pending)) SendState(pending); ExchangeSales.SendCurrent(owner); }
    internal static void Tick()
    {
        if (!SlotNetwork.IsHost) return;
        ExchangeSales.ServerTick();
        var scene=Plugin.RestAreaScene ? (int)GlobalPredictor.CurrentScene:-1;
        if(scene!=visitScene){Visit="";visitScene=scene;}
        if (!Plugin.RestAreaScene) Visit = "";
        else if (Plugin.ShopActive && WorldShop.Exists && Visit.Length == 0)
        { Visit = Guid.NewGuid().ToString("N"); SlotNetwork.PublishReady(); }
        foreach (var p in orders.Values.ToArray())
        {
            try
            {
                var order = p.Order;
                if (order.Stage == ExchangeStage.Unresolved) continue;
                if (order.Stage == ExchangeStage.AwaitDebit && (!Plugin.RestAreaGameplay || Time.unscaledTime - p.Since > 8)) Refund(p);
                if (order.ObserveDelivery(Count(p.Player,p.Info.ItemId),Spent(p))) { p.Since = Time.unscaledTime; SendState(p); }
                if (Time.unscaledTime - p.Since <= 10) continue;
                if (order.Stage is ExchangeStage.AwaitReceipt or ExchangeStage.Refunding) { p.Since = Time.unscaledTime; SendState(p); }
                else if (order.Stage == ExchangeStage.Delivering) Fail(p,"Native item delivery could not be confirmed");
            }
            catch (Exception ex) { Fail(p,ex.Message); }
        }
    }
    internal static void Reset()
    {
        foreach (var p in orders.Values) Plugin.Current?.Log.LogWarning($"Network ended with exchange {p.Order.Id}, owner {p.Order.Owner}, stage {p.Order.Stage}.");
        orders.Clear(); seen.Clear(); Visit = "";visitScene=-1;
        ExchangeSales.ResetServer();
    }
}
