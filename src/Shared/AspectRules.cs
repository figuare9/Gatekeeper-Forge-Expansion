namespace Gatekeeper.Mods;

// EAspect is a byte on both the native save and FishNet wire. Keep expansion IDs
// away from the native range without exceeding the byte representation.
public static class AspectRules
{
    public const byte Chaos=240,Scaling=241,Elite=242;
    public const string Revision="expansion-aspects-5";
    public const float ChaosBonus=.75f,HealthPerLevel=.07f,PrismsPerLevel=.05f;
    public static bool IsCustom(int id)=>id is Chaos or Scaling or Elite or AscendantRules.SaveId;
    public static float SpawnMultiplier(float native,bool chaos)=>!chaos?native:native+ChaosBonus;
    public static float TeamFactor(IEnumerable<int> levels)=>TeamScaling(levels).Health;
    public static AspectScaling TeamScaling(IEnumerable<int> levels)
    {
        double total=0;var count=0;
        // A newly spawned Gatekeeper can have native level zero; it is still
        // part of the team average. Negative values represent missing data.
        foreach(var level in levels){if(level<0)continue;total+=level;count++;}
        var extra=count==0?0:Math.Max(0,total/count-1);
        return new((float)(1+HealthPerLevel*extra),(float)(1+PrismsPerLevel*extra));
    }
    public static float Health(float native,float factor)=>!float.IsFinite(native) || native<=0 || !float.IsFinite(factor) || factor<1?native:(float)Math.Min(float.MaxValue,(double)native*factor);
    public static int EliteMask(int allowed)=>allowed&127;
    public static int ChooseElite(int existing,int allowed,int choice)
    {
        if(existing!=0)return existing;
        var types=Enumerable.Range(0,7).Select(i=>1<<i).Where(i=>(EliteMask(allowed)&i)!=0).ToArray();
        return types.Length==0?4:types[(int)((uint)choice%(uint)types.Length)];
    }
}

public readonly record struct AspectScaling(float Health,float Prisms)
{
    public static AspectScaling Unscaled=>new(1,1);
}

// Carry sub-Prism fractions between native enemy rewards. Rounding every small
// kill up would turn a 5% bonus into a 100% bonus on one-Prism enemies.
public sealed class AspectPrismCarry
{
    public double Remainder {get;private set;}
    public int Apply(int native,float factor)
    {
        if(native<=0 || !float.IsFinite(factor) || factor<=1)return native;
        var exact=Math.Min(int.MaxValue,(double)native*factor+Remainder);
        var amount=(int)Math.Floor(exact+1e-6);Remainder=Math.Max(0,exact-amount);return amount;
    }
    public void Clear()=>Remainder=0;
}

public sealed class AspectRunMarker
{
    public bool RequiresMod {get;set;}
    public int[] Aspects {get;set;}=Array.Empty<int>();
    public string Revision {get;set;}=AspectRules.Revision;
}
