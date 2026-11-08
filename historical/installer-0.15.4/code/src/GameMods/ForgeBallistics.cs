using Gatekeeper.Char_Scripts.General;
using Gatekeeper.EnvironmentStuff.Spectris;
using Gatekeeper.Enemy.Collector.Projectile;
using Gatekeeper.Mods;
using Gatekeeper.NPC;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static partial class ForgeWorld
{
    private static void LaunchRivet(Engineer e,Vector3 from,Vector3 aim,int level)
    {
        var direction=aim-from;if(direction.sqrMagnitude<.001f)direction=e.Player.CachedTransform.forward;
        var charged=ForgeRules.Charged(level,++e.Shots);
        var rivet=new Rivet(e,++unit,from,direction.normalized,level,1,charged);
        rivet.Group.Flying++;AddRivet(rivet);RivetShots++;
    }
    private static void AddRivet(Rivet rivet,int sentry=0,bool second=false)
    {
        rivets.Add(rivet);
        var visibleRange=rivet.Slot==2 ? ForgeRules.SentryRange(rivet.Level):rivet.Flight.Range;
        var shot=Message(rivet.Owner,"rivet",rivet.Slot,rivet.Position,rivet.Position+rivet.Direction*visibleRange);
        shot.Unit=rivet.Unit;shot.Level=rivet.Level;shot.Direct=rivet.Slot==1 && !rivet.Fragment;shot.Charged=rivet.Charged;
        shot.Remaining=visibleRange/rivet.Flight.Speed;shot.Speed=rivet.Flight.Speed;shot.Construct=sentry;shot.Feedback=second ? 1:0;
        Publish(shot);
    }
    private static void AdvanceRivets()
    {
        foreach(var rivet in rivets.ToArray())
        {
            var stopped=!Alive(rivet.Owner.Player);
            if(!stopped && rivet.Slot==2)stopped=AdvanceSeeker(rivet);
            else if(!stopped)
            {
                var step=rivet.Flight.Advance(Time.deltaTime);if(step<=0)continue;
                var end=rivet.Position+rivet.Direction*step;
                foreach(var hit in Physics.Query_SphereCastAll(Physics.defaultPhysicsScene,rivet.Position,ForgeRules.RivetRadius,rivet.Direction,step,~0,QueryTriggerInteraction.Collide).OrderBy(h=>h.distance))
                {
                    var collider=hit.collider;if(collider==null || collider.GetComponentInParent<CharManager>()!=null)continue;
                    var enemy=collider.GetComponentInParent<NpcCharacter>();
                    if(enemy!=null)
                    {
                        if(rivet.Fragment && rivet.Group.Direct.Contains(enemy.ObjectId))continue;
                        if(Enemy(enemy) && rivet.Flight.Hit(enemy.ObjectId))
                        {
                            var at=rivet.Position+rivet.Direction*Math.Clamp(hit.distance,0,step);
                            if(rivet.Fragment)rivet.Group.Fragments.Add((enemy,enemy.CachedTransform.position));
                            else
                            {
                                if(rivet.Slot==1){RivetTargets++;rivet.Group.Direct.Add(enemy.ObjectId);}
                                Damage(rivet.Owner,rivet.Slot,enemy.CachedTransform.position,new[]{enemy},ForgeRules.Damage(rivet.Slot,rivet.Level,rivet.Charged)*rivet.DamageBoost,rivet.Direction,rivet.Slot==1);
                                if(rivet.Slot==1 && rivet.Level>=2)
                                {
                                    for(var i=-1;i<=1;i+=2)
                                    {
                                        // Splinters spread at the impact height rather than
                                        // inheriting a downward shot into the floor.
                                        var forward=rivet.Direction;forward.y=0;
                                        if(forward.sqrMagnitude<.001f)forward=rivet.Owner.Player.CachedTransform.forward;
                                        var d=Quaternion.AngleAxis(i*35,Vector3.up)*forward.normalized;
                                        var fragment=new Rivet(rivet.Owner,++unit,at+d*.28f,d,rivet.Level,1,false,rivet.Group,true);
                                        rivet.Group.Flying++;AddRivet(fragment);
                                    }
                                }
                            }
                            if(rivet.Flight.Finished){end=at;stopped=true;break;}
                        }
                        continue;
                    }
                    var cup=collider.GetComponentInParent<SpectrisCup>();
                    if(cup!=null)
                    {
                        if(rivet.Slot==1 && !rivet.Fragment && cup.IsSpawned && !cup.IsDead && cup.gameObject.scene.handle==rivet.Owner.Player.gameObject.scene.handle && rivet.Cups.Add(cup.ObjectId))
                        {
                            var impact=Message(rivet.Owner,"cup",1,cup.transform.position,cup.transform.position);
                            impact.Targets=new[]{cup.ObjectId};impact.Damage=rivet.Owner.Player.Data.DamageData.FinalDamage*ForgeRules.Damage(1,rivet.Level,rivet.Charged);
                            if(CannonRules.ValidDamage(impact.Damage))Send(rivet.Owner.Player,impact);
                        }
                        continue;
                    }
                    var skull=collider.GetComponentInParent<CollectorSkullProjectile>();
                    if(skull!=null)
                    {
                        // These are destructible network projectiles, not NPCs.
                        // Use the native server destruction path and let its pool
                        // synchronize removal without treating skulls as NPCs.
                        if(SlotNetwork.IsHost && skull.IsSpawned && skull.IsServerStarted &&
                            skull.gameObject.scene.handle==rivet.Owner.Player.gameObject.scene.handle && rivet.Flight.Hit(skull.ObjectId))
                        {
                            var at=rivet.Position+rivet.Direction*Math.Clamp(hit.distance,0,step);
                            skull.Destruct();RivetSkulls++;
                            if(rivet.Flight.Finished){end=at;stopped=true;break;}
                        }
                        continue;
                    }
                    if(collider.isTrigger)continue;
                    end=rivet.Position+rivet.Direction*Math.Clamp(hit.distance,0,step);RivetWallStops++;stopped=true;break;
                }
                rivet.Position=end;stopped|=rivet.Flight.Finished;
            }
            if(!stopped)continue;
            rivets.Remove(rivet);
            var stop=Message(rivet.Owner,"rivet-stop",rivet.Slot,rivet.Position,rivet.Position+rivet.Direction);stop.Unit=rivet.Unit;stop.Charged=rivet.Charged;stop.Feedback=rivet.FinalImpact ? 1:0;Publish(stop);
            if(rivet.Slot==1)
            {
                var group=rivet.Group;group.Flying--;if(!rivet.Fragment)group.Complete=true;
                // Resolve after the parent and its fragments finish, so direct victims
                // cannot also receive a secondary fragment from this parent shot.
                if(group.Complete && group.Flying==0 && Alive(rivet.Owner.Player))
                    foreach(var contact in group.Fragments)
                        if(Enemy(contact.Target) && !group.Direct.Contains(contact.Target.ObjectId) && group.Secondary.Add(contact.Target.ObjectId))
                            Damage(rivet.Owner,1,contact.At,new[]{contact.Target},ForgeRules.FragmentDamageMultiplier);
            }
        }
    }
    private static void Damage(Engineer e,int slot,Vector3 at,IEnumerable<NpcCharacter> targets,float multiplier,Vector3 direction=default,bool direct=false,int hitOrdinal=0)
    {
        var ids=targets.Where(Enemy).Select(t=>t.ObjectId).Distinct().ToArray();if(ids.Length==0)return;
        var damage=e.Player.Data.DamageData.FinalDamage*multiplier;
        if(!CannonRules.ValidDamage(damage))return;
        // Bound individual network receipts without dropping the rest of a crowd.
        foreach(var batch in ids.Chunk(32))
        {
            var m=Message(e,"hit",slot,at,at);m.Damage=damage;m.Targets=batch;m.Direct=direct;m.Feedback=hitOrdinal;
            if(direct)pendingHits[m.Sequence]=new(e,slot,batch,Time.unscaledTime+5,direction);
            Send(e.Player,m);
        }
    }
}
