# Code supplied for review

These are the original first-party C# sources and project files associated with the exact reproduced 0.15.4 release. They include the existing checks. No hardening candidate has been applied to this code; its proposed diff is supplied separately.

This is a code-inspection snapshot, not the complete build workspace. It excludes embedded model/texture/audio assets, BepInEx/runtime binaries, generated game bindings, and local player data. The original complete workspace and its assets were used for the exact rebuild. Assets are required to rebuild the gameplay DLL and must match their original hashes; game bindings are generated from the recipient's legitimate supported Gatekeeper installation. Do not distribute those private bindings.

Toolchain: Microsoft .NET SDK 8.0.427. Gameplay target: net6.0 with the pinned BepInEx build; installer target: net8.0 with Microsoft runtime 8.0.33. The report explains why those runtime targets need maintenance. Do not infer that net6.0 is a recommended target for a new release.

Rebuild steps used against the complete workspace:

1. Rebuild GameMods.csproj and SubtleBalance.csproj in Release into separate output folders using -t:Rebuild.
2. Rebuild Manager.csproj in Release with PortablePayload=true, then publish that same build with --no-build. The embedded portable-payload.zip is a required input and its hash is in release-hashes/provenance evidence.
3. Rebuild Manager.Linux.csproj in Release with LinuxBundle=true, then publish the same build with --no-build.
4. Compare SHA-256 of each result to reproducible-build-results.json. Every comparison was exact in this review, using the same Windows machine and SDK family.
5. Run Manager.Tests in Release with --payload pointing at that verified payload. The original and isolated cache-hardening candidate each passed 694 checks.

Read SECURITY_REVIEW.md for scope and limits. A reviewer needing every asset/build input should request the complete sanitized build workspace rather than attempting to compile this code-only snapshot by itself.
