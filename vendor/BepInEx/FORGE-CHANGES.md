# Forge Windows / Proton runtime compatibility build

Modified from BepInEx commit `b782da4ac54e1072d2857f4a2f77c80c3ef08005` (build 791), November 7, 2026. This is a Forge-maintained compatibility build, **not an official BepInEx release**. Original copyrights and LGPL-2.1 license remain applicable. Complete corresponding source, including the original Unity reference libraries needed to compile it, is provided in this folder.

Changes:

- Upgrade HarmonyX to 2.16.1, MonoMod.RuntimeDetour to 25.3.6 and MonoMod.Utils to 25.0.14, with their resolved dependencies.
- Adapt retired MonoMod platform APIs through `BepInEx.Core/ForgePlatformCompatibility.cs`; retain BepInEx's Windows/Wine detection.
- Target only the Windows IL2CPP loader used by Gatekeeper on Windows and Steam Proton. Exclude the obsolete Unix console implementation; this build is not for native Linux/macOS game executables.
- Adapt the expanded detour interface and use the supported Marshal delegate conversion for native trampolines. Retain the original Dobby/Funchook providers and delegate lifetime protection.
- Use RuntimeInformation for Windows detection in UnityInfo; replace retired ReflectionHelper properties.
- Suppress package generation and embedded debug symbols; identify the build as `6.0.0-forge-runtime.1`.

The upstream loader's complete CoreCLR runtime is replaced separately with Microsoft's .NET 10.0.14 Windows x64 runtime. Game plugins retain their existing public API and gameplay content version 0.15.4. No Gatekeeper binary or generated game interop assembly is part of this source.

Build the IL2CPP project with .NET SDK 10.0.403:

```
dotnet build Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj -c Release -p:SolutionDir=./ -p:BuildDir=../../../../build-net10
```

NuGet package sources are listed in NuGet.Config. The enclosing Forge project supplies `build-loader.ps1` to compile and stage the DLLs. The original Windows Doorstop proxy and Dobby native libraries remain unchanged from the pinned upstream release.
