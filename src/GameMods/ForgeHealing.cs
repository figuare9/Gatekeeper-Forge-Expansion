using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Enemy.Base;
using Gatekeeper.Mods;
using Gatekeeper.NPC;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static partial class ForgeWorld
{
    private static float nextHeal;
    // Check the enemy interface directly: invulnerable enemies are still enemies.
    private static bool AllyNpc(NpcCharacter? n)=>n!=null && n.IsSpawned && n.IsActiveAndAlive && n.IsServerStarted && n.TryCast<IEnemy>()==null;
    private static bool ActiveField(Device d)=>d.Slot==4 && d.End>Time.time && Alive(d.Owner.Player);
    private static bool InsideField(Device d,Component target)=>d.Owner.Player.gameObject.scene.handle==target.gameObject.scene.handle &&
        (target.transform.position-d.Position).sqrMagnitude<=Mathf.Pow(ForgeRules.Radius(d.Level),2);
    private static void HealAllies()
    {
        if(!Combat || !SlotNetwork.IsHost || Time.deltaTime<=0 || Time.time<nextHeal)return;
        nextHeal=Time.time+ForgeRules.HealInterval;
        var fields=devices.Where(ActiveField).ToArray();
        // Sum complete fields, then heal each native recipient once per tick.
        foreach(var p in GameWorldIndex.Players())
        {
            // Native summons have a hidden CharManager AND an NPC body. Heal their
            // body below so its native forwarding path is used exactly once.
            if(p==null || !p.IsSpawned || !p.IsServerStarted || !p.IsActiveAndAlive || p.IsConstruct || p.Data?.HealthData==null)continue;
            var health=p.Data.HealthData;var repair=engineers.TryGetValue(p.ObjectId,out var e) && e.Passive.Stationary(Time.time);
            var rate=ForgeRules.HealPerSecond*fields.Count(d=>InsideField(d,p))+(repair ? ForgeRules.StillRegen:0);
            var amount=ForgeRules.Regen(health.FinalHealth,health.CurrentHealth,rate);
            if(amount>0)health.ServerTakeHeal(amount,repair,false);
        }
        foreach(var n in allNpcs)
        {
            if(!AllyNpc(n))continue;
            var boost=devices.Any(d=>d.ConstructId==n.ObjectId && TurretBoost(d));
            var rate=ForgeRules.HealPerSecond*fields.Count(d=>InsideField(d,n))+(boost ? ForgeRules.TurretRegen:0);
            var amount=ForgeRules.Regen(n.MaxHealth,n.CurrentHealth,rate);
            if(amount>0)n.ClientTakeHeal(amount,false,false);
        }
    }
}
