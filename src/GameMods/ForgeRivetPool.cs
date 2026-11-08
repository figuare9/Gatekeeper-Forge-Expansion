using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static partial class ForgeArt
{
    private sealed class PooledRivet(GameObject root)
    {
        internal readonly GameObject Root=root;
        internal readonly IntPtr Pointer=root.Pointer;
        internal readonly MeshRenderer Mesh=root.GetComponent<MeshRenderer>();
        internal readonly MeshFilter Filter=root.GetComponent<MeshFilter>();
        internal readonly TrailRenderer Trail=root.GetComponentInChildren<TrailRenderer>();
        internal readonly float Width=root.GetComponentInChildren<TrailRenderer>().widthMultiplier;
        internal bool Active;internal float Retire;
    }
    private static GameObject? poolRoot;
    private static readonly Dictionary<IntPtr,PooledRivet> pooledRivets=new();
    private static readonly Stack<PooledRivet> idleRivets=new();
    private static readonly List<PooledRivet> fadingRivets=new();
    internal static int RivetPoolCount=>pooledRivets.Count;
    internal static int RivetPoolActive=>pooledRivets.Values.Count(v=>v.Active);
    internal static void PrepareRivets()
    {
        EnsureRivet();
        foreach(var asset in new Object?[]{rivetMesh,rivetMaterial,rivetMaterial?.mainTexture,rivetTrailMaterial})
            if(asset!=null)asset.hideFlags|=HideFlags.DontUnloadUnusedAsset;
        EnsurePool();
        while(pooledRivets.Count<24){var entry=NewRivet();entry.Trail.emitting=false;entry.Root.SetActive(false);idleRivets.Push(entry);}
    }
    private static void EnsurePool()
    {
        if(poolRoot!=null){if(!poolRoot.activeSelf)poolRoot.SetActive(true);return;}
        poolRoot=new GameObject("Forge pooled rivets");Object.DontDestroyOnLoad(poolRoot);
        pooledRivets.Clear();idleRivets.Clear();fadingRivets.Clear();
    }
    private static PooledRivet NewRivet()
    {
        var root=CreateRivet(Vector3.zero,Vector3.forward,poolRoot!.scene);
        root.transform.SetParent(poolRoot.transform,false);
        var entry=new PooledRivet(root);pooledRivets[root.Pointer]=entry;return entry;
    }
    internal static GameObject Rivet(Vector3 at,Vector3 direction,Scene scene)
    {
        EnsureRivet();EnsurePool();PooledRivet? entry=null;
        while(idleRivets.Count>0)
        {var candidate=idleRivets.Pop();if(candidate.Root!=null && candidate.Mesh!=null && candidate.Trail!=null){entry=candidate;break;}pooledRivets.Remove(candidate.Pointer);}
        entry??=NewRivet();entry.Active=true;entry.Retire=0;
        var root=entry.Root;root.transform.SetPositionAndRotation(at,Quaternion.LookRotation(direction));root.transform.localScale=Vector3.one;
        entry.Filter.sharedMesh=rivetMesh;entry.Mesh.enabled=true;entry.Mesh.forceRenderingOff=false;entry.Mesh.sharedMaterial=rivetMaterial;
        entry.Trail.enabled=true;entry.Trail.forceRenderingOff=false;
        entry.Trail.time=.07f;entry.Trail.widthMultiplier=entry.Width;BlueTracer(entry.Trail);entry.Trail.Clear();
        root.SetActive(true);entry.Trail.emitting=true;return root;
    }
    internal static void StyleRivet(GameObject root,int slot,bool direct,bool charged)
    {
        if(!pooledRivets.TryGetValue(root.Pointer,out var entry))return;
        root.transform.localScale=Vector3.one*(slot==2 ? .5f:direct ? 1:.4f);
        var trail=entry.Trail;trail.time=slot==2 ? .085f:direct ? .07f:.10f;
        trail.widthMultiplier=entry.Width*(slot==2 ? .65f:direct ? 1:.55f)*(charged ? 1.35f:1);
        BlueTracer(trail,charged);
        entry.Mesh.sharedMaterial=charged || slot==1 && !direct ? Material("hot"):rivetMaterial;
    }
    internal static void StopRivet(GameObject root)
    {
        if(root==null)return;
        if(!pooledRivets.TryGetValue(root.Pointer,out var entry)){Object.Destroy(root);return;}
        if(!entry.Active)return;
        entry.Active=false;entry.Mesh.enabled=false;entry.Trail.emitting=false;entry.Retire=Time.time+entry.Trail.time+.02f;fadingRivets.Add(entry);
    }
    internal static void BendRivet(GameObject root,Vector3 at)
    {
        if(root==null || !pooledRivets.TryGetValue(root.Pointer,out var entry))return;
        if((root.transform.position-at).sqrMagnitude>4)entry.Trail.Clear();
        root.transform.position=at;entry.Trail.AddPosition(at);
    }
    internal static void TickRivets()
    {
        for(var i=fadingRivets.Count-1;i>=0;i--)
        {
            var entry=fadingRivets[i];if(Time.time<entry.Retire)continue;
            fadingRivets.RemoveAt(i);
            if(entry.Root==null){pooledRivets.Remove(entry.Pointer);continue;}
            entry.Trail.Clear();entry.Root.SetActive(false);
            if(idleRivets.Count<96)idleRivets.Push(entry);
            else{pooledRivets.Remove(entry.Root.Pointer);Object.Destroy(entry.Root);}
        }
    }
}
