# Forge Expansion — installation kit 0.15.5

Send **Gatekeeper.ForgeExpansion-Setup-v0.15.5.exe**. This one Windows x64 EXE contains the complete community expansion and everything needed to install it. It contains no personal saves, unlocks, settings, logs, game binaries or generated game bindings.

For **desktop Linux**, send **Gatekeeper.ForgeExpansion-Linux-v0.15.5.tar.gz** instead. Extract it, run `Install-Forge.sh`, then add the Steam launch setting shown at the end. It installs the same expansion into the Windows game running through Proton. See [Linux steps](linux/README-LINUX.md). This is a Linux testing kit; actual Linux gameplay/co-op still needs verification.

## Install

1. Install Gatekeeper through Steam and close the game.
2. Save the EXE anywhere, such as Desktop or Downloads, and open it.
3. The installer finds Gatekeeper automatically. If it cannot find your game, choose **Find Gatekeeper** and select the folder containing **Gatekeeper.exe**. **Change folder** lets you choose another installation.
4. Click **Install expansion**. Your saves are backed up, the included files are installed, and the entire expansion is enabled together. There are no feature switches or separate downloads.
5. When it says **Ready to play**, choose **Play Gatekeeper** or close the installer and start the game normally from Steam. The expansion stays installed and enabled.

The first game launch can take longer while the loader prepares its Unity bindings. If Windows denies access to the Steam folder, close the installer, right-click its EXE and choose **Run as administrator**.

## Read the expansion news

The installer includes the complete developer-style DLC article, ability upgrades, items, economy, Aspects, Ascendant, music and quality-of-life notes. Use the expansion/article selectors and section jump list in the main window. No internet connection is needed to read it. Installer **0.15.5** retains gameplay **0.15.4** and protocol **12**. This update strengthens package verification and updates the bundled runtime and its loader dependencies. All co-op players should update together.

## Included together

- **Forge:** custom character, piercing rivet cannon, persistent sentries, magnetic/thermite grenades, Aegis shields, passives, animations, icons and sound.
- **Cannon Wheel:** the jackpot-unlocked cannon companion, its detailed PBR model, wheel motion, aiming, recoil and sound; 250% damage per cannon.
- **Item Exchange:** buy unlocked items and sell owned items beside the Emporium in Quiet Bay and Quiet Place.
- **Prism Jackpot:** the slot machine opposite the Emporium in both rest areas.
- **Seven Forge DLC songs:** shuffled into regular gameplay alongside the original soundtrack, with crossfades and the normal Music volume control.
- **Three new Aspects:** Chaos (+75% enemies, +125% with Swarm), Scaling (+7% enemy health and 5% Prism rewards per average team level after level 1), and Elite (all enemies and bosses are elite). All three start unlocked.

The expansion uses one fixed content configuration. The former movement, damage and cooldown sliders are not part of the installation flow; their optional balance patches remain off, matching the creator's current configuration. The legacy balance patches retain their single-player restrictions. Halfmetal Head uses its original capacity and summon rules.

The three expansion Aspects are immediately available. Forge starts locked: find and approach the engineering relay in the west-side service bay off the left-hand route on Celestium's Ethereal Bridge from Stage 7 onward. The fixed lever animates and unlocks Forge for everyone currently in the run; his portrait appears in the native top banner. Map rotation remains normal, so the exact stage can vary. A late joiner can approach it themselves. Existing Forge checkpoints can still continue. Forge unlock progress is local to each Steam account, in `BepInEx/config/local.gatekeeper.custommods.forge-progress.json`.

Cannon Wheel still requires winning 777 on Prism Jackpot; installation does not grant item stacks or unlock other native content. Cannon unlock progress stays in `BepInEx/config/local.gatekeeper.custommods.cannon-progress.json`. Both unlock files are preserved during updates/removal and copied beside installer save backups. Keep them with your own saves when moving PCs. These are local unlocks, not Steam achievements or Steam Cloud unlocks. Do not distribute your progress files or saves.

## Release balance

Ascendant now ramps more gently in both solo and co-op. Its extra elite chance starts after 2× starting build strength, grows 3% per further doubling, and caps at 15%. Its added enemy health is roughly halved through the middle of the power curve. Normal difficulty scaling continues; the independent Scaling Aspect remains +7% health and +5% Prisms per level. Everyone in a co-op lobby should update together.

Adjusters, Indicators and Yellow Pills now cap at 25 each. Restored runs above those limits are reduced through native inventory updates; keep the installer backup if you want to retain the old run for an earlier build. Halfmetal Head uses its stock limit. Forge starts with 250 health, 2 regeneration and 9 movement speed. Ascendant is initially on in new run setup; click its highlighted row to change it. Its flag does not select an Aspect or grant an Aspect bonus.

## Updates and recovery

Open the newer EXE and choose **Update expansion**. Existing installations made by the older mod manager are detected automatically, including their backups and installation records. Updating enables the complete package, rather than retaining old feature selections. The previous setup and changed packaged binaries are backed up.

If setup was interrupted or an included file is missing, choose **Repair installation**. The installer resumes its recorded operation. It refuses to overwrite files changed outside this installer or another mod loader.

**Support** provides save backups, backup/save folders, the game log, licenses and **Remove expansion**. Removing the expansion preserves personal saves, unlocks, generated configuration and caches, other plugins and changed files.

Finish or end any saved run using Forge, Cannon Wheel or the new Aspects before removing the expansion. Vanilla Gatekeeper does not recognize the custom character, item or Aspect definitions. The installer blocks removal while it records such a run. Do not bypass that check.

## Co-op testing

Everyone needs the same **0.15.5 gameplay version** and should update together. Install the kit, then use the game's normal co-op lobby and invitations. All content switches are set consistently by the installer. It bundles gameplay content **0.15.5 / protocol 12**, including the new Aspects, Forge abilities in Quiet Place and Quiet Bay, seeking turret rivets, improved firing effects and seven replacement gameplay songs. The performance and visual recovery fixes are retained. Everyone must update together; a lobby using the new Aspects requires matching Aspect support from every player. Music playback is local and does not add network traffic.

Co-op still needs live validation. Completed Forge save/resume, normal Cannon Wheel drops and its newer native Emporium listing/random pedestal offers also need further playtesting. Keep the automatic backups.

This is an unofficial community expansion, not a Steam DLC entitlement. Each player supplies their own Gatekeeper installation and any official DLC needed for official content. Supported binaries: Steam build **23057178**, Unity **2022.3.62f2**, Windows x64. Unsupported game builds are blocked before installation.

## Where files live

The self-contained EXE includes its Windows runtime, BepInEx loader/runtime, the pinned Unity support ZIP, both plugins, every model/texture, all four prepared slot sounds and the seven Forge DLC songs. No separate .NET, SDK, loader, song or asset download is needed during setup.

The EXE unpacks verified components into `%LOCALAPPDATA%/GatekeeperModManager/bundles`. Each selected game has settings, installation receipts and backups in `%LOCALAPPDATA%/GatekeeperModManager/games`. Existing folder-based installations keep their original state and backups. Moving or replacing the installer EXE preserves that data. The installed loader stays enabled for normal Steam launches until the expansion is removed.

Gatekeeper and Steam retain their normal network requirements. Gatekeeper and its assets belong to their owners. Third-party licenses and the supplied sound notices are bundled under **Support → Licenses & instructions**.


Installer 0.15.5 retains gameplay content 0.15.4 and co-op protocol 12. It updates bundled runtimes and verifies cached installation components against the embedded manifest.
