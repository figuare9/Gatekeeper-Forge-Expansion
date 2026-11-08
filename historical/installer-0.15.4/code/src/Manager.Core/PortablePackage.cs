using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gatekeeper.ModManager;

public sealed record BundleFile(string Path, long Bytes, string Sha256);
public sealed record BundleManifest(string Version, BundleFile[] Files);

public static class PortablePackage
{
    public static string Key(string path)
    {
        var full = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full)))[..24];
    }

    // Data stays stable when the shareable EXE is moved or replaced. Reuse an
    // existing folder installation so its receipts, switches and backups survive.
    public static string DataRoot(string game, string localData)
    {
        var legacy = GameService.Inside(game, "GatekeeperModManager");
        var receipt = Storage.Read<InstallReceipt>(GameService.Inside(legacy, "state/installation.json"));
        if (receipt != null && System.IO.Path.GetFullPath(receipt.GameRoot).Equals(System.IO.Path.GetFullPath(game), PathIdentity.Comparison)) return legacy;
        return GameService.Inside(localData, "games/" + Key(game));
    }

    public static BundleManifest Extract(Stream source, string destination)
    {
        using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > 2048 || zip.Entries.GroupBy(e => e.FullName, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new InvalidDataException("Invalid bundled component list.");
        var entry = zip.GetEntry("bundle.json") ?? throw new InvalidDataException("Bundled component manifest is missing.");
        if (entry.Length > 1_000_000) throw new InvalidDataException("Bundled manifest is too large.");
        BundleManifest manifest;
        using (var stream = entry.Open()) manifest = JsonSerializer.Deserialize<BundleManifest>(stream) ?? throw new InvalidDataException("Invalid bundled manifest.");
        if (manifest.Files == null || manifest.Files.Length == 0 || manifest.Files.Length != zip.Entries.Count - 1 ||
            manifest.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new InvalidDataException("Bundled manifest entries do not match.");
        long size = 0;
        foreach (var file in manifest.Files)
        {
            if (file.Path == null || file.Path.Contains('\\') || file.Path.Split('/').Any(p => p is "" or "." or "..") ||
                !(file.Path.StartsWith("packages/loader/", StringComparison.Ordinal) || file.Path.StartsWith("packages/mods/", StringComparison.Ordinal) || file.Path.StartsWith("notices/", StringComparison.Ordinal)) ||
                file.Bytes < 0 || file.Bytes > 300_000_000 || !Regex.IsMatch(file.Sha256 ?? "", "^[a-f0-9]{64}$"))
                throw new InvalidDataException("Invalid bundled component path or hash.");
            GameService.Inside(destination, file.Path);
            GameService.Inside(destination, file.Path + ".extracting");
            if ((size += file.Bytes) > 1_000_000_000 || zip.GetEntry(file.Path)?.Length != file.Bytes)
                throw new InvalidDataException("Bundled component length does not match.");
        }
        foreach (var file in manifest.Files)
        {
            var target = GameService.Inside(destination, file.Path);
            if (File.Exists(target) && new FileInfo(target).Length == file.Bytes && GameService.Hash(target) == file.Sha256) continue;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            var temporary = GameService.Inside(destination, file.Path + ".extracting");
            try
            {
                using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var input = zip.GetEntry(file.Path)!.Open()) input.CopyTo(output);
                if (GameService.Hash(temporary) != file.Sha256) throw new InvalidDataException("Bundled component failed its integrity check: " + file.Path);
                File.Move(temporary, target, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return manifest;
    }
}
