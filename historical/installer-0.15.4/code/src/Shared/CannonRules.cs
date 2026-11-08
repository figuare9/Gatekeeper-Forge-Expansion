using NumericsVector=System.Numerics.Vector3;

namespace Gatekeeper.Mods;

public static class CannonRules
{
    public const int ItemId=30001, CatalogAchievementId=30001, MaxStacks=10;
    public const string UnlockRequirement="Win a 777 jackpot on Prism Jackpot in Quiet Bay or Quiet Place.";
    public const string Key="gkmm.cannon-wheel";
    public const float Range=24.3f, BlastRadius=2.4f, ProjectileSpeed=42;
    public const float LeadFraction=.65f, MaxLead=3.5f, OwnerLeash=32, TargetOwnerLimit=36;
    // Pinned game build: combat maps/arenas use 100..999; 998 is a quiet room.
    public static bool InCombatLevel(bool gameplay,int scene) => gameplay && scene>=100 && scene<=999 && scene!=998;
    public static int Count(int stacks) => Math.Clamp(stacks,0,MaxStacks);
    public static bool HasUnit(int stacks,int unit) => unit>=0 && unit<Count(stacks);
    // Each stack now adds a full cannon; no additional hidden damage/rate bonus.
    public static float DamageMultiplier(int stacks) => stacks<=0 ? 0 : 2.5f;
    public static float Cooldown(int stacks) => stacks<=0 ? float.PositiveInfinity : 2f;
    public static float FollowSpeed(float distance,float ownerSpeed) => Math.Clamp(Math.Max(6,ownerSpeed+1)+Math.Clamp(distance-2,0,6)*.8f,6,45);
    public static bool ShouldRejoin(float distance,bool pathPending,bool reachable,float stuckSeconds) =>
        distance>32 && !pathPending && !reachable && stuckSeconds>=2.5f;
    public static float Flight(float distance) => Math.Clamp(distance/ProjectileSpeed,.08f,.75f);
    public static bool CanFire(bool host,bool gameplay,bool alive,int stacks,bool hasTarget,float now,float next) =>
        host && gameplay && alive && stacks>0 && hasTarget && float.IsFinite(now) && now>=next;
    public static bool ValidDamage(float damage) => float.IsFinite(damage) && damage>0 && damage<=10_000_000;
    public static bool CanTrack(float ownerDistance,float cannonDistance,bool retained) =>
        ownerDistance>=0 && ownerDistance<=TargetOwnerLimit && cannonDistance>=0 && cannonDistance<=Range*(retained ? 1.3f:1);
    public static CannonMove ChooseMove(CannonMove previous,float ownerDistance,bool enemy,float enemyDistance,bool visible)
    {
        if(ownerDistance>OwnerLeash || (previous==CannonMove.Return && ownerDistance>13))return CannonMove.Return;
        if(!enemy)return CannonMove.Patrol;
        if(enemyDistance<5 || (previous==CannonMove.Retreat && enemyDistance<9))return CannonMove.Retreat;
        if(!visible || enemyDistance>18 || (previous==CannonMove.Advance && enemyDistance>14))return CannonMove.Advance;
        return CannonMove.Hold;
    }
    public static NumericsVector PredictAim(NumericsVector origin,NumericsVector target,NumericsVector velocity)
    {
        velocity.Y=0;
        if(!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Z))return target;
        var speed=velocity.Length();if(speed>20)velocity*=20/speed;
        var time=Flight(NumericsVector.Distance(origin,target));
        var lead=velocity*(LeadFraction*time);
        // One refinement accounts for the predicted point being farther away.
        time=Flight(NumericsVector.Distance(origin,target+lead));lead=velocity*(LeadFraction*time);
        if(lead.LengthSquared()>MaxLead*MaxLead)lead=NumericsVector.Normalize(lead)*MaxLead;
        return target+lead;
    }
}

public enum CannonMove { Patrol, Return, Advance, Hold, Retreat }

// Ranged companion behavior: roam, pursue a firing position, hold/evade, regroup.
// Movement itself remains the game's native AIPath with acceleration/smoothing.
public sealed class CannonTactics(int unit,int seed)
{
    private readonly Random random=new(seed);
    private NumericsVector goal,anchor;
    private float next;
    private bool initialized;
    public CannonMove Mode {get;private set;}=CannonMove.Patrol;
    public NumericsVector Update(NumericsVector owner,NumericsVector cannon,NumericsVector? enemy,bool visible,float now)
    {
        var mode=CannonRules.ChooseMove(Mode,NumericsVector.Distance(owner,cannon),enemy.HasValue,
            enemy.HasValue ? NumericsVector.Distance(cannon,enemy.Value):0,visible);
        var changed=mode!=Mode;Mode=mode;
        if(initialized && !changed && now<next && (mode!=CannonMove.Patrol || NumericsVector.DistanceSquared(owner,anchor)<64))return goal;
        if(mode==CannonMove.Patrol)
        {
            var angle=random.NextDouble()*Math.PI*2;var radius=7+random.NextDouble()*4;
            goal=owner+new NumericsVector((float)Math.Cos(angle)*(float)radius,0,(float)Math.Sin(angle)*(float)radius);
            anchor=owner;next=now+3.5f+(float)random.NextDouble()*3;
        }
        else if(mode==CannonMove.Hold)
        {
            if(!initialized || changed)goal=cannon;
            next=now+.25f;
        }
        else
        {
            var center=mode==CannonMove.Return ? owner:enemy!.Value;
            var away=cannon-center;away.Y=0;
            if(away.LengthSquared()<.01f)away=new((float)Math.Cos(unit*2.4),0,(float)Math.Sin(unit*2.4));
            away=NumericsVector.Normalize(away);
            var radius=mode==CannonMove.Return ? 8:mode==CannonMove.Retreat ? 10:visible ? 13:3.5f;
            var side=new NumericsVector(-away.Z,0,away.X)*((unit%3-1)*1.5f);
            goal=center+away*radius+side;
            var tether=goal-owner;tether.Y=0;
            if(tether.LengthSquared()>30*30)goal=owner+NumericsVector.Normalize(tether)*30;
            next=now+.25f;
        }
        initialized=true;return goal;
    }
}

// Separate monotonic streams per character and message kind. A delayed/replayed
// packet never applies the same approved native hit twice.
public sealed class CannonReceipts
{
    private readonly Dictionary<(int,int,string),long> last=new();
    public bool Claim(int character,long sequence,string kind,int unit=0)
    {
        if (character<0 || unit<0 || unit>=CannonRules.MaxStacks || sequence<=0 || kind is not ("state" or "shot" or "hit" or "remove")) return false;
        var key=(character,unit,kind);
        if (last.TryGetValue(key,out var previous) && sequence<=previous) return false;
        last[key]=sequence; return true;
    }
    public void Clear() => last.Clear();
}

public sealed class CannonProgressData
{
    public int Version { get; set; }=1;
    public Dictionary<string,string> Winners { get; set; }=new();
    public bool Unlocked(string account) => Winners.TryGetValue(account,out var round) && SlotRules.IsRequestId(round);
    public bool Award(string account,string round)
    {
        if (string.IsNullOrWhiteSpace(account) || !SlotRules.IsRequestId(round) || Unlocked(account)) return false;
        Winners[account]=round; return true;
    }
}
public sealed record CannonRunMarker(bool RequiresMod,int ItemId,int Count,string Account);

public readonly record struct CannonPoint(float X,float Z);
// Remote replicas render between buffered snapshots instead of chasing each packet.
public readonly record struct CannonPose(System.Numerics.Vector3 Position,float Yaw);
public sealed class CannonPoseBuffer
{
    private sealed record Sample(float At,CannonPose Pose,System.Numerics.Vector3 Velocity);
    private readonly List<Sample> samples=new();
    private int warp=-1;
    public bool Push(float at,System.Numerics.Vector3 position,float yaw,System.Numerics.Vector3 velocity,int revision)
    {
        if(!float.IsFinite(at) || !float.IsFinite(yaw) || !Finite(position) || !Finite(velocity) || revision<0 ||
            (samples.Count>0 && (at<samples[^1].At || revision<warp)))return false;
        if(revision!=warp){samples.Clear();warp=revision;}
        var sample=new Sample(at,new(position,yaw),velocity);
        if(samples.Count>0 && at==samples[^1].At)samples[^1]=sample;else samples.Add(sample);
        while(samples.Count>8)samples.RemoveAt(0);
        return true;
    }
    private static bool Finite(System.Numerics.Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    public CannonPose At(float now)
    {
        if(samples.Count==0)return default;
        var time=now-.12f;
        if(time<=samples[0].At)return samples[0].Pose;
        for(var i=1;i<samples.Count;i++)
        {
            var b=samples[i];if(time>b.At)continue;
            var a=samples[i-1];var t=(time-a.At)/(b.At-a.At);
            var delta=(b.Pose.Yaw-a.Pose.Yaw+540)%360-180;
            return new(System.Numerics.Vector3.Lerp(a.Pose.Position,b.Pose.Position,t),a.Pose.Yaw+delta*t);
        }
        var last=samples[^1];
        return new(last.Pose.Position+last.Velocity*Math.Clamp(time-last.At,0,.12f),last.Pose.Yaw);
    }
}
