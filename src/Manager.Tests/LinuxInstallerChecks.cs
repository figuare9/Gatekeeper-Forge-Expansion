using System.IO.Compression;
using Gatekeeper.ModManager;

internal static class LinuxInstallerChecks
{
    public static void Run(Action<bool, string> check, Action<Action, string> fails)
    {
        using var f = new Fixture();
        var home = Path.Combine(f.Manager, "linux home");
        var steam = Path.Combine(home, ".local/share/Steam");
        var flatpak = Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam");
        var external = Path.Combine(f.Manager, "Other Steam Library");
        Directory.CreateDirectory(Path.Combine(steam, "steamapps")); Directory.CreateDirectory(Path.Combine(external, "steamapps"));
        string Vdf(string path) => path.Replace("\\", "\\\\").Replace("\"", "\\\"");
        File.WriteAllText(Path.Combine(steam, "steamapps/libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"" + Vdf(steam) + "\" } \"1\" { \"path\" \"" + Vdf(external) + "\" } }");
        var candidates = LinuxSteam.RootCandidates(home);
        check(candidates.Contains(steam) && candidates.Contains(flatpak), "Linux discovery missed desktop or Flatpak Steam.");
        check(candidates.Contains(Path.Combine(home, ".steam/root")), "Linux discovery missed Steam's legacy link location.");
        check(LinuxSteam.RootCandidates(home, external).Contains(Path.Combine(external, "Steam")), "Linux discovery ignored XDG_DATA_HOME.");
        check(LinuxSteam.AbsoluteOr("relative/path", home) == home, "A relative XDG environment variable changed the data location.");
        check(LinuxSteam.Libraries(candidates).Contains(external), "Linux Steam discovery missed an external library with spaces.");
        var game = Path.GetFullPath(Path.Combine(external, "steamapps/common/Gatekeeper")); Directory.CreateDirectory(game);
        foreach (var name in new[] { "Gatekeeper.exe", "GameAssembly.dll", GameService.MetadataFile })
        { var target = Path.Combine(game, name); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(f.Game, name), target); }
        File.WriteAllText(Path.Combine(external, "steamapps/appmanifest_2106670.acf"), "\"AppState\" { \"appid\" \"2106670\" \"installdir\" \"Gatekeeper\" }");
        check(LinuxSteam.Find(candidates).SequenceEqual(new[] { game }), "Linux discovery did not select Gatekeeper from the external Steam library.");
        fails(() => LinuxSteam.ResolveSaveRoot(game, candidates), "An uninitialized Proton prefix silently skipped save discovery.");
        var prefix = Path.Combine(external, "steamapps/compatdata/2106670/pfx");
        var saves = Path.GetFullPath(Path.Combine(prefix, "drive_c/users/steamuser/AppData/LocalLow/Gravity Lagoon/Gatekeeper"));
        Directory.CreateDirectory(Path.Combine(saves, "Saves")); File.WriteAllText(Path.Combine(saves, "Saves/test-run"), "proton-progress");
        check(LinuxSteam.ResolveSaveRoot(game, candidates) == saves, "The Linux installer selected Windows host saves instead of the game's Proton saves.");
        check(LinuxSteam.ResolveSaveRoot(game, candidates, prefix) == saves, "Explicit Proton-prefix selection was ignored.");
        fails(() => LinuxSteam.ResolveSaveRoot(game, candidates, home), "An invalid explicit Proton prefix was accepted.");
        var otherPrefix = Path.Combine(steam, "steamapps/compatdata/2106670/pfx");
        var otherSaves = Path.Combine(otherPrefix, "drive_c/users/steamuser/AppData/LocalLow/Gravity Lagoon/Gatekeeper/Saves");
        Directory.CreateDirectory(otherSaves); File.WriteAllText(Path.Combine(otherSaves, "old-run"), "other-progress");
        fails(() => LinuxSteam.ResolveSaveRoot(game, candidates), "Two active Proton save locations silently backed up the wrong saves.");
        check(LinuxSteam.ResolveSaveRoot(game, candidates, prefix) == saves, "An explicit prefix did not resolve ambiguous Proton saves.");
        File.WriteAllText(Path.Combine(f.Manager, "packages/mods/Gatekeeper.CustomMods.dll"), "linux-test-content");
        var service = new GameService(game, Path.Combine(f.Manager, "linux-state"), () => false, saves, f.Manager)
        { ExpectedAssembly = GameService.Hash(Path.Combine(game, "GameAssembly.dll")), ExpectedMetadata = GameService.Hash(Path.Combine(game, GameService.MetadataFile)) };
        service.InstallExpansion();
        check(service.ExpansionState().Stage == ExpansionStage.Ready && service.LoaderEnabled, "The Linux-selected game did not receive the full enabled package.");
        var backup = Directory.GetFiles(Path.Combine(service.ManagerRoot, "backups"), "*.zip").Single();
        using (var zip = ZipFile.OpenRead(backup))
            check(zip.GetEntry("Saves/test-run") != null && zip.GetEntry("Saves/progress") == null, "The Linux installation backed up a host save rather than the selected Proton save.");
        check(File.ReadAllText(Path.Combine(saves, "Saves/test-run")) == "proton-progress", "Linux installation modified the original Proton save.");
        service.InstallExpansion();
        check(service.ExpansionState().Stage == ExpansionStage.Ready, "A second Linux installation could not repair/update its own receipt.");
        check(service.Uninstall().Count == 0 && File.ReadAllText(Path.Combine(saves, "Saves/test-run")) == "proton-progress", "Linux removal changed save data or could not remove its own files.");
        var proc = Path.Combine(f.Manager, "proc"); Directory.CreateDirectory(Path.Combine(proc, "123"));
        File.WriteAllText(Path.Combine(proc, "123/comm"), "steam\n");
        check(!LinuxSteam.GameRunning(proc), "Steam itself blocked Linux installation as a running Gatekeeper game.");
        File.WriteAllText(Path.Combine(proc, "123/comm"), "Gatekeeper.exe\n");
        check(LinuxSteam.GameRunning(proc), "The Linux installer missed the active Windows game process under Proton.");
        File.WriteAllText(Path.Combine(proc, "123/comm"), "Gatekeeper\n");
        check(LinuxSteam.GameRunning(proc), "The Linux installer missed a shortened game process name.");
        fails(() => LinuxSteam.GameRunning(Path.Combine(proc, "missing")), "An unavailable Linux process list permitted an unsafe installation.");
        check(GameService.Inside(f.Manager, "packages\\mods\\Gatekeeper.CustomMods.dll") == Path.GetFullPath(Path.Combine(f.Manager, "packages/mods/Gatekeeper.CustomMods.dll")), "Windows receipt path separators were not normalized.");
        fails(() => GameService.Inside(f.Manager, "..\\outside"), "A Windows-style relative path escaped the selected directory.");
        check(LinuxSteam.LaunchOptions.Contains("winhttp.dll=n,b") && LinuxSteam.LaunchOptions.Split("%command%").Length == 2, "The Proton setup omitted the native loader override or duplicated the game command.");
        if (OperatingSystem.IsLinux())
        {
            var alias = Path.Combine(home, "steam-alias"); Directory.CreateSymbolicLink(alias, steam);
            check(LinuxSteam.PhysicalPath(alias) == steam, "Steam's linked root did not resolve to its physical installation.");
            fails(() => GameService.Inside(Path.Combine(f.Manager, "lower"), "../LOWER/elsewhere"), "A case-changing traversal escaped a Linux folder.");
            check(PortablePackage.Key(Path.Combine(f.Manager, "game")) != PortablePackage.Key(Path.Combine(f.Manager, "GAME")), "Linux games whose paths differ by case share an installation record.");
            Directory.Delete(alias);
        }
    }
}
