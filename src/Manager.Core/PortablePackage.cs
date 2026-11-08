using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gatekeeper.ModManager;

public sealed record BundleFile(string Path, long Bytes, string Sha256);
public sealed record BundleManifest(string Version, BundleFile[] Files);

// Constructed only from the manifest inside the executable's payload, never from
// a manifest or hashes read back from the writable extraction cache.
public sealed class VerifiedPackage
{
    public string Root { get; }
    public string Version { get; }
    internal IReadOnlyList<BundleFile> Files { get; }
    internal VerifiedPackage(string root, BundleManifest manifest)
    {
        Root = Path.GetFullPath(root);
        Version = manifest.Version;
        Files = Array.AsReadOnly(manifest.Files.ToArray());
    }

    internal void Validate()
    {
        PortablePackage.CheckCache(Root, Files, allowTemporary: false);
        foreach (var file in Files)
        {
            var path = GameService.Inside(Root, file.Path);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Bytes || GameService.Hash(path) != file.Sha256)
                throw new InvalidDataException("A cached component changed. Close and reopen the installer to repair it: " + file.Path);
        }
    }
}

public static class PortablePackage
{
    private sealed class UnexpectedCacheFile(string message) : IOException(message);

    internal static void CheckCache(string destination, IReadOnlyList<BundleFile> files, bool allowTemporary)
    {
        if (!Directory.Exists(destination)) return;
        var expected = files.Select(f => f.Path).ToHashSet(PathIdentity.Comparer);
        if (allowTemporary) expected.UnionWith(files.Select(f => f.Path + ".extracting"));
        foreach (var existing in GameService.SafeFiles(destination))
        {
            var relative = Path.GetRelativePath(destination, existing).Replace('\\', '/');
            GameService.Inside(destination, relative);
            if (!expected.Contains(relative))
                throw new UnexpectedCacheFile("An unexpected cached component was preserved: " + relative);
        }
    }

    public static VerifiedPackage Prepare(Stream source, string destination)
    {
        BundleManifest manifest;
        try { manifest = Extract(source, destination); }
        catch (UnexpectedCacheFile)
        {
            // Keep suspect files for inspection and rebuild only from the embedded
            // ZIP. No unlisted file is copied into the replacement cache.
            var parent = Path.GetDirectoryName(Path.GetFullPath(destination))!;
            var preserved = GameService.Inside(parent, Path.GetFileName(destination) + ".preserved-" + Guid.NewGuid().ToString("N"));
            GameService.Inside(parent, Path.GetFileName(destination));
            Directory.Move(destination, preserved);
            source.Position = 0;
            manifest = Extract(source, destination);
        }
        if (manifest.Version != ExpansionKit.ContentVersion)
            throw new InvalidDataException("The included expansion version does not match this installer.");
        return new VerifiedPackage(destination, manifest);
    }
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
            manifest.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1) ||
            manifest.Files.Any(f => manifest.Files.Any(other => (f.Path + ".extracting").Equals(other.Path, StringComparison.OrdinalIgnoreCase))))
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
        CheckCache(destination, manifest.Files, allowTemporary: true);
        foreach (var file in manifest.Files)
        {
            var target = GameService.Inside(destination, file.Path);
            var temporary = GameService.Inside(destination, file.Path + ".extracting");
            try
            {
                if (File.Exists(target) && new FileInfo(target).Length == file.Bytes && GameService.Hash(target) == file.Sha256) continue;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                // Unlink an interrupted temporary file, then create a new entry
                // atomically. Never truncate a pre-existing hard-linked file.
                if (File.Exists(temporary)) File.Delete(temporary);
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var input = zip.GetEntry(file.Path)!.Open()) input.CopyTo(output);
                if (GameService.Hash(temporary) != file.Sha256) throw new InvalidDataException("Bundled component failed its integrity check: " + file.Path);
                File.Move(temporary, target, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        CheckCache(destination, manifest.Files, allowTemporary: false);
        return manifest;
    }
}
