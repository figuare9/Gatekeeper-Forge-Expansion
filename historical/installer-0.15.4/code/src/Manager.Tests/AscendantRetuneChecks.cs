using Gatekeeper.Mods;

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
    }
}
