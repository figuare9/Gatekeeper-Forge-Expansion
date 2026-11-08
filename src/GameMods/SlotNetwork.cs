using System.Text.Json;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Delegating;
using FishNet.Serializing;
using FishNet.Transporting;
using Gatekeeper.Currency.Prism;
using Gatekeeper.Mods;
using Gatekeeper.General.Predictor;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Gatekeeper.CustomMods;

internal static class SlotNetwork
{
    // Native RPC hashes in this pinned PrismsManager build are 0 and 1. Keep
    // custom IDs inside the one-byte hash format and refuse occupied entries.
    private const uint RequestRpc = 240, ResponseRpc = 241;
    private static PrismsManager? endpoint;
    private static ServerRpcDelegate? serverHandler;
    private static ClientRpcDelegate? clientHandler;
    private static readonly Dictionary<int, NetworkConnection> peers = new();
    private static readonly HashSet<int> forgePeers=new();
    internal static bool ForgePeer(int id)=>forgePeers.Contains(id);
    private static float nextHello;
    private static int helloAttempts;
    private static string? lastError;
    internal static string Session { get; private set; } = "";
    internal static bool Ready { get; private set; }
    internal static bool IsHost => endpoint != null && endpoint.IsServerStarted;
    internal static int LocalId => endpoint?.LocalConnection?.ClientId ?? -1;
    internal static NetworkConnection[] Participants => peers.Values.Where(p => p.IsActive && p.IsAuthenticated).ToArray();

    internal static void Tick()
    {
        if (Plugin.Current?.Slots.Value != true && !Plugin.ShopActive && !CannonRegistry.Enabled && !ForgeRegistry.Enabled) { if (endpoint != null) Detach(); return; }
        // Instance searches the entire loaded object set when absent. The
        // native lifecycle already maintains this registered singleton field.
        var manager = PrismsManager._instance;
        if (manager == null || !manager.IsSpawned) { if (endpoint != null) Detach(); return; }
        try
        {
            if (endpoint == null || endpoint.Pointer != manager.Pointer)
            {
                Detach();
                if (manager._serverRpcDelegates.ContainsKey(RequestRpc) || manager._targetRpcDelegates.ContainsKey(ResponseRpc))
                    throw new InvalidOperationException("Slot RPC IDs are already in use; refusing to replace another handler.");
                serverHandler = DelegateSupport.ConvertDelegate<ServerRpcDelegate>((Action<PooledReader, Channel, NetworkConnection>)ReadServer);
                clientHandler = DelegateSupport.ConvertDelegate<ClientRpcDelegate>((Action<PooledReader, Channel>)ReadClient);
                manager.RegisterServerRpc(RequestRpc, serverHandler);
                manager.RegisterTargetRpc(ResponseRpc, clientHandler);
                endpoint = manager;
                Session = IsHost ? Guid.NewGuid().ToString("N") : "";
                Plugin.Logger.LogInfo("Slot network attached; host authority=" + IsHost);
            }
            if (endpoint.IsClientStarted && Time.unscaledTime >= nextHello)
            {
                nextHello = Time.unscaledTime + (Ready ? 3 : Math.Min(30,3 * ++helloAttempts));
                Request(new() { Kind = "hello", CannonUnlocked=CannonProgress.Unlocked,CannonEnabled=CannonRegistry.Enabled,ForgeEnabled=ForgeRegistry.Enabled,ForgeRevision=ForgeRules.Revision });
            }
        }
        catch (Exception ex)
        {
            if (lastError != ex.Message) { lastError = ex.Message; Plugin.Logger.LogError("Slot network: " + ex); }
        }
    }

    private static SlotMessage? Read(PooledReader reader)
    {
        var json = reader.ReadString();
        if (json == null || json.Length > ModProtocol.MaxMessageChars) return null;
        var message = JsonSerializer.Deserialize<SlotMessage>(json);
        return message?.Version == SlotRules.Protocol ? message : null;
    }
    private static void ReadServer(PooledReader reader, Channel channel, NetworkConnection sender)
    {
        try { var message = Read(reader); if (message != null) HandleServer(sender, message); }
        catch (Exception ex) { Plugin.Logger.LogWarning("Slot request rejected: " + ex.Message); }
    }
    private static void ReadClient(PooledReader reader, Channel channel)
    {
        try { var message = Read(reader); if (message != null) HandleClient(message); }
        catch (Exception ex) { Plugin.Logger.LogWarning("Slot response rejected: " + ex.Message); }
    }

    private static void HandleServer(NetworkConnection sender, SlotMessage message)
    {
        if (!IsHost || sender == null || !sender.IsActive || !sender.IsAuthenticated) return;
        if (message.Kind == "hello")
        {
            peers[sender.ClientId] = sender;
            if(message.ForgeEnabled && message.ForgeRevision==ForgeRules.Revision && ForgeRegistry.Enabled)forgePeers.Add(sender.ClientId);else forgePeers.Remove(sender.ClientId);
            CannonProgress.Peer(sender.ClientId,message.CannonUnlocked,message.CannonEnabled);
            Send(sender, ReadyMessage());
            SlotAuthority.SendQuote(sender);
            SlotAuthority.SendCurrent(sender);
            ExchangeAuthority.SendCurrent(sender);
            ForgeUnlock.SendState(sender);
            return;
        }
        if (message.Session != Session || !peers.ContainsKey(sender.ClientId)) return;
        if(message.Kind=="forgeUnlock" && message.ForgeUnlock!=null){ForgeUnlock.ServerReceive(sender,message.ForgeUnlock);return;}
        // New transactions must originate in the host's current room. Wallet
        // acknowledgements may still finish settling during a transition.
        if ((message.Kind is "bet" or "quote" || message.Kind=="exchange" && message.Shop?.Kind is "buy" or "sell" or "quotes") &&
            message.Scene!=(int)GlobalPredictor.CurrentScene)return;
        if(message.Kind=="forge" && message.Forge!=null){ForgeWorld.ServerReceive(sender,message.Forge);return;}
        if (message.Kind == "exchange" && message.Shop != null) ExchangeAuthority.Receive(sender,message.Shop);
        else SlotAuthority.Receive(sender, message);
    }
    private static void HandleClient(SlotMessage message)
    {
        if (message.Kind == "ready")
        {
            if (Session.Length > 0 && Session != message.Session) { SlotPlayer.Reset(); ExchangeClient.Reset(); CannonWorld.Clear(true); ForgeWorld.Clear(); }
            Session = message.Session; Ready = true;
            if(message.Scene==(int)GlobalPredictor.CurrentScene)
            {
                SlotPlayer.SetVisit(message.Visit);
                ExchangeClient.SetVisit(message.ShopVisit);
                if (!IsHost && message.Pose != null) WorldSlots.SetRemotePose(message.Pose,message.Scene);
                if (!IsHost && message.ShopPose != null) WorldShop.SetRemotePose(message.ShopPose,message.Scene);
            }
            return;
        }
        if (message.Session != Session) return;
        if(message.Kind=="forgeUnlock" && message.ForgeUnlock!=null){ForgeUnlock.Receive(message.ForgeUnlock);return;}
        if(message.Kind=="forge" && message.Forge!=null){ForgeWorld.Receive(message.Forge);return;}
        if(message.Kind=="cannon" && message.Cannon!=null) {CannonWorld.Receive(message.Cannon);return;}
        if (message.Kind == "exchange" && message.Shop != null) ExchangeClient.Receive(message.Shop);
        else SlotPlayer.Receive(message);
    }

    internal static void Request(SlotMessage message)
    {
        if (endpoint == null || !endpoint.IsClientStarted) return;
        message.Session = Session;
        message.Scene=(int)GlobalPredictor.CurrentScene;
        message.Visit = SlotPlayer.Visit;
        if (IsHost) { HandleServer(endpoint.LocalConnection, message); return; }
        var writer = WriterPool.Retrieve();
        try { writer.WriteString(Encode(message)); endpoint.SendServerRpc(RequestRpc, writer, Channel.Reliable, DataOrderType.Last); }
        finally { writer.Store(); }
    }
    internal static void Send(NetworkConnection target, SlotMessage message)
    {
        if (!IsHost || target == null || !target.IsActive) return;
        message.Session = Session;
        message.Visit = SlotAuthority.Visit;
        if (target.ClientId == LocalId) { HandleClient(message); return; }
        var writer = WriterPool.Retrieve();
        try { writer.WriteString(Encode(message)); endpoint!.SendTargetRpc(ResponseRpc, writer, Channel.Reliable, DataOrderType.Last, target, false, true); }
        finally { writer.Store(); }
    }
    internal static void Publish(SlotMessage message)
    {
        foreach (var peer in peers.Values.ToArray()) if (peer.IsActive) Send(peer, message);
    }
    internal static void SendOwner(int owner,SlotMessage message)
    {if(peers.TryGetValue(owner,out var peer) && peer.IsActive && peer.IsAuthenticated)Send(peer,message);}
    private static SlotMessage ReadyMessage() => new() { Kind = "ready", Scene=(int)GlobalPredictor.CurrentScene, Pose = WorldSlots.Pose, ShopPose = WorldShop.Pose, ShopVisit = ExchangeAuthority.Visit };
    private static string Encode(SlotMessage message)
    {
        var json = JsonSerializer.Serialize(message);
        return json.Length <= ModProtocol.MaxMessageChars ? json : throw new InvalidOperationException("Mod message exceeds the transport limit.");
    }
    internal static void PublishReady() => Publish(ReadyMessage());
    internal static void Detach()
    {
        SlotPlayer.Reset(); SlotAuthority.Reset(); ExchangeClient.Reset(); ExchangeAuthority.Reset();
        CannonWorld.Clear(true); CannonProgress.ResetPeers(); ForgeWorld.Clear();
        try
        {
            endpoint?._serverRpcDelegates.Remove(RequestRpc);
            endpoint?._targetRpcDelegates.Remove(ResponseRpc);
        }
        catch { /* The native network scene may already have been destroyed. */ }
        endpoint = null; serverHandler = null; clientHandler = null;
        peers.Clear();forgePeers.Clear(); Ready = false; Session = ""; nextHello = 0; helloAttempts = 0;
    }
}
