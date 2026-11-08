using System.IO.Compression;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// Shared, pinned geometry and one PBR atlas across all ten possible companions.
internal sealed class CannonArt
{
    private static JsonDocument? model;
    private static readonly Dictionary<string,Material> materials=new();
    private static readonly Dictionary<string,Texture2D> textures=new();
    private static readonly Dictionary<string,(Mesh Mesh,Material[] Palette)> grouped=new();
    internal static Sprite? Icon { get; private set; }
    internal static MeshRenderer? Pickup { get; private set; }
    internal static Material MaterialFor(string name)=>materials[name];
    internal readonly GameObject Root;
    private readonly Dictionary<string,Transform> pivots=new();
    private readonly List<MeshRenderer> renderers=new();
    private float nextIntegrity;
    private float leftRoll,rightRoll,recoilAt=-100,pitch;
    private Vector3 last,lastForward=Vector3.forward;
    internal static Vector3 V(JsonElement p)=>new(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle());
    private static Stream Resource(string name)=>typeof(CannonArt).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.cannon-wheel."+name)
        ?? throw new InvalidDataException("Missing Cannon Wheel asset: "+name);
    private static JsonElement Data
    {
        get
        {
            if(model==null)
            {
                using var source=Resource("json.gz");using var gzip=new GZipStream(source,CompressionMode.Decompress);
                model=JsonDocument.Parse(gzip);
            }
            return model.RootElement;
        }
    }
    private static Vector3 At(string name)=>V(Data.GetProperty("rig").GetProperty(name).GetProperty("at"));
    private static Texture2D Texture(string name,bool linear)
    {
        if(textures.TryGetValue(name,out var existing) && existing!=null)return existing;
        using var source=Resource(name);using var bytes=new MemoryStream();source.CopyTo(bytes);
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,linear)
        {
            name="Cannon Wheel "+name,hideFlags=HideFlags.DontUnloadUnusedAsset,
            filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=4
        };
        if(!ImageConversion.LoadImage(texture,bytes.ToArray(),false))throw new InvalidDataException("Could not decode Cannon Wheel "+name);
        Object.DontDestroyOnLoad(texture);textures[name]=texture;return texture;
    }
    internal static void Prepare(Material? template)
    {
        if(Pickup!=null)return;
        var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? template?.shader
            ?? throw new InvalidOperationException("Cannon Wheel has no usable PBR shader.");
        var pbr=new Material(shader){name="Cannon Wheel / painted steel, ceramic and rubber PBR",hideFlags=HideFlags.DontUnloadUnusedAsset};
        foreach(var key in new[]{"_BaseColor","_Color"})if(pbr.HasProperty(key))pbr.SetColor(key,Color.white);
        var albedo=Texture("albedo.png",false);pbr.mainTexture=albedo;
        foreach(var key in new[]{"_BaseMap","_MainTex"})if(pbr.HasProperty(key))pbr.SetTexture(key,albedo);
        if(pbr.HasProperty("_BumpMap")){pbr.SetTexture("_BumpMap",Texture("normal.png",true));pbr.EnableKeyword("_NORMALMAP");}
        if(pbr.HasProperty("_BumpScale"))pbr.SetFloat("_BumpScale",1);
        if(pbr.HasProperty("_MetallicGlossMap"))
        {pbr.SetTexture("_MetallicGlossMap",Texture("metal-smooth.png",true));pbr.EnableKeyword("_METALLICSPECGLOSSMAP");pbr.EnableKeyword("_METALLICGLOSSMAP");}
        if(pbr.HasProperty("_Metallic"))pbr.SetFloat("_Metallic",1);
        if(pbr.HasProperty("_Smoothness"))pbr.SetFloat("_Smoothness",1);
        if(pbr.HasProperty("_GlossMapScale"))pbr.SetFloat("_GlossMapScale",1);
        if(pbr.HasProperty("_OcclusionMap"))pbr.SetTexture("_OcclusionMap",Texture("occlusion.png",true));
        if(pbr.HasProperty("_OcclusionStrength"))pbr.SetFloat("_OcclusionStrength",.65f);
        if(pbr.HasProperty("_EmissionMap"))pbr.SetTexture("_EmissionMap",Texture("emission.png",false));
        if(pbr.HasProperty("_EmissionColor")){pbr.SetColor("_EmissionColor",Color.white*2.5f);pbr.EnableKeyword("_EMISSION");}
        Object.DontDestroyOnLoad(pbr);materials["pbr"]=pbr;
        // Shot FX have their own tiny untextured materials. Orange armor does not glow.
        foreach(var (name,color,emission) in new[]{("orange",new Color(1,.40f,.08f),true),("bore",new Color(.025f,.035f,.045f),false)})
        {
            var m=new Material(shader){name="Cannon Wheel FX / "+name,hideFlags=HideFlags.DontUnloadUnusedAsset};
            foreach(var key in new[]{"_BaseColor","_Color"})if(m.HasProperty(key))m.SetColor(key,color);
            if(emission && m.HasProperty("_EmissionColor")){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*3);}
            Object.DontDestroyOnLoad(m);materials[name]=m;
        }
        using var icon=Resource("png");using var bytes=new MemoryStream();icon.CopyTo(bytes);
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="Cannon Wheel icon",hideFlags=HideFlags.DontUnloadUnusedAsset};
        if(!ImageConversion.LoadImage(texture,bytes.ToArray(),false))throw new InvalidDataException("Cannon icon could not be decoded.");
        texture.filterMode=FilterMode.Bilinear;Object.DontDestroyOnLoad(texture);
        Icon=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),256);Object.DontDestroyOnLoad(Icon);
        var pickup=ReadMesh(Data.GetProperty("pickup"),"Cannon Wheel PBR pickup");
        var cache=new GameObject("GKMM Cannon Wheel mesh cache");Object.DontDestroyOnLoad(cache);cache.SetActive(false);
        foreach(var entry in Data.GetProperty("meshes").EnumerateObject())
        {
            var geometry=ReadMesh(entry.Value,"Cannon Wheel / "+entry.Name);grouped[entry.Name]=geometry;
            var holder=new GameObject(entry.Name);holder.transform.SetParent(cache.transform,false);
            holder.AddComponent<MeshFilter>().sharedMesh=geometry.Mesh;
            holder.AddComponent<MeshRenderer>().sharedMaterials=geometry.Palette;
        }
        var root=new GameObject("GKMM Cannon Wheel pickup template");Object.DontDestroyOnLoad(root);
        root.AddComponent<MeshFilter>().sharedMesh=pickup.Mesh;Pickup=root.AddComponent<MeshRenderer>();Pickup.sharedMaterials=pickup.Palette;root.SetActive(false);
        Plugin.Logger.LogInfo("Cannon Wheel CW-PBR-01 ready: six rigid assemblies, shared 2K PBR atlas; shader="+shader.name);
    }
    internal CannonArt(Vector3 position)
    {
        Root=new GameObject("GKMM Cannon Wheel");Root.transform.position=position;last=position;
        foreach(var group in Data.GetProperty("rig").EnumerateObject())
        {
            var node=new GameObject(group.Name);var info=group.Value;
            var parent=info.TryGetProperty("parent",out var parentName) ? pivots[parentName.GetString()!]:Root.transform;
            node.transform.SetParent(parent,false);
            node.transform.localPosition=V(info.GetProperty("at"))-(info.TryGetProperty("parent",out parentName) ? At(parentName.GetString()!):Vector3.zero);
            pivots[group.Name]=node.transform;
        }
        foreach(var pair in grouped)
        {
            var node=pair.Key=="body" ? Root:pivots[pair.Key].gameObject;
            node.AddComponent<MeshFilter>().sharedMesh=pair.Value.Mesh;
            var renderer=node.AddComponent<MeshRenderer>();renderer.sharedMaterials=pair.Value.Palette;renderers.Add(renderer);
        }
    }
    private static (Mesh Mesh,Material[] Palette) ReadMesh(JsonElement data,string name)
    {
        var vertices=data.GetProperty("vertices").EnumerateArray().Select(V).ToArray();
        var mesh=new Mesh{name=name,hideFlags=HideFlags.DontUnloadUnusedAsset,indexFormat=vertices.Length>65535 ? IndexFormat.UInt32:IndexFormat.UInt16};
        mesh.vertices=vertices;mesh.normals=data.GetProperty("normals").EnumerateArray().Select(V).ToArray();
        mesh.uv=data.GetProperty("uv").EnumerateArray().Select(p=>new Vector2(p[0].GetSingle(),p[1].GetSingle())).ToArray();
        var sub=data.GetProperty("submeshes").EnumerateArray().ToArray();mesh.subMeshCount=sub.Length;
        for(var i=0;i<sub.Length;i++)mesh.SetTriangles(sub[i].GetProperty("triangles").EnumerateArray().Select(v=>v.GetInt32()).ToArray(),i);
        mesh.RecalculateBounds();mesh.RecalculateTangents();Object.DontDestroyOnLoad(mesh);
        return(mesh,sub.Select(s=>materials[s.GetProperty("material").GetString()!]).ToArray());
    }
    internal static Vector3 AimOrigin(Vector3 position)=>position+Vector3.up*At("pitch").y;
    internal bool Intact()
    {
        if(Root==null)return false;
        if(Time.unscaledTime<nextIntegrity)return true;
        nextIntegrity=Time.unscaledTime+.5f;
        if(pivots.Values.Any(p=>p==null) || renderers.Any(r=>r==null))return false;
        if(!Root.activeSelf)Root.SetActive(true);
        foreach(var renderer in renderers)
        {
            if(!renderer.gameObject.activeSelf)renderer.gameObject.SetActive(true);
            if(!renderer.enabled)renderer.enabled=true;if(renderer.forceRenderingOff)renderer.forceRenderingOff=false;
        }
        return true;
    }
    internal static Vector3 ShotOrigin(Vector3 position,Vector3 aim)
    {
        var full=aim-AimOrigin(position);var flat=full;flat.y=0;
        var yaw=flat.sqrMagnitude>.0001f ? Quaternion.LookRotation(flat):Quaternion.identity;
        var elevation=-Mathf.Clamp(Mathf.Atan2(full.y,Mathf.Max(.1f,flat.magnitude))*Mathf.Rad2Deg,-12,25);
        return position+yaw*(At("pitch")+Quaternion.Euler(elevation,0,0)*(V(Data.GetProperty("muzzle"))-At("recoil")));
    }
    internal Vector3 Muzzle=>pivots["recoil"].TransformPoint(V(Data.GetProperty("muzzle"))-At("recoil"));
    internal void Snap(Vector3 position,float yaw)
    {Root.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));last=position;lastForward=Root.transform.forward;}
    internal void Animate(Vector3 aim)
    {
        var forward=Root.transform.forward;var delta=Root.transform.position-last;
        var distance=Vector3.Dot(delta,(forward+lastForward).normalized);
        var turn=Vector3.SignedAngle(lastForward,forward,Vector3.up)*Mathf.Deg2Rad;
        if(delta.sqrMagnitude<9)
        {
            var radius=Data.GetProperty("wheelRadius").GetSingle();var halfTrack=Mathf.Abs(At("wheel_l").x);
            leftRoll+=(distance+turn*halfTrack)/radius*Mathf.Rad2Deg;
            rightRoll+=(distance-turn*halfTrack)/radius*Mathf.Rad2Deg;
        }
        last=Root.transform.position;lastForward=forward;
        // Source axle names are retained through the DCC-to-Unity reflection.
        var left=At("wheel_l").x<0 ? "wheel_l":"wheel_r";
        var right=left=="wheel_l" ? "wheel_r":"wheel_l";
        pivots[left].localRotation=Quaternion.Euler(leftRoll,0,0);pivots[right].localRotation=Quaternion.Euler(rightRoll,0,0);
        var full=aim-AimOrigin(Root.transform.position);var direction=full;direction.y=0;
        if(direction.sqrMagnitude>.01f)
            pivots["turret"].rotation=Quaternion.Slerp(pivots["turret"].rotation,Quaternion.LookRotation(direction),Mathf.Min(1,Time.deltaTime*12));
        var targetPitch=-Mathf.Clamp(Mathf.Atan2(full.y,Mathf.Max(.1f,direction.magnitude))*Mathf.Rad2Deg,-12,25);
        pitch=Mathf.MoveTowardsAngle(pitch,targetPitch,Time.deltaTime*180);
        pivots["pitch"].localRotation=Quaternion.Euler(pitch,0,0);
        var age=Time.time-recoilAt;
        var kick=age>=0 && age<.35f ? (1-Mathf.Exp(-age/.012f))*Mathf.Exp(-age/.085f)*(1-age/.35f):0;
        pivots["recoil"].localPosition=At("recoil")-At("pitch")-Vector3.forward*(.22f*kick);
    }
    internal void Fire()=>recoilAt=Time.time;
    internal void Destroy(){if(Root!=null)Object.Destroy(Root);}
}
