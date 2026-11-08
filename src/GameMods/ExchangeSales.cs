using FishNet.Connection;
using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.General.Currency;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Items;
using Gatekeeper.MainMenuScripts.Database.ItemsDatabaseController;
using Gatekeeper.Mods;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class ExchangeSales
{
    private sealed class ServerSale(SaleOrder order,CharManager player,NetworkConnection owner,ItemDatabaseInfo info,int spent)
    {
        internal readonly SaleOrder Order=order;
        internal readonly CharManager Player=player;
        internal readonly NetworkConnection Owner=owner;
        internal readonly ItemDatabaseInfo Info=info;
        internal readonly int Spent=spent;
        internal int ExpectedAccumulated;
        internal float At=Time.unscaledTime;
    }
    private sealed class ClientSale(string id,ItemDatabaseInfo info,CharManager player,int price)
    {
        internal readonly string Id=id;
        internal readonly ItemDatabaseInfo Info=info;
        internal readonly CharManager Player=player;
        internal readonly int Price=price;
        internal int BeforeCount,Spent,ExpectedAccumulated;
        internal bool RemovalClaimed,CreditClaimed,Credited,Unresolved;
        internal float At=Time.unscaledTime,NextAck;
    }
    private static readonly Dictionary<int,ServerSale> sales=new();
    private static readonly HashSet<string> seen=new(StringComparer.OrdinalIgnoreCase);
    private static ClientSale? local;
    internal static bool ClientBusy => local!=null;
    internal static bool CanClose => local==null || local.Unresolved;
    internal static bool ServerBusy(int owner) => sales.ContainsKey(owner);
    private static int Count(CharManager p,ItemID id) => p.ItemManager._itemsSync.TryGetValue(id,out var n) ? n : 0;
    private static void Send(NetworkConnection owner,ExchangeMessage message)
    {
        message.Visit=ExchangeAuthority.Visit;
        SlotNetwork.Send(owner,new() { Kind="exchange",Shop=message });
    }
    private static void Request(ExchangeMessage message)
    {
        message.Visit=ExchangeClient.Visit;
        SlotNetwork.Request(new() { Kind="exchange",Shop=message });
    }
    internal static void Sell(ItemDatabaseInfo info)
    {
        if (ExchangeClient.Busy || !ShopUI.InRestArea || !SlotNetwork.Ready || ExchangeClient.Visit.Length==0 || !ExchangeRules.AllowedType((int)info.itemType)) return;
        var p=PlayableCharactersHolder.MyCharacter;
        var quote=ExchangeClient.Quote(info);
        if (p==null || !p.IsOwner || !p.IsActiveAndAlive || quote?.Sale is not >0 || p.ItemManager.GetItemQuantity(info.ItemId)<=0) return;
        var holder=p.PrismHolder;
        var funds=ShopWallet.For(p,CurrencyType.Prism)?.Read();
        if (holder==null || funds?.Agrees!=true || !ExchangeRules.CanCredit(funds.Value.Displayed,holder.AccumulatedValue,quote.Sale)) return;
        local=new(Guid.NewGuid().ToString("N"),info,p,quote.Sale);
        ShopUI.SetMessage("Confirming sale value...");
        try { Request(new() { Kind="sell",Id=local.Id,Item=(int)info.ItemId,Price=quote.Sale,Count=p.ItemManager.GetItemQuantity(info.ItemId),Spent=holder.SpentValue }); }
        catch (Exception ex) { ClientFail(ex.Message); }
    }
    internal static bool ReceiveServer(NetworkConnection sender,ExchangeMessage m)
    {
        if (m.Kind!="sell" && !m.Kind.StartsWith("sale",StringComparison.Ordinal)) return false;
        if (m.Kind=="sell")
        {
            try { Start(sender,m); }
            catch (Exception ex)
            {
                if (sales.TryGetValue(sender.ClientId,out var pending)) ServerFail(pending,ex.Message);
                else Send(sender,new() {Kind="saleReject",Id=m.Id,Note="Sale is unavailable. Please try again."});
            }
            return true;
        }
        if (!sales.TryGetValue(sender.ClientId,out var sale) || m.Id!=sale.Order.Id || m.Character!=sale.Player.NetworkObject.ObjectId) return true;
        var order=sale.Order; var p=sale.Player; var holder=p.PrismHolder;
        try
        {
            if (m.Kind=="saleCancel" && order.Cancel(Count(p,sale.Info.ItemId)))
            { Send(sender,new() {Kind="saleCancelled",Id=order.Id,Note="Sale cancelled. No payment was issued."}); sales.Remove(order.Owner); }
            else if (m.Kind=="saleRemoved" && order.Stage==SaleStage.AwaitRemoval && holder._spentSync.Value==sale.Spent)
            {
                var accumulated=holder._accumulatedSync.Value;
                var balance=(long)holder.InitialValue+accumulated-holder._spentSync.Value;
                if (balance<0 || balance>int.MaxValue || !ExchangeRules.CanCredit((int)balance,accumulated,order.Price))
                { ServerFail(sale,"Sale payout would exceed the wallet limit"); return true; }
                if (order.ClaimCredit(sender.ClientId,m.Id,m.Count,Count(p,sale.Info.ItemId)))
                {
                    sale.ExpectedAccumulated=checked(accumulated+order.Price); sale.At=Time.unscaledTime;
                    CreditSeller(sale);
                }
            }
            else if (m.Kind=="salePaid" && m.Spent==sale.Spent && holder._spentSync.Value==sale.Spent &&
                order.Complete(sender.ClientId,m.Id,m.Count,Count(p,sale.Info.ItemId),m.Accumulated,holder._accumulatedSync.Value,sale.ExpectedAccumulated))
            {
                Send(sender,new() {Kind="saleDone",Id=order.Id,Price=order.Price});
                Plugin.Logger.LogInfo($"Sale settled {order.Id}: owner {order.Owner}, {sale.Info.ItemId}, one stack removed, {order.Price} Prisms.");
                sales.Remove(order.Owner);
            }
            else if (m.Kind=="saleError") ServerFail(sale,"Owner could not confirm native removal or payout");
        }
        catch (Exception ex) { ServerFail(sale,ex.Message); }
        return true;
    }
    private static void Start(NetworkConnection sender,ExchangeMessage m)
    {
        void Reject(string note) => Send(sender,new() {Kind="saleReject",Id=m.Id,Note=note});
        if (!SlotRules.IsRequestId(m.Id)) return;
        if (seen.Contains(m.Id)) { SendCurrent(sender); return; }
        if (ExchangeAuthority.Busy(sender.ClientId) || SlotAuthority.BusyFor(sender.ClientId)) { Reject("Finish your current transaction first."); return; }
        var p=PlayableCharactersHolder.GetCharacterByClientId(sender.ClientId);
        if (m.Visit!=ExchangeAuthority.Visit || m.Visit.Length==0 || !Plugin.RestAreaGameplay || !Plugin.ShopActive || !WorldShop.Exists ||
            p==null || !p.IsServerStarted || p.OwnerId!=sender.ClientId || !p.IsActiveAndAlive || WorldShop.DistanceSquared(p)>SlotRules.Reach*SlotRules.Reach)
        { Reject("Walk up to the Item Exchange in Quiet Bay or Quiet Place."); return; }
        if (!DatabaseInfoProvider.Items.ItemInfos.TryGetValue((ItemID)m.Item,out var info) || info==null || !ExchangeRules.AllowedType((int)info.itemType))
        { Reject("This item cannot be sold."); return; }
        var count=Count(p,info.ItemId); var quote=ExchangePricing.Quote(info,p);
        if (count<=0 || m.Count!=count) { Reject("Your inventory changed. Select the item again."); return; }
        if (quote==null || quote.Sale<=0 || m.Price!=quote.Sale) { Reject("The sale value changed. Check the current value and sell again."); return; }
        var holder=p.PrismHolder;
        var balance=(long)holder.InitialValue+holder._accumulatedSync.Value-holder._spentSync.Value;
        if (!holder.IsServerStarted || m.Spent!=holder._spentSync.Value || balance<0 || balance>int.MaxValue ||
            !ExchangeRules.CanCredit((int)balance,holder._accumulatedSync.Value,quote.Sale))
        { Reject("Your wallet is syncing or at its currency limit."); return; }
        var sale=new ServerSale(new(m.Id,sender.ClientId,count,quote.Sale),p,sender,info,m.Spent);
        sales.Add(sender.ClientId,sale); seen.Add(m.Id); SendState(sale);
    }
    private static void CreditSeller(ServerSale sale)
    {
        if (!SlotNetwork.IsHost || sale.Order.Stage!=SaleStage.AwaitPayout || !sale.Order.CreditClaimed || !sale.Player.IsServerStarted)
            throw new InvalidOperationException("A seller credit requires the host's one-time removal claim.");
        // ClientAdd distributes Prism awards to teammates. Use the same paired
        // holder + targeted reactive-property receipt as the slot's own payout.
        sale.Player.PrismHolder.ForceChange(sale.Order.Price,false);
        if (sale.Player.PrismHolder._accumulatedSync.Value!=sale.ExpectedAccumulated)
            throw new InvalidOperationException("The seller's native credit did not match.");
        SendState(sale);
    }
    private static void SendState(ServerSale sale)
    {
        Send(sale.Owner,new() {Kind=sale.Order.Stage switch {SaleStage.AwaitRemoval=>"saleRemove",SaleStage.AwaitPayout=>"saleCredit",_=>"saleError"},
            Id=sale.Order.Id,Item=(int)sale.Info.ItemId,Price=sale.Order.Price,Count=sale.Order.BeforeCount,
            Character=sale.Player.NetworkObject.ObjectId,Spent=sale.Spent,Accumulated=sale.ExpectedAccumulated});
    }
    private static void ServerFail(ServerSale sale,string reason)
    {
        sale.Order.Fail();
        Plugin.Logger.LogError($"Sale {sale.Order.Id}, owner {sale.Order.Owner}: {reason}. No repeat removal, credit or speculative rollback.");
        Send(sale.Owner,new() {Kind="saleError",Id=sale.Order.Id,Note="Sale verification needs the mod log before trading again."});
    }
    internal static void SendCurrent(NetworkConnection owner)
    { if (sales.TryGetValue(owner.ClientId,out var sale)) SendState(sale); }
    internal static void ServerTick()
    {
        foreach (var sale in sales.Values.ToArray())
        {
            if (sale.Order.Stage==SaleStage.Unresolved || Time.unscaledTime-sale.At<10) continue;
            try { sale.At=Time.unscaledTime; SendState(sale); }
            catch (Exception ex) { ServerFail(sale,ex.Message); }
        }
    }
    internal static void ReceiveClient(ExchangeMessage m)
    {
        var sale=local;
        if (sale==null || m.Id!=sale.Id) return;
        if (m.Kind is "saleReject" or "saleCancelled" or "saleDone")
        {
            ShopUI.SetMessage(m.Kind=="saleDone" ? $"Sold one {sale.Info.itemNameKey.ToString()}: +{sale.Price} Prisms." : m.Note);
            local=null; ExchangeClient.Invalidate(); ShopUI.RefreshInventory(); return;
        }
        if (m.Kind=="saleError") { ClientFail(m.Note); return; }
        var p=PlayableCharactersHolder.MyCharacter;
        if (p==null || !p.IsOwner || p.Pointer!=sale.Player.Pointer || p.NetworkObject.ObjectId!=m.Character ||
            m.Item!=(int)sale.Info.ItemId || m.Price!=sale.Price) { ClientFail("Sale owner or item mismatch"); return; }
        try
        {
            sale.BeforeCount=m.Count; sale.Spent=m.Spent;
            if (m.Kind=="saleRemove" && !sale.RemovalClaimed)
            {
                if (!ShopUI.InRestArea || !p.IsActiveAndAlive || p.ItemManager.GetItemQuantity(sale.Info.ItemId)!=m.Count || Count(p,sale.Info.ItemId)!=m.Count || p.PrismHolder.SpentValue!=m.Spent)
                { Request(new() {Kind="saleCancel",Id=sale.Id,Character=m.Character}); return; }
                sale.RemovalClaimed=true; sale.At=Time.unscaledTime;
                p.ItemManager.OwnerRemoveItem(sale.Info.ItemId,1);
                ShopUI.SetMessage("Confirming item removal...");
            }
            else if (m.Kind=="saleCredit" && !sale.CreditClaimed)
            {
                if (!sale.RemovalClaimed || p.ItemManager.GetItemQuantity(sale.Info.ItemId)!=sale.BeforeCount-1 || Count(p,sale.Info.ItemId)!=sale.BeforeCount-1)
                { ClientFail("Sale credit arrived before confirmed item removal"); return; }
                sale.CreditClaimed=true; sale.ExpectedAccumulated=m.Accumulated;
                p.PrismHolder.AccumulatedValue=checked(p.PrismHolder.AccumulatedValue+sale.Price);
                sale.Credited=true; sale.At=Time.unscaledTime; ShopUI.SetMessage("Confirming your sale proceeds...");
            }
            ClientTick();
        }
        catch (Exception ex)
        {
            ClientFail(ex.Message);
            Request(new() {Kind="saleError",Id=sale.Id,Character=p.NetworkObject.ObjectId});
        }
    }
    private static void ClientFail(string reason)
    {
        if (local!=null) local.Unresolved=true;
        ShopUI.SetMessage("Sale verification needs the mod log before trading again.");
        Plugin.Logger.LogError("Sale owner: "+reason);
    }
    internal static void ClientTick()
    {
        var sale=local;
        if (sale==null || sale.Unresolved) return;
        try
        {
            var p=sale.Player; var holder=p.PrismHolder;
            if (Time.unscaledTime>=sale.NextAck && sale.RemovalClaimed && p.ItemManager.GetItemQuantity(sale.Info.ItemId)==sale.BeforeCount-1 && Count(p,sale.Info.ItemId)==sale.BeforeCount-1)
            {
                var funds=ShopWallet.For(p,CurrencyType.Prism)?.Read();
                if (!sale.Credited || funds?.Agrees==true && holder.AccumulatedValue>=sale.ExpectedAccumulated && holder._accumulatedSync.Value>=sale.ExpectedAccumulated && holder.SpentValue==sale.Spent)
                {
                    sale.NextAck=Time.unscaledTime+.35f;
                    Request(new() {Kind=sale.Credited ? "salePaid":"saleRemoved",Id=sale.Id,Character=p.NetworkObject.ObjectId,
                        Count=sale.BeforeCount-1,Spent=holder.SpentValue,Accumulated=holder.AccumulatedValue});
                }
            }
            if (Time.unscaledTime-sale.At>35) ClientFail("Host confirmation timed out");
        }
        catch (Exception ex) { ClientFail(ex.Message); }
    }
    internal static void ResetClient()
    {
        if (local!=null) Plugin.Current?.Log.LogWarning("Session ended during sale "+local.Id);
        local=null;
    }
    internal static void ResetServer()
    {
        foreach (var sale in sales.Values) Plugin.Current?.Log.LogWarning($"Session ended during sale {sale.Order.Id}, stage {sale.Order.Stage}");
        sales.Clear(); seen.Clear();
    }
}
