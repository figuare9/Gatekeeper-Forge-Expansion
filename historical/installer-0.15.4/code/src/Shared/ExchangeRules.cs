namespace Gatekeeper.Mods;

public static class ExchangeRules
{
    // Match Pedestal.ServerSetPrice: round the run-scaled float to even first.
    public static int PedestalBase(float scaled) => !float.IsFinite(scaled) || scaled < 0 || (double)scaled > int.MaxValue
        ? -1 : (int)Math.Round((double)scaled, MidpointRounding.ToEven);
    // Pedestal.ItemPrice adds floorf(base * TradersCodexDiscountPerc).
    public static int PlayerPrice(int pedestalBase, float modifier)
    {
        if (pedestalBase < 0 || !float.IsFinite(modifier)) return -1;
        var result = (double)pedestalBase + Math.Floor((float)pedestalBase * modifier);
        return result < 0 || result > int.MaxValue ? -1 : (int)result;
    }
    public static int Markup(int nativePrice)
    {
        if (nativePrice < 0) return -1;
        var marked = ((long)nativePrice * 1265 + 999) / 1000;
        return marked > int.MaxValue ? -1 : (int)marked;
    }
    public static bool AllowedType(int type) => type is >= 0 and < 5;
    public static int SalePrice(int nativePrice) => nativePrice < 0 ? -1 : nativePrice / 2;
    public static bool CanCredit(int balance,int accumulated,int price) => price > 0 && balance >= 0 && accumulated >= 0 &&
        (long)balance + price <= int.MaxValue && (long)accumulated + price <= int.MaxValue;
}

public enum SaleStage { AwaitRemoval, AwaitPayout, Finished, Unresolved }
public sealed class SaleOrder
{
    public string Id { get; }
    public int Owner { get; }
    public int BeforeCount { get; }
    public int Price { get; }
    public SaleStage Stage { get; private set; } = SaleStage.AwaitRemoval;
    public bool CreditClaimed { get; private set; }
    public SaleOrder(string id,int owner,int beforeCount,int price)
    {
        if (!SlotRules.IsRequestId(id) || beforeCount <= 0 || price <= 0) throw new ArgumentException("Invalid sale.");
        Id=id; Owner=owner; BeforeCount=beforeCount; Price=price;
    }
    public bool ClaimCredit(int owner,string id,int clientCount,int serverCount)
    {
        if (owner!=Owner || id!=Id || Stage!=SaleStage.AwaitRemoval || CreditClaimed || clientCount!=BeforeCount-1 || serverCount!=BeforeCount-1) return false;
        CreditClaimed=true; Stage=SaleStage.AwaitPayout; return true;
    }
    public bool Complete(int owner,string id,int clientCount,int serverCount,int clientAccumulated,int serverAccumulated,int expectedAccumulated)
    {
        if (owner!=Owner || id!=Id || Stage!=SaleStage.AwaitPayout || clientCount!=BeforeCount-1 || serverCount!=BeforeCount-1 ||
            clientAccumulated < expectedAccumulated || serverAccumulated < expectedAccumulated) return false;
        Stage=SaleStage.Finished; return true;
    }
    public bool Cancel(int serverCount)
    {
        if (Stage!=SaleStage.AwaitRemoval || serverCount!=BeforeCount) return false;
        Stage=SaleStage.Finished; return true;
    }
    public void Fail() => Stage=SaleStage.Unresolved;
}

public enum ExchangeStage { AwaitDebit, Delivering, AwaitReceipt, Refunding, Finished, Unresolved }

public sealed class ExchangeOrder
{
    public string Id { get; }
    public int Owner { get; }
    public int Item { get; }
    public int Price { get; }
    public int BeforeSpent { get; }
    public int BeforeCount { get; }
    public int ExpectedSpent => BeforeSpent + Price;
    public ExchangeStage Stage { get; private set; } = ExchangeStage.AwaitDebit;
    public bool GrantClaimed { get; private set; }
    public ExchangeOrder(string id, int owner, int item, int price, int spent, int count)
    {
        if (!SlotRules.IsRequestId(id) || price < 0 || spent < 0 || count < 0 || (long)spent + price > int.MaxValue || count == int.MaxValue)
            throw new ArgumentException("Invalid exchange order.");
        Id = id; Owner = owner; Item = item; Price = price; BeforeSpent = spent; BeforeCount = count;
    }
    public bool ClaimGrant(int sender, string id, int clientSpent, int serverSpent)
    {
        if (sender != Owner || id != Id || Stage != ExchangeStage.AwaitDebit || GrantClaimed ||
            clientSpent != ExpectedSpent || serverSpent != ExpectedSpent) return false;
        GrantClaimed = true; Stage = ExchangeStage.Delivering; return true;
    }
    public bool ObserveDelivery(int serverCount, int serverSpent)
    {
        if (Stage != ExchangeStage.Delivering || serverCount <= BeforeCount || serverSpent != ExpectedSpent) return false;
        Stage = ExchangeStage.AwaitReceipt; return true;
    }
    public bool Complete(int sender, string id, int clientCount, int serverCount, int serverSpent)
    {
        if (sender != Owner || id != Id || Stage != ExchangeStage.AwaitReceipt || clientCount <= BeforeCount ||
            serverCount <= BeforeCount || serverSpent != ExpectedSpent) return false;
        Stage = ExchangeStage.Finished; return true;
    }
    public bool Refund()
    {
        if (Stage != ExchangeStage.AwaitDebit || GrantClaimed) return false;
        Stage = ExchangeStage.Refunding; return true;
    }
    public bool CompleteRefund(int sender, string id, int clientSpent, int serverSpent)
    {
        if (sender != Owner || id != Id || Stage != ExchangeStage.Refunding || clientSpent != BeforeSpent || serverSpent != BeforeSpent) return false;
        Stage = ExchangeStage.Finished; return true;
    }
    public void Fail() => Stage = ExchangeStage.Unresolved;
}
