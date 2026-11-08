# Nexus Mods review entry point

## Which release is this?

Current source: installer **0.15.5**, gameplay **0.15.4**, protocol **12**. Original quarantined upload: **Gatekeeper.ForgeExpansion-Setup-v0.15.4.7z**. The latter is a different binary and source revision. Its preserved code/provenance is under `historical/installer-0.15.4`; do not substitute the current source as evidence about what the old binary does.

| Original tested distribution | SHA-256 |
|---|---|
| Gatekeeper.ForgeExpansion-Setup-v0.15.5.exe | `50fd12e47692c87d803a612a859d5c9f838fa445fbdb2d4901c11c72f1131811` |
| Gatekeeper.ForgeExpansion-Setup-v0.15.5.zip | `9379e0c446dabed2a79cd5aba49f8c2c90c1b9e56e4e997e68ad80084f3279f0` |
| Gatekeeper.ForgeExpansion-Linux-v0.15.5.tar.gz | `71538e45c66bf6afb3459e9c0a8409043ad5315bd4af556c55f758a51a283b65` |

## Review map

- `src/Manager`: Windows Forms installer UI and local command-line operations.
- `src/Manager.Linux`: Linux installer UI/terminal and Steam/Proton handling.
- `src/Manager.Core`: manifest verification, safe extraction/staging, game detection, ownership receipts, backups, install/repair/remove and process launch.
- `src/Manager.Tests`: installer and pure gameplay regression tests.
- `src/GameMods`, `src/SubtleBalance`, `src/Shared`: game plugins and shared rules/protocol.
- `src/GameMods/Assets`: exact model, texture, icon, audio and music build inputs.
- `vendor/BepInEx`: complete corresponding modified third-party loader source, locks and licenses.
- `bootstrap.py`, `dependencies.lock.json`, root build scripts and `tools/loader_dependency_manifest.py`: dependency/bootstrap/build path.

The installer places the loader/runtime/plugins in the selected supported game installation, enables the fixed expansion profile and records owned files. It backs up relevant progress/config before changes, uses a per-user extracted package cache, and provides managed repair/removal. It launches Gatekeeper through Steam when requested. Game plugins use the game's existing multiplayer stack for host-authoritative gameplay. The source includes no updater, telemetry endpoint or separate credential collection UI; reviewers should inspect the code rather than treat this statement as an audit result.

0.15.5 replaces the obsolete runtime and updates the loader patching/logging dependency stack. It retains the original embedded inventory through installation, excludes unlisted cache files, revalidates staging and avoids truncating through a stale hard-linked temporary. See [detailed findings, fixes and actual validation limits](docs/SECURITY_FIXES_0_15_5.md).

## Evidence limits

The project previously passed 713 actual-payload checks, native solo smoke tests and Defender scans with no detected threats. Those are engineering checks, not independent security certification, code signing or proof that every possible threat is absent. Native Linux execution, live peer co-op and independent antivirus-laboratory certification were not performed. The EXE remains unsigned; Nexus must assess the release independently.

This repository intentionally excludes personal saves/account unlocks/receipts, credentials, game-owned executable/native metadata and generated game bindings. Local historical evidence can mention paths to private test folders that are not part of the public source. Dependency and supplied-asset licenses apply independently of the first-party MIT code license.
