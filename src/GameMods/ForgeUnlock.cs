using System.Text.Json;
using BepInEx;
using FishNet.Connection;
using Gatekeeper.Achievements.Data;
using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.General.Predictor;
using Gatekeeper.General.SaveLoad;
using Gatekeeper.General.SaveLoad.Achievements;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Mods;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// Account progress is deliberately independent of the prototype's old native
// always-unlocked flag, run resets, installer updates and other Steam users.
internal static class ForgeUnlock
{
    private static ForgeUnlockData data=new();
    private static bool loaded,storageBlocked,pulled,bannerPending;
    private static string account="",pending="",world="",token="";
    private static float nextLoad,nextCheck,nextSave,nextGrant,nextBanner;
    private static int sceneHandle,pendingCharacter;
    private static ForgeLeverArt? art;
    private static Vector3 point;
    private static readonly HashSet<(int Owner,int Character)> granted=new();
    private static readonly Dictionary<int,(string Proof,int Character)> awaiting=new();
    private static AchievementInfo? popupInfo;
    internal static string? Error{get;private set;}
    internal static bool Unlocked {get{Load();return data.Unlocked(account);}}
    internal static string State=>!InLevel?"absent":art==null?"waiting for terrain":pulled?"activated":"ready";
    private static string ProgressFile=>Path.Combine(Paths.ConfigPath,Plugin.Id+".forge-progress.json");
    private static bool InLevel=>ForgeRegistry.Enabled && ForgeUnlockRules.InLevel(
        GlobalPredictor.GameState==GameState.Gameplay,(int)GlobalPredictor.CurrentScene);
    // Fixed authored map socket. Only Y is resolved against native terrain.
    private static readonly Vector3 Socket=new(ForgeUnlockRules.RelayX,0,ForgeUnlockRules.RelayZ);
    private static readonly Vector3[] Footprint={Vector3.zero,
        new(-ForgeUnlockRules.FootHalfWidth,0,-ForgeUnlockRules.FootHalfDepth),new(ForgeUnlockRules.FootHalfWidth,0,-ForgeUnlockRules.FootHalfDepth),
        new(-ForgeUnlockRules.FootHalfWidth,0,ForgeUnlockRules.FootHalfDepth),new(ForgeUnlockRules.FootHalfWidth,0,ForgeUnlockRules.FootHalfDepth)};
    private static bool TryGroundSocket(Vector3 socket,float yaw,out Vector3 resolved)
    {
        resolved=default;
        if(!Physics.Raycast(socket+Vector3.up*70,Vector3.down,out var ground,140,ForgeUnlockRules.GroundMask,QueryTriggerInteraction.Ignore) ||
            !ForgeUnlockRules.Supported(ground.point.y,ground.point.y,ground.normal.y))return false;
        var rotation=Quaternion.Euler(0,yaw,0);
        foreach(var corner in Footprint)
        {
            var at=new Vector3(socket.x,ground.point.y,socket.z)+rotation*corner;
            if(!Physics.Raycast(at+Vector3.up*2,Vector3.down,out var support,4,ForgeUnlockRules.GroundMask,QueryTriggerInteraction.Ignore) ||
                !ForgeUnlockRules.Supported(ground.point.y,support.point.y,support.normal.y))return false;
        }
        resolved=new(socket.x,ground.point.y+.025f,socket.z);
        var center=resolved+rotation*new Vector3(0,ForgeUnlockRules.ClearanceCenterHeight,ForgeUnlockRules.ClearanceCenterDepth);
        return !Physics.CheckBox(center,new(ForgeUnlockRules.ClearanceHalfWidth,ForgeUnlockRules.ClearanceHalfHeight,ForgeUnlockRules.ClearanceHalfDepth),
            rotation,ForgeUnlockRules.StaticGeometryMask,QueryTriggerInteraction.Ignore);
    }
    internal static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(AchievementsInfoProvider),nameof(AchievementsInfoProvider.GetCharacterById)),prefix:new(typeof(ForgeUnlock),nameof(PopupCharacter)));
    }
    private static bool PopupCharacter(AchievementID __0,ref CharacterType __result)
    {if((int)__0!=ForgeUnlockRules.PopupAchievementId)return true;__result=ForgeRegistry.Type;return false;}
    private static void Load()
    {
        if(Time.unscaledTime<nextLoad)return;nextLoad=Time.unscaledTime+1;
        if(!loaded)
        {
            try
            {
                if(File.Exists(ProgressFile))
                {
                    if(new FileInfo(ProgressFile).Length>1_000_000)throw new InvalidDataException("Forge progress exceeds its size limit.");
                    data=JsonSerializer.Deserialize<ForgeUnlockData>(File.ReadAllText(ProgressFile))??throw new InvalidDataException("Empty Forge progress.");
                    if(data.Version!=1 || data.Accounts==null)throw new InvalidDataException("Unsupported Forge progress version.");
                }
            }
            catch(Exception e)
            {
                // Never overwrite a damaged original unless it was preserved.
                try{File.Copy(ProgressFile,ProgressFile+".corrupt-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"),false);data=new();}
                catch{storageBlocked=true;}
                Report("Forge progress was preserved for recovery: "+e.Message);
            }
            loaded=true;
        }
        try
        {
            var id=Steamworks.SteamUser.GetSteamID().m_SteamID;
            if(id!=0)account=id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch{ /* Steam is not ready yet; no anonymous unlock is granted. */ }
    }
    private static void Award(string proof,int character)
    {
        if(!SlotRules.IsRequestId(proof))return;
        pending=proof;pendingCharacter=character;Load();if(account.Length==0 || storageBlocked || Time.unscaledTime<nextSave)return;
        nextSave=Time.unscaledTime+2;
        if(data.Unlocked(account)){pending="";Ack(proof,character);return;}
        var updated=new ForgeUnlockData{Accounts=new(data.Accounts)};
        if(!updated.Award(account,proof))return;
        try
        {
            File.WriteAllText(ProgressFile+".tmp",JsonSerializer.Serialize(updated));File.Move(ProgressFile+".tmp",ProgressFile,true);
        }
        catch(Exception e){Report("Forge unlock will retry saving: "+e.Message);return;}
        data=updated;pending="";bannerPending=true;Error=null;
        ForgeRegistry.RefreshUnlock();Ack(proof,character);
        Plugin.Logger.LogInfo("Forge permanently unlocked for the current Steam account by the Ethereal Bridge engineering relay.");
    }
    private static void Ack(string proof,int character)=>SlotNetwork.Request(new(){Kind="forgeUnlock",ForgeUnlock=new(){Kind="ack",Token=proof,Scene=ForgeUnlockRules.Scene,Owner=SlotNetwork.LocalId,Character=character}});
    internal static void Receive(ForgeUnlockMessage message)
    {
        if(!ForgeRegistry.Enabled || message.Scene!=ForgeUnlockRules.Scene || !SlotRules.IsRequestId(message.Token))return;
        if(message.Kind=="grant" && message.Owner==SlotNetwork.LocalId && PlayableCharactersHolder.MyCharacter?.NetworkObject.ObjectId==message.Character){Award(message.Token,message.Character);return;}
        if(message.Kind=="pulled" && InLevel){pulled=true;token=message.Token;art?.Pull();}
    }
    internal static void ServerReceive(NetworkConnection sender,ForgeUnlockMessage message)
    {
        if(!SlotNetwork.IsHost || sender==null || !sender.IsActive || !sender.IsAuthenticated || message.Owner!=sender.ClientId)return;
        if(message.Kind=="ack" && message.Scene==ForgeUnlockRules.Scene && awaiting.TryGetValue(sender.ClientId,out var proof) && proof.Proof==message.Token && proof.Character==message.Character)
            awaiting.Remove(sender.ClientId);
    }
    internal static void SendState(NetworkConnection peer)
    {if(InLevel && pulled && token.Length>0)SlotNetwork.Send(peer,new(){Kind="forgeUnlock",ForgeUnlock=new(){Kind="pulled",Scene=ForgeUnlockRules.Scene,Token=token}});}
    internal static void Tick()
    {
        Load();if(pending.Length>0)Award(pending,pendingCharacter);
        if(bannerPending && Time.unscaledTime>=nextBanner){nextBanner=Time.unscaledTime+1;ShowBanner();}
        var current=SceneManager.GetActiveScene();
        if(!InLevel){if(art!=null || world.Length>0)ClearWorld();return;}
        if(!SlotNetwork.Ready)return;
        if(world!=SlotNetwork.Session || sceneHandle!=current.handle)
        {ClearWorld();world=SlotNetwork.Session;sceneHandle=current.handle;}
        if(art!=null && art.Root==null)art=null;
        if(art!=null)art.Tick();
        if(Time.unscaledTime<nextCheck)return;nextCheck=Time.unscaledTime+.20f;
        try
        {
            if(art==null)
            {
                if(!TryGroundSocket(Socket,ForgeUnlockRules.RelayYaw,out point))return;
                art=new ForgeLeverArt(point,ForgeUnlockRules.RelayYaw);if(pulled)art.Pull(true);
                Plugin.Logger.LogInfo("Forge discovery relay placed at its fixed Ethereal Bridge terrain socket "+point);
            }
            if(!SlotNetwork.IsHost || !SlotNetwork.Ready || Time.timeScale<=0)return;
            foreach(var player in GameWorldIndex.Players())
            {
                if(!player.IsSpawned || !SlotNetwork.ForgePeer(player.OwnerId) || granted.Contains((player.OwnerId,player.NetworkObject.ObjectId)) || !player.IsActiveAndAlive)continue;
                var delta=player.CachedTransform.position-point;
                if(!ForgeUnlockRules.Eligible(true,(int)GlobalPredictor.CurrentScene,true,new Vector2(delta.x,delta.z).sqrMagnitude,delta.y) ||
                    Physics.Linecast(player.CachedTransform.position+Vector3.up,point+Vector3.up,ForgeUnlockRules.StaticGeometryMask,QueryTriggerInteraction.Ignore))continue;
                if(!pulled){pulled=true;token=Guid.NewGuid().ToString("N");art.Pull();SlotNetwork.Publish(new(){Kind="forgeUnlock",ForgeUnlock=new(){Kind="pulled",Scene=ForgeUnlockRules.Scene,Token=token}});}
                // Every current participant earns the discovery together. A
                // later joiner must approach; a status snapshot grants nothing.
                foreach(var ally in GameWorldIndex.Players())if(ally.IsSpawned && SlotNetwork.ForgePeer(ally.OwnerId) && granted.Add((ally.OwnerId,ally.NetworkObject.ObjectId)))awaiting[ally.OwnerId]=(token,ally.NetworkObject.ObjectId);
                nextGrant=0;break;
            }
            if(Time.unscaledTime<nextGrant)return;nextGrant=Time.unscaledTime+2;
            foreach(var pair in awaiting.ToArray())SlotNetwork.SendOwner(pair.Key,new(){Kind="forgeUnlock",ForgeUnlock=new(){Kind="grant",Scene=ForgeUnlockRules.Scene,Owner=pair.Key,Token=pair.Value.Proof,Character=pair.Value.Character}});
        }
        catch(Exception e){Report("Forge discovery relay: "+e.Message);}
    }
    private static void ShowBanner()
    {
        if(GlobalPredictor.GameState!=GameState.Gameplay)return;
        try
        {
            // Native unlock popups are lazily created and remain inactive
            // until queued. Active-object searches cannot discover them.
            var controller=Gatekeeper.Utility.Popups.PopupsController._instance;
            if(controller==null)return;
            var panel=controller.Get(Gatekeeper.Utility.Popups.PopupDesignation.UnlockCharacter);
            if(panel==null)return;
            var popup=panel.GetComponent<Gatekeeper.Achievements.AchievementPopup>()??panel.GetComponentInChildren<Gatekeeper.Achievements.AchievementPopup>(true);
            if(popup==null || popup.uiPopup==null)return;
            if(popupInfo==null)
            {
                var achievements=DatabaseInfoProvider.Achievements;
                var id=(AchievementID)ForgeUnlockRules.PopupAchievementId;
                if(achievements.AchievementsInfos.ContainsKey(id))throw new InvalidOperationException("Forge discovery popup ID is occupied.");
                popupInfo=Object.Instantiate(achievements.GetInfo(AchievementID.CharacterBastion));Object.DontDestroyOnLoad(popupInfo);
                popupInfo.LocalID=id;popupInfo.IsSteamAchievement=false;popupInfo.SteamAchievementObject=null;popupInfo.SteamIntStatObject=null;
            }
            popup.Show(Gatekeeper.Utility.Popups.PopupDesignation.UnlockCharacter,ref popupInfo);
            foreach(var label in panel.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                if(label.text=="FORGE")label.text="FORGE UNLOCKED!";
            bannerPending=false;
        }
        catch(Exception e){bannerPending=false;Report("Forge unlock banner: "+e.Message);}
    }
    private static void Report(string error){if(Error==error)return;Error=error;Plugin.Logger.LogWarning(error);}
    private static void ClearWorld()
    {
        art?.Destroy();art=null;world="";sceneHandle=0;pulled=false;token="";granted.Clear();awaiting.Clear();nextCheck=nextGrant=0;
    }
    internal static void Clear()=>ClearWorld();
}
