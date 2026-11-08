# Gatekeeper — Forge Expansion installation kit 0.15.4

A self-contained Windows installer for the complete community expansion. Open the EXE, confirm the detected game location, and click **Install expansion**. No mod selection, balance sliders or plugin manager is presented to players.

Distribute `release/Gatekeeper.ForgeExpansion-Setup-v0.15.4.exe`. See [friend installation steps](SHARE_WITH_FRIENDS.md).

A separate **desktop Linux / Steam Proton testing kit** is available as `release/Gatekeeper.ForgeExpansion-Linux-v0.15.4.tar.gz`. It uses the same gameplay payload, native Linux installation dialogs/terminal prompts, Steam/Flatpak discovery and Proton save backups. A one-time Steam DLL launch setting is required. See [Linux installation and validation status](linux/README-LINUX.md). Build with `build-linux.ps1`; this does not rebuild or install gameplay plugins. Linux gameplay has not yet been verified.

## Player experience

- The installer opens first. It detects a remembered/adjacent/Steam-library installation, or displays **Find Gatekeeper** if none is found or several are available.
- **Install expansion** installs and enables the complete content package after backing up saves and checking the pinned game binaries.
- After installation the button becomes **Play Gatekeeper**. Steam can also launch the installed expansion directly; the installer does not need to stay open.
- Existing managed installs get **Update expansion**. Interrupted installs or missing components get **Repair installation**. Configuration is set to the complete package on install/update/repair.
- **Support** contains backup/save folders, manual backups, the game log, bundled notices and removal. Unfinished runs using Forge, Cannon Wheel or expansion Aspects retain their removal protection.

The installation kit version is **0.15.4**. Gameplay content is **0.15.4 / protocol 12**. Three new native Aspects join the complete expansion, available immediately in solo and co-op. Forge combat in Quiet Place/Quiet Bay, turret balance, seeking rivets, firing effects, the seven replacement songs, performance fixes and visual recovery are retained. Everyone in co-op must use the same content version.

## Release 0.15.4 — Forge relay placement

The discovery relay now sits in a west-side service bay beyond the entrance junction, aligned with the outer wall. Ground support and clearance cover its entire footprint and lever swing; interaction checks include the map's wall collision layer. Existing unlocks remain intact. All co-op peers should update together. See [placement details and verification](research/FORGE_RELAY_PLACEMENT_0_15_4.md).

## Installer 0.15.3 — complete expansion news

The main window now includes a readable offline news library with the full developer-style expansion article, the latest Ascendant update and installer news. Browse by expansion and article, jump to sections, or copy the full text. [Read the complete player article](content/news/forge-complete.md). [Add a future expansion](research/EXPANSION_NEWS.md) by extending the catalog and adding Markdown; the selectors and bundled notices update automatically. Gameplay stays **0.15.2**, including co-op compatibility.

## Release 0.15.2 — gentler Ascendant ramp

Ascendant's health curve grows more slowly, and its extra elites start after the team's first doubling of build strength. Each subsequent doubling adds a 3% chance, capped at 15%. At 8× starting build strength, its health factor is now 2.12× (formerly 3.30×), with a 6% extra elite chance (formerly 18%). Solo uses the one player's build; co-op uses team-average power. Normal enemy scaling continues. The separate Scaling Aspect remains +7% health / +5% Prisms per level. See [retune and verification](research/ASCENDANT_RETUNE_0_15_2.md).

## Release 0.15.1 — discover Forge

Forge starts locked, with the native question-mark character card. From Stage 7 onward, find the engineering relay in the west service bay off the left-hand route on Celestium's Ethereal Bridge and approach the lever. Its pull animation unlocks Forge permanently for the current party, with his portrait in the native top unlock banner. Map rotation is unchanged, so the precise encounter stage varies. Late joiners can approach the relay themselves. Existing Forge checkpoints remain resumable. Details, editable assets and verification: [Forge discovery unlock](research/FORGE_UNLOCK.md).

Unlock progress is local to each Steam account, in `BepInEx/config/local.gatekeeper.custommods.forge-progress.json`; installer backups copy it beside the save ZIP. Installation/update does not grant it. Keep that file with your saves when moving PCs; it is not a Steam achievement or Steam Cloud unlock.

## Release 0.15.0 balance and presentation

Forge now starts with 250 health, 2 regeneration and 9 movement speed; rivet knockback is 15% stronger. Adjusters, Indicators and Yellow Pills cap at 25 each across all characters. Expanded Halfmetal Head capacity is removed. Item Exchange purchases are 10% more expensive than the former unrounded rate (native price × 1.265); sale payouts are unchanged. Jackpot wins have bounded confetti. Forge firing effects follow the barrel tip, and the custom body/gun respect death and revival. Ascendant starts on for new setup, has a clear clickable control, and is excluded from Aspect counts and bonuses. Seven replacement songs are normalized against original gameplay music. Details: [release notes and verification](research/RELEASE_0_15_0.md).

## Complete content package

**Three new Aspects of Chaos** are unlocked by default and use the native selection, tooltips, completion bonus, pause display and saved-run systems. **Chaos** adds 75% more enemies; with Swarm, the total bonus is 125% (2.25× normal). **Scaling** adds 7% enemy health and 5% enemy Prism rewards per average team level after level 1, frozen at each enemy's spawn. **Elite** makes all hostile enemies elite, including bosses. The six-column menu expands to three rows. Details and validation: [expansion Aspects](research/EXPANSION_ASPECTS.md).

**Ascendant difficulty** is an optional switch below the native difficulty choices, on by default for new run setup; the toggle remembers an explicit choice. Its gentler health curve uses team offensive power. Bonus elites begin above 2× starting build power, rise 3% per further doubling, and cap at a 15% chance for eligible ordinary spawns. Additional elites keep ordinary outgoing attack damage. Existing enemies retain their spawn snapshot; items are not nerfed. The native difficulty choice and the three expansion Aspects can still be selected independently. See [current Ascendant tuning](research/ASCENDANT_RETUNE_0_15_2.md) and [controller validation history](research/FORGE_UX_ASCENDANT.md).

**Seven replacement expansion songs** join regular gameplay for every character. A shuffled Forge song alternates with an original soundtrack turn, with short crossfades and no immediate Forge-song repeat. The game's Music and Master volume controls apply. Menu, Quiet Bay, arena and boss music keep their native behavior. Compressed music streams one song at a time; integrity checks and cache extraction run off the frame loop. No original song or game audio bank is replaced. Details: [music integration](research/EXPANSION_MUSIC.md).

**Forge** unlocks through the Ethereal Bridge engineering relay from Stage 7 onward. The class has the custom armored character, two-handed rivet gun, fitted animation set, illustrated portrait and ability icons. Its kit includes piercing rivets, persistent allied sentries, magnetic/thermite grenades, protective healing Aegis shields and the three-part Field Engineer passive. Hold the grenade or Aegis ability to aim and release to cast, using keyboard or controller. Item Exchange supports controller navigation, Buy/Sell, confirm and cancel. Detailed mechanics: [Forge kit](research/FORGE_KIT.md).

**Cannon Wheel** is a new Amulet unlocked for an account by winning 777 at Prism Jackpot. Installation does not grant a stack. After unlocking, obtain it through normal drops, eligible random Quiet Bay Amulet offers or the Item Exchange. One independently firing cannon per stack, up to 10; each deals 250% of the owner's base damage per shot. The detailed model has a shared 2K PBR material, wheels, independent aiming and recoil. Mechanics: [Cannon Wheel](research/CANNON_WHEEL.md). Art: [model notes](art/cannon-wheel/README.md).

**Item Exchange** adds a terminal beside the Emporium in Quiet Bay and Quiet Place. Buy unlocked items at live Quiet Bay prices plus 26.5%; sell one owned non-Triad stack for 50% of native value. Co-op transactions verify the buyer's own inventory, unlocks and wallet. The host validates and delivers each transaction once.

**Prism Jackpot** adds a slot cabinet opposite the Emporium in both rest areas. First approach with at least five Prisms fixes the bet at 20% of that balance for the visit. A 20% win chance returns five times the bet in total. Stakes and payouts belong to the bettor. [Slot rules](research/PRISM_JACKPOT.md), [audio notes](research/SLOT_AUDIO.md).


The fixed profile enables the complete expansion. Legacy optional movement/damage/cooldown patches remain off, matching the creator's configuration and native base combat tuning. Their APIs remain for development/backward compatibility, but the player UI does not expose them or third-party plugin management.

## Multiplayer and progress

All peers should use the same installation kit and game version. Forge checks class revision and roster order; shop, slots and cannons use protocol 12 and owner-scoped receipts. Co-op still needs live validation. Earlier solo checks cover Forge movement, rigging, ballistics, elevation targeting, prism rewards, grenade damage/pull/thermite, turret health/caps/targeting, extra charges, shield healing and long-session visual recovery. The new Cannon model was exercised with one and ten native companions. Completed custom save/resume lifecycles and some native Cannon drop/Emporium/pedestal flows still need playtesting.

Cannon unlock progress is local and scoped to each Steam account on that PC, in `BepInEx/config/local.gatekeeper.custommods.cannon-progress.json`. It is not a Steam achievement or Steam Cloud unlock. Each peer unlocks its own item. Native drops/pedestals follow the host's pool; an unlocked client can buy its own Cannon Wheel from the exchange.

Do not remove the expansion during an unfinished saved Forge or Cannon Wheel run. The installer blocks removal when a run marker requires custom content; vanilla cannot read those definitions. Save backups include the native Saves folder and GatekeeperSettingsSave files; matching cannon-progress and forge-progress JSON files are copied beside each ZIP when present.

## Installation and recovery

Setup validates all package sources and target ownership before writing, rejects linked paths and unrelated loaders, backs up saves/previous setup, and records an incomplete receipt before copying. Replacements are staged and verified. Retry can recover either a fresh partial installation or an interrupted update from the previous receipt. Missing owned files are repaired; externally edited files are preserved and reported. Completion is recorded only after the full package, fixed configuration and enabled loader are in place.

Installed state checks every packaged component hash and the required configuration values, accepting normal BepInEx comment/order/case formatting. Removal deletes only unchanged owned files and preserves saves, unlocks, other plugins, generated configuration/caches and backups.

Fresh data is stored under `%LOCALAPPDATA%/GatekeeperModManager/games/<installation-key>`. Existing `GatekeeperModManager/state/installation.json` records are reused in place. The single-file payload extracts to a verified hash-addressed local cache. It includes both .NET runtimes, the pinned Unity support ZIP, plugins, assets, instructions and licenses. It excludes saves, config, logs, game binaries, generated interop assemblies and development probes.

## Supported game and development

- Steam app **2106670**, build **23057178**, Windows x64.
- Unity **2022.3.62f2**, IL2CPP metadata 31.
- GameAssembly SHA-256: `2f59b50a1dde831a32006b419adbf560a195b0de638d0669bac8c029e85d9ce9`.
- Metadata SHA-256: `e0f9349d0d37aa0684243c63a72057c238f104e0c67abf150d7ac9cf8918b009`.
- BepInEx **6.0.0-be.791+b782da4**.

`src/Manager` contains the installer UI and development command-line operations. `src/Manager.Core/ExpansionKit.cs` defines the fixed package policy, integrity checks and recoverable install/update flow. The legacy core methods remain for development and compatibility. `src/GameMods` and `src/SubtleBalance` contain the existing gameplay plugins; generated game reference assemblies are never redistributed.

Run `build.ps1` for checks and a development publish. Run `build-portable.ps1` to verify the actual bundled loader/components and create the self-contained release EXE. Tests use temporary synthetic game/save folders, including fresh installs, legacy migration, interrupted setup, fixed configuration, file conflicts, unsupported games, full-payload installation/removal and saved-run protection.

This is an unofficial community expansion. It does not grant official Steam DLC entitlements. Each player supplies the base game and any official DLC required for official content. See [third-party notices](THIRD_PARTY_NOTICES.md) and the bundled licenses.

