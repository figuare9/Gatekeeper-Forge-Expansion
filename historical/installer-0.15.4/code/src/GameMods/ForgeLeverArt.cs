using System.IO.Compression;
using System.Text.Json;
using Gatekeeper.Mods;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// Two rigid draw meshes, one PBR atlas; no Animator, skinned renderer or light.
internal sealed class ForgeLeverArt
{
    private sealed class Key {public float Time{get;set;}public float Angle{get;set;}}
    private sealed class Model
    {public int Version{get;set;}public float[] Hinge{get;set;}=Array.Empty<float>();public float Duration{get;set;}public Key[] Keys{get;set;}=Array.Empty<Key>();public Dictionary<string,ForgeMesh> Meshes{get;set;}=new();}
    private static Model? model;
    private static readonly Dictionary<string,Mesh> meshes=new();
    private static readonly Dictionary<string,Texture2D> textures=new();
    private static Material? material;
    internal readonly GameObject Root;
    private readonly Transform hinge;
    private readonly Renderer[] renderers;
    private readonly MaterialPropertyBlock properties=new();
    private float pulledAt=-1,nextIntegrity;
    private static Stream Resource(string file)=>typeof(ForgeLeverArt).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.Forge.forge-lever-"+file)
        ??throw new InvalidDataException("Missing Forge lever texture: "+file);
    private static Texture2D Texture(string name,bool linear)
    {
        if(textures.TryGetValue(name,out var texture) && texture!=null)return texture;
        using var source=Resource(name+".png");using var data=new MemoryStream();source.CopyTo(data);
        texture=new Texture2D(2,2,TextureFormat.RGBA32,true,linear){name="Forge relay / "+name,hideFlags=HideFlags.DontUnloadUnusedAsset,filterMode=FilterMode.Trilinear,wrapMode=TextureWrapMode.Clamp,anisoLevel=4};
        if(!ImageConversion.LoadImage(texture,data.ToArray(),false))throw new InvalidDataException("Invalid Forge lever texture.");
        Object.DontDestroyOnLoad(texture);textures[name]=texture;return texture;
    }
    private static void Prepare()
    {
        if(model==null)
        {
            using var source=typeof(ForgeLeverArt).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.Forge.forge-lever.json.gz")??throw new InvalidDataException("Missing Forge lever geometry.");
            using var zip=new GZipStream(source,CompressionMode.Decompress);model=JsonSerializer.Deserialize<Model>(zip)??throw new InvalidDataException("Empty Forge lever.");
            if(model.Version!=1 || model.Hinge.Length!=3 || model.Keys.Length<2 || model.Meshes.Count!=2)throw new InvalidDataException("Unsupported Forge lever.");
        }
        if(material==null)
        {
            var shader=Shader.Find("Universal Render Pipeline/Lit")??throw new InvalidOperationException("Forge lever PBR shader missing.");
            material=new Material(shader){name="Forge engineering relay / 1K PBR",hideFlags=HideFlags.DontUnloadUnusedAsset};
            foreach(var key in new[]{"_BaseColor","_Color"})if(material.HasProperty(key))material.SetColor(key,Color.white);
            foreach(var key in new[]{"_BaseMap","_MainTex"})if(material.HasProperty(key))material.SetTexture(key,Texture("albedo",false));
            if(material.HasProperty("_BumpMap")){material.SetTexture("_BumpMap",Texture("normal",true));material.EnableKeyword("_NORMALMAP");}
            if(material.HasProperty("_MetallicGlossMap")){material.SetTexture("_MetallicGlossMap",Texture("metal-smooth",true));material.EnableKeyword("_METALLICSPECGLOSSMAP");}
            foreach(var key in new[]{"_Metallic","_Smoothness"})if(material.HasProperty(key))material.SetFloat(key,1);
            if(material.HasProperty("_EmissionMap"))material.SetTexture("_EmissionMap",Texture("emission",false));
            if(material.HasProperty("_EmissionColor")){material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",Color.white*2);}
            Object.DontDestroyOnLoad(material);
        }
        foreach(var pair in model.Meshes)
        {
            if(meshes.TryGetValue(pair.Key,out var old) && old!=null)continue;
            var data=pair.Value;data.Validate(0,false);var count=data.Vertices.Length/3;
            var vertices=new Vector3[count];var normals=new Vector3[count];var uv=new Vector2[count];
            for(var i=0;i<count;i++){vertices[i]=new(data.Vertices[i*3],data.Vertices[i*3+1],data.Vertices[i*3+2]);normals[i]=new(data.Normals[i*3],data.Normals[i*3+1],data.Normals[i*3+2]);uv[i]=new(data.Uv[i*2],data.Uv[i*2+1]);}
            var mesh=new Mesh{name="Forge relay / "+pair.Key,hideFlags=HideFlags.DontUnloadUnusedAsset,vertices=vertices,normals=normals,uv=uv,triangles=data.Triangles};
            mesh.RecalculateBounds();mesh.RecalculateTangents();Object.DontDestroyOnLoad(mesh);meshes[pair.Key]=mesh;
        }
    }
    internal ForgeLeverArt(Vector3 position,float yaw)
    {
        Prepare();Root=new GameObject("Forge discovery engineering relay");Root.transform.SetPositionAndRotation(position,Quaternion.Euler(0,yaw,0));
        hinge=new GameObject("Lever hinge").transform;hinge.SetParent(Root.transform,false);hinge.localPosition=new(model!.Hinge[0],model.Hinge[1],model.Hinge[2]);
        var list=new List<Renderer>();
        foreach(var name in new[]{"base","lever"})
        {
            var part=new GameObject("Forge relay "+name);part.transform.SetParent(name=="base"?Root.transform:hinge,false);
            part.AddComponent<MeshFilter>().sharedMesh=meshes[name];var renderer=part.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;list.Add(renderer);
        }
        renderers=list.ToArray();Tick();
    }
    internal void Pull(bool instant=false)
    {if(pulledAt<0)pulledAt=Time.time-(instant?model!.Duration:0);}
    internal void Tick()
    {
        if(Root==null)return;
        var age=pulledAt<0?0:Math.Max(0,Time.time-pulledAt);var keys=model!.Keys;var angle=keys[^1].Angle;
        for(var i=1;i<keys.Length;i++)if(age<=keys[i].Time){var t=Mathf.InverseLerp(keys[i-1].Time,keys[i].Time,age);angle=Mathf.Lerp(keys[i-1].Angle,keys[i].Angle,Mathf.SmoothStep(0,1,t));break;}
        hinge.localRotation=Quaternion.Euler(angle,0,0);
        if(Time.unscaledTime<nextIntegrity)return;nextIntegrity=Time.unscaledTime+1;
        Prepare();var color=pulledAt<0?Color.white*2:new Color(.1f,3,.15f);
        properties.SetColor("_EmissionColor",color);
        foreach(var renderer in renderers)if(renderer!=null){renderer.sharedMaterial=material;renderer.enabled=true;renderer.forceRenderingOff=false;renderer.SetPropertyBlock(properties);}
    }
    internal void Destroy(){if(Root!=null)Object.Destroy(Root);}
}
