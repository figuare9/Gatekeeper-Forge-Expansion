using Gatekeeper.CameraScripts.HUD;
using Gatekeeper.Char_Scripts;
using Gatekeeper.Interfaces;
using I2.Loc;

namespace Gatekeeper.CustomMods;

internal static class TerminalPrompt
{
    private static InteractPanel? ownedPanel;
    private static string? lastError;

    internal static void Update()
    {
        try
        {
            if (!Plugin.RestAreaGameplay) { if (ownedPanel != null) Hide(); return; }
            var player = PlayableCharactersHolder.MyCharacter;
            var hud = HUDManager._instance;
            if (player == null || hud == null || !HUDManager.IsReady || HUDManager.IsUiDisabled || !hud.AreAllPopupsClosed())
            { Hide(); return; }
            var slot = WorldSlots.CanOffer(player);
            var anchor = slot ? WorldSlots.Anchor : WorldShop.EmporiumAnchor;
            if (anchor == null || !slot && !WorldShop.CanInteract(player)) { Hide(); return; }
            var panel = hud.InteractPanel;
            if (panel == null || !panel._inited || panel.simplePanelText == null) { Hide(); return; }
            if (ownedPanel != null && ownedPanel.Pointer != panel.Pointer) Hide();

            // Use the Emporium's actual HUD control and native formatting. Its
            // font, outline, position and mapped button hint remain native.
            var label = anchor.TypeLabel;
            var original = panel.GetPanelText(InteractableType.Emporium, label);
            var name = label.ToString();
            var text = !string.IsNullOrWhiteSpace(name) && original.Contains(name)
                ? original.Replace(name, slot ? SlotPlayer.Title(player) : "Item Exchange")
                : panel._interactKeyText + " " + (slot ? SlotPlayer.Title(player) : "Item Exchange");
            if (ownedPanel == null || !panel.simplePanel.gameObject.activeSelf || panel._currentType != InteractableType.Emporium ||
                panel._currentLabel.mTerm != label.mTerm)
                panel.ShowInteractPanel(InteractableType.Emporium, label, null);
            ownedPanel = panel;
            if (panel.simplePanelText.text != text) panel.simplePanelText.text = text;
            WorldSlots.Focus(slot); WorldShop.Focus(!slot);
        }
        catch (Exception ex)
        {
            if (lastError != ex.Message) { lastError = ex.Message; Plugin.Logger.LogWarning("Terminal interaction prompt: " + ex); }
            Hide();
        }
    }

    internal static void Hide()
    {
        WorldSlots.Focus(false); WorldShop.Focus(ShopUI.IsOpen);
        var panel = ownedPanel;
        ownedPanel = null;
        if (panel == null) return;
        try
        {
            panel.HideInteractPanel();
            var player = PlayableCharactersHolder.MyCharacter;
            var hud = HUDManager._instance;
            if (!Plugin.RestAreaGameplay || ShopUI.IsOpen || player == null || player.Controller.IsInputLocked || hud == null ||
                HUDManager.IsUiDisabled || !hud.AreAllPopupsClosed()) return;
            var current = WorldShop.NativeSelection(player);
            if (current != null && current.IsActive && current.IsCanInteract)
                panel.ShowInteractPanel(current.InteractableType, current.TypeLabel, current.TryCast<ILocalizationParamsManager>());
        }
        catch (Exception ex) { Plugin.Current?.Log.LogWarning("Terminal prompt restore: " + ex.Message); }
    }
}
