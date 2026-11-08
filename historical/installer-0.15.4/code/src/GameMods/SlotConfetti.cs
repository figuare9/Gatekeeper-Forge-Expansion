using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// One reusable emitter, at most 160 paper flecks. Native simulation owns their
// movement; no physics bodies, lights, per-fleck objects or managed frame loop.
internal static class SlotConfetti
{
    private static ParticleSystem? emitter;
    private static Material? material;
    private static readonly System.Random random=new();
    internal static int Bursts {get;private set;}
    private static float R(float min,float max)=>min+(float)random.NextDouble()*(max-min);
    internal static void Prepare()
    {
        if(emitter!=null)return;
        var origin=WorldSlots.CelebrationOrigin;if(origin==null)return;
        var root=new GameObject("Prism Jackpot confetti");root.transform.SetParent(origin,false);
        emitter=root.AddComponent<ParticleSystem>();emitter.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=emitter.main;main.loop=true;main.playOnAwake=false;main.maxParticles=160;
        main.simulationSpace=ParticleSystemSimulationSpace.World;main.gravityModifier=new ParticleSystem.MinMaxCurve(.65f);
        var emission=emitter.emission;emission.enabled=false;var shape=emitter.shape;shape.enabled=false;
        var fade=emitter.colorOverLifetime;fade.enabled=true;
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,.65f),new GradientAlphaKey(0,1)});
        fade.color=new ParticleSystem.MinMaxGradient{m_Mode=ParticleSystemGradientMode.Gradient,m_GradientMax=gradient};
        var renderer=root.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        if(material==null)
        {
            var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit")??throw new InvalidOperationException("Confetti shader unavailable.");
            material=new Material(shader){name="Prism Jackpot paper",renderQueue=3000,hideFlags=HideFlags.DontUnloadUnusedAsset};
            foreach(var (key,value) in new[]{("_Surface",1f),("_Blend",0f),("_SrcBlend",5f),("_DstBlend",10f),("_ZWrite",0f),("_Cull",0f)})
                if(material.HasProperty(key))material.SetFloat(key,value);
            foreach(var key in new[]{"_BaseColor","_Color"})if(material.HasProperty(key))material.SetColor(key,Color.white);
            material.SetOverrideTag("RenderType","Transparent");material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");Object.DontDestroyOnLoad(material);
        }
        renderer.sharedMaterial=material;emitter.Play(true);
    }
    internal static void Burst()
    {
        try
        {
            Prepare();if(emitter==null)return;Bursts++;
            var origin=WorldSlots.CelebrationOrigin!;
            var colors=new[]{new Color(1,.65f,.08f),new Color(.15f,.85f,1),new Color(1,.25f,.12f),new Color(.9f,.95f,1)};
            for(var i=0;i<120;i++)
            {
                var side=i%2==0?-1:1;
                var particle=new ParticleSystem.Particle
                {
                    position=origin.TransformPoint(new Vector3(side*.55f,2.1f,-.15f)),
                    velocity=origin.TransformDirection(new Vector3(side*R(.8f,3),R(3.5f,6.5f),R(-1.5f,1.5f))),
                    startSize3D=new Vector3(R(.06f,.10f),R(.10f,.18f),.02f),
                    startLifetime=R(1.5f,2.4f),startColor=(Color32)colors[i%colors.Length],
                    m_Rotation=new Vector3(R(0,6),R(0,6),R(0,6)),m_AngularVelocity=new Vector3(R(-6,6),R(-6,6),R(-6,6)),randomSeed=(uint)random.Next(1,int.MaxValue)
                };
                emitter.Emit(new ParticleSystem.EmitParams{m_Particle=particle,m_PositionSet=true,m_VelocitySet=true,m_StartSizeSet=true,
                    m_StartColorSet=true,m_StartLifetimeSet=true,m_RotationSet=true,m_AngularVelocitySet=true,m_RandomSeedSet=true},1);
            }
        }
        catch(Exception e){Plugin.Logger.LogWarning("Jackpot confetti: "+e.Message);}
    }
}
