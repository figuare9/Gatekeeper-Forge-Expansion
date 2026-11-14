# Gatekeeper Forge Expansion — source and build instructions

Complete current mod/installer source for Nexus Mods review. **Installer 0.15.8, gameplay content 0.15.8, protocol 12.** This is an unofficial community expansion for Gatekeeper, Steam app 2106670, pinned Windows build 23057178.

Review the fixed [v0.15.8-source snapshot](https://github.com/figuare9/Gatekeeper-Forge-Expansion/tree/v0.15.8-source). The earlier [v0.15.5-source snapshot](https://github.com/figuare9/Gatekeeper-Forge-Expansion/tree/v0.15.5-source) remains available, and [the comparison](https://github.com/figuare9/Gatekeeper-Forge-Expansion/compare/v0.15.5-source...v0.15.8-source) shows the intervening changes.

- [Detailed build instructions](BUILD.md)
- [Security review guide and release identity](SECURITY_REVIEW.md)
- [Changes since 0.15.5 and current validation](docs/RELEASE_0_15_8.md)
- [Original 0.15.8 distribution hashes](docs/RELEASE_SHA256_0_15_8.txt)
- [Current source publication validation](docs/SOURCE_EXPORT_VALIDATION_0_15_8.md)
- [Complete expansion feature article](content/news/forge-complete.md)
- [0.15.5 security fixes and validation](docs/SECURITY_FIXES_0_15_5.md)
- [Original quarantined 0.15.4 source snapshot](historical/installer-0.15.4/README.md)

The source covers Forge and its unlock relay, seeking turrets, rivets, grenades, healing shields, Cannon Wheel, Item Exchange, Prism Jackpot, expansion Aspects, Ascendant, item caps, controller/UI changes, music playback, Windows/Linux installers and installer news. Embedded runtime model/texture/audio inputs are in `src/GameMods/Assets`. Editable authoring masters are not needed to compile these inputs.

`src` contains the first-party C# projects and pure rule/test code. `vendor/BepInEx` contains the complete corresponding modified loader source, its dependency locks, LGPL license and `FORGE-CHANGES.md`. Current production build scripts are included verbatim. `bootstrap.py` is an additional source-export helper: it downloads public, hash-pinned toolchain/runtime inputs and verifies the included common Unity support ZIP declared in `dependencies.lock.json`. NuGet restores the loader's locked dependencies from its declared upstream feeds. Generated gameplay bindings must be made locally from a legitimate game installation.

No Gatekeeper executable/native metadata, generated game bindings, player saves, unlock records, installation receipts or account credentials are included. This repository contains source publication snapshots, not reconstructed historical development commits. The original quarantined 0.15.4 source has its own explicit provenance; it must not be confused with the current release.

The code license is [MIT](LICENSE). BepInEx and other dependencies retain their own licenses. Supplied models, textures and recordings are not relicensed by the code license; they are included as mod build inputs for review. See [third-party and asset notices](THIRD_PARTY_NOTICES.md). Game ownership and official DLC entitlements are separate.

The Windows/Proton loader uses the complete official .NET 10.0.14 runtime with a Forge-maintained compatibility build. It is not an official BepInEx release. The Linux package is a native installer for the Windows game under Steam Proton; native Linux/Proton gameplay and live co-op require separate testing. Builds and scans do not constitute antivirus certification or guarantee Nexus approval.
