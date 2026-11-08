using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Gatekeeper.ModManager;

public sealed partial class GameService
{
    public const string PluginId = "local.gatekeeper.subtlebalance";
    public const string CustomPluginId = "local.gatekeeper.custommods";
    public const string CustomPlugin = "BepInEx/plugins/Gatekeeper.CustomMods/Gatekeeper.CustomMods.dll";
    public const string SupportedAssembly = "2f59b50a1dde831a32006b419adbf560a195b0de638d0669bac8c029e85d9ce9";
    public const string SupportedMetadata = "e0f9349d0d37aa0684243c63a72057c238f104e0c67abf150d7ac9cf8918b009";
    public const string MetadataFile = "Gatekeeper_Data/il2cpp_data/Metadata/global-metadata.dat";
    private const string Proxy = "winhttp.dll", DisabledProxy = "winhttp.dll.gkmm-disabled";
    private const string Plugin = "BepInEx/plugins/Gatekeeper.SubtleBalance/Gatekeeper.SubtleBalance.dll";
    private readonly Func<bool> isRunning;
    private readonly VerifiedPackage? verifiedPackage;
    internal string ExpectedAssembly { get; set; } = SupportedAssembly;
    internal string ExpectedMetadata { get; set; } = SupportedMetadata;
    public string Root { get; }
    public string ManagerRoot { get; }
    public string PackageRoot { get; }
    public string ReceiptPath => Path.Combine(ManagerRoot, "state", "installation.json");
    public string ProfilePath => Path.Combine(ManagerRoot, "state", "profile.json");
    public string SaveRoot { get; }
    public bool Running => isRunning();
    public bool LoaderEnabled => File.Exists(Inside(Root, Proxy));
    public bool Installed => Receipt()?.Complete == true && File.Exists(Inside(Root, Plugin));
    public string LogPath => Inside(Root, "BepInEx/LogOutput.log");
    public string StatusPath => Inside(Root, $"BepInEx/config/{PluginId}.status.json");

    public GameService(string gameRoot, string managerRoot, Func<bool>? running = null, string? saveRoot = null, string? packageRoot = null, VerifiedPackage? verifiedPackage = null)
    {
        Root = Path.GetFullPath(gameRoot);
        ManagerRoot = Path.GetFullPath(managerRoot);
        PackageRoot = Path.GetFullPath(packageRoot ?? managerRoot);
        this.verifiedPackage = verifiedPackage;
        if (verifiedPackage != null && !PackageRoot.Equals(verifiedPackage.Root, PathIdentity.Comparison))
            throw new InvalidDataException("The verified package belongs to a different folder.");
        isRunning = running ?? (() => Process.GetProcessesByName("Gatekeeper").Any());
        SaveRoot = saveRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Gravity Lagoon", "Gatekeeper");
    }

    public static string Inside(string root, string relative)
    {
        // Receipts written on Windows can contain backslash-separated paths.
        relative = relative.Replace('\\', Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("Invalid relative path.");
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(basePath, relative));
        if (!result.StartsWith(basePath, PathIdentity.Comparison)) throw new InvalidDataException("Path leaves the selected folder.");
        // Reject junctions and symlinks, including in ancestors of the selected root.
        for (var current = result; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked folders are not supported: " + current);
        return result;
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public GameInfo Inspect()
    {
        var exe = Inside(Root, "Gatekeeper.exe");
        using (var reader = new BinaryReader(File.OpenRead(exe)))
        {
            if (reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Gatekeeper.exe is not a Windows executable.");
            reader.BaseStream.Position = 0x3c;
            var header = reader.ReadInt32();
            if (header < 64 || header > reader.BaseStream.Length - 6) throw new InvalidDataException("Invalid executable header.");
            reader.BaseStream.Position = header;
            if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("This manager requires the 64-bit Windows game.");
        }
        var metadata = Inside(Root, MetadataFile);
        using (var reader = new BinaryReader(File.OpenRead(metadata)))
            if (reader.ReadUInt32() != 0xfab11baf) throw new InvalidDataException("Unity IL2CPP metadata is missing or invalid.");
        var assembly = Hash(Inside(Root, "GameAssembly.dll"));
        var metaHash = Hash(metadata);
        var unity = File.Exists(Inside(Root, "UnityPlayer.dll")) ? FileVersionInfo.GetVersionInfo(Inside(Root, "UnityPlayer.dll")).ProductVersion ?? "Unknown" : "Unknown";
        return new(Root, unity, assembly, metaHash, assembly == ExpectedAssembly && metaHash == ExpectedMetadata);
    }

    public InstallReceipt? Receipt()
    {
        var receipt = Storage.Read<InstallReceipt>(ReceiptPath);
        if (receipt != null && !Path.GetFullPath(receipt.GameRoot).Equals(Root, PathIdentity.Comparison))
            throw new InvalidOperationException("This manager's installation record belongs to another game folder.");
        return receipt;
    }

    private void RequireClosed()
    {
        if (Running) throw new InvalidOperationException("Close Gatekeeper before changing mods or making a save backup.");
    }
    private void RequireVanillaCompatible()
    {
        RequireForgeCompatible();
        var aspectMarker=Storage.Read<Gatekeeper.Mods.AspectRunMarker>(Inside(Root,$"BepInEx/config/{CustomPluginId}.aspects-run.json"));
        if(aspectMarker is {RequiresMod:true} && aspectMarker.Aspects.Any(Gatekeeper.Mods.AspectRules.IsCustom))
            throw new InvalidOperationException("This saved run uses expansion Aspects. Finish or end it with the expansion enabled before launching vanilla or removing the loader.");
        var marker=Storage.Read<Gatekeeper.Mods.CannonRunMarker>(Inside(Root,$"BepInEx/config/{CustomPluginId}.cannon-run.json"));
        if(marker is {RequiresMod:true,Count:>0,ItemId:Gatekeeper.Mods.CannonRules.ItemId})
            throw new InvalidOperationException("This saved run contains Cannon Wheel. Finish or end that run with mod support enabled before launching vanilla or removing the loader. You may disable Cannon Wheel's effect and continue modded.");
    }

    private void RequireForgeCompatible()
    {
        var marker=Storage.Read<Gatekeeper.Mods.ForgeRunMarker>(Inside(Root,$"BepInEx/config/{CustomPluginId}.forge-run.json"));
        if(marker is {RequiresMod:true,CharacterId:Gatekeeper.Mods.ForgeRules.CharacterId})
            throw new InvalidOperationException("This saved run uses Forge. Finish or end it with Forge enabled before disabling Forge, launching vanilla, or removing the loader.");
    }

    public BalanceProfile Profile() => Storage.Read<BalanceProfile>(ProfilePath) ?? new();

    public void SaveProfile(BalanceProfile profile)
    {
        RequireClosed();
        profile.Validate();
        if(!profile.Forge)RequireForgeCompatible();
        if (Installed)
        {
            var config = Inside(Root, $"BepInEx/config/{PluginId}.cfg");
            Inside(Root, $"BepInEx/config/{PluginId}.cfg.tmp");
            Storage.Write(config, profile.Config());
            var customConfig = Inside(Root, $"BepInEx/config/{CustomPluginId}.cfg");
            Inside(Root, $"BepInEx/config/{CustomPluginId}.cfg.tmp");
            Storage.Write(customConfig, profile.CustomConfig());
        }
        Storage.Json(ProfilePath, profile);
    }

    public string? BackupSaves()
    {
        RequireClosed();
        if (!Directory.Exists(SaveRoot)) return null;
        var inputs = new List<string>();
        var saves = Inside(SaveRoot, "Saves");
        if (Directory.Exists(saves)) inputs.AddRange(SafeFiles(saves));
        inputs.AddRange(Directory.GetFiles(SaveRoot, "GatekeeperSettingsSave*"));
        if (inputs.Count == 0) return null;
        var output = Path.Combine(ManagerRoot, "backups", $"saves-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var partial = output + ".partial";
        try
        {
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
                foreach (var file in inputs)
                {
                    var relative = Path.GetRelativePath(SaveRoot, file);
                    Inside(SaveRoot, relative);
                    zip.CreateEntryFromFile(file, relative.Replace('\\', '/'), CompressionLevel.Optimal);
                }
            File.Move(partial, output);
            foreach(var name in new[]{"cannon-progress","forge-progress"})
            {
                var progress=Inside(Root,$"BepInEx/config/{CustomPluginId}.{name}.json");
                if(File.Exists(progress))File.Copy(progress,Path.ChangeExtension(output,name+".json"),overwrite:false);
            }
        }
        catch { if (File.Exists(partial)) File.Delete(partial); throw; }
        return output;
    }

    public static IEnumerable<string> SafeFiles(string folder)
    {
        foreach (var item in Directory.EnumerateFileSystemEntries(folder))
        {
            if ((File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked package or save entries are not supported.");
            if (Directory.Exists(item)) { foreach (var file in SafeFiles(item)) yield return file; }
            else yield return item;
        }
    }

    public void Install()
    {
        RequireClosed();
        var info = Inspect();
        if (!info.Supported) throw new InvalidOperationException("This game build has changed. Its gameplay hooks need to be checked before installing.");
        if (Receipt() != null) throw new InvalidOperationException("An installation record already exists. Remove the managed loader before reinstalling.");
        if (File.Exists(Inside(Root, Proxy)) || File.Exists(Inside(Root, DisabledProxy)) ||
            Directory.Exists(Inside(Root, "MelonLoader")) || File.Exists(Inside(Root, "version.dll")) || File.Exists(Inside(Root, "doorstop_config.ini")))
            throw new InvalidOperationException("Another loader or proxy is already present. This manager will not overwrite it.");

        var files = KitFiles(requireCustom: false);
        foreach (var (source, relative, _) in files)
        {
            if (!File.Exists(source)) throw new FileNotFoundException("A packaged component is missing.", source);
            var target = Inside(Root, relative);
            if (File.Exists(target) || Directory.Exists(target)) throw new IOException("Refusing to overwrite: " + relative);
        }
        if (!files.Any(f => f.Relative == Proxy) || !files.Any(f => f.Relative.Replace('\\', '/') == "BepInEx/core/BepInEx.Unity.IL2CPP.dll"))
            throw new InvalidDataException("The IL2CPP loader package is incomplete.");

        BackupSaves();
        var receipt = new InstallReceipt { GameRoot = Root, AssemblyHash = info.AssemblyHash, MetadataHash = info.MetadataHash };
        receipt.Files.AddRange(files.Select(f => new OwnedFile(f.Relative, f.Sha256)));
        // Record the entire operation before writing to the game, so interrupted installs are recoverable.
        Storage.Json(ReceiptPath, receipt);
        foreach (var (source, relative, hash) in files)
        {
            var target = Inside(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var pending = Inside(Root, relative + ".kit-new");
            if (File.Exists(pending)) throw new IOException("An existing staged file was kept: " + relative);
            File.Copy(source, pending, overwrite: false);
            if (Hash(pending) != hash) throw new IOException("An included file failed its integrity check.");
            File.Move(pending, target, overwrite: false);
        }
        receipt.Complete = true;
        Storage.Json(ReceiptPath, receipt);
        var loaderConfig = Inside(Root, "BepInEx/config/BepInEx.cfg");
        if (!File.Exists(loaderConfig))
        {
            Inside(Root, "BepInEx/config/BepInEx.cfg.tmp");
            var unitySupport = File.Exists(Inside(Root,"BepInEx/unity-libs/2022.3.62.zip"))
                ? "\n[IL2CPP]\nUnityBaseLibrariesSource = 2022.3.62.zip\n" : "";
            Storage.Write(loaderConfig, "[Logging.Console]\nEnabled = false\n" + unitySupport);
        }
        SaveProfile(Profile());
        SetLoader(false);
    }

    public void SetLoader(bool enable)
    {
        RequireClosed();
        if(!enable)RequireVanillaCompatible();
        var receipt = Receipt() ?? throw new InvalidOperationException("Install the managed loader first.");
        var expected = receipt.Files.Single(f => f.RelativePath == Proxy).Sha256;
        var from = Inside(Root, enable ? DisabledProxy : Proxy);
        var to = Inside(Root, enable ? Proxy : DisabledProxy);
        if (File.Exists(from) && File.Exists(to)) throw new IOException("Both loader proxies exist; resolve this conflict before launching.");
        if (File.Exists(from))
        {
            if (Hash(from) != expected) throw new IOException("The loader proxy changed. Refusing to rename an unrecognized file.");
            File.Move(from, to);
        }
        else if (!File.Exists(to) || Hash(to) != expected) throw new IOException("The managed loader proxy is missing or changed.");
    }

    public void UpdateCustomMods()
    {
        RequireClosed();
        var info = Inspect();
        if (!info.Supported || !Installed) throw new InvalidOperationException("A compatible managed installation is required.");
        var receipt = Receipt()!;
        var source = Inside(PackageRoot, "packages/mods/Gatekeeper.CustomMods.dll");
        var target = Inside(Root, CustomPlugin);
        var pending = Inside(Root, CustomPlugin + ".new");
        var hash = verifiedPackage == null ? Hash(source) : KitFiles().Single(f => f.Relative == CustomPlugin).Sha256;
        var old = receipt.Files.FirstOrDefault(f => f.RelativePath.Replace('\\', '/') == CustomPlugin);
        if (File.Exists(pending) && old != null && Hash(pending) == old.Sha256)
        {
            var previous = Storage.Read<InstallReceipt>(ReceiptPath + ".previous");
            var previousFile = previous?.Files.FirstOrDefault(f => f.RelativePath.Replace('\\', '/') == CustomPlugin);
            if (previous != null && Path.GetFullPath(previous.GameRoot).Equals(Root, PathIdentity.Comparison) &&
                (!File.Exists(target) || previousFile != null && Hash(target) == previousFile.Sha256))
            {
                if (File.Exists(target))
                {
                    var previousBackup = Inside(ManagerRoot, "state/plugin-backups/" + Hash(target) + ".dll");
                    Directory.CreateDirectory(Path.GetDirectoryName(previousBackup)!);
                    if (!File.Exists(previousBackup)) File.Copy(target, previousBackup);
                }
                File.Move(pending, target, true);
            }
        }
        if (File.Exists(target))
        {
            if (old == null || Hash(target) != old.Sha256) throw new IOException("The installed custom plugin has been changed outside this manager; it will not be overwritten.");
            if (old.Sha256 == hash) { SaveProfile(Profile()); return; }
            var backup = Inside(ManagerRoot, "state/plugin-backups/" + old.Sha256 + ".dll");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            if (!File.Exists(backup)) File.Copy(target, backup);
        }
        BackupSaves();
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(pending) && Hash(pending) != hash) throw new IOException("An unrecognized staged custom-plugin file exists; it will not be overwritten.");
        if (!File.Exists(pending)) File.Copy(source, pending);
        if (Hash(pending) != hash) throw new IOException("An included custom plugin failed its integrity check.");
        if (old != null) receipt.Files.Remove(old);
        receipt.Files.Add(new(CustomPlugin, hash));
        // Keep the previous receipt alongside the staged replacement for interrupted-update recovery.
        File.Copy(ReceiptPath, ReceiptPath + ".previous", true);
        Storage.Json(ReceiptPath, receipt);
        File.Move(pending, target, true);
        SaveProfile(Profile());
    }

    public IReadOnlyList<string> Uninstall()
    {
        RequireClosed();
        RequireVanillaCompatible();
        var receipt = Receipt() ?? throw new InvalidOperationException("No managed installation is recorded.");
        // Validate every recorded path before making any changes.
        foreach (var file in receipt.Files) Inside(Root, file.RelativePath);
        if (File.Exists(Inside(Root, Proxy)) || File.Exists(Inside(Root, DisabledProxy))) SetLoader(false);
        else if (receipt.Complete) throw new IOException("The managed loader proxy is missing. Check the installation record before removing files.");
        var kept = new List<string>();
        foreach (var file in receipt.Files)
        {
            var relative = file.RelativePath == Proxy ? DisabledProxy : file.RelativePath;
            var path = Inside(Root, relative);
            if (!File.Exists(path)) continue;
            if (Hash(path) == file.Sha256) File.Delete(path);
            else kept.Add(relative);
        }
        // Generated configuration, interop caches, logs and unrelated mods stay on disk.
        File.Move(ReceiptPath, ReceiptPath + $".removed-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        return kept;
    }

    public void Launch(bool modded)
    {
        RequireClosed();
        if(!modded)RequireVanillaCompatible();
        var info = Inspect();
        if (modded)
        {
            if (!info.Supported) throw new InvalidOperationException("The game build changed. Use vanilla until its mods are revalidated.");
            if (!Installed) throw new InvalidOperationException("Install the mod loader first.");
            SaveProfile(Profile());
        }
        if (Receipt() != null) SetLoader(modded);
        else if (modded || File.Exists(Inside(Root, Proxy)) || File.Exists(Inside(Root, "version.dll")))
            throw new InvalidOperationException("An unmanaged loader is present; this manager cannot guarantee a vanilla launch.");
        // Steam supplies the app context, avoiding a second launch by SteamAPI_RestartAppIfNecessary.
        var start = new ProcessStartInfo("steam://rungameid/2106670") { UseShellExecute = true };
        Process.Start(start);
    }

    public IReadOnlyList<ExternalMod> Mods()
    {
        var plugins = Inside(Root, "BepInEx/plugins");
        if (!Directory.Exists(plugins)) return Array.Empty<ExternalMod>();
        return SafeFiles(plugins).Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + "Gatekeeper.SubtleBalance" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !p.Contains(Path.DirectorySeparatorChar + "Gatekeeper.CustomMods" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(p => new ExternalMod(Path.GetFileName(p).Replace(".dll.disabled", "").Replace(".dll", ""),
                Path.GetRelativePath(Root, p), p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))).OrderBy(p => p.Name).ToArray();
    }

    public void ToggleMod(ExternalMod mod)
    {
        RequireClosed();
        if (!Mods().Any(x => x.RelativePath == mod.RelativePath)) throw new InvalidDataException("Mod is no longer present.");
        var source = Inside(Root, mod.RelativePath);
        var target = Inside(Root, mod.Enabled ? mod.RelativePath + ".disabled" : mod.RelativePath[..^9]);
        File.Move(source, target, overwrite: false);
    }

    public void ImportMod(string path)
    {
        RequireClosed();
        if (!Installed) throw new InvalidOperationException("Install the mod loader first.");
        if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose a BepInEx 6 IL2CPP plugin DLL.");
        var name = Path.GetFileName(path);
        if (!Regex.IsMatch(name, @"^[A-Za-z0-9_. -]+\.dll$", RegexOptions.IgnoreCase)) throw new InvalidDataException("The plugin filename contains unsupported characters.");
        var target = Inside(Root, "BepInEx/plugins/Imported/" + name);
        if (File.Exists(target + ".disabled")) throw new IOException("That plugin is already installed and disabled.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(path, target, overwrite: false);
    }
}
