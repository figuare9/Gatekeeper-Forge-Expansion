using System.Security.Cryptography;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Items;
using Gatekeeper.MainMenuScripts.Database.ItemsDatabaseController;
using Gatekeeper.Network;
using HarmonyLib;
using Gatekeeper.Mods;
using Gatekeeper.General.Predictor;
using Gatekeeper.Utility.SceneHelper;

namespace Gatekeeper.CustomMods;

[BepInPlugin(Id, "Gatekeeper Custom Mods", Version)]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.gatekeeper.custommods";
    public const string Version = "0.15.4";
    internal static Plugin? Current;
    internal ConfigEntry<bool> Shop = null!, Slots = null!, Cannon = null!, Forge = null!;
    internal ConfigEntry<bool> PreviewAtMenu = null!;
    internal static ManualLogSource Logger => Current!.Log;
    internal static string StatusFile => Path.Combine(Paths.ConfigPath, Id + ".status.json");
    private Harmony? harmony,forgeHarmony;
    public override void Load()
    {
        Current = this;
        Shop = Config.Bind("QuietBayShop", "Enabled", true, "Add a co-op Item Exchange beside the Emporium in Quiet Bay and Quiet Place. Buy at live Quiet Bay price plus 26.5%; sell owned non-Triad items for 50% of native value. Requires this mod on host and buyers.");
        Slots = Config.Bind("PrismJackpot", "Enabled", true, "Add a co-op compatible slot machine opposite the Emporium in Quiet Bay and Quiet Place. First approach locks 20% of your Prisms as the bet for this rest-area visit; 20% win chance; 5x total payout. Requires the mod on the host and participating clients.");
        PreviewAtMenu = Config.Bind("Development", "PreviewAtMenu", false, "Read-only shop preview at the main menu. Purchases remain restricted to Quiet Bay and Quiet Place.");
        Cannon=Config.Bind("CannonWheel","Enabled",true,"Enable Cannon Wheel, the jackpot-unlocked Amulet. Disabling stops its effects and drops but retains its item definition for existing modded saves.");
        Forge=Config.Bind("Forge","Enabled",true,"Enable Forge with the custom rigged model, two-handed rivet gun, animations and piercing projectiles. Co-op requires matching enabled Forge on every player. Finish any Forge run before disabling.");
        try
        {
            VerifyBuild();
            harmony = new Harmony(Id);
            GameWorldIndex.Patch(harmony);
            AspectRegistry.Patch(harmony);
            ExpansionMusic.Patch(harmony);
            CannonRegistry.Patch(harmony);
            ItemCaps.Patch(harmony);
            harmony.Patch(AccessTools.Method(typeof(CharManager), "OnOwnerUpdate"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(OwnerInputPrefix)));
            try
            {
                forgeHarmony=new Harmony(Id+".forge");ForgeRegistry.Patch(forgeHarmony);ForgeRegistry.PatchesReady=true;
            }
            catch(Exception ex){forgeHarmony?.UnpatchSelf();Log.LogError("Forge prototype disabled; existing mods remain available: "+ex);}
            AddComponent<ShopBehaviour>();
            Log.LogInfo("Custom mods ready: Forge; Chaos, Scaling and Elite Aspects; native Halfmetal Head limits; item caps 25; Quiet Bay Item Exchange; co-op Prism Jackpot; jackpot-unlocked Cannon Wheel Amulet.");
            WriteStatus("ready");
        }
        catch (Exception ex)
        {
            harmony?.UnpatchSelf();
            Log.LogError(ex);
            WriteStatus("disabled", ex.Message);
            Current = null;
        }
    }

    internal static bool Solo()
    {
        try { return Current != null && NetworkManagerController.IsSinglePlayer; }
        catch { return false; }
    }
    internal static bool ShopActive => Current?.Shop.Value == true;
    internal static bool RestAreaScene => Current != null && CustomModRules.IsRestAreaScene((int)GlobalPredictor.CurrentScene);
    internal static bool RestAreaGameplay => RestAreaScene && GlobalPredictor.GameState == GameState.Gameplay;
    internal static bool SlotsInRestArea => Current?.Slots.Value == true && RestAreaGameplay;

    private static void VerifyBuild()
    {
        foreach (var (file, expected) in new[]
        {
            ("GameAssembly.dll", "2f59b50a1dde831a32006b419adbf560a195b0de638d0669bac8c029e85d9ce9"),
            ("Gatekeeper_Data/il2cpp_data/Metadata/global-metadata.dat", "e0f9349d0d37aa0684243c63a72057c238f104e0c67abf150d7ac9cf8918b009")
        })
        {
            using var stream = File.OpenRead(Path.Combine(Paths.GameRootPath, file)); using var hash = SHA256.Create();
            if (!Convert.ToHexString(hash.ComputeHash(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Game build changed; custom mods require revalidation.");
        }
    }

    private static bool OwnerInputPrefix(CharManager __instance)
    {
        if (!RestAreaGameplay || Gatekeeper.Char_Scripts.PlayableCharactersHolder.MyCharacter?.Pointer != __instance.Pointer) return true;
        try { return !ShopUI.IsOpen && !SlotPlayer.OwnsInput(__instance) && !SlotPlayer.TryInteract(__instance) && !WorldShop.TryInteract(__instance); }
        catch (Exception ex) { ShopUI.ReportError(ex); return true; }
    }

    internal static void WriteStatus(string state, string? error = null)
    {
        try
        {
            File.WriteAllText(StatusFile + ".tmp", JsonSerializer.Serialize(new { state, error, processId = Environment.ProcessId,
                utc = DateTime.UtcNow, version = Version, metalheads = false, shop = Current?.Shop.Value, slots = Current?.Slots.Value, cannonWheel = Current?.Cannon.Value, cannonItemId = CannonRules.ItemId, forge = Current?.Forge.Value, forgePrototype = true, forgePatched = ForgeRegistry.PatchesReady, forgeRegistered = ForgeRegistry.Info!=null, forgePrefabReady = ForgeRegistry.PrefabReady, forgeMenuReady = ForgeRegistry.MenuReady, forgeError = ForgeRegistry.Error,
                forgeCustomModels = ForgeAvatar.Count, forgeAssetError = ForgeAvatar.Error,
                ascendantActive=AscendantDifficulty.Active,ascendantPower=AscendantDifficulty.Power,ascendantError=AscendantDifficulty.Error,
                expansionAspectsRegistered=AspectRegistry.Registered,expansionAspectsError=AspectRegistry.Error,expansionAspectMechanicsError=AspectGameplay.Error,
                expansionScaledSpawns=AspectGameplay.ScaledSpawns,expansionForcedElites=AspectGameplay.ForcedElites,
                forgeRivetShots=ForgeWorld.RivetShots,forgeRivetTargets=ForgeWorld.RivetTargets,forgeRivetConfirmed=ForgeWorld.RivetConfirmed,forgeRivetWallStops=ForgeWorld.RivetWallStops,
                forgeInput=ForgeWorld.InputState,
                forgeReceiptStreams=ForgeWorld.ReceiptStreams,forgeCombatError=ForgeWorld.CombatError,
                cappedItems = 25, expandedMetalheads = false }));
            File.Move(StatusFile + ".tmp", StatusFile, true);
        }
        catch (Exception ex) { Current?.Log.LogWarning(ex.Message); }
    }

    public override bool Unload()
    {
        ShopUI.Close(force: true);
        SlotNetwork.Detach(); WorldSlots.Clear();
        WorldShop.Clear();
        ItemCaps.Restore();
        ForgeUnlock.Clear();
        ForgeWorld.Clear();ForgeAvatar.Clear();ForgeLobby.Detach();forgeHarmony?.UnpatchSelf();
        ExpansionMusic.Clear();
        harmony?.UnpatchSelf(); Current = null; return true;
    }
}
