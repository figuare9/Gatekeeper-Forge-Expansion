# Forge Expansion: Build Your Stronghold

Good news, Gatekeepers: Forge is ready to join the fight. What began as a new character has grown into a full community expansion, bringing a defensive engineer, an autonomous cannon companion, new ways to spend your Prisms, three Aspects of Chaos, adaptive Ascendant gameplay, and seven additional songs.

The idea behind Forge is simple: take a position, build it up, and make the enemy pay for every step. Heavy rivets punch through the front line while sentries cover the approaches. Magnetic charges gather enemies together, and a healing shield gives the team somewhere to hold its ground. Forge trades speed for preparation, durability, and sustained firepower.

These are the complete expansion notes for version **0.15.8**, including single-player Aspect visibility, zero-health elite death recovery, and stage-based Ascendant pacing and Forge discovery on Ethereal Bridge regardless of stage. The installer includes this offline news library and earlier update articles.

## At a glance

- **New unlockable character:** Forge, with a complete four-ability kit, three upgrades per active ability, and three passive effects.
- **New item:** Cannon Wheel, an independently moving and firing companion for every owned stack, up to ten.
- **New rest-area services:** Item Exchange and Prism Jackpot in both Quiet Bay and Quiet Place.
- **Three new Aspects:** Chaos, Scaling, and Elite, available immediately.
- **Ascendant:** an optional adaptive challenge that responds to offensive build strength in solo and co-op.
- **Seven expansion songs:** shuffled into regular gameplay alongside the original soundtrack.
- **Presentation and usability:** custom models, PBR materials, animation, ability icons, controller aiming, clearer tooltips, and performance and visual-lifetime fixes.

## Meet Forge

Forge enters combat with **250 health, 2 base regeneration, and 9 movement speed**. His rivet gun is a two-handed industrial weapon, supported by a custom armored character model, fitted weapon grips, an illustrated selection portrait, and a complete ability icon set.

Movement follows the same general approach as Tech Hunter: Forge turns smoothly toward his movement direction while traveling freely. Firing quickly turns him toward the target and keeps cursor-directed aiming active for three seconds before returning to free movement. Walk and sprint pacing have been adjusted to suit his heavy frame.

Forge starts **locked**. His selection slot shows the game's question-mark presentation until he is discovered. Whenever you visit **Celestium's Ethereal Bridge**, regardless of stage, look for the engineering relay in a side bay off the left-hand route. It sits beyond the entrance, aligned with the surrounding architecture, with space to approach and operate it. Approach the animated lever to unlock Forge and receive the portrait banner. Map rotation determines when that location appears; every visit can now lead to the unlock. Present party members receive the unlock; late arrivals can approach the relay themselves.

## Rivet Cannon — primary fire

Slow, heavy shots give Forge a deliberate rhythm. Each oversized rivet travels at **120 units per second**, reaches **36 metres**, and uses a **0.20-unit hit radius**. At base level, shots pierce up to **three enemies**, deal **343.75% weapon damage** (137.5 before modifiers at Forge's starting damage), and fire every **0.75 seconds** before attack-speed bonuses.

Surviving enemies are knocked back, with the latest balance pass increasing the push by **15%**. Blue tracers, a fitted projectile model, strong weapon recoil, and firing effects at the animated barrel tip make the shot easier to read in motion.

- **Upgrade I — Reinforced Rivets:** increases damage to **437.5%** and penetration to **four enemies**.
- **Upgrade II — Splinter Head:** each hit releases **two damaging fragments**, each dealing **125% damage** and traveling up to **12 metres**.
- **Upgrade III — Rail Driver:** every third shot deals **687.5% damage** and pierces up to **six enemies**, retaining the fragment upgrade.

Rivets can break Prism-bearing objects and destroy the Collector's pursuing skulls. Targeting accounts for enemy elevation, and scenery blocks shots appropriately. The primary-fire UI no longer flashes an orange resource warning simply because Forge has fired.

## Sentry Deployment — secondary fire

Deploy a persistent allied turret with **250 health**, **27-metre range**, and **55 starting damage per rivet** (137.5% weapon damage). A base turret fires every **0.85 seconds**, with a **10-second deployment cooldown**. Up to **three sentries** can remain deployed; placing another replaces the oldest. Enemies can target and destroy them.

Sentry rivets seek through nearby enemies rather than creating an unrestricted area blast. At base level they can strike **three targets**, dealing **100%, 75%, and 50%** damage in order. Subsequent targets must be within **12 metres** of the previous hit, and walls block the chain.

- **Upgrade I — Reinforced Emplacements:** reduces deployment cooldown to **7.5 seconds**, raises health to **375** and range to **33.75 metres**, adds **15% attack speed**, and raises the chain to **four targets**. Hits after the second deal 50% damage.
- **Upgrade II — Seeker Cascade:** extends each chain to **six targets**, retaining the earlier improvements.
- **Upgrade III — Twin Rails:** fires both barrels together, allows **four deployed sentries**, reaches **42.19 metres**, and chains through up to **eight targets**. Each barrel deals **103.125% weapon damage**; every third volley deals an additional **25% damage** with no damage falloff along its chain.

The updated turret uses the detailed supplied model with independent turning, aiming, barrel recoil, firing sound, blue seeking trails, muzzle flashes, and short smoke bursts. Its native health bar remains visible without the duplicate orange bar. Replacing a turret at the deployment limit does not trigger Forge's destruction passive.

## Volatile Charge — grenade ability

**Hold to aim; release to throw.** A red circular preview with a translucent red centre marks the target area. A quick tap throws immediately, while holding gives time to position the charge. The same aiming flow supports controller bindings.

The base charge can be thrown up to **36 metres**, detonates after **1.1 seconds**, deals **400% damage** in a **6-metre blast radius**, and has a **6-second cooldown**. It damages groups inside the blast rather than only the direct target.

- **Upgrade I — Concussion:** expands the blast to **8 metres** and adds a **one-second stun**.
- **Upgrade II — Magnetic:** strongly pulls enemies toward the charge before detonation from within a **10-metre pull radius**.
- **Upgrade III — Thermite:** raises explosion damage to **600%** and leaves a **5-metre burning area** for **four seconds**, dealing **80% damage per second**. Magnetic pull remains active.

The charge has its own model, and the magnetic pull and fragment damage paths received dedicated fixes during playtesting.

## Aegis Field — shield ability

Hold to position Aegis with the red area preview, then release to deploy it up to **36 metres** away. Turrets and shields resolve their placement to the ground instead of stacking on shield bubbles, constructs, or unsuitable scenery.

The base shield lasts **11 seconds**, covers a **9-metre radius**, grants **50% damage reduction**, and restores **4% of maximum health per second** to living friendly players and allied entities inside. This includes sentries and Halfmetal Heads. Sentries in the field also gain **30% attack speed**. Base cooldown is **16 seconds**.

- **Upgrade I — Expanded Aegis:** expands the radius to **10.5 metres** and extends duration to **13 seconds**.
- **Upgrade II — Suppression Field:** slows enemies inside by **30%**.
- **Upgrade III — Repulsor Aegis:** increases damage reduction to **65%** and pushes enemies back on deployment and at **two-second intervals**.

The field appears as a translucent blue hex-pattern shield bubble with green healing pulses. Charge-granting items such as Charged Core allow separate, fully working fields. Healing from overlapping fields stacks; damage reduction uses the strongest applicable shield rather than multiplying into invulnerability.

## Field Engineer — three passive effects

- **Command:** while Forge stands within **12 metres** of his sentries, they gain **25% damage**, **25% attack speed**, and regenerate **3% of maximum health per second**.
- **Enrage:** an enemy destroying one of Forge's sentries grants him **25% attack speed for five seconds**. Repeated triggers refresh the duration without stacking the bonus; replacing an old sentry does not count.
- **Self Repair:** after standing still for **one second**, Forge regenerates an additional **2% of maximum health per second** while stationary.

## Cannon Wheel — bring your own artillery

Win a **777 jackpot** at Prism Jackpot to permanently unlock this new **Amulet** for your account. The win unlocks access; it does not hand out a free stack. Once unlocked, Cannon Wheel can appear in eligible normal drops and random Quiet Bay Amulet offers, or be purchased through Item Exchange. Its native item listing and inventory tooltip explain the unlock and effect.

Each stack adds **one independent cannon**, up to **ten**. Each cannon fires every **two seconds**, dealing **250% of its owner's final damage**, with a **24.3-metre acquisition range** and **2.4-metre blast radius**. Additional stacks add companions rather than a hidden multiplier to every existing cannon.

Cannons patrol, pursue firing positions, hold range, back away from nearby enemies, and regroup with their owner. Faster shells use partial movement prediction, giving them a better chance against moving targets while still allowing enemies to dodge. Companions activate in combat areas and clear away in rest areas and menus; owned item stacks remain in inventory.

The redesigned model combines orange armor, dark machinery, metal fittings, rubber tread, and cyan lighting with a shared **2K PBR texture set**. Rolling wheels, differential turns, independent turret yaw and pitch, barrel recoil, muzzle effects, and impact sound bring the companion to life.

## Item Exchange — shape your build

A dedicated terminal beside the Emporium in **Quiet Bay and Quiet Place** lets you buy unlocked items and sell individual owned stacks. Category filters, search, item icons, colored stats, and Buy/Sell views make the expanded selection manageable. Controller navigation supports browsing, switching modes, confirming, and cancelling.

Purchase prices follow the current native Quiet Bay value, including applicable native modifiers, with a **26.5% premium**, rounded up. Selling returns **50% of native value**, rounded down. Triads are excluded. Buying capped items respects the player's current inventory and available capacity.

In co-op, purchases, sales, and wallet changes are scoped to the player using the terminal. The host validates the transaction and guards against duplicate delivery or payout. Interaction reach is more forgiving around the shop and slot cabinet, including side and rear approaches.

## Prism Jackpot — a reason to celebrate

The slot cabinet sits opposite the Emporium in **both rest areas**. On the first approach with at least **five Prisms**, it fixes that player's stake at **20% of their current balance**, rounded down, for the visit. Walking away or winning does not recalculate that stake.

Each spin has a **20% chance to win**. A winning result returns **five times the stake in total**; the original bet has already been paid. Bets and rewards belong to the bettor, including in co-op. Animated reels, cabinet lighting, dedicated sound effects, and a bounded burst of jackpot confetti complete the presentation. A confirmed 777 win also unlocks Cannon Wheel for that account.

## Three new Aspects of Chaos

All three Aspects are **unlocked by default**. They join the game's Aspect selection, tooltips, pause display, completion bonuses, and saved-run handling. The selection grid expands to a third row to make room for them.

- **Aspect of Chaos:** adds **75% more enemies**. With native Swarm's 50% increase, the combined bonus is **125% more enemies**, or **2.25 times** the normal amount.
- **Aspect of Scaling:** adds **7% enemy health** and **5% enemy Prism rewards** per average team level after level one, on top of normal scaling. Each enemy keeps the values assigned when it spawns. At team level 11, this means 70% extra health and 50% extra Prisms from this Aspect.
- **Aspect of Elite:** makes all hostile enemies elite, **including bosses**. Existing elite types are retained where applicable.

These choices can be combined with each other and with the native Aspects. Chaos and Swarm add their spawn bonuses together; they do not multiply each other.

The expansion Aspects are available directly from **Single Player**, with no private-lobby workaround. The Elite Aspect also recovers ordinary elites that receive lethal damage but stall at zero health without completing death. Native rewards and death effects are retained; boss phases and training dummies keep their scripted behavior.

## Ascendant — let strong builds meet stronger opposition

Ascendant is a separate, optional difficulty switch below the native difficulty choices. It starts **on for a new setup** and remembers an explicit choice. Its clearer button remains visibly clickable when off. It does not count as an Aspect or alter the displayed number of selected Aspects or their completion bonus.

At each combat stage's entry, Ascendant snapshots offensive build growth using **damage, attack speed, and expected critical damage**, relative to the character's starting stats. Solo uses that player's power; co-op uses the **average team power**, so the strongest player does not alone set the challenge for everyone.

The opening stage uses normal difficulty without an Ascendant bonus. From stage 2, the entry snapshot sets one Ascendant health factor and extra-elite chance for the entire stage. Level-ups, purchases, deaths and late joins do not recalculate them during that stage; accumulated build growth takes effect when the next stage loads. Continuing the same saved stage retains its entry snapshot.

Ascendant's **added health bonus and extra-elite chance are 10% lower through average character level 30**. This easing gradually fades across levels 31–50. From level 50, the established formula is unchanged: at eight times starting build strength, health is **2.12 times**; at sixteen times strength, **2.93 times**. The health factor remains capped at **25 times**. The reduction applies to the Ascendant bonus, preserving native baseline health.

Extra elite pressure begins only **above twice starting build power**. Every further doubling adds **three percentage points**, up to **15% extra elite chance** for eligible ordinary spawns. For example, four-times power adds 3%, eight-times adds 6%, and sixteen-times adds 9%. Native elites can still appear before this threshold. The threshold is based on build strength, not stage or elapsed time.

Ascendant focuses on enemy endurance and elite pressure while preserving normal outgoing attack damage for its added elites. It does not reduce item bonuses or continuously rewrite the health of enemies already alive. Native difficulty, enemy abilities, and any separately selected Aspects still apply. The separate Aspect of Scaling retains its own spawn-time level calculation. Aspect of Elite continues to make every enemy elite regardless of Ascendant's bonus chance.

## Shared balance and quality of life

- **Adjusters, Indicators, and Yellow Pills** each cap at **25 stacks** across all characters. Further acquisitions respect the cap, and loading an older over-cap run brings those stacks down to 25 through the game's inventory system.
- The mod's expanded **Halfmetal Head capacity** has been removed, restoring the game's native capacity behavior.
- Forge can fire, use abilities, attack training dummies, and break Prism objects in **Quiet Bay and Quiet Place**.
- Ability descriptions use shorter explanations, colored keywords, and separate stat lines to match the rest of the character UI.
- Grenade and shield hold-and-release aiming and Item Exchange navigation support controller input as well as keyboard and mouse.
- Forge's selection model loads without briefly presenting the old Hybrid placeholder. The portrait and ability icons have been revised to fit the game's presentation.
- Character grip, lower-body deformation, armor, canisters, and locomotion received dedicated fitting and animation passes. The gun sits farther forward for a more natural two-handed stance.
- Forge's body and gun hide on death and return on revival, following the tombstone presentation used by the other characters.

## Seven more songs for the journey

The expansion adds **seven replacement Forge tracks** to regular gameplay. They replace the expansion's earlier five-song selection and **do not replace the original game soundtrack**. A shuffled expansion track alternates with an original soundtrack turn, with short crossfades and no immediate repeat of the previous expansion song.

The new tracks are loudness-matched against native gameplay music and follow the game's **Music and Master volume controls**. Menu, boss, arena, and Quiet Bay music retain their native behavior. Music streams one compressed track at a time rather than loading the entire playlist into memory.

## Built for longer runs

The performance and visual-lifetime fixes from testing are included. Runtime entity lookups and presentation updates have been tightened, turret resources are prepared ahead of first deployment, and repeated shots use bounded effects and reusable projectile visuals. Shared mesh and material resources survive scene cleanup correctly.

The event tracking behind Forge's visuals no longer grows with every projectile over an entire run. This addresses the failure where turrets, shields, the weapon, and shots could stop displaying while their gameplay continued. The turret's duplicate health bar has also been removed. These improvements reduce avoidable work; frame rate still depends on hardware, settings, enemy counts, and the chosen Aspects.

## Install, unlock, and play together

The Windows EXE is a complete installation kit: it detects a compatible Gatekeeper installation, installs the full expansion, and provides update, repair, backup, and removal support. There is no content checklist to assemble. Once installed, launch through Steam or the kit's Play button.

This is an **unofficial community expansion**. The base game is required, and official DLC areas still require their official DLC. Everyone in a co-op group needs the same compatible **gameplay version**, currently **0.15.8**. All players should update together for the updated Ascendant stage snapshots and discovery requirement.

Forge and Cannon Wheel unlocks are local to each Steam account on that PC; they are not Steam achievements or Steam Cloud unlocks. Installer backups include the expansion's unlock sidecars when present, so keep those with your saves when moving to another computer. Finish runs containing custom content before removing the expansion.

A separate **desktop Linux x64 / Steam Proton testing kit** includes the same gameplay and these notes, with regular Steam and Flatpak discovery. It requires the documented one-time Steam launch option. Linux gameplay validation remains outstanding.

Thank you to everyone who has been putting Forge through his paces. Your testing has shaped the class, the economy, and the late-game challenge. Find the relay, assemble your defenses, and give the enemy a stronghold worth breaking.
