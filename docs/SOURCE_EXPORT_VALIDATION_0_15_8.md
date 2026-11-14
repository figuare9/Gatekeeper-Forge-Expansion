# Source publication validation — 0.15.8 (November 13, 2026)

This update publishes installer/gameplay 0.15.8 for Nexus review. It preserves tag `v0.15.5-source` and the original 0.15.4 historical snapshot. [Earlier publication validation](SOURCE_EXPORT_VALIDATION.md) remains a dated historical record.

## Source and history

- All **156** first-party files under `src` match the active 0.15.8 production workspace byte for byte. `source-manifest.json` records their sizes/hashes, and `verify-source.py` checks complete coverage.
- The preserved **94** original 0.15.4 source/project files and **61** shared runtime asset inputs still match their existing historical manifests. No models, textures, icons or audio/music inputs changed in this update.
- The vendored modified BepInEx source, its four NuGet lock documents, production build scripts, declared dependency pins and Unity support ZIP match the prior/production inputs. The three Unity reference DLLs already supplied under the upstream loader source's `lib` folder are unchanged; they are common upstream build inputs, not locally generated Gatekeeper bindings. Root review documentation, player notes, current news and release identity were updated separately.
- Production files are preserved as raw bytes. A few files changed line endings during development; the whitespace-insensitive Git comparison highlights substantive changes.

## Isolated source build

The public source inputs were copied into a fresh isolated ignored workspace. No prebuilt custom mod DLL or old portable payload was used as an input.

- Existing hash-pinned SDK 8.0.427 / SDK 10.0.403 installations and the local NuGet cache were reused. Fresh SDK downloads on a replacement OS were not retested.
- Bootstrap verified the original public BepInEx base archive, downloaded and verified the pinned official .NET 10.0.14 runtime archive, and verified the included common Unity support ZIP. It built the modified managed loader from the supplied vendor source with locked dependency restore. Bootstrap's repeat invocation also passed.
- Local generated reference assemblies from the legitimate pinned game were supplied privately, outside tracked source. No proprietary game executables, metadata or generated references are published.
- **744 normal checks passed. 756 actual-payload checks passed for Windows, and 756 passed again for Linux packaging.** Both gameplay plugins, the loader and installer projects compiled; self-contained Windows publish and native Linux cross-publish/package completed.
- All **432** rebuilt payload entries passed size/hash verification. The rebuilt Windows EXE and Linux ELF embed the identical new payload. No player state, generated bindings or development helpers entered it.
- Existing loader XML/dependency/net6 compatibility and assembly-reference warnings remain; there were no build errors.

## Production-workspace assembly comparison

The supplied read-only `tools/AssemblyCompare` helper compares normalized method bodies, local variable types, exception regions and embedded resource hashes. It does not load Unity or execute the compared assemblies. [Machine-readable results](BUILD_VERIFICATION_0_15_8.json) record this check.

| Assembly | Method bodies matched | Resource result |
|---|---:|---|
| Gatekeeper.CustomMods | 2,042 | All 60 embedded resources match |
| Gatekeeper.SubtleBalance | 34 | No embedded resources |
| Manager.Core | 1,154 | All 9 resources match |
| Gatekeeper.ModManager | 170 | Only rebuilt embedded Payload.zip differs |
| Gatekeeper.ForgeExpansion.Linux | 51 | Only rebuilt embedded Payload.zip differs |

The comparison uses production-workspace managed assemblies and the isolated rebuild. Original distribution identities are recorded in [SECURITY_REVIEW](../SECURITY_REVIEW.md). PE/PDB build metadata, paths and ZIP timestamps can change binary hashes. These results are not a claim of bit-for-bit reproducibility, an extraction audit of every single-file installer module or independent security certification. Rebuilt payload differences are reported rather than hidden; its complete inventory and source inputs were checked separately.

## Limits and privacy

No game was launched, installed expansion changed, player save restored, new antivirus scan performed or certificate obtained for this source publication. The latest zero-health turret report was not replayed live; live co-op and Linux gameplay remain separate playtests. The unsigned EXE still requires Nexus's independent review.

The proposed 485-file publication passed checks for generated outputs, credentials and private profile paths. Only source, required build inputs, dependency provenance/licenses and review documentation are committed. Private build outputs/logs, local SDK/reference links, player data, configuration, receipts, account credentials and proprietary game-owned files are excluded. The source update does not rewrite the historical tag or claim that old binaries contain later changes.
