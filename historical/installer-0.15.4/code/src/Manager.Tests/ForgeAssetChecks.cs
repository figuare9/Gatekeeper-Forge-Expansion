using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using Gatekeeper.Mods;

internal static class ForgeAssetChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> fails)
    {
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../GameMods/Assets/Forge/forge-character.json.gz"));
        using var file=File.OpenRead(path);using var gzip=new GZipStream(file,CompressionMode.Decompress);
        var model=ForgeModel.Read(gzip);
        check(model.Bones.Length==70 && model.Clips.Length==14,"The actual Forge asset lost bones or animation clips.");
        var y=model.Body.Vertices.Where((_,i)=>i%3==1).ToArray();
        check(y.Min()>-.01f && Math.Abs(y.Max()-4)<.02f,"Forge's game coordinates are upside down or scaled incorrectly.");
        Vector3 V(float[] a,int i=0)=>new(a[i],a[i+1],a[i+2]);
        Matrix4x4 Local(float[] a,int i=0)=>Matrix4x4.CreateScale(V(a,i+7))*Matrix4x4.CreateFromQuaternion(new(a[i+3],a[i+4],a[i+5],a[i+6]))*Matrix4x4.CreateTranslation(V(a,i));
        Matrix4x4[] Global(float[]? frame=null)
        {
            var result=new Matrix4x4[model.Bones.Length];
            for(var i=0;i<result.Length;i++)result[i]=Local(frame ?? model.Bones[i].Rest,frame==null ? 0:i*10)*(model.Bones[i].Parent>=0 ? result[model.Bones[i].Parent]:Matrix4x4.Identity);
            return result;
        }
        var rest=Global();var skin=new Matrix4x4[rest.Length];
        for(var i=0;i<skin.Length;i++)
        {
            var b=model.Bones[i].Bind;
            skin[i]=Matrix4x4.Transpose(new(b[0],b[1],b[2],b[3],b[4],b[5],b[6],b[7],b[8],b[9],b[10],b[11],b[12],b[13],b[14],b[15]))*rest[i];
        }
        var maxError=0f;
        for(var i=0;i<model.Body.Vertices.Length/3;i++)
        {
            var vertex=V(model.Body.Vertices,i*3);var deformed=Vector3.Zero;
            for(var j=0;j<4;j++)deformed+=Vector3.Transform(vertex,skin[model.Body.BoneIndices[i*4+j]])*model.Body.Weights[i*4+j];
            maxError=Math.Max(maxError,Vector3.Distance(vertex,deformed));
        }
        check(maxError<.0001f,"Forge's bind matrices deform the rest mesh: "+maxError);
        var right=Array.FindIndex(model.Bones,b=>b.Name=="Right_Hand");var left=Array.FindIndex(model.Bones,b=>b.Name=="Left_Hand");
        var weapon=Array.FindIndex(model.Bones,b=>b.Name=="Weapon");
        var chest=Array.FindIndex(model.Bones,b=>b.Name=="UpperChest");
        var canisterVertices=Enumerable.Range(0,model.Body.Vertices.Length/3).Where(i=>
        {
            var p=V(model.Body.Vertices,i*3);
            return Math.Abs(p.X) is >.30f and <.58f && p.Y is >2.75f and <3.50f && p.Z<-.32f;
        }).ToArray();
        check(canisterVertices.Length>500 && canisterVertices.All(i=>
            Enumerable.Range(0,4).All(j=>model.Body.Weights[i*4+j]<.0001f || model.Body.BoneIndices[i*4+j]==chest)),
            "A rigid Forge canister is still weighted to moving arms or multiple torso bones.");
        foreach(var side in new[]{"Right","Left"})
        {
            var knee=Array.FindIndex(model.Bones,b=>b.Name==side+"_KneeArmor");
            var shin=Array.FindIndex(model.Bones,b=>b.Name==side+"_LowerLeg");
            check(knee>=0 && Enumerable.Range(0,model.Body.Vertices.Length/3).Count(i=>
                Enumerable.Range(0,4).Any(j=>model.Body.BoneIndices[i*4+j]==knee && model.Body.Weights[i*4+j]>.999f))>250,
                "Forge's hard kneepad lost its independent rigid attachment: "+side);
            check(model.Clips.All(c=>c.Frames.All(f=>Vector3.Distance(Global(f)[knee].Translation,Global(f)[shin].Translation)<.0001f)),
                "Forge's kneepad separates from its knee pivot: "+side);
        }
        // Joint centres lie inside the glove, so a small clearance from the gun
        // is expected. This also checks the fingers, not just the wrist targets.
        var neutral=Global(model.Clips.Single(c=>c.Name=="idle").Frames[0]);
        // Standing at the unchanged bind hip/ankle positions must reproduce
        // the original lower body, not merely preserve rigid armour lengths.
        var neutralSkin=new Matrix4x4[rest.Length];
        for(var i=0;i<rest.Length;i++)
        {
            Matrix4x4.Invert(rest[i],out var inverseRest);
            neutralSkin[i]=inverseRest*neutral[i];
        }
        var lowerBodyError=0f;
        for(var i=0;i<model.Body.Vertices.Length/3;i++)
        {
            var vertex=V(model.Body.Vertices,i*3);if(vertex.Y>2.02f)continue;
            var deformed=Vector3.Zero;
            for(var j=0;j<4;j++)deformed+=Vector3.Transform(vertex,neutralSkin[model.Body.BoneIndices[i*4+j]])*model.Body.Weights[i*4+j];
            lowerBodyError=Math.Max(lowerBodyError,Vector3.Distance(vertex,deformed));
        }
        check(lowerBodyError<.0001f,"Forge's neutral legs no longer match the original lower body: "+lowerBodyError);
        float LegTwist(Matrix4x4[] pose,string side,string part)
        {
            var bone=Array.FindIndex(model.Bones,b=>b.Name==side+"_"+part);
            var end=Array.FindIndex(model.Bones,b=>b.Name==side+"_"+(part=="UpperLeg" ? "LowerLeg":"Foot"));
            var from=Vector3.Normalize(rest[end].Translation-rest[bone].Translation);
            var to=Vector3.Normalize(pose[end].Translation-pose[bone].Translation);
            var cross=Vector3.Cross(from,to);var dot=Math.Clamp(Vector3.Dot(from,to),-1,1);
            if(dot<-.9999f)return MathF.PI;
            var swing=Quaternion.Normalize(new Quaternion(cross,1+dot));
            Matrix4x4.Decompose(rest[bone],out _,out var restRotation,out _);
            Matrix4x4.Decompose(pose[bone],out _,out var poseRotation,out _);
            var residual=Quaternion.Inverse(swing)*poseRotation*Quaternion.Inverse(restRotation);
            return 2*MathF.Atan2(Math.Abs(Vector3.Dot(new(residual.X,residual.Y,residual.Z),from)),Math.Abs(residual.W));
        }
        float[] MixFrames(float[] a,float[] b,float t)
        {
            var output=new float[a.Length];
            for(var i=0;i<a.Length;i+=10)
            {
                var p=Vector3.Lerp(V(a,i),V(b,i),t);var s=Vector3.Lerp(V(a,i+7),V(b,i+7),t);
                var q=Quaternion.Slerp(new(a[i+3],a[i+4],a[i+5],a[i+6]),new(b[i+3],b[i+4],b[i+5],b[i+6]),t);
                new[]{p.X,p.Y,p.Z,q.X,q.Y,q.Z,q.W,s.X,s.Y,s.Z}.CopyTo(output,i);
            }
            return output;
        }
        var rawMixedTwist=0f;var correctedMixedTwist=0f;var plantedFootError=0f;
        foreach(var direction in new[]{"run_forward","run_back","run_left","run_right","sprint"})
        foreach(var phase in new[]{0f,.25f,.5f,.75f})foreach(var blend in new[]{.25f,.5f,.75f})
        {
            var clip=model.Clips.Single(c=>c.Name==direction);var sideways=model.Clips.Single(c=>c.Name=="run_left");
            var sample=MixFrames(model.Clips.Single(c=>c.Name=="idle").Frames[73],MixFrames(clip.Frames[(int)(phase*(clip.Frames.Length-1))],sideways.Frames[(int)(phase*24)],.35f),blend);
            var pose=Global(sample);
            Quaternion Rotation(Matrix4x4 m){Matrix4x4.Decompose(m,out _,out var q,out _);return q;}
            void Rotate(int bone,Quaternion q)
            {
                Matrix4x4.Decompose(pose[bone],out var scale,out _,out var position);
                Matrix4x4.Invert(pose[bone],out var inverse);
                var delta=inverse*(Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation(position));
                for(var i=0;i<pose.Length;i++)
                    for(var ancestor=i;ancestor>=0;ancestor=model.Bones[ancestor].Parent)
                        if(ancestor==bone){pose[i]*=delta;break;}
            }
            foreach(var side in new[]{"Right","Left"})
            {
                int Index(string part)=>Array.FindIndex(model.Bones,b=>b.Name==side+"_"+part);
                var thigh=Index("UpperLeg");var shin=Index("LowerLeg");var foot=Index("Foot");
                var hipPosition=pose[thigh].Translation;var kneePosition=pose[shin].Translation;var anklePosition=pose[foot].Translation;var footRotation=Rotation(pose[foot]);
                rawMixedTwist=Math.Max(rawMixedTwist,Math.Max(LegTwist(pose,side,"UpperLeg"),LegTwist(pose,side,"LowerLeg")));
                Rotate(thigh,ForgeRigMath.PreserveLegRoll(Rotation(rest[thigh]),rest[shin].Translation-rest[thigh].Translation,kneePosition-hipPosition));
                Rotate(shin,ForgeRigMath.PreserveLegRoll(Rotation(rest[shin]),rest[foot].Translation-rest[shin].Translation,anklePosition-kneePosition));
                Rotate(foot,footRotation);
                plantedFootError=Math.Max(plantedFootError,Vector3.Distance(pose[foot].Translation,anklePosition));
                correctedMixedTwist=Math.Max(correctedMixedTwist,Math.Max(LegTwist(pose,side,"UpperLeg"),LegTwist(pose,side,"LowerLeg")));
            }
        }
        check(rawMixedTwist>.05f,"The mixed-locomotion regression cases no longer exercise leg-roll drift.");
        check(correctedMixedTwist<.001f,"Forge's leg-roll correction fails after animation blending.");
        check(plantedFootError<.0001f,"Forge's leg-roll correction moves a planted ankle.");
        foreach(var side in new[]{"Right","Left"})
        {
            var foot=Array.FindIndex(model.Bones,b=>b.Name==side+"_Foot");
            float Stride(string name)
            {
                var positions=model.Clips.Single(c=>c.Name==name).Frames.Select(f=>Global(f)[foot].Translation.Z).ToArray();
                return positions.Max()-positions.Min();
            }
            check(Stride("sprint")>Stride("run_forward")*1.3f,"Sprint lost its longer foot travel: "+side);
        }
        Matrix4x4.Invert(neutral[weapon],out var inverseWeapon);
        foreach(var side in new[]{"Right","Left"})foreach(var digit in new[]{"Index","Middle","Ring","Pinky","Thumb"})
        {
            var tip=Array.FindIndex(model.Bones,b=>b.Name==side+"_"+digit+"DistalEnd");
            var point=Vector3.Transform(neutral[tip].Translation,inverseWeapon);
            var distance=Enumerable.Range(0,model.Gun.Vertices.Length/3).Min(i=>Vector3.Distance(point,V(model.Gun.Vertices,i*3)));
            check(distance<.09f,"Forge finger floats away from the gun: "+side+" "+digit+" "+distance);
        }
        foreach(var clip in model.Clips)
        {
            check(Math.Abs((clip.Frames.Length-1)/(float)clip.Fps-clip.Duration)<.0001f,"Forge clip timing changed: "+clip.Name);
            if(clip.Loop)check(clip.Frames[0].SequenceEqual(clip.Frames[^1]),"A Forge locomotion loop has a visible seam: "+clip.Name);
            var maxGrip=0f;var maxTwist=0f;var maxReach=0f;var valid=true;
            foreach(var frame in clip.Frames)
            {
                for(var b=0;b<model.Bones.Length;b++)
                {
                    var i=b*10;var norm=frame[i+3]*frame[i+3]+frame[i+4]*frame[i+4]+frame[i+5]*frame[i+5]+frame[i+6]*frame[i+6];
                    valid &= Math.Abs(norm-1)<.0001f && frame[i+7]>.0001f && frame[i+8]>.0001f && frame[i+9]>.0001f;
                }
                var global=Global(frame);
                foreach(var side in new[]{"Right","Left"})foreach(var part in new[]{"UpperLeg","LowerLeg"})
                    maxTwist=Math.Max(maxTwist,LegTwist(global,side,part));
                if(clip.Name is "idle" or "run_forward" or "run_back" or "run_left" or "run_right" or "sprint" or "fire")
                {
                    maxGrip=Math.Max(maxGrip,Vector3.Distance(global[right].Translation,Vector3.Transform(V(model.RightGrip),global[weapon])));
                    maxGrip=Math.Max(maxGrip,Vector3.Distance(global[left].Translation,Vector3.Transform(V(model.LeftGrip),global[weapon])));
                    foreach(var hand in new[]{right,left})
                    {
                        var elbow=model.Bones[hand].Parent;var shoulder=model.Bones[elbow].Parent;
                        var upperLength=Vector3.Distance(global[shoulder].Translation,global[elbow].Translation);
                        var lowerLength=Vector3.Distance(global[elbow].Translation,global[hand].Translation);
                        maxReach=Math.Max(maxReach,Vector3.Distance(global[shoulder].Translation,global[hand].Translation)/(upperLength+lowerLength));
                    }
                }
            }
            check(valid,"Forge has invalid rotation/scale samples: "+clip.Name);
            check(maxTwist<.001f,"Forge's thigh or shin twists around its length in "+clip.Name+": "+(maxTwist*180/MathF.PI)+" degrees");
            check(maxGrip<.001f,"Forge's two-handed grip separates during "+clip.Name+": "+maxGrip);
            if(clip.Name is "idle" or "run_forward" or "run_back" or "run_left" or "run_right" or "sprint" or "fire")
                check(maxReach<.995f,"Forge's support elbow locks straight during "+clip.Name+": "+maxReach);
        }
        var muzzle=Vector3.Transform(V(model.Muzzle),Global(model.Clips.Single(c=>c.Name=="idle").Frames[0])[weapon]);
        check(muzzle.Y>2.5f && muzzle.Z>1,"Forge's muzzle points below the ground or behind the character.");
        var broken=model.Body.Triangles[0];model.Body.Triangles[0]=int.MaxValue;
        fails(()=>model.Validate(),"An invalid Forge mesh was accepted.");model.Body.Triangles[0]=broken;
        var flight=new ForgeRivetFlight();
        check(flight.Advance(0)==0 && flight.Advance(float.NaN)==0 && flight.Advance(-1)==0,"A paused/invalid simulation moved a rivet.");
        check(flight.Hit(123) && !flight.Hit(123) && flight.Hit(456),"A penetrating rivet damaged one enemy twice or failed to pierce.");
        check(flight.Advance(100)<=ForgeRules.RivetSpeed*.1f,"A stalled frame allowed an unbounded rivet sweep.");
        for(var i=0;i<100;i++)flight.Advance(1/60f);
        check(flight.Finished && flight.Travelled==ForgeRules.RivetRange && flight.Advance(1)==0,"A rivet exceeded its range.");
        var capped=new ForgeRivetFlight();
        check(!capped.Hit(-1) && capped.Hit(1) && !capped.Hit(1) && capped.Hit(2) && capped.Hit(3) && !capped.Hit(4) && capped.HitCount==3 && capped.Finished && capped.Advance(.01f)==0,
            "The heavy rivet must hit exactly three distinct targets, then stop.");
        var primaryGate=new ForgeChargeGate();
        check(primaryGate.TrySpend(0,1,ForgeRules.RivetCooldown(1)) && !primaryGate.TrySpend(.749,1,ForgeRules.RivetCooldown(1)) && primaryGate.TrySpend(.75,1,ForgeRules.RivetCooldown(1)),
            "Heavy rivets bypassed their 0.75-second base firing interval.");
        check(Math.Abs(ForgeRules.RivetCooldown(2)-.375f)<.00001f && ForgeRules.RivetCooldown(float.NaN)==.75f,"Rivet attack-speed scaling is invalid.");
        check(ForgeRules.Damage(1,0)*40==137.5f && ForgeRules.Damage(1,1)*40==175 && ForgeRules.Damage(1,3,true)*40==275 && ForgeRules.FragmentDamageMultiplier*40==50,"Rivet evolution damage is incorrect.");
        var charged=new ForgeRivetFlight(6);for(var i=0;i<6;i++)check(charged.Hit(i),"Charged rivet lost a penetration target.");
        check(charged.Finished && !charged.Hit(6),"Charged rivet exceeded six targets.");
        check(Enumerable.Range(1,9).Where(n=>ForgeRules.Charged(3,n)).SequenceEqual(new[]{3,6,9}) && !ForgeRules.Charged(2,3),"Charged shot rhythm is incorrect.");
        check(ForgeRules.SentryRange(0)==27 && ForgeRules.SentryRange(1)==33.75f && ForgeRules.SentryRange(3)==42.1875f && float.IsPositiveInfinity(ForgeRules.Duration(2,0)),"Persistent turret ranges or lifetime changed.");
        check(ForgeRules.SentryHealth(0)==250 && Enumerable.Range(1,3).All(l=>ForgeRules.SentryHealth(l)==375),"Turret health must retain the 25% increase at every evolution.");
        check(ForgeRules.MagneticRadius==10 && ForgeRules.MagneticDistance(9.5f)==8.85f && ForgeRules.MagneticDistance(.5f)==0 && ForgeRules.MagneticDistance(float.NaN)==0 && ForgeRules.MagneticDuration<ForgeRules.MagneticWindow,"Magnetic pull must bring edge targets to the centre before detonation without overshooting.");
        check(ForgeRules.Damage(2,0)*40==55 && ForgeRules.Damage(2,3)*40==41.25f && ForgeRules.Damage(3,3)*40==240 && ForgeRules.GrenadeRadius(0)==6 && ForgeRules.GrenadeRadius(1)==8 && ForgeRules.Duration(4,1)==13,"Area ability radius, burst damage or field lifetime changed.");
        check(Enumerable.Range(0,4).Select(ForgeRules.SentryTargets).SequenceEqual(new[]{3,4,6,8}),"Seeking target counts are incorrect.");
        check(ForgeRules.SentryHitMultiplier(1,false)==1 && ForgeRules.SentryHitMultiplier(2,false)==.75f && Enumerable.Range(3,6).All(i=>ForgeRules.SentryHitMultiplier(i,false)==.5f),"Seeker falloff is incorrect.");
        check(Enumerable.Range(1,8).All(i=>ForgeRules.SentryHitMultiplier(i,true)==1.25f),"Charged volleys must retain bonus damage without falloff.");
        var volleyA=new ForgeVolley();var volleyB=new ForgeVolley();
        check(!volleyA.Fire(3,false) && !volleyA.Fire(3,true) && !volleyA.Fire(3,false) && !volleyA.Fire(3,true) && volleyA.Fire(3,false) && volleyA.Fire(3,true) && !volleyB.Fire(3,false) && volleyA.Count==3 && volleyB.Count==1,"Paired barrels or separate turrets corrupted the third-volley counter.");
        check(ForgeRules.SentryCooldown(0)==10 && ForgeRules.SentryCooldown(1)==7.5f && ForgeRules.SentryRate(0)==1 && ForgeRules.SentryRate(3)==1.15f && ForgeRules.SentrySeekRadius==ForgeRules.GrenadeRadius(1)*1.5f,"Turret cadence, deployment cooldown or seeking radius is incorrect.");
        var seeker=new ForgeRivetFlight(8,150,120);
        for(var i=0;i<8;i++)check(seeker.Hit(i) && seeker.HasHit(i) && !seeker.Hit(i),"A seeker lost a target or damaged one twice.");
        check(seeker.Finished && !seeker.Hit(8),"A seeker exceeded eight targets.");
        var arc=new ForgeSeekArc(Vector3.Zero,Vector3.UnitZ*2,new Vector3(5,1,0));
        check(arc.Point(-1)==Vector3.Zero && arc.Point(2)==arc.To && Vector3.Normalize(arc.Tangent(0))==Vector3.UnitZ && arc.Length>Vector3.Distance(arc.From,arc.To),"Seeking arcs lost their endpoints, tangent or distance budget.");
        check(ForgeRules.CastRange(1)==36 && ForgeRules.CastRange(3)==36 && ForgeRules.CastRange(2)==12 && ForgeRules.CastRange(4)==36,"Grenade/field range diverged, leaked into sentry placement or reduced primary range.");
        var splinter=new ForgeRivetFlight(1,ForgeRules.FragmentRange,ForgeRules.FragmentSpeed);
        splinter.Advance(.1f);check(!splinter.Finished && splinter.Travelled>5,"Shrapnel still expires within a blink or cannot reach past its previous range.");
        splinter.Advance(.1f);splinter.Advance(.1f);
        check(splinter.Finished && splinter.Travelled==12 && splinter.Hit(1) && !splinter.Hit(2),"Shrapnel range or one-target contact limit is invalid.");
        var refund=new ForgeChargeGate();check(refund.TrySpend(0,2,8) && refund.TrySpend(0,2,8),"Multi-charge setup failed.");refund.RefundLast();
        check(refund.TrySpend(0,2,8) && !refund.TrySpend(0,2,8),"Failed deployment refund erased a prior charge.");
        var fast=new ForgeRivetFlight();
        check(Math.Abs(fast.Advance(1/60f)-2f)<.00001f && ForgeRules.RivetRadius==.20f && ForgeRules.RivetScale==2.625f,
            "The enlarged rivet must travel 120 units/second with a .20 contact radius.");
        using var rivetFile=File.OpenRead(Path.Combine(Path.GetDirectoryName(path)!,"forge-rivet.json"));
        var rivet=System.Text.Json.JsonSerializer.Deserialize<ForgeMesh>(rivetFile)!;rivet.Validate(0,false);
        var minZ=rivet.Vertices.Where((_,i)=>i%3==2).Min();var maxZ=rivet.Vertices.Where((_,i)=>i%3==2).Max();
        var shaftRadius=Enumerable.Range(0,rivet.Vertices.Length/3).Where(i=>rivet.Vertices[i*3+2]<-.18f).Max(i=>new Vector2(rivet.Vertices[i*3],rivet.Vertices[i*3+1]).Length());
        check(Math.Abs(minZ+.52f)<.00001f && maxZ==0 && Math.Abs(shaftRadius-.055f)<.00001f,"The projectile's muzzle fit, scale or forward axis changed.");
        var outward=true;
        for(var i=0;i<rivet.Triangles.Length;i+=3)
        {
            var a=V(rivet.Vertices,rivet.Triangles[i]*3);var b=V(rivet.Vertices,rivet.Triangles[i+1]*3);var c=V(rivet.Vertices,rivet.Triangles[i+2]*3);
            var normal=Vector3.Normalize(Vector3.Cross(b-a,c-a));
            for(var j=0;j<3;j++)
            {var n=V(rivet.Normals,rivet.Triangles[i+j]*3);outward &= Math.Abs(n.Length()-1)<.00001f && Vector3.Dot(normal,n)>.5f;}
        }
        check(outward,"The rivet has reversed, degenerate or invalid smoothed normals.");
        foreach(var equipment in new[]{"sentry","charge","aegis"})
        {
            using var rigFile=File.OpenRead(Path.Combine(Path.GetDirectoryName(path)!,"forge-"+equipment+"-rig.json.gz"));
            using var rigGzip=new GZipStream(rigFile,CompressionMode.Decompress);
            using var rig=System.Text.Json.JsonDocument.Parse(rigGzip);
            var good=true;var count=0;
            foreach(var part in rig.RootElement.GetProperty("Parts").EnumerateArray())
            {
                var mesh=part.GetProperty("Mesh").Deserialize<ForgeMesh>()!;mesh.Validate(0,false);count++;
                for(var i=0;i<mesh.Triangles.Length;i+=3)
                {
                    var a=V(mesh.Vertices,mesh.Triangles[i]*3);var b=V(mesh.Vertices,mesh.Triangles[i+1]*3);var c=V(mesh.Vertices,mesh.Triangles[i+2]*3);
                    var cross=Vector3.Normalize(Vector3.Cross(b-a,c-a));
                    for(var j=0;j<3;j++)
                    {var n=V(mesh.Normals,mesh.Triangles[i+j]*3);good &= Math.Abs(n.Length()-1)<.00001f && Vector3.Dot(cross,n)>.05f;}
                }
            }
            check(good && count>0,"Forge "+equipment+" has reversed, degenerate or invalid corner normals.");
        }
        var firing=model.Clips.Single(c=>c.Name=="fire");
        check(firing.Duration>=.5f && firing.Frames.Max(f=>Vector3.Distance(Global(f)[weapon].Translation,neutral[weapon].Translation))>.15f,
            "The heavy rivet lost its stronger recoil or deliberate recovery.");
    }
}
