using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Enemy.Base;
using Gatekeeper.General.Audio;
using Gatekeeper.General.LocalPool;
using Gatekeeper.General.Predictor;
using Gatekeeper.Mods;
using Gatekeeper.NPC;
using Gatekeeper.Other.Audio;
using Gatekeeper.PoolScripts;
using Gatekeeper.Utility;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
using NumericsVector=System.Numerics.Vector3;

namespace Gatekeeper.CustomMods;

internal static class CannonWorld
{
    private readonly record struct Key(int Owner,int Unit);
    private sealed class Agent(CharManager player,int unit)
    {
        internal readonly CharManager Player=player;
        internal readonly int Character=player.NetworkObject.ObjectId,Scene=player.gameObject.scene.handle;
        internal readonly Key Key=new(player.OwnerId,unit);
        internal readonly string World=Guid.NewGuid().ToString("N");
        internal readonly CannonFollower Follower=new(player,unit);
        internal Vector3 Aim=player.CachedTransform.position+Vector3.forward*3;
        internal float NextFire=Time.time+1+unit*.13f,NextState,NextTarget;
        internal NpcCharacter? Target;
    }
    private sealed class Replica(CannonArt art,CharManager player,string world)
    {
        internal readonly CannonArt Art=art;
        internal readonly CharManager Player=player;
        internal readonly string World=world;
        internal readonly CannonPoseBuffer Poses=new();
        internal Vector3 Aim=art.Root.transform.position+Vector3.forward;
        internal float LastAt=Time.unscaledTime;
        internal int Warp=-1;
    }
    private sealed record Shot(Agent Agent,long Sequence,Vector3 To,float End,float Damage);
    private sealed record Ball(GameObject Object,Vector3 From,Vector3 To,float Start,float Duration);
    private static readonly Dictionary<Key,Agent> agents=new();
    private static readonly Dictionary<Key,Replica> replicas=new();
    private static readonly List<Shot> shots=new();
    private static readonly List<Ball> balls=new();
    private static readonly CannonReceipts receipts=new();
    private static NpcCharacter[] enemies=Array.Empty<NpcCharacter>();
    private static float nextEnemies;
    private static long sequence;
    private static string? lastError;
    private static bool Gameplay=>CannonRegistry.Enabled && CannonRules.InCombatLevel(GlobalPredictor.GameState==GameState.Gameplay,(int)GlobalPredictor.CurrentScene);
    private static int Stacks(CharManager p)=>p.ItemManager._itemsSync.TryGetValue(CannonRegistry.Id,out var n) ? CannonRules.Count(n):0;
    private static float[] A(Vector3 p)=>new[]{p.x,p.y,p.z};
    private static bool Vector(float[]? p)=>p?.Length==3 && p.All(float.IsFinite);
    private static Vector3 V(float[] p)=>new(p[0],p[1],p[2]);
    private static NumericsVector N(Vector3 p)=>new(p.x,p.y,p.z);
    private static Vector3 V(NumericsVector p)=>new(p.X,p.Y,p.Z);
    private static bool Enemy(NpcCharacter? e)
    {
        try{return e!=null && e.IsSpawned && e.IsActiveAndAlive && e.CanTakeDamage && e.TryCast<IEnemy>()!=null;}
        catch{return false;}
    }
    private static void Cache()
    {
        if(Time.unscaledTime<nextEnemies)return;nextEnemies=Time.unscaledTime+.3f;
        enemies=GameWorldIndex.Npcs.Where(Enemy).ToArray();
    }
    internal static bool Sight(Vector3 from,Vector3 to)
    {
        var d=to-from;var length=d.magnitude;if(length<.1f)return true;
        foreach(var hit in Physics.RaycastAll(from,d/length,length,~0,QueryTriggerInteraction.Ignore))
        {
            var c=hit.collider;
            if(c==null || hit.distance>=length-.4f || c.GetComponentInParent<NpcCharacter>()!=null || c.GetComponentInParent<CharManager>()!=null)continue;
            return false;
        }
        return true;
    }
    internal static void Tick()
    {
        try
        {
            if(!Gameplay || !SlotNetwork.Ready || CannonRegistry.Info==null)
            {if(agents.Count+replicas.Count+balls.Count>0)Clear();return;}
            Cache();
            if(SlotNetwork.IsHost)ServerTick();
        }
        catch(Exception ex){Report(ex);}
    }
    // AIPath moves during Unity's updates. Render afterwards, every frame,
    // without making the host wait for its own 10 Hz replication messages.
    internal static void Render()
    {
        try
        {
            if(!Gameplay){if(agents.Count+replicas.Count+balls.Count>0)Clear();return;}
            foreach(var pair in replicas.ToArray())
            {
                var key=pair.Key;var r=pair.Value;
                if(r.Player==null || !r.Player.IsSpawned || !r.Player.IsActiveAndAlive || !r.Art.Intact() || Time.unscaledTime-r.LastAt>8)
                {r.Art.Destroy();replicas.Remove(key);continue;}
                Vector3 position;float yaw;
                if(SlotNetwork.IsHost && agents.TryGetValue(key,out var a) && a.World==r.World && a.Follower.Root!=null)
                {position=a.Follower.Position;yaw=a.Follower.Yaw;r.Aim=a.Aim;}
                else
                {var pose=r.Poses.At(Time.unscaledTime);position=V(pose.Position);yaw=pose.Yaw;}
                r.Art.Root.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
                r.Art.Animate(r.Aim);
            }
            foreach(var ball in balls.ToArray())
            {
                var t=(Time.time-ball.Start)/ball.Duration;
                if(t>=1){Object.Destroy(ball.Object);balls.Remove(ball);Explosion(ball.To);continue;}
                if(ball.Object!=null)ball.Object.transform.position=Vector3.Lerp(ball.From,ball.To,t)+Vector3.up*(Mathf.Sin(t*Mathf.PI)*.5f);
            }
        }
        catch(Exception ex){Report(ex);}
    }
    private static void Report(Exception ex)
    {if(lastError!=ex.Message){lastError=ex.Message;Plugin.Logger.LogError("Cannon Wheel: "+ex);}}
    private static void ServerTick()
    {
        var valid=new HashSet<Key>();
        foreach(var peer in SlotNetwork.Participants)
        {
            if(!CannonProgress.EnabledFor(peer.ClientId))continue;
            var p=PlayableCharactersHolder.GetCharacterByClientId(peer.ClientId);
            if(p==null || !p.IsServerStarted || !p.IsActiveAndAlive)continue;
            var count=Stacks(p);
            for(var unit=0;unit<count;unit++)
            {
                var key=new Key(peer.ClientId,unit);valid.Add(key);
                if(agents.TryGetValue(key,out var old) && (old.Player==null || old.Player.Pointer!=p.Pointer || old.Scene!=p.gameObject.scene.handle || old.Follower.Root==null))RemoveAgent(key);
                if(!agents.TryGetValue(key,out var agent))
                {agent=new(p,unit);agents.Add(key,agent);}
                var position=agent.Follower.Position;var origin=CannonArt.AimOrigin(position);
                var target=SelectTarget(agent,p,position);
                Vector3? targetAt=target!=null ? target.CachedTransform.position+Vector3.up*.7f:null;
                var visible=targetAt.HasValue && Sight(origin,targetAt.Value);
                agent.Follower.Tick(target!=null ? target.CachedTransform.position:null,visible);
                agent.Aim=targetAt.HasValue ? V(CannonRules.PredictAim(N(origin),N(targetAt.Value),N(TargetVelocity(target!))))
                    :position+agent.Follower.Direction*4+Vector3.up;
                if(Time.unscaledTime>=agent.NextState)
                {
                    agent.NextState=Time.unscaledTime+.1f;
                    Publish(Message(agent,"state",++sequence,count,position));
                }
                var inRange=targetAt.HasValue && (targetAt.Value-origin).sqrMagnitude<=CannonRules.Range*CannonRules.Range;
                if(Time.deltaTime<=0 || !CannonRules.CanFire(true,Gameplay,p.IsActiveAndAlive,count,
                    visible && inRange && !agent.Follower.Returning,Time.time,agent.NextFire))continue;
                // Lead must not aim through scenery. A clear current position is
                // a valid lower-accuracy fallback when prediction crosses a wall.
                if(!Sight(origin,agent.Aim))agent.Aim=targetAt!.Value;
                var damage=p.Data.DamageData.FinalDamage*CannonRules.DamageMultiplier(1);
                if(!CannonRules.ValidDamage(damage))continue;
                agent.NextFire=Time.time+CannonRules.Cooldown(1);
                var from=CannonArt.ShotOrigin(position,agent.Aim);
                var flight=CannonRules.Flight((agent.Aim-from).magnitude);var id=++sequence;
                shots.Add(new(agent,id,agent.Aim,Time.time+flight,damage));
                var shot=Message(agent,"shot",id,count,from);shot.Flight=flight;Publish(shot);
            }
        }
        foreach(var key in agents.Keys.Where(k=>!valid.Contains(k)).ToArray())RemoveAgent(key);
        foreach(var shot in shots.Where(s=>Time.time>=s.End).ToArray())
        {
            shots.Remove(shot);var a=shot.Agent;var p=a.Player;
            if(p==null || !p.IsSpawned || !p.IsActiveAndAlive || !CannonRules.HasUnit(Stacks(p),a.Key.Unit) ||
                !agents.TryGetValue(a.Key,out var current) || current.World!=a.World)continue;
            var targets=enemies.Where(e=>Enemy(e) && e.gameObject.scene.handle==p.gameObject.scene.handle &&
                (e.CachedTransform.position+Vector3.up*.7f-shot.To).sqrMagnitude<=CannonRules.BlastRadius*CannonRules.BlastRadius &&
                Sight(shot.To,e.CachedTransform.position+Vector3.up*.7f))
                .Select(e=>e.NetworkObject.ObjectId).Distinct().Take(12).ToArray();
            var hit=Message(a,"hit",shot.Sequence,Stacks(p),shot.To);hit.Targets=targets;hit.Damage=shot.Damage;
            SlotNetwork.Send(p.Owner,new(){Kind="cannon",Cannon=hit});
        }
    }
    private static NpcCharacter? SelectTarget(Agent a,CharManager player,Vector3 position)
    {
        var owner=player.CachedTransform.position;
        bool Track(NpcCharacter? e,bool retained)=>Enemy(e) && e!.gameObject.scene.handle==player.gameObject.scene.handle &&
            CannonRules.CanTrack((e.CachedTransform.position-owner).magnitude,(e.CachedTransform.position-position).magnitude,retained);
        if(a.Follower.Returning){a.Target=null;return null;}
        if(Track(a.Target,true))return a.Target;
        a.Target=null;
        if(Time.unscaledTime<a.NextTarget)return null;
        a.NextTarget=Time.unscaledTime+.25f;
        var candidates=enemies.Where(e=>Track(e,false)).OrderBy(e=>(e.CachedTransform.position-position).sqrMagnitude).Take(8).ToArray();
        a.Target=candidates.FirstOrDefault(e=>Sight(position+Vector3.up,e.CachedTransform.position+Vector3.up*.7f)) ?? candidates.FirstOrDefault();
        return a.Target;
    }
    private static Vector3 TargetVelocity(NpcCharacter target)
    {
        try
        {
            var data=target.BaseData;
            if(data?.AIPath!=null)return data.AIPath.velocity;
            if(data?.Rigidbody!=null)return data.Rigidbody.velocity;
        }
        catch { /* Pooled/dead targets use their last visible point. */ }
        return Vector3.zero;
    }
    private static CannonMessage Message(Agent a,string kind,long id,int stacks,Vector3 position)=>new()
    {
        Kind=kind,Owner=a.Key.Owner,Unit=a.Key.Unit,Character=a.Character,World=a.World,Sequence=id,Stacks=stacks,
        Scene=(int)GlobalPredictor.CurrentScene,Position=A(position),Aim=A(a.Aim),Velocity=A(a.Follower.Velocity),Yaw=a.Follower.Yaw,Warp=a.Follower.Warp
    };
    private static void Publish(CannonMessage message)=>SlotNetwork.Publish(new(){Kind="cannon",Cannon=message});
    private static void RemoveAgent(Key key)
    {
        if(!agents.Remove(key,out var a))return;
        Publish(new(){Kind="remove",Owner=key.Owner,Unit=key.Unit,Character=a.Character,World=a.World,Scene=(int)GlobalPredictor.CurrentScene,Sequence=++sequence});
        a.Follower.Destroy();
    }
    internal static void Receive(CannonMessage m)
    {
        if(m.Kind=="unlock"){if(m.Owner==SlotNetwork.LocalId)CannonProgress.Award(m.Round);return;}
        if(!Gameplay || m.Scene!=(int)GlobalPredictor.CurrentScene || !SlotRules.IsRequestId(m.World) || m.Unit<0 || m.Unit>=CannonRules.MaxStacks)return;
        var p=PlayableCharactersHolder.GetCharacterByClientId(m.Owner);
        if(p==null || !p.IsSpawned || p.NetworkObject.ObjectId!=m.Character)return;
        var key=new Key(m.Owner,m.Unit);
        if(m.Kind=="remove")
        {
            if(!receipts.Claim(m.Character,m.Sequence,"remove",m.Unit))return;
            if(replicas.TryGetValue(key,out var removed) && removed.World==m.World){removed.Art.Destroy();replicas.Remove(key);}return;
        }
        if(m.Kind=="state")
        {
            if(!Vector(m.Position) || !Vector(m.Aim) || !Vector(m.Velocity) || !float.IsFinite(m.Yaw) || m.Warp<0 ||
                !CannonRules.HasUnit(m.Stacks,m.Unit) || !receipts.Claim(m.Character,m.Sequence,"state",m.Unit))return;
            if(replicas.TryGetValue(key,out var previous) && previous.World!=m.World){previous.Art.Destroy();replicas.Remove(key);}
            if(!replicas.TryGetValue(key,out var r))
            {
                var art=new CannonArt(V(m.Position!));SceneManager.MoveGameObjectToScene(art.Root,p.gameObject.scene);
                r=new(art,p,m.World);replicas.Add(key,r);
            }
            if(!r.Poses.Push(Time.unscaledTime,N(V(m.Position!)),m.Yaw,N(V(m.Velocity!)),m.Warp))return;
            if(r.Warp!=m.Warp){r.Art.Snap(V(m.Position!),m.Yaw);r.Warp=m.Warp;}
            r.Aim=V(m.Aim!);r.LastAt=Time.unscaledTime;return;
        }
        if(!replicas.TryGetValue(key,out var replica) || replica.World!=m.World)return;
        if(m.Kind=="shot")
        {
            if(!Vector(m.Position) || !Vector(m.Aim) || !float.IsFinite(m.Flight) || m.Flight<.08f || m.Flight>.75f ||
                !receipts.Claim(m.Character,m.Sequence,"shot",m.Unit))return;
            replica.Art.Fire();
            var muzzle=replica.Art.Muzzle;
            var flash=GameObject.CreatePrimitive(PrimitiveType.Sphere);SceneManager.MoveGameObjectToScene(flash,p.gameObject.scene);
            flash.transform.position=muzzle;flash.transform.localScale=Vector3.one*.38f;
            flash.GetComponent<Renderer>().sharedMaterial=CannonArt.MaterialFor("orange");flash.GetComponent<Collider>().enabled=false;Object.Destroy(flash,.07f);
            var obj=GameObject.CreatePrimitive(PrimitiveType.Sphere);obj.name="Cannon Wheel shell";SceneManager.MoveGameObjectToScene(obj,p.gameObject.scene);
            obj.transform.position=muzzle;obj.transform.localScale=Vector3.one*.23f;obj.GetComponent<Renderer>().sharedMaterial=CannonArt.MaterialFor("bore");
            var collider=obj.GetComponent<Collider>();collider.enabled=false;Object.Destroy(collider);
            balls.Add(new(obj,muzzle,V(m.Aim!),Time.time,m.Flight));
            try{AudioController.PlayOneShot(AudioClipTag.CannonadeHit,muzzle);}catch(Exception ex){Plugin.Logger.LogWarning("Cannon sound: "+ex.Message);}
        }
        else if(m.Kind=="hit")
        {
            if(!p.IsOwner || m.Owner!=SlotNetwork.LocalId || !CannonRules.HasUnit(p.ItemManager.GetItemQuantity(CannonRegistry.Id),m.Unit) ||
                m.Targets==null || m.Targets.Length>12 || !Vector(m.Position) || !CannonRules.ValidDamage(m.Damage) ||
                !receipts.Claim(m.Character,m.Sequence,"hit",m.Unit))return;
            foreach(var target in m.Targets.Distinct())
            {
                var enemy=enemies.FirstOrDefault(e=>Enemy(e) && e.NetworkObject.ObjectId==target && e.gameObject.scene.handle==p.gameObject.scene.handle);
                if(enemy==null || (enemy.CachedTransform.position+Vector3.up*.7f-V(m.Position!)).sqrMagnitude>4*4)continue;
                // Only the owning player executes each host-approved unit hit once.
                DamageDealer.PlayerDealDamage(p,PlayableCharacterHitType.Item,enemy.gameObject,m.Damage,false,
                    new HitPointInfo(HitPositionType.BodyCenter,PoolItemID.ItemCannonadeExplosion,enemy.CachedTransform.position,Quaternion.identity),
                    DamageType.Normal,true,true,true,false,false,true);
            }
        }
    }
    private static void Explosion(Vector3 at)
    {
        try
        {
            LocalPoolManager.Get(PoolItemID.ItemCannonadeExplosion,at,Quaternion.identity,null,Vector3.one*.75f);
            AudioController.PlayOneShot(AudioClipTag.CannonadeHit,at);
        }
        catch(Exception ex){Plugin.Logger.LogWarning("Cannon explosion FX: "+ex.Message);}
    }
    internal static void Clear(bool resetReceipts=false)
    {
        foreach(var a in agents.Values)a.Follower.Destroy();
        foreach(var r in replicas.Values)r.Art.Destroy();
        foreach(var ball in balls)if(ball.Object!=null)Object.Destroy(ball.Object);
        replicas.Clear();agents.Clear();shots.Clear();balls.Clear();enemies=Array.Empty<NpcCharacter>();nextEnemies=0;
        if(resetReceipts){receipts.Clear();sequence=0;}
    }
}
