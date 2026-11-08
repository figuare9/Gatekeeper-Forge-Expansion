using System.Text.Json;
using BepInEx;
using Gatekeeper.Char_Scripts;
using Gatekeeper.General.Predictor;
using Gatekeeper.General.SaveLoad;
using Gatekeeper.Mods;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class CannonProgress
{
    private static readonly Dictionary<int,bool> peers=new();
    private static readonly Dictionary<int,bool> enabledPeers=new();
    private static CannonProgressData data=new();
    private static bool loaded;
    private static string account="",pendingAward="";
    private static float toastUntil;
    private static GUIStyle? style;
    private static bool? marked;
    private static int markedCount=-1;
    private static string ProgressFile=>Path.Combine(Paths.ConfigPath,Plugin.Id+".cannon-progress.json");
    private static string RunFile=>Path.Combine(Paths.ConfigPath,Plugin.Id+".cannon-run.json");
    internal static bool Unlocked {get{Load();return account.Length>0 && data.Unlocked(account);}}
    private static void Load()
    {
        if(!loaded)
        {
            try {if(File.Exists(ProgressFile))data=JsonSerializer.Deserialize<CannonProgressData>(File.ReadAllText(ProgressFile)) ?? new();}
            catch(Exception ex)
            {
                try{File.Copy(ProgressFile,ProgressFile+".corrupt-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"),false);}
                catch(Exception copy){Plugin.Logger.LogWarning("Cannon progress backup: "+copy.Message);}
                Plugin.Logger.LogWarning("Cannon progress was preserved for recovery: "+ex.Message);
            }
            loaded=true;
            data.Winners ??= new();
        }
        try
        {
            var steam=Steamworks.SteamUser.GetSteamID().m_SteamID;
            if(steam!=0)account=steam.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch { /* Steam may not have initialized during the item-provider constructor. */ }
    }
    internal static bool ForOwner(int owner)=>owner==SlotNetwork.LocalId ? Unlocked : peers.TryGetValue(owner,out var yes)&&yes;
    internal static bool EnabledFor(int owner)=>owner==SlotNetwork.LocalId ? CannonRegistry.Enabled : enabledPeers.TryGetValue(owner,out var yes)&&yes;
    internal static void Peer(int owner,bool unlocked,bool enabled)
    {if(unlocked || !peers.ContainsKey(owner))peers[owner]=unlocked;enabledPeers[owner]=enabled;}
    internal static void UnlockWinner(FishNet.Connection.NetworkConnection owner,string round)
    {
        if(!SlotNetwork.IsHost || !SlotRules.IsRequestId(round))return;
        peers[owner.ClientId]=true;
        SlotNetwork.Send(owner,new() {Kind="cannon",Cannon=new(){Kind="unlock",Round=round,Owner=owner.ClientId}});
    }
    internal static void Award(string round)
    {
        if(!SlotRules.IsRequestId(round))return;
        Load();if(account.Length==0){pendingAward=round;return;}
        if(!data.Award(account,round))return;
        try
        {
            File.WriteAllText(ProgressFile+".tmp",JsonSerializer.Serialize(data));
            File.Move(ProgressFile+".tmp",ProgressFile,true);
        }
        catch(Exception ex)
        {
            data.Winners.Remove(account);pendingAward=round;
            Plugin.Logger.LogWarning("Cannon unlock will retry saving: "+ex.Message);
            return; // A progress-file error must not interrupt the jackpot payout.
        }
        pendingAward="";toastUntil=Time.unscaledTime+9;
        CannonRegistry.RefreshAvailability();CannonCatalog.Ensure();
        ExchangeClient.Invalidate();
        if(ShopUI.IsOpen)ShopUI.RefreshInventory();
        Plugin.Logger.LogInfo("Cannon Wheel permanently unlocked for Steam account "+account+" by confirmed jackpot "+round);
    }
    internal static void Tick()
    {
        Load();
        if(pendingAward.Length>0 && account.Length>0)Award(pendingAward);
        if(!SaveLoadManager.IsReady)return;
        var saved=SaveLoadManager.SessionSaveData;
        var count=0;
        if(saved!=null && saved.HasData && saved.Data!=null)
        {
            var items=saved.Data.Inventory.Items;
            if(items!=null && items.TryGetValue(CannonRegistry.Id,out var stored))count=Math.Max(0,stored);
        }
        var player=PlayableCharactersHolder.MyCharacter;
        if(GlobalPredictor.GameState==GameState.Gameplay && player!=null && player.IsActiveAndAlive)count=Math.Max(count,player.ItemManager.GetItemQuantity(CannonRegistry.Id));
        var requires=count>0;
        if(marked==requires && markedCount==count)return;
        File.WriteAllText(RunFile+".tmp",JsonSerializer.Serialize(new CannonRunMarker(requires,CannonRules.ItemId,count,account)));
        File.Move(RunFile+".tmp",RunFile,true);marked=requires;markedCount=count;
    }
    internal static void OnGui()
    {
        if(Time.unscaledTime>=toastUntil)return;
        style??=new GUIStyle(GUI.skin.label){fontSize=22,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter,wordWrap=true};
        var scale=Mathf.Min(Screen.width/1280f,Screen.height/800f);
        var old=GUI.matrix;var color=GUI.color;
        try
        {
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new(scale,scale,1));
            var width=Screen.width/scale;
            GUI.color=new(.04f,.07f,.10f,.95f);GUI.DrawTexture(new(width/2-285,54,570,84),Texture2D.whiteTexture);
            GUI.color=new(.91f,.72f,.39f);GUI.Label(new(width/2-275,58,550,76),"CANNON WHEEL UNLOCKED\nAmulet • Find it in drops or the Item Exchange",style);
        }
        finally{GUI.matrix=old;GUI.color=color;}
    }
    internal static void ResetPeers(){peers.Clear();enabledPeers.Clear();}
}
