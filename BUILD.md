# Build the application from source

## Scope and requirements

Build on Windows x64 with PowerShell 7, Python 3.10+ and network access for the initial dependency download/restore. `bootstrap.py` uses only Python's standard library. It obtains SDK **8.0.427** (net6 gameplay compiler), SDK **10.0.403** (installer/loader), the pinned upstream BepInEx base distribution, official runtime **10.0.14**, and the pinned Unity support ZIP (included under `vendor/UnitySupport` to avoid upstream download failures). URLs and SHA-512/SHA-256 values are in `dependencies.lock.json`; SDK/runtime digests came from Microsoft's release metadata. NuGet dependencies and exact resolved versions are in `vendor/BepInEx/**/packages.lock.json`.

Compilation of gameplay requires generated IL2CPP reference assemblies from your own legitimate **Gatekeeper Steam build 23057178**, Unity 2022.3.62f2. Neither owning this source nor installing the community expansion grants official game/DLC entitlements. The installer/pure-rule tests can run without game files; full gameplay compilation requires local bindings.

## 1. Get the source and prepare dependencies

Clone into a writable folder. Keep this layout for the existing relative project references:

```text
ReviewWorkspace/
  BepInEx/
    interop/                    # locally generated references only
    unity-libs/2022.3.62.zip
  Gatekeeper-Forge-Expansion/
    src/
    vendor/BepInEx/
    tools/dotnet/dotnet.exe      # SDK 8
    tools/dotnet10/dotnet.exe    # SDK 10
    packages/loader/
```

From the source root, in PowerShell 7:

```powershell
python -B ./bootstrap.py
./build-loader.ps1
```

Bootstrap verifies downloaded archives and the included Unity support ZIP before extracting or using them. It stages only public dependency files and a shared Unity support ZIP in the parent workspace; it does not launch the game, install plugins, edit saves or request account credentials. Existing different/unmarked loader staging is rejected instead of being silently deleted. Use a fresh checkout to change dependency versions. SDK folders can be reused if they contain the exact pinned versions; `--skip-sdks` requires those existing folders.

`build-loader.ps1` builds the modified loader from included source with `RestoreLockedMode=true`, stages its managed output, and generates dependency/source/license notices. Native Doorstop/detour components come from the hash-pinned upstream base distribution. The complete Windows CoreCLR runtime comes from Microsoft's pinned archive; no custom runtime binary is compiled here.

## 2. Run the code and installer checks without a game

```powershell
./tools/dotnet10/dotnet.exe run --project ./src/Manager.Tests/Manager.Tests.csproj -c Release
```

The regular suite has 701 checks at this snapshot. Installer tests use synthetic game/save fixtures. They check path/ownership safety, transactional installs, recovery/removal, Linux/Proton discovery, owner-scoped multiplayer rules, pricing and pure gameplay rules; they do not replace native gameplay tests.

## 3. Generate local game reference assemblies

Use the loader built in step 1 against a separate, legally installed copy of the pinned game. Close Gatekeeper, back up any existing loader/config/saves, then stage the contents of `packages/loader` beside that game's executable. Do not overwrite a different managed loader installation blindly. This installs the loader only; the custom mod DLLs have not yet been compiled. Launch Gatekeeper once through Steam and quit after BepInEx completes generation. Read `BepInEx/LogOutput.log` for success and diagnostics.

Copy the generated `BepInEx/interop` folder from that test installation into `ReviewWorkspace/BepInEx/interop`, adjacent to this source checkout. Bootstrap has already provided the adjacent `BepInEx/unity-libs/2022.3.62.zip`. The existing `.csproj` references deliberately use `../../../BepInEx/interop/*.dll` from `src/GameMods` and `src/SubtleBalance`. Alternatively, a checkout inside the test game's directory can use those adjacent folders directly, but a separate review workspace makes the boundary easier to inspect.

Do not commit or redistribute the generated bindings, game executable, GameAssembly.dll, native metadata, saves or config. These inputs are intentionally absent from this repository.

## 4. Build the Windows installer

```powershell
./build-portable.ps1
```

This rebuilds the compatibility loader, runs the normal checks, compiles both gameplay plugins, builds a manifest-bound payload, runs the checks with that actual payload, and publishes the self-contained Windows EXE:

```text
release/Gatekeeper.ForgeExpansion-Setup-v0.15.5.exe
```

The actual-payload suite has 713 checks. `build.ps1` alone creates a development publish. Neither build script installs the expansion or launches the game. `build-portable.ps1` may fetch a missing upstream license; bootstrap and NuGet restore also use the network. The player installer itself uses embedded components instead of downloading the expansion at install time.

## 5. Cross-publish the desktop Linux installer

After the Windows payload exists:

```powershell
./build-linux.ps1
```

Output: `release/Gatekeeper.ForgeExpansion-Linux-v0.15.5.tar.gz`. It reuses the same payload, reruns payload checks, publishes a native Linux x64 installer and adds executable modes in the TAR. It targets the Windows game under Steam/Flatpak Steam Proton, not a native Linux Gatekeeper build. Cross-publishing does not prove Linux gameplay compatibility.

## Release comparison and older version

Published original 0.15.5 hashes are in `SECURITY_REVIEW.md`. A functional rebuild may have different hashes because of build paths, assembly debugging information, ZIP timestamps/ordering or packaging. Do not claim bit-for-bit reproducibility without controlling and comparing all inputs. Source snapshots and asset inventories establish what is supplied for review; the binary hashes identify the originally tested installer.

The original quarantined file was **0.15.4**, using different runtime/dependency and cache-handling code. `historical/installer-0.15.4` preserves its exact first-party code snapshot and recorded hashes. Read its separate guide before evaluating the old upload. Current `main` is the corrected 0.15.5 build and is not a claim that the quarantined 0.15.4 binary contains those fixes.
