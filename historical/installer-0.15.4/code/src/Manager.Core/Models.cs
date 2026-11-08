using System.Text.Json;

namespace Gatekeeper.ModManager;

public sealed record BalanceProfile
{
    public bool Enabled { get; set; } = true;
    public bool Movement { get; set; }
    public bool Damage { get; set; }
    public bool Cooldown { get; set; }
    public bool Metalheads25 { get; set; } = false; // Legacy key; expanded capacity was removed.
    public bool QuietBayShop { get; set; } = true;
    public bool PrismJackpot { get; set; } = true;
    public bool Forge {get;set;} = true;
    public bool CannonWheel { get; set; } = true;
    public int MovementPercent { get; set; } = 5;
    public int DamagePercent { get; set; } = 5;
    public int CooldownPercent { get; set; } = 5;
    public void Validate()
    {
        if (new[] { MovementPercent, DamagePercent, CooldownPercent }.Any(x => x < 0 || x > 20))
            throw new InvalidDataException("Each adjustment must be between 0% and 20%.");
    }
    public string Config()
    {
        Validate();
        return $"# Managed by Gatekeeper Mod Manager. Restart the game after changes.\n[General]\nEnabled = {Enabled.ToString().ToLowerInvariant()}\n\n[Movement]\nEnabled = {Movement.ToString().ToLowerInvariant()}\nPercent = {MovementPercent}\n\n[Damage]\nEnabled = {Damage.ToString().ToLowerInvariant()}\nPercent = {DamagePercent}\n\n[Cooldown]\nEnabled = {Cooldown.ToString().ToLowerInvariant()}\nPercent = {CooldownPercent}\n";
    }
    public string CustomConfig() => $"# Managed by Gatekeeper Mod Manager. Restart Gatekeeper after changes.\n[Metalheads]\nEnabled = false\n\n[QuietBayShop]\nEnabled = {QuietBayShop.ToString().ToLowerInvariant()}\n\n[PrismJackpot]\nEnabled = {PrismJackpot.ToString().ToLowerInvariant()}\n\n[CannonWheel]\nEnabled = {CannonWheel.ToString().ToLowerInvariant()}\n\n[Forge]\nEnabled = {Forge.ToString().ToLowerInvariant()}\n";
}

public sealed record OwnedFile(string RelativePath, string Sha256);
public sealed record InstallReceipt
{
    public int Version { get; init; } = 1;
    public required string GameRoot { get; init; }
    public required string AssemblyHash { get; init; }
    public required string MetadataHash { get; init; }
    public bool Complete { get; set; }
    public List<OwnedFile> Files { get; init; } = new();
}

public sealed record GameInfo(string Root, string UnityVersion, string AssemblyHash, string MetadataHash, bool Supported);
public sealed record ExternalMod(string Name, string RelativePath, bool Enabled);

public static class Storage
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", content);
        File.Move(path + ".tmp", path, true);
    }
    public static void Json<T>(string path, T value) => Write(path, JsonSerializer.Serialize(value, JsonOptions));
    public static T? Read<T>(string path) => File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default;
}
