using System.Numerics;

namespace Gatekeeper.Mods;

// Render state only: native movement, input and combat speed stay authoritative.
public sealed class ForgeLocomotion
{
    public const float AimSeconds=3;
    public const float NativeCycleSeconds=.4f;
    public Vector3 Heading {get;private set;}
    public float AimUntil {get;private set;}=float.NegativeInfinity;
    public float Phase {get;private set;}
    public float SprintBlend {get;private set;}
    public float CyclesPerSecond {get;private set;}

    public ForgeLocomotion(Vector3 initialHeading)
    {Heading=Planar(initialHeading,out var heading) ? heading:Vector3.UnitZ;}
    public bool IsAiming(float time)=>time<AimUntil;
    public void Fire(Vector3 direction,float time)
    {
        if(!float.IsFinite(time) || !Planar(direction,out var heading))return;
        Heading=heading;AimUntil=time+AimSeconds;
    }
    public void Face(float time,float deltaTime,Vector3 velocity,Vector3 aim,float turnDegreesPerSecond)
    {
        if(!float.IsFinite(deltaTime) || deltaTime<=0)return;
        if(IsAiming(time))
        {
            if(Planar(aim,out var heading))Heading=heading;
            return;
        }
        if(!Planar(velocity,out var target) || velocity.LengthSquared()<=.04f)return;
        // A bounded yaw turn also handles a full reversal without the unstable
        // interpolation through a zero-length direction vector.
        var angle=MathF.Atan2(Heading.X,Heading.Z);
        var goal=MathF.Atan2(target.X,target.Z);
        var difference=MathF.Atan2(MathF.Sin(goal-angle),MathF.Cos(goal-angle));
        var rate=float.IsFinite(turnDegreesPerSecond) && turnDegreesPerSecond>0 ? turnDegreesPerSecond:720;
        angle+=Math.Clamp(difference,-rate*MathF.PI/180*deltaTime,rate*MathF.PI/180*deltaTime);
        Heading=new(MathF.Sin(angle),0,MathF.Cos(angle));
    }
    public void Gait(float speed,float deltaTime,bool sprint,float baseMoveSpeed,float sprintMultiplier)
    {
        if(!float.IsFinite(deltaTime) || deltaTime<=0)return;
        if(!float.IsFinite(speed) || speed<0 || speed>60)speed=0;
        if(!float.IsFinite(baseMoveSpeed) || baseMoveSpeed<=0)baseMoveSpeed=13;
        if(!float.IsFinite(sprintMultiplier) || sprintMultiplier<1)sprintMultiplier=1.4f;
        var target=sprint && speed>.2f ? 1f:0f;
        SprintBlend+=Math.Clamp(target-SprintBlend,-deltaTime/.12f,deltaTime/.18f);
        // Tech Hunter's 0.4s cycle is controlled by Move Speed, whose native
        // update compresses bonuses: 1 + (FinalMoveSpeed/BaseMoveSpeed-1)*.3.
        // Sprint gains distance through its longer stride, not faster feet.
        var reference=baseMoveSpeed*(1+(sprintMultiplier-1)*SprintBlend);
        var ratio=speed/reference;
        var playback=ratio<=1 ? ratio:1+(ratio-1)*.3f;
        CyclesPerSecond=speed>.2f ? Math.Min(3.25f,playback/NativeCycleSeconds):0;
        Phase=(Phase+CyclesPerSecond*deltaTime)%1;
    }
    private static bool Planar(Vector3 value,out Vector3 normalized)
    {
        value.Y=0;
        if(!float.IsFinite(value.X) || !float.IsFinite(value.Z) || value.LengthSquared()<.000001f)
        {normalized=Vector3.Zero;return false;}
        normalized=Vector3.Normalize(value);return true;
    }
}
