namespace Gatekeeper.Mods;

// Independent of native enemy damage, player health/resistance and observed DPS.
// Sublinear health growth leaves a real return on every offensive upgrade.
public static class AscendantRules
{
    public const byte SaveId=243;
    public const string Revision="ascendant-3";
    public const float MaximumHealth=25,MaximumExtraElites=.15f;
    public const float EliteStartPower=2;
    public static float ExpectedCrit(float chance,float damageMultiplier)
        =>1+Math.Clamp(Safe(chance,0),0,1)*(Math.Max(1,Safe(damageMultiplier,1))-1);
    public static float Power(float damageRatio,float speedRatio,float critRatio)
        =>Math.Clamp(Math.Max(1,Safe(damageRatio,1))*Math.Max(1,Safe(speedRatio,1))*Math.Max(1,Safe(critRatio,1)),1,100000);
    public static float TeamPower(IEnumerable<float> players)
    {
        double sum=0;var count=0;
        foreach(var p in players){if(!float.IsFinite(p)||p<1)continue;sum+=p;count++;}
        return count==0?1:(float)(sum/count);
    }
    public static float Health(float power)=>Math.Clamp(1+.45f*((float)Math.Pow(Math.Max(1,Safe(power,1)),.60)-1),1,MaximumHealth);
    // The first doubling creates breathing room; later doublings add 3%,
    // bounded at 15%. Native elites are selected independently by the game.
    public static float EliteChance(float power)=>Math.Clamp(.03f*(float)Math.Log(Math.Max(1,Safe(power,1)/EliteStartPower),2),0,MaximumExtraElites);
    public static float EarlyFactor(float teamLevel)=>.9f+.1f*Math.Clamp((Safe(teamLevel,1)-30)/20,0,1);
    public static AscendantFactors ForStage(float power,float teamLevel,int completedStages)
    {
        // The opening stage has only native difficulty and selected Aspects.
        if(completedStages<=0)return AscendantFactors.Unscaled;
        var ease=EarlyFactor(teamLevel);
        var health=Health(power);var elites=EliteChance(power);
        // Preserve the established late-game formula exactly, including caps.
        return ease>=1?new(health,elites):new(1+(health-1)*ease,elites*ease);
    }
    private static float Safe(float value,float fallback)=>float.IsFinite(value)?value:fallback;
}

public readonly record struct AscendantFactors(float Health,float ExtraElites)
{
    public static AscendantFactors Unscaled=>new(1,0);
}

// One host-owned entry snapshot, including a repeat/Continue of the same map.
// It stores inputs rather than derived multipliers, and rejects old revisions.
public sealed class AscendantStageSnapshot
{
    public string Revision {get;set;}=AscendantRules.Revision;
    public string Session {get;set;}="";
    public int Scene {get;set;}
    public int CompletedStages {get;set;}
    public float Power {get;set;}=1;
    public float TeamLevel {get;set;}=1;
    public bool Matches(string session,int scene,int completed)=>Revision==AscendantRules.Revision &&
        !string.IsNullOrWhiteSpace(Session) && Session.Length<=256 && Session==session &&
        Scene==scene && Scene>=100 && Scene<=999 && Scene!=998 && CompletedStages==completed && completed>=0 &&
        float.IsFinite(Power) && Power>=1 && Power<=100000 && float.IsFinite(TeamLevel) && TeamLevel>=1;
    public AscendantFactors Factors()=>AscendantRules.ForStage(Power,TeamLevel,CompletedStages);
}

public sealed class AscendantStageCache
{
    public AscendantStageSnapshot? Current {get;private set;}
    public bool Matches(string session,int scene,int completed)=>Current?.Matches(session,scene,completed)==true;
    public bool Restore(AscendantStageSnapshot? snapshot,string session,int scene,int completed)
    {
        if(snapshot?.Matches(session,scene,completed)!=true)return false;
        Current=snapshot;return true;
    }
    public AscendantFactors Capture(string session,int scene,int completed,Func<(float Power,float TeamLevel)> sample)
    {
        if(Matches(session,scene,completed))return Current!.Factors();
        var input=completed<=0?(Power:1f,TeamLevel:1f):sample();
        var snapshot=new AscendantStageSnapshot{Session=session,Scene=scene,CompletedStages=completed,Power=input.Power,TeamLevel=input.TeamLevel};
        if(!snapshot.Matches(session,scene,completed))throw new ArgumentException("Invalid Ascendant stage snapshot.");
        Current=snapshot;return snapshot.Factors();
    }
    public void Clear()=>Current=null;
}
