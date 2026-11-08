using Gatekeeper.Mods;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// A real hemispherical energy surface. The grid is the dual of an icosphere:
// mostly hexagons, with twelve pentagonal seams, so there is no UV pole pinch.
internal static class ForgeShield
{
    private static Mesh? shell,hexagons;
    private static Material? surface,grid,healing;
    private sealed class Bubble(GameObject root,MeshRenderer body,MeshRenderer lines,MeshRenderer pulse)
    {
        internal readonly GameObject Root=root;
        internal readonly MeshRenderer Body=body,Lines=lines,Pulse=pulse;
        internal readonly MaterialPropertyBlock Properties=new();
        internal readonly float Start=Time.time;
        internal float End;
    }
    private static readonly Dictionary<IntPtr,Bubble> bubbles=new();
    internal static void State(GameObject boundary,int level,float remaining)
    {
        if(bubbles.TryGetValue(boundary.Pointer,out var broken) && (broken.Root==null || broken.Body==null || broken.Lines==null || broken.Pulse==null))
        {if(broken.Root!=null)Object.Destroy(broken.Root);bubbles.Remove(boundary.Pointer);}
        if(!bubbles.TryGetValue(boundary.Pointer,out var b))
        {
            EnsureAssets();
            var root=new GameObject("Forge Aegis shield bubble");root.transform.SetParent(boundary.transform,false);
            b=new(root,Part(root,"Blue translucent membrane",shell!,surface!),Part(root,"Blue hexagonal shield grid",hexagons!,grid!),Part(root,"Green healing pulse",shell!,healing!));
            bubbles[boundary.Pointer]=b;
        }
        if(!boundary.activeSelf)boundary.SetActive(true);
        if(!b.Root.activeSelf)b.Root.SetActive(true);
        foreach(var renderer in new[]{b.Body,b.Lines,b.Pulse})
        {if(!renderer.gameObject.activeSelf)renderer.gameObject.SetActive(true);if(!renderer.enabled)renderer.enabled=true;if(renderer.forceRenderingOff)renderer.forceRenderingOff=false;}
        b.Root.transform.localScale=Vector3.one*ForgeRules.Radius(level);
        b.End=Time.time+remaining;
    }
    internal static void Render()
    {
        foreach(var pair in bubbles.ToArray())
        {
            var b=pair.Value;
            if(b.Root==null || b.Body==null || b.Lines==null || b.Pulse==null)
            {if(b.Root!=null)Object.Destroy(b.Root);bubbles.Remove(pair.Key);continue;}
            var fade=Math.Clamp((Time.time-b.Start)/.25f,0,1)*Math.Clamp((b.End-Time.time)/.35f,0,1);
            var breathe=1+.06f*Mathf.Sin((Time.time-b.Start)*2);
            var phase=(Time.time-b.Start)%1.5f/1.5f;var pulse=Mathf.Sin(phase*Mathf.PI);
            var blue=Color.Lerp(new(.035f,.36f,.9f,.055f),new(.045f,.7f,.4f,.075f),pulse*.45f);blue.a*=fade;
            var lines=Color.Lerp(new(.12f,.68f,1,.26f),new(.15f,1,.48f,.34f),pulse*.65f);lines.a*=fade*breathe;
            Tint(b,b.Body,blue);Tint(b,b.Lines,lines);
            b.Pulse.transform.localScale=Vector3.one*(.08f+.92f*phase);
            Tint(b,b.Pulse,new Color(.06f,.95f,.35f,.075f*pulse*fade));
        }
    }
    private static void Tint(Bubble b,MeshRenderer renderer,Color color)
    {
        b.Properties.SetColor("_BaseColor",color);b.Properties.SetColor("_Color",color);
        renderer.SetPropertyBlock(b.Properties);
    }
    private static MeshRenderer Part(GameObject root,string name,Mesh mesh,Material material)
    {
        var child=new GameObject(name);child.transform.SetParent(root.transform,false);
        child.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=child.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        return renderer; // No collider: allies and shots pass through this visual.
    }
    private static Material Transparent(string name,Color color,int queue)
    {
        var shader=Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
        if(shader==null)throw new InvalidOperationException("No shield shader is available.");
        var m=new Material(shader){name=name,renderQueue=queue};
        foreach(var key in new[]{"_BaseColor","_Color"})if(m.HasProperty(key))m.SetColor(key,color);
        foreach(var (key,value) in new[]{("_Surface",1f),("_Blend",0f),("_SrcBlend",5f),("_DstBlend",10f),("_ZWrite",0f),("_Cull",0f)})
            if(m.HasProperty(key))m.SetFloat(key,value);
        m.SetOverrideTag("RenderType","Transparent");m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHATEST_ON");m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        Object.DontDestroyOnLoad(m);return m;
    }
    private static void EnsureAssets()
    {
        if(surface==null)surface=Transparent("Forge blue shield membrane (5.5% opacity)",new(.035f,.36f,.9f,.055f),3000);
        if(grid==null)grid=Transparent("Forge blue hexagon energy (26% opacity)",new(.12f,.68f,1,.26f),3001);
        if(healing==null)healing=Transparent("Forge green healing wave",new(.06f,.95f,.35f,.075f),3002);
        if(shell==null)
        {
            const int slices=96,rings=32;
            var v=new List<Vector3>();var t=new List<int>();
            // Latitude rings stop short of the top; a single pole closes the dome.
            for(var r=0;r<rings;r++)for(var s=0;s<=slices;s++)
            {
                var a=s*Mathf.PI*2/slices;var latitude=r*Mathf.PI*.5f/rings;
                v.Add(new(Mathf.Cos(latitude)*Mathf.Cos(a),Mathf.Sin(latitude),Mathf.Cos(latitude)*Mathf.Sin(a)));
            }
            for(var r=0;r<rings-1;r++)for(var s=0;s<slices;s++)
            {var i=r*(slices+1)+s;var j=i+slices+1;t.AddRange(new[]{i,j,i+1,i+1,j,j+1});}
            var pole=v.Count;v.Add(Vector3.up);
            for(var s=0;s<slices;s++){var i=(rings-1)*(slices+1)+s;t.AddRange(new[]{i,pole,i+1});}
            shell=Mesh("Forge smooth shield hemisphere",v,t);
        }
        if(hexagons==null)hexagons=HexMesh();
    }
    private static Mesh Mesh(string name,List<Vector3> v,List<int> t)
    {
        var mesh=new Mesh{name=name,vertices=v.ToArray(),normals=v.Select(p=>p.normalized).ToArray(),triangles=t.ToArray()};
        mesh.RecalculateBounds();Object.DontDestroyOnLoad(mesh);return mesh;
    }
    private static Mesh HexMesh()
    {
        var g=(1+Mathf.Sqrt(5))/2;
        var points=new List<Vector3>{new(-1,g,0),new(1,g,0),new(-1,-g,0),new(1,-g,0),new(0,-1,g),new(0,1,g),new(0,-1,-g),new(0,1,-g),new(g,0,-1),new(g,0,1),new(-g,0,-1),new(-g,0,1)};
        for(var i=0;i<points.Count;i++)points[i]=points[i].normalized;
        var faces=new List<(int A,int B,int C)>{(0,11,5),(0,5,1),(0,1,7),(0,7,10),(0,10,11),(1,5,9),(5,11,4),(11,10,2),(10,7,6),(7,1,8),(3,9,4),(3,4,2),(3,2,6),(3,6,8),(3,8,9),(4,9,5),(2,4,11),(6,2,10),(8,6,7),(9,8,1)};
        static (int,int) Edge(int a,int b)=>a<b ? (a,b):(b,a);
        for(var pass=0;pass<3;pass++)
        {
            var midpoints=new Dictionary<(int,int),int>();var next=new List<(int,int,int)>();
            int Mid(int a,int b)
            {
                var key=Edge(a,b);if(midpoints.TryGetValue(key,out var n))return n;
                n=points.Count;points.Add((points[a]+points[b]).normalized);midpoints[key]=n;return n;
            }
            foreach(var (a,b,c) in faces)
            {var ab=Mid(a,b);var bc=Mid(b,c);var ca=Mid(c,a);next.AddRange(new[]{(a,ab,ca),(b,bc,ab),(c,ca,bc),(ab,bc,ca)});}
            faces=next;
        }
        var centres=faces.Select(f=>(points[f.A]+points[f.B]+points[f.C]).normalized).ToArray();
        var edges=new Dictionary<(int,int),int>();var v=new List<Vector3>();var triangles=new List<int>();
        for(var i=0;i<faces.Count;i++)
        {
            var f=faces[i];foreach(var key in new[]{Edge(f.A,f.B),Edge(f.B,f.C),Edge(f.C,f.A)})
            {
                if(!edges.TryGetValue(key,out var other)){edges[key]=i;continue;}
                var a=centres[i];var b=centres[other];
                if(a.y<=0 && b.y<=0)continue;
                // Clip at ground level, keeping the dome open beneath the floor.
                if(a.y<0)a=Vector3.Lerp(a,b,-a.y/(b.y-a.y)).normalized;
                if(b.y<0)b=Vector3.Lerp(b,a,-b.y/(a.y-b.y)).normalized;
                var across=Vector3.Cross(a,b).normalized*.0017f;var n=v.Count;
                v.AddRange(new[]{(a-across).normalized*1.001f,(a+across).normalized*1.001f,(b-across).normalized*1.001f,(b+across).normalized*1.001f});
                triangles.AddRange(new[]{n,n+2,n+1,n+2,n+3,n+1});
            }
        }
        return Mesh("Forge geodesic hexagonal shield pattern",v,triangles);
    }
}
