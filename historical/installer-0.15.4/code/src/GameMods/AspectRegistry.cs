using System.Reflection;
using System.Text.Json;
using BepInEx;
using Gatekeeper.General.Aspects;
using Gatekeeper.General.Aspects.Negatives.Multipliers.Swarm;
using Gatekeeper.General.SaveLoad;
using Gatekeeper.General.SaveLoad.Session;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.MainMenuScripts.MainMenu.AspectsPanel;
using Gatekeeper.Mods;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static class AspectRegistry
{
    internal static readonly byte[] Ids={AspectRules.Chaos,AspectRules.Scaling,AspectRules.Elite};
    private static readonly byte[] AllIds=Ids.Append(AscendantRules.SaveId).ToArray();
    internal static bool Registered=>infos.Count==4 && Error==null;
    internal static string? Error {get;private set;}
    private static readonly Dictionary<byte,BaseAspectInfo> infos=new();
    private static readonly Dictionary<string,string> terms=new()
    {
        ["gkmm.aspect.240.name"]="Aspect of Chaos",
        ["gkmm.aspect.240.description"]="75% more enemies. Stacks with Swarm for 125% more enemies.",
        ["gkmm.aspect.241.name"]="Aspect of Scaling",
        ["gkmm.aspect.241.description"]="Enemies gain 7% health and drop 5% more Prisms per team level after level 1. Uses the team's average level when each enemy spawns.",
        ["gkmm.aspect.242.name"]="Aspect of Elite",
        ["gkmm.aspect.242.description"]="Every enemy is elite, including bosses.",
        ["gkmm.aspect.243.name"]="Ascendant Difficulty",
        ["gkmm.aspect.243.description"]="Rise to a greater challenge. Enemies grow tougher as your team's build strengthens, with more elite encounters to test your power."
    };
    private static AspectsInfoProvider? provider;
    private static AspectsPanelController? panel;
    private static float next;
    private static string? marked;

    internal static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Constructor(typeof(AspectsInfoProvider)),postfix:new(typeof(AspectRegistry),nameof(Provider)));
        h.Patch(AccessTools.Method(typeof(AspectsInfoProvider),nameof(AspectsInfoProvider.IsUnlocked)),prefix:new(typeof(AspectRegistry),nameof(Unlocked)));
        h.Patch(AccessTools.Method(typeof(AspectsInfoProvider),nameof(AspectsInfoProvider.GetTranslatedDescription)),prefix:new(typeof(AspectRegistry),nameof(Description)));
        foreach(var name in new[]{nameof(LocalizationManager.GetTranslation),nameof(LocalizationManager.GetTranslationByManager)})
            h.Patch(AccessTools.Method(typeof(LocalizationManager),name),prefix:new(typeof(AspectRegistry),nameof(Translation)));
        h.Patch(AccessTools.Method(typeof(LocalizationManager),nameof(LocalizationManager.TryGetTranslation)),prefix:new(typeof(AspectRegistry),nameof(TryTranslation)));
        h.Patch(AccessTools.Method(typeof(AspectsPanelController),"Awake"),postfix:new(typeof(AspectRegistry),nameof(Panel)));
        h.Patch(AccessTools.Method(typeof(AspectsPanelController),"SpawnAspects"),postfix:new(typeof(AspectRegistry),nameof(Layout)));
        h.Patch(AccessTools.Method(typeof(AspectsPanelController),nameof(AspectsPanelController.Setup)),prefix:new(typeof(AspectRegistry),nameof(BeforeSetup)),postfix:new(typeof(AspectRegistry),nameof(Layout)));
        foreach(var name in new[]{"Awake","OnStartClient","ServerInit"})
            h.Patch(AccessTools.Method(typeof(AspectsManager),name),name=="Awake"?null:new HarmonyMethod(typeof(AspectRegistry),nameof(ManagerReady)),name=="Awake"?new HarmonyMethod(typeof(AspectRegistry),nameof(ManagerReady)):null);
        h.Patch(AccessTools.Method(typeof(SessionSaveData),nameof(SessionSaveData.Save)),postfix:new(typeof(AspectRegistry),nameof(Saved)));
        AscendantDifficulty.Patch(h);
        AspectGameplay.Patch(h);
    }
    private static void Report(Exception e)
    {if(Error==e.Message)return;Error=e.Message;Plugin.Logger.LogError("Expansion Aspects: "+e);}
    private static void Provider(AspectsInfoProvider __instance)
    {try{Ensure(__instance);}catch(Exception e){Report(e);}}
    internal static void Ensure(AspectsInfoProvider? p=null)
    {
        p??=DatabaseInfoProvider.Aspects;if(p==null || provider?.Pointer==p.Pointer)return;
        if(!p._aspectInfos.TryGetValue(EAspect.Swarm,out var template))return;
        foreach(var id in AllIds)
        {
            var type=(EAspect)id;
            if(p._aspectInfos.TryGetValue(type,out var occupied) && (!infos.TryGetValue(id,out var own) || occupied.Pointer!=own.Pointer))
                throw new InvalidOperationException("Expansion Aspect ID is already occupied: "+id);
            if(!infos.TryGetValue(id,out var info))
            {
                info=Object.Instantiate(template);Object.DontDestroyOnLoad(info);
                info.name="GKMM "+terms[$"gkmm.aspect.{id}.name"];info.Type=type;
                info.IsPositive=false;info.IsCoop=false;
                info.NameKey=(LocalizedString)$"gkmm.aspect.{id}.name";
                info.DescriptionKey=(LocalizedString)$"gkmm.aspect.{id}.description";
                info.Icon=Icon(id==AscendantRules.SaveId?AspectRules.Scaling:id);info.TryCast<AspectOfSwarmInfo>()!.SpawnMultiplier=0;
                infos.Add(id,info);
            }
            p._aspectInfos[type]=info;
        }
        p._allTypes=p._allTypes.Concat(Ids.Select(i=>(EAspect)i)).Distinct().ToArray();
        provider=p;Error=null;
        Plugin.Logger.LogInfo("Three expansion Aspects and optional Ascendant difficulty registered in the native catalog, unlocked for solo/co-op: Chaos, Scaling and Elite.");
    }
    private static Sprite Icon(byte id)
    {
        var name=id==AspectRules.Chaos?"chaos":id==AspectRules.Scaling?"scaling":"elite";
        using var stream=typeof(Plugin).Assembly.GetManifestResourceStream($"Gatekeeper.CustomMods.Assets.Aspects.aspect-{name}.png")??throw new FileNotFoundException("Aspect sprite is missing: "+name);
        using var buffer=new MemoryStream();stream.CopyTo(buffer);
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);texture.name="GKMM Aspect "+name;
        if(!ImageConversion.LoadImage(texture,buffer.ToArray(),false))throw new InvalidDataException("Could not decode Aspect sprite: "+name);
        texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
        texture.hideFlags|=HideFlags.DontUnloadUnusedAsset;Object.DontDestroyOnLoad(texture);
        var sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
        sprite.name=texture.name;sprite.hideFlags|=HideFlags.DontUnloadUnusedAsset;Object.DontDestroyOnLoad(sprite);return sprite;
    }
    private static bool Unlocked(EAspect __0,ref bool __result)
    {if(!AspectRules.IsCustom((int)__0))return true;__result=Registered;return false;}
    private static bool Description(EAspect __0,ref string __result)
    {
        if(!AspectRules.IsCustom((int)__0))return true;
        __result=terms[$"gkmm.aspect.{(int)__0}.description"];
        if(!ForgeLobby.CanUseAspects)__result+="\nEvery player needs the same Forge Expansion.";
        return false;
    }
    private static bool Translation(string __0,ref string __result)
    {if(!terms.TryGetValue(__0??"",out var value))return true;__result=value;return false;}
    private static bool TryTranslation(string __0,ref string __1,ref bool __result)
    {if(!terms.TryGetValue(__0??"",out var value))return true;__1=value;__result=true;return false;}
    private static void Panel(AspectsPanelController __instance)=>panel=__instance;
    private static void UnlockProfile()
    {
        if(!Registered || !SaveLoadManager.IsReady || SaveLoadManager.DataSave?.Aspects==null)return;
        // Native panel Setup reads the unlocked list directly, bypassing the
        // provider's IsUnlocked method. Register real profile unlocks as well.
        foreach(var id in Ids)if(!SaveLoadManager.DataSave.Aspects.Contains((EAspect)id))SaveLoadManager.DataSave.Aspects.Add((EAspect)id);
    }
    private static void BeforeSetup(){Ensure();UnlockProfile();}
    internal static bool CustomSelected()
    {
        if(panel!=null)foreach(var i in panel._selectedAspects)if(AspectRules.IsCustom((int)i.Type))return true;
        return false;
    }
    private static void Layout(AspectsPanelController __instance)
    {
        try
        {
            panel=__instance;
            // Both stock panels already have a six-column GridLayoutGroup and
            // ContentSizeFitter. Rebuild after the added cells, letting native
            // layout expand to three rows without accumulating frame offsets.
            foreach(var container in new[]{panel.negativeContainer,panel.positiveContainer})
                LayoutRebuilder.ForceRebuildLayoutImmediate(container.Cast<RectTransform>());
        }
        catch(Exception e){Report(e);}
    }
    private static void ManagerReady(AspectsManager __instance)
    {
        try
        {
            Ensure();if(!Registered || __instance._aspects==null)return;
            foreach(var id in AllIds)
            {
                if(__instance._aspects.ContainsKey((EAspect)id))continue;
                // The stock Swarm component is a passive native IAspect holder:
                // ServerInit/ClientInit are no-ops. Keep its new holder inactive;
                // it adds no RPC, prefab, Update or native spawn-side effects.
                // Our authoritative hooks implement the three independent rules.
                var root=new GameObject(infos[id].name+" native holder");root.SetActive(false);
                root.transform.SetParent(__instance.transform,false);
                var holder=root.AddComponent<AspectOfSwarm>();
                AccessTools.Property(holder.GetType(),"Info").SetValue(holder,infos[id].Cast<AspectOfSwarmInfo>());
                __instance._aspects.Add((EAspect)id,holder.Cast<IAspect>());
            }
        }
        catch(Exception e){Report(e);}
    }
    private static void Saved(SessionSaveData __instance)
    {try{WriteMarker(__instance);}catch(Exception e){Report(e);}}
    private static void WriteMarker(SessionSaveData? saved)
    {
        var ids=new List<int>();
        if(saved!=null && saved.HasData && saved.Data.Aspects!=null)foreach(var id in saved.Data.Aspects)if(AspectRules.IsCustom((int)id))ids.Add((int)id);
        ids.Sort();var key=string.Join(",",ids);if(marked==key)return;
        var path=Path.Combine(Paths.ConfigPath,Plugin.Id+".aspects-run.json");
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new AspectRunMarker{RequiresMod=ids.Count>0,Aspects=ids.ToArray()}));File.Move(path+".tmp",path,true);marked=key;
    }
    internal static void Tick()
    {
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+.5f;
        try{Ensure();UnlockProfile();if(SaveLoadManager.IsReady)WriteMarker(SaveLoadManager.SessionSaveData);}catch(Exception e){Report(e);}
    }
}
