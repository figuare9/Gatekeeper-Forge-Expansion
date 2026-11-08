using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Win32;

namespace Gatekeeper.ModManager;

internal static class Program
{
    private static readonly string LocalData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GatekeeperModManager");
    private sealed record Preferences(string GameRoot);

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, ExpansionKit.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        string? output = null;
        try
        {
            string? explicitGame = null, preview = null;
            var command = "";
            var choose = false;
            for (var i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("A command option is missing its value.");
                switch (args[i])
                {
                    case "--game-dir": explicitGame = Path.GetFullPath(Value()); break;
                    case "--output": output = Path.GetFullPath(Value()); break;
                    case "--render-preview": preview = Path.GetFullPath(Value()); break;
                    case "--choose-game": choose = true; break;
                    case "--inspect": case "--install": case "--disable": case "--update-mods": case "--launch-modded": case "--uninstall": case "--prepare":
                        if (command.Length > 0) throw new ArgumentException("Choose one command at a time.");
                        command = args[i]; break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            var packageRoot = PreparePackages();
            if (command == "--prepare")
            {
                if (output == null) throw new ArgumentException("Specify --output for the package check.");
                Storage.Json(output, new { prepared = true, packageRoot, customModSha256 = GameService.Hash(Path.Combine(packageRoot,"packages/mods/Gatekeeper.CustomMods.dll")) });
                return 0;
            }
            GameService SelectGame(string root)
            {
                var dataRoot = PortablePackage.DataRoot(root, LocalData);
                var selected = new GameService(root, dataRoot, packageRoot: packageRoot);
                Storage.Json(Path.Combine(LocalData, "last-game.json"), new Preferences(root));
                return selected;
            }
            var gameRoot = ResolveGame(explicitGame, choose, command.Length == 0);
            if (command.Length > 0)
            {
                if (gameRoot == null) throw new DirectoryNotFoundException("Gatekeeper was not found. Specify --game-dir with its installation folder.");
                using var instance = new Mutex(true, "Local\\GatekeeperModManager-" + PortablePackage.Key(gameRoot), out var firstInstance);
                if (!firstInstance)
                {
                    throw new InvalidOperationException("The installer or an older mod manager is already open for this game folder.");
                }
                var dataRoot = PortablePackage.DataRoot(gameRoot,LocalData);
                var game = new GameService(gameRoot,dataRoot,packageRoot:packageRoot);
                    output ??= Path.Combine(dataRoot,"state/command-result.json");
                    object result = command switch
                    {
                        "--inspect" => new { game = game.Inspect(), game.ManagerRoot, game.PackageRoot, game.Installed, game.LoaderEnabled, expansion = game.ExpansionState(), profile = game.Profile() },
                        "--install" => Run(() => game.InstallExpansion(),new { installed = true, version = ExpansionKit.Version }),
                        "--disable" => Run(() => game.SetLoader(false),new { loaderEnabled = false }),
                        "--update-mods" => Run(() => game.InstallExpansion(),new { updated = true, version = ExpansionKit.Version }),
                        "--launch-modded" => Run(() => game.LaunchExpansion(),new { launched = true }),
                        "--uninstall" => new { retainedChangedFiles = game.Uninstall() },
                        _ => throw new ArgumentException("Unknown command.")
                    };
                    Storage.Json(output,result);
                    return 0;
            }
            using var installer = new Mutex(true, "Local\\GatekeeperForgeExpansionInstaller", out var firstInstaller);
            if (!firstInstaller) { MessageBox.Show("The Forge expansion installer is already open.", ExpansionKit.Name); return 1; }
            using var form = new MainForm(gameRoot == null ? null : SelectGame(gameRoot), SelectGame, preview);
            Application.Run(form);
            return 0;
        }
        catch (Exception ex)
        {
            if (output != null) Storage.Json(output,new { error = ex.ToString() });
            else MessageBox.Show(ex.Message,ExpansionKit.Name,MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }

    private static T Run<T>(Action work,T result) { work(); return result; }

    private static string PreparePackages()
    {
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Gatekeeper.ModManager.Payload.zip");
        if (payload != null)
        {
            var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(); payload.Position = 0;
            var destination = GameService.Inside(LocalData,"bundles/" + hash);
            using var gate = new Mutex(false,"Local\\GatekeeperModManager-Package-" + hash);
            try { try { gate.WaitOne(); } catch (AbandonedMutexException) { } PortablePackage.Extract(payload,destination); }
            finally { gate.ReleaseMutex(); }
            return destination;
        }
        for (var directory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar); directory != null; directory = Path.GetDirectoryName(directory))
            if (Directory.Exists(Path.Combine(directory,"packages/loader"))) return directory;
        throw new InvalidOperationException("The bundled components are missing. Download the complete installation kit again.");
    }

    private static string? ResolveGame(string? explicitGame,bool choose,bool interactive)
    {
        if (explicitGame != null)
            return SteamDiscovery.IsGame(explicitGame) ? explicitGame : throw new DirectoryNotFoundException("Choose the Gatekeeper folder containing Gatekeeper.exe.");
        string? remembered = null;
        try { remembered = Storage.Read<Preferences>(Path.Combine(LocalData,"last-game.json"))?.GameRoot; }
        catch (System.Text.Json.JsonException) { }
        if (!choose)
        {
            if (remembered != null && SteamDiscovery.IsGame(remembered)) return Path.GetFullPath(remembered);
            for (var parent = Path.GetDirectoryName(Environment.ProcessPath); parent != null; parent = Path.GetDirectoryName(parent))
                if (SteamDiscovery.IsGame(parent)) return parent;
            var matches = SteamDiscovery.Find(SteamRoots());
            if (matches.Length == 1) return matches[0];
        }
        if (!interactive) throw new DirectoryNotFoundException("Gatekeeper was not found. Specify --game-dir with its installation folder.");
        // Keep the installer visible when discovery fails or finds multiple games.
        // Folder selection happens on its main screen, rather than before it opens.
        return null;
    }

    private static IEnumerable<string> SteamRoots()
    {
        var roots = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam") };
        try { if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) is string user) roots.Add(user); }
        catch (System.Security.SecurityException) { }
        foreach (var view in new[] { RegistryView.Registry32,RegistryView.Registry64 })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);
                using var key = machine.OpenSubKey(@"SOFTWARE\Valve\Steam");
                if (key?.GetValue("InstallPath") is string path) roots.Add(path);
            }
            catch (System.Security.SecurityException) { }
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
