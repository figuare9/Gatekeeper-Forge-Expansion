using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Enemy.Collector.Projectile;
using Gatekeeper.Mods;
using Gatekeeper.NPC;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using NVector=System.Numerics.Vector3;

namespace Gatekeeper.CustomMods;

internal static partial class ForgeWorld
{
    private static readonly Il2CppStructArray<RaycastHit> seekerHits=new(64);
    private static readonly List<RaycastHit> sortedSeekerHits=new(64);
    private static readonly List<(NpcCharacter N,Vector3 At,float Distance)> seekerCandidates=new(128);
    internal static int SeekerHits {get;private set;}
    internal static int SeekerHops {get;private set;}
    private static NVector N(Vector3 v)=>new(v.x,v.y,v.z);
    private static Vector3 U(NVector v)=>new(v.X,v.Y,v.Z);
    private static Vector3 Centre(NpcCharacter n)=>n.Collider!=null ? n.Collider.bounds.center:n.CachedTransform.position+Vector3.up*.8f;
    private static bool InEngagement(Rivet r,NpcCharacter n)=>Enemy(n) && !r.Flight.HasHit(n.ObjectId) &&
        n.gameObject.scene.handle==r.Owner.Player.gameObject.scene.handle &&
        (n.CachedTransform.position-r.EngagementOrigin).sqrMagnitude<=Mathf.Pow(ForgeRules.SentryRange(r.Level),2);
    private static bool Seek(Rivet r,bool impact=false)
    {
        if(r.Flight.Finished || ++r.Redirects>16)return false;
        seekerCandidates.Clear();
        foreach(var n in enemies)
        {
            if(!InEngagement(r,n))continue;
            var at=Centre(n);var distance=(at-r.Position).sqrMagnitude;
            if(distance<=ForgeRules.SentrySeekRadius*ForgeRules.SentrySeekRadius && distance>.0025f)
                seekerCandidates.Add((n,at,distance));
        }
        seekerCandidates.Sort((a,b)=>a.Distance.CompareTo(b.Distance));
        foreach(var c in seekerCandidates)
        {
            if(!CannonWorld.Sight(r.Position,c.At))continue;
            r.SeekTarget=c.N;r.SeekTargetId=c.N.ObjectId;
            var length=Mathf.Sqrt(c.Distance);
            // Retain the exiting tangent, with a small lift on wide turns.
            var control=r.Position+r.Direction*Math.Min(length*.32f,1.8f)+Vector3.up*Math.Min(.3f,length*.035f);
            r.Arc=new(N(r.Position),N(control),N(c.At));r.ArcDistance=0;r.NextGuide=Time.time+.05f;
            PublishLeg(r,r.Position,control,c.At,Math.Max(.008f,r.Arc.Length/ForgeRules.RivetSpeed),impact);
            SeekerHops++;return true;
        }
        return false;
    }
    private static void PublishLeg(Rivet r,Vector3 from,Vector3 control,Vector3 to,float duration,bool impact=false)
    {
        var m=Message(r.Owner,"rivet-turn",2,from,to);m.Control=A(control);m.Unit=r.Unit;m.Level=r.Level;
        m.Charged=r.Charged;m.Remaining=duration;m.Speed=ForgeRules.RivetSpeed;m.Direct=impact;Publish(m);
    }
    private static bool AdvanceSeeker(Rivet r)
    {
        if(Time.time-r.Created>=ForgeRules.SentryFlightLifetime || r.Flight.Finished)return true;
        var budget=r.Flight.Advance(Time.deltaTime);if(budget<=0)return false;
        if(r.Arc==null && (r.Position-r.EngagementOrigin).sqrMagnitude>Mathf.Pow(ForgeRules.SentryRange(r.Level),2))return true;
        if(r.Arc!=null)
        {
            if(r.SeekTarget==null || r.SeekTarget.ObjectId!=r.SeekTargetId || !InEngagement(r,r.SeekTarget))
            {if(!Seek(r))return true;}
            else
            {
                var target=Centre(r.SeekTarget);
                if((target-U(r.Arc.From)).sqrMagnitude>Mathf.Pow(ForgeRules.SentrySeekRadius+1,2))return true;
                var moved=NVector.DistanceSquared(r.Arc.To,N(target))>.0004f;r.Arc.To=N(target);
                if(moved && Time.time>=r.NextGuide)
                {
                    r.NextGuide=Time.time+.05f;
                    var tangent=U(r.Arc.Tangent(r.ArcDistance/r.Arc.Length)).normalized;
                    var remaining=Math.Max(.008f,(r.Arc.Length-r.ArcDistance)/ForgeRules.RivetSpeed);
                    PublishLeg(r,r.Position,r.Position+tangent*Math.Min(1,(target-r.Position).magnitude*.32f),target,remaining);
                }
            }
        }
        // Step along the curve in 0.5 m travel-budget increments. Continuous sweeps prevent fast
        // projectiles tunnelling, including through slopes and thin walls.
        while(budget>.0001f)
        {
            var step=Math.Min(.5f,budget);budget-=step;
            var end=r.Position+r.Direction*step;
            if(r.Arc!=null){r.ArcDistance+=step;end=U(r.Arc.Point(r.ArcDistance/Math.Max(.001f,r.Arc.Length)));}
            var travel=end-r.Position;var distance=travel.magnitude;
            if(distance<.00001f)return true;
            var direction=travel/distance;r.Direction=direction;
            sortedSeekerHits.Clear();
            var count=Physics.defaultPhysicsScene.SphereCast(r.Position,ForgeRules.RivetRadius,direction,seekerHits,distance,~0,QueryTriggerInteraction.Collide);
            // NonAlloc results are unordered. A saturated buffer must never
            // drop a wall or a nearer victim; fall back only for that segment.
            if(count>=seekerHits.Length)
                foreach(var hit in Physics.Query_SphereCastAll(Physics.defaultPhysicsScene,r.Position,ForgeRules.RivetRadius,direction,distance,~0,QueryTriggerInteraction.Collide))sortedSeekerHits.Add(hit);
            else for(var i=0;i<count;i++)sortedSeekerHits.Add(seekerHits[i]);
            sortedSeekerHits.Sort((a,b)=>a.distance.CompareTo(b.distance));
            var redirected=false;
            foreach(var hit in sortedSeekerHits)
            {
                var collider=hit.collider;if(collider==null || collider.GetComponentInParent<CharManager>()!=null)continue;
                var enemy=collider.GetComponentInParent<NpcCharacter>();
                if(enemy!=null)
                {
                    if(!InEngagement(r,enemy) || !r.Flight.Hit(enemy.ObjectId))continue;
                    r.Position+=direction*Math.Clamp(hit.distance,0,distance);
                    Damage(r.Owner,2,enemy.CachedTransform.position,new[]{enemy},ForgeRules.Damage(2,r.Level)*r.DamageBoost*ForgeRules.SentryHitMultiplier(r.Flight.HitCount,r.Charged),hitOrdinal:r.Flight.HitCount);
                    SeekerHits++;
                    if(r.Flight.Finished || !Seek(r,true)){r.FinalImpact=true;return true;}
                    redirected=true;break;
                }
                var skull=collider.GetComponentInParent<CollectorSkullProjectile>();
                if(skull!=null)
                {if(SlotNetwork.IsHost && skull.IsSpawned && skull.IsServerStarted && skull.gameObject.scene.handle==r.Owner.Player.gameObject.scene.handle)skull.Destruct();continue;}
                if(collider.isTrigger)continue;
                r.Position+=direction*Math.Clamp(hit.distance,0,distance);RivetWallStops++;return true;
            }
            if(redirected)continue;
            r.Position=end;
            // A moved/dead target may be missed physically. Redirect once at
            // the endpoint; damage is still possible only after a swept hit.
            if(r.Arc!=null && r.ArcDistance>=r.Arc.Length && !Seek(r))return true;
        }
        return r.Flight.Finished;
    }
}
