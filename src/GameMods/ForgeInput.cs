using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.Base;
using Gatekeeper.CameraScripts.HUD.SkillPanelStuff;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Char_Scripts.General.SupportPack;
using Gatekeeper.General.InputSystem;
using Gatekeeper.General.InputSystem.Enums;
using Gatekeeper.General.Predictor;
using Gatekeeper.General.Tools;
using Gatekeeper.Mods;
using Gatekeeper.NPC;
using Gatekeeper.Enemy.Collector.Projectile;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static partial class ForgeWorld
{
    private static int inputFrame=-1,heldSlot,queuedSlot;
    private static float queuedAt,previewAt;
    private static Vector3 queuedPoint;
    private static bool waitForRelease;
    private static Vector3 heldPoint;
    private static GameObject? preview,heldProp;
    private static Mesh? previewMesh;
    private const int PreviewSegments=48,PreviewRings=3;
    private static readonly Vector3[] previewVertices=new Vector3[1+PreviewSegments*PreviewRings];
    private static bool Input(PlayableCharacterSkills __instance)
    {
        var p=__instance.characterMain?.Manager;
        if(!ForgeRegistry.Is(p))return true;
        if(!p!.IsOwner)return false;
        if(inputFrame==Time.frameCount)return false;
        if(!Combat || !SlotNetwork.Ready || !p.IsActiveAndAlive || GameplayInputManager.CastSkillsIsBlocked ||
            !ForgeAvatar.Ready(p) || ShopUI.IsOpen || Time.deltaTime<=0)
        {
            CancelAim();waitForRelease=true;InputState=$"blocked combat={Combat},network={SlotNetwork.Ready},alive={p.IsActiveAndAlive},input={GameplayInputManager.CastSkillsIsBlocked},skills={__instance.CanUseSkills},shop={ShopUI.IsOpen},dt={Time.deltaTime}";
            if(p.Data!=null && p.Data.IsAttack)p.Data.IsAttack=false;return false;
        }
        inputFrame=Time.frameCount;
        var space=GameplayInputManager.ButtonIsPressed(ActionName.ThirdSkill);
        var field=GameplayInputManager.ButtonIsPressed(ActionName.FourthSkill);
        if(waitForRelease){if(space || field)return false;waitForRelease=false;}
        var selected=0;Vector3 point=default;
        if(heldSlot==0)
        {
            var start=GameplayInputManager.ButtonWasPressed(ActionName.FourthSkill) ? 4:
                GameplayInputManager.ButtonWasPressed(ActionName.ThirdSkill) ? 3:0;
            if(start>0 && Available(p,start))
            {
                heldSlot=start;heldPoint=Placement(p,CursorPoint(p),start);
                preview=ForgeArt.Ring(heldPoint,start==3 ? ForgeRules.GrenadeRadius(Level(p,3)):ForgeRules.Radius(Level(p,4)),p.gameObject.scene);
                preview.name="Forge red aim preview";preview.GetComponent<LineRenderer>().sharedMaterial=ForgeArt.Material("red");
                AddPreviewFill();previewAt=0;
                heldProp=ForgeEquipment.Create(start,ForgeAvatar.LeftHand(p),p.gameObject.scene);
            }
        }
        if(heldSlot>0)
        {
            heldPoint=Placement(p,CursorPoint(p),heldSlot);PreviewCircle(heldPoint,heldSlot==3 ? ForgeRules.GrenadeRadius(Level(p,3)):ForgeRules.Radius(Level(p,4)));
            ForgeAvatar.Hold(p,heldSlot);
            if(heldProp!=null)heldProp.transform.position=ForgeAvatar.LeftHand(p);
            if(!(heldSlot==3 ? space:field)){selected=heldSlot;point=heldPoint;CancelAim();queuedSlot=selected;queuedPoint=point;queuedAt=Time.unscaledTime;}
            if(p.Data.IsAttack)p.Data.IsAttack=false;
            if(selected==0){InputState="aiming "+heldSlot;return false;}
        }
        if(queuedSlot>0)
        {
            if(Time.unscaledTime-queuedAt>1)queuedSlot=0;
            else{selected=queuedSlot;point=queuedPoint;}
        }
        var attacking=selected==0 && GameplayInputManager.ButtonIsPressed(ActionName.FirstSkill);
        if(p.Data.IsAttack!=attacking)p.Data.IsAttack=attacking;
        if(selected==0 && GameplayInputManager.ButtonWasPressed(ActionName.SecondSkill)){selected=2;point=Placement(p,CursorPoint(p));}
        if(selected==0 && attacking)
        {
            selected=1;
            var muzzle=ForgeAvatar.Muzzle(p);var aim=NativeAim(p);
            var direction=aim!=null ? aim.GetSkillDirection(muzzle):CursorPoint(p)-muzzle;
            if(direction.sqrMagnitude<.001f)direction=p.CachedTransform.forward;
            point=muzzle+direction.normalized*ForgeRules.RivetRange;
        }
        if(selected==0 || !Available(p,selected))return false;
        // Held skills commit once, on release; polling limits only repeated LMB requests.
        if(selected==1 && Time.unscaledTime<nextInput)return false;
        if(pendingCasts.TryGetValue(p.ObjectId,out var pending))
        {if(Time.unscaledTime-pending.At<2){InputState="awaiting host";return false;}pendingCasts.Remove(p.ObjectId);}
        var id=Guid.NewGuid().ToString("N");pendingCasts[p.ObjectId]=(id,Time.unscaledTime);nextInput=Time.unscaledTime+.05f;
        queuedSlot=0;
        InputState="cast "+selected;
        SlotNetwork.Request(new(){Kind="forge",Forge=new(){Kind="cast",Id=id,Character=p.ObjectId,Slot=selected,Scene=(int)GlobalPredictor.CurrentScene,Sequence=++requestSequence,Aim=A(point)}});
        return false;
    }
    private static bool Available(CharManager p,int slot)=>p.Data.SkillsData.Skills.TryGetValue((SkillType)slot,out var data) && data.IsAvailable;
    private static SkillAimLogic? NativeAim(CharManager p)=>p.GetComponentInChildren<SkillAimLogic>(true);
    private static Vector3 CursorPoint(CharManager p)
    {
        // CameraManager uses the native mouse/gamepad virtual cursor. The ground
        // ray ignores shields, weapon meshes, enemies and decorative prop layers.
        if(p.CameraManager!=null){var hit=p.CameraManager.MouseGroundRayHit();if(hit.collider!=null)return hit.point;}
        return p.PlaneRay.MousePoint();
    }
    private const int GroundMask=1<<8; // Native Constants.GroundLayer, also used by Tech Hunter.
    private static Vector3 Placement(CharManager p,Vector3 point,int slot=2)
    {TryPlacement(p,point,slot,out var ground);return ground;}
    private static bool TryPlacement(CharManager p,Vector3 requested,int slot,out Vector3 point)
    {
        point=requested;
        if(!float.IsFinite(point.x)||!float.IsFinite(point.y)||!float.IsFinite(point.z))return false;
        var origin=p.CachedTransform.position;
        point=origin+Vector3.ClampMagnitude(point-origin,ForgeRules.CastRange(slot));
        var from=origin+Vector3.up*1.5f;var to=point+Vector3.up*.25f-from;
        if(Physics.Raycast(from,to.normalized,out var obstruction,to.magnitude,GroundMask,QueryTriggerInteraction.Ignore) &&
            GroundSurface(obstruction.collider) && obstruction.normal.y<.55f)
            point=from+to.normalized*Math.Max(0,obstruction.distance-.5f);
        // Only the game's ground collision layer may support a deployed device.
        // Casting through other layers projects shields, characters and props down to terrain.
        if(!Physics.Raycast(point+Vector3.up*3,Vector3.down,out var hit,12,GroundMask,QueryTriggerInteraction.Ignore) ||
            !GroundSurface(hit.collider) || hit.normal.y<.55f)return false;
        point=hit.point;
        if((point-origin).sqrMagnitude>Mathf.Pow(ForgeRules.CastRange(slot)+.1f,2))return false;
        if(slot is 2 or 4)
        {
            // Ground-layer scenery can include roofs/podium tops. Native walkable
            // navigation must agree with the surface height before deployment.
            if(NavSupportTool.IsReady && (!NavSupportTool.GetNearestNodeCenter(point,out var walkable) ||
                Math.Abs(walkable.y-point.y)>1.25f))return false;
            // A prop above the ground must not leave a turret embedded in its mesh.
            foreach(var c in Physics.OverlapCapsule(point+Vector3.up*.4f,point+Vector3.up*1.1f,.35f,~GroundMask,QueryTriggerInteraction.Ignore))
                if(c!=null && c.GetComponentInParent<CharManager>()==null && c.GetComponentInParent<NpcCharacter>()==null &&
                    c.GetComponentInParent<CollectorSkullProjectile>()==null)return false;
        }
        return true;
    }
    private static bool GroundSurface(Collider? c)=>c!=null && c.gameObject.layer==8 &&
        c.GetComponentInParent<NpcCharacter>()==null && c.GetComponentInParent<CharManager>()==null &&
        c.GetComponentInParent<CollectorSkullProjectile>()==null;
    private static float PreviewHeight(Vector3 p,float fallback)
        =>Physics.Raycast(p+Vector3.up*3,Vector3.down,out var hit,12,GroundMask,QueryTriggerInteraction.Ignore) &&
          GroundSurface(hit.collider) && hit.normal.y>.55f?hit.point.y:fallback;
    private static void AddPreviewFill()
    {
        var fill=new GameObject("Forge translucent targeting fill");fill.transform.SetParent(preview!.transform,false);
        previewMesh=new Mesh{name="Forge terrain-conforming aim area"};previewMesh.MarkDynamic();
        var triangles=new List<int>();
        for(var i=0;i<PreviewSegments;i++)
        {
            var next=(i+1)%PreviewSegments;triangles.AddRange(new[]{0,1+i,1+next});
            for(var ring=1;ring<PreviewRings;ring++)
            {
                var inner=1+(ring-1)*PreviewSegments;var outer=1+ring*PreviewSegments;
                triangles.AddRange(new[]{inner+i,outer+i,outer+next,inner+i,outer+next,inner+next});
            }
        }
        previewMesh.vertices=previewVertices;previewMesh.triangles=triangles.ToArray();
        fill.AddComponent<MeshFilter>().sharedMesh=previewMesh;
        var renderer=fill.AddComponent<MeshRenderer>();renderer.sharedMaterial=ForgeArt.TargetFillMaterial();
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
    }
    private static void PreviewCircle(Vector3 at,float radius)
    {
        if(preview==null)return;
        preview.transform.position=at;var line=preview.GetComponent<LineRenderer>();
        if(Time.unscaledTime<previewAt)return;previewAt=Time.unscaledTime+.08f;
        previewVertices[0]=new Vector3(0,PreviewHeight(at,at.y)-at.y+.055f,0);
        for(var ring=1;ring<=PreviewRings;ring++)for(var i=0;i<PreviewSegments;i++)
        {
            var a=i*Mathf.PI*2/PreviewSegments;var offset=new Vector3(Mathf.Sin(a)*radius*ring/PreviewRings,0,Mathf.Cos(a)*radius*ring/PreviewRings);
            var p=at+offset;var height=PreviewHeight(p,at.y);offset.y=height-at.y+.055f;
            previewVertices[1+(ring-1)*PreviewSegments+i]=offset;
            if(ring==PreviewRings){offset.y+=.035f;line.SetPosition(i,offset);if(i==0)line.SetPosition(PreviewSegments,offset);}
        }
        if(previewMesh!=null){previewMesh.vertices=previewVertices;previewMesh.RecalculateBounds();}
    }
    private static void CancelAim()
    {heldSlot=queuedSlot=0;if(preview!=null)Object.Destroy(preview);if(previewMesh!=null)Object.Destroy(previewMesh);if(heldProp!=null)Object.Destroy(heldProp);previewMesh=null;preview=heldProp=null;}
}
