namespace Gatekeeper.Mods;

public static class ItemCapRules
{
    public const int Maximum=25;
    // Pinned native ItemID values: Indicator, Yellow Pill, Adjuster.
    public static bool Limited(int id)=>id is 11 or 29 or 52;
    public static int Allow(int id,int owned,int requested)=>!Limited(id) ? requested :
        Math.Min(Math.Max(0,requested),Math.Max(0,Maximum-Math.Max(0,owned)));
}

// A result is celebratory only if this client saw the reels still spinning.
// Catch-up snapshots, duplicate results and a second "finished" do not replay it.
public sealed class JackpotCelebration
{
    private string round="";
    private bool eligible,played;
    public void Begin(string id,float elapsed)
    {
        if(!SlotRules.IsRequestId(id) || round==id)return;
        round=id;played=false;eligible=float.IsFinite(elapsed) && elapsed<SlotRules.SpinSeconds;
    }
    public bool Claim(string id,bool won)=>eligible && won && id==round && !played && (played=true);
    public void Reset(){round="";eligible=false;played=false;}
}
