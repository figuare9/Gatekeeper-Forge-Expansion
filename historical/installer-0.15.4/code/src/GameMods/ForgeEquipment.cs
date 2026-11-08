using System.IO.Compression;
using System.Text.Json;
using Gatekeeper.Mods;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// Mechanical parts are rigidly attached to the authored bones. No metal deformation.
internal static class ForgeEquipment
{
    private sealed class Part
    {
        public string Name {get;set;}="";
        public int Bone {get;set;}
        public string Material {get;set;}="";
        public ForgeMesh Mesh {get;set;}=new();
    }
    private sealed class Definition
    {
        public string Name {get;set;}="";
        public ForgeBone[] Bones {get;set;}=Array.Empty<ForgeBone>();
        public Part[] Parts {get;set;}=Array.Empty<Part>();
        public ForgeClip[] Clips {get;set;}=Array.Empty<ForgeClip>();
        public Dictionary<string,float[]> Colors {get;set;}=new();
        public float[] YawPivot {get;set;}=new[]{0f,.873f,0f};
        public float[] Pivot {get;set;}=new[]{0f,1.192f,-.111f};
        public float[] Muzzle {get;set;}=new[]{0f,1.472f,1.65f};
        public float TwinOffset {get;set;}=.22f;
        public float HealthHeight {get;set;}=2.42f;
    }
    private sealed record Asset(Definition Data,Mesh[] Meshes,Material[] Materials);
    private static readonly Dictionary<int,Asset> assets=new();
    private static readonly Dictionary<IntPtr,Instance> instances=new();
    // Decode the large supplied textures before the player can deploy a sentry.
    // Keep native resources through the game's UnloadUnusedAssets scene cleanup;
    // managed cache references alone do not make them reachable to Unity.
    internal static void Prepare()
    {
        var asset=Load(2);
        foreach(var mesh in asset.Meshes)mesh.UploadMeshData(false);
    }
    private static Asset Load(int slot)
    {
        if(!assets.TryGetValue(slot,out var asset) || asset.Materials.Any(m=>m==null) || asset.Meshes.Any(m=>m==null))
        {
            var name=slot==2 ? "sentry":slot==3 ? "charge":"aegis";
            using var stream=typeof(ForgeEquipment).Assembly.GetManifestResourceStream($"Gatekeeper.CustomMods.Assets.Forge.forge-{name}-rig.json.gz")
                ?? throw new InvalidDataException("Missing equipment rig: "+name);
            using var gzip=new GZipStream(stream,CompressionMode.Decompress);
            var data=JsonSerializer.Deserialize<Definition>(gzip) ?? throw new InvalidDataException("Empty equipment rig.");
            var meshes=new Mesh[data.Parts.Length];var materials=new Material[meshes.Length];
            var palette=new Dictionary<string,Material>();
            for(var i=0;i<meshes.Length;i++)
            {
                var part=data.Parts[i];var d=part.Mesh;d.Validate(0,false);var n=d.Vertices.Length/3;
                var vertices=new Vector3[n];var normals=new Vector3[n];var uv=new Vector2[n];
                for(var j=0;j<n;j++){vertices[j]=new(d.Vertices[j*3],d.Vertices[j*3+1],d.Vertices[j*3+2]);normals[j]=new(d.Normals[j*3],d.Normals[j*3+1],d.Normals[j*3+2]);uv[j]=new(d.Uv[j*2],d.Uv[j*2+1]);}
                var mesh=new Mesh{name=part.Name,hideFlags=HideFlags.DontUnloadUnusedAsset,vertices=vertices,normals=normals,uv=uv,triangles=d.Triangles};mesh.RecalculateBounds();mesh.RecalculateTangents();Object.DontDestroyOnLoad(mesh);meshes[i]=mesh;
                if(!palette.TryGetValue(part.Material,out var material))
                {
                    var c=data.Colors[part.Material];var color=new Color(c[0],c[1],c[2],c[3]);
                    material=new Material(ForgeArt.Material("dark")){name="Forge equipment "+part.Material,hideFlags=HideFlags.DontUnloadUnusedAsset};
                    foreach(var k in new[]{"_BaseColor","_Color"})if(material.HasProperty(k))material.SetColor(k,color);
                    if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.28f);
                    if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",part.Material is "metal" or "steel" ? .6f:.15f);
                    if(part.Material=="supplied")SuppliedMaterial(material);
                    else if(part.Material is "energy" or "lens"){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",color*1.8f);}
                    Object.DontDestroyOnLoad(material);palette[part.Material]=material;
                }
                materials[i]=material;
            }
            asset=new(data,meshes,materials);assets[slot]=asset;
        }
        return asset;
    }
    private static readonly Dictionary<string,Texture2D> textures=new();
    private static Texture2D Texture(string name,bool linear)
    {
        if(textures.TryGetValue(name,out var old) && old!=null)return old;
        using var stream=typeof(ForgeEquipment).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.Forge."+name) ?? throw new InvalidDataException("Missing sentry texture: "+name);
        using var bytes=new MemoryStream();stream.CopyTo(bytes);
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,linear){name="Forge supplied turret "+name,hideFlags=HideFlags.DontUnloadUnusedAsset,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Repeat,anisoLevel=4};
        if(!ImageConversion.LoadImage(texture,bytes.ToArray(),false))throw new InvalidDataException("Could not decode sentry texture: "+name);
        Object.DontDestroyOnLoad(texture);textures[name]=texture;return texture;
    }
    private static void SuppliedMaterial(Material material)
    {
        var albedo=Texture("sentry-albedo.png",false);material.mainTexture=albedo;
        foreach(var k in new[]{"_BaseMap","_MainTex"})if(material.HasProperty(k))material.SetTexture(k,albedo);
        if(material.HasProperty("_BumpMap")){material.SetTexture("_BumpMap",Texture("sentry-normal.png",true));material.EnableKeyword("_NORMALMAP");}
        if(material.HasProperty("_MetallicGlossMap")){material.SetTexture("_MetallicGlossMap",Texture("sentry-metal-smooth.png",true));material.EnableKeyword("_METALLICSPECGLOSSMAP");}
        if(material.HasProperty("_Smoothness"))material.SetFloat("_Smoothness",.65f);
        if(material.HasProperty("_Metallic"))material.SetFloat("_Metallic",1);
        if(material.HasProperty("_EmissionMap"))material.SetTexture("_EmissionMap",Texture("sentry-emission.png",false));
        if(material.HasProperty("_EmissionColor")){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",new Color(.3f,1.1f,1.3f));}
    }
    private static Vector3 V(float[] p)=>new(p[0],p[1],p[2]);
    internal static (float Yaw,float Pitch) SentryAngles(Vector3 at,Vector3 target,int level,bool second=false)
    {
        var d=Load(2).Data;var delta=target-at-V(d.YawPivot);
        var side=d.Muzzle[0]+(level>=3 ? (second ? d.TwinOffset:-d.TwinOffset):0);
        var flat=new Vector2(delta.x,delta.z).magnitude;
        var yaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg-Mathf.Asin(Math.Clamp(side/Math.Max(flat,.01f),-.95f,.95f))*Mathf.Rad2Deg;
        var local=Quaternion.AngleAxis(-yaw,Vector3.up)*delta-(V(d.Pivot)-V(d.YawPivot));
        var distance=new Vector2(local.y,local.z).magnitude;
        var pitch=-Mathf.Atan2(local.y,local.z)*Mathf.Rad2Deg+Mathf.Asin(Math.Clamp((d.Muzzle[1]-d.Pivot[1])/Math.Max(distance,.01f),-.95f,.95f))*Mathf.Rad2Deg;
        return(yaw,Math.Clamp(pitch,-55,45));
    }
    internal static (Vector3 Muzzle,Vector3 Direction) SentryShot(Vector3 at,Vector3 target,int level,bool second)
    {
        var d=Load(2).Data;var angles=SentryAngles(at,target,level,second);var yaw=Quaternion.AngleAxis(angles.Yaw,Vector3.up);var pitch=Quaternion.AngleAxis(angles.Pitch,Vector3.right);
        var offset=V(d.Muzzle)-V(d.Pivot)+(level>=3 ? Vector3.right*(second ? d.TwinOffset:-d.TwinOffset):Vector3.zero);
        return(at+V(d.YawPivot)+yaw*(V(d.Pivot)-V(d.YawPivot)+pitch*offset),yaw*pitch*Vector3.forward);
    }
    internal static GameObject Create(int slot,Vector3 at,Scene scene)
    {
        var asset=Load(slot);
        var root=new GameObject("Forge rigged "+asset.Data.Name);SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=at;
        instances[root.Pointer]=new(root,asset,slot);return root;
    }
    internal static void State(GameObject root,Vector3 aim,float health,float maxHealth,int level)
    {if(root!=null && instances.TryGetValue(root.Pointer,out var i)){i.Target=aim;i.Hp=maxHealth>0 ? Math.Clamp(health/maxHealth,0,1):1;i.Level=level;}}
    internal static void Fire(GameObject root,bool second,Vector3 direction)
    {if(root!=null && instances.TryGetValue(root.Pointer,out var i))i.Shot(second,direction);}
    internal static void Destroy(GameObject root)
    {if(root!=null && instances.TryGetValue(root.Pointer,out var i)){i.DeathAt=Time.time;Object.Destroy(root,.72f);}}
    internal static void Render()
    {
        foreach(var pair in instances.ToArray())
        {
            var i=pair.Value;
            if(i.Root==null){instances.Remove(pair.Key);continue;}
            // An incomplete rig must not throw every frame and interrupt the
            // remaining shields/projectiles. The next device state rebuilds it.
            try
            {
                if(!i.Intact()){Object.Destroy(i.Root);instances.Remove(pair.Key);continue;}
                i.Update();
            }
            catch(Exception ex)
            {
                // Retire just this damaged render rig; other visuals keep updating.
                if(i.Root!=null)Object.Destroy(i.Root);instances.Remove(pair.Key);
                Plugin.Logger.LogWarning("Forge equipment visual will reload: "+ex.Message);
            }
        }
    }
    private sealed class Instance
    {
        internal readonly GameObject Root;
        private readonly Asset asset;
        private readonly int slot;
        private readonly Transform[] bones;
        private readonly GameObject[] parts;
        private readonly MeshRenderer[] renderers;
        private float nextIntegrity;
        private readonly float start=Time.time;
        internal Vector3 Target;
        internal float Hp=1,FireAt=-100,DeathAt=-100;
        internal int Level;
        internal bool Second;
        private float yaw,pitch;
        private float leftAt=-100,rightAt=-100;
        private readonly int yawBone,pitchBone,leftBone,rightBone;
        private readonly Renderer[] flashes=Array.Empty<Renderer>();
        internal Instance(GameObject root,Asset a,int s)
        {
            Root=root;asset=a;slot=s;Target=root.transform.position+Vector3.forward*10;
            bones=new Transform[a.Data.Bones.Length];
            yawBone=Array.FindIndex(a.Data.Bones,b=>b.Name=="Yaw");pitchBone=Array.FindIndex(a.Data.Bones,b=>b.Name=="Pitch");
            leftBone=Array.FindIndex(a.Data.Bones,b=>b.Name=="BarrelL");rightBone=Array.FindIndex(a.Data.Bones,b=>b.Name=="BarrelR");
            for(var n=0;n<bones.Length;n++)
            {
                var b=a.Data.Bones[n];var t=new GameObject(b.Name).transform;t.SetParent(b.Parent>=0 ? bones[b.Parent]:root.transform,false);bones[n]=t;Apply(t,b.Rest,0);
            }
            parts=new GameObject[a.Data.Parts.Length];
            renderers=new MeshRenderer[parts.Length];
            for(var n=0;n<parts.Length;n++)
            {
                var p=a.Data.Parts[n];var ob=new GameObject(p.Name);ob.transform.SetParent(bones[p.Bone],false);
                ob.AddComponent<MeshFilter>().sharedMesh=a.Meshes[n];renderers[n]=ob.AddComponent<MeshRenderer>();renderers[n].sharedMaterial=a.Materials[n];parts[n]=ob;
            }
            if(slot==2)
            {
                flashes=new Renderer[2];
                for(var n=0;n<2;n++)
                {
                    var flash=GameObject.CreatePrimitive(PrimitiveType.Sphere);flash.name=n==0 ? "Left muzzle flash":"Right muzzle flash";
                    flash.transform.SetParent(bones[n==0 ? leftBone:rightBone],false);
                    // Each flash follows its recoil bone. Both barrels use the supplied bore.
                    var boneAt=V(a.Data.Bones[leftBone].Rest);
                    flash.transform.localPosition=V(a.Data.Muzzle)-V(a.Data.Pivot)-boneAt+Vector3.forward*.04f;
                    flash.transform.localScale=new(.09f,.09f,.22f);var collider=flash.GetComponent<Collider>();collider.enabled=false;Object.Destroy(collider);
                    flashes[n]=flash.GetComponent<Renderer>();flashes[n].sharedMaterial=ForgeArt.Material("hot");flashes[n].enabled=false;
                }
                // The native construct already displays its health HUD. A second
                // world-space bar looks like a floating piece of the turret.
            }
        }
        internal void Shot(bool second,Vector3 direction)
        {
            FireAt=Time.time;Second=second;if(second)rightAt=Time.time;else leftAt=Time.time;
            // Shot receipts align the render to the host's actual barrel trajectory.
            yaw=Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg;
            pitch=-Mathf.Atan2(direction.y,new Vector2(direction.x,direction.z).magnitude)*Mathf.Rad2Deg;
        }
        private void SampleBone(int bone,string clipName,float time)
        {
            var clip=asset.Data.Clips.First(c=>c.Name==clipName);var f=Math.Clamp(time,0,clip.Duration)*clip.Fps;
            var lo=Math.Clamp((int)f,0,clip.Frames.Length-1);var hi=Math.Min(lo+1,clip.Frames.Length-1);var k=bone*10;var x=clip.Frames[lo];var y=clip.Frames[hi];var t=f-lo;
            bones[bone].localPosition=Vector3.Lerp(new(x[k],x[k+1],x[k+2]),new(y[k],y[k+1],y[k+2]),t);
            bones[bone].localRotation=Quaternion.Slerp(new(x[k+3],x[k+4],x[k+5],x[k+6]),new(y[k+3],y[k+4],y[k+5],y[k+6]),t);
        }
        internal bool Intact()
        {
            if(Time.unscaledTime<nextIntegrity)return true;
            nextIntegrity=Time.unscaledTime+.5f;
            if(bones.Any(b=>b==null) || parts.Any(p=>p==null) || renderers.Any(r=>r==null) || flashes.Any(r=>r==null) ||
                asset.Meshes.Any(m=>m==null) || asset.Materials.Any(m=>m==null))return false;
            if(DeathAt<0)
            {
                if(!Root.activeSelf)Root.SetActive(true);
                for(var n=0;n<renderers.Length;n++)
                {
                    var r=renderers[n];var visible=slot!=2 || asset.Data.Bones[asset.Data.Parts[n].Bone].Name!="BarrelR" || Level>=3;
                    if(parts[n].activeSelf!=visible)parts[n].SetActive(visible);
                    if(!r.enabled)r.enabled=true;if(r.forceRenderingOff)r.forceRenderingOff=false;
                    if(r.sharedMaterial!=asset.Materials[n])r.sharedMaterial=asset.Materials[n];
                }
            }
            return true;
        }
        internal void Update()
        {
            var age=Time.time-start;var fire=Time.time-FireAt;var death=Time.time-DeathAt;
            var name=DeathAt>0 ? "destroy":slot==3 ? "arm":age<.75f ? "deploy":"idle";
            var clip=asset.Data.Clips.FirstOrDefault(c=>c.Name==name) ?? asset.Data.Clips[0];
            var time=name=="destroy" ? death:name.StartsWith("fire") ? fire:age;
            var f=(clip.Loop ? time%clip.Duration:Math.Min(time,clip.Duration))*clip.Fps;
            var lo=Math.Clamp((int)f,0,clip.Frames.Length-1);var hi=Math.Min(lo+1,clip.Frames.Length-1);var t=f-lo;
            for(var n=0;n<bones.Length;n++)
            {
                var x=clip.Frames[lo];var y=clip.Frames[hi];var k=n*10;
                bones[n].localPosition=Vector3.Lerp(new(x[k],x[k+1],x[k+2]),new(y[k],y[k+1],y[k+2]),t);
                bones[n].localRotation=Quaternion.Slerp(new(x[k+3],x[k+4],x[k+5],x[k+6]),new(y[k+3],y[k+4],y[k+5],y[k+6]),t);
                bones[n].localScale=Vector3.Lerp(new(x[k+7],x[k+8],x[k+9]),new(y[k+7],y[k+8],y[k+9]),t);
            }
            if(slot==2)
            {
                if(DeathAt<0 && age>=.75f)
                {
                    if(Time.time-leftAt<.3f)SampleBone(leftBone,"fire_left",Time.time-leftAt);
                    if(Time.time-rightAt<.3f)SampleBone(rightBone,"fire_right",Time.time-rightAt);
                    if(fire<.3f)SampleBone(pitchBone,Second ? "fire_right":"fire_left",fire);
                }
                var angles=SentryAngles(Root.transform.position,Target,Level,Second && fire<.3f);
                yaw=Mathf.MoveTowardsAngle(yaw,angles.Yaw,360*Time.deltaTime);pitch=Mathf.MoveTowards(pitch,angles.Pitch,180*Time.deltaTime);
                bones[yawBone].localRotation=Quaternion.AngleAxis(yaw,Vector3.up)*bones[yawBone].localRotation;
                bones[pitchBone].localRotation=Quaternion.AngleAxis(pitch,Vector3.right)*bones[pitchBone].localRotation;
                for(var n=0;n<parts.Length;n++)
                {
                    var bone=asset.Data.Bones[asset.Data.Parts[n].Bone].Name;
                    if(bone=="BarrelR")parts[n].SetActive(Level>=3);
                    // One barrel sits centrally until the twin-barrel evolution.
                    if(bone=="BarrelL")parts[n].transform.localPosition=Level>=3 ? Vector3.left*asset.Data.TwinOffset:Vector3.zero;
                }
                flashes[0].transform.localPosition=V(asset.Data.Muzzle)-V(asset.Data.Pivot)-V(asset.Data.Bones[leftBone].Rest)+Vector3.forward*.04f+(Level>=3 ? Vector3.left*asset.Data.TwinOffset:Vector3.zero);
                flashes[0].enabled=DeathAt<0 && Time.time-leftAt<.055f;flashes[1].enabled=DeathAt<0 && Level>=3 && Time.time-rightAt<.055f;
            }
        }
        private static void Apply(Transform t,float[] p,int k)
        {t.localPosition=new(p[k],p[k+1],p[k+2]);t.localRotation=new(p[k+3],p[k+4],p[k+5],p[k+6]);t.localScale=new(p[k+7],p[k+8],p[k+9]);}
    }
}
