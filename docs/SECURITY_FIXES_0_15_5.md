# Forge Expansion 0.15.5 — security fixes and verification

November 7, 2026. Installation kit **0.15.5**, gameplay content **0.15.4**, co-op protocol **12**. This follow-up resolves the two findings from the 0.15.4 review. It is engineering validation, not an independent security certification.

## SR-01: bundled obsolete runtime — resolved

The loader's entire .NET 6.0.7 runtime was replaced with the complete official Microsoft .NET **10.0.14** Windows x64 shared runtime: all **189** distributed runtime files match the official archive byte for byte. The archive and SDK downloads matched Microsoft's published SHA-512 metadata. Both self-contained installers also embed .NET 10.0.14; their extracted runtime configuration files confirm this.

Microsoft lists this .NET 10 release as active LTS, supported through November 14, 2028. Source: [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json). A fresh OSV query for the exact Windows runtime package/version returned no matching advisories; this is not proof that all vulnerabilities are known.

A direct runtime swap failed because the old patching stack was incompatible. The release therefore includes a **modified Forge-maintained BepInEx compatibility build**, `6.0.0-forge-runtime.1`, based on upstream commit `b782da4ac54e1072d2857f4a2f77c80c3ef08005`. It updates HarmonyX to 2.16.1, MonoMod.RuntimeDetour to 25.3.6, MonoMod.Utils to 25.0.14 and Microsoft.Extensions.Logging to 10.0.14. Its dependency graph is locked. The original Windows Doorstop proxy and native detour libraries are retained.

This is not an official upstream BepInEx release. Complete corresponding source, the original LGPL license, modification notes, dependency provenance and available dependency license files are bundled under `notices/loader-source`, `notices/loader-dependencies.json` and `notices/loader-dependency-licenses`. Source is included as ordinary files rather than a nested source archive. This loader targets the Windows game, including Steam Proton; it is not a loader for a native Linux game executable.

## SR-02: writable cache could add unlisted components — resolved

Previously, extraction checked listed files, while installation independently enumerated the cache and calculated fresh hashes. An extra local patcher could therefore enter the installed file list.

The release now:

- Retains an immutable component inventory derived from the embedded manifest through preparation and installation; it never trusts a cache-side manifest.
- Rejects unlisted cached files. Preparation preserves the old cache in a separate `.preserved-…` folder and rebuilds only from the embedded ZIP.
- Revalidates cached components before installation and checks staged replacements against the original trusted hashes before promoting them.
- Rejects a listed file changed after preparation, including a change made between validation and copying.
- Safely resumes known extraction temporaries. It removes a stale temporary directory entry and creates a new file atomically, avoiding truncation through a pre-existing hard link.
- Rejects manifest paths colliding with extraction temporary names, alongside existing traversal, duplicate, size, hash and linked-folder guards.

The fix applies to Windows and Linux installation paths and the legacy development installation method. It is not a sandbox against an attacker already controlling the user account or executable.

Runtime updates also back up and retire unchanged receipt-owned files absent from the new runtime. Externally changed files and unrelated files are preserved. An interrupted operation retains recovery information.

## Verification performed

- **713 checks passed** against the actual bundled payload, covering installer safeguards, fresh installation, migration, interrupted setup, repair, removal, Proton discovery/save selection, new cache regressions and gameplay rule checks.
- The actual Windows EXE recovered from an injected **benign, non-executable test sentinel** in its extraction cache. The file was preserved separately and never installed.
- The final EXE repaired a damaged cache containing a stale hard-linked extraction temporary without changing the independent linked file.
- **429 payload files** passed length/hash verification. The Linux archive's **191 files**, executable permissions, ELF bytes and payload were checked. Neither distribution contains personal saves, account unlocks, generated game bindings or the development helper.
- The actual Windows installer upgraded the existing installation, retired obsolete owned runtime files, and reported **Ready to play**.
- Gatekeeper successfully generated fresh bindings after the old binding folder was set aside. The rebuilt gameplay plugin also compiled against those fresh bindings.
- In-game checks exercised Forge selection, primary fire, turret deployment, and controller-style held grenade/shield aiming followed by release. Runtime status reported no Forge, asset, combat or Aspect errors. A final launch loaded only the two production plugins on .NET 10.0.14.
- A short firing-effect comparison measured approximately 10.35 ms with effects, 10.39 ms without, and 10.25 ms with effects again. This checks that effect path only; it is not a controlled old-runtime/new-runtime performance comparison or a 144 FPS guarantee.
- Microsoft Defender custom scans reported **no threats**, both for the packaged installers and again with Windows/Linux managed bundles and the payload extracted for scanning. Remediation was disabled for these read-only scans.
- All **22 saved-player files** and **13 configuration files** were restored byte for byte to the fresh pre-test backup. Earned unlock data was preserved. The development helper was removed, and the game was closed.
- Published 0.15.4 Windows EXE, uploaded 7z and Linux archive hashes remain unchanged, preserving the original audit evidence.

Native Linux execution, Proton gameplay on a Linux machine, live peer co-op, a full playthrough, and independent antivirus-laboratory certification were not performed. The executable remains unsigned. Nexus Mods must assess this new build independently; these fixes do not guarantee automatic approval or clear the old upload's quarantine.

## Release hashes

| File | SHA-256 |
|---|---|
| Gatekeeper.ForgeExpansion-Setup-v0.15.5.exe | `50fd12e47692c87d803a612a859d5c9f838fa445fbdb2d4901c11c72f1131811` |
| Gatekeeper.ForgeExpansion-Setup-v0.15.5.zip | `9379e0c446dabed2a79cd5aba49f8c2c90c1b9e56e4e997e68ad80084f3279f0` |
| Gatekeeper.ForgeExpansion-Linux-v0.15.5.tar.gz | `71538e45c66bf6afb3459e9c0a8409043ad5315bd4af556c55f758a51a283b65` |

Build with `build-portable.ps1`, then `build-linux.ps1`. The pinned loader sources are in `vendor/BepInEx`; runtime provenance is in `packages/runtime.source.json`. Local execution evidence is retained under the task referenced by `art/security-fixes-current.txt`; that private folder includes test backups and is not a player distribution.
