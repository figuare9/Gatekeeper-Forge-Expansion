using Gatekeeper.Char_Scripts.General;
using Gatekeeper.General.Tools;
using Gatekeeper.Mods;
using Pathfinding;
using Pathfinding.RVO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// Halfmetal Head uses AIPath + Seeker + SimpleSmoothModifier on the game's A*
// graph. Reuse those native movement components without spawning a Metalhead.
internal sealed class CannonFollower
{
    internal readonly GameObject Root;
    private readonly CharManager player;
    private readonly CannonTactics tactics;
    internal bool Returning=>tactics.Mode==CannonMove.Return;
    private AIPath? path;
    private Vector3 lastPlayer,direction=Vector3.forward,fallbackVelocity;
    private float observedSpeed,stuck;
    internal int Warp { get; private set; }
    internal Vector3 Position=>Root.transform.position;
    internal Vector3 Direction=>direction;
    internal Vector3 Velocity=>path!=null ? path.velocity:fallbackVelocity;
    internal float Yaw=>Root.transform.eulerAngles.y;

    internal CannonFollower(CharManager owner,int index)
    {
        player=owner;lastPlayer=owner.CachedTransform.position;
        tactics=new(index,HashCode.Combine(owner.OwnerId,index,Guid.NewGuid()));
        Root=new GameObject("Cannon Wheel native follower "+index);
        SceneManager.MoveGameObjectToScene(Root,owner.gameObject.scene);
        Root.transform.position=Ground(lastPlayer+new Vector3(Mathf.Sin(index*2.4f)*8,0,Mathf.Cos(index*2.4f)*8));
    }
    internal static Vector3 Ground(Vector3 desired)
    {
        if(NavSupportTool.IsReady)return NavSupportTool.GetNearestPoint(desired);
        foreach(var hit in Physics.RaycastAll(desired+Vector3.up*3,Vector3.down,7,~0,QueryTriggerInteraction.Ignore).OrderBy(h=>h.distance))
            if(hit.collider!=null && hit.normal.y>.65f && hit.collider.GetComponentInParent<Gatekeeper.NPC.NpcCharacter>()==null &&
                hit.collider.GetComponentInParent<CharManager>()==null)return hit.point;
        return desired;
    }
    private void AttachPath()
    {
        if(path!=null || !NavSupportTool.IsReady || AstarPath.active==null)return;
        Root.SetActive(false);
        Root.AddComponent<Seeker>().drawGizmos=false;
        var smooth=Root.AddComponent<SimpleSmoothModifier>();
        smooth.smoothType=SimpleSmoothModifier.SmoothType.Simple;
        smooth.uniformLength=true;smooth.maxSegmentLength=.4f;smooth.iterations=2;smooth.strength=.25f;
        path=Root.AddComponent<AIPath>();
        path.radius=.65f;path.height=1.3f;path.maxSpeed=7;path.maxAcceleration=24;
        path.rotationSpeed=240;path.slowdownDistance=1.6f;path.endReachedDistance=.35f;
        path.pickNextWaypointDist=1.2f;path.slowWhenNotFacingTarget=false;path.preventMovingBackwards=false;
        path.constrainInsideGraph=true;path.whenCloseToDestination=CloseToDestinationMode.Stop;
        path.orientation=OrientationMode.ZAxisForward;path.enableRotation=true;
        path.gravity=new Vector3(0,-9.81f,0);path.groundMask=~0;path.updatePosition=true;path.updateRotation=true;path.simulateMovement=true;
        path.repathRate=.3f;path.canSearch=true;path.destination=Position;
        if(Object.FindObjectOfType<RVOSimulator>()!=null)
        {
            var rvo=Root.AddComponent<RVOController>();
            rvo.radius=.65f;rvo.height=1.3f;rvo.center=.65f;
            rvo.agentTimeHorizon=.7f;rvo.obstacleTimeHorizon=.5f;rvo.maxNeighbours=10;
            rvo.lockWhenNotMoving=true;rvo.priority=.35f;
        }
        Root.SetActive(true);
        path.FindComponents();
        path.Teleport(Ground(Position),true);
    }
    internal void Tick(Vector3? enemy,bool visible)
    {
        if(Root==null)return;
        AttachPath();
        var at=player.CachedTransform.position;
        var move=at-lastPlayer;move.y=0;lastPlayer=at;
        var dt=Time.deltaTime;
        if(dt<=0)return;
        if(move.sqrMagnitude>.0001f)
            direction=Vector3.Slerp(direction,move.normalized,1-Mathf.Exp(-5*dt)).normalized;
        observedSpeed=Mathf.Lerp(observedSpeed,Mathf.Min(40,move.magnitude/dt),1-Mathf.Exp(-4*dt));
        var point=tactics.Update(new(at.x,at.y,at.z),new(Position.x,Position.y,Position.z),
            enemy.HasValue ? new System.Numerics.Vector3(enemy.Value.x,enemy.Value.y,enemy.Value.z):null,visible,Time.time);
        var goal=new Vector3(point.X,point.Y,point.Z);var distance=(goal-Position).magnitude;
        var speed=CannonRules.FollowSpeed(distance,Mathf.Max(observedSpeed,player.Data.MoveSpeedData.FinalMoveSpeed));
        if(path!=null)
        {
            path.maxSpeed=speed;path.destination=goal;
            var reachable=path.hasPath && float.IsFinite(path.remainingDistance) && (!path.reachedEndOfPath || distance<3);
            var stalled=!path.pathPending && !reachable && distance>10;
            stuck=stalled ? stuck+dt:0;
            if(CannonRules.ShouldRejoin((Position-at).magnitude,path.pathPending,reachable,stuck))
            {path.Teleport(Ground(goal),true);Warp++;stuck=0;}
        }
        else
        {
            // Before a scene's A* graph is ready, only take a clear direct route.
            var delta=goal-Position;delta.y=0;
            var desired=delta.magnitude>.35f && CannonWorld.Sight(Position+Vector3.up*.5f,goal+Vector3.up*.5f)
                ? delta.normalized*Mathf.Min(speed,delta.magnitude*3):Vector3.zero;
            fallbackVelocity=Vector3.MoveTowards(fallbackVelocity,desired,24*dt);
            Root.transform.position+=fallbackVelocity*dt;
            if(fallbackVelocity.sqrMagnitude>.01f)
                Root.transform.rotation=Quaternion.Slerp(Root.transform.rotation,Quaternion.LookRotation(fallbackVelocity),1-Mathf.Exp(-8*dt));
        }
    }
    internal void Destroy(){if(Root!=null)Object.Destroy(Root);}
}
