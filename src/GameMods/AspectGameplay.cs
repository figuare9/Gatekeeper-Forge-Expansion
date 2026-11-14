using System.Reflection;
using FishNet.Object.Synchronizing;
using FishNet.Connection;
using FishNet.Object;
using Gatekeeper.Char_Scripts;
using Gatekeeper.Elites;
using Gatekeeper.Enemy.Base;
using Gatekeeper.Enemy.Base.Logic;
using Gatekeeper.Enemy.Dummy;
using Gatekeeper.General;
using Gatekeeper.General.Aspects;
using Gatekeeper.General.Predictor;
using Gatekeeper.General.Tasks.TaskLogic;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Mods;
using Gatekeeper.NPC;
using Gatekeeper.Utility;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class AspectGameplay
{
    private static readonly Dictionary<IntPtr,AspectScaling> factors=new();
    private static readonly Dictionary<string,EliteBinding> bindings=new();
    private static readonly AspectPrismCarry prisms=new();
    private static readonly Dictionary<EliteType,EliteInfo> defensiveElites=new();
    private static readonly HashSet<IntPtr> extraElites=new();
    private static readonly HashSet<IntPtr> completedDeaths=new();
    private sealed record PendingDeath(NpcCharacter Enemy,int Sender,NetworkObject Owner,DamageType Type,float At);
    private static readonly Dictionary<IntPtr,PendingDeath> pendingDeaths=new();
    private static float nextDeathCheck;
    internal static int ScaledSpawns,ForcedElites,RecoveredDeaths;
    internal static string? Error {get;private set;}
    // Native level generation can spawn enemies before the loading screen ends.
    // The authoritative active-Aspect set, rather than the UI gameplay state,
    // must therefore gate initial population, health and elite selection.
    internal static bool Active(byte id)=>AspectRegistry.Registered && AspectsManager._instance?.IsAspectActive((EAspect)id)==true;
    internal static void Patch(Harmony h)
    {
        h.Patch(AccessTools.PropertyGetter(typeof(DefaultModeSpawnLogic),"AspectOfSwarmMultiplier"),postfix:new(typeof(AspectGameplay),nameof(SpawnMultiplier)));
        h.Patch(AccessTools.Method(typeof(NpcCharacter),nameof(NpcCharacter.ServerSpawn)),prefix:new(typeof(AspectGameplay),nameof(Spawn)),postfix:new(typeof(AspectGameplay),nameof(Health)));
        h.Patch(AccessTools.Method(typeof(NpcCharacter),"ServerCalculateStats"),postfix:new(typeof(AspectGameplay),nameof(Health)));
        h.Patch(AccessTools.Method(typeof(NpcCharacter),"KillerAndServerTakeDamage"),postfix:new(typeof(AspectGameplay),nameof(CompleteDeath)));
        h.Patch(AccessTools.Method(typeof(RewardManager),"CalculatePrismDropAmount"),postfix:new(typeof(AspectGameplay),nameof(Reward)));
        h.Patch(AccessTools.Method(typeof(NpcCharacter),"OnDisable"),postfix:new(typeof(AspectGameplay),nameof(Disabled)));
        // Do not patch inherited empty NetworkBehaviour methods: IL2CPP shares
        // their native address with unrelated no-op methods.
        h.Patch(AccessTools.Method(typeof(AspectsManager),"Awake"),prefix:new(typeof(AspectGameplay),nameof(Clear)));
        h.Patch(AccessTools.Method(typeof(AspectsManager),"OnStopClient"),postfix:new(typeof(AspectGameplay),nameof(Clear)));
    }
    private static void Report(Exception e)
    {if(Error==e.Message)return;Error=e.Message;Plugin.Logger.LogError("Expansion Aspect mechanics: "+e);}
    private static void SpawnMultiplier(ref float __result)=>__result=AspectRules.SpawnMultiplier(__result,Active(AspectRules.Chaos));
    internal static AspectScaling TeamFactor()
    {
        var levels=new List<int>(4);
        // The native roster includes dead connected players, so dying does not
        // temporarily lower the difficulty for the surviving teammates.
        foreach(var p in GameWorldIndex.Players())if(p.IsSpawned && p.Data!=null)levels.Add(p.Data.ActualLevel);
        return AspectRules.TeamScaling(levels);
    }
    private static void Spawn(NpcCharacter __instance,ref EliteType __2)
    {
        if(!__instance.IsServerStarted)return;
        var enemy=__instance.TryCast<IEnemy>();if(enemy==null)return;
        try
        {
            var factor=Active(AspectRules.Scaling)?TeamFactor():AspectScaling.Unscaled;
            var ascendant=AscendantDifficulty.StageFactors();
            factors[__instance.Pointer]=new(factor.Health*ascendant.Health,factor.Prisms);
            if(factors[__instance.Pointer].Health>1)ScaledSpawns++;
            extraElites.Remove(__instance.Pointer);
            completedDeaths.Remove(__instance.Pointer);
            pendingDeaths.Remove(__instance.Pointer);
            var ascent=AscendantDifficulty.Active && !enemy.IsElite && !__instance.IsSiren && AspectRules.EliteMask((int)enemy.Info.EliteTypes)!=0 && UnityEngine.Random.value<ascendant.ExtraElites;
            if(enemy.IsElite || !Active(AspectRules.Elite) && !ascent)return;
            var type=(EliteType)AspectRules.ChooseElite((int)enemy.EliteType,(int)enemy.Info.EliteTypes,UnityEngine.Random.Range(0,int.MaxValue));
            // Generic EnemyCharacter.ServerSpawn has already set its native
            // elite fields before entering this non-generic base entry point.
            // Replace those fields before ServerInit calculates health/damage
            // or announces spawn to elite controllers. This also covers direct
            // boss/summon spawns which bypass the ordinary enemy factory.
            var name=__instance.GetIl2CppType().FullName;
            if(!bindings.TryGetValue(name,out var binding))bindings[name]=binding=new EliteBinding(name);
            binding.Set(__instance.Pointer,type);__2=type;ForcedElites++;
            if(ascent && !Active(AspectRules.Elite))extraElites.Add(__instance.Pointer);
        }
        catch(Exception e){Report(e);}
    }
    private static EliteInfo DefensiveElite(EliteType type)
    {
        if(defensiveElites.TryGetValue(type,out var info))return info;
        info=UnityEngine.Object.Instantiate(DatabaseInfoProvider.Elites.GetInfo(type));
        info.name="Ascendant "+type;info.DamageModifier=info.SirenDamageModifier=1;
        UnityEngine.Object.DontDestroyOnLoad(info);info.hideFlags|=HideFlags.DontUnloadUnusedAsset;
        defensiveElites[type]=info;return info;
    }
    private sealed class EliteBinding
    {
        private readonly ConstructorInfo constructor;
        private readonly PropertyInfo sync,info;
        internal EliteBinding(string nativeName)
        {
            var type=typeof(NpcCharacter).Assembly.GetType(nativeName)??throw new InvalidOperationException("Enemy binding unavailable: "+nativeName);
            constructor=type.GetConstructor(new[]{typeof(IntPtr)})??throw new InvalidOperationException("Enemy pointer constructor unavailable: "+nativeName);
            sync=AccessTools.Property(type,"_eliteType")??throw new InvalidOperationException("Enemy elite SyncVar unavailable: "+nativeName);
            info=AccessTools.Property(type,"EliteInfo")??throw new InvalidOperationException("Enemy elite metadata unavailable: "+nativeName);
        }
        internal void Set(IntPtr pointer,EliteType type,EliteInfo? custom=null)
        {
            var proxy=(Il2CppObjectBase)constructor.Invoke(new object[]{pointer});
            var data=custom??DatabaseInfoProvider.Elites.GetInfo(type)??throw new InvalidOperationException("Native elite type is unavailable: "+type);
            var value=(SyncVar<EliteType>)sync.GetValue(proxy)!;
            info.SetValue(proxy,data);value.Value=type;
        }
    }
    private static void Health(NpcCharacter __instance)
    {
        // Apply once after the complete native calculation. Final bosses write
        // MaxHealth in their base calculation and then again for loop modifiers;
        // multiplying every setter would incorrectly square the Scaling bonus.
        // The pinned native spawn path inlines CalculateStats; later level/stat
        // refreshes use ServerCalculateStats, so both completed paths are hooked.
        if(!__instance.IsServerStarted)return;
        if(extraElites.Contains(__instance.Pointer))
        {
            var enemy=__instance.TryCast<IEnemy>();
            if(enemy?.EliteInfo!=null)
            {
                var defensive=DefensiveElite(enemy.EliteType);
                if(enemy.EliteInfo.Pointer!=defensive.Pointer)
                {
                    // Native ServerSpawn replaces EliteInfo after entering its
                    // base class. Normalize only after that complete calculation.
                    __instance.CurrentDamage/=Math.Max(1,enemy.EliteInfo.DamageModifier);
                    bindings[__instance.GetIl2CppType().FullName].Set(__instance.Pointer,enemy.EliteType,defensive);
                }
            }
        }
        if(factors.TryGetValue(__instance.Pointer,out var factor) && factor.Health>1)
            __instance.MaxHealth=AspectRules.Health(__instance.MaxHealth,factor.Health);
    }
    private static void Reward(RewardManager __instance,Gatekeeper.Interfaces.IRewardSource __0,ref int __result)
    {
        if(!__instance.IsServerStarted || __result<=0)return;
        var source=__0?.TryCast<EnemyRewardLogic>();var npc=source?._character?.TryCast<NpcCharacter>();
        if(npc!=null && factors.TryGetValue(npc.Pointer,out var factor))__result=prisms.Apply(__result,factor.Prisms);
    }
    private static void CompleteDeath(NpcCharacter __instance,NetworkConnection __0,NetworkObject __1,DamageType __2,float __4)
    {
        // Native damage and native death are separate messages. Finish a
        // missing death confirmation only after the host applied real damage
        // and finds an ordinary elite at zero health. Keep boss phase scripts,
        // immunity, damage attribution, rewards and native death callbacks.
        if(!__instance.IsServerStarted || !Active(AspectRules.Elite))return;
        try
        {
            var enemy=__instance.TryCast<IEnemy>();if(enemy==null || __0==null)return;
            if(!AspectRules.CompleteEliteDeath(true,true,__instance.IsSpawned,__instance.IsActive && !__instance.IsDead && __instance.CanTakeDamage,enemy.IsElite,ScriptedDeath(__instance),
                __0.IsActive && __0.IsAuthenticated,__instance.CurrentHealth,__4) ||
                completedDeaths.Contains(__instance.Pointer))return;
            // Give the normal killer-client death message time to arrive. Do
            // not race it, reset the deadline on another hit, or scan the world.
            pendingDeaths.TryAdd(__instance.Pointer,new(__instance,__0.ClientId,__1,__2,Time.unscaledTime+.5f));
        }
        catch(Exception e){Report(e);}
    }
    internal static void Tick()
    {
        if(pendingDeaths.Count==0 || Time.unscaledTime<nextDeathCheck || Time.timeScale<=0)return;
        nextDeathCheck=Time.unscaledTime+.1f;
        foreach(var pair in pendingDeaths.ToArray())
        {
            var pending=pair.Value;if(Time.unscaledTime<pending.At)continue;
            pendingDeaths.Remove(pair.Key);
            try
            {
                var npc=pending.Enemy;if(npc==null)continue;
                var enemy=npc.TryCast<IEnemy>();if(enemy==null)continue;
                if(!AspectRules.CompleteEliteDeath(Active(AspectRules.Elite),npc.IsServerStarted,npc.IsSpawned,npc.IsActive && !npc.IsDead && npc.CanTakeDamage,
                    enemy.IsElite,ScriptedDeath(npc),true,npc.CurrentHealth,1) || !completedDeaths.Add(pair.Key))continue;
                // Dispatch the actual enemy's virtual ServerDeath: native
                // rewards, task accounting, death effects and networking.
                npc.RpcLogic___RpcServerDeath___3148112092(pending.Sender,pending.Type,false,pending.Owner);
                // Native ServerDeath leaves local-killer cleanup to that
                // client's next step. Complete it if the missing client was us.
                if(!npc.IsDead)npc.AllClientsDeath(pending.Type,false,pending.Owner);
                RecoveredDeaths++;
            }
            catch(Exception e){Report(e);}
        }
    }
    private static bool ScriptedDeath(NpcCharacter npc)=>npc.IsSiren || npc.TryCast<DummyEnemy>()!=null;
    private static void Disabled(NpcCharacter __instance){factors.Remove(__instance.Pointer);extraElites.Remove(__instance.Pointer);completedDeaths.Remove(__instance.Pointer);pendingDeaths.Remove(__instance.Pointer);}
    private static void Clear(){AscendantDifficulty.ClearStage();pendingDeaths.Clear();completedDeaths.Clear();nextDeathCheck=0;extraElites.Clear();factors.Clear();prisms.Clear();ScaledSpawns=ForcedElites=RecoveredDeaths=0;Error=null;}
}
