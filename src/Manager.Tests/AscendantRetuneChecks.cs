using Gatekeeper.Mods;
using System.Text.Json;

internal static class AscendantRetuneChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var (power,health,elites) in new[]{(2f,1.2320725f,0f),(4f,1.5838285f,.03f),(8f,2.116991f,.06f),
            (16f,2.9251142f,.09f),(32f,4.15f,.12f),(64f,6.0065796f,.15f)})
        {
            check(Math.Abs(AscendantRules.Health(power)-health)<.00001f && Math.Abs(AscendantRules.EliteChance(power)-elites)<.00001f,
                "Ascendant diverged from the agreed health/elite curve at build power "+power);
            var solo=AscendantRules.TeamPower(new[]{power});var identicalTeam=AscendantRules.TeamPower(new[]{power,power,power,power});
            check(solo==power && identicalTeam==power && AscendantRules.Health(solo)==AscendantRules.Health(identicalTeam) &&
                AscendantRules.EliteChance(solo)==AscendantRules.EliteChance(identicalTeam),"Solo and equivalent co-op builds scale differently.");
        }
        foreach(var invalid in new[]{-1f,0f,1f,float.NaN,float.NegativeInfinity,float.PositiveInfinity})
            check(AscendantRules.Health(invalid)==1 && AscendantRules.EliteChance(invalid)==0,"Starting/invalid build acquired an Ascendant bonus.");
        check(AscendantRules.EliteChance(1.99f)==0 && AscendantRules.EliteChance(2)==0 && AscendantRules.EliteChance(2.01f)>0,
            "Bonus elites did not respect the first-doubling grace threshold.");
        var mixed=AscendantRules.TeamPower(new[]{1f,31f});
        check(mixed==16 && Math.Abs(AscendantRules.Health(mixed)-2.9251142f)<.00001f && Math.Abs(AscendantRules.EliteChance(mixed)-.09f)<.00001f,
            "Mixed co-op builds were scaled by their strongest member or their summed power.");
        var previousHealth=1f;var previousElites=0f;
        var monotonic=true;var gentler=true;var bounded=true;
        foreach(var power in Enumerable.Range(1,1000).Select(n=>1+n*.1f).Append(100000f).Append(float.MaxValue))
        {
            var health=AscendantRules.Health(power);var elites=AscendantRules.EliteChance(power);
            monotonic&=health>=previousHealth && elites>=previousElites;
            bounded&=float.IsFinite(health) && health>=1 && health<=25 && elites>=0 && elites<=.15f && health<power;
            var former=Math.Clamp(1+.7f*((float)Math.Pow(power,.70)-1),1,25);
            gentler&=health<=former+.0001f;
            previousHealth=health;previousElites=elites;
        }
        check(monotonic && gentler && bounded,"Ascendant stopped growing, exceeded limits, or became harsher than the previous curve.");
        check(AscendantRules.Health(100000)==25 && AscendantRules.EliteChance(128)==.15f,
            "Late-game health/elite ceilings changed unexpectedly.");
        var healthWithScaling=AspectRules.Health(100,AspectRules.TeamScaling(new[]{20}).Health*AscendantRules.Health(8));
        check(Math.Abs(healthWithScaling-493.2589f)<.001f && AspectRules.TeamScaling(new[]{20}).Prisms==1.95f,
            "The Ascendant retune changed the independent 7% health / 5% Prism Scaling Aspect.");
        StageChecks(check);
    }
    private static void StageChecks(Action<bool,string> check)
    {
        foreach(var level in new[]{1f,15f,30f})
        {
            var early=AscendantRules.ForStage(8,level,1);
            check(Math.Abs((early.Health-1)/(AscendantRules.Health(8)-1)-.9f)<.000001f && Math.Abs(early.ExtraElites-.054f)<.000001f,
                "Ascendant did not ease its added health and elite pressure by 10% through level 30.");
        }
        foreach(var (level,ease) in new[]{(30f,.90f),(35f,.925f),(40f,.95f),(45f,.975f),(50f,1f)})
            check(Math.Abs(AscendantRules.EarlyFactor(level)-ease)<.000001f,"Ascendant's level 30–50 recovery ramp is incorrect.");
        foreach(var power in new[]{1f,2f,8f,64f,100000f,float.NaN})
        {
            check(AscendantRules.ForStage(power,50,1)==new AscendantFactors(AscendantRules.Health(power),AscendantRules.EliteChance(power)) &&
                AscendantRules.ForStage(power,100,100)==new AscendantFactors(AscendantRules.Health(power),AscendantRules.EliteChance(power)),
                "Level-50+ Ascendant changed its established end-game formula.");
            check(AscendantRules.ForStage(power,100,0)==AscendantFactors.Unscaled,"The opening stage acquired an Ascendant bonus.");
        }
        check(AscendantRules.ForStage(1,20,1)==AscendantFactors.Unscaled && AscendantRules.ForStage(8,float.NaN,1)==AscendantRules.ForStage(8,1,1),
            "Ascendant reduced native baseline health or accepted an invalid easing level.");
        var cache=new AscendantStageCache();var calls=0;
        (float Power,float TeamLevel) Entry(float power,float level){calls++;return (power,level);}
        check(cache.Capture("run-a",101,0,()=>Entry(64,60))==AscendantFactors.Unscaled && calls==0,"The starting stage sampled/penalized a starter build.");
        var first=cache.Capture("run-a",201,1,()=>Entry(8,20));
        var upgraded=cache.Capture("run-a",201,1,()=>Entry(64,60));
        check(first==upgraded && calls==1 && cache.Current!.Power==8 && cache.Current.TeamLevel==20,
            "Buying items or leveling during the stage recalculated its Ascendant snapshot.");
        var serialized=JsonSerializer.Serialize(cache.Current);var resumed=new AscendantStageCache();
        check(resumed.Restore(JsonSerializer.Deserialize<AscendantStageSnapshot>(serialized),"run-a",201,1) &&
            resumed.Capture("run-a",201,1,()=>Entry(128,100))==first && calls==1,
            "Continue/reloading the same stage changed its frozen entry factors.");
        var next=resumed.Capture("run-a",201,2,()=>Entry(64,60));
        check(calls==2 && next==new AscendantFactors(AscendantRules.Health(64),AscendantRules.EliteChance(64)),
            "Advancing a stage on a repeated map did not apply accumulated build growth.");
        resumed.Capture("run-a",203,2,()=>Entry(16,40));
        resumed.Capture("run-b",203,2,()=>Entry(4,10));
        check(calls==4 && resumed.Current!.Session=="run-b" && resumed.Current.Power==4,
            "A new map or run reused a different stage's snapshot.");
        var saved=JsonSerializer.Deserialize<AscendantStageSnapshot>(serialized)!;
        check(!resumed.Restore(saved,"other-run",201,1) && !resumed.Restore(saved,"run-a",203,1) && !resumed.Restore(saved,"run-a",201,2),
            "An unrelated save/map/stage restored the wrong Ascendant factors.");
        saved.Revision="ascendant-2";
        check(!resumed.Restore(saved,"run-a",201,1),"An older tuning revision restored a stale Ascendant snapshot.");
        saved.Revision=AscendantRules.Revision;saved.Power=float.NaN;
        check(!resumed.Restore(saved,"run-a",201,1),"Malformed saved Ascendant power bypassed snapshot validation.");
        var solo=new AscendantStageCache().Capture("solo",201,1,()=> (AscendantRules.TeamPower(new[]{8f}),20));
        var team=new AscendantStageCache().Capture("team",201,1,()=> (AscendantRules.TeamPower(new[]{8f,8f,8f,8f}),20));
        check(solo==team,"Stage-frozen solo and equivalent co-op teams have different difficulty.");
        using(var fixture=new Fixture())
        {
            var path=Path.Combine(fixture.Game,"BepInEx/config/local.gatekeeper.custommods.ascendant-stage.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,serialized);
            var backup=fixture.Service.BackupSaves()!;
            check(File.ReadAllText(Path.ChangeExtension(backup,"ascendant-stage.json"))==serialized,"Installer backups omitted the frozen Ascendant stage snapshot.");
        }
    }
}
