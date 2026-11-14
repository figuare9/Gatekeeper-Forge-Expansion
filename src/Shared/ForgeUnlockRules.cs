namespace Gatekeeper.Mods;

public static class ForgeUnlockRules
{
    public const int Scene=201, PopupAchievementId=30002;
    public const float Reach=3.5f, HeightTolerance=2, PullSeconds=1.2f;
    // Authored west service bay, beyond the entrance junction. The model's
    // front is local -Z; 270 degrees faces east, parallel to the west wall.
    public const float RelayX=-106,RelayZ=-181,RelayYaw=270;
    public const int GroundMask=1<<8,StaticGeometryMask=(1<<0)|(1<<8)|(1<<15);
    public const float FootHalfWidth=.65f,FootHalfDepth=.54f,SupportTolerance=.06f;
    // Covers both meshes across the complete pull-and-latch animation. The
    // lowest 5 cm is the deliberate floor-contact zone, not a wall clearance.
    public const float ClearanceHalfWidth=.72f,ClearanceHalfHeight=1.25f,ClearanceHalfDepth=.84f;
    public const float ClearanceCenterHeight=1.3f,ClearanceCenterDepth=.08f;
    public static bool Supported(float center,float sample,float up)=>float.IsFinite(center) && float.IsFinite(sample) && float.IsFinite(up) && up>=.985f && Math.Abs(sample-center)<=SupportTolerance;
    public const string Requirement="Find the engineering relay on Ethereal Bridge. Approach its lever to unlock Forge for your party.";
    public static bool InLevel(bool gameplay,int scene)=>gameplay && scene==Scene;
    public static bool Eligible(bool gameplay,int scene,bool alive,float distanceSquared,float heightDifference)=>
        InLevel(gameplay,scene) && alive && float.IsFinite(distanceSquared) &&
        distanceSquared>=0 && distanceSquared<=Reach*Reach && float.IsFinite(heightDifference) && Math.Abs(heightDifference)<=HeightTolerance;
    public static bool Account(string value)=>ulong.TryParse(value,out var id) && id>0 && value==id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class ForgeUnlockData
{
    public int Version {get;set;}=1;
    public Dictionary<string,string> Accounts {get;set;}=new();
    public bool Unlocked(string account)=>ForgeUnlockRules.Account(account) && Accounts!=null && Accounts.TryGetValue(account,out var token) && SlotRules.IsRequestId(token);
    public bool Award(string account,string token)
    {
        if(!ForgeUnlockRules.Account(account) || !SlotRules.IsRequestId(token) || Unlocked(account))return false;
        (Accounts??=new())[account]=token;return true;
    }
}

public sealed class ForgeUnlockMessage
{
    public string Kind {get;set;}="";
    public string Token {get;set;}="";
    public int Scene {get;set;}
    public int Owner {get;set;}
    public int Character {get;set;}
}
