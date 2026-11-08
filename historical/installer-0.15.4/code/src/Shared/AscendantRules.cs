namespace Gatekeeper.Mods;

// Independent of native enemy damage, player health/resistance and observed DPS.
// Sublinear health growth leaves a real return on every offensive upgrade.
public static class AscendantRules
{
    public const byte SaveId=243;
    public const string Revision="ascendant-2";
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
    private static float Safe(float value,float fallback)=>float.IsFinite(value)?value:fallback;
}
