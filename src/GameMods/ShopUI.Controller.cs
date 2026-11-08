using Gatekeeper.Char_Scripts;
using Gatekeeper.General.Currency;
using Gatekeeper.General.InputSystem;
using Gatekeeper.General.InputSystem.Enums;
using Gatekeeper.Items;
using Gatekeeper.Infrastructure.Providers.InputProvider;
using Rewired;
using Gatekeeper.MainMenuScripts.Database.ItemsDatabaseController;
using Gatekeeper.Mods;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static partial class ShopUI
{
    private static bool previousPopup;
    private static readonly List<(ControllerMap Map,bool Enabled)> uiMaps=new();
    private static float controllerReady,repeatAt;
    private static int lastHorizontal,lastVertical;
    private static string ControllerHint=>GameplayInputManager.CurrentDeviceIsJoystick
        ? "UP/DOWN: ITEM   LEFT/RIGHT: GROUP   SHOULDERS: BUY/SELL   CONFIRM: TRADE   BACK: CLOSE\n":"";
    private static void OpenController()
    {
        previousPopup=GameplayInputManager.IsGamepadAndPopupShowing;
        GameplayInputManager.IsGamepadAndPopupShowing=true;
        uiMaps.Clear();
        var provider=GameplayInputManager._inputProvider?.TryCast<RewiredProvider>();
        if(provider!=null)foreach(var device in new[]{InputDeviceType.Keyboard,InputDeviceType.Mouse,InputDeviceType.Joystick})
        {
            var map=provider.GetControllerMap(device,ActionsCategory.UI);
            if(map!=null){uiMaps.Add((map,map.enabled));map.enabled=true;}
        }
        controllerReady=Time.unscaledTime+.25f;repeatAt=0;lastHorizontal=lastVertical=0;
    }
    private static void CloseController()
    {
        GameplayInputManager.IsGamepadAndPopupShowing=previousPopup;
        foreach(var state in uiMaps)state.Map.enabled=state.Enabled;
        uiMaps.Clear();
    }
    private static bool CanTrade(ItemDatabaseInfo info)
    {
        var player=PlayableCharactersHolder.MyCharacter;
        var funds=player!=null?ShopWallet.For(player,CurrencyType.Prism)?.Read():null;
        var count=player!=null?player.ItemManager.GetItemQuantity(info.ItemId):0;
        var price=Price(info);
        var owner=InRestArea && SlotNetwork.Ready && ExchangeClient.Visit.Length>0 && player!=null && player.IsOwner && player.IsActiveAndAlive && funds?.Agrees==true;
        return selling?owner && count>0 && !ExchangeClient.Busy && player!.PrismHolder!=null && price>=0 && ExchangeRules.CanCredit(funds!.Value.Displayed,player.PrismHolder.AccumulatedValue,price)
            :CustomModRules.CanBuy(InRestArea,SlotNetwork.Ready && ExchangeClient.Visit.Length>0,Unlocked(info),DlcAvailable(info),owner,info.IsMaxStacksOrHigher(count),price,funds?.Displayed??0,ExchangeClient.Busy);
    }
    private static void ControllerTick()
    {
        if(!GameplayInputManager.CurrentDeviceIsJoystick || Time.unscaledTime<controllerReady)return;
        searchFocused=false;
        if(GameplayInputManager.ButtonWasPressed(ActionName.UICancel)){Close();return;}
        if(GameplayInputManager.ButtonWasPressed(ActionName.UITabLeft) || GameplayInputManager.ButtonWasPressed(ActionName.UITabRight))
        {selling=!selling;scrollOffset=0;RefreshCatalog();}
        var axes=GameplayInputManager.GetVectorRaw(ActionName.UIHorizontal,ActionName.UIVertical);
        var horizontal=axes.x>.5f?1:axes.x<-.5f?-1:0;
        var vertical=axes.y>.5f?-1:axes.y<-.5f?1:0;
        if(horizontal==0 && vertical==0){lastHorizontal=lastVertical=0;repeatAt=0;}
        else if(horizontal!=lastHorizontal || vertical!=lastVertical || Time.unscaledTime>=repeatAt)
        {
            var first=horizontal!=lastHorizontal || vertical!=lastVertical;
            lastHorizontal=horizontal;lastVertical=vertical;repeatAt=Time.unscaledTime+(first?.32f:.12f);
            if(horizontal!=0){filter=Math.Clamp(filter+horizontal,-1,4);scrollOffset=0;Filter();}
            else if(filtered.Count>0)
            {
                var index=selected==null?0:filtered.FindIndex(x=>x.Info.ItemId==selected.Info.ItemId);
                index=Math.Clamp(index+vertical,0,filtered.Count-1);selected=filtered[index];
                const float height=530,row=71;
                if(index*row<scrollOffset)scrollOffset=index*row;
                if((index+1)*row>scrollOffset+height)scrollOffset=(index+1)*row-height;
            }
        }
        if(GameplayInputManager.ButtonWasPressed(ActionName.UISubmit) && selected!=null && CanTrade(selected.Info))
        {if(selling)ExchangeSales.Sell(selected.Info);else Buy(selected);}
    }
}
