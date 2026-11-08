namespace Gatekeeper.Mods;

public static class ForgeTooltips
{
    private static string Stat(string name,string value,string color="8ABFDC")=>$"<color=#{color}>{name}:</color> {value}";
    private static string Text(string intro,params string[] stats)=>intro+"\n\n"+string.Join("\n",stats);
    public static readonly string[] Descriptions={"",
        Text("Heavy rivets pierce enemies and knock back survivors.",Stat("Damage","343.75%","EE6055"),Stat("Targets","3"),Stat("Fire Interval","0.75 s")),
        Text("Deploy a permanent sentry. Its rivets seek nearby enemies; walls block them. Replaces the oldest at the limit.",Stat("Damage","137.5%","EE6055"),Stat("Seeking Targets","3 (100% / 75% / 50%)"),Stat("Health","250","47BA8E"),Stat("Range","27 m"),Stat("Fire Interval","0.85 s"),Stat("Maximum Sentries","3"),Stat("Cooldown","10 s")),
        Text("Hold the ability to aim; release to throw an explosive charge.",Stat("Damage","400%","EE6055"),Stat("Blast Radius","6 m"),Stat("Throw Range","36 m"),Stat("Fuse","1.1 s"),Stat("Cooldown","6 s")),
        Text("Hold the ability to aim; release to shield and heal allies. Sentries inside fire faster. Extra charges allow separate fields; healing stacks.",Stat("Damage Reduction","50%"),Stat("Healing / Second","4% max health","47BA8E"),Stat("Sentry Attack Speed","+30%","FFC31A"),Stat("Radius","9 m"),Stat("Duration","11 s"),Stat("Placement Range","36 m"),Stat("Cooldown","16 s")),
        "<color=#FFC31A>COMMAND</color>\nNearby sentries gain 25% damage and attack speed, and heal 3% max health/s.\n"+Stat("Command Radius","12 m")+
        "\n\n<color=#FFC31A>ENRAGE</color>\nAn enemy destroying a sentry grants 25% attack speed for 5 s. Refreshes without stacking."+
        "\n\n<color=#47BA8E>SELF REPAIR</color>\nAfter standing still for 1 s, heal 2% max health/s."
    };
    public static readonly string[][] Upgrades={System.Array.Empty<string>(),
        new[]{Text("REINFORCED RIVETS\nStronger rivets pierce an extra enemy.",Stat("Damage","437.5%","EE6055"),Stat("Targets","4")),
            Text("SPLINTER HEAD\nEach hit releases two damaging fragments.",Stat("Fragment Damage","125% each","EE6055"),Stat("Fragment Range","12 m")),
            Text("RAIL DRIVER\nEvery third shot becomes a heavy rail shot. Keeps shrapnel.",Stat("Third Shot Damage","687.5%","EE6055"),Stat("Third Shot Targets","6"))},
        new[]{Text("REINFORCED EMPLACEMENTS\nFaster, tougher sentries with greater reach.",Stat("Attack Speed","+15%","FFC31A"),Stat("Seeking Targets","4 (100% / 75% / 50% / 50%)"),Stat("Health","375","47BA8E"),Stat("Range","33.75 m"),Stat("Cooldown","7.5 s")),
            Text("SEEKER CASCADE\nRivets chain through more enemies. Keeps earlier upgrades.",Stat("Seeking Targets","6")),
            Text("TWIN RAILS\nTwin barrels fire together. Every third volley hits at full damage throughout its chain.",Stat("Damage / Barrel","103.125%","EE6055"),Stat("Third Volley Damage","+25%","EE6055"),Stat("Seeking Targets","8"),Stat("Maximum Sentries","4"),Stat("Range","42.19 m"))},
        new[]{Text("CONCUSSION\nA wider explosion stuns enemies.",Stat("Blast Radius","8 m"),Stat("Stun Duration","1 s")),
            Text("MAGNETIC\nPulls enemies strongly toward the charge before detonation.",Stat("Pull Radius","10 m")),
            Text("THERMITE\nA stronger blast leaves burning ground. Keeps the magnetic pull.",Stat("Explosion Damage","600%","EE6055"),Stat("Burn Damage / Second","80%","EE6055"),Stat("Burn Radius","5 m"),Stat("Burn Duration","4 s"))},
        new[]{Text("EXPANDED AEGIS\nA larger, longer-lasting shield. Keeps healing and damage reduction.",Stat("Radius","10.5 m"),Stat("Duration","13 s")),
            Text("SUPPRESSION FIELD\nEnemies inside the shield move more slowly.",Stat("Enemy Speed","-30%")),
            Text("REPULSOR AEGIS\nStronger protection pushes enemies back on deployment and at intervals.",Stat("Damage Reduction","65%"),Stat("Repulse Interval","2 s"))},
        new[]{"All three Field Engineer effects are active. Evolve Forge's four active abilities.","All three Field Engineer effects are active. Evolve Forge's four active abilities.","All three Field Engineer effects are active. Evolve Forge's four active abilities."}
    };
}
