# Forge Expansion installer 0.15.5 — desktop Linux testing kit

This kit runs natively on **x86-64 Linux** and installs the complete Windows expansion into **Gatekeeper running through Steam Proton**. It is a separate installer, with exactly the same gameplay plugins/assets as the Windows 0.15.5 release. It is not a port of Gatekeeper to native Linux.

The installer was cross-built and its installation logic checked on Windows. **Linux game launch, audio and co-op still require testing.** Do not describe this package as Linux gameplay verified yet.

## Expansion news

Read the complete developer-style article in `notices/news-forge-complete.md`. The included catalog and other news articles keep the update history together. To print the article without selecting or changing a game, run `bash Install-Forge.sh --news --cli`. Installer and gameplay are 0.15.5, including the revised relay placement; all co-op peers should update together.

## Install

1. Install Gatekeeper in Steam. Run it once through Proton, then close the game.
2. Extract the whole `Gatekeeper.ForgeExpansion-Linux-v0.15.5.tar.gz` archive into a folder you own.
3. Run `Install-Forge.sh` using your file manager's **Run as program** option. If it opens in an editor, open a terminal in that folder and run `bash Install-Forge.sh`.
4. Confirm the detected game location and choose **Install expansion**. Everything is included; there are no optional content switches.
5. In Steam, open **Gatekeeper → Properties → General → Launch Options**. If the box is empty, paste:

   ```text
   WINEDLLOVERRIDES="winhttp.dll=n,b" %command%
   ```

   Preserve any existing options. If `WINEDLLOVERRIDES` already exists, add `winhttp.dll=n,b` to its semicolon-separated value. Do not create two override variables or duplicate `%command%`. The installer deliberately leaves Steam's configuration files alone because Steam may overwrite edits while running.
6. Launch normally from Steam. The first modded launch may take longer while BepInEx generates bindings. Use the same expansion version as your Windows friends: **0.15.5 / protocol 12**.

The script uses Zenity or KDialog when available and otherwise provides terminal prompts. No separate .NET SDK/runtime, Wine installer, protontricks, root access or sudo is required for this installation kit. The system still needs Steam, Proton and the normal system libraries required by a self-contained .NET 8 Linux program. [BepInEx's Proton setup documentation](https://docs.bepinex.dev/master/articles/advanced/steam_interop.html#protonwine) describes the Windows loader and DLL override used here.

Regular Steam, Flatpak Steam and external libraries are detected. Standard `~/.steam` aliases are resolved before installation. Game files and game metadata are checked against the supported build before anything is installed. Unrelated loaders and externally edited managed files are preserved and reported.

## Updates, backups and removal

Forge starts locked. Approach the fixed engineering relay in the west-side service bay off the left-hand route on Celestium's Ethereal Bridge from Stage 7 onward to unlock him for the current party. The account unlock persists in `BepInEx/config/local.gatekeeper.custommods.forge-progress.json`; installation does not grant it. Keep it with your personal saves and Cannon Wheel progress when moving PCs. Both progress files are included beside installer save backups.

Run the kit again to update or repair. It refuses changes while Gatekeeper is running. It uses the same save protection, file ownership checks and interrupted-install recovery as the Windows installer.

Data lives under `${XDG_DATA_HOME:-~/.local/share}/GatekeeperModManager/games/<installation-key>`. Verified package caches live under `${XDG_CACHE_HOME:-~/.cache}/GatekeeperModManager`. Save backups include the game's **Proton** `AppData/LocalLow/Gravity Lagoon/Gatekeeper` save folder, rather than a Windows host folder. Existing managed installations in `GatekeeperModManager/state` are reused when they belong to this game.

From a terminal in the extracted folder:

```sh
bash Install-Forge.sh --inspect --cli
bash Install-Forge.sh --backup --cli
bash Install-Forge.sh --uninstall --cli
```

Removal preserves saves, unlocks, caches, generated configuration, unrelated plugins and backups. Finish an unfinished Forge/Cannon Wheel/custom Aspect run first; removal is blocked while those saves need the expansion. Remove the Forge `winhttp.dll` override from Steam launch options after removal, preserving other entries.

If several game folders or Proton prefixes are found, specify the active ones:

```sh
bash Install-Forge.sh --game-dir "/path/to/steamapps/common/Gatekeeper" --prefix "/path/to/steamapps/compatdata/2106670/pfx"
```

For support, share the error shown by the installer and the game's `BepInEx/LogOutput.log`. Do not share your saves unless you intend to.

Supported game: Steam app **2106670**, build **23057178**, Windows x64 / Unity **2022.3.62f2**, through Proton. The installer blocks other game binaries. This unofficial community expansion does not grant any official Steam DLC entitlements. Bundled notices are in `notices/`.

Installer 0.15.5 retains gameplay content 0.15.4 and co-op protocol 12. It updates bundled runtimes and verifies cached installation components against the embedded manifest.
