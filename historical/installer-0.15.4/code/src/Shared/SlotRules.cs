namespace Gatekeeper.Mods;

public static class SlotRules
{
    public const int Protocol = 12;
    // Shared by both cabinet prompts and host-side shop/slot transactions.
    public const float Reach = 3.2f;
    public const float SpinSeconds = 3;
    // Whole Prisms: round down and require at least five, never gamble 100%
    // of a one-Prism balance or silently increase the advertised percentage.
    public static int Stake(int balance) => balance < 5 ? 0 : balance / 5;
    public static int Payout(int stake) => stake > 0 && stake <= int.MaxValue / 5 ? stake * 5 : 0;
    public static bool Wins(int roll) => roll == 0;
    public static bool CanBet(int balance, int accumulated, int spent, int stake) => stake > 0 && balance >= stake && Payout(stake) > 0 &&
        (long)spent + stake <= int.MaxValue &&
        (long)accumulated + Payout(stake) <= int.MaxValue &&
        (long)balance - stake + Payout(stake) <= int.MaxValue;
    public static bool IsRequestId(string id) => Guid.TryParseExact(id, "N", out _);
}

// One price per player per Quiet Bay visit. Walking away, losing, winning or
// spending money elsewhere must never recalculate an existing price.
public sealed class SlotVisitStakes
{
    private readonly Dictionary<int, int> stakes = new();
    public int ForPlayer(int owner) => stakes.TryGetValue(owner, out var stake) ? stake : 0;
    public int Capture(int owner, int balance)
    {
        var stake = ForPlayer(owner);
        if (stake > 0) return stake;
        stake = SlotRules.Stake(balance);
        if (stake > 0) stakes.Add(owner, stake);
        return stake;
    }
    public void Clear() => stakes.Clear();
}

public enum SlotStage { AwaitDebit, Spinning, AwaitPayout, Finished, Refunded, Unresolved }

public sealed class SlotWalletReceipts
{
    private readonly HashSet<string> applied = new(StringComparer.OrdinalIgnoreCase);
    public bool Claim(string id, string change, int recipient, int localPlayer) =>
        recipient == localPlayer && SlotRules.IsRequestId(id) && change is "debit" or "refund" or "payout" && applied.Add(id + ":" + change);
    public bool Apply(string id, string change, int recipient, int localPlayer, int delta, Action<int, bool> update)
    {
        if (change is "debit" or "payout" && delta <= 0 || change == "refund" && delta >= 0 || !Claim(id,change,recipient,localPlayer)) return false;
        update(delta,change != "payout");
        return true;
    }
    public void Clear() => applied.Clear();
}

public sealed class SlotRound
{
    public string Id { get; }
    public int Owner { get; }
    public int Stake { get; }
    public bool Won { get; }
    public int Payout => Won ? SlotRules.Payout(Stake) : 0;
    public SlotStage Stage { get; private set; } = SlotStage.AwaitDebit;
    public bool PayoutClaimed { get; private set; }
    public SlotRound(string id, int owner, int stake, int roll)
    {
        if (!SlotRules.IsRequestId(id) || stake <= 0 || SlotRules.Payout(stake) == 0 || roll is < 0 or >= 5) throw new ArgumentException("Invalid slot round.");
        Id = id; Owner = owner; Stake = stake; Won = SlotRules.Wins(roll);
    }
    public bool ConfirmDebit(int sender, string id)
    {
        if (sender != Owner || id != Id || Stage != SlotStage.AwaitDebit) return false;
        Stage = SlotStage.Spinning; return true;
    }
    public bool ClaimPayout()
    {
        if (Stage != SlotStage.Spinning || PayoutClaimed) return false;
        PayoutClaimed = true;
        Stage = Won ? SlotStage.AwaitPayout : SlotStage.Finished;
        return Won;
    }
    public bool ConfirmPayout(int sender, string id)
    {
        if (sender != Owner || id != Id || Stage != SlotStage.AwaitPayout) return false;
        Stage = SlotStage.Finished; return true;
    }
    public bool RefundBeforeSpin()
    {
        if (Stage != SlotStage.AwaitDebit) return false;
        Stage = SlotStage.Refunded; return true;
    }
    public void MarkUnresolved() => Stage = SlotStage.Unresolved;
}
