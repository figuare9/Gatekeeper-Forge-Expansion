# Third-party components

The manager and Subtle Balance source in `src/` are original project code under the accompanying MIT license. Gatekeeper and its assets remain the property of their respective owners; no game binary or game asset is included as part of the manager's source.

This local workspace includes third-party development tools and the official BepInEx runtime distribution. Their original licenses and notices apply independently:

The single-file edition also bundles the Microsoft .NET Windows desktop runtime and the unmodified Unity 2022.3.62 managed reference-library ZIP obtained through BepInEx's Unity library service. The reference libraries are common Unity support libraries; game-derived interop/dump assemblies and Gatekeeper binaries are excluded. The loader generates game bindings on the recipient's machine. Embedded instructions and notices are accessible through **Backups & setup → Readme & licenses**.

The four slot sound recordings and seven Forge DLC music tracks were supplied by the user and processed locally; the project code license does not relicense those recordings or songs. FFmpeg was used as an existing development tool and is not included in the manager package. Playback uses the FMOD runtime already shipped with Gatekeeper; no FMOD runtime binary is redistributed by this mod.

- **BepInEx**, LGPL-2.1: https://github.com/BepInEx/BepInEx/blob/master/LICENSE
- **UnityDoorstop**: https://github.com/NeighTools/UnityDoorstop
- **HarmonyX**: https://github.com/BepInEx/HarmonyX
- **Il2CppInterop** and included dependencies: https://github.com/BepInEx/Il2CppInterop
- **Cpp2IL / LibCpp2IL**: https://github.com/SamboyCoding/Cpp2IL
- **.NET runtime and SDK**, MIT and included third-party notices: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- **Il2CppDumper**, MIT: https://github.com/Perfare/Il2CppDumper/blob/master/LICENSE
- **Capstone**, development-only native code inspection: https://github.com/capstone-engine/capstone

The BepInEx package is available from https://builds.bepinex.dev/projects/bepinex_be. The exact downloaded archive URL and local digest are recorded under `packages/*.source.json`. The .NET SDK download was checked against Microsoft's published SHA-512 hash.

Installation kit 0.15.5 replaces the bundled runtime with Microsoft's complete .NET 10.0.14 Windows x64 runtime and uses .NET 10.0.14 for both self-contained installers. It includes a **modified, Forge-maintained BepInEx compatibility build** based on commit b782da4ac54e1072d2857f4a2f77c80c3ef08005, with HarmonyX 2.16.1, MonoMod.RuntimeDetour 25.3.6, MonoMod.Utils 25.0.14 and Microsoft.Extensions.Logging 10.0.14. This is not an official upstream BepInEx release. Complete corresponding loader source, dependency declarations, changes and the original LGPL-2.1 license are bundled as ordinary files under `notices/loader-source`. The Windows/Proton loader and its native proxy remain separate from the native Linux installer.

Keep upstream licenses, notices, and source availability obligations when redistributing dependencies. Do not include local save backups, installation state, or game-derived dump/interop files in a public package.
