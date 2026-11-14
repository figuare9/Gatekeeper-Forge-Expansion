# Nexus Mods review entry point

## Which release is this?

Current review source: **installer/gameplay 0.15.8**, protocol **12**, tag **`v0.15.8-source`**. The latest reported quarantined release is 0.15.8. [Build instructions](BUILD.md), [changes since 0.15.5](docs/RELEASE_0_15_8.md) and [source publication validation](docs/SOURCE_EXPORT_VALIDATION_0_15_8.md) apply to this tag.

The earlier `v0.15.5-source` tag remains unchanged. The original quarantined upload was **Gatekeeper.ForgeExpansion-Setup-v0.15.4.7z**, a different binary/source revision preserved under `historical/installer-0.15.4`. Do not substitute current source or current scan/build results as evidence about an older binary.

## Original 0.15.8 distribution identities

| Distribution | SHA-256 |
|---|---|
| Gatekeeper.ForgeExpansion-Setup-v0.15.8.exe | `2d0ea4a3ce4397d551204b1578055f9da99f0e12e17d4aaa5cf2105779f39e26` |
| Gatekeeper.ForgeExpansion-Setup-v0.15.8.zip | `f929634ef5e8fdc6bcc6318f8371fa4a11268a2da39592f2a94b9305e9ebc7e2` |
| Gatekeeper.ForgeExpansion-Linux-v0.15.8.tar.gz | `c957a28c6da3035d8edef08d902af2bc5d76e08819f426fff2c0f9ee05caffb8` |

These identify the locally built distribution files, not an unverified copy on another service. Repacking an EXE into another archive changes the outer archive hash; compare the contained EXE as well. A source rebuild may have a different hash because of build paths and archive metadata.

## Historical 0.15.5 distribution identities

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

The 0.15.8 production build passed **744 regular checks** and **756 actual-payload checks** for each platform. An isolated rebuild of the public source is documented separately in [source publication validation](docs/SOURCE_EXPORT_VALIDATION_0_15_8.md). Pure tests and cross-publishing do not replace in-game or Linux/co-op playtesting. The reported zero-health turret encounter was not replayed live during the latest fix.

The older 0.15.5 engineering review included native Windows smoke tests and Defender scans; those are historical results for that release, not a fresh scan of 0.15.8. No new antivirus scan, independent security certification or code signing was performed for this source update. The EXE remains unsigned, and Nexus must assess the relevant binary independently.

This repository intentionally excludes personal saves/account unlocks/receipts, credentials, game-owned executable/native metadata and generated game bindings. Local historical evidence can mention paths to private test folders that are not part of the public source. Dependency and supplied-asset licenses apply independently of the first-party MIT code license.
