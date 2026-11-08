using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.General.InputSystem;
using Gatekeeper.General.InputSystem.Enums;
using Gatekeeper.Mods;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class SlotPlayer
{
    private static readonly JackpotCelebration celebration=new();
    private static string pendingId = "", animationId = "", note = "";
    private static CharManager? lockedPlayer;
    private static bool previousLock, animationWon;
    private static float requestedAt, animationStart = -100, flashUntil, noteUntil;
    private static SlotMessage? expected;
    private static string awaiting = "";
    private static readonly int[] stopped = {7,7,7};
    private static readonly SlotWalletReceipts receipts = new();
    private static int quotedStake;
    private static float nextQuoteRequest;
    internal static string Visit { get; private set; } = "";
    internal static void SetVisit(string visit)
    {
        if (visit == Visit) return;
        Visit = visit; quotedStake = 0; nextQuoteRequest = 0;
    }
    internal static bool IsAnimating => Time.unscaledTime - animationStart < SlotRules.SpinSeconds;
    internal static float AnimationAge => Time.unscaledTime - animationStart;
    internal static bool WinFlash => Time.unscaledTime < flashUntil;
    internal static bool OwnsInput(CharManager player) => lockedPlayer != null && lockedPlayer.Pointer == player.Pointer;
    internal static string Title(CharManager player)
    {
        if (noteUntil > Time.unscaledTime) return "Prism Jackpot — " + note;
        if (pendingId.Length > 0) return "Prism Jackpot — " + (awaiting == "debitAck" ? "Confirming bet…" : "Spinning…");
        if (IsAnimating) return "Prism Jackpot — Machine in use";
        if (quotedStake > 0) return player.PrismHolder != null && player.PrismHolder.CurrentValue >= quotedStake
            ? $"Prism Jackpot — Bet {quotedStake} Prisms (fixed this visit)"
            : $"Prism Jackpot — Need {quotedStake} Prisms (fixed bet)";
        return player.PrismHolder != null && player.PrismHolder.CurrentValue >= 5
            ? "Prism Jackpot — Checking fixed bet…" : "Prism Jackpot — Need at least 5 Prisms to set your bet";
    }
    internal static bool TryInteract(CharManager player)
    {
        if (!WorldSlots.CanOffer(player) || !GameplayInputManager.ButtonWasPressed(ActionName.Interaction)) return false;
        if (pendingId.Length > 0 || IsAnimating || WinFlash) return true;
        var holder = player.PrismHolder;
        if (holder == null) return true;
        if (quotedStake == 0)
        { RequestQuote(player); SetNote(holder.CurrentValue < 5 ? "Need at least 5 Prisms to set your bet" : "Checking fixed bet; interact again when ready"); return true; }
        if (holder.CurrentValue < quotedStake) { SetNote($"Need {quotedStake} Prisms for this visit's fixed bet"); return true; }
        pendingId = Guid.NewGuid().ToString("N"); requestedAt = Time.unscaledTime;
        lockedPlayer = player; previousLock = player.Controller.IsInputLocked;
        player.OwnerSetAttack(false); player.Controller.SetMovementDirection(Vector3.zero); player.Controller.SetInputLockState(true);
        try { SlotNetwork.Request(new() { Kind = "bet", Id = pendingId, Accumulated = holder.AccumulatedValue, Spent = holder.SpentValue }); }
        catch (Exception ex) { Plugin.Logger.LogError("Could not request slot spin: " + ex); SetNote("Host request failed"); Release(); }
        return true;
    }
    internal static void Receive(SlotMessage message)
    {
        if (message.Kind == "quote")
        {
            if (Plugin.RestAreaScene && message.Owner == SlotNetwork.LocalId && Visit.Length > 0 && message.Visit == Visit &&
                message.Stake > 0 && SlotRules.Payout(message.Stake) > 0) quotedStake = message.Stake;
            return;
        }
        if (message.Kind == "wallet")
        {
            var owner = PlayableCharactersHolder.MyCharacter;
            if (owner == null || !owner.IsOwner || owner.OwnerId != message.Owner || owner.NetworkObject.ObjectId != message.Character || owner.PrismHolder == null) return;
            var holder = owner.PrismHolder;
            // Claim before invoking native callbacks: a callback throwing after
            // applying the delta must not allow a later retry to apply it twice.
            receipts.Apply(message.Id,message.Change,message.Owner,owner.OwnerId,message.Delta,(delta,spent) =>
            {
                if (!spent) holder.AccumulatedValue = checked(holder.AccumulatedValue + delta);
                else
                {
                    if ((long)holder.SpentValue + delta != message.Spent) throw new InvalidOperationException("Unexpected slot spending state; refusing to overwrite the owner wallet.");
                    holder.SpentValue = checked(holder.SpentValue + delta);
                }
            });
            return;
        }
        if (message.Kind == "spin")
        {
            if (animationId != message.Id)
            {
                animationId = message.Id; animationStart = Time.unscaledTime - Math.Clamp(message.Elapsed,0,SlotRules.SpinSeconds); animationWon = message.Won;
                if (animationWon) Array.Fill(stopped,7);
                else
                {
                    var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(message.Id));
                    for (var i=0;i<3;i++) stopped[i] = bytes[i] % 10;
                    if (stopped.All(x => x == 7)) stopped[2] = 2;
                }
                SlotAudio.Begin(message.Id, Math.Clamp(message.Elapsed, 0, SlotRules.SpinSeconds));
                celebration.Begin(message.Id,message.Elapsed);
            }
            return;
        }
        if (message.Kind is "result" or "finished")
        {
            if(celebration.Claim(message.Id,message.Won))SlotConfetti.Burst();
            if (message.Won && message.Id == animationId) { flashUntil = Time.unscaledTime + 2.4f; SlotAudio.Win(message.Id); }
            if (message.Owner != SlotNetwork.LocalId || message.Id != pendingId) return;
            if (message.Won) { expected = message; awaiting = "payoutAck"; Tick(); }
            else { SetNote($"No win. Bet: {message.Stake} Prisms"); Release(); }
            return;
        }
        if (message.Owner != SlotNetwork.LocalId || message.Id != pendingId) return;
        if (message.Kind == "debit") { expected = message; awaiting = "debitAck"; Tick(); }
        else if (message.Kind == "reject" && message.WalletSync) { expected = message; awaiting = "refundAck"; Tick(); }
        else if (message.Kind is "reject" or "error") { SetNote(message.Note); Release(); }
    }
    internal static void Tick()
    {
        if (!Plugin.RestAreaScene) SetVisit("");
        else if (quotedStake == 0)
        {
            var local = PlayableCharactersHolder.MyCharacter;
            if (local != null) RequestQuote(local);
        }
        if (pendingId.Length == 0) return;
        var player = lockedPlayer;
        if (player == null || player.PrismHolder == null) { Release(); return; }
        if (expected != null)
        {
            var holder = player.PrismHolder;
            if (holder.AccumulatedValue >= expected.Accumulated && holder._accumulatedSync.Value >= expected.Accumulated &&
                holder.SpentValue == expected.Spent && holder._spentSync.Value == expected.Spent)
            {
                var message = expected; var ack = awaiting; expected = null; awaiting = "";
                var requestId = pendingId;
                if (ack == "payoutAck")
                {
                    SetNote($"777! Paid {message.Payout} Prisms");
                    Plugin.Logger.LogInfo($"Slot owner confirmed payout {message.Id}: {message.Payout} Prisms, visible balance {holder.CurrentValue}.");
                    Release();
                }
                else if (ack == "refundAck") { SetNote(message.Note); Release(); }
                SlotNetwork.Request(new() { Kind = ack, Id = requestId, Accumulated = message.Accumulated, Spent = message.Spent });
            }
        }
        if (pendingId.Length > 0 && Time.unscaledTime - requestedAt > 25)
        { SetNote("Wallet confirmation pending; check the mod log"); Plugin.Logger.LogWarning("Slot response timeout: " + pendingId); Release(); }
    }
    private static void RequestQuote(CharManager player)
    {
        if (Time.unscaledTime < nextQuoteRequest || !SlotNetwork.Ready || !Plugin.SlotsInRestArea || !WorldSlots.Exists ||
            !player.IsOwner || !player.IsActiveAndAlive || player.PrismHolder == null || player.PrismHolder.CurrentValue < 5 ||
            WorldSlots.DistanceSquared(player) > SlotRules.Reach * SlotRules.Reach) return;
        nextQuoteRequest = Time.unscaledTime + .5f;
        SlotNetwork.Request(new() { Kind = "quote" });
    }
    internal static float Reel(int index)
    {
        var age = AnimationAge; var stopAt = SlotSoundTimeline.ReelStop(index);
        return age < 0 || age >= stopAt ? stopped[index] : Mathf.Repeat(age * (19 + index * 3) + index * 2.3f,10);
    }
    private static void SetNote(string text) { note = text; noteUntil = Time.unscaledTime + 4; }
    private static void Release()
    {
        var player = lockedPlayer; lockedPlayer = null; pendingId = ""; expected = null; awaiting = "";
        try { if (player != null && player.Controller != null) player.Controller.SetInputLockState(previousLock); }
        catch (Exception ex) { Plugin.Current?.Log.LogWarning("Slot input restore: " + ex.Message); }
    }
    internal static void Reset() { Release(); receipts.Clear(); SlotAudio.Clear(); celebration.Reset(); SetVisit(""); animationId = ""; animationStart = -100; flashUntil = 0; noteUntil = 0; }
}
