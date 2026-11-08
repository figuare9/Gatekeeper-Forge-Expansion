using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// Three shared world-space emitters. No per-shot GameObjects, lights, physics
// smoke, shadows or Update callbacks; particle counts are bounded per client.
internal static class ForgeShotFx
{
    private static GameObject? root;
    private static ParticleSystem? smoke,flash,sparks;
    private static readonly System.Random random=new(74113);
    internal static int Bursts {get;private set;}
    internal static Vector3 LastOrigin {get;private set;}
    internal static void Prepare()
    {
        if(root!=null && smoke!=null && flash!=null && sparks!=null)
        {
            if(!root.activeSelf)root.SetActive(true);
            foreach(var system in new[]{smoke,flash,sparks})
            {if(!system.gameObject.activeSelf)system.gameObject.SetActive(true);if(!system.isPlaying)system.Play(true);}
            return;
        }
        if(root!=null)Object.Destroy(root);
        root=new GameObject("Forge shared firing effects");Object.DontDestroyOnLoad(root);
        smoke=Emitter("Pressure smoke","forge-fx-smoke.png",128,false);
        flash=Emitter("Muzzle plasma","forge-fx-flash.png",64,true);
        sparks=Emitter("Blue impact flecks","forge-fx-spark.png",192,true);
    }
    private static ParticleSystem Emitter(string name,string texture,int count,bool luminous)
    {
        var child=new GameObject(name);child.transform.SetParent(root!.transform,false);
        var system=child.AddComponent<ParticleSystem>();system.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
        var main=system.main;main.loop=true;main.playOnAwake=false;main.simulationSpace=ParticleSystemSimulationSpace.World;
        main.maxParticles=count;main.startSpeed=new ParticleSystem.MinMaxCurve(0);main.startLifetime=new ParticleSystem.MinMaxCurve(1);
        var emission=system.emission;emission.enabled=false;var shape=system.shape;shape.enabled=false;
        var over=system.colorOverLifetime;over.enabled=true;
        var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
            luminous ? new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.65f,.25f),new GradientAlphaKey(0,1)}:
            new[]{new GradientAlphaKey(.4f,0),new GradientAlphaKey(.9f,.15f),new GradientAlphaKey(.5f,.55f),new GradientAlphaKey(0,1)});
        over.color=new ParticleSystem.MinMaxGradient{m_Mode=ParticleSystemGradientMode.Gradient,m_GradientMax=gradient};
        var size=system.sizeOverLifetime;size.enabled=true;
        size.size=new ParticleSystem.MinMaxCurve{m_Mode=ParticleSystemCurveMode.Curve,m_CurveMultiplier=1,m_CurveMax=AnimationCurve.Linear(0,luminous ? 1:.4f,1,luminous ? .25f:1.65f)};
        var renderer=child.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
        var shader=Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? throw new InvalidOperationException("Forge particle shader is missing.");
        var material=new Material(shader){name="Forge "+name,hideFlags=HideFlags.DontUnloadUnusedAsset};
        foreach(var key in new[]{"_BaseColor","_Color"})if(material.HasProperty(key))material.SetColor(key,Color.white);
        foreach(var (key,value) in new[]{("_Surface",1f),("_Blend",0f),("_SrcBlend",5f),("_DstBlend",luminous ? 1f:10f),("_ZWrite",0f),("_Cull",0f)})
            if(material.HasProperty(key))material.SetFloat(key,value);
        material.SetOverrideTag("RenderType","Transparent");material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");material.DisableKeyword("_ALPHAPREMULTIPLY_ON");material.renderQueue=3000;
        using var resource=typeof(ForgeShotFx).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.Forge."+texture)
            ?? throw new InvalidDataException("Missing Forge effects texture: "+texture);
        using var bytes=new MemoryStream();resource.CopyTo(bytes);
        var map=new Texture2D(2,2,TextureFormat.RGBA32,false){name=texture,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.DontUnloadUnusedAsset};
        if(!ImageConversion.LoadImage(map,bytes.ToArray(),false))throw new InvalidDataException("Invalid Forge particle texture.");
        foreach(var key in new[]{"_BaseMap","_MainTex"})if(material.HasProperty(key))material.SetTexture(key,map);
        Object.DontDestroyOnLoad(map);Object.DontDestroyOnLoad(material);renderer.sharedMaterial=material;
        system.Play(true);return system;
    }
    private static float R(float a,float b)=>a+(float)random.NextDouble()*(b-a);
    private static Vector3 Scatter(float scale)=>new Vector3(R(-1,1),R(-1,1),R(-1,1))*scale;
    private static void Emit(ParticleSystem system,Vector3 at,Vector3 velocity,float size,float life,Color color)
    {
        var particle=new ParticleSystem.Particle{position=at,velocity=velocity,startSize=size,startLifetime=life,remainingLifetime=life,
            startColor=(Color32)color,m_Rotation=new Vector3(0,0,R(0,Mathf.PI*2)),m_AngularVelocity=new Vector3(0,0,R(-.6f,.6f)),randomSeed=(uint)random.Next(1,int.MaxValue)};
        // Explicit overrides avoid the legacy Emit(Particle) path's inherited
        // spawn settings on IL2CPP. All other simulation remains native.
        system.Emit(new ParticleSystem.EmitParams{m_Particle=particle,m_PositionSet=true,m_VelocitySet=true,
            m_StartSizeSet=true,m_StartColorSet=true,m_StartLifetimeSet=true,m_RandomSeedSet=true,
            m_RotationSet=true,m_AngularVelocitySet=true},1);
    }
    internal static void Fire(Vector3 at,Vector3 direction,bool charged,bool attachedMuzzle=false)
    {
        Prepare();Bursts++;LastOrigin=at;
        var blue=charged ? new Color(.8f,.97f,1,1):new Color(.22f,.78f,1,1);
        // Forge's own flash is attached to the barrel socket, so it follows a
        // strafing character throughout recoil. World flashes remain for sentries.
        if(!attachedMuzzle)
        {
            Emit(flash!,at+direction*.14f,direction*.9f,charged ? 1.05f:.8f,.075f,blue);
            Emit(flash!,at+direction*.28f,direction*3,.26f,.035f,Color.white);
        }
        for(var i=0;i<(charged ? 5:3);i++)
            Emit(smoke!,at+direction*R(.12f,.32f)+Scatter(.035f),direction*R(.35f,1.0f)+Vector3.up*R(.4f,.9f)+Scatter(.18f),R(.6f,.9f),R(.55f,.8f),new Color(.65f,.76f,.85f,.8f));
        for(var i=0;i<(charged ? 5:3);i++)
            Emit(sparks!,at,direction*R(2,5)+Scatter(1.6f),R(.06f,.11f),R(.08f,.16f),blue);
        // A cooler vent puff lingers briefly behind the hot flash.
        if(!attachedMuzzle)Emit(smoke!,at-direction*.2f,Vector3.up*.65f-direction*.35f,.45f,.65f,new Color(.38f,.6f,.75f,.5f));
    }
    internal static void Impact(Vector3 at,Vector3 direction,bool charged)
    {
        Prepare();var blue=charged ? new Color(.78f,.96f,1):new Color(.12f,.7f,1);
        Emit(flash!,at,Vector3.zero,charged ? .6f:.4f,.065f,blue);
        for(var i=0;i<(charged ? 5:3);i++)Emit(sparks!,at,Scatter(3)-direction*.8f,R(.065f,.12f),R(.12f,.23f),blue);
    }
    internal static void Clear()
    {foreach(var system in new[]{smoke,flash,sparks})if(system!=null)system.Clear(true);}
}
