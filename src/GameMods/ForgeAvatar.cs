using System.IO.Compression;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Mods;
using Il2CppInterop.Runtime;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// Render-only skeleton. The native chassis retains input, damage, networking,
// inventory, collision and the original character lifecycle.
internal static class ForgeAvatar
{
    private static ForgeModel? model;
    private static Mesh? bodyMesh,gunMesh;
    private static Vector3 muzzleLocal;
    private static Material? bodyMaterial,gunMaterial;
    private static Sprite? portrait;
    private static readonly Dictionary<nint,Instance> instances=new();
    private static readonly Dictionary<string,Texture2D> textures=new();
    internal static string? Error {get;private set;}
    internal static int Count=>instances.Count;
    private const string RootName="GKMM Forge custom character";
    internal static Sprite Portrait
    {
        get
        {
            if(portrait!=null)return portrait;
            var texture=Texture("forge-portrait.png",false);
            portrait=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),256);
            Object.DontDestroyOnLoad(portrait);return portrait;
        }
    }
    private static Stream Resource(string name)=>typeof(ForgeAvatar).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.Forge."+name)
        ?? throw new InvalidDataException("Forge asset missing: "+name);
    private static ForgeModel Data
    {
        get
        {
            if(model!=null)return model;
            using var source=Resource("forge-character.json.gz");using var gzip=new GZipStream(source,CompressionMode.Decompress);
            model=ForgeModel.Read(gzip);return model;
        }
    }
    private static Texture2D Texture(string name,bool linear)
    {
        if(textures.TryGetValue(name,out var old) && old!=null)return old;
        using var stream=Resource(name);using var bytes=new MemoryStream();stream.CopyTo(bytes);
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,linear){name="Forge "+name};
        if(!ImageConversion.LoadImage(texture,bytes.ToArray(),false))throw new InvalidDataException("Could not decode "+name);
        texture.filterMode=FilterMode.Trilinear;texture.wrapMode=TextureWrapMode.Repeat;texture.anisoLevel=4;
        Object.DontDestroyOnLoad(texture);textures[name]=texture;return texture;
    }
    private static Material MaterialFor(string part,Material? template)
    {
        var shader=Shader.Find("Universal Render Pipeline/Lit") ?? template?.shader ?? Shader.Find("Standard");
        if(shader==null)throw new InvalidOperationException("No Forge character shader is available.");
        var material=new Material(shader);
        material.name="Forge supplied "+part+" PBR";
        var albedo=Texture(part+"-albedo.png",false);material.mainTexture=albedo;
        foreach(var key in new[]{"_MainTex","_BaseMap","_Albedo"})if(material.HasProperty(key))material.SetTexture(key,albedo);
        foreach(var key in new[]{"_Color","_BaseColor"})if(material.HasProperty(key))material.SetColor(key,Color.white);
        foreach(var keyword in new[]{"TCP2_COLORS_AS_NORMALS","TCP2_TANGENT_AS_NORMALS","TCP2_UV1_AS_NORMALS","TCP2_UV2_AS_NORMALS","TCP2_UV3_AS_NORMALS","TCP2_UV4_AS_NORMALS","TCP2_UV_NORMALS_FULL","TCP2_UV_NORMALS_ZW"})material.DisableKeyword(keyword);
        if(material.HasProperty("_BumpMap")){material.SetTexture("_BumpMap",Texture(part+"-normal.png",true));material.EnableKeyword("_NORMALMAP");}
        if(material.HasProperty("_MetallicGlossMap")){material.SetTexture("_MetallicGlossMap",Texture(part+"-metal-smooth.png",true));material.EnableKeyword("_METALLICSPECGLOSSMAP");}
        if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",1);
        if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.7f);
        if(material.HasProperty("_EmissionMap"))material.SetTexture("_EmissionMap",Texture(part+"-emission.png",false));
        if(material.HasProperty("_EmissionColor")){material.SetColor("_EmissionColor",new Color(.4f,1.5f,1.8f));material.EnableKeyword("_EMISSION");}
        Object.DontDestroyOnLoad(material);return material;
    }
    private static Vector3 V(float[] a,int offset=0)=>new(a[offset],a[offset+1],a[offset+2]);
    private static Quaternion Q(float[] a,int offset=0)=>new(a[offset],a[offset+1],a[offset+2],a[offset+3]);
    private static Matrix4x4 Matrix(float[] a)=>new(){m00=a[0],m01=a[1],m02=a[2],m03=a[3],m10=a[4],m11=a[5],m12=a[6],m13=a[7],m20=a[8],m21=a[9],m22=a[10],m23=a[11],m30=a[12],m31=a[13],m32=a[14],m33=a[15]};
    private static Mesh ReadMesh(ForgeMesh data,bool skin)
    {
        var mesh=new Mesh{name=skin ? "Forge supplied skinned body":"Forge supplied rivet gun"};
        var count=data.Vertices.Length/3;
        var vertices=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];
        for(var i=0;i<count;i++){vertices[i]=V(data.Vertices,i*3);normals[i]=V(data.Normals,i*3);uv[i]=new(data.Uv[i*2],data.Uv[i*2+1]);}
        mesh.vertices=vertices;mesh.normals=normals;mesh.uv=uv;mesh.triangles=data.Triangles;
        if(!skin)
        {
            // Center on the actual forward rim, independent of duplicated UV /
            // hard-normal vertices. The old marker was slightly above the bore.
            var end=vertices.Max(v=>v.z);var rim=vertices.Where(v=>v.z>=end-.002f).ToArray();
            muzzleLocal=rim.Length>=12 ? new((rim.Min(v=>v.x)+rim.Max(v=>v.x))*.5f,
                (rim.Min(v=>v.y)+rim.Max(v=>v.y))*.5f,end+.012f):V(Data.Muzzle);
        }
        if(skin)
        {
            var weights=new BoneWeight[count];
            for(var i=0;i<count;i++)weights[i]=new(){m_BoneIndex0=data.BoneIndices[i*4],m_BoneIndex1=data.BoneIndices[i*4+1],m_BoneIndex2=data.BoneIndices[i*4+2],m_BoneIndex3=data.BoneIndices[i*4+3],m_Weight0=data.Weights[i*4],m_Weight1=data.Weights[i*4+1],m_Weight2=data.Weights[i*4+2],m_Weight3=data.Weights[i*4+3]};
            mesh.boneWeights=weights;mesh.bindposes=Data.Bones.Select(b=>Matrix(b.Bind)).ToArray();
        }
        mesh.RecalculateBounds();mesh.RecalculateTangents();Object.DontDestroyOnLoad(mesh);return mesh;
    }
    internal static void Attach(GameObject root,CharManager? player=null)
    {
        if(root==null || instances.ContainsKey(root.Pointer))return;
        try
        {
            var data=Data;
            var original=root.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
            var template=original?.sharedMaterial;
            if(bodyMaterial==null)bodyMaterial=MaterialFor("body",template);
            if(gunMaterial==null)gunMaterial=MaterialFor("gun",template);
            if(bodyMesh==null)bodyMesh=ReadMesh(data.Body,true);
            if(gunMesh==null)gunMesh=ReadMesh(data.Gun,false);
            var instance=new Instance(root,player,original?.gameObject.layer ?? root.layer);
            instances[root.Pointer]=instance;
            Error=null;Plugin.Logger.LogInfo($"Forge custom model attached: {(player==null ? "menu":"gameplay")}; {data.Bones.Length} bones, {data.Clips.Length} clips, two-handed rivet gun.");
            Plugin.WriteStatus("ready");
        }
        catch(Exception ex)
        {
            if(Error!=ex.Message){Error=ex.Message;Plugin.Logger.LogError("Forge custom model: "+ex);Plugin.WriteStatus("ready");}
        }
    }
    internal static void PrepareMenu(GameObject template)
    {
        Attach(template);
        if(!instances.ContainsKey(template.Pointer))throw new InvalidOperationException("Could not prepare Forge's menu model: "+Error);
    }
    internal static void Event(CharManager p,string name,float boost=0)
    {
        Attach(p.gameObject,p);
        if(!instances.TryGetValue(p.gameObject.Pointer,out var instance))return;
        if(name is "fire" or "fire_charged"){instance.FireAt=Time.time;instance.FirePower=name=="fire_charged" ? 1.55f:1;}
        else{instance.EventName=name;instance.EventAt=Time.time;}
        instance.BoostUntil=Math.Max(instance.BoostUntil,Time.time+Math.Clamp(boost,0,11));
    }
    internal static Vector3 Muzzle(CharManager p)
    {
        if(instances.TryGetValue(p.gameObject.Pointer,out var instance) && instance.Socket!=null)return instance.Socket.position;
        return p.CachedTransform.position+Vector3.up*2.7f+p.CachedTransform.forward*.8f;
    }
    internal static Vector3 AimMuzzle(CharManager p,Vector3 target)
    {
        if(instances.TryGetValue(p.gameObject.Pointer,out var instance))instance.Aim(target-p.CachedTransform.position);
        return instances.TryGetValue(p.gameObject.Pointer,out instance) ? instance.Socket.position:Muzzle(p);
    }
    internal static void Aim(CharManager p,Vector3 direction)
    {if(instances.TryGetValue(p.gameObject.Pointer,out var instance))instance.Aim(direction);}
    internal static void Hold(CharManager p,int slot)
    {if(instances.TryGetValue(p.gameObject.Pointer,out var i)){i.EventName=slot==3 ? "throw_charge":"deploy_field";i.EventAt=Time.time-.22f;}}
    internal static bool Ready(CharManager p)=>instances.TryGetValue(p.gameObject.Pointer,out var i) && i.CanAct;
    internal static Vector3 LeftHand(CharManager p)
    {
        if(instances.TryGetValue(p.gameObject.Pointer,out var instance))return instance.LeftHandPosition;
        return p.CachedTransform.position+Vector3.up*2.7f;
    }
    internal static void Tick()
    {
        foreach(var pair in instances.ToArray())
        {
            var instance=pair.Value;
            if(instance.Parent==null){instance.Destroy();instances.Remove(pair.Key);continue;}
            if(!instance.Parent.activeInHierarchy)continue;
            if(!instance.Intact())
            {
                // Replace only the render skeleton. Native health, charges,
                // inventory and network identity stay with the same character.
                if(instance.Root!=null){instance.Root.name="Forge retired visual";instance.Root.SetActive(false);}
                instance.Destroy();instances.Remove(pair.Key);Attach(instance.Parent,instance.Player);
                if(instances.TryGetValue(pair.Key,out var replacement))replacement.Resume(instance);
                continue;
            }
            try{instance.Update();}
            catch(Exception ex){if(Error!=ex.Message){Error=ex.Message;Plugin.Logger.LogError("Forge animation: "+ex);}}
        }
    }
    internal static void Clear(){foreach(var i in instances.Values)i.Destroy();instances.Clear();}

    private readonly record struct Pose(Vector3 Position,Quaternion Rotation,Vector3 Scale)
    {
        internal static Pose Mix(Pose a,Pose b,float t)=>new(Vector3.Lerp(a.Position,b.Position,t),Quaternion.Slerp(a.Rotation,b.Rotation,t),Vector3.Lerp(a.Scale,b.Scale,t));
    }
    private sealed class Instance
    {
        internal readonly GameObject Parent,Root;
        internal readonly CharManager? Player;
        internal readonly Transform Socket;
        private readonly Transform[] bones;
        private readonly int weaponIndex,rightHandIndex,leftHandIndex;
        private readonly Quaternion rightGripRotation,leftGripRotation;
        private readonly Leg[] legs;
        private readonly record struct Leg(int Thigh,int Shin,int Foot,int Armour,Vector3 ThighDirection,Vector3 ShinDirection,Quaternion ThighRest,Quaternion ShinRest,Quaternion ArmourRest);
        internal Vector3 LeftHandPosition=>bones[Array.FindIndex(Data.Bones,b=>b.Name=="Left_Hand")].position;
        private readonly Renderer[] hidden;
        private readonly SkinnedMeshRenderer body;
        private readonly MeshRenderer gun,flash;
        private readonly MaterialPropertyBlock properties=new();
        private readonly Dictionary<string,ForgeClip> clips=Data.Clips.ToDictionary(c=>c.Name);
        internal string EventName="";
        internal float EventAt=-100,FireAt=-100,BoostUntil;
        private float emittedFireAt=-100;
        private float moveBlend,deadAt=-1,appearAt,stopAt=-100;
        private readonly ForgeLocomotion locomotion;
        private bool wasMoving;
        private float diagnoseAt=Time.unscaledTime+2;
        private float nextIntegrity;
        private Vector3 last,direction=Vector3.forward;
        private Vector3 castDirection;
        private float aimPitch;
        internal float FirePower=1;
        internal bool CanAct=>Player!=null && Player.IsActiveAndAlive && Time.time-appearAt>=clips["appear"].Duration;
        internal Instance(GameObject parent,CharManager? player,int layer)
        {
            Parent=parent;Player=player;last=parent.transform.position;appearAt=player==null ? -100:Time.time;
            // The inactive menu template already contains the posed visual.
            // Native Instantiate clones its bones and skin references together;
            // adopt them rather than constructing a second skeleton on selection.
            var prepared=parent.transform.Find(RootName);
            hidden=parent.GetComponentsInChildren<Renderer>(true).Where(r=>(prepared==null || !r.transform.IsChildOf(prepared)) &&
                (r.TryCast<SkinnedMeshRenderer>()!=null || r.TryCast<MeshRenderer>()!=null && UnderArmature(r.transform,parent.transform))).ToArray();
            foreach(var effect in parent.GetComponentsInChildren<ParticleSystem>(true))
                if(UnderArmature(effect.transform,parent.transform))
                {effect.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);effect.gameObject.SetActive(false);}
            Root=prepared!=null ? prepared.gameObject:new GameObject(RootName);
            if(prepared==null){Root.transform.SetParent(parent.transform,false);Root.layer=layer;}
            var heading=Root.transform.forward;locomotion=new(new(heading.x,0,heading.z));
            bones=new Transform[Data.Bones.Length];
            for(var i=0;i<bones.Length;i++)
            {
                var def=Data.Bones[i];var holder=def.Parent>=0 ? bones[def.Parent]:Root.transform;
                var bone=prepared!=null ? holder.Find("Forge_"+def.Name):null;
                if(bone==null){var go=new GameObject("Forge_"+def.Name);go.layer=Root.layer;bone=go.transform;bone.SetParent(holder,false);}
                bones[i]=bone;
                bone.localPosition=V(def.Rest);bone.localRotation=Q(def.Rest,3);bone.localScale=V(def.Rest,7);
            }
            weaponIndex=Array.FindIndex(Data.Bones,b=>b.Name=="Weapon");
            rightHandIndex=Array.FindIndex(Data.Bones,b=>b.Name=="Right_Hand");
            leftHandIndex=Array.FindIndex(Data.Bones,b=>b.Name=="Left_Hand");
            var neutral=clips["idle"].Frames[0];var rotations=new Quaternion[bones.Length];
            for(var i=0;i<bones.Length;i++)rotations[i]=(Data.Bones[i].Parent>=0 ? rotations[Data.Bones[i].Parent]:Quaternion.identity)*Q(neutral,i*10+3);
            rightGripRotation=Quaternion.Inverse(rotations[weaponIndex])*rotations[rightHandIndex];
            leftGripRotation=Quaternion.Inverse(rotations[weaponIndex])*rotations[leftHandIndex];
            legs=new[]{ReadLeg("Right"),ReadLeg("Left")};
            var skin=Root.transform.Find("Forge supplied body")?.gameObject;
            if(skin==null){skin=new GameObject("Forge supplied body");skin.layer=Root.layer;skin.transform.SetParent(Root.transform,false);}
            body=skin.GetComponent<SkinnedMeshRenderer>() ?? skin.AddComponent<SkinnedMeshRenderer>();body.sharedMesh=bodyMesh;body.bones=bones;body.rootBone=bones[0];body.sharedMaterial=bodyMaterial;
            body.quality=SkinQuality.Bone4;body.updateWhenOffscreen=true;body.localBounds=new Bounds(new Vector3(0,2,0),new Vector3(7,7,7));
            var weaponBone=bones[weaponIndex];var weapon=weaponBone.Find("Forge supplied rivet gun")?.gameObject;
            if(weapon==null){weapon=new GameObject("Forge supplied rivet gun");weapon.layer=Root.layer;weapon.transform.SetParent(weaponBone,false);weapon.AddComponent<MeshFilter>();weapon.AddComponent<MeshRenderer>();}
            weapon.GetComponent<MeshFilter>().sharedMesh=gunMesh;gun=weapon.GetComponent<MeshRenderer>();gun.sharedMaterial=gunMaterial;
            var muzzle=weapon.transform.Find("Forge muzzle");
            if(muzzle==null){muzzle=new GameObject("Forge muzzle").transform;muzzle.SetParent(weapon.transform,false);}
            muzzle.localPosition=muzzleLocal;Socket=muzzle;
            var light=Socket.Find("Forge muzzle flash")?.gameObject;
            if(light==null){light=GameObject.CreatePrimitive(PrimitiveType.Sphere);light.name="Forge muzzle flash";light.layer=Root.layer;light.transform.SetParent(Socket,false);light.transform.localScale=new Vector3(.2f,.2f,.35f);var collider=light.GetComponent<Collider>();collider.enabled=false;Object.Destroy(collider);}
            flash=light.GetComponent<MeshRenderer>();flash.sharedMaterial=ForgeArt.Material("cyan");flash.enabled=false;
            var animator=parent.GetComponent<Animator>();if(animator!=null)animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            foreach(var renderer in hidden)if(renderer!=null)renderer.enabled=false;
            Update();
        }
        private static bool UnderArmature(Transform transform,Transform stop)
        {for(var p=transform;p!=null && p.Pointer!=stop.Pointer;p=p.parent)if(p.name.EndsWith("_Armature",StringComparison.Ordinal))return true;return false;}
        private Pose Sample(ForgeClip clip,int bone,float time)
        {
            var frame=clip.Loop ? Mathf.Repeat(time,clip.Duration)*clip.Fps:Math.Clamp(time*clip.Fps,0,clip.Frames.Length-1);
            var a=Math.Min((int)frame,clip.Frames.Length-1);var b=Math.Min(a+1,clip.Frames.Length-1);var t=frame-a;var offset=bone*10;
            var av=clip.Frames[a];var bv=clip.Frames[b];
            return new(Vector3.Lerp(V(av,offset),V(bv,offset),t),Quaternion.Slerp(Q(av,offset+3),Q(bv,offset+3),t),Vector3.Lerp(V(av,offset+7),V(bv,offset+7),t));
        }
        private Pose Add(Pose pose,ForgeClip clip,int bone,float time)
        {
            var basis=Sample(clip,bone,0);var sample=Sample(clip,bone,time);
            return new(pose.Position+sample.Position-basis.Position,pose.Rotation*(Quaternion.Inverse(basis.Rotation)*sample.Rotation),pose.Scale);
        }
        private Leg ReadLeg(string side)
        {
            int Index(string part)=>Array.FindIndex(Data.Bones,b=>b.Name==side+"_"+part);
            var thigh=Index("UpperLeg");var shin=Index("LowerLeg");var foot=Index("Foot");var armour=Index("KneeArmor");
            Quaternion Rest(int i)=>Quaternion.Inverse(Root.transform.rotation)*bones[i].rotation;
            return new(thigh,shin,foot,armour,Root.transform.InverseTransformDirection(bones[shin].position-bones[thigh].position),
                Root.transform.InverseTransformDirection(bones[foot].position-bones[shin].position),Rest(thigh),Rest(shin),Rest(armour));
        }
        private static Quaternion SwingFromRest(Quaternion rest,Vector3 from,Vector3 to)
        {
            var q=ForgeRigMath.PreserveLegRoll(new(rest.x,rest.y,rest.z,rest.w),new(from.x,from.y,from.z),new(to.x,to.y,to.z));
            return new(q.X,q.Y,q.Z,q.W);
        }
        private void PreserveLegRoll(Leg leg)
        {
            // Mixing local FK poses can reintroduce roll despite twist-free
            // authored frames. Preserve the mixed hip/knee/ankle positions,
            // then recover the original segment roll and planted foot facing.
            var thigh=bones[leg.Thigh];var shin=bones[leg.Shin];var foot=bones[leg.Foot];
            var hip=thigh.position;var knee=shin.position;var ankle=foot.position;var footRotation=foot.rotation;
            var root=Root.transform.rotation;
            var thighRest=root*leg.ThighRest;var shinRest=root*leg.ShinRest;
            thigh.rotation=SwingFromRest(thighRest,root*leg.ThighDirection,knee-hip);
            shin.rotation=SwingFromRest(shinRest,root*leg.ShinDirection,ankle-knee);
            foot.rotation=footRotation;
            var upperDelta=thigh.rotation*Quaternion.Inverse(thighRest);var lowerDelta=shin.rotation*Quaternion.Inverse(shinRest);
            bones[leg.Armour].rotation=Quaternion.Slerp(upperDelta,lowerDelta,.5f)*(root*leg.ArmourRest);
        }
        private void HoldGrip(int handIndex,float[] grip,Quaternion rotation)
        {
            // FK interpolation and additive recoil do not preserve a closed
            // two-hand chain. Solve the arm after all animation layers mix.
            var hand=bones[handIndex];var lower=hand.parent;var upper=lower.parent;
            var origin=upper.position;var joint=lower.position;var end=hand.position;
            var goal=bones[weaponIndex].TransformPoint(V(grip));
            var upperLength=Vector3.Distance(origin,joint);var lowerLength=Vector3.Distance(joint,end);
            var offset=goal-origin;var distance=offset.magnitude;
            if(distance<.0001f || upperLength<.0001f || lowerLength<.0001f)return;
            var axis=offset/distance;
            var reach=Math.Clamp(distance,Math.Abs(upperLength-lowerLength)+.0001f,upperLength+lowerLength-.0001f);
            var bend=joint-origin-axis*Vector3.Dot(joint-origin,axis);
            if(bend.sqrMagnitude<.000001f)bend=Vector3.Cross(axis,Root.transform.up);
            if(bend.sqrMagnitude<.000001f)bend=Vector3.Cross(axis,Root.transform.right);
            var along=(upperLength*upperLength-lowerLength*lowerLength+reach*reach)/(2*reach);
            var height=MathF.Sqrt(Math.Max(0,upperLength*upperLength-along*along));
            var elbow=origin+axis*along+bend.normalized*height;
            upper.rotation=Quaternion.FromToRotation(joint-origin,elbow-origin)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(hand.position-lower.position,origin+axis*reach-lower.position)*lower.rotation;
            hand.rotation=bones[weaponIndex].rotation*rotation;
        }
        internal void Aim(Vector3 forward)
        {
            aimPitch=Math.Clamp(Mathf.Atan2(forward.y,new Vector2(forward.x,forward.z).magnitude)*Mathf.Rad2Deg,-50,50);
            forward.y=0;if(forward.sqrMagnitude<.001f)return;
            castDirection=forward.normalized;
            locomotion.Fire(new(castDirection.x,0,castDirection.z),Time.time);
            Root.transform.rotation=Quaternion.LookRotation(castDirection);
        }
        private void Face(Vector3 velocity,float dt)
        {
            if(Player==null || Player.Data?.IsDead==true)return;
            var aim=castDirection;
            if(locomotion.IsAiming(Time.time))
            {
                if(Player.IsOwner && Player.PlaneRay!=null)aim=Player.PlaneRay.LookDirection();
                else if(Player.LookTarget!=null)aim=Player.LookTarget.position-Player.CachedTransform.position;
            }
            locomotion.Face(Time.time,dt,new(velocity.x,0,velocity.z),new(aim.x,0,aim.z),Player.Controller?.rotationRate ?? 720);
            var heading=locomotion.Heading;
            // Reapply world heading even while idle: the hidden donor's parent
            // may rotate independently toward its own mouse target.
            Root.transform.rotation=Quaternion.LookRotation(new(heading.X,0,heading.Z));
        }
        internal bool Intact()
        {
            if(Root==null || body==null || gun==null || flash==null || Socket==null)return false;
            if(Time.unscaledTime<nextIntegrity)return true;
            nextIntegrity=Time.unscaledTime+.5f;
            return bones.All(b=>b!=null);
        }
        internal void Resume(Instance previous)
        {
            appearAt=previous.appearAt;deadAt=previous.deadAt;EventName=previous.EventName;EventAt=previous.EventAt;
            FireAt=previous.FireAt;FirePower=previous.FirePower;BoostUntil=previous.BoostUntil;
            emittedFireAt=previous.emittedFireAt;
            castDirection=previous.castDirection;aimPitch=previous.aimPitch;
        }
        internal void Update()
        {
            if(!Parent.activeInHierarchy)return;
            // Native appearance setup may disable newly discovered renderers.
            // These renderers follow our own appearance/death animation.
            if(!Root.activeSelf)Root.SetActive(true);
            // The custom skeleton is outside the native skin controller's renderer
            // list. Respect native death state before visibility recovery: otherwise
            // liveness repair resurrects the corpse beneath the tombstone every frame.
            var visible=Player==null || Player.IsActiveAndAlive && Player.Data?.IsDead!=true;
            body.enabled=visible;gun.enabled=visible;
            body.forceRenderingOff=false;gun.forceRenderingOff=false;
            if(!visible){if(Player?.Data?.IsDead==true && deadAt<0)deadAt=Time.time;flash.enabled=false;foreach(var renderer in hidden)if(renderer!=null)renderer.enabled=false;last=Parent.transform.position;return;}
            // Menu skin cleanup can explicitly destroy a renderer's material
            // while the next scene is already spawning. Unity destroyed-object
            // checks are required here; managed null coalescing misses them.
            if(bodyMaterial==null)bodyMaterial=MaterialFor("body",null);
            if(gunMaterial==null)gunMaterial=MaterialFor("gun",null);
            if(body.sharedMaterial!=bodyMaterial)body.sharedMaterial=bodyMaterial;
            if(gun.sharedMaterial!=gunMaterial)gun.sharedMaterial=gunMaterial;
            var dt=Math.Max(0,Time.deltaTime);var position=Parent.transform.position;var velocity=dt>.00001f ? (position-last)/dt:Vector3.zero;last=position;velocity.y=0;
            // Owner physics velocity is stable between fixed ticks; position
            // differences alone alternate between zero and doubled speed.
            if(Player!=null && Player.IsOwner && Player.Controller!=null)velocity=Player.Controller.GetVelocity();
            velocity.y=0;
            if(!float.IsFinite(velocity.x) || !float.IsFinite(velocity.z) || velocity.magnitude>60 || Player?.Data?.IsDead==true)velocity=Vector3.zero;
            var speed=Player==null ? 0:velocity.magnitude;
            Face(velocity,dt);
            var moving=speed>.2f;
            if(moving && velocity.sqrMagnitude>.01f)direction=Vector3.Slerp(direction,Root.transform.InverseTransformDirection(velocity.normalized),Math.Min(1,dt*16));
            if(wasMoving && !moving)stopAt=Time.time;
            wasMoving=moving;moveBlend=Mathf.MoveTowards(moveBlend,moving ? 1:0,dt*8);
            var nativeMove=Player?.Data?.MoveSpeedData;
            var sprint=nativeMove?.IsSprint==true && Player?.Data?.IsAttack!=true && !locomotion.IsAiming(Time.time) && direction.z>.5f;
            var baseSpeed=nativeMove?.BaseMoveSpeed ?? 13;
            // Native sprint is FinalMoveSpeed + BaseMoveSpeed * bonus. Its
            // serialized .5 is an additional 50%, not a total speed multiplier.
            var sprintRatio=1+(nativeMove?.sprintSpeedMultiplier ?? .5f)*baseSpeed/Math.Max(.01f,nativeMove?.FinalMoveSpeed ?? baseSpeed);
            locomotion.Gait(speed,dt,sprint,baseSpeed,sprintRatio);
            var phase=locomotion.Phase;
            var forward=clips[direction.z>=0 ? "run_forward":"run_back"];
            var sideways=clips[direction.x>=0 ? "run_right":"run_left"];
            var sideWeight=Math.Abs(direction.x)/Math.Max(.001f,Math.Abs(direction.x)+Math.Abs(direction.z));
            var dead=Player?.Data?.IsDead==true;
            if(dead && deadAt<0)deadAt=Time.time;
            if(!dead && deadAt>=0){deadAt=-1;appearAt=Time.time;}
            var age=Time.time-EventAt;var active=clips.TryGetValue(EventName,out var eventClip) && age<eventClip.Duration;
            var fireAge=Time.time-FireAt;
            for(var i=0;i<bones.Length;i++)
            {
                var idle=Sample(clips["idle"],i,Time.time);
                var run=Pose.Mix(Sample(forward,i,phase*forward.Duration),Sample(sideways,i,phase*sideways.Duration),sideWeight);
                run=Pose.Mix(run,Sample(clips["sprint"],i,phase*clips["sprint"].Duration),locomotion.SprintBlend);
                var pose=Pose.Mix(idle,run,moveBlend);
                var stopAge=Time.time-stopAt;
                if(!moving && stopAge<clips["stop"].Duration)pose=Pose.Mix(pose,Sample(clips["stop"],i,stopAge),Math.Min(.35f,Mathf.Sin(stopAge/clips["stop"].Duration*Mathf.PI)*.35f));
                if(active)pose=Add(pose,eventClip!,i,age);
                if(fireAge<clips["fire"].Duration && (!active || !Data.Bones[i].Name.StartsWith("Left_",StringComparison.Ordinal)))pose=Add(pose,clips["fire"],i,fireAge);
                if(dead)pose=Sample(clips["death"],i,Time.time-deadAt);
                else if(Time.time-appearAt<clips["appear"].Duration)pose=Sample(clips["appear"],i,Time.time-appearAt);
                bones[i].localPosition=pose.Position;bones[i].localRotation=pose.Rotation;bones[i].localScale=pose.Scale;
            }
            foreach(var leg in legs)PreserveLegRoll(leg);
            if(fireAge>=0 && fireAge<.3f && FirePower>1)bones[weaponIndex].position-=Root.transform.forward*(.16f*(FirePower-1)*Mathf.Sin(fireAge/.3f*Mathf.PI));
            if(locomotion.IsAiming(Time.time))bones[weaponIndex].rotation=Quaternion.AngleAxis(-aimPitch,Root.transform.right)*bones[weaponIndex].rotation;
            HoldGrip(rightHandIndex,Data.RightGrip,rightGripRotation);
            if(!active || dead || Time.time-appearAt<clips["appear"].Duration)HoldGrip(leftHandIndex,Data.LeftGrip,leftGripRotation);
            foreach(var renderer in hidden)if(renderer!=null)renderer.enabled=false;
            flash.enabled=!dead && fireAge>=0 && fireAge<.035f;
            // Resolve this frame's turn, locomotion, recoil and hand IK before
            // emitting from the actual visible barrel. The host's earlier
            // collision origin remains authoritative for projectile damage.
            if(Player!=null && fireAge>=0 && fireAge<.25f && emittedFireAt!=FireAt)
            {
                emittedFireAt=FireAt;
                ForgeShotFx.Fire(Socket.position,Socket.forward,FirePower>1,true);
            }
            properties.SetColor("_EmissionColor",new Color(.4f,1.5f,1.8f)*(BoostUntil>Time.time ? 1.8f+.3f*Mathf.Sin(Time.time*12):1));
            body.SetPropertyBlock(properties);gun.SetPropertyBlock(properties);
            if(Time.unscaledTime>=diagnoseAt){diagnoseAt=float.PositiveInfinity;Diagnose();}
        }
        internal void Destroy(){if(Root!=null)Object.Destroy(Root);}
        internal void Diagnose()
        {
            Plugin.Logger.LogInfo($"Forge render diagnostics: player={Player!=null}; parent={Parent.name} at {Parent.transform.position}, scale={Parent.transform.lossyScale}; active={Root.activeInHierarchy}; layer={Root.layer}; body={body.enabled}/{body.forceRenderingOff}; bounds={body.bounds}; hips={bones[0].position}; materialPresent={body.sharedMaterial!=null}.");
            Plugin.Logger.LogInfo($"Forge grip diagnostics after animation blending: right={Vector3.Distance(bones[rightHandIndex].position,bones[weaponIndex].TransformPoint(V(Data.RightGrip))):F6}; left={Vector3.Distance(bones[leftHandIndex].position,bones[weaponIndex].TransformPoint(V(Data.LeftGrip))):F6}; event={EventName}.");
        }
    }
}
