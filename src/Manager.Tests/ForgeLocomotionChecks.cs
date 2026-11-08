using System.Numerics;
using Gatekeeper.Mods;

internal static class ForgeLocomotionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var motion=new ForgeLocomotion(Vector3.UnitZ);
        motion.Face(0,.05f,Vector3.UnitX*13,-Vector3.UnitZ,720);
        check(motion.Heading.X>0 && motion.Heading.Z>0,"A free turn must approach movement smoothly and ignore the cursor.");
        for(var i=1;i<20;i++)motion.Face(i*.05f,.05f,Vector3.UnitX*13,-Vector3.UnitZ,720);
        check(Vector3.Distance(motion.Heading,Vector3.UnitX)<.0001f,"Free movement does not finish facing travel.");
        motion.Face(1,.1f,Vector3.Zero,-Vector3.UnitZ,720);
        check(Vector3.Distance(motion.Heading,Vector3.UnitX)<.0001f,"Moving the cursor while idle turns Forge outside combat.");
        motion.Fire(-Vector3.UnitX,2);
        check(Vector3.Distance(motion.Heading,-Vector3.UnitX)<.0001f && motion.IsAiming(2),"A shot does not immediately face its firing direction.");
        motion.Face(4.99f,.01f,Vector3.UnitZ*13,Vector3.UnitX,720);
        check(Vector3.Distance(motion.Heading,Vector3.UnitX)<.0001f && motion.IsAiming(4.99f),"Cursor following ended before three seconds.");
        motion.Face(5,.01f,Vector3.UnitZ*13,-Vector3.UnitX,720);
        check(!motion.IsAiming(5) && motion.Heading.X>0 && motion.Heading.Z>0,"Three-second timeout fails to resume a smooth movement turn.");
        motion.Fire(-Vector3.UnitZ,6);motion.Fire(Vector3.UnitZ,8.5f);
        check(motion.IsAiming(11.49f) && !motion.IsAiming(11.5f),"Successive shots do not refresh the aiming window.");
        motion.Fire(Vector3.Zero,12);
        check(!motion.IsAiming(12),"A rejected zero direction starts a new aiming window.");
        var paused=motion.Heading;
        motion.Face(13,0,Vector3.UnitX,Vector3.UnitX,720);
        check(motion.Heading==paused,"Paused movement changes orientation.");
        var reversal=new ForgeLocomotion(Vector3.UnitZ);
        for(var i=0;i<30;i++)reversal.Face(i/60f,1/60f,-Vector3.UnitZ*13,Vector3.UnitX,720);
        check(Vector3.Distance(reversal.Heading,-Vector3.UnitZ)<.0001f,"A 180-degree movement reversal fails to turn.");
        var a=new ForgeLocomotion(Vector3.UnitZ);var b=new ForgeLocomotion(Vector3.UnitZ);
        a.Face(0,.1f,Vector3.UnitX*13,Vector3.Zero,540);
        for(var i=0;i<12;i++)b.Face(0,.1f/12,Vector3.UnitX*13,Vector3.Zero,540);
        check(Vector3.Distance(a.Heading,b.Heading)<.0001f,"Free turning changes with rendering frame rate.");
        foreach(var fps in new[]{30,60,144})
        {
            var gait=new ForgeLocomotion(Vector3.UnitZ);
            double cycles=0;
            for(var i=0;i<fps*4;i++){gait.Gait(13,1f/fps,false,13,1.4f);cycles+=gait.CyclesPerSecond/fps;}
            check(Math.Abs(cycles-10)<.0001,"Normal gait must produce ten full cycles in four seconds at native base speed: "+fps);
            var phase=gait.Phase;
            gait.Gait(13,0,true,13,1.4f);
            check(gait.Phase==phase,"Paused gait advances a step.");
            for(var i=0;i<fps;i++)gait.Gait(18.2f,1f/fps,true,13,1.4f);
            check(gait.SprintBlend>.999f && Math.Abs(gait.CyclesPerSecond-2.5f)<.0001f,"Sprint should gain stride length instead of frantic steps.");
            gait.Gait(13,1f/fps,false,13,1.4f);
            check(gait.SprintBlend is >0 and <1,"Sprint switches clips abruptly instead of blending.");
            gait.Gait(0,1f/fps,false,13,1.4f);phase=gait.Phase;
            for(var i=0;i<fps;i++)gait.Gait(0,1f/fps,false,13,1.4f);
            check(gait.Phase==phase && gait.CyclesPerSecond==0 && gait.SprintBlend==0,"Standing still continues running or sprinting.");
        }
        var slow=new ForgeLocomotion(Vector3.UnitZ);
        slow.Gait(6.5f,.1f,false,13,1.4f);
        check(Math.Abs(slow.CyclesPerSecond-1.25f)<.0001f,"Slow movement does not reduce cadence with travel.");
        slow.Gait(26,.1f,false,13,1.4f);
        check(Math.Abs(slow.CyclesPerSecond-3.25f)<.0001f,"Large movement bonuses exceed the bounded cadence.");
        var nativeSprint=new ForgeLocomotion(Vector3.UnitZ);
        for(var i=0;i<60;i++)nativeSprint.Gait(19.5f,1/60f,true,13,1+.5f);
        check(Math.Abs(nativeSprint.CyclesPerSecond-2.5f)<.0001f,"The native additive 50% sprint bonus speeds up Forge's legs.");
        // With a 100% movement bonus, native sprint adds the same base 6.5
        // units/sec, so its ratio to ordinary travel becomes 1.25, not 1.5.
        for(var i=0;i<60;i++)nativeSprint.Gait(32.5f,1/60f,true,13,1+6.5f/26);
        check(Math.Abs(nativeSprint.CyclesPerSecond-3.25f)<.0001f,"Sprint with movement bonuses no longer matches native cadence scaling.");
        slow.Gait(float.NaN,.1f,false,13,1.4f);
        check(slow.CyclesPerSecond==0 && float.IsFinite(slow.Phase),"An invalid velocity corrupts animation time.");
    }
}
