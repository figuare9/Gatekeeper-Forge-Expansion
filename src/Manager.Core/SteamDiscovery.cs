using System.Text.RegularExpressions;

namespace Gatekeeper.ModManager;

public static class SteamDiscovery
{
    private static readonly Regex Pairs = new(@"""((?:\\.|[^""\\])*)""\s*""((?:\\.|[^""\\])*)""", RegexOptions.Compiled);
    private static string Decode(string value) => value.Replace("\\\\", "\\").Replace("\\\"", "\"");
    public static bool IsGame(string root) => File.Exists(Path.Combine(root, "Gatekeeper.exe")) &&
        File.Exists(Path.Combine(root, "GameAssembly.dll")) && File.Exists(Path.Combine(root, GameService.MetadataFile));

    public static string[] LibraryPaths(string text) => Pairs.Matches(text).Cast<Match>()
        .Where(m => m.Groups[1].Value == "path" || int.TryParse(m.Groups[1].Value, out _))
        .Select(m => Decode(m.Groups[2].Value)).Where(Path.IsPathRooted).Distinct(PathIdentity.Comparer).ToArray();

    public static string[] Find(IEnumerable<string> steamRoots)
    {
        var libraries = new HashSet<string>(PathIdentity.Comparer);
        foreach (var root in steamRoots.Where(Directory.Exists))
        {
            libraries.Add(Path.GetFullPath(root));
            var file = Path.Combine(root, "steamapps/libraryfolders.vdf");
            try { if (File.Exists(file)) foreach (var library in LibraryPaths(File.ReadAllText(file))) libraries.Add(library); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var games = new HashSet<string>(PathIdentity.Comparer);
        foreach (var library in libraries)
        {
            try
            {
                var common = Path.Combine(library, "steamapps/common");
                var manifest = Path.Combine(library, "steamapps/appmanifest_2106670.acf");
                if (File.Exists(manifest))
                {
                    var dir = Pairs.Matches(File.ReadAllText(manifest)).Cast<Match>().FirstOrDefault(m => m.Groups[1].Value == "installdir");
                    if (dir != null)
                    {
                        var candidate = GameService.Inside(common, Decode(dir.Groups[2].Value));
                        if (IsGame(candidate)) games.Add(candidate);
                    }
                }
                var standard = GameService.Inside(common, "Gatekeeper");
                if (IsGame(standard)) games.Add(standard);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { } catch (InvalidDataException) { }
        }
        return games.OrderBy(p => p, PathIdentity.Comparer).ToArray();
    }
}
