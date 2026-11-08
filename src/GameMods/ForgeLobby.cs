using FishNet.Object;
using System.Text.Json;
using FishNet.Connection;
using FishNet.Object.Delegating;
using FishNet.Serializing;
using FishNet.Transporting;
using Gatekeeper.Mods;
using Gatekeeper.Network.GameLobby;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class ForgeLobby
{
    private sealed class Hello
    {
        public string Revision {get;set;}="";
        public string Roster {get;set;}="";
        public bool Enabled {get;set;}
        public bool Ready {get;set;}
        public bool Unlocked {get;set;}
        public string AspectsRevision {get;set;}="";
        public bool AspectsRegistered {get;set;}
        public bool AspectsReady {get;set;}
    }
    private const uint Request=242,Response=243;
    private static LobbyManager? endpoint;
    private static ServerRpcDelegate? server;
    private static ClientRpcDelegate? client;
    private static readonly Dictionary<int,(Hello Hello,float At,NetworkConnection Connection)> peers=new();
    private static float next,seen;
    private static bool ready;
    private static bool aspectsReady;
    private static string? error;
    internal static string Note {get;private set;}="Forge: waiting for matching co-op class registrations.";
    internal static bool CanChoose
    {
        get
        {
            if(!ForgeRegistry.Enabled || !ForgeRegistry.PrefabReady)return false;
            var lobby=LobbyManager._instance;
            if(lobby!=null && lobby.IsSpawned)
                return endpoint!=null && endpoint.Pointer==lobby.Pointer && (endpoint.IsServerStarted ? AllReady():ready && Time.unscaledTime-seen<7);
            return Plugin.Solo();
        }
    }
    internal static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(LobbyManager),nameof(LobbyManager.ServerStartLanding)),prefix:new(typeof(ForgeLobby),nameof(Landing)));
    }
    private static bool Landing(LobbyManager __instance)
    {
        if(AspectRegistry.CustomSelected() && !CanUseAspects)
        {Note="Every player needs the same Forge Expansion for Ascendant or expansion Aspects.";Plugin.Logger.LogWarning(Note);return false;}
        // Existing stock card indices are unchanged: Forge is appended last.
        var index=ForgeRegistry.CardIndex;
        if(index<0)return true;
        var custom=false;
        foreach(var p in __instance.PlayersData)
        {
            if(!p.Value.IsValid || p.Value.CharacterIndex!=index && p.Value.RandomCharacterIndex!=index)continue;
            custom=true;
            var unlocked=p.Key==__instance.LocalConnection.ClientId?ForgeUnlock.Unlocked:
                peers.TryGetValue(p.Key,out var peer) && peer.Hello.Unlocked && Time.unscaledTime-peer.At<7;
            if(!unlocked){Note="Discover Forge's engineering relay before selecting him.";Plugin.Logger.LogWarning(Note);return false;}
        }
        if(!custom || CanChoose)return true;
        Note="Forge needs the same enabled prototype on every player before landing.";Plugin.Logger.LogWarning(Note);return false;
    }
    private static Hello Local()=>new(){Revision=ForgeRules.Revision,Roster=ForgeRegistry.Roster(),Enabled=ForgeRegistry.Enabled && ForgeRegistry.PrefabReady,Unlocked=ForgeUnlock.Unlocked,
        AspectsRevision=AspectRules.Revision,AspectsRegistered=AspectRegistry.Registered};
    internal static bool CanUseAspects
    {
        get
        {
            if(!AspectRegistry.Registered)return false;
            var lobby=LobbyManager._instance;
            if(lobby==null || !lobby.IsSpawned)return Plugin.Solo();
            return endpoint!=null && endpoint.Pointer==lobby.Pointer && (endpoint.IsServerStarted?AllAspectPeersReady():aspectsReady && Time.unscaledTime-seen<7);
        }
    }
    private static bool AllAspectPeersReady()
    {
        if(endpoint==null || !AspectRegistry.Registered)return false;
        foreach(var pair in endpoint.PlayersData)
        {
            if(!pair.Value.IsValid || pair.Key==endpoint.LocalConnection.ClientId)continue;
            if(!peers.TryGetValue(pair.Key,out var peer) || Time.unscaledTime-peer.At>7 ||
                !peer.Hello.AspectsRegistered || peer.Hello.AspectsRevision!=AspectRules.Revision)return false;
        }
        return true;
    }
    private static bool AllReady()
    {
        if(endpoint==null)return false;
        var local=Local();if(!local.Enabled || local.Roster.Length!=64)return false;
        foreach(var pair in endpoint.PlayersData)
        {
            if(!pair.Value.IsValid)continue;
            if(pair.Key==endpoint.LocalConnection.ClientId)continue;
            if(!peers.TryGetValue(pair.Key,out var peer) || Time.unscaledTime-peer.At>7 ||
                !ForgeRules.Compatible(peer.Hello.Revision,peer.Hello.Roster,peer.Hello.Enabled,local.Roster))return false;
        }
        return true;
    }
    internal static void Tick()
    {
        try
        {
            var manager=LobbyManager._instance;
            if(manager==null || !manager.IsSpawned){Detach();return;}
            if(endpoint==null || endpoint.Pointer!=manager.Pointer)
            {
                Detach();
                if(manager._serverRpcDelegates.ContainsKey(Request) || manager._targetRpcDelegates.ContainsKey(Response))throw new InvalidOperationException("Forge lobby RPC IDs are occupied.");
                server=DelegateSupport.ConvertDelegate<ServerRpcDelegate>((Action<PooledReader,Channel,NetworkConnection>)ReadServer);
                client=DelegateSupport.ConvertDelegate<ClientRpcDelegate>((Action<PooledReader,Channel>)ReadClient);
                manager.RegisterServerRpc(Request,server);manager.RegisterTargetRpc(Response,client);endpoint=manager;
            }
            if(Time.unscaledTime<next)return;next=Time.unscaledTime+2;
            if(manager.IsClientStarted && !manager.IsServerStarted)
            {
                var writer=WriterPool.Retrieve();try{writer.WriteString(JsonSerializer.Serialize(Local()));manager.SendServerRpc(Request,writer,Channel.Reliable,DataOrderType.Last);}finally{writer.Store();}
            }
            if(manager.IsServerStarted)
            {
                var response=Local();response.Ready=AllReady();response.AspectsReady=AllAspectPeersReady();
                foreach(var peer in peers.Values.ToArray())if(peer.Connection.IsActive)
                {
                    var writer=WriterPool.Retrieve();try{writer.WriteString(JsonSerializer.Serialize(response));manager.SendTargetRpc(Response,writer,Channel.Reliable,DataOrderType.Last,peer.Connection,false,true);}finally{writer.Store();}
                }
            }
            Note=CanChoose ? "Forge prototype ready." : "Forge: every player needs the same enabled class prototype.";
        }
        catch(Exception e){ready=false;if(error!=e.Message){error=e.Message;Plugin.Logger.LogWarning("Forge lobby: "+e);}}
    }
    private static Hello? Read(PooledReader r)
    {var s=r.ReadString();return s!=null && s.Length<512 ? JsonSerializer.Deserialize<Hello>(s):null;}
    private static void ReadServer(PooledReader r,Channel _,NetworkConnection sender)
    {
        try
        {
            if(endpoint==null || !endpoint.IsServerStarted || !sender.IsActive || !sender.IsAuthenticated)return;
            var hello=Read(r);if(hello!=null)peers[sender.ClientId]=(hello,Time.unscaledTime,sender);
        }
        catch(Exception e){Plugin.Logger.LogWarning("Forge lobby hello rejected: "+e.Message);}
    }
    private static void ReadClient(PooledReader r,Channel _)
    {
        try{var m=Read(r);if(m==null)return;ready=m.Ready && ForgeRules.Compatible(m.Revision,m.Roster,m.Enabled,ForgeRegistry.Roster());aspectsReady=m.AspectsReady && m.AspectsRegistered && m.AspectsRevision==AspectRules.Revision && AspectRegistry.Registered;seen=Time.unscaledTime;}
        catch{ready=aspectsReady=false;}
    }
    internal static void Detach()
    {
        if(endpoint!=null){try{endpoint._serverRpcDelegates.Remove(Request);endpoint._targetRpcDelegates.Remove(Response);}catch{}}
        endpoint=null;server=null;client=null;peers.Clear();ready=aspectsReady=false;seen=next=0;
    }
}
