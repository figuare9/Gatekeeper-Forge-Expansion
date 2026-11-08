namespace Gatekeeper.Mods;

public enum SlotSoundCue { Lever, CycleStart, NumberLand, CycleStop }

// Audio follows the same reel stops as the visible numbers. Reliable-message
// retries and late joins must not replay the lever, old clicks or a jackpot.
public sealed class SlotSoundTimeline
{
    private string round = "";
    private int landed;
    private bool cycleStopped, won;
    public int Landed => landed;
    public static float ReelStop(int index) => 1.8f + index * .42f;
    public SlotSoundCue[] Begin(string id, float age)
    {
        if (!SlotRules.IsRequestId(id) || round == id) return Array.Empty<SlotSoundCue>();
        round = id; won = false; landed = 0;
        while (landed < 3 && age >= ReelStop(landed)) landed++;
        cycleStopped = landed == 3;
        if (cycleStopped) return Array.Empty<SlotSoundCue>();
        return age <= .15f ? new[] { SlotSoundCue.Lever, SlotSoundCue.CycleStart } : new[] { SlotSoundCue.CycleStart };
    }
    public SlotSoundCue[] Advance(float age)
    {
        if (round.Length == 0 || cycleStopped) return Array.Empty<SlotSoundCue>();
        var cues = new List<SlotSoundCue>();
        while (landed < 3 && age >= ReelStop(landed)) { landed++; cues.Add(SlotSoundCue.NumberLand); }
        if (landed == 3) { cycleStopped = true; cues.Add(SlotSoundCue.CycleStop); }
        return cues.ToArray();
    }
    public bool ConfirmWin(string id) => round.Length > 0 && id == round && !won && (won = true);
    public void Reset() { round = ""; landed = 0; cycleStopped = false; won = false; }
}
