using Gatekeeper.Char_Scripts.General.CharData;
using Gatekeeper.Construct.HalfmetalHead;
using Gatekeeper.General.CharactersStats.AttackSpeed;
using Gatekeeper.Interfaces;
using Gatekeeper.Mods;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static partial class ForgeWorld
{
    private const AttackSpeedModificatorSource RageSource=(AttackSpeedModificatorSource)241;
    private static readonly AttackSpeedModificator rageMod=new(ForgeRules.RageBonus);
    private static IStatModificator<AttackSpeedModificatorSource,AttackSpeedModificator> AttackSpeed(Engineer e)=>
        e.Player.Data.AttackSpeedData.Cast<IStatModificator<AttackSpeedModificatorSource,AttackSpeedModificator>>();
    private static bool LivingSentry(Device d)=>d.Slot==2 && d.Construct!=null && d.Construct.IsSpawned && d.Construct.IsActiveAndAlive && d.Construct.CurrentHealth>0;
    private static bool TurretBoost(Device d)=>LivingSentry(d) && Alive(d.Owner.Player) &&
        d.Owner.Player.gameObject.scene.handle==d.Construct!.gameObject.scene.handle &&
        (d.Owner.Player.CachedTransform.position-d.Position).sqrMagnitude<=ForgeRules.CommandRadius*ForgeRules.CommandRadius;
    private static Device? TurretForData(CharData data)=>devices.FirstOrDefault(d=>LivingSentry(d) &&
        d.Construct!.TryCast<HalfmetalHeadConstruct>()?.ConstructManager?.Data?.Pointer==data.Pointer);
    private static void TurretDamageTaken(CharData __instance,IDamageSource damageSource,float __result,Device? __state)
    {
        // Only a positive, lethal native enemy hit qualifies. Pool replacement and
        // scene cleanup remove the device without entering this damage path.
        if(!Combat || !SlotNetwork.IsHost || __state==null || __state.DeathCounted || !devices.Contains(__state) ||
            !Alive(__state.Owner.Player) || damageSource==null || damageSource.DamageSourceType!=DamageSourceType.Enemy ||
            !float.IsFinite(__result) || __result<=0 || __instance.HealthData.CurrentHealth>0)return;
        __state.DeathCounted=true;__state.Owner.Passive.EnemyKilledTurret(Time.time);
        UpdateRage(__state.Owner);State(__state.Owner);
    }
    private static void UpdateRage(Engineer e)
    {
        var active=Alive(e.Player) && e.Passive.Enraged(Time.time);
        if(e.RageApplied==active)return;
        if(active)AttackSpeed(e).AddModificator(RageSource,rageMod);
        else AttackSpeed(e).RemoveModificator(RageSource,rageMod,true);
        e.RageApplied=active;
    }
    private static void StopPassive(Engineer e)
    {
        if(e.RageApplied && e.Player!=null && e.Player.IsSpawned && e.Player.Data?.AttackSpeedData!=null)
            AttackSpeed(e).RemoveModificator(RageSource,rageMod,true);
        e.RageApplied=false;
    }
    private static void TickPassives()
    {
        if(Time.deltaTime<=0)return;
        foreach(var e in engineers.Values.ToArray())
        {
            if(!Alive(e.Player)){StopPassive(e);engineers.Remove(e.Player.ObjectId);continue;}
            var at=e.Player.CachedTransform.position;var moved=(at-e.StillAt).sqrMagnitude>.04f*.04f;
            e.Passive.Observe(Time.time,moved);if(moved)e.StillAt=at;
            UpdateRage(e);
            if(Time.unscaledTime>=e.NextState){e.NextState=Time.unscaledTime+.25f;State(e);}
        }
    }
}
