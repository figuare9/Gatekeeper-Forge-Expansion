using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Gatekeeper.Mods;
using HarmonyLib;

namespace Gatekeeper.SubtleBalance;

[BepInPlugin(Id, "Gatekeeper Subtle Balance", "0.1.0")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.gatekeeper.subtlebalance";
    // Hooks were inspected against this exact native build. An update must be revalidated.
    public const string SupportedAssembly = "2f59b50a1dde831a32006b419adbf560a195b0de638d0669bac8c029e85d9ce9";
    public const string SupportedMetadata = "e0f9349d0d37aa0684243c63a72057c238f104e0c67abf150d7ac9cf8918b009";
    private static Plugin? current;
    private Func<bool>? isSinglePlayer;
    private Harmony? harmony;
    private ConfigEntry<bool> enabled = null!, movement = null!, damage = null!, cooldown = null!;
    private ConfigEntry<int> movementPercent = null!, damagePercent = null!, cooldownPercent = null!;
    private readonly Dictionary<string, int> hits = new() { ["movement"] = 0, ["damage"] = 0, ["cooldown"] = 0 };
    private readonly List<string> installedHooks = new();
    private bool gateErrorLogged;

    public override void Load()
    {
        current = this;
        enabled = Config.Bind("General", "Enabled", true, "Master switch. Changes take effect on the next game launch.");
        movement = Config.Bind("Movement", "Enabled", false, "Increase player movement speed in single-player.");
        movementPercent = Percent("Movement", 5);
        damage = Config.Bind("Damage", "Enabled", false, "Increase player final damage in single-player.");
        damagePercent = Percent("Damage", 5);
        cooldown = Config.Bind("Cooldown", "Enabled", false, "Shorten player skill recharge durations in single-player.");
        cooldownPercent = Percent("Cooldown", 5);
        if (!enabled.Value || !(movement.Value || damage.Value || cooldown.Value))
        {
            Log.LogInfo("Optional balance adjustments are off; native movement, damage and cooldown methods are left unpatched.");
            Status("inactive");
            return;
        }
        try
        {
            if (!Matches(Path.Combine(Paths.GameRootPath, "GameAssembly.dll"), SupportedAssembly) ||
                !Matches(Path.Combine(Paths.GameRootPath, "Gatekeeper_Data", "il2cpp_data", "Metadata", "global-metadata.dat"), SupportedMetadata))
                throw new InvalidOperationException("Game build changed. All balance hooks are disabled until this build is checked.");

            var game = Assembly.Load("Gatekeeper");
            var gate = game.GetType("Gatekeeper.Network.NetworkManagerController", true)!
                .GetProperty("IsSinglePlayer", BindingFlags.Public | BindingFlags.Static)?.GetMethod
                ?? throw new MissingMethodException("Single-player detection was not found.");
            isSinglePlayer = (Func<bool>)gate.CreateDelegate(typeof(Func<bool>));
            harmony = new Harmony(Id);
            Hook(game, "CharDataMoveSpeed", "get_BaseMoveSpeed", typeof(float), Type.EmptyTypes, nameof(MovementPostfix));
            Hook(game, "CharDataDamage", "get_FinalDamage", typeof(float), Type.EmptyTypes, nameof(DamagePostfix));
            Hook(game, "CharDataSkillItem", "Cooldown", null, new[] { typeof(float) }, nameof(CooldownPrefix), prefix: true);
            Log.LogInfo("Subtle Balance ready: 3 hooks installed; changes are gated by the game's IsSinglePlayer property.");
            Status("ready");
        }
        catch (Exception ex)
        {
            harmony?.UnpatchSelf();
            installedHooks.Clear();
            isSinglePlayer = null;
            Log.LogError($"Balance mods disabled: {ex}");
            Status("disabled", ex.Message);
        }
    }

    private ConfigEntry<int> Percent(string section, int value) => Config.Bind(section, "Percent", value,
        new ConfigDescription("Percent adjustment, 0 to 20.", new AcceptableValueRange<int>(0, 20)));

    private static bool Matches(string path, string expected)
    {
        using var stream = File.OpenRead(path);
        using var algorithm = SHA256.Create();
        return Convert.ToHexString(algorithm.ComputeHash(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private void Hook(Assembly game, string type, string method, Type? returnType, Type[] args, string patch, bool prefix = false)
    {
        var target = game.GetType("Gatekeeper.Char_Scripts.General.CharData." + type, true)!
            .GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null)
            ?? throw new MissingMethodException(type, method);
        if (returnType != null && target.ReturnType != returnType) throw new InvalidOperationException("Hook signature changed: " + method);
        var handler = new HarmonyMethod(typeof(Plugin).GetMethod(patch, BindingFlags.Static | BindingFlags.NonPublic)!);
        harmony!.Patch(target, prefix: prefix ? handler : null, postfix: prefix ? null : handler);
        installedHooks.Add(type + "." + method);
        Log.LogInfo("Installed hook: " + installedHooks[^1]);
    }

    private bool Active(ConfigEntry<bool> toggle)
    {
        if (!enabled.Value || !toggle.Value || isSinglePlayer == null) return false;
        try { return isSinglePlayer(); }
        catch (Exception ex)
        {
            // Fail closed if the game is between scenes or its network state is unavailable.
            if (!gateErrorLogged) { Log.LogWarning("Single-player check unavailable; keeping vanilla values: " + ex.Message); gateErrorLogged = true; }
            return false;
        }
    }

    private static void MovementPostfix(ref float __result)
    {
        var p = current;
        if (p == null || !p.Active(p.movement)) return;
        var original = __result;
        __result = BalanceMath.Increase(original, p.movementPercent.Value);
        p.Record("movement", original, __result);
    }

    private static void DamagePostfix(ref float __result)
    {
        var p = current;
        if (p == null || !p.Active(p.damage)) return;
        var original = __result;
        __result = BalanceMath.Increase(original, p.damagePercent.Value);
        p.Record("damage", original, __result);
    }

    private static void CooldownPrefix(ref float __0)
    {
        var p = current;
        if (p == null || !p.Active(p.cooldown)) return;
        var original = __0;
        __0 = BalanceMath.Shorten(original, p.cooldownPercent.Value);
        p.Record("cooldown", original, __0);
    }

    private void Record(string key, float original, float changed)
    {
        if (original == changed) return;
        hits[key]++;
        if (hits[key] != 1) return;
        Log.LogInfo($"Applied {key} in single-player: {original:0.###} -> {changed:0.###}");
        Status("ready");
    }

    private void Status(string state, string? error = null)
    {
        try
        {
            var path = Path.Combine(Paths.ConfigPath, Id + ".status.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { state, error, processId = Environment.ProcessId,
                loadedUtc = DateTime.UtcNow, hooks = installedHooks, firstApplied = hits.Where(x => x.Value > 0).Select(x => x.Key).ToArray() }));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception ex) { Log.LogWarning("Could not write mod status: " + ex.Message); }
    }

    public override bool Unload() { harmony?.UnpatchSelf(); current = null; return true; }
}
