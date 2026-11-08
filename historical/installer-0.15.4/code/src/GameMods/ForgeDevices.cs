using Gatekeeper.Char_Scripts.Base;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Construct.HalfmetalHead;
using Gatekeeper.Construct.HalfmetalHead.Attacks;
using Gatekeeper.General.CharactersStats.MoveSpeed;
using Gatekeeper.Infrastructure.Factories;
using Gatekeeper.Interfaces;
using Gatekeeper.Mods;
using Gatekeeper.NPC;
using Gatekeeper.PoolScripts;
using Gatekeeper.Utility;
using HarmonyLib;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static partial class ForgeWorld
{
    private static readonly HashSet<int> constructIds=new();
    private sealed class ConstructPresentation(NpcCharacter npc)
    {
        internal readonly Renderer[] Renderers=npc.GetComponentsInChildren<Renderer>(true).ToArray();
        internal readonly HalfmetalHeadMeleeAttack[] Attacks=npc.GetComponentsInChildren<HalfmetalHeadMeleeAttack>(true).ToArray();
        internal readonly Pathfinding.AIPath? Path=npc.GetComponentInChildren<Pathfinding.AIPath>(true);
    }
    private static readonly Dictionary<IntPtr,ConstructPresentation> constructPresentation=new();
    internal static void ForgetConstruct(IntPtr pointer)=>constructPresentation.Remove(pointer);
    private sealed record Burn(Engineer Owner,Vector3 At,float End);
    private static readonly List<Burn> burns=new();
    private static readonly Dictionary<int,NpcCharacter> slowed=new();
    private static readonly MoveSpeedModificator slow=new(-.3f);
    private const SpeedModificatorSource SlowSource=(SpeedModificatorSource)240;
    private static float nextBurn,nextSlow;
    private static IStatModificator<SpeedModificatorSource,MoveSpeedModificator> Speed(NpcCharacter npc)=>npc.MoveData.Cast<IStatModificator<SpeedModificatorSource,MoveSpeedModificator>>();
    private static void PatchConstructs(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(NpcCharacter),nameof(NpcCharacter.GetDamageAfterProtection)),postfix:new(typeof(ForgeWorld),nameof(ProtectConstruct)));
        h.Patch(AccessTools.Method(typeof(HalfmetalHeadAi),nameof(HalfmetalHeadAi.TryServerTeleport)),prefix:new(typeof(ForgeWorld),nameof(StockConstructAction)));
        foreach(var name in new[]{"ServerProcessUpdate","ServerStartAttack","OwnerHandleOnDamageTriggerDealDamage"})
            h.Patch(AccessTools.Method(typeof(HalfmetalHeadMeleeAttack),name),prefix:new(typeof(ForgeWorld),nameof(StockConstructAction)));
    }
    private static void ProtectConstruct(NpcCharacter __instance,ref float __result)
    {
        // Constructs forward through CharData.ServerTakeDamage instead. Keep
        // this path for other allied NPCs so protection cannot apply twice.
        if(!Combat || !SlotNetwork.IsHost || !AllyNpc(__instance) || __instance.IsConstruct || !float.IsFinite(__result) || __result<=0)return;
        var protection=0f;
        foreach(var d in devices)
            if(d.Slot==4 && Alive(d.Owner.Player) && d.Owner.Player.gameObject.scene.handle==__instance.gameObject.scene.handle && d.End>Time.time &&
                (__instance.transform.position-d.Position).sqrMagnitude<=Mathf.Pow(ForgeRules.Radius(d.Level),2))
                protection=Math.Max(protection,ForgeRules.Protection(d.Level));
        __result*=1-ForgeRules.Reduction(protection);
    }
    private static bool StockConstructAction(Component __instance)
    {
        var npc=__instance.GetComponentInParent<NpcCharacter>();
        return npc==null || !constructIds.Contains(npc.ObjectId);
    }
    private static NpcCharacter? SpawnConstruct(Engineer e,Vector3 at,int level)
    {
        try
        {
            var factory=new ConstructFactory(PoolManager.Instance);
            var data=factory.Create(PoolItemID.HalfmetalHeadConstruct,e.Player,at,Quaternion.identity,ForgeRules.SentryHealth(level)/180,0,false);
            var npc=data?.ConstructCharacter?.TryCast<NpcCharacter>();
            if(npc==null)return null;
            constructIds.Add(npc.ObjectId);ConfigureConstruct(npc,at);
            Plugin.Logger.LogInfo($"Forge sentry native construct {npc.ObjectId}: health={npc.MaxHealth}, owner={e.Player.ObjectId}.");
            return npc;
        }
        catch(Exception ex){Plugin.Logger.LogError("Forge sentry allocation: "+ex);return null;}
    }
    private static void ConfigureConstruct(NpcCharacter npc,Vector3 at)
    {
        if(!constructPresentation.TryGetValue(npc.Pointer,out var cached))
        {
            constructPresentation[npc.Pointer]=cached=new(npc);
            npc.SetPushAvailable(false);npc.SetPullAvailable(false);
        }
        var t=npc.CachedTransform;var delta=t.position-at;
        if(delta.sqrMagnitude>.000001f)t.position=at;
        foreach(var r in cached.Renderers)if(r!=null && r.enabled)r.enabled=false;
        foreach(var attack in cached.Attacks)if(attack!=null && attack.enabled)attack.enabled=false;
        var path=cached.Path;
        if(path!=null){if(!path.isStopped)path.isStopped=true;if(path.canMove)path.canMove=false;}
    }
    private static void Attract(Device d)
    {
        if(d.Construct==null || !d.Construct.CanBeTarget)return;
        foreach(var enemy in Near(d.Owner,d.Position,12))
        {
            var current=enemy.AttackTarget;
            var distance=(enemy.CachedTransform.position-d.Position).sqrMagnitude;
            if(current==null || !current.IsActiveAndAlive || distance<(enemy.CachedTransform.position-current.CachedTransform.position).sqrMagnitude*.8f)
            {
                // The native discovery handler deliberately preserves an existing
                // player target. Unsubscribe it before assigning sentry aggro.
                enemy.ResetDetectedTarget();
                enemy.ServerHandleOnNewTargetFound(d.Construct.Cast<ICharacter>());
            }
        }
    }
    private static IEnumerable<NpcCharacter> BlastTargets(Engineer e,Vector3 at,float radius)=>enemies.Where(t=>Enemy(t) &&
        t.gameObject.scene.handle==e.Player.gameObject.scene.handle &&
        ((t.Collider!=null ? t.Collider.ClosestPoint(at):t.CachedTransform.position)-at).sqrMagnitude<=radius*radius &&
        CannonWorld.Sight(at+Vector3.up*.6f,t.Collider!=null ? t.Collider.bounds.center:t.CachedTransform.position+Vector3.up*.6f));
    private static void ServerTick()
    {
        AdvanceRivets();
        TickPassives();
        HealAllies();
        foreach(var push in pushes.Where(s=>Time.time>=s.At).ToArray())
        {
            pushes.Remove(push);var target=push.Target;
            if(!Alive(push.Owner.Player) || !Enemy(target) || target.ObjectId!=push.Id || !target.IsServerStarted || !target.CanBePushed ||
                target.gameObject.scene.handle!=push.Owner.Player.gameObject.scene.handle)continue;
            target.ClientPush(push.Direction,ForgeRules.RivetPushDistance,ForgeRules.RivetPushDuration,false,push.Owner.Player.Cast<ICharacter>());RivetKnockbacks++;
        }
        foreach(var d in devices.ToArray())
        {
            if(!Alive(d.Owner.Player) || Time.time>=d.End || d.Slot==2 && (d.Construct==null || !d.Construct.IsSpawned || Time.time>d.Created+3 && !d.Construct.IsActiveAndAlive))
            {Remove(d);continue;}
            if(d.Construct!=null)ConfigureConstruct(d.Construct,d.Position);
            if(Time.unscaledTime>=d.NextState){d.NextState=Time.unscaledTime+.25f;PublishDevice(d);}
            if(Time.deltaTime<=0)continue;
            var e=d.Owner;
            if(d.Slot==4)
            {
                if(d.Level>=3 && Time.time>=d.NextPulse)
                {
                    d.NextPulse=Time.time+2;
                    foreach(var t in Near(e,d.Position,ForgeRules.Radius(d.Level)))
                    {
                        var outward=t.CachedTransform.position-d.Position;outward.y=0;
                        if(t.CanBePushed && outward.sqrMagnitude>.001f)t.ClientPush(outward.normalized,1.25f,.18f,false,e.Player.Cast<ICharacter>());
                    }
                    var pulse=Message(e,"pulse",4,d.Position,d.Position);pulse.Level=d.Level;Publish(pulse);
                }
                continue;
            }
            if(d.TargetNpc!=null && !Enemy(d.TargetNpc)){d.TargetNpc=null;d.NextTarget=0;}
            if(Time.time>=d.NextTarget)
            {
                d.NextTarget=Time.time+.125f;
                var range=ForgeRules.SentryRange(d.Level);var scene=e.Player.gameObject.scene.handle;
                // Test nearest candidates first and stop at the first clear line.
                // Near(...).OrderBy(...) previously raycast the entire crowd on
                // every rendered frame, even while the gun was cooling down.
                d.TargetNpc=enemies.Where(t=>Enemy(t) && t.gameObject.scene.handle==scene &&
                    (t.CachedTransform.position-d.Position).sqrMagnitude<=range*range)
                    .OrderBy(t=>(t.CachedTransform.position-d.Position).sqrMagnitude)
                    .FirstOrDefault(t=>CannonWorld.Sight(d.Position+Vector3.up*.6f,t.CachedTransform.position+Vector3.up*.6f));
            }
            var target=d.TargetNpc;
            if(target==null){d.NextFire=Time.time+.15f;d.Burst=0;continue;}
            d.Target=target.Collider!=null ? target.Collider.bounds.center:target.CachedTransform.position+Vector3.up*.8f;
            if(d.TargetId!=target.ObjectId){d.TargetId=target.ObjectId;PublishDevice(d);}
            var angles=ForgeEquipment.SentryAngles(d.Position,d.Target,d.Level,d.Burst==1);
            d.AimYaw=Mathf.MoveTowardsAngle(d.AimYaw,angles.Yaw,360*Time.deltaTime);
            d.AimPitch=Mathf.MoveTowards(d.AimPitch,angles.Pitch,180*Time.deltaTime);
            if(Math.Abs(Mathf.DeltaAngle(d.AimYaw,angles.Yaw))>1 || Math.Abs(d.AimPitch-angles.Pitch)>1)continue;
            if(Time.time<d.NextFire)continue;
            var field=devices.Any(f=>f.Slot==4 && SameScene(f.Owner.Player,e.Player) && f.End>Time.time && (f.Position-d.Position).sqrMagnitude<=Mathf.Pow(ForgeRules.Radius(f.Level),2));
            var boosted=TurretBoost(d);
            var rate=ForgeRules.SentryRate(d.Level)*(boosted ? 1+ForgeRules.TurretSpeedBonus:1)*(field ? 1.3f:1);
            var shot=ForgeEquipment.SentryShot(d.Position,d.Target,d.Level,d.Burst==1);
            if((target.CachedTransform.position-d.Position).sqrMagnitude>Mathf.Pow(ForgeRules.SentryRange(d.Level),2) ||
                !CannonWorld.Sight(shot.Muzzle,d.Target)){d.TargetNpc=null;d.NextFire=Time.time+.125f;continue;}
            if(!CannonWorld.Sight(d.Position+Vector3.up*1.2f,shot.Muzzle)){d.NextFire=Time.time+.15f;continue;}
            var charged=d.Volley.Fire(d.Level,d.Burst==1);
            var projectile=new Rivet(e,++unit,shot.Muzzle,shot.Direction,d.Level,2,charged,damageBoost:boosted ? 1+ForgeRules.TurretDamageBonus:1){EngagementOrigin=d.Position};
            AddRivet(projectile,d.Unit,d.Burst==1);Attract(d);PublishDevice(d);
            if(d.Level>=3 && d.Burst==0){d.Burst=1;d.NextFire=Time.time+.10f;}
            else{d.Burst=0;d.NextFire=Time.time+Math.Max(.02f,.85f/rate-(d.Level>=3 ? .10f:0));}
        }
        // One native movement per enemy reaches the centre before detonation.
        // Repeated short, weak pulls were barely visible and competed with one another.
        foreach(var b in blasts.Where(b=>b.Level>=2 && Alive(b.Owner.Player) && Time.time>=b.End-ForgeRules.MagneticWindow && Time.time<b.End-.05f && Time.time>=b.NextPull))
        {
            b.NextPull=Time.time+.1f;
            foreach(var target in Near(b.Owner,b.At,ForgeRules.MagneticRadius))
            {
                var direction=b.At-target.CachedTransform.position;direction.y=0;
                var distance=ForgeRules.MagneticDistance(direction.magnitude);
                if(target.CanBePulled && distance>0 && b.Pulled.Add(target.ObjectId))
                    target.ClientPull(direction.normalized,distance,Math.Min(ForgeRules.MagneticDuration,b.End-Time.time-.03f),true,b.Owner.Player.Cast<ICharacter>());
            }
        }
        foreach(var b in blasts.Where(b=>Time.time>=b.End).ToArray())
        {
            blasts.Remove(b);if(!Alive(b.Owner.Player))continue;
            // Include enemies whose bodies intersect the blast, rather than
            // requiring their root pivot to lie inside the ground circle.
            var targets=BlastTargets(b.Owner,b.At,ForgeRules.GrenadeRadius(b.Level)).ToArray();
            Damage(b.Owner,3,b.At,targets,ForgeRules.Damage(3,b.Level));
            if(b.Level>=1)foreach(var t in targets)if(t.CanBeStunned)t.ClientStun(1,true);
            var blast=Message(b.Owner,"blast",3,b.At,b.At);blast.Level=b.Level;Publish(blast);
            if(b.Level>=3){burns.Add(new(b.Owner,b.At,Time.time+4));var burn=Message(b.Owner,"thermite",3,b.At,b.At);burn.Remaining=4;Publish(burn);}
        }
        burns.RemoveAll(b=>!Alive(b.Owner.Player) || b.End<=Time.time);
        if(Time.time>=nextBurn)
        {
            nextBurn=Time.time+.5f;
            // Each enemy receives only the strongest overlapping thermite source.
            foreach(var target in enemies.Where(Enemy))
            {
                var source=burns.Where(b=>b.Owner.Player.gameObject.scene.handle==target.gameObject.scene.handle &&
                    (b.At-target.CachedTransform.position).sqrMagnitude<=25 && CannonWorld.Sight(b.At+Vector3.up*.4f,target.CachedTransform.position+Vector3.up*.6f))
                    .OrderByDescending(b=>b.Owner.Player.Data.DamageData.FinalDamage).FirstOrDefault();
                if(source!=null)Damage(source.Owner,3,target.CachedTransform.position,new[]{target},.4f);
            }
        }
        if(Time.time>=nextSlow)
        {
            nextSlow=Time.time+.15f;var inside=new HashSet<int>();
            foreach(var f in devices.Where(d=>d.Slot==4 && d.Level>=2))
                foreach(var target in Near(f.Owner,f.Position,ForgeRules.Radius(f.Level)))
                {inside.Add(target.ObjectId);if(!slowed.ContainsKey(target.ObjectId)){Speed(target).AddModificator(SlowSource,slow);slowed[target.ObjectId]=target;}}
            foreach(var pair in slowed.ToArray())
                if(!inside.Contains(pair.Key) || !Enemy(pair.Value))
                {if(pair.Value!=null && pair.Value.IsSpawned)Speed(pair.Value).RemoveModificator(SlowSource,slow,false);slowed.Remove(pair.Key);}
        }
    }
}
