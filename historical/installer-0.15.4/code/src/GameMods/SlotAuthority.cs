using System.Security.Cryptography;
using FishNet.Connection;
using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Currency;
using Gatekeeper.Mods;
using Gatekeeper.General.Predictor;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class SlotAuthority
{
    private sealed class Pending(SlotRound round, CharManager player, NetworkConnection owner, int accumulated, int spent)
    {
        internal readonly SlotRound Round = round;
        internal readonly CharManager Player = player;
        internal readonly NetworkConnection Owner = owner;
        internal int Accumulated = accumulated, Spent = spent;
        internal readonly int BeforeSpent = spent - round.Stake;
        internal float Since = Time.unscaledTime;
    }
    private static Pending? active;
    private static readonly HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SlotVisitStakes stakes = new();
    private static float nextQuoteCheck;
    private static int visitScene=-1;
    private static string? lastQuoteError;
    internal static string Visit { get; private set; } = "";
    internal static bool Busy => active != null;
    internal static bool BusyFor(int owner) => active?.Round.Owner == owner;

    internal static void Receive(NetworkConnection sender, SlotMessage message)
    {
        if (!SlotNetwork.IsHost) return;
        if (message.Kind == "quote") { SendQuote(sender); return; }
        if (message.Kind == "bet")
        {
            try { Start(sender, message); }
            catch (Exception ex)
            {
                if (active != null) active.Round.MarkUnresolved();
                Plugin.Logger.LogError("Slot charge requires reconciliation: " + message.Id + "; " + ex);
                SlotNetwork.Send(sender, new() { Kind = "error", Id = message.Id, Owner = sender.ClientId, Note = "Bet confirmation failed; see the mod log." });
            }
            return;
        }
        var order = active;
        if (order == null || sender.ClientId != order.Round.Owner || message.Id != order.Round.Id) return;
        var holder = order.Player.PrismHolder;
        if (message.Accumulated != order.Accumulated || message.Spent != order.Spent ||
            holder._spentSync.Value != order.Spent || holder._accumulatedSync.Value < order.Accumulated) return;
        if (message.Kind == "debitAck" && order.Round.ConfirmDebit(sender.ClientId, message.Id))
        {
            order.Since = Time.unscaledTime;
            SlotNetwork.Publish(new() { Kind = "spin", Id = order.Round.Id, Owner = order.Round.Owner, Stake = order.Round.Stake, Won = order.Round.Won });
        }
        else if (message.Kind == "payoutAck" && order.Round.ConfirmPayout(sender.ClientId, message.Id)) Finish(order);
    }

    private static void Start(NetworkConnection sender, SlotMessage request)
    {
        void Reject(string reason) => SlotNetwork.Send(sender, new() { Kind = "reject", Id = request.Id, Owner = sender.ClientId, Note = reason });
        if (!SlotRules.IsRequestId(request.Id)) return;
        if (seen.Contains(request.Id)) return; // Replays never charge, reroll or pay again.
        if (active != null) { Reject("Machine in use. Wait for the current spin."); return; }
        if (ExchangeAuthority.Busy(sender.ClientId)) { Reject("Finish your Item Exchange purchase first."); return; }
        var player = PlayableCharactersHolder.GetCharacterByClientId(sender.ClientId);
        if (!Plugin.SlotsInRestArea || !WorldSlots.Exists || player == null || player.OwnerId != sender.ClientId ||
            !player.IsServerStarted || !player.IsActiveAndAlive || WorldSlots.DistanceSquared(player) > SlotRules.Reach * SlotRules.Reach)
        { Reject("Walk up to the slot machine in Quiet Bay or Quiet Place."); return; }
        var holder = player.PrismHolder;
        if (holder == null || !holder.IsServerStarted) { Reject("Prism wallet is unavailable."); return; }
        var accumulated = holder._accumulatedSync.Value;
        var spent = holder._spentSync.Value;
        var balance = checked(holder.InitialValue + accumulated - spent);
        if (request.Accumulated != accumulated || request.Spent != spent)
        { Reject("Your wallet is syncing. Please try again."); return; }
        var stake = stakes.ForPlayer(sender.ClientId);
        if (Visit.Length == 0 || request.Visit != Visit || stake == 0)
        { Reject("Confirm the machine's fixed bet and interact again."); SendQuote(sender); return; }
        if (!SlotRules.CanBet(balance, accumulated, spent, stake))
        { Reject(balance < stake ? $"You need {stake} Prisms for this visit's fixed bet." : "This bet would exceed the game's currency limit."); return; }
        var round = new SlotRound(request.Id, sender.ClientId, stake, RandomNumberGenerator.GetInt32(5));
        seen.Add(round.Id);
        active = new(round, player, sender, accumulated, checked(spent + round.Stake));
        // The server chooses the amount and outcome; neither is accepted from a client.
        ApplyOwnWallet(player, round.Stake, "debit", round.Id);
        if (holder._spentSync.Value != active.Spent) throw new InvalidOperationException("Slot debit was not applied exactly.");
        SlotNetwork.Send(sender, new() { Kind = "debit", Id = round.Id, Owner = round.Owner, Stake = round.Stake,
            Accumulated = accumulated, Spent = active.Spent });
        Plugin.Logger.LogInfo($"Slot bet {round.Id}: owner {round.Owner}, balance {balance}, stake {round.Stake}; awaiting owner wallet acknowledgment.");
    }

    // Unlike PrismsManager.ClientAdd (which shares its award with teammates),
    // change only the bettor's server holder, then send a receipt to that owner.
    // The owner applies a native reactive-property delta exactly once. Absolute
    // reconnection snapshots could overwrite concurrent ordinary Prism income.
    private static void ApplyOwnWallet(CharManager player, int amount, string change, string id)
    {
        if (!SlotNetwork.IsHost || !player.IsServerStarted) throw new InvalidOperationException("Slot wallet changes require the server.");
        player.PrismHolder.ForceChange(amount, change != "payout");
        SendWallet(player, amount, change, id);
    }
    private static void SendWallet(CharManager player, int amount, string change, string id) => SlotNetwork.Send(player.Owner,
        new() { Kind = "wallet", Id = id, Owner = player.OwnerId, Character = player.NetworkObject.ObjectId, Delta = amount, Change = change,
            Accumulated = player.PrismHolder._accumulatedSync.Value, Spent = player.PrismHolder._spentSync.Value });

    internal static void Tick()
    {
        // A price/peer query must not prevent an already charged spin from settling.
        try { UpdateVisit(); }
        catch (Exception ex) { ReportQuoteError(ex); }
        var order = active;
        if (order == null || !SlotNetwork.IsHost) return;
        try
        {
            if (order.Round.Stage == SlotStage.AwaitDebit && Time.unscaledTime - order.Since > 10)
            {
                if (order.Round.RefundBeforeSpin())
                {
                    var observed = order.Player.PrismHolder._spentSync.Value - order.BeforeSpent;
                    if (observed < 0 || observed > order.Round.Stake) throw new InvalidOperationException("Unexpected spending prevents a speculative slot refund.");
                    if (observed > 0) ApplyOwnWallet(order.Player, -observed, "refund", order.Round.Id);
                    SlotNetwork.Send(order.Owner, new() { Kind = "reject", Id = order.Round.Id, Owner = order.Round.Owner,
                        Note = "Spin cancelled; your stake was refunded.", Accumulated = order.Player.PrismHolder._accumulatedSync.Value,
                        Spent = order.Player.PrismHolder._spentSync.Value, WalletSync = true });
                    active = null;
                }
            }
            else if (order.Round.Stage == SlotStage.Spinning &&
                (Time.unscaledTime - order.Since >= SlotRules.SpinSeconds || !Plugin.SlotsInRestArea))
            {
                if (order.Round.ClaimPayout())
                {
                    order.Accumulated = checked(order.Player.PrismHolder._accumulatedSync.Value + order.Round.Payout);
                    // Marked before the native call; a callback exception cannot
                    // cause a repeated payout on the next frame or replay.
                    ApplyOwnWallet(order.Player, order.Round.Payout, "payout", order.Round.Id);
                    if (order.Player.PrismHolder._accumulatedSync.Value != order.Accumulated)
                        throw new InvalidOperationException("Slot payout was not applied exactly.");
                    order.Since = Time.unscaledTime;
                    SlotNetwork.Publish(new() { Kind = "result", Id = order.Round.Id, Owner = order.Round.Owner,
                        Stake = order.Round.Stake, Won = true, Payout = order.Round.Payout,
                        Accumulated = order.Accumulated, Spent = order.Spent });
                }
                else Finish(order);
            }
            else if (order.Round.Stage == SlotStage.AwaitPayout && Time.unscaledTime - order.Since > 10)
            {
                // The server already credited the winner. Never issue it twice
                // just because an acknowledgment was lost or a peer left.
                SendWallet(order.Player, order.Round.Payout, "payout", order.Round.Id); Finish(order);
            }
        }
        catch (Exception ex)
        {
            order.Round.MarkUnresolved();
            Plugin.Logger.LogError("Slot round requires reconciliation: " + order.Round.Id + "; " + ex);
            SlotNetwork.Send(order.Owner, new() { Kind = "error", Id = order.Round.Id, Owner = order.Round.Owner,
                Note = "Wallet confirmation failed. The machine is paused; see the mod log." });
        }
    }

    private static void UpdateVisit()
    {
        if (!SlotNetwork.IsHost) return;
        var scene=Plugin.RestAreaScene ? (int)GlobalPredictor.CurrentScene:-1;
        if(scene!=visitScene){Visit="";stakes.Clear();nextQuoteCheck=0;visitScene=scene;}
        if (!Plugin.RestAreaScene)
        {
            Visit = ""; stakes.Clear(); nextQuoteCheck = 0;
            return;
        }
        if (!Plugin.SlotsInRestArea || !WorldSlots.Exists) return;
        if (Visit.Length == 0)
        {
            Visit = Guid.NewGuid().ToString("N");
            SlotNetwork.PublishReady();
        }
        if (Time.unscaledTime < nextQuoteCheck) return;
        nextQuoteCheck = Time.unscaledTime + .2f;
        foreach (var peer in SlotNetwork.Participants)
            if (stakes.ForPlayer(peer.ClientId) == 0) SendQuote(peer);
    }

    internal static void SendQuote(NetworkConnection connection)
    {
        try { SendQuoteCore(connection); }
        catch (Exception ex) { ReportQuoteError(ex); }
    }
    private static void ReportQuoteError(Exception ex)
    {
        if (lastQuoteError == ex.Message) return;
        lastQuoteError = ex.Message;
        Plugin.Logger.LogWarning("Slot fixed-price query: " + ex);
    }
    private static void SendQuoteCore(NetworkConnection connection)
    {
        if (!SlotNetwork.IsHost || Visit.Length == 0 || !Plugin.SlotsInRestArea || !WorldSlots.Exists) return;
        var stake = stakes.ForPlayer(connection.ClientId);
        if (stake == 0)
        {
            var player = PlayableCharactersHolder.GetCharacterByClientId(connection.ClientId);
            if (player == null || player.OwnerId != connection.ClientId || !player.IsServerStarted || !player.IsActiveAndAlive ||
                WorldSlots.DistanceSquared(player) > SlotRules.Reach * SlotRules.Reach) return;
            var holder = player.PrismHolder;
            if (holder == null || !holder.IsServerStarted) return;
            var balance = checked(holder.InitialValue + holder._accumulatedSync.Value - holder._spentSync.Value);
            stake = stakes.Capture(connection.ClientId, balance);
            if (stake == 0) return;
            Plugin.Logger.LogInfo($"Slot fixed bet: visit {Visit}, owner {connection.ClientId}, first-approach balance {balance}, stake {stake}.");
        }
        SlotNetwork.Send(connection, new() { Kind = "quote", Owner = connection.ClientId, Stake = stake });
    }

    private static void Finish(Pending order)
    {
        if(order.Round.Won)CannonProgress.UnlockWinner(order.Owner,order.Round.Id);
        SlotNetwork.Publish(new() { Kind = "finished", Id = order.Round.Id, Owner = order.Round.Owner,
            Stake = order.Round.Stake, Won = order.Round.Won, Payout = order.Round.Payout,
            Accumulated = order.Player.PrismHolder._accumulatedSync.Value, Spent = order.Player.PrismHolder._spentSync.Value });
        Plugin.Logger.LogInfo($"Slot settled {order.Round.Id}: owner {order.Round.Owner}, stake {order.Round.Stake}, payout {order.Round.Payout}, won {order.Round.Won}.");
        active = null;
    }
    internal static void SendCurrent(NetworkConnection connection)
    {
        var order = active;
        if (order?.Round.Stage is not (SlotStage.Spinning or SlotStage.AwaitPayout)) return;
        SlotNetwork.Send(connection, new() { Kind = "spin", Id = order.Round.Id, Owner = order.Round.Owner,
            Stake = order.Round.Stake, Won = order.Round.Won, Elapsed = order.Round.Stage == SlotStage.Spinning ?
                Math.Clamp(Time.unscaledTime - order.Since,0,SlotRules.SpinSeconds) : SlotRules.SpinSeconds });
    }
    internal static void Reset()
    {
        if (active != null) Plugin.Current?.Log.LogWarning($"Network session ended with slot round {active.Round.Id} in state {active.Round.Stage}; server owner {active.Round.Owner}. No automatic duplicate payout was attempted.");
        active = null; seen.Clear(); stakes.Clear(); Visit = ""; nextQuoteCheck = 0; lastQuoteError = null;visitScene=-1;
    }
}
