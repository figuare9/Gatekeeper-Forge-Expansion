using UnityEngine;
using UnityEngine.SceneManagement;
using System.Text.Json;
using Gatekeeper.Mods;
using Gatekeeper.General.LocalPool;
using Gatekeeper.PoolScripts;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// Deliberately replaceable prototype art. No imported game assets are packaged.
internal static partial class ForgeArt
{
    private static readonly Dictionary<int,Sprite> icons=new();
    private static readonly Dictionary<string,Material> materials=new();
    private static Mesh? rivetMesh;
    private static Material? rivetMaterial;
    private static Material? rivetTrailMaterial;
    internal static string RivetTracerSource {get;private set;}="waiting";
    internal static Sprite Icon(int slot)
    {
        if(icons.TryGetValue(slot,out var existing) && existing!=null)return existing;
        var name=slot switch{1=>"rivet",2=>"sentry",3=>"charge",4=>"aegis",_=>"feedback"};
        using var stream=typeof(ForgeArt).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.Forge.forge-ability-"+name+".png")
            ?? throw new InvalidDataException("Missing Forge ability icon: "+name);
        using var bytes=new MemoryStream();stream.CopyTo(bytes);
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="Forge ability "+name,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        if(!ImageConversion.LoadImage(texture,bytes.ToArray(),false))throw new InvalidDataException("Could not decode Forge icon: "+name);
        Object.DontDestroyOnLoad(texture);
        var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),texture.width);
        sprite.name="Forge ability "+name;Object.DontDestroyOnLoad(sprite);icons[slot]=sprite;return sprite;
    }
    internal static Material Material(string key)
    {
        // Native character cleanup can destroy shared materials during a scene change.
        if(materials.TryGetValue(key,out var material) && material!=null)return material;
        var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if(shader==null)throw new InvalidOperationException("No Forge prototype shader is available.");
        var m=new Material(shader);var c=key=="hot" ? new Color(1,.92f,.72f):key=="red" ? new Color(1,.035f,.015f):key=="cyan" ? new Color(.04f,.75f,1):key=="orange" ? new Color(.9f,.3f,.035f):new Color(.12f,.15f,.19f);
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);if(m.HasProperty("_Color"))m.SetColor("_Color",c);
        if(key is "cyan" or "red" or "hot" && m.HasProperty("_EmissionColor")){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",c*2);}
        Object.DontDestroyOnLoad(m);materials[key]=m;return m;
    }
    internal static void Tint(GameObject root)
    {
        if(root.transform.Find("Forge temporary color marker")!=null)return;
        var marker=new GameObject("Forge temporary color marker");marker.transform.SetParent(root.transform,false);
        foreach(var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var palette=renderer.materials;
            foreach(var m in palette)
            {
                if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",new Color(.9f,.42f,.16f));
                if(m.HasProperty("_Color"))m.SetColor("_Color",new Color(.9f,.42f,.16f));
            }
        }
    }
    private static GameObject Part(Transform parent,PrimitiveType type,Vector3 pos,Vector3 scale,string color)
    {
        var p=GameObject.CreatePrimitive(type);p.transform.SetParent(parent,false);p.transform.localPosition=pos;p.transform.localScale=scale;
        p.GetComponent<Renderer>().sharedMaterial=Material(color);var collider=p.GetComponent<Collider>();collider.enabled=false;Object.Destroy(collider);return p;
    }
    internal static GameObject Device(int slot,Vector3 at,Scene scene)
    {
        var root=new GameObject(slot==2 ? "Forge sentry":slot==4 ? "Forge Aegis generator":"Forge charge");SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=at;
        if(slot==2)
        {
            for(var i=0;i<3;i++)
            {
                var a=i*Mathf.PI*2/3;var leg=Part(root.transform,PrimitiveType.Cube,new Vector3(Mathf.Sin(a)*.42f,.2f,Mathf.Cos(a)*.42f),new Vector3(.2f,.3f,.85f),"dark");leg.transform.localRotation=Quaternion.Euler(0,i*120,0);
            }
            Part(root.transform,PrimitiveType.Cylinder,new(0,.55f,0),new(.55f,.4f,.55f),"orange");
            var head=Part(root.transform,PrimitiveType.Cube,new(0,1,0),new(.8f,.45f,.7f),"orange");head.name="Aim";
            Part(head.transform,PrimitiveType.Cube,new(0,0,.8f),new(.3f,.3f,1.1f),"dark");
            Part(head.transform,PrimitiveType.Sphere,new(0,0,.4f),new(.35f,.35f,.35f),"cyan");
        }
        else if(slot==4)
        {
            Part(root.transform,PrimitiveType.Cylinder,new(0,.2f,0),new(.7f,.2f,.7f),"dark");Part(root.transform,PrimitiveType.Sphere,new(0,.6f,0),new(.65f,.65f,.65f),"cyan");
        }
        else Part(root.transform,PrimitiveType.Sphere,Vector3.zero,Vector3.one*.45f,"orange");
        return root;
    }
    internal static GameObject Ring(Vector3 at,float radius,Scene scene)
    {
        var root=new GameObject("Forge field boundary");SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=at;
        var line=root.AddComponent<LineRenderer>();line.useWorldSpace=false;line.positionCount=65;line.startWidth=line.endWidth=.075f;line.sharedMaterial=Material("cyan");
        for(var i=0;i<=64;i++){var a=i*Mathf.PI*2/64;line.SetPosition(i,new Vector3(Mathf.Sin(a)*radius,.13f,Mathf.Cos(a)*radius));}return root;
    }
    internal static Material TargetFillMaterial()
    {
        if(materials.TryGetValue("target-fill",out var existing) && existing!=null)return existing;
        var shader=Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
        if(shader==null)throw new InvalidOperationException("No targeting preview shader is available.");
        var material=new Material(shader){name="Forge red targeting fill (20% opacity)",renderQueue=3000};
        var red=new Color(1,.025f,.015f,.20f);
        foreach(var name in new[]{"_BaseColor","_Color"})if(material.HasProperty(name))material.SetColor(name,red);
        foreach(var (name,value) in new[]{("_Surface",1f),("_Blend",0f),("_SrcBlend",5f),("_DstBlend",10f),("_ZWrite",0f),("_Cull",0f)})
            if(material.HasProperty(name))material.SetFloat(name,value);
        material.SetOverrideTag("RenderType","Transparent");material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        Object.DontDestroyOnLoad(material);materials["target-fill"]=material;return material;
    }
    internal static void Trace(Vector3 from,Vector3 to,Scene scene)
    {
        var root=new GameObject("Forge rivet tracer");SceneManager.MoveGameObjectToScene(root,scene);
        var line=root.AddComponent<LineRenderer>();line.positionCount=2;line.startWidth=.065f;line.endWidth=.03f;line.sharedMaterial=Material("cyan");line.SetPosition(0,from);line.SetPosition(1,to);Object.Destroy(root,.09f);
    }
    internal static GameObject Magnet(Vector3 at,Scene scene)
    {
        var root=new GameObject("Forge magnetic suction");SceneManager.MoveGameObjectToScene(root,scene);root.transform.position=at;
        for(var i=0;i<6;i++)
        {
            var arc=new GameObject("Inward magnetic arc");arc.transform.SetParent(root.transform,false);
            var line=arc.AddComponent<LineRenderer>();line.useWorldSpace=false;line.positionCount=16;
            line.startWidth=.025f;line.endWidth=.11f;line.sharedMaterial=Material("cyan");
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
        }
        MagnetState(root,0);return root;
    }
    internal static void MagnetState(GameObject root,float progress)
    {
        var lines=root.GetComponentsInChildren<LineRenderer>();var t=Math.Clamp(progress,0,1);
        for(var i=0;i<lines.Length;i++)for(var j=0;j<16;j++)
        {
            var tail=1-j/15f;var radius=Mathf.Lerp(ForgeRules.MagneticRadius,ForgeRules.MagneticStop,t);
            radius=Math.Min(ForgeRules.MagneticRadius,radius+tail*2);
            var angle=i*Mathf.PI/3+t*1.3f-tail*.35f;
            lines[i].SetPosition(j,new Vector3(Mathf.Sin(angle)*radius,.18f,Mathf.Cos(angle)*radius));
        }
    }
    private static GameObject CreateRivet(Vector3 at,Vector3 direction,Scene scene)
    {
        EnsureRivet();
        var root=new GameObject("Forge oversized piercing rivet");SceneManager.MoveGameObjectToScene(root,scene);
        root.transform.SetPositionAndRotation(at,Quaternion.LookRotation(direction));
        root.AddComponent<MeshFilter>().sharedMesh=rivetMesh;
        var renderer=root.AddComponent<MeshRenderer>();renderer.sharedMaterial=rivetMaterial;
        renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        var tail=new GameObject("Forge rivet tracer");tail.transform.SetParent(root.transform,false);
        tail.transform.localPosition=new Vector3(0,0,-.52f*ForgeRules.RivetScale);
        var trail=tail.AddComponent<TrailRenderer>();trail.emitting=false;
        var source=LocalPoolManager.Instance?.MainPool?._localPoolInfos;
        var native=source!=null && source.TryGetValue(PoolItemID.HybridShell,out var info) && info.Prefab!=null
            ? info.Prefab.GetComponentInChildren<TrailRenderer>(true):null;
        if(native!=null && native.sharedMaterial!=null)
        {
            if(rivetTrailMaterial==null)
            {
                // Use a neutral sprite shader so the native amber texture cannot tint blue trails.
                var shader=Shader.Find("Sprites/Default") ?? native.sharedMaterial.shader;
                rivetTrailMaterial=new Material(shader){name="Forge blue rivet tracer"};
                foreach(var key in new[]{"_BaseColor","_Color","_TintColor"})if(rivetTrailMaterial.HasProperty(key))rivetTrailMaterial.SetColor(key,Color.white);
                foreach(var key in new[]{"_MainTex","_BaseMap"})if(rivetTrailMaterial.HasProperty(key))rivetTrailMaterial.SetTexture(key,Texture2D.whiteTexture);
                Object.DontDestroyOnLoad(rivetTrailMaterial);
            }
            trail.sharedMaterial=rivetTrailMaterial;
            trail.widthCurve=native.widthCurve;trail.widthMultiplier=native.widthMultiplier*ForgeRules.RivetScale;
            trail.time=native.time;trail.textureMode=native.textureMode;trail.alignment=native.alignment;
            RivetTracerSource="Hybrid shell";
        }
        else
        {
            trail.sharedMaterial=Material("cyan");trail.startWidth=.145f;trail.endWidth=0;trail.time=.03f;
            RivetTracerSource="fallback";
        }
        BlueTracer(trail);
        trail.minVertexDistance=.05f;trail.numCapVertices=2;trail.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;trail.receiveShadows=false;
        trail.Clear();trail.emitting=true;
        return root;
    }
    internal static void BlueTracer(TrailRenderer trail,bool charged=false)
    {
        trail.startColor=charged ? new Color(.65f,.92f,1,1):new Color(.08f,.65f,1,1);
        trail.endColor=new Color(.025f,.25f,1,0);
    }
    private static void EnsureRivet()
    {
        Stream Resource(string file)=>typeof(ForgeArt).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.Forge."+file)
            ?? throw new InvalidDataException("Forge rivet asset missing: "+file);
        if(rivetMesh==null)
        {
            using var stream=Resource("forge-rivet.json");
            var data=JsonSerializer.Deserialize<ForgeMesh>(stream) ?? throw new InvalidDataException("Empty Forge rivet mesh.");data.Validate(0,false);
            var count=data.Vertices.Length/3;var vertices=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];
            for(var i=0;i<count;i++)
            {
                vertices[i]=new Vector3(data.Vertices[i*3],data.Vertices[i*3+1],data.Vertices[i*3+2])*ForgeRules.RivetScale;
                normals[i]=new(data.Normals[i*3],data.Normals[i*3+1],data.Normals[i*3+2]);uv[i]=new(data.Uv[i*2],data.Uv[i*2+1]);
            }
            rivetMesh=new Mesh{name="Forge machined steel rivet",vertices=vertices,normals=normals,uv=uv,triangles=data.Triangles};
            rivetMesh.RecalculateBounds();rivetMesh.RecalculateTangents();Object.DontDestroyOnLoad(rivetMesh);
        }
        if(rivetMaterial==null)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? throw new InvalidOperationException("No steel rivet shader.");
            rivetMaterial=new Material(shader){name="Forge worn steel rivet"};
            using var stream=Resource("rivet-albedo.png");using var bytes=new MemoryStream();stream.CopyTo(bytes);
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,true,false){name="Forge rivet steel albedo"};
            if(!ImageConversion.LoadImage(texture,bytes.ToArray(),false))throw new InvalidDataException("Invalid rivet texture.");
            texture.filterMode=FilterMode.Trilinear;texture.anisoLevel=4;Object.DontDestroyOnLoad(texture);
            rivetMaterial.mainTexture=texture;
            foreach(var key in new[]{"_BaseMap","_MainTex"})if(rivetMaterial.HasProperty(key))rivetMaterial.SetTexture(key,texture);
            foreach(var key in new[]{"_BaseColor","_Color"})if(rivetMaterial.HasProperty(key))rivetMaterial.SetColor(key,Color.white);
            if(rivetMaterial.HasProperty("_Metallic"))rivetMaterial.SetFloat("_Metallic",.86f);
            if(rivetMaterial.HasProperty("_Smoothness"))rivetMaterial.SetFloat("_Smoothness",.71f);
            Object.DontDestroyOnLoad(rivetMaterial);
        }
    }
}
