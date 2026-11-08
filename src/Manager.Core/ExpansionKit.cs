namespace Gatekeeper.ModManager;

public static class ExpansionKit
{
    public const string Version = "0.15.5";
    public const string ContentVersion = "0.15.4";
    public const string Name = "Gatekeeper · Forge Expansion";

    // One tested content package for every player. Legacy balance sliders are
    // intentionally left at native combat values, as in the creator's profile.
    public static BalanceProfile Profile() => new()
    {
        Enabled = true, Metalheads25 = false, QuietBayShop = true,
        PrismJackpot = true, CannonWheel = true, Forge = true,
        Movement = false, Damage = false, Cooldown = false,
        MovementPercent = 5, DamagePercent = 5, CooldownPercent = 5
    };
}

public enum ExpansionStage { NotInstalled, UpdateRequired, RepairRequired, Ready }
public sealed record ExpansionStatus(ExpansionStage Stage, string Detail);
public sealed record ExpansionReceipt(string Version, string ContentVersion, string[]? SharedFiles = null);
public sealed record InstallProgress(int Percent, string Message);

public sealed partial class GameService
{
    private string ExpansionReceiptPath => Inside(ManagerRoot, "state/expansion.json");
    private sealed record KitFile(string Source, string Relative, string Sha256);

    private List<KitFile> KitFiles(bool requireCustom = true)
    {
        if (verifiedPackage != null)
        {
            verifiedPackage.Validate();
            var verifiedFiles = verifiedPackage.Files.Where(f => f.Path.StartsWith("packages/loader/", StringComparison.Ordinal) ||
                    f.Path is "packages/mods/Gatekeeper.SubtleBalance.dll" or "packages/mods/Gatekeeper.CustomMods.dll")
                .Select(f => new KitFile(Inside(PackageRoot, f.Path), f.Path.StartsWith("packages/loader/", StringComparison.Ordinal)
                    ? f.Path["packages/loader/".Length..] : f.Path.EndsWith("/Gatekeeper.CustomMods.dll", StringComparison.Ordinal) ? CustomPlugin : Plugin, f.Sha256))
                .Where(f => f.Relative.StartsWith("BepInEx/", StringComparison.Ordinal) || f.Relative.StartsWith("dotnet/", StringComparison.Ordinal) ||
                    f.Relative is Proxy or "doorstop_config.ini" or ".doorstop_version").ToList();
            if (!verifiedFiles.Any(f => f.Relative == Proxy) || !verifiedFiles.Any(f => f.Relative == "BepInEx/core/BepInEx.Unity.IL2CPP.dll") ||
                !verifiedFiles.Any(f => f.Relative == Plugin) || requireCustom && !verifiedFiles.Any(f => f.Relative == CustomPlugin))
                throw new InvalidDataException("This verified installation kit is incomplete. Download it again.");
            return verifiedFiles;
        }
        var loader = Inside(PackageRoot, "packages/loader");
        var files = SafeFiles(loader).Select(source => (Source: source,
            Relative: Path.GetRelativePath(loader, source).Replace('\\', '/')))
            .Where(f => f.Relative.StartsWith("BepInEx/", StringComparison.Ordinal) ||
                f.Relative.StartsWith("dotnet/", StringComparison.Ordinal) ||
                f.Relative is Proxy or "doorstop_config.ini" or ".doorstop_version").ToList();
        files.Add((Inside(PackageRoot, "packages/mods/Gatekeeper.SubtleBalance.dll"), Plugin));
        var custom = Inside(PackageRoot, "packages/mods/Gatekeeper.CustomMods.dll");
        if (requireCustom || File.Exists(custom)) files.Add((custom, CustomPlugin));
        if (!files.Any(f => f.Relative == Proxy) ||
            !files.Any(f => f.Relative == "BepInEx/core/BepInEx.Unity.IL2CPP.dll") ||
            files.GroupBy(f => f.Relative, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new InvalidDataException("This installation kit is incomplete. Download the complete EXE again.");
        return files.Select(f => new KitFile(f.Source, f.Relative, Hash(f.Source))).ToList();
    }

    public ExpansionStatus ExpansionState()
    {
        var receipt = Receipt();
        if (receipt == null) return new(ExpansionStage.NotInstalled, "Gatekeeper found. Ready to install the complete expansion.");
        if (!receipt.Complete) return new(ExpansionStage.RepairRequired, "An earlier installation was interrupted. Resume to finish installing.");
        var kit = Storage.Read<ExpansionReceipt>(ExpansionReceiptPath);
        foreach (var file in KitFiles())
        {
            var owned = receipt.Files.FirstOrDefault(f => f.RelativePath.Replace('\\', '/').Equals(file.Relative, StringComparison.OrdinalIgnoreCase));
            var relative = file.Relative == Proxy && !LoaderEnabled ? DisabledProxy : file.Relative;
            var path = Inside(Root, relative);
            var shared = kit?.SharedFiles?.Contains(file.Relative, StringComparer.OrdinalIgnoreCase) == true;
            if (owned == null && !shared || owned != null && owned.Sha256 != file.Sha256)
                return new(ExpansionStage.UpdateRequired, "An earlier package is installed. Update to the complete expansion.");
            if (!File.Exists(path) || Hash(path) != file.Sha256)
                return new(ExpansionStage.RepairRequired, "An expansion file is missing or changed. Check and repair the installation.");
        }
        if (kit?.Version != ExpansionKit.Version)
            return new(ExpansionStage.UpdateRequired, "Your existing installation is ready to update to this installation kit.");
        var profile = ExpansionKit.Profile();
        if (!LoaderEnabled || File.Exists(Inside(Root, DisabledProxy)) ||
            !ConfigMatches(Inside(Root, $"BepInEx/config/{PluginId}.cfg"), profile.Config()) ||
            !ConfigMatches(Inside(Root, $"BepInEx/config/{CustomPluginId}.cfg"), profile.CustomConfig()))
            return new(ExpansionStage.RepairRequired, "Complete the setup to enable the full expansion.");
        return new(ExpansionStage.Ready, "The complete expansion is installed and ready to play.");
    }

    private static bool ConfigMatches(string path, string expected)
    {
        if (!File.Exists(path)) return false;
        static Dictionary<string, string> Entries(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var section = "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or ';') continue;
                if (line.StartsWith('[') && line.EndsWith(']')) { section = line; continue; }
                var equals = line.IndexOf('=');
                if (equals > 0) result[section + line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
            return result;
        }
        var actual = Entries(File.ReadAllText(path));
        return Entries(expected).All(p => actual.TryGetValue(p.Key, out var value) && value.Equals(p.Value, StringComparison.OrdinalIgnoreCase));
    }

    public void InstallExpansion(IProgress<InstallProgress>? progress = null)
    {
        RequireClosed();
        progress?.Report(new(5, "Checking Gatekeeper and the included files…"));
        var info = Inspect();
        if (!info.Supported) throw new InvalidOperationException("This Gatekeeper version is not supported by this installation kit. No files were installed.");
        var files = KitFiles();
        var receipt = Receipt();
        var installedKit = Storage.Read<ExpansionReceipt>(ExpansionReceiptPath);
        if (System.Version.TryParse(installedKit?.Version, out var installedVersion) && installedVersion > System.Version.Parse(ExpansionKit.Version))
            throw new InvalidOperationException("A newer expansion is already installed. Use its installation kit.");
        if (receipt == null && (File.Exists(Inside(Root, Proxy)) || File.Exists(Inside(Root, DisabledProxy)) ||
            File.Exists(Inside(Root, "version.dll")) || File.Exists(Inside(Root, "doorstop_config.ini")) || Directory.Exists(Inside(Root, "MelonLoader"))))
            throw new InvalidOperationException("Another mod loader is already installed. This kit will not overwrite it.");
        if (File.Exists(Inside(Root, Proxy)) && File.Exists(Inside(Root, DisabledProxy)))
            throw new IOException("Two loader copies were found. Open Support and check the installation before continuing.");
        if (receipt != null) foreach (var owned in receipt.Files) Inside(Root, owned.RelativePath);
        var previous = receipt is { Complete: false } ? Storage.Read<InstallReceipt>(ReceiptPath + ".previous") : null;
        if (previous != null && !Path.GetFullPath(previous.GameRoot).Equals(Root, PathIdentity.Comparison))
            throw new InvalidDataException("The recovery record belongs to a different game folder.");
        var targets = new List<(KitFile File, string Target, string? OriginalHash)>();
        // Retire only receipt-owned runtime files, preserving externally changed
        // files and keeping old hashes in the incomplete receipt for recovery.
        var retired = (receipt?.Files ?? new List<OwnedFile>()).Where(f => f.RelativePath.Replace('\\', '/').StartsWith("dotnet/", StringComparison.Ordinal) &&
            !files.Any(p => p.Relative.Equals(f.RelativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var old in retired)
        {
            var path = Inside(Root, old.RelativePath);
            if (File.Exists(path) && Hash(path) != old.Sha256)
                throw new IOException("An older runtime file was changed outside this installer and was kept: " + old.RelativePath);
        }
        var sharedFiles = new List<string>();
        foreach (var relative in new[] { $"BepInEx/config/{PluginId}.cfg", $"BepInEx/config/{CustomPluginId}.cfg", "BepInEx/config/BepInEx.cfg" })
        {
            Inside(Root, relative); Inside(Root, relative + ".tmp");
        }
        foreach (var file in files)
        {
            var relative = file.Relative == Proxy && File.Exists(Inside(Root, DisabledProxy)) ? DisabledProxy : file.Relative;
            var target = Inside(Root, relative);
            Inside(Root, relative + ".kit-new");
            if (Directory.Exists(target)) throw new IOException("A folder is blocking installation: " + relative);
            var hash = File.Exists(target) ? Hash(target) : null;
            var owned = receipt?.Files.FirstOrDefault(f => f.RelativePath.Replace('\\', '/').Equals(file.Relative, StringComparison.OrdinalIgnoreCase));
            var old = previous?.Files.FirstOrDefault(f => f.RelativePath.Replace('\\', '/').Equals(file.Relative, StringComparison.OrdinalIgnoreCase));
            // Older local loader installs downloaded this support ZIP themselves.
            // Reuse an identical ZIP without claiming it or deleting it on removal.
            var shared = receipt != null && owned == null && hash == file.Sha256 && file.Relative == "BepInEx/unity-libs/2022.3.62.zip";
            if (shared) sharedFiles.Add(file.Relative);
            if (!shared && hash != null && (owned == null || hash != owned.Sha256 && hash != old?.Sha256))
                throw new IOException("A file was changed outside this installer and was kept: " + relative + ". Restore that file or contact the mod creator.");
            if (File.Exists(target + ".kit-new") && Hash(target + ".kit-new") != file.Sha256)
                throw new IOException("An unrecognized staged file was kept: " + relative + ".kit-new");
            targets.Add((file, target, hash));
        }
        progress?.Report(new(15, "Backing up your saves and previous setup…"));
        BackupSaves();
        var setupBackup = Inside(ManagerRoot, "state/setup-backups/" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(setupBackup);
        foreach (var config in new[] { ProfilePath, Inside(Root, $"BepInEx/config/{PluginId}.cfg"), Inside(Root, $"BepInEx/config/{CustomPluginId}.cfg") })
            if (File.Exists(config)) File.Copy(config, Path.Combine(setupBackup, Path.GetFileName(config)));
        foreach (var target in targets.Where(t => t.OriginalHash != null && t.OriginalHash != t.File.Sha256))
        {
            var copy = Inside(ManagerRoot, "state/plugin-backups/" + target.OriginalHash + ".dll");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            if (!File.Exists(copy)) File.Copy(target.Target, copy);
        }
        foreach (var old in retired)
        {
            var source = Inside(Root, old.RelativePath);
            var copy = Inside(setupBackup, old.RelativePath);
            if (!File.Exists(source)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(source, copy);
        }
        if (receipt is { Complete: true }) File.Copy(ReceiptPath, ReceiptPath + ".previous", true);
        var next = new InstallReceipt { GameRoot = Root, AssemblyHash = info.AssemblyHash, MetadataHash = info.MetadataHash };
        next.Files.AddRange(receipt?.Files.Where(f => !files.Any(p => p.Relative.Equals(f.RelativePath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))) ?? Enumerable.Empty<OwnedFile>());
        next.Files.AddRange(files.Where(f => !sharedFiles.Contains(f.Relative)).Select(f => new OwnedFile(f.Relative, f.Sha256)));
        Storage.Json(ReceiptPath, next);
        for (var i = 0; i < targets.Count; i++)
        {
            var (file, target, hash) = targets[i];
            progress?.Report(new(25 + 55 * i / targets.Count, "Installing the complete expansion…"));
            if (hash == file.Sha256) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file.Source, target + ".kit-new", true);
            if (Hash(target + ".kit-new") != file.Sha256) throw new IOException("An included file failed its integrity check.");
            File.Move(target + ".kit-new", target, true);
        }
        progress?.Report(new(85, "Enabling Forge and all included content…"));
        var profile = ExpansionKit.Profile();
        foreach (var entry in new[] { (Path: $"BepInEx/config/{PluginId}.cfg", Text: profile.Config()), (Path: $"BepInEx/config/{CustomPluginId}.cfg", Text: profile.CustomConfig()) })
        {
            Inside(Root, entry.Path + ".tmp");
            Storage.Write(Inside(Root, entry.Path), entry.Text);
        }
        Storage.Json(ProfilePath, profile);
        var loaderConfig = Inside(Root, "BepInEx/config/BepInEx.cfg");
        if (!File.Exists(loaderConfig))
        {
            Inside(Root, "BepInEx/config/BepInEx.cfg.tmp");
            var unity = File.Exists(Inside(Root, "BepInEx/unity-libs/2022.3.62.zip")) ? "\n[IL2CPP]\nUnityBaseLibrariesSource = 2022.3.62.zip\n" : "";
            Storage.Write(loaderConfig, "[Logging.Console]\nEnabled = false\n" + unity);
        }
        SetLoader(true);
        foreach (var old in retired)
        {
            var path = Inside(Root, old.RelativePath);
            if (File.Exists(path))
            {
                if (Hash(path) != old.Sha256) throw new IOException("An older runtime file changed during installation and was kept.");
                File.Delete(path);
            }
            next.Files.RemoveAll(f => f.RelativePath == old.RelativePath);
        }
        next.Complete = true;
        Storage.Json(ReceiptPath, next);
        Storage.Json(ExpansionReceiptPath, new ExpansionReceipt(ExpansionKit.Version, ExpansionKit.ContentVersion, sharedFiles.ToArray()));
        progress?.Report(new(100, "Installation complete. Ready to play."));
    }

    public void LaunchExpansion()
    {
        RequireClosed();
        if (ExpansionState().Stage != ExpansionStage.Ready)
            throw new InvalidOperationException("Complete the expansion installation before playing.");
        SaveProfile(ExpansionKit.Profile());
        Launch(true);
    }
}
