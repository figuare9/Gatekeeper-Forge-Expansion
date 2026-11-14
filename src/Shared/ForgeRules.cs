namespace Gatekeeper.Mods;

public static class ForgeRules
{
    public const int CharacterId = 30002;
    public const ushort CollectionId = 30200;
    public const string Key = "gkmm.forge";
    public const string Revision = "forge-kit-15-8";
    public const float RivetSpeed = 120;
    public const float RivetRange = 36;
    public const float RivetRadius = .20f;
    public const float RivetScale = 2.625f;
    public const float RivetPushDistance = 2.07f;
    public const float BaseMoveSpeed = 9, BaseHealth = 250, BaseRegeneration = 2;
    public const float RivetPushDuration = .18f;
    public const float RivetDamageMultiplier = 3.4375f; // 137.5 at Forge's native base damage 40.
    public const float FragmentDamageMultiplier = 1.25f;
    public const float SentrySeekRadius=12, SentryFlightLifetime=2;
    public static int SentryTargets(int level)=>level>=3 ? 8:level>=2 ? 6:level>=1 ? 4:3;
    public static float SentryHitMultiplier(int ordinal,bool charged)=>charged ? 1.25f:ordinal<=1 ? 1:ordinal==2 ? .75f:.5f;
    public static float SentryRate(int level)=>level>=1 ? 1.15f:1;
    public const int RivetMaxTargets = 3;
    public const float RivetInterval = .75f;
    public const float FragmentRange = 12;
    public const float FragmentSpeed = 55;
    public const float GrenadeRange = 36;
    public const float MagneticRadius = 10,MagneticWindow=.5f,MagneticDuration=.42f,MagneticStop=.65f;
    public static float MagneticDistance(float distance)=>float.IsFinite(distance) ? Math.Max(0,distance-MagneticStop):0;
    public static float CastRange(int slot) => slot==1 ? RivetRange : slot is 3 or 4 ? GrenadeRange : 12;
    public static float RivetCooldown(float attackSpeed) => RivetInterval / (float.IsFinite(attackSpeed) ? Math.Clamp(attackSpeed,.2f,8):1);
    public const float CommandRadius=12,TurretDamageBonus=.25f,TurretSpeedBonus=.25f,TurretRegen=.03f;
    public const float RageBonus=.25f,RageDuration=5,StillDelay=1,StillRegen=.02f;
    // Both playable rest areas permit Forge abilities: DeltaQuietBay (20)
    // and TheNowhereQuietPlace (998), immediately before the final throne room.
    public static bool Combat(bool gameplay,int scene) => gameplay && (scene is 20 or 998 || CannonRules.InCombatLevel(gameplay,scene));
    public static int Level(int value) => Math.Clamp(value,0,3);
    public static int Sentries(int level) => level >= 3 ? 4 : 3;
    public static float SentryRange(int level) => level>=3 ? 42.1875f : level>=1 ? 33.75f : 27;
    public static float SentryHealth(int level) => level>=1 ? 375 : 250;
    public static float SentryCooldown(int level) => level>=1 ? 7.5f : 10;
    public static int Penetration(int level,bool charged=false) => charged ? 6 : level>=1 ? 4 : 3;
    public static bool Charged(int level,int shot) => level>=3 && shot%3==0;
    public static float Damage(int slot,int level,bool charged=false) => slot switch
    {1=>charged ? 6.875f : level>=1 ? 4.375f : RivetDamageMultiplier,2=>level>=3 ? 1.03125f : 1.375f,3=>level>=3 ? 6 : 4,_=>0};
    public static float Duration(int slot,int level) => slot==2 ? float.PositiveInfinity : (level>=1 ? 10 : 8)+(slot==4 ? 3:0);
    public static float Radius(int level) => level>=1 ? 10.5f : 9;
    public static float GrenadeRadius(int level) => level>=1 ? 8 : 6;
    public static float Protection(int level) => level>=3 ? .65f : .50f;
    public static float Reduction(float value) => float.IsFinite(value) ? Math.Clamp(value,0,.65f) : 0;
    public const float HealInterval=.5f,HealPerSecond=.04f;
    public static float Healing(float maximum,float current,int fields=1) => fields<=0 ? 0:Regen(maximum,current,HealPerSecond*fields);
    public static float Regen(float maximum,float current,float rate) => !float.IsFinite(maximum) || !float.IsFinite(current) || !float.IsFinite(rate) || maximum<=0 || current<=0 || rate<=0 ? 0 : Math.Min(Math.Max(0,maximum-current),maximum*rate*HealInterval);
    public static bool ValidPoint(float[]? value) => value is {Length:3} && value.All(v=>float.IsFinite(v) && Math.Abs(v)<100000);
    public static bool Compatible(string revision,string roster,bool enabled,string expected) => enabled &&
        revision==Revision && roster.Length==64 && roster==expected;
}

// Host-owned serial recharge queue. Queueing rather than multiplying independent
// timers matches native charge recharge; repeated or rejected requests spend nothing.
public sealed class ForgeChargeGate
{
    private readonly Queue<double> recharge=new();
    public bool TrySpend(double now,int capacity,double cooldown)
    {
        if(!double.IsFinite(now) || !double.IsFinite(cooldown) || cooldown<=0 || capacity<1 || capacity>50)return false;
        while(recharge.Count>0 && recharge.Peek()<=now)recharge.Dequeue();
        if(recharge.Count>=capacity)return false;
        recharge.Enqueue(Math.Max(now,recharge.Count>0 ? recharge.Last():now)+cooldown);return true;
    }
    public void RefundLast()
    {if(recharge.Count==0)return;var keep=recharge.Take(recharge.Count-1).ToArray();recharge.Clear();foreach(var t in keep)recharge.Enqueue(t);}
}

public sealed class ForgePassiveState
{
    public double StillSince {get;private set;}=double.NaN;
    public double RageUntil {get;private set;}
    public void Observe(double now,bool moved)
    {if(!double.IsFinite(now))return;if(moved || !double.IsFinite(StillSince) || now<StillSince)StillSince=now;}
    public bool Stationary(double now)=>double.IsFinite(now) && double.IsFinite(StillSince) && now-StillSince>=ForgeRules.StillDelay;
    public bool Enraged(double now)=>double.IsFinite(now) && now<RageUntil;
    public void EnemyKilledTurret(double now)
    {if(double.IsFinite(now))RageUntil=now+ForgeRules.RageDuration;}
}

public sealed record ForgeRunMarker(bool RequiresMod,int CharacterId);

public sealed class ForgeMessage
{
    public string Kind {get;set;}="";
    public string Id {get;set;}="";
    public string World {get;set;}="";
    public int Owner {get;set;}
    public int Character {get;set;}
    public int Scene {get;set;}
    public int Slot {get;set;}
    public int Level {get;set;}
    public int Unit {get;set;}
    public int Feedback {get;set;}
    public long Sequence {get;set;}
    public float[]? Position {get;set;}
    public float[]? Aim {get;set;}
    public float[]? Control {get;set;}
    public int[]? Targets {get;set;}
    public float Damage {get;set;}
    public float Cooldown {get;set;}
    public float Remaining {get;set;}
    public float Boost {get;set;}
    public bool Direct {get;set;}
    public bool Charged {get;set;}
    public float Health {get;set;}
    public float MaxHealth {get;set;}
    public float Speed {get;set;}
    public int Construct {get;set;}=-1;
}

// Forge sends monotonically numbered events over one reliable, ordered channel.
// A unit identifies the visual object, not a separate sequence stream. Remember
// each character/kind high-water mark so retired projectiles cannot fill a
// lifetime cache and silently disable every subsequent visual.
public sealed class ForgeReceipts
{
    private readonly Dictionary<(int Character,string Kind),long> latest=new();
    public int Count=>latest.Count;
    public bool Claim(int character,long sequence,string kind,int unit=0)
    {
        if(character<0 || sequence<=0 || unit<0 || string.IsNullOrEmpty(kind) || kind.Length>20)return false;
        var key=(character,kind);
        if(latest.TryGetValue(key,out var previous) && sequence<=previous)return false;
        latest[key]=sequence;return true;
    }
    public void Clear()=>latest.Clear();
}
