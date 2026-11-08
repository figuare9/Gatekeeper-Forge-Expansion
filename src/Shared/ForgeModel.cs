using System.Text.Json;
using System.Numerics;

namespace Gatekeeper.Mods;

public sealed class ForgeModel
{
    public int Version {get;set;}
    public float Height {get;set;}
    public ForgeBone[] Bones {get;set;} = Array.Empty<ForgeBone>();
    public ForgeMesh Body {get;set;} = new();
    public ForgeMesh Gun {get;set;} = new();
    public ForgeClip[] Clips {get;set;} = Array.Empty<ForgeClip>();
    public float[] Muzzle {get;set;} = Array.Empty<float>();
    public float[] RightGrip {get;set;} = Array.Empty<float>();
    public float[] LeftGrip {get;set;} = Array.Empty<float>();

    public static ForgeModel Read(Stream stream)
    {
        var model=JsonSerializer.Deserialize<ForgeModel>(stream) ?? throw new InvalidDataException("Forge model is empty.");
        model.Validate();return model;
    }
    public void Validate()
    {
        if(Version!=1 || Bones.Length is <2 or >128 || !float.IsFinite(Height) || Height<=0)
            throw new InvalidDataException("Unsupported Forge model.");
        for(var i=0;i<Bones.Length;i++)
            if(string.IsNullOrWhiteSpace(Bones[i].Name) || Bones[i].Parent < -1 || Bones[i].Parent>=i ||
                Bones[i].Rest.Length!=10 || Bones[i].Bind.Length!=16 || !Bones[i].Rest.All(float.IsFinite) || !Bones[i].Bind.All(float.IsFinite))
                throw new InvalidDataException("Forge skeleton hierarchy or bind pose is invalid.");
        Body.Validate(Bones.Length,true);Gun.Validate(Bones.Length,false);
        if(Clips.Select(c=>c.Name).Distinct().Count()!=Clips.Length || !Clips.Any(c=>c.Name=="idle"))
            throw new InvalidDataException("Forge animation set is incomplete.");
        foreach(var clip in Clips)
            if(clip.Fps<=0 || !float.IsFinite(clip.Duration) || clip.Duration<=0 || clip.Frames.Length<2 ||
                clip.Frames.Any(f=>f.Length!=Bones.Length*10 || !f.All(float.IsFinite)))
                throw new InvalidDataException("Forge animation data is invalid: "+clip.Name);
        if(Muzzle.Length!=3 || !Muzzle.All(float.IsFinite))throw new InvalidDataException("Forge muzzle socket is invalid.");
    }
}
public sealed class ForgeBone
{
    public string Name {get;set;}="";
    public int Parent {get;set;}=-1;
    public float[] Rest {get;set;}=Array.Empty<float>();
    public float[] Bind {get;set;}=Array.Empty<float>();
}
public sealed class ForgeClip
{
    public string Name {get;set;}="";
    public int Fps {get;set;}=60;
    public float Duration {get;set;}
    public bool Loop {get;set;}
    public float[][] Frames {get;set;}=Array.Empty<float[]>();
}
public sealed class ForgeMesh
{
    public float[] Vertices {get;set;}=Array.Empty<float>();
    public float[] Normals {get;set;}=Array.Empty<float>();
    public float[] Uv {get;set;}=Array.Empty<float>();
    public int[] Triangles {get;set;}=Array.Empty<int>();
    public float[] Weights {get;set;}=Array.Empty<float>();
    public int[] BoneIndices {get;set;}=Array.Empty<int>();
    public void Validate(int bones,bool skinned)
    {
        var count=Vertices.Length/3;
        if(count<3 || Vertices.Length%3!=0 || Normals.Length!=Vertices.Length || Uv.Length!=count*2 ||
            Triangles.Length%3!=0 || Triangles.Any(i=>i<0 || i>=count) || !Vertices.All(float.IsFinite) ||
            !Normals.All(float.IsFinite) || !Uv.All(float.IsFinite))throw new InvalidDataException("Forge mesh is invalid.");
        if(!skinned)return;
        if(Weights.Length!=count*4 || BoneIndices.Length!=count*4 || BoneIndices.Any(i=>i<0 || i>=bones) ||
            Weights.Any(w=>!float.IsFinite(w) || w<0 || w>1))throw new InvalidDataException("Forge skin weights are invalid.");
        for(var i=0;i<count;i++)
            if(Math.Abs(Weights[i*4]+Weights[i*4+1]+Weights[i*4+2]+Weights[i*4+3]-1)>.001f)
                throw new InvalidDataException("Forge has an unweighted or unnormalized vertex.");
    }
}

public sealed class ForgeRivetFlight
{
    public readonly int MaxTargets;
    public readonly float Range,Speed;
    public ForgeRivetFlight(int maxTargets=ForgeRules.RivetMaxTargets,float range=ForgeRules.RivetRange,float speed=ForgeRules.RivetSpeed)
    {MaxTargets=Math.Clamp(maxTargets,1,8);Range=range;Speed=speed;}
    private readonly HashSet<int> targets=new();
    public int HitCount=>targets.Count;
    public float Travelled {get;private set;}
    public bool Finished=>Travelled>=Range || targets.Count>=MaxTargets;
    public float Advance(float deltaTime)
    {
        if(!float.IsFinite(deltaTime) || deltaTime<=0 || Finished)return 0;
        var step=Math.Min(Range-Travelled,Speed*Math.Min(deltaTime,.1f));
        Travelled+=step;return step;
    }
    public bool Hit(int target)=>target>=0 && targets.Count<MaxTargets && targets.Add(target);
    public bool HasHit(int target)=>targets.Contains(target);
}

// Each turret counts volleys independently. Both barrels share one charged volley.
public sealed class ForgeVolley
{
    public int Count {get;private set;}
    public bool Charged {get;private set;}
    public bool Fire(int level,bool secondBarrel)
    {if(!secondBarrel){Count++;Charged=level>=3 && Count%3==0;}return Charged;}
}

// Short tangent-continuous bends make the seeking readable without turning it
// into an instantaneous chain lightning attack. Physics sweeps every segment.
public sealed class ForgeSeekArc
{
    public readonly Vector3 From,Control;
    public Vector3 To {get;set;}
    public readonly float Length;
    public ForgeSeekArc(Vector3 from,Vector3 control,Vector3 to)
    {From=from;Control=control;To=to;var previous=from;for(var i=1;i<=16;i++){var p=Point(i/16f);Length+=Vector3.Distance(previous,p);previous=p;}}
    public Vector3 Point(float t){t=Math.Clamp(t,0,1);var a=1-t;return From*(a*a)+Control*(2*a*t)+To*(t*t);}
    public Vector3 Tangent(float t)=>2*((Control-From)*(1-Math.Clamp(t,0,1))+(To-Control)*Math.Clamp(t,0,1));
}

public static class ForgeRigMath
{
    // A shortest swing preserves the supplied bone's own axial roll, even
    // when mirrored imported limbs use very different local coordinate frames.
    public static Quaternion PreserveLegRoll(Quaternion restRotation,Vector3 restDirection,Vector3 posedDirection)
    {
        if(restDirection.LengthSquared()<1e-12f || posedDirection.LengthSquared()<1e-12f)return restRotation;
        var from=Vector3.Normalize(restDirection);var to=Vector3.Normalize(posedDirection);
        var dot=Math.Clamp(Vector3.Dot(from,to),-1,1);Quaternion swing;
        if(dot<-.999999f)
        {
            var axis=Vector3.Cross(from,Vector3.UnitX);
            if(axis.LengthSquared()<1e-8f)axis=Vector3.Cross(from,Vector3.UnitY);
            swing=Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),MathF.PI);
        }
        else swing=Quaternion.Normalize(new Quaternion(Vector3.Cross(from,to),1+dot));
        return Quaternion.Normalize(swing*restRotation);
    }
}
