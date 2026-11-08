using BepInEx;
using Gatekeeper.Char_Scripts;
using Gatekeeper.General.Predictor;
using Gatekeeper.General.SaveLoad;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Items;
using Gatekeeper.MainMenuScripts.Database.ItemsDatabaseController;
using Gatekeeper.MainMenuScripts.MainMenu.StorePanel;
using Gatekeeper.Mods;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static class CannonRegistry
{
    internal static readonly ItemID Id=(ItemID)CannonRules.ItemId;
    internal static bool Enabled=>Plugin.Current?.Cannon.Value==true;
    internal static ItemDatabaseInfo? Info { get; private set; }
    private static float next;
    private static string? error;
    private static readonly Dictionary<string,string> terms=new()
    {
        [CannonRules.Key+".name"]="Cannon Wheel",
        [CannonRules.Key+".group-name"]="CANNON WHEEL",
        [CannonRules.Key+".unlock"]=CannonRules.UnlockRequirement,
        [CannonRules.Key+".lore"]="The house keeps its winnings. You keep the artillery.",
        [CannonRules.Key+".stats"]="Summons a wheeled cannon per stack that roams and hunts enemies during combat levels.\nCannons: 1 (+1 per stack)\nDamage per cannon: 250%\nFire interval per cannon: 2 seconds\nRange: 24.3 m\nBlast radius: 2.4 m\nMaximum 10 cannons."
    };
    internal static void Patch(Harmony harmony)
    {
        CannonCatalog.Patch(harmony);
        harmony.Patch(AccessTools.Constructor(typeof(ItemsInfoProvider)),postfix:new HarmonyMethod(typeof(CannonRegistry),nameof(ProviderReady)));
        harmony.Patch(AccessTools.Method(typeof(SaveLoadManager),nameof(SaveLoadManager.GetUnlockedStatus)),
            prefix:new HarmonyMethod(typeof(CannonRegistry),nameof(Unlocked)));
        foreach(var name in new[]{nameof(LocalizationManager.GetTranslation),nameof(LocalizationManager.GetTranslationByManager)})
            harmony.Patch(AccessTools.Method(typeof(LocalizationManager),name),prefix:new HarmonyMethod(typeof(CannonRegistry),nameof(Translation)));
        harmony.Patch(AccessTools.Method(typeof(LocalizationManager),nameof(LocalizationManager.TryGetTranslation)),
            prefix:new HarmonyMethod(typeof(CannonRegistry),nameof(TryTranslation)));
    }
    internal static void SetRelatedData(ItemsInfoProvider provider,ItemDatabaseInfo info,StoreAchievementInfo? group=null)
    {
        // ItemRelatedData contains native references and is represented by a
        // boxed wrapper in interop. The generic dictionary setter copies that
        // box's header as the struct payload. Native IDictionary unboxes it.
        var key=Il2CppSystem.Enum.ToObject(Il2CppInterop.Runtime.Il2CppType.Of<ItemID>(),(int)Id);
        var related=new ItemsInfoProvider.ItemRelatedData(info,provider.GetItemTypeInfo(info.itemType),null,group);
        provider.ItemRelatedDatas.System_Collections_IDictionary_set_Item(key,related);
        if(provider.GetItemRelatedData(Id).ItemInfo?.Pointer!=info.Pointer)
            throw new InvalidOperationException("Cannon Wheel tooltip metadata failed its native round trip.");
    }
    private static void ProviderReady(ItemsInfoProvider __instance)
    { try { Ensure(__instance); } catch(Exception ex) { Report(ex); } }
    private static bool Unlocked(string __0,ref bool __result)
    { if(__0!=CannonRules.Key)return true;__result=Enabled && CannonProgress.Unlocked;return false; }
    private static bool Translation(string __0,ref string __result)
    { if(!terms.TryGetValue(__0 ?? "",out var value))return true;__result=value;return false; }
    private static bool TryTranslation(string __0,ref string __1,ref bool __result)
    { if(!terms.TryGetValue(__0 ?? "",out var value))return true;__1=value;__result=true;return false; }
    internal static bool Is(ItemDatabaseInfo info)=>info.ItemId==Id && info.Id==CannonRules.Key;
    internal static bool HostEligible(ItemDatabaseInfo info,int owner)=>Is(info) ? Enabled && CannonProgress.ForOwner(owner) : ShopUI.LocallyEligible(info);
    internal static void Tick()
    {
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+.5f;
        try
        {
            CannonProgress.Tick();
            var provider=DatabaseInfoProvider.Items;
            if(provider!=null)Ensure(provider);
            RefreshAvailability();CannonCatalog.Ensure();
        }
        catch(Exception ex){Report(ex);}
    }
    internal static void RefreshAvailability()
    {
        if(Info==null)return;
        Info.unlocked=Enabled && CannonProgress.Unlocked;
        Info.defaultDropSource=Info.unlocked ? ItemDropSource.All:ItemDropSource.None;
        Info.arenaDropSource=Info.defaultDropSource;
    }
    private static void Report(Exception ex)
    {if(error==ex.Message)return;error=ex.Message;Plugin.Logger.LogError("Cannon Wheel registration: "+ex);}
    internal static void Ensure(ItemsInfoProvider provider)
    {
        if(provider.ItemInfos.TryGetValue(Id,out var existing))
        {
            if(existing.Id!=CannonRules.Key)throw new InvalidOperationException("Another item occupies Cannon Wheel's ID; refusing to replace it.");
            Info=existing;return;
        }
        if(!provider.ItemInfos.TryGetValue(ItemID.HalfmetalHead,out var template) || template==null)return;
        CannonArt.Prepare(template.ItemFbx!=null ? template.ItemFbx.sharedMaterial:null);
        var info=Object.Instantiate(template);
        Object.DontDestroyOnLoad(info);
        info.name="GKMM Cannon Wheel";info.Id=CannonRules.Key;info.ItemId=Id;
        info.itemType=ItemType.Amulets;info.itemMaxCount=CannonRules.MaxStacks;info.hideInDatabase=false;info.hideInForge=false;
        info.dlcId=Gatekeeper.DownloadableContent.DlcID.None;
        info.ignoreByConstructs=true;info.itemUnlockCost=0;info.unlocked=Enabled && CannonProgress.Unlocked;info.wasSeen=CannonProgress.Unlocked;
        info.modificationParams=new Il2CppSystem.Collections.Generic.List<ParamModification>();
        info.ProjectileInfos=new Il2CppSystem.Collections.Generic.List<ItemProjectileInfo>();
        info.triadItems=new Il2CppSystem.Collections.Generic.List<ItemDatabaseInfo>();
        info.ItemFbx=CannonArt.Pickup;info.DatabaseIcon=CannonArt.Icon;
        info.itemNameKey=(LocalizedString)(CannonRules.Key+".name");
        info.itemLongDescKey=(LocalizedString)(CannonRules.Key+".lore");
        info.itemStatsKey=info.itemStatsKeyAlternative=(LocalizedString)(CannonRules.Key+".stats");
        info.defaultDropSource=info.unlocked ? ItemDropSource.All:ItemDropSource.None;info.arenaDropSource=info.defaultDropSource;
        // Add every native lookup used by inventory cells, stats and item pools.
        provider._itemParamGrowthTypes[Id]=ItemParamGrowthType.Linear;
        provider._ignoredByConstructs[Id]=info;
        SetRelatedData(provider,info);
        provider.ItemInfos.Add(Id,info);Info=info;
        Plugin.Logger.LogInfo($"Cannon Wheel registered as native Amulet {CannonRules.ItemId}; jackpot unlocked={CannonProgress.Unlocked}.");
    }
}
