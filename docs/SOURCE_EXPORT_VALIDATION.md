# Source publication validation — November 8, 2026

This repository is a publication snapshot for Nexus review, not a reconstruction of earlier Git commit history. Current installer 0.15.5 / gameplay 0.15.4 / protocol 12 is distinct from the quarantined 0.15.4 upload.

- All 156 current first-party source/project/asset files matched the active 0.15.5 workspace byte for byte, including their staged Git blob IDs. `source-manifest.json` records their hashes.
- All 94 preserved original 0.15.4 source/project files matched the earlier review's recorded hashes and staged Git blobs. All 61 shared runtime assets matched the pre-security-update workspace; original build scripts/news and dependency provenance were recovered separately.
- Private-data screening of the publication found no saved-player/config/receipt data, credentials, personal email or private profile paths. The two account-shaped constants in ForgeUnlockChecks are synthetic isolation-test fixtures, not copied account progress.
- The current source was built in an isolated ignored workspace, using the declared original BepInEx base archive and official .NET 10.0.14 runtime. Runtime archive hashes were verified; no prebuilt custom mod DLL or old portable payload was used as a build input. Existing pinned SDK installations and NuGet cache were reused. Fresh SDK download on a replacement OS was not retested.
- Normal checks: **701 passed**. Checks against the newly built payload: **713 passed**. Linux packaging reran **713 checks** successfully.
- Both gameplay plugins and the modified loader compiled; Windows self-contained publish and Linux cross-publish/package completed. Existing upstream/net6 compatibility and assembly-reference warnings remain. These results are not a native Linux gameplay test.
- The first bootstrap attempt exposed an upstream Unity library HTTP 403. The unchanged 1.8 MiB support ZIP is now included with a verified digest and explicit third-party provenance. Bootstrap also creates the release output folder required by the original production scripts; the completed Windows publish was then copied through the original final publish step. Bootstrap's repeat invocation was checked.
- The legacy preparation helper was exercised through source/hash validation and input staging with SDK copying skipped. The legacy source was not rebuilt/relaunched during this publication check; its exact-build evidence is historical and separately identified.
- No live game was launched, installed plugin changed, save restored, new antivirus scan performed, or independent security certification obtained for this source publication.

Build paths/ZIP metadata can change output hashes. Published original binary hashes in SECURITY_REVIEW identify the previously tested distributions, not a guarantee that every rebuild will be byte-identical. Nexus must review the relevant release independently.
