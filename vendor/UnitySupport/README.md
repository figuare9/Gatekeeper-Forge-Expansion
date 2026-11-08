# Pinned Unity reference libraries

`2022.3.62.zip` is the unchanged common Unity reference-library ZIP supplied through BepInEx's Unity library service. Its upstream URL and SHA-256 are pinned in the root dependency lock. This is the same support ZIP shipped by the mod; it is not generated Gatekeeper interop or a copy of the game's native binaries.

It is included to make the documented build independent of that service's occasional HTTP 403/download failures. Bootstrap verifies its hash before use. These third-party reference binaries retain their original rights; the project's MIT code license does not relicense Unity components. See the root third-party notices.
