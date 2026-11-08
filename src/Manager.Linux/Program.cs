using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Gatekeeper.ModManager.Linux;

internal static class Program
{
    [DllImport("libc")] private static extern uint geteuid();
    private sealed record Preferences(string GameRoot);
    private static int Main(string[] args)
    {
        var dialogs = new Dialogs(args.Contains("--cli"));
        try
        {
            if (args.Contains("--help") || args.Contains("-h")) { Help(); return 0; }
            if (args.Contains("--news"))
            {
                var at = Array.IndexOf(args, "--news");
                var news = ExpansionNews.Bundled;
                var id = at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal) ? args[at + 1] : news.Featured.Id;
                var article = news.Index.Expansions.SelectMany(e => e.Articles).SingleOrDefault(a => a.Id == id)
                    ?? throw new ArgumentException("Unknown article. Available IDs: " + string.Join(", ", news.Index.Expansions.SelectMany(e => e.Articles).Select(a => a.Id)));
                Console.WriteLine(ExpansionNews.PlainText(news.Markdown(article))); return 0;
            }
            if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                throw new PlatformNotSupportedException("This installation kit is for x86-64 desktop Linux. Use the Windows installer on Windows.");
            if (geteuid() == 0) throw new InvalidOperationException("Run this installer as your normal Steam user, without sudo. Administrator access is not needed.");
            string? game = null, prefix = null;
            var command = ""; var yes = false;
            for (var i = 0; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("An option is missing its value.");
                switch (args[i])
                {
                    case "--game-dir": game = Value(); break;
                    case "--prefix": prefix = Value(); break;
                    case "--cli": break;
                    case "--yes": yes = true; break;
                    case "--inspect": case "--install": case "--uninstall": case "--backup": case "--launch":
                        if (command.Length != 0) throw new ArgumentException("Choose one command at a time.");
                        command = args[i]; break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var data = Path.Combine(LinuxSteam.AbsoluteOr(Environment.GetEnvironmentVariable("XDG_DATA_HOME"), Path.Combine(home, ".local/share")), "GatekeeperModManager");
            var cache = Path.Combine(LinuxSteam.AbsoluteOr(Environment.GetEnvironmentVariable("XDG_CACHE_HOME"), Path.Combine(home, ".cache")), "GatekeeperModManager");
            // Normalize XDG aliases before handing paths to the shared write guard.
            data = LinuxSteam.PhysicalPath(data); cache = LinuxSteam.PhysicalPath(cache);
            var roots = LinuxSteam.RootCandidates(home, Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
            var preference = GameService.Inside(data, "last-game-linux.json");
            if (game == null)
            {
                try { var remembered = Storage.Read<Preferences>(preference)?.GameRoot; if (remembered != null && SteamDiscovery.IsGame(remembered)) game = remembered; }
                catch (JsonException) { }
                if (game == null)
                {
                    var found = LinuxSteam.Find(roots);
                    if (found.Length == 1) game = found[0];
                    else if (command.Length == 0) game = dialogs.PickFolder();
                    else throw new DirectoryNotFoundException("Gatekeeper could not be selected automatically. Specify --game-dir with the folder containing Gatekeeper.exe.");
                }
            }
            if (string.IsNullOrWhiteSpace(game)) return 0;
            game = LinuxSteam.PhysicalPath(game);
            if (!SteamDiscovery.IsGame(game)) throw new DirectoryNotFoundException("Select the Gatekeeper game folder containing Gatekeeper.exe, GameAssembly.dll and Gatekeeper_Data.");
            var saves = LinuxSteam.ResolveSaveRoot(game, roots, prefix);
            var state = PortablePackage.DataRoot(game, data);
            using var gate = Lock(GameService.Inside(data, "locks/" + PortablePackage.Key(game) + ".lock"));
            var package = PreparePackage(cache);
            var service = new GameService(game, state, () => LinuxSteam.GameRunning(), saves, package.Root, package);
            var info = service.Inspect();
            var status = service.ExpansionState();
            if (command == "--inspect")
            {
                Console.WriteLine(JsonSerializer.Serialize(new { game = info, expansion = status, saveRoot = saves, dataRoot = state, packageRoot = package.Root, running = service.Running, launchOptions = LinuxSteam.LaunchOptions }, Storage.JsonOptions));
                return 0;
            }
            if (!info.Supported) throw new InvalidOperationException("This game build is not supported by the included expansion. No game files were changed.");
            if (service.Running) throw new InvalidOperationException("Close Gatekeeper before installing, updating, backing up or removing the expansion.");
            if (command == "--uninstall")
            {
                if (!yes && !dialogs.Confirm("Remove the expansion from:\n" + game + "\n\nYour saves, unlocks and backups will be kept. Finish any saved run using expansion content first.", "Remove expansion")) return 0;
                var retained = service.Uninstall();
                dialogs.Message("Expansion removed. Your saves and unlocks were kept.\nRemove the winhttp.dll entry from Gatekeeper's Steam launch options if it was added for Forge." +
                    (retained.Count > 0 ? "\nChanged files were preserved: " + string.Join(", ", retained) : "")); return 0;
            }
            if (command == "--backup") { dialogs.Message(service.BackupSaves() is { } backup ? "Backup saved to:\n" + backup : "No saves were found yet."); return 0; }
            if (command == "--launch") { service.LaunchExpansion(); return 0; }
            if (status.Stage == ExpansionStage.Ready && command != "--install") { dialogs.Instructions(Instructions(game, state)); return 0; }
            var action = status.Stage switch { ExpansionStage.UpdateRequired => "Update expansion", ExpansionStage.RepairRequired => "Repair installation", _ => "Install expansion" };
            if (!yes && !dialogs.Confirm("Gatekeeper · Forge Expansion " + ExpansionKit.Version + "\n\nGame:\n" + game + "\n\nProton saves to back up:\n" + saves +
                "\n\nInstalls the complete expansion: Forge, Cannon Wheel, shops, music and new Aspects.\nAfter installation, paste one launch setting into Steam.", action)) return 0;
            using (dialogs.Progress(out var progress)) service.InstallExpansion(progress);
            Storage.Json(preference, new Preferences(game));
            dialogs.Instructions(Instructions(game, state));
            return 0;
        }
        catch (Exception ex) { dialogs.Message(ex.Message, true); return 1; }
    }
    private static FileStream Lock(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("Another Forge installer is already working on this game or package. Close it and try again."); }
    }
    private static VerifiedPackage PreparePackage(string cache)
    {
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Gatekeeper.ModManager.Payload.zip") ??
            throw new InvalidDataException("The included expansion files are missing. Download the complete Linux installation kit.");
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(); payload.Position = 0;
        using var gate = Lock(GameService.Inside(cache, "bundle.lock"));
        var destination = GameService.Inside(cache, "bundles/" + hash);
        return PortablePackage.Prepare(payload, destination);
    }
    private static string Instructions(string game, string state) =>
        "Forge Expansion " + ExpansionKit.Version + " is installed.\n\n" +
        "ONE-TIME STEAM SETUP\n" +
        "Open Steam → Gatekeeper → Properties → General → Launch Options.\n" +
        "If that box is empty, copy this entire line into it:\n\n" + LinuxSteam.LaunchOptions + "\n\n" +
        "If you already have launch options, keep them. Add winhttp.dll=n,b to your existing WINEDLLOVERRIDES value, or add the setting before your existing command. Keep exactly one %command%.\n\n" +
        "Launch Gatekeeper normally from Steam. Use the Windows version through Proton. The first modded launch may take longer while bindings are generated.\n\n" +
        "Everyone in co-op must use gameplay " + ExpansionKit.ContentVersion + ".\n\n" +
        "Complete expansion notes are included in notices/news-forge-complete.md beside this installer.\n\n" +
        "Game: " + game + "\nBackups and installation records: " + state + "\nGame log: " + Path.Combine(game, "BepInEx/LogOutput.log") +
        "\n\nThis Linux kit has been cross-built and its installation logic checked on Windows. A Linux gameplay test is still required.";
    private static void Help() => Console.WriteLine("Gatekeeper Forge Expansion " + ExpansionKit.Version + " — desktop Linux / Steam Proton\n" +
        "Run Install-Forge.sh for guided installation. No sudo or separate .NET installation.\n\n" +
        "Options:\n  --news [ARTICLE]    Read offline expansion notes without selecting or changing a game\n  --game-dir PATH    Select the Steam game folder\n  --prefix PATH      Select compatdata/2106670/pfx if needed\n  --cli              Use the terminal instead of desktop dialogs\n" +
        "  --inspect          Report game, installation and save locations\n  --install          Install/update/repair the complete expansion\n  --uninstall        Remove unchanged managed files; preserve saves\n" +
        "  --backup           Back up Proton saves\n  --launch           Launch an already installed expansion through Steam\n  --yes              Confirm an explicitly selected installation/removal\n  --help             Show this help\n\nSteam launch options:\n" + LinuxSteam.LaunchOptions);
}
