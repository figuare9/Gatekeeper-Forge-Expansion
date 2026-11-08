using System.Text.Json;
using BepInEx;
using Gatekeeper.Char_Scripts;
using Gatekeeper.General.Predictor;
using Gatekeeper.General.SaveLoad;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Utility.SceneHelper;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Gatekeeper.CustomMods;

public sealed class ShopBehaviour : MonoBehaviour
{
    private static bool catalogWritten;
    private static float nextCheck;
    public ShopBehaviour(IntPtr pointer) : base(pointer) { }

    public void Update()
    {
        ExpansionMusic.Tick();
        try
        {
            ForgeRegistry.Tick();
            ForgeLobby.Tick();
            AspectRegistry.Tick();
            AscendantDifficulty.Tick();
            CannonRegistry.Tick();
            SlotNetwork.Tick();
            ForgeUnlock.Tick();
            ForgeWorld.PollInput();
            WorldShop.Tick();
            WorldSlots.Tick();
            ExchangeAuthority.Tick();
            SlotAuthority.Tick();
            SlotPlayer.Tick();
            SlotAudio.Tick();
            ExchangeClient.Tick();
            CannonWorld.Tick();
            ForgeWorld.Tick();
            ShopUI.Tick();
            if (ShopUI.IsOpen && Keyboard.current?.escapeKey.wasPressedThisFrame == true) ShopUI.Close();
        }
        catch (Exception ex) { ShopUI.ReportError(ex); }
        if (Plugin.Current == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + .5f;
        try
        {
            ItemCaps.Ensure();
            if (!catalogWritten && DatabaseInfoProvider.Items != null)
            {
                var provider = DatabaseInfoProvider.Items;
                var items = new List<object>();
                foreach (var pair in provider.ItemInfos)
                {
                    var item = pair.Value;
                    var parameters = new List<object>();
                    foreach (var p in item.modificationParams) parameters.Add(new { type = p.modificationType.ToString(), first = p.firstLevelModificationValue, subsequent = p.otherLevelModificationValue });
                    items.Add(new { id = item.ItemId.ToString(), unlocked = SaveLoadManager.GetUnlockedStatus(item.Id), assetDefaultUnlocked = item.unlocked, item.itemMaxCount, currency = item.currency.ToString(), item.itemCost,
                        item.itemObeliskCost, item.itemPedestalCost, item.itemCostStep, item.itemUnlockCost, rarity = item.itemType.ToString(), name = item.itemNameKey.ToString(), parameters });
                }
                File.WriteAllText(Path.Combine(Paths.ConfigPath, Plugin.Id + ".catalog.json"), JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
                catalogWritten = true; Plugin.WriteStatus("ready");
            }
        }
        catch (Exception ex) { Plugin.Logger.LogWarning("Catalog initialization: " + ex.Message); }
    }

    public void OnGUI()
    {
        if (Plugin.Current == null) return;
        try { ShopUI.Draw(); CannonProgress.OnGui(); ForgeWorld.OnGui(); } catch (Exception ex) { ShopUI.ReportError(ex); }
    }

    public void LateUpdate()
    {
        if (Plugin.Current != null) { TerminalPrompt.Update(); CannonWorld.Render(); ForgeAvatar.Tick(); ForgeWorld.Render(); }
    }

    public void OnDestroy() { ForgeUnlock.Clear(); ExpansionMusic.Clear(); ShopUI.Close(force: true); SlotNetwork.Detach(); WorldSlots.Clear(); WorldShop.Clear(); CannonWorld.Clear(); ForgeWorld.Clear(); }
}
