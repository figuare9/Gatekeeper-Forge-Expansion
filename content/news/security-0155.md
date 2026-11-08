# A Stronger Foundation for Forge

Forge Expansion installation kit **0.15.5** strengthens the expansion's installation and runtime foundations. Your characters, balance, assets, music and unlock progress remain on gameplay content **0.15.4**, using co-op protocol **12**.

## Updated bundled runtimes

The installation kits and game loader now include Microsoft .NET **10.0.14**, an actively supported LTS runtime. Players do not need to install .NET separately. The loader's complete runtime is updated together; this avoids mixing system libraries from different .NET releases.

The loader also includes a Forge-maintained BepInEx compatibility build with updated HarmonyX and MonoMod libraries. This is a modified build, rather than an official upstream BepInEx release. Its corresponding source and original license are included under `notices/loader-source`.

## Verified installation components

The installer now keeps the trusted component list from its embedded package through to installation. It rejects changed cache files and verifies staged replacements against the original hashes before using them. Unexpected cache files are preserved separately and a clean cache is rebuilt from the installer. Interrupted extraction can resume safely.

Updates back up and retire unchanged files belonging to the older runtime. Files changed outside the installer are preserved for review. Save backups, unlock protection and the existing installation recovery checks remain in place.

## Availability

Windows and desktop Linux installation kits include the same gameplay files. Linux uses the Windows game through Steam Proton. Windows runtime compatibility is tested locally; native Linux gameplay and co-op still require testing on a Linux machine.

These checks and runtime updates are not antivirus certification and do not guarantee automatic approval by a mod hosting service.
