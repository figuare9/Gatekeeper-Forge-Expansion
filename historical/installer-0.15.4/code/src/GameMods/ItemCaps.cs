using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Char_Scripts.General.Network;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Items;
using Gatekeeper.Mods;
using HarmonyLib;

namespace Gatekeeper.CustomMods;

internal static class ItemCaps
{
    private static readonly ItemID[] limited={ItemID.Indicator,ItemID.Adjuster,ItemID.YellowPill};
    private static ItemsInfoProvider? provider;
    private static readonly Dictionary<ItemID,int> originals=new();
    internal static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Constructor(typeof(ItemsInfoProvider)),postfix:new(typeof(ItemCaps),nameof(Ready)));
        // Native batch restoration and single acquisition have separate bodies.
        h.Patch(AccessTools.Method(typeof(CharItemManager),"AddItemInternal"),prefix:new(typeof(ItemCaps),nameof(Single)));
        h.Patch(AccessTools.Method(typeof(CharItemManager),"AddItemsInternal"),prefix:new(typeof(ItemCaps),nameof(Batch)));
        // Trim before native entry-point validation as well: an oversized award
        // should fill the remaining capacity, not be rejected at 24 stacks.
        h.Patch(AccessTools.Method(typeof(CharItemManager),nameof(CharItemManager.OwnerAddItem)),prefix:new(typeof(ItemCaps),nameof(Single)));
        foreach(var name in new[]{nameof(CharItemManager.OwnerAddItems),nameof(CharItemManager.ServerAndOwnerAddItems)})
            h.Patch(AccessTools.Method(typeof(CharItemManager),name),prefix:new(typeof(ItemCaps),nameof(Batch)));
        h.Patch(AccessTools.Method(typeof(CharItemManager),nameof(CharItemManager.ClientInit)),postfix:new(typeof(ItemCaps),nameof(Loaded)));
    }
    private static void Ready(ItemsInfoProvider __instance)=>Ensure(__instance);
    internal static void Ensure(ItemsInfoProvider? current=null)
    {
        current??=DatabaseInfoProvider.Items;if(current==null)return;
        if(provider?.Pointer!=current.Pointer){provider=current;originals.Clear();}
        foreach(var id in limited)
        {
            var info=current.GetInfo(id);if(info==null)continue;
            if(!originals.ContainsKey(id))originals[id]=info.itemMaxCount;
            info.itemMaxCount=ItemCapRules.Maximum;
        }
    }
    private static bool Single(CharItemManager __instance,ItemID __0,ref int __2)
    {
        if(!ItemCapRules.Limited((int)__0))return true;
        __2=ItemCapRules.Allow((int)__0,__instance.GetItemQuantity(__0),__2);return __2>0;
    }
    private static bool Batch(CharItemManager __instance,ref Il2CppSystem.Collections.Generic.List<NetworkItemData> __0)
    {
        if(__0==null)return true;
        var used=new Dictionary<ItemID,int>();
        var result=new Il2CppSystem.Collections.Generic.List<NetworkItemData>();
        foreach(var entry in __0)
        {
            var count=entry.Count;
            if(ItemCapRules.Limited((int)entry.Id))
            {
                var owned=used.TryGetValue(entry.Id,out var value)?value:__instance.GetItemQuantity(entry.Id);
                count=ItemCapRules.Allow((int)entry.Id,owned,count);used[entry.Id]=owned+count;
            }
            if(count>0)result.Add(new NetworkItemData(entry.Id,count));
        }
        __0=result;return result.Count>0;
    }
    private static void Loaded(CharItemManager __instance)
    {
        // Native save hydration can copy the item dictionary directly. The owner
        // uses the ordinary removal RPC so host/owner/observers update stats and
        // inventory consistently; never edit the user's save files ourselves.
        if(!__instance.IsOwner)return;
        foreach(var id in limited)
        {
            var excess=__instance.GetItemQuantity(id)-ItemCapRules.Maximum;
            if(excess<=0)continue;
            __instance.OwnerRemoveItem(id,excess);
            Plugin.Logger.LogInfo($"Applied release item cap to restored {id}: removed {excess} above 25.");
        }
    }
    internal static void Restore()
    {
        if(provider!=null)foreach(var pair in originals){var info=provider.GetInfo(pair.Key);if(info!=null)info.itemMaxCount=pair.Value;}
        originals.Clear();provider=null;
    }
}
