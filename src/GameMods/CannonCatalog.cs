using Gatekeeper.Achievements.Data;
using Gatekeeper.CameraScripts.HUD.Emporium;
using Gatekeeper.DownloadableContent;
using Gatekeeper.General.SaveLoad;
using Gatekeeper.General.SaveLoad.Achievements;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Items;
using Gatekeeper.MainMenuScripts.MainMenu.StorePanel;
using Gatekeeper.Mods;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// Native Emporium groups are separate from the item database. Supply local
// achievement metadata without registering a Steam achievement/controller or
// adding an unknown achievement ID to the game's persisted achievement file.
internal static class CannonCatalog
{
    internal static readonly AchievementID Id=(AchievementID)CannonRules.CatalogAchievementId;
    private static AchievementInfo? achievement;
    private static AchievementElementData? progress;
    private static StoreAchievementInfo? group;
    private static bool busy;
    private static string? error;

    internal static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(EmporiumPopupController),"AchievementPanelsInit"),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(BeforePanels)));
        harmony.Patch(AccessTools.Method(typeof(EmporiumPopupController),nameof(EmporiumPopupController.Show)),
            postfix:new HarmonyMethod(typeof(CannonCatalog),nameof(Shown)));
        harmony.Patch(AccessTools.Method(typeof(StoreAchievementsInfoProvider),nameof(StoreAchievementsInfoProvider.SetupItemsStatus)),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(BeforeStatus)));
        harmony.Patch(AccessTools.Method(typeof(ItemsInfoProvider),"GetAvailableItemsInternal"),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(BeforePool)));
        harmony.Patch(AccessTools.Method(typeof(AchievementsInfoProvider),nameof(AchievementsInfoProvider.GetByLocalId)),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(GetProgress)));
        harmony.Patch(AccessTools.Method(typeof(AchievementsInfoProvider),nameof(AchievementsInfoProvider.GetInfo)),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(GetInfo)));
        harmony.Patch(AccessTools.Method(typeof(AchievementsInfoProvider),nameof(AchievementsInfoProvider.IsUnlocked),new[]{typeof(AchievementID)}),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(IsUnlocked)));
        harmony.Patch(AccessTools.Method(typeof(AchievementsSaveData),nameof(AchievementsSaveData.SetWasSeen)),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(SeenAchievement)));
        harmony.Patch(AccessTools.Method(typeof(SaveLoadManager),nameof(SaveLoadManager.SetUnlockedStatus)),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(SetUnlocked)));
        harmony.Patch(AccessTools.Method(typeof(SaveLoadManager),nameof(SaveLoadManager.GetWasSeenStatus)),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(GetSeenItem)));
        harmony.Patch(AccessTools.Method(typeof(SaveLoadManager),nameof(SaveLoadManager.SetWasSeenStatus)),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(SetSeenItem)));
        harmony.Patch(AccessTools.Method(typeof(EmporiumPopupController),"TryBuyItem"),
            prefix:new HarmonyMethod(typeof(CannonCatalog),nameof(TryUnlockByCurrency)));
    }
    private static void Report(Exception ex)
    {if(error!=ex.Message){error=ex.Message;Plugin.Logger.LogError("Cannon Wheel native catalog: "+ex);}}
    private static void BeforePanels()=>Ensure();
    private static void BeforeStatus(StoreAchievementsInfoProvider __instance,AchievementsInfoProvider __0)=>Ensure(__instance,__0);
    private static void BeforePool(ItemsInfoProvider __instance)
    {
        try{CannonRegistry.Ensure(__instance);CannonRegistry.RefreshAvailability();}
        catch(Exception ex){Report(ex);}
    }
    internal static void Ensure(StoreAchievementsInfoProvider? stores=null,AchievementsInfoProvider? achievements=null)
    {
        if(busy)return;busy=true;
        try
        {
            if(progress!=null)progress.Unlocked=CannonProgress.Unlocked;
            if(!CannonRegistry.Enabled)return;
            stores??=DatabaseInfoProvider.StoreAchievements;achievements??=DatabaseInfoProvider.Achievements;
            var items=DatabaseInfoProvider.Items;
            if(stores==null || achievements==null || items==null)return;
            CannonRegistry.Ensure(items);CannonRegistry.RefreshAvailability();
            var item=CannonRegistry.Info;if(item==null)return;
            if(achievements.AchievementsInfos.ContainsKey(Id))throw new InvalidOperationException("Cannon Wheel's local catalog achievement ID is occupied; refusing to replace it.");
            if(achievement==null)
            {
                if(!achievements.AchievementsInfos.TryGetValue(AchievementID.HalfmetalHead,out var template))return;
                achievement=Object.Instantiate(template);Object.DontDestroyOnLoad(achievement);
                achievement.name="GKMM Cannon Wheel local unlock";achievement.LocalID=Id;
                achievement.IsSteamAchievement=false;achievement.SteamAchievementObject=null;achievement.SteamIntStatObject=null;
                achievement.InGamePopup=false;achievement.Category=AchievementCategory.Achievement;achievement.Group=AchievementGroup.Yellow;
                achievement.TitleKey=(LocalizedString)(CannonRules.Key+".group-name");
                achievement.DescriptionKey=(LocalizedString)(CannonRules.Key+".unlock");
                progress=new AchievementElementData(achievement){WasSeen=CannonProgress.Unlocked};
            }
            progress!.Unlocked=CannonProgress.Unlocked;progress.IntProgress=progress.Unlocked ? 1:0;
            var found=false;var lastIndex=0;
            foreach(var entry in stores.StoreAchievementInfos)
            {
                lastIndex=Math.Max(lastIndex,entry.Index);
                if(entry.AchievementID!=Id)continue;
                if(entry.Items==null || entry.Items.Length!=1 || !CannonRegistry.Is(entry.Items[0]))
                    throw new InvalidOperationException("Another Emporium group occupies Cannon Wheel's ID.");
                group=entry;found=true;
            }
            if(group==null)
            {
                group=ScriptableObject.CreateInstance<StoreAchievementInfo>();Object.DontDestroyOnLoad(group);
                group.name="GKMM Cannon Wheel Emporium group";group.Index=lastIndex+1;group.AchievementID=Id;group.DlcId=DlcID.None;
            }
            if(group.Items==null || group.Items.Length!=1 || group.Items[0]?.Pointer!=item.Pointer)group.Items=new[]{item};
            if(!found)
            {
                stores.StoreAchievementInfos.Add(group);
                Plugin.Logger.LogInfo("Cannon Wheel added to native Emporium groups; unlock: "+CannonRules.UnlockRequirement);
            }
            CannonRegistry.SetRelatedData(items,item,group);
        }
        catch(Exception ex){Report(ex);}
        finally{busy=false;}
    }
    private static bool GetProgress(AchievementID __0,ref AchievementElementData __result)
    {
        if(__0!=Id)return true;
        Ensure();if(progress==null)return true;__result=progress;return false;
    }
    private static bool GetInfo(AchievementID __0,ref AchievementInfo __result)
    {if(__0!=Id)return true;Ensure();if(achievement==null)return true;__result=achievement;return false;}
    private static bool IsUnlocked(AchievementID __0,ref bool __result)
    {if(__0!=Id || achievement==null)return true;__result=CannonProgress.Unlocked;return false;}
    private static bool SeenAchievement(AchievementID __0)
    {if(__0!=Id || progress==null)return true;progress.WasSeen=true;return false;}
    private static bool SetUnlocked(string __0)=>__0!=CannonRules.Key;
    private static bool GetSeenItem(string __0,ref bool __result)
    {if(__0!=CannonRules.Key)return true;__result=CannonRegistry.Info?.wasSeen==true;return false;}
    private static bool SetSeenItem(string __0,bool __1)
    {if(__0!=CannonRules.Key)return true;if(CannonRegistry.Info!=null)CannonRegistry.Info.wasSeen=__1;return false;}
    private static bool TryUnlockByCurrency(Gatekeeper.MainMenuScripts.Database.ItemsDatabaseController.ItemDatabaseInfo __0,ref bool __result)
    {
        if(!CannonRegistry.Is(__0))return true;
        __result=false;return false; // The jackpot is the only unlock mechanism.
    }
    private static void Shown(EmporiumPopupController __instance)
    {
        try
        {
            Ensure();
            foreach(var panel in __instance._achievementPanels)
                if(panel.StoreAchievementInfo?.AchievementID==Id){panel.Refresh();panel.UpdateLocalization();panel.CheckPurchased();}
            __instance.RefreshUnlockedAchievementsCount();__instance.RefreshUnlockedItemsCount();
        }
        catch(Exception ex){Report(ex);}
    }
}
