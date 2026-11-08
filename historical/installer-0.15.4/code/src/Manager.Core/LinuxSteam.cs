using System.Diagnostics;

namespace Gatekeeper.ModManager;

// The installer runs natively on Linux; the unchanged Windows game and loader
// continue to run inside Steam's Proton prefix.
public static class LinuxSteam
{
    public const string LaunchOptions = "WINEDLLOVERRIDES=\"winhttp.dll=n,b\" %command%";
    public const string AppId = "2106670";

    public static string[] RootCandidates(string home, string? xdgData = null) => new[]
    {
        Path.Combine(AbsoluteOr(xdgData, Path.Combine(home, ".local/share")), "Steam"),
        Path.Combine(home, ".local/share/Steam"), Path.Combine(home, ".steam/steam"), Path.Combine(home, ".steam/root"),
        Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
        Path.Combine(home, ".var/app/com.valvesoftware.Steam/data/Steam")
    }.Distinct(PathIdentity.Comparer).ToArray();

    public static string AbsoluteOr(string? value, string fallback) => !string.IsNullOrEmpty(value) && Path.IsPathFullyQualified(value) ? value : fallback;

    // Steam commonly exposes ~/.steam/root and external libraries through links.
    // Resolve those aliases once, then let the shared installer reject any links
    // inside the actual installation/package rather than following them on writes.
    public static string PhysicalPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var info = new DirectoryInfo(current);
            if (info.LinkTarget != null)
                current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException("A Steam folder link could not be resolved: " + current);
        }
        return Path.GetFullPath(current);
    }

    public static string[] Libraries(IEnumerable<string> steamRoots)
    {
        var libraries = new HashSet<string>(PathIdentity.Comparer);
        foreach (var candidate in steamRoots.Where(Directory.Exists))
        {
            try
            {
                var root = PhysicalPath(candidate);
                libraries.Add(root);
                var vdf = Path.Combine(root, "steamapps/libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (var path in SteamDiscovery.LibraryPaths(File.ReadAllText(vdf)).Where(Directory.Exists))
                        libraries.Add(PhysicalPath(path));
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return libraries.OrderBy(p => p, PathIdentity.Comparer).ToArray();
    }

    public static string[] Find(IEnumerable<string> steamRoots) => SteamDiscovery.Find(Libraries(steamRoots))
        .Select(PhysicalPath).Distinct(PathIdentity.Comparer).ToArray();

    public static string ResolveSaveRoot(string game, IEnumerable<string> steamRoots, string? explicitPrefix = null)
    {
        if (explicitPrefix != null) return SaveInPrefix(PhysicalPath(explicitPrefix));
        var prefixes = new List<string>();
        var common = Directory.GetParent(PhysicalPath(game));
        if (common?.Name == "common" && common.Parent?.Name == "steamapps")
            prefixes.Add(Path.Combine(common.Parent.FullName, "compatdata", AppId, "pfx"));
        prefixes.AddRange(Libraries(steamRoots).Select(l => Path.Combine(l, "steamapps/compatdata", AppId, "pfx")));
        var candidates = prefixes.Where(p => Directory.Exists(Path.Combine(p, "drive_c/users/steamuser")))
            .Select(p => SaveInPrefix(PhysicalPath(p))).Distinct(PathIdentity.Comparer).ToArray();
        var withSaves = candidates.Where(p => Directory.Exists(Path.Combine(p, "Saves")) ||
            Directory.Exists(p) && Directory.EnumerateFiles(p, "GatekeeperSettingsSave*").Any()).ToArray();
        if (withSaves.Length > 1)
            throw new InvalidOperationException("More than one Gatekeeper Proton save folder was found. Use --prefix with the active compatdata/2106670/pfx folder so the correct saves are backed up.");
        if (withSaves.Length == 1) return withSaves[0];
        if (candidates.Length > 1)
            throw new InvalidOperationException("More than one Gatekeeper Proton prefix was found. Use --prefix with the active compatdata/2106670/pfx folder.");
        return candidates.SingleOrDefault() ?? throw new DirectoryNotFoundException("Start Gatekeeper once through Steam using Proton, then close it and reopen this installer. Its Proton save folder has not been created yet.");
    }

    private static string SaveInPrefix(string prefix)
    {
        if (!Directory.Exists(Path.Combine(prefix, "drive_c/users/steamuser")))
            throw new DirectoryNotFoundException("Choose the Proton prefix folder containing drive_c/users/steamuser.");
        return PhysicalPath(Path.Combine(prefix, "drive_c/users/steamuser/AppData/LocalLow/Gravity Lagoon/Gatekeeper"));
    }

    public static bool GameRunning(string procRoot = "/proc")
    {
        if (!OperatingSystem.IsLinux() && procRoot == "/proc")
            return Process.GetProcessesByName("Gatekeeper").Any();
        if (!Directory.Exists(procRoot)) throw new IOException("Cannot check running games because /proc is unavailable. No installation changes were made.");
        foreach (var directory in Directory.EnumerateDirectories(procRoot).Where(d => int.TryParse(Path.GetFileName(d), out _)))
        {
            try
            {
                var name = File.ReadAllText(Path.Combine(directory, "comm")).Trim();
                if (name.Equals("Gatekeeper.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("Gatekeeper", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
            catch (UnauthorizedAccessException) { } // Other users' processes may be hidden.
        }
        return false;
    }
}
