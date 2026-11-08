using Gatekeeper.General.InputSystem.Enums;
using Gatekeeper.CameraScripts.HUD.SkillPanelStuff;
using FishNet.Connection;
using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.Base;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Char_Scripts.General.CharData;
using Gatekeeper.Char_Scripts.Hybrid;
using Gatekeeper.Enemy.Base;
using Gatekeeper.EnvironmentStuff.Spectris;
using Gatekeeper.Interfaces;
using Gatekeeper.General.Audio;
using Gatekeeper.General.InputSystem;
using Gatekeeper.General.LocalPool;
using Gatekeeper.General.Predictor;
using Gatekeeper.Mods;
using Gatekeeper.NPC;
using Gatekeeper.Other.Audio;
using Gatekeeper.PoolScripts;
using Gatekeeper.Utility;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static partial class ForgeWorld
{
    private sealed class Engineer(CharManager p)
    {
        internal readonly CharManager Player=p;
        internal readonly string World=Guid.NewGuid().ToString("N");
        internal readonly ForgePassiveState Passive=new();
        internal readonly Dictionary<int,ForgeChargeGate> Gates=new();
        internal Vector3 StillAt=p.CachedTransform.position;
        internal bool RageApplied;
        internal float NextState;
        internal int Shots;
    }
    private sealed class Device(Engineer e,int slot,int unit,Vector3 at,int level,float end)
    {
        internal readonly Engineer Owner=e;
        internal readonly int Slot=slot,Unit=unit,Level=level;
        internal readonly Vector3 Position=at;
        internal readonly float End=end;
        internal readonly float Created=Time.time;
        internal float NextFire=Time.time+.8f,NextState,NextPulse=Time.time;
        internal NpcCharacter? Construct;
        internal int ConstructId=-1;
        internal Vector3 Target=at+Vector3.forward*10;
        internal int Burst;
        internal readonly ForgeVolley Volley=new();
        internal float AimYaw,AimPitch;
        internal float NextTarget;
        internal NpcCharacter? TargetNpc;
        internal int TargetId=-1;
        internal bool DeathCounted;
    }
    private sealed record Blast(Engineer Owner,Vector3 At,float End,int Level)
    {internal float NextPull;internal readonly HashSet<int> Pulled=new();}
    private sealed record Hit(Engineer Owner,int Slot,int[] Targets,float Until,Vector3 Direction);
    private sealed record Push(Engineer Owner,NpcCharacter Target,int Id,Vector3 Direction,float At);
    private sealed class ShotGroup
    {
        internal readonly HashSet<int> Direct=new(),Secondary=new();
        internal readonly List<(NpcCharacter Target,Vector3 At)> Fragments=new();
        internal bool Complete;
        internal int Flying;
    }
    private sealed class Rivet(Engineer owner,int id,Vector3 from,Vector3 direction,int level,int slot=1,bool charged=false,ShotGroup? group=null,bool fragment=false,float damageBoost=1)
    {
        internal readonly Engineer Owner=owner;
        internal readonly int Unit=id,Level=level;
        internal Vector3 Direction=direction;
        internal readonly int Slot=slot;
        internal readonly bool Charged=charged,Fragment=fragment;
        internal readonly float DamageBoost=damageBoost;
        internal readonly ShotGroup Group=group ?? new();
        internal readonly ForgeRivetFlight Flight=new(fragment ? 1:slot==2 ? ForgeRules.SentryTargets(level):ForgeRules.Penetration(level,charged),fragment ? ForgeRules.FragmentRange:slot==2 ? ForgeRules.SentryRange(level)+ForgeRules.SentrySeekRadius*(ForgeRules.SentryTargets(level)-1)*1.5f:ForgeRules.RivetRange,fragment ? ForgeRules.FragmentSpeed:ForgeRules.RivetSpeed);
        internal readonly HashSet<int> Cups=new();
        internal Vector3 Position=from;
        internal Vector3 EngagementOrigin=from;
        internal readonly float Created=Time.time;
        internal ForgeSeekArc? Arc;
        internal float ArcDistance,NextGuide;
        internal NpcCharacter? SeekTarget;
        internal int SeekTargetId=-1,Redirects;
        internal bool FinalImpact;
    }
    private sealed class Visual(GameObject root,GameObject? ring,CharManager owner)
    {
        internal readonly GameObject Root=root;
        internal readonly GameObject? Ring=ring;
        internal readonly CharManager Owner=owner;
        internal float End;
        internal int Construct=-1;
        internal void Destroy(){if(Root!=null)Object.Destroy(Root);if(Ring!=null)Object.Destroy(Ring);}
    }
    private static readonly Dictionary<int,Engineer> engineers=new();
    private static readonly List<Device> devices=new();
    private static readonly List<Blast> blasts=new();
    private static readonly Dictionary<long,Hit> pendingHits=new();
    private static readonly List<Rivet> rivets=new();
    private static readonly List<Push> pushes=new();
    private static readonly Dictionary<string,Visual> visuals=new();
    private static readonly Dictionary<int,(string Id,float At)> pendingCasts=new();
    private static readonly ForgeReceipts receipts=new(),requests=new();
    private static NpcCharacter[] enemies=Array.Empty<NpcCharacter>();
    private static NpcCharacter[] allNpcs=Array.Empty<NpcCharacter>();
    private static float nextEnemies,nextInput;
    private static long sequence,requestSequence;
    private static int unit;
    private static string? error;
    internal static int ReceiptStreams=>receipts.Count;
    internal static string? CombatError=>error;
    internal static int RivetShots {get;private set;}
    internal static int RivetTargets {get;private set;}
    internal static int RivetWallStops {get;private set;}
    internal static int RivetConfirmed {get;private set;}
    internal static int RivetCups {get;private set;}
    internal static int RivetSkulls {get;private set;}
    internal static int SentryAudioShots {get;private set;}
    internal static int SentryAudioFailures {get;private set;}
    internal static int RivetKnockbacks {get;private set;}
    private static float nextDiagnostics;
    internal static string InputState {get;private set;}="waiting";
    private static int boostedTurrets;
    private static float rageUntil;
    private static bool stationary;
    internal static bool Combat=>ForgeRegistry.Enabled && ForgeRules.Combat(GlobalPredictor.GameState==GameState.Gameplay,(int)GlobalPredictor.CurrentScene);
    internal static void Patch(Harmony h)
    {
        PatchConstructs(h);
        h.Patch(AccessTools.Method(typeof(PlayableCharacterSkills),nameof(PlayableCharacterSkills.OwnerSkillsUpdate)),prefix:new(typeof(ForgeWorld),nameof(Input)));
        h.Patch(AccessTools.Method(typeof(PlayableCharacterSkills),"OwnerHandleOnAttackStateChanged"),prefix:new(typeof(ForgeWorld),nameof(AttackAnimation)));
        h.Patch(AccessTools.Method(typeof(CharData),nameof(CharData.ServerTakeDamage)),prefix:new(typeof(ForgeWorld),nameof(Protect)),postfix:new(typeof(ForgeWorld),nameof(TurretDamageTaken)));
        h.Patch(AccessTools.Method(typeof(SkillIcon),"UpdateIconColor"),prefix:new(typeof(ForgeWorld),nameof(PrimaryIconColor)));
        h.Patch(AccessTools.Method(typeof(SkillIcon),nameof(SkillIcon.PingSkillCd)),prefix:new(typeof(ForgeWorld),nameof(PrimaryIconPing)));
        h.Patch(AccessTools.Method(typeof(SkillIcon),"Refresh"),prefix:new(typeof(ForgeWorld),nameof(PrimaryIconRefresh)));
        // IL2CPP inlines parts of the base dispatcher into native callers.
        // Guard its concrete skill paths too, then poll Forge input directly.
        foreach(var name in new[]{"OwnerFirstSkillUpdate","OwnerSecondSkillUpdate","OwnerThirdSkillUpdate","OwnerFourthSkillUpdate"})
            h.Patch(AccessTools.Method(typeof(PlayableCharacterSkills),name),prefix:new(typeof(ForgeWorld),nameof(SkipDonor)));
        foreach(var name in new[]{"OwnerFirstSkillUpdate","ClientHandleOnFirstSkillPush","OwnerHandleOnSecondSkillStart","OwnerHandleOnSecondSkillPush","OwnerHandleOnThirdSkillStart","OwnerHandleOnThirdSkillOut","OwnerHandleOnFourthSkillStart","OwnerHandleOnFourthSkillPush"})
            h.Patch(AccessTools.DeclaredMethod(typeof(HybridSkills),name),prefix:new(typeof(ForgeWorld),nameof(SkipDonor)));
    }
    private static bool SkipDonor(PlayableCharacterSkills __instance)=>!ForgeRegistry.Is(__instance.characterMain?.Manager);
    private static bool PrimaryIconPing(SkillIcon __instance)=>(int)__instance.SkillType!=1 || !ForgeRegistry.Is(__instance._currentCharacter);
    private static void PrimaryIconRefresh(SkillIcon __instance,ref SkillType __0)
    {
        // Native Refresh uses its argument only to trigger the separate
        // ready/glow UIAnimator. None keeps its charge/text refresh intact.
        if(!PrimaryIconPing(__instance))__0=(SkillType)0;
    }
    private static bool PrimaryIconColor(SkillIcon __instance)
    {
        if(PrimaryIconPing(__instance))return true;
        // Keep actual recharge and native item events; suppress only HUD tint.
        if(__instance.skillImage!=null)__instance.skillImage.color=Color.white;
        return false;
    }
    internal static void PollInput()
    {
        if(!Combat)return;
        var p=PlayableCharactersHolder.MyCharacter;
        if(ForgeRegistry.Is(p) && p!.playableCharacterMain?.Skills!=null)Input(p.playableCharacterMain.Skills);
    }
    private static bool AttackAnimation(PlayableCharacterSkills __instance)=>!ForgeRegistry.Is(__instance.characterMain?.Manager);
    private static void Protect(CharData __instance,ref float damage,out Device? __state)
    {
        __state=null;
        if(!Combat || !SlotNetwork.IsHost || !__instance.IsServerStarted || !float.IsFinite(damage) || damage<=0)return;
        __state=TurretForData(__instance);
        // Native constructs forward damage through CharData as players do.
        var p=__instance.CharManager;if(p==null)return;
        var protection=0f;
        foreach(var d in devices)
            if(d.Slot==4 && Alive(d.Owner.Player) && SameScene(d.Owner.Player,p) && Time.time<d.End &&
                (p.CachedTransform.position-d.Position).sqrMagnitude<=Mathf.Pow(ForgeRules.Radius(d.Level),2))
                protection=Math.Max(protection,ForgeRules.Protection(d.Level));
        damage*=1-ForgeRules.Reduction(protection);
    }
    private static bool Alive(CharManager? p)=>p!=null && p.IsSpawned && p.IsActiveAndAlive && ForgeRegistry.Is(p);
    private static bool SameScene(CharManager a,CharManager b)=>a.gameObject.scene.handle==b.gameObject.scene.handle;
    private static bool Enemy(NpcCharacter? e)=>e!=null && e.IsSpawned && e.IsActiveAndAlive && e.CanTakeDamage && e.TryCast<IEnemy>()!=null;
    private static float[] A(Vector3 p)=>new[]{p.x,p.y,p.z};
    private static Vector3 V(float[] p)=>new(p[0],p[1],p[2]);
    private static int Level(CharManager p,int slot)=>p.Data.SkillsData.Skills.TryGetValue((SkillType)slot,out var data) ? ForgeRules.Level(data.CurrentSkillLevel):0;
    private static Engineer For(CharManager p)
    {
        if(engineers.TryGetValue(p.ObjectId,out var old) && old.Player!=null && old.Player.Pointer==p.Pointer)return old;
        var e=new Engineer(p);engineers[p.ObjectId]=e;return e;
    }
    internal static void ServerReceive(NetworkConnection sender,ForgeMessage m)
    {
        if(!Combat || !SlotNetwork.IsHost || !sender.IsAuthenticated || m.Scene!=(int)GlobalPredictor.CurrentScene)return;
        var p=PlayableCharactersHolder.GetCharacterByClientId(sender.ClientId);
        if(!Alive(p) || p!.ObjectId!=m.Character || !p.IsServerStarted || !SlotNetwork.ForgePeer(sender.ClientId))return;
        var e=For(p);
        if(m.Kind=="confirm")
        {
            if(!pendingHits.TryGetValue(m.Sequence,out var hit) || hit.Owner!=e || hit.Slot!=1 || m.Targets is not {Length:>0 and <=32} ||
                !m.Targets.Any(id=>hit.Targets.Contains(id)))return;
            pendingHits.Remove(m.Sequence);
            foreach(var id in m.Targets.Intersect(hit.Targets).Distinct())
            {
                var target=enemies.FirstOrDefault(n=>Enemy(n) && n.ObjectId==id);
                if(target!=null && hit.Direction.sqrMagnitude>.5f)
                    pushes.Add(new(e,target,id,hit.Direction,Time.time+.05f));
            }
            return;
        }
        if(m.Kind!="cast" || m.Slot<1 || m.Slot>4 || !SlotRules.IsRequestId(m.Id) || !ForgeRules.ValidPoint(m.Aim) ||
            !requests.Claim(m.Character,m.Sequence,"cast",m.Slot))return;
        if(m.Slot==1 && rivets.Count(r=>r.Owner==e)>=48){Reject(p,m);return;}
        if(!p.Data.SkillsData.Skills.TryGetValue((SkillType)m.Slot,out var skill) || !skill.IsAvailable){Reject(p,m);return;}
        var level=Level(p,m.Slot);
        var cd=m.Slot==1 ? ForgeRules.RivetCooldown(p.playableCharacterMain.Skills.AttackSpeed):p.playableCharacterMain.Skills.GetSkillCooldown((SkillType)m.Slot);
        if(m.Slot==2 && level>=1)cd*=.75f;
        cd=Math.Clamp(cd,.025f,120);
        if(!e.Gates.TryGetValue(m.Slot,out var gate)){gate=new();e.Gates[m.Slot]=gate;}
        var origin=p.CachedTransform.position;var delta=V(m.Aim!)-origin;
        if(delta.sqrMagnitude<.01f)delta=p.CachedTransform.forward;
        var aim=origin+Vector3.ClampMagnitude(delta,ForgeRules.CastRange(m.Slot));
        if(m.Slot>1 && !TryPlacement(p,aim,m.Slot,out aim)){Reject(p,m);return;}
        if(!gate.TrySpend(Time.time,Math.Clamp(skill.MaxCharges,1,50),cd)){Reject(p,m);return;}
        // A failed native construct allocation must not consume a charge.
        NpcCharacter? construct=null;
        if(m.Slot==2){construct=SpawnConstruct(e,aim,level);if(construct==null){Reject(p,m);gate.RefundLast();return;}}
        var accept=Message(e,"consume",m.Slot,origin,aim);accept.Id=m.Id;accept.Cooldown=cd;Send(p,accept);
        if(m.Slot==1)LaunchRivet(e,ForgeAvatar.AimMuzzle(p,aim),aim,level);
        else if(m.Slot==2)
        {
            var own=devices.Where(d=>d.Owner==e && d.Slot==2).OrderBy(d=>d.Unit).ToArray();
            while(own.Length>=ForgeRules.Sentries(level)){Remove(own[0]);own=own.Skip(1).ToArray();}
            var d=new Device(e,2,++unit,aim,level,float.PositiveInfinity){Construct=construct,ConstructId=construct!.ObjectId};devices.Add(d);PublishDevice(d);
        }
        else if(m.Slot==3)
        {
            var shot=Message(e,"grenade",3,origin+Vector3.up,aim);shot.Remaining=1.1f;shot.Level=level;Publish(shot);
            blasts.Add(new(e,aim,Time.time+1.1f,level));
        }
        else
        {
            // Each native charge can sustain its own complete Aegis field.
            var own=devices.Where(d=>d.Owner==e && d.Slot==4).OrderBy(d=>d.Unit).ToArray();
            var capacity=Math.Clamp(skill.MaxCharges,1,50);
            while(own.Length>=capacity){Remove(own[0]);own=own.Skip(1).ToArray();}
            var d=new Device(e,4,++unit,aim,level,Time.time+ForgeRules.Duration(4,level));devices.Add(d);PublishDevice(d);
        }
        State(e);
    }
    private static void Reject(CharManager p,ForgeMessage request)=>Send(p,new(){Kind="reject",Id=request.Id,Character=p.ObjectId,Owner=p.OwnerId,Scene=(int)GlobalPredictor.CurrentScene});
    internal static void Tick()
    {
        try
        {
            if(!Combat || !SlotNetwork.Ready){if(engineers.Count+visuals.Count+devices.Count+rivets.Count+rivetVisuals.Count+pushes.Count>0)Clear();return;}
            if(Time.unscaledTime>=nextEnemies)
            {
                nextEnemies=Time.unscaledTime+.2f;allNpcs=GameWorldIndex.Npcs;enemies=allNpcs.Where(Enemy).ToArray();
                if(SlotNetwork.IsHost)foreach(var p in GameWorldIndex.Players())if(Alive(p) && p.IsServerStarted)For(p);
            }
            if(SlotNetwork.IsHost)ServerTick();
            if(Time.unscaledTime>=nextDiagnostics){nextDiagnostics=Time.unscaledTime+2;Plugin.WriteStatus("ready");}
            foreach(var pair in visuals.ToArray())if(pair.Value.Root==null || Time.time>=pair.Value.End || !Alive(pair.Value.Owner)){pair.Value.Destroy();visuals.Remove(pair.Key);}
            foreach(var key in pendingHits.Where(x=>Time.unscaledTime>x.Value.Until).Select(x=>x.Key).ToArray())pendingHits.Remove(key);
        }
        catch(Exception ex){if(error!=ex.Message){error=ex.Message;Plugin.Logger.LogError("Forge combat: "+ex);}}
    }
    private static IEnumerable<NpcCharacter> Near(Engineer e,Vector3 at,float radius)=>enemies.Where(t=>Enemy(t) &&
        t.gameObject.scene.handle==e.Player.gameObject.scene.handle && (t.CachedTransform.position-at).sqrMagnitude<=radius*radius && CannonWorld.Sight(at+Vector3.up*.6f,t.CachedTransform.position+Vector3.up*.6f));
    private static ForgeMessage Message(Engineer e,string kind,int slot,Vector3 at,Vector3 aim)=>new()
    {Kind=kind,World=e.World,Owner=e.Player.OwnerId,Character=e.Player.ObjectId,Scene=(int)GlobalPredictor.CurrentScene,Sequence=++sequence,
        Slot=slot,Position=A(at),Aim=A(aim),Boost=(float)Math.Max(0,e.Passive.RageUntil-Time.time)};
    private static void State(Engineer e)
    {
        var m=Message(e,"passive",5,e.Player.CachedTransform.position,e.Player.CachedTransform.position);
        m.Feedback=devices.Count(d=>d.Owner==e && TurretBoost(d));m.Direct=e.Passive.Stationary(Time.time);Send(e.Player,m);
    }
    private static void PublishDevice(Device d)
    {
        var m=Message(d.Owner,"device",d.Slot,d.Position,d.Target);m.Unit=d.Unit;m.Level=d.Level;m.Remaining=d.Slot==2 ? 2:Math.Max(0,d.End-Time.time);
        if(d.Construct!=null){m.Construct=d.Construct.ObjectId;m.Health=d.Construct.CurrentHealth;m.MaxHealth=d.Construct.MaxHealth;}Publish(m);
    }
    private static void Remove(Device d)
    {
        devices.Remove(d);
        if(d.Construct!=null)constructPresentation.Remove(d.Construct.Pointer);
        constructIds.Remove(d.ConstructId);
        if(d.Construct!=null && d.Construct.IsSpawned)
        {
            // Completed native spawn/death routines may already dispose this
            // source. Pool cleanup and the remove receipt must still run.
            try{d.Construct.ServerSpawnCts?.Cancel();}
            catch(Il2CppException ex) when(ex.Message.Contains("ObjectDisposedException")){}
            var pool=d.Construct.GetComponentInParent<PoolItem>();
            if(pool!=null)pool.DespawnItem(null);else d.Construct.Despawn(default(Il2CppSystem.Nullable<FishNet.Object.DespawnType>));
        }
        var m=Message(d.Owner,"remove",d.Slot,d.Position,d.Position);m.Unit=d.Unit;Publish(m);
    }
    private static void Publish(ForgeMessage m)=>SlotNetwork.Publish(new(){Kind="forge",Forge=m});
    private static void Send(CharManager p,ForgeMessage m)=>SlotNetwork.Send(p.Owner,new(){Kind="forge",Forge=m});
    internal static void Receive(ForgeMessage m)
    {
        if(!Combat || m.Scene!=(int)GlobalPredictor.CurrentScene)return;
        var p=PlayableCharactersHolder.GetCharacterByClientId(m.Owner);
        if(!Alive(p) || p!.ObjectId!=m.Character)return;
        if(m.Kind=="reject")
        {if(p.IsOwner && pendingCasts.TryGetValue(m.Character,out var pending) && pending.Id==m.Id)pendingCasts.Remove(m.Character);return;}
        if(!SlotRules.IsRequestId(m.World) || m.Slot<1 || m.Slot>5 || !ForgeRules.ValidPoint(m.Position) || !ForgeRules.ValidPoint(m.Aim) ||
            !receipts.Claim(m.Character,m.Sequence,m.Kind,m.Unit))return;
        if(m.Kind=="consume")
        {
            if(!p.IsOwner || !pendingCasts.TryGetValue(m.Character,out var pending) || pending.Id!=m.Id || m.Slot>4 || !float.IsFinite(m.Cooldown) || m.Cooldown<.025f || m.Cooldown>120)return;
            pendingCasts.Remove(m.Character);
            p.Data.SkillsData.Skills[(SkillType)m.Slot].SpendCharge(m.Cooldown);
            p.playableCharacterMain.Skills.ClientSendMessageOnUseSkill((SkillType)m.Slot);
            p.playableCharacterMain.Skills.ClientSendMessageOnPushSkill((SkillType)m.Slot);
            return;
        }
        if(m.Kind=="passive")
        {if(p.IsOwner && float.IsFinite(m.Boost)){boostedTurrets=Math.Clamp(m.Feedback,0,4);stationary=m.Direct;rageUntil=Time.time+Math.Clamp(m.Boost,0,ForgeRules.RageDuration);}return;}
        if(m.Kind=="cup")
        {
            if(!p.IsOwner || m.Owner!=SlotNetwork.LocalId || m.Slot!=1 || m.Targets is not {Length:1} || !CannonRules.ValidDamage(m.Damage))return;
            var cup=Object.FindObjectsOfType<SpectrisCup>().FirstOrDefault(c=>c!=null && c.IsSpawned && !c.IsDead && c.ObjectId==m.Targets[0] &&
                c.gameObject.scene.handle==p.gameObject.scene.handle && (c.transform.position-V(m.Position!)).sqrMagnitude<9);
            if(cup!=null)
            {
                DamageDealer.PlayerDealDamage(p,PlayableCharacterHitType.FirstSkill,cup.gameObject,m.Damage,false,
                    new HitPointInfo(HitPositionType.Custom,PoolItemID.HybridShellHit,cup.transform.position,Quaternion.identity),DamageType.Normal,true,true,true,false,false,false);
                RivetCups++;
            }
            return;
        }
        if(m.Kind=="hit")
        {
            if(!p.IsOwner || m.Owner!=SlotNetwork.LocalId || m.Targets is not {Length:>0 and <=32} || !CannonRules.ValidDamage(m.Damage))return;
            var confirmed=new List<int>();
            foreach(var id in m.Targets.Distinct())
            {
                var enemy=enemies.FirstOrDefault(e=>Enemy(e) && e.ObjectId==id && e.gameObject.scene.handle==p.gameObject.scene.handle);
                if(enemy==null || (enemy.CachedTransform.position-V(m.Position!)).sqrMagnitude>100)continue;
                // The native return value is its critical-hit flag, not hit success.
                // These host-issued receipts already identify a live, damageable target.
                // A seeking continuation is a secondary hit, not a fresh skill
                // cast. Native item damage rules keep normal damage/crit/summon
                // scaling without recursively spawning a skill-proc storm.
                var hitType=m.Slot==1 ? PlayableCharacterHitType.FirstSkill:m.Slot==2 && m.Feedback>1 ? PlayableCharacterHitType.Item:PlayableCharacterHitType.Skill;
                DamageDealer.PlayerDealDamage(p,hitType,enemy.gameObject,m.Damage,false,
                    new HitPointInfo(HitPositionType.BodyCenter,m.Slot<=2 ? PoolItemID.HybridShellHit:PoolItemID.ItemCannonadeExplosion,enemy.CachedTransform.position,Quaternion.identity),DamageType.Normal,true,true,true,false,false,m.Slot==2);
                confirmed.Add(id);
            }
            if(m.Slot==1 && m.Direct && confirmed.Count>0){RivetConfirmed+=confirmed.Count;SlotNetwork.Request(new(){Kind="forge",Forge=new(){Kind="confirm",Character=m.Character,Scene=m.Scene,Sequence=m.Sequence,Targets=confirmed.ToArray()}});}
            return;
        }
        var key=$"{m.Character}/{m.World}/{m.Unit}";
        if(m.Kind=="rivet-stop")
        {if(m.Slot==2 && m.Feedback==1)ForgeShotFx.Impact(V(m.Position!),V(m.Aim!)-V(m.Position!),m.Charged);if(rivetVisuals.Remove(key,out var stopped) && stopped.Root!=null){ForgeArt.BendRivet(stopped.Root,V(m.Position!));ForgeArt.StopRivet(stopped.Root);}return;}
        if(m.Kind=="rivet-turn")
        {
            if(m.Slot!=2 || !ForgeRules.ValidPoint(m.Control) || !float.IsFinite(m.Remaining) || m.Remaining<=0 || m.Remaining>.5f)return;
            var from=V(m.Position!);var to=V(m.Aim!);var control=V(m.Control!);
            if((to-from).sqrMagnitude>196 || (control-from).sqrMagnitude>49)return;
            if(m.Direct)ForgeShotFx.Impact(from,(control-from).normalized,m.Charged);
            // Hops are ordered host events, not new shots. Retain the object,
            // trail and audio envelope; late arrivals can reconstruct the mesh.
            if(!rivetVisuals.TryGetValue(key,out var v) || v.Root==null)
            {
                var root=ForgeArt.Rivet(from,(to-from).normalized,p.gameObject.scene);
                ForgeArt.StyleRivet(root,2,false,m.Charged);
                rivetVisuals[key]=v=new(root,from,(to-from).normalized,Time.time,m.Remaining,p,ForgeRules.RivetSpeed);
            }
            ForgeArt.BendRivet(v.Root,from);
            v.Arc=new(N(from),N(control),N(to));v.Start=Time.time;v.Duration=m.Remaining;v.From=from;
            return;
        }
        if(m.Kind=="rivet")
        {
            if(m.Slot is <1 or >2 || !float.IsFinite(m.Remaining) || m.Remaining<=0 || m.Remaining>2 || !float.IsFinite(m.Speed) || m.Speed<1 || m.Speed>200)return;
            var from=V(m.Position!);var path=V(m.Aim!)-from;
            if(path.sqrMagnitude<.01f || path.magnitude>44)return;
            if(rivetVisuals.Remove(key,out var old) && old.Root!=null)ForgeArt.StopRivet(old.Root);
            var root=ForgeArt.Rivet(from,path.normalized,p.gameObject.scene);
            ForgeArt.StyleRivet(root,m.Slot,m.Direct,m.Charged);
            rivetVisuals[key]=new(root,from,path.normalized,Time.time,Math.Min(2,m.Remaining),p,m.Speed);
            if(m.Slot==1 && m.Direct){ForgeAvatar.Aim(p,path.normalized);ForgeAvatar.Event(p,m.Charged ? "fire_charged":"fire",m.Boost);}
            if(m.Slot==2 && visuals.TryGetValue($"{m.Character}/{m.World}/{m.Construct}",out var sentry))ForgeEquipment.Fire(sentry.Root,m.Feedback==1,path.normalized);
            if(m.Slot==2)ForgeShotFx.Fire(from,path.normalized,m.Charged);
            if(m.Slot==2 || m.Direct)
                try{AudioController.PlayOneShot(m.Charged ? AudioClipTag.HybridShellsFat:AudioClipTag.HybridShells,from);if(m.Slot==2)SentryAudioShots++;}
                catch(Exception ex){if(m.Slot==2 && SentryAudioFailures++==0)Plugin.Logger.LogWarning("Forge sentry firing audio: "+ex.Message);}
            return;
        }
        if(m.Kind=="remove"){if(visuals.Remove(key,out var old)){constructIds.Remove(old.Construct);ForgeEquipment.Destroy(old.Root);if(old.Ring!=null)Object.Destroy(old.Ring);}return;}
        if(m.Kind=="device")
        {
            if(m.Slot!=2 && m.Slot!=4 || !float.IsFinite(m.Remaining) || m.Remaining<0 || m.Remaining>40)return;
            if(visuals.TryGetValue(key,out var broken) && (broken.Root==null || m.Slot==4 && broken.Ring==null))
            {broken.Destroy();visuals.Remove(key);}
            if(!visuals.TryGetValue(key,out var visual))
            {
                ForgeAvatar.Event(p,m.Slot==2 ? "deploy_sentry":"deploy_field",m.Boost);
                var root=ForgeEquipment.Create(m.Slot,V(m.Position!),p.gameObject.scene);var ring=m.Slot==4 ? ForgeArt.Ring(V(m.Position!),ForgeRules.Radius(ForgeRules.Level(m.Level)),p.gameObject.scene):null;
                visual=new(root,ring,p);visuals[key]=visual;
            }
            visual.End=Time.time+Math.Min(m.Remaining,2);
            if(m.Slot==4 && visual.Ring!=null)ForgeShield.State(visual.Ring,ForgeRules.Level(m.Level),m.Remaining);
            ForgeEquipment.State(visual.Root,V(m.Aim!),m.Health,m.MaxHealth,m.Level);
            if(m.Slot==2){visual.Construct=m.Construct;constructIds.Add(m.Construct);}return;
        }
        if(m.Kind=="trace")
        {
            ForgeArt.Trace(V(m.Position!),V(m.Aim!),p.gameObject.scene);
            try{AudioController.PlayOneShot(AudioClipTag.HybridShells,V(m.Position!));}catch{}
            if(m.Slot==2 && visuals.TryGetValue(key,out var v)){var head=v.Root.transform.Find("Aim");if(head!=null){var d=V(m.Aim!)-head.position;d.y=0;if(d.sqrMagnitude>.01f)head.rotation=Quaternion.LookRotation(d);}}
        }
        else if(m.Kind=="grenade")
        {
            ForgeAvatar.Event(p,"throw_charge",m.Boost);
            var grenade=ForgeEquipment.Create(3,ForgeAvatar.LeftHand(p),p.gameObject.scene);grenades.Add(new(grenade,ForgeAvatar.LeftHand(p),V(m.Aim!),Time.time,p,m.Level));
        }
        else if(m.Kind=="thermite")
        {
            var ring=ForgeArt.Ring(V(m.Position!),5,p.gameObject.scene);ring.GetComponent<LineRenderer>().sharedMaterial=ForgeArt.Material("orange");
            visuals["burn/"+m.Sequence]=new(ring,null,p){End=Time.time+4};
        }
        else if(m.Kind=="pulse")
        {
            var ring=ForgeArt.Ring(V(m.Position!),ForgeRules.Radius(ForgeRules.Level(m.Level)),p.gameObject.scene);
            pulses.Add((ring,Time.time));Object.Destroy(ring,.4f);
        }
        else if(m.Kind=="blast")
        {
            var radius=ForgeRules.GrenadeRadius(ForgeRules.Level(m.Level));
            var ring=ForgeArt.Ring(V(m.Position!),radius,p.gameObject.scene);var line=ring.GetComponent<LineRenderer>();
            line.sharedMaterial=ForgeArt.Material("hot");line.startWidth=line.endWidth=.18f;Object.Destroy(ring,.35f);
            try{LocalPoolManager.Get(PoolItemID.ItemCannonadeExplosion,V(m.Position!),Quaternion.identity,null,Vector3.one*(radius/2.4f));AudioController.PlayOneShot(AudioClipTag.CannonadeHit,V(m.Position!));}catch{}
        }
    }
    private sealed class Grenade
    {
        internal readonly GameObject Root;internal GameObject? Magnet;internal Vector3 From;internal readonly Vector3 To;
        internal readonly float Start;internal readonly CharManager Owner;internal readonly int Level;internal bool Released;
        internal Grenade(GameObject root,Vector3 from,Vector3 to,float start,CharManager owner,int level)
        {Root=root;From=from;To=to;Start=start;Owner=owner;Level=level;}
    }
    private static readonly List<Grenade> grenades=new();
    private static readonly List<(GameObject Root,float At)> pulses=new();
    private sealed class RivetVisual(GameObject root,Vector3 from,Vector3 direction,float start,float duration,CharManager owner,float speed)
    {
        internal readonly GameObject Root=root;internal readonly CharManager Owner=owner;
        internal Vector3 From=from,Direction=direction;internal float Start=start,Duration=duration,Speed=speed;
        internal ForgeSeekArc? Arc;
    }
    private static readonly Dictionary<string,RivetVisual> rivetVisuals=new();
    internal static void Render()
    {
        ForgeShield.Render();
        ForgeEquipment.Render();
        ForgeArt.TickRivets();
        foreach(var wave in pulses.ToArray())
        {
            if(wave.Root==null){pulses.Remove(wave);continue;}
            wave.Root.transform.localScale=Vector3.one*Math.Clamp((Time.time-wave.At)/.4f,.05f,1);
        }
        foreach(var visual in visuals.Values.Where(v=>v.Construct>=0))
        {
            if(visual.Root==null)continue;
            var npc=allNpcs.FirstOrDefault(n=>n!=null && n.ObjectId==visual.Construct && n.IsSpawned);
            if(npc!=null)ConfigureConstruct(npc,visual.Root.transform.position);
        }
        foreach(var pair in rivetVisuals.ToArray())
        {
            var rivet=pair.Value;var age=Time.time-rivet.Start;
            if(!Combat || rivet.Root==null || !Alive(rivet.Owner) || age>=rivet.Duration)
            {if(rivet.Root!=null)ForgeArt.StopRivet(rivet.Root);rivetVisuals.Remove(pair.Key);continue;}
            var t=age/Math.Max(.001f,rivet.Duration);
            var at=rivet.Arc!=null ? U(rivet.Arc.Point(t)):rivet.From+rivet.Direction*(age*rivet.Speed);
            var direction=rivet.Arc!=null ? U(rivet.Arc.Tangent(t)):rivet.Direction;
            rivet.Root.transform.SetPositionAndRotation(at,direction.sqrMagnitude>.0001f ? Quaternion.LookRotation(direction):rivet.Root.transform.rotation);
        }
        foreach(var g in grenades.ToArray())
        {
            if(!Combat || g.Root==null || Time.time-g.Start>=1.1f){if(g.Root!=null)Object.Destroy(g.Root);if(g.Magnet!=null)Object.Destroy(g.Magnet);grenades.Remove(g);continue;}
            var age=Time.time-g.Start;
            if(age<.333f){if(g.Owner!=null)g.Root.transform.position=ForgeAvatar.LeftHand(g.Owner);continue;}
            if(!g.Released){if(g.Owner!=null)g.From=ForgeAvatar.LeftHand(g.Owner);g.Released=true;}
            var travel=g.Level>=2 ? .267f:.55f;var t=Math.Clamp((age-.333f)/travel,0,1);
            g.Root.transform.position=Vector3.Lerp(g.From,g.To+Vector3.up*.32f,t)+Vector3.up*Mathf.Sin(t*Mathf.PI)*2;
            if(t<1)g.Root.transform.Rotate(Vector3.right,540*Time.deltaTime,Space.Self);
            if(g.Level>=2 && age>=1.1f-ForgeRules.MagneticWindow)
            {
                if(g.Magnet==null)g.Magnet=ForgeArt.Magnet(g.To,g.Root.scene);
                ForgeArt.MagnetState(g.Magnet,(age-(1.1f-ForgeRules.MagneticWindow))/ForgeRules.MagneticWindow);
            }
        }
    }
    internal static void OnGui()
    {
        if(!Combat)return;
        var p=PlayableCharactersHolder.MyCharacter;if(!Alive(p))return;
        var text=$"FORGE · {boostedTurrets} TURRETS BOOSTED";
        if(stationary)text+=" · SELF REPAIR";
        if(Time.time<rageUntil)text+=$" · ENRAGED {rageUntil-Time.time:0.0}s";
        GUI.Label(new Rect(24,Screen.height-120,650,26),text);
    }
    internal static void Clear()
    {
        CancelAim();waitForRelease=true;
        foreach(var t in slowed.Values)if(t!=null && t.IsSpawned)Speed(t).RemoveModificator(SlowSource,slow,false);
        slowed.Clear();burns.Clear();
        if(SlotNetwork.IsHost)foreach(var e in engineers.Values)StopPassive(e);
        if(SlotNetwork.IsHost)foreach(var d in devices.ToArray())Remove(d);
        constructIds.Clear();
        constructPresentation.Clear();
        foreach(var v in visuals.Values)v.Destroy();foreach(var g in grenades){if(g.Root!=null)Object.Destroy(g.Root);if(g.Magnet!=null)Object.Destroy(g.Magnet);}
        foreach(var r in rivetVisuals.Values)if(r.Root!=null)ForgeArt.StopRivet(r.Root);
        ForgeShotFx.Clear();
        rivets.Clear();rivetVisuals.Clear();pushes.Clear();
        visuals.Clear();grenades.Clear();engineers.Clear();devices.Clear();blasts.Clear();pendingHits.Clear();pendingCasts.Clear();
        foreach(var wave in pulses)if(wave.Root!=null)Object.Destroy(wave.Root);pulses.Clear();
        receipts.Clear();requests.Clear();enemies=allNpcs=Array.Empty<NpcCharacter>();boostedTurrets=0;rageUntil=0;stationary=false;nextEnemies=nextInput=nextHeal=0;
    }
}
