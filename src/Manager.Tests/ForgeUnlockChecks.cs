using Gatekeeper.Mods;
using Gatekeeper.ModManager;
using System.IO.Compression;
using System.Text.Json;

internal static class ForgeUnlockChecks
{
    internal static void Run(Action<bool,string> check,string root)
    {
        check(ForgeUnlockRules.Eligible(true,201,6,true,12.25f,2),"Stage 7 at the approach boundary did not qualify.");
        check(!ForgeUnlockRules.Eligible(true,201,5,true,0,0) && !ForgeUnlockRules.Eligible(true,101,6,true,0,0),"An early/wrong map unlocked Forge.");
        check(!ForgeUnlockRules.Eligible(false,201,6,true,0,0) && !ForgeUnlockRules.Eligible(true,201,6,false,0,0),"A menu/dead player unlocked Forge.");
        check(!ForgeUnlockRules.Eligible(true,201,6,true,12.26f,0) && !ForgeUnlockRules.Eligible(true,201,6,true,0,2.01f),"The lever unlocked across its distance/height bounds.");
        check(!ForgeUnlockRules.Eligible(true,201,6,true,float.NaN,0) && !ForgeUnlockRules.Eligible(true,201,6,true,-1,0),"Malformed distance qualified for the lever.");
        check(MathF.Sqrt(MathF.Pow(ForgeUnlockRules.RelayX+4.87f,2)+MathF.Pow(ForgeUnlockRules.RelayZ+285.03f,2))>100 && ForgeUnlockRules.RelayYaw%90==0,"The discovery socket returned to the entrance or lost architectural alignment.");
        check(ForgeUnlockRules.Supported(0,.04f,1) && !ForgeUnlockRules.Supported(0,.2f,1) && !ForgeUnlockRules.Supported(0,0,.9f) && !ForgeUnlockRules.Supported(float.NaN,0,1),"Relay support accepted a step, steep surface or invalid height.");
        var data=new ForgeUnlockData();const string account="76561198000000001",other="76561198000000002";var proof=Guid.NewGuid().ToString("N");
        check(!data.Unlocked(account) && data.Award(account,proof) && data.Unlocked(account) && !data.Unlocked(other),"Forge unlock was not scoped to the earning account.");
        check(!data.Award(account,Guid.NewGuid().ToString("N")) && !data.Award(other,"bad"),"A duplicate/malformed proof changed unlock progress.");
        check(!data.Award("0",proof) && !data.Award("anonymous",proof) && !data.Award("076561198000000001",proof),"Anonymous or noncanonical account gained the unlock.");
        var restored=JsonSerializer.Deserialize<ForgeUnlockData>(JsonSerializer.Serialize(data))!;
        check(restored.Unlocked(account) && !restored.Unlocked(other),"Permanent Forge unlock did not survive serialization.");
        using(var fixture=new Fixture())
        {
            var path=Path.Combine(fixture.Game,"BepInEx/config/local.gatekeeper.custommods.forge-progress.json");Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,JsonSerializer.Serialize(data));
            var backup=fixture.Service.BackupSaves()!;
            check(File.ReadAllText(Path.ChangeExtension(backup,"forge-progress.json"))==File.ReadAllText(path),"Installer backup omitted permanent Forge discovery progress.");
        }
        using var source=File.OpenRead(Path.Combine(root,"src/GameMods/Assets/Forge/forge-lever.json.gz"));using var zip=new GZipStream(source,CompressionMode.Decompress);using var model=JsonDocument.Parse(zip);
        var json=model.RootElement;var meshCount=0;var triangles=0;var normalsGood=true;var windingGood=true;
        foreach(var mesh in json.GetProperty("Meshes").EnumerateObject())
        {
            meshCount++;var m=mesh.Value;var vertices=m.GetProperty("Vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();var normals=m.GetProperty("Normals").EnumerateArray().Select(x=>x.GetSingle()).ToArray();var ids=m.GetProperty("Triangles").EnumerateArray().Select(x=>x.GetInt32()).ToArray();triangles+=ids.Length/3;
            for(var i=0;i<normals.Length;i+=3){var n=new System.Numerics.Vector3(normals[i],normals[i+1],normals[i+2]);normalsGood&=float.IsFinite(n.Length()) && Math.Abs(n.Length()-1)<.01;}
            System.Numerics.Vector3 V(int i)=>new(vertices[i*3],vertices[i*3+1],vertices[i*3+2]);
            System.Numerics.Vector3 N(int i)=>new(normals[i*3],normals[i*3+1],normals[i*3+2]);
            for(var i=0;i<ids.Length;i+=3){var a=ids[i];var b=ids[i+1];var c=ids[i+2];var face=System.Numerics.Vector3.Cross(V(b)-V(a),V(c)-V(a));windingGood&=face.Length()>1e-10 && System.Numerics.Vector3.Dot(face,N(a)+N(b)+N(c))>=-1e-7;}
        }
        check(meshCount==2 && triangles<15000 && triangles>1000,"Lever mesh budget/hierarchy is incorrect.");
        check(normalsGood && windingGood,"Lever normals, winding or nondegenerate topology is incorrect.");
        var keys=json.GetProperty("Keys").EnumerateArray().ToArray();
        check(json.GetProperty("Duration").GetSingle()==1.2f && keys[0].GetProperty("Angle").GetSingle()==32 && keys[^1].GetProperty("Angle").GetSingle()==-32,"Lever pull animation has incorrect endpoint poses.");
        var hinge=json.GetProperty("Hinge").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var bounded=true;var grounded=true;
        foreach(var mesh in json.GetProperty("Meshes").EnumerateObject())
        {
            var vs=mesh.Value.GetProperty("Vertices").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
            for(var angle=-32;angle<=38;angle++)for(var i=0;i<vs.Length;i+=3)
            {
                var x=vs[i];var y=vs[i+1];var z=vs[i+2];
                if(mesh.Name=="lever")
                {var a=angle*MathF.PI/180;var nextY=MathF.Cos(a)*y-MathF.Sin(a)*z+hinge[1];z=MathF.Sin(a)*y+MathF.Cos(a)*z+hinge[2];y=nextY;x+=hinge[0];}
                bounded&=Math.Abs(x)<=ForgeUnlockRules.ClearanceHalfWidth && Math.Abs(z-ForgeUnlockRules.ClearanceCenterDepth)<=ForgeUnlockRules.ClearanceHalfDepth && y<=ForgeUnlockRules.ClearanceCenterHeight+ForgeUnlockRules.ClearanceHalfHeight;
                grounded&=y>=-.0001f;
            }
        }
        check(bounded && grounded,"Relay clearance volume does not contain the actual model across its full lever swing.");
    }
}
