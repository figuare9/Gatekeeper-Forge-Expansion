# Source changes from 0.15.5 to 0.15.8

Current review snapshot: **installer/gameplay 0.15.8**, co-op protocol **12**. First-party files under `src` are copied byte-for-byte from the production workspace used to build this release; their inventory is in `source-manifest.json`. The production build scripts and required runtime assets are included. The earlier 0.15.5 Git tag and original 0.15.4 historical source are preserved.

## 0.15.6 — Forge discovery at any stage

Removed both the relay-spawn and approach-activation Stage 7 minimums. Active gameplay on Ethereal Bridge remains required. Placement, ground/clearance/line-of-sight checks, animated lever, permanent account unlocks and party-award behavior retain their existing paths. Locked-character text, player instructions and bundled news were updated.

Review `src/Shared/ForgeUnlockRules.cs`, `src/GameMods/ForgeUnlock.cs` and `src/Manager.Tests/ForgeUnlockChecks.cs`.

## 0.15.7 — stage-entry Ascendant snapshots

The host captures team-average build power and character level at combat-stage entry, replacing the former two-second live power sampler. The opening stage has no Ascendant bonus. Through character level 30, the added Ascendant health bonus and extra-elite chance are multiplied by 0.90; the factor rises linearly to 1.00 at level 50. From level 50, the established full-strength functions and caps are returned exactly.

Stage inputs persist in a local `ascendant-stage.json` sidecar so Continue of the same stage does not recalculate them. Run/map/stage/revision and numeric checks reject unrelated or invalid snapshots. Installer save backups include the sidecar. Native difficulty and the separate Scaling Aspect retain independent behavior.

Review `src/Shared/AscendantRules.cs`, `src/GameMods/AscendantDifficulty.cs`, `src/GameMods/AspectGameplay.cs`, `src/Manager.Core/GameService.cs` and `src/Manager.Tests/AscendantRetuneChecks.cs`.

## 0.15.8 — single-player menu and zero-health elites

Aspects register before the initial native menu enumerates its catalog and creates cells. The cell-spawn prefix adds missing custom IDs without duplicates or the private Ascendant flag. Setup can repair an existing stock-only panel before assigning native selections, unlocks and click state. Single-player local transport does not await a remote compatibility handshake; co-op retains matching-peer checks.

The Elite Aspect adds a recovery for accepted authenticated damage that leaves an ordinary elite at zero health without death completion. The host waits 0.5 seconds for the normal confirmation, then rechecks the pending target and invokes the existing virtual native death path with the original sender, owner and damage type. Completion is claimed once per pooled life. Boss phases, training dummies, positive-health/undamageable entities and non-authoritative clients are excluded. This is not a blanket removal of elite affixes or a direct item/currency grant. The reported turret encounter has not been replayed live; this is a targeted recovery for its described symptom.

Review `src/GameMods/AspectRegistry.cs`, `src/GameMods/ForgeLobby.cs`, `src/GameMods/AspectGameplay.cs`, `src/GameMods/ShopBehaviour.cs`, `src/Shared/AspectRules.cs` and the regressions in `src/Manager.Tests/Program.cs`.

## Compatibility and review scope

Forge content revision is `forge-kit-15-8`; Aspect compatibility revision is `expansion-aspects-7`; Ascendant tuning revision is `ascendant-3`. Saved IDs and wire protocol remain unchanged. Co-op peers should update together.

The modified loader source, runtime/dependency inputs and the 0.15.5 cache-verification fixes are unchanged. No runtime model, texture, icon, audio or music asset was changed in 0.15.6–0.15.8. A few source files changed line endings during development; the export preserves the actual production bytes. `git diff --ignore-space-at-eol v0.15.5-source..v0.15.8-source` helps review the substantive changes.

See [current source-build validation](SOURCE_EXPORT_VALIDATION_0_15_8.md), [binary identities and review map](../SECURITY_REVIEW.md) and [build instructions](../BUILD.md). Original distribution hashes identify the local tested 0.15.8 files; a functional rebuild may have different hashes because of build paths and archive metadata.
