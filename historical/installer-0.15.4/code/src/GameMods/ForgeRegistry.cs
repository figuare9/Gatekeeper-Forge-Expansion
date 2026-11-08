using Gatekeeper.General.SaveLoad.Session;
using Gatekeeper.CameraScripts.HUD.EvolutionPanel.EvolutionStatSelector;
using Gatekeeper.MainMenuScripts.MainMenu.CharacterSelectPanel.PanelCharacterInfo;
using Gatekeeper.CameraScripts.HUD.SkillPanelStuff;
using Gatekeeper.CameraScripts.HUD.StatisticPanel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BepInEx;
using Doozy.Runtime.UIManager.Components;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.Base;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.Char_Scripts.Hybrid;
using Gatekeeper.Char_Scripts.General.SupportPack;
using Gatekeeper.General.SaveLoad;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.EnvironmentStuff.OmniStatue;
using Gatekeeper.MainMenuScripts.Database;
using Gatekeeper.MainMenuScripts.MainMenu.CharacterSelectPanel;
using Gatekeeper.MainMenuScripts.MainMenu.CharacterSelectPanel.Characters;
using Gatekeeper.MainMenuScripts.MainMenu.CharacterSelectPanel.CharacterSelectorPanel;
using Gatekeeper.Mods;
using HarmonyLib;
using I2.Loc;
using TMPro;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static class ForgeRegistry
{
    internal static readonly CharacterType Type=(CharacterType)ForgeRules.CharacterId;
    internal static bool PatchesReady;
    internal static bool Enabled=>PatchesReady && !failed && Plugin.Current?.Forge.Value==true;
    internal static PlayableCharacterInfo? Info {get;private set;}
    internal static bool PrefabReady {get;private set;}
    internal static bool MenuReady=>card!=null;
    internal static string? Error=>error;
    private static string stage="waiting for character data";
    private static GameObject? storage;
    private static NetworkManager? network;
    private static CharacterSelectorPanel? selector;
    private static CharacterSelectorCard? card;
    private static TextMeshProUGUI? lockedMark;
    private static float next;
    private static bool? marked;
    private static string? error;
    private static bool failed;
    private static readonly Dictionary<string,string> terms=new()
    {
        [ForgeRules.Key+".name"]="FORGE",
        [ForgeRules.Key+".description"]="THE ENGINEER\nPiercing rivets, automated turrets and explosive charges.\nBoost nearby turrets. Repair while standing still.\nShield and heal your team with Aegis.",
        [ForgeRules.Key+".unlock"]=ForgeUnlockRules.Requirement,
    };
    private static readonly string[] names={"","RIVET CANNON","SENTRY DEPLOYMENT","VOLATILE CHARGE","AEGIS FIELD","FIELD ENGINEER"};
    private static readonly string[] descriptions=ForgeTooltips.Descriptions;
    private static readonly string[][] upgrades=ForgeTooltips.Upgrades;

    internal static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Constructor(typeof(GatekeepersInfoProvider)),postfix:new(typeof(ForgeRegistry),nameof(Provider)));
        h.Patch(AccessTools.Method(typeof(CharacterSelectorPanel),"Awake"),postfix:new(typeof(ForgeRegistry),nameof(Panel)));
        h.Patch(AccessTools.Method(typeof(SaveLoadManager),nameof(SaveLoadManager.GetUnlockedStatus)),prefix:new(typeof(ForgeRegistry),nameof(Unlocked)));
        h.Patch(AccessTools.Method(typeof(AchievementsInfoProvider),nameof(AchievementsInfoProvider.IsUnlocked),new[]{typeof(CharacterType)}),prefix:new(typeof(ForgeRegistry),nameof(CharacterUnlocked)));
        foreach(var name in new[]{nameof(LocalizationManager.GetTranslation),nameof(LocalizationManager.GetTranslationByManager)})
            h.Patch(AccessTools.Method(typeof(LocalizationManager),name),prefix:new(typeof(ForgeRegistry),nameof(Translation)));
        h.Patch(AccessTools.Method(typeof(LocalizationManager),nameof(LocalizationManager.TryGetTranslation)),prefix:new(typeof(ForgeRegistry),nameof(TryTranslation)));
        foreach(var name in new[]{"OnPointerClick","OnSubmit"})h.Patch(AccessTools.Method(typeof(UIButton),name),prefix:new(typeof(ForgeRegistry),nameof(SelectCard)));
        h.Patch(AccessTools.PropertyGetter(typeof(HybridMain),nameof(HybridMain.CanAvoidDeathByPassive)),prefix:new(typeof(ForgeRegistry),nameof(NoReplicant)));
        h.Patch(AccessTools.Method(typeof(PlayableMenuCharacter),nameof(PlayableMenuCharacter.Setup)),postfix:new(typeof(ForgeRegistry),nameof(MenuArt)));
        // SpawnCharacter inlines PlayableMenuCharacter.Setup, but calls this
        // concrete skin setup before returning the newly instantiated model.
        h.Patch(AccessTools.Method(typeof(CharMenuSkinController),nameof(CharMenuSkinController.Setup)),postfix:new(typeof(ForgeRegistry),nameof(MenuSkinArt)));
        h.Patch(AccessTools.Method(typeof(MenuCharacterSpawner),nameof(MenuCharacterSpawner.SpawnCharacter)),postfix:new(typeof(ForgeRegistry),nameof(MenuSpawned)));
        h.Patch(AccessTools.Method(typeof(CharManager),"ClientInitComponents"),postfix:new(typeof(ForgeRegistry),nameof(PlayerArt)));
        h.Patch(AccessTools.Method(typeof(OmniStatuesInfoProvider),nameof(OmniStatuesInfoProvider.GetOmniStatueInfo),new[]{typeof(CharacterType)}),prefix:new(typeof(ForgeRegistry),nameof(NoOmni)));
        h.Patch(AccessTools.Method(typeof(PanelCharacterInfo),"HandleOmniStatueInfoChanged"),prefix:new(typeof(ForgeRegistry),nameof(OmniPanel)));
        h.Patch(AccessTools.Method(typeof(PanelCharacterInfo),nameof(PanelCharacterInfo.FillInfo)),postfix:new(typeof(ForgeRegistry),nameof(SkinPanel)));
        h.Patch(AccessTools.Method(typeof(SessionSaveData),nameof(SessionSaveData.Save)),postfix:new(typeof(ForgeRegistry),nameof(Saved)));
        h.Patch(AccessTools.Method(typeof(StatisticPanelView),"FillCharacterName"),prefix:new(typeof(ForgeRegistry),nameof(ResultCharacter)));
        h.Patch(AccessTools.Method(typeof(CharacterSelectorPanel),nameof(CharacterSelectorPanel.Select)),prefix:new(typeof(ForgeRegistry),nameof(SelectAllowed)));
        h.Patch(AccessTools.Method(typeof(CharacterSelectorCard),nameof(CharacterSelectorCard.CheckUnlockCard)),postfix:new(typeof(ForgeRegistry),nameof(LockedPortrait)));
        ForgeUnlock.Patch(h);ForgeLobby.Patch(h);ForgeWorld.Patch(h);
    }
    internal static bool Is(CharManager? p)=>p!=null && p.PlayableCharacterInfo!=null && p.PlayableCharacterInfo.Id==ForgeRules.Key;
    private static void Provider(GatekeepersInfoProvider __instance){try{Ensure(__instance);}catch(Exception e){Report(e);}}
    private static bool Unlocked(string __0,ref bool __result)
    {if(__0!=ForgeRules.Key)return true;__result=Enabled && ForgeUnlock.Unlocked;return false;}
    private static bool CharacterUnlocked(CharacterType __0,ref bool __result)
    {if(__0!=Type)return true;__result=Enabled && ForgeUnlock.Unlocked;return false;}
    private static bool SelectAllowed(CharacterSelectorCard __0)=>__0?.CharacterInfo?.Id!=ForgeRules.Key || Enabled && ForgeUnlock.Unlocked && ForgeLobby.CanChoose;
    private static void LockedPortrait(CharacterSelectorCard __instance)
    {
        if(__instance.CharacterInfo?.Id!=ForgeRules.Key)return;
        var unlocked=ForgeUnlock.Unlocked;
        __instance.characterIcon.enabled=unlocked;
        if(lockedMark==null || lockedMark.transform.parent?.Pointer!=__instance.characterIcon.transform.Pointer)
        {
            var root=new GameObject("Forge locked question mark");
            var rect=root.AddComponent<RectTransform>();rect.SetParent(__instance.characterIcon.transform,false);
            rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            lockedMark=root.AddComponent<TextMeshProUGUI>();
            lockedMark.font=Object.FindObjectOfType<TextMeshProUGUI>()?.font??TMP_Settings.defaultFontAsset;
            lockedMark.text="?";lockedMark.fontSize=70;lockedMark.fontStyle=FontStyles.Bold;
            lockedMark.alignment=TextAlignmentOptions.Center;lockedMark.color=new(.45f,.49f,.55f,1);
            lockedMark.raycastTarget=false;
        }
        lockedMark.gameObject.SetActive(!unlocked);
    }
    internal static void RefreshUnlock()
    {
        if(Info!=null)Info.unlocked=ForgeUnlock.Unlocked;
        EnsureProfile();
        if(card!=null){card.UpdateInfo();card.CheckUnlockCard();card.SetLock(!ForgeUnlock.Unlocked || !ForgeLobby.CanChoose);}
    }
    private static bool Translation(string __0,ref string __result)
    {if(!terms.TryGetValue(__0??"",out var value))return true;__result=value;return false;}
    private static bool TryTranslation(string __0,ref string __1,ref bool __result)
    {if(!terms.TryGetValue(__0??"",out var value))return true;__1=value;__result=true;return false;}
    private static bool NoOmni(CharacterType __0,ref OmniStatueInfo __result)
    {if(__0!=Type)return true;__result=null!;return false;}
    private static bool OmniPanel(PanelCharacterInfo __instance)
    {if(__instance._characterInfo?.Id!=ForgeRules.Key)return true;SkinPanel(__instance);return false;}
    private static void SkinPanel(PanelCharacterInfo __instance)
    {
        if(__instance._characterInfo?.Id!=ForgeRules.Key)return;
        __instance._isOmniSkinUnlocked=false;__instance._skinType=CharSkinType.Default;
        __instance.skinToggles[CharSkinType.Default].gameObject.SetActive(true);
        __instance.skinToggles[CharSkinType.Omni].gameObject.SetActive(false);
        __instance.omniSkinTooltipTrigger.enabled=false;
    }
    private static void EnsureProfile()
    {
        if(!SaveLoadManager.IsReady || SaveLoadManager.DataSave==null)return;
        var data=SaveLoadManager.DataSave;
        if(data.SelectedSkins==null || data.DatabaseElements==null)return;
        data.SelectedSkins[Type]=CharSkinType.Default;
        var unlocked=ForgeUnlock.Unlocked;
        if(!data.DatabaseElements.ContainsKey(ForgeRules.Key))data.DatabaseElements.Add(ForgeRules.Key,new DatabaseElementData(ForgeRules.Key,unlocked,true,0));
        else data.DatabaseElements[ForgeRules.Key].Unlocked=unlocked;
    }
    private static void Saved(SessionSaveData __instance)
    {try{WriteMarker(__instance);}catch(Exception e){Plugin.Logger.LogError("Could not update the Forge saved-run marker: "+e);}}
    private static void WriteMarker(SessionSaveData? saved)
    {
        var required=saved!=null && saved.HasData && saved.Data.CharacterData.SelectedCharacter==Type;
        if(marked==required)return;
        var path=Path.Combine(Paths.ConfigPath,Plugin.Id+".forge-run.json");
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(new ForgeRunMarker(required,ForgeRules.CharacterId)));File.Move(path+".tmp",path,true);marked=required;
    }
    private static bool NoReplicant(HybridMain __instance,ref bool __result)
    {if(!Is(__instance.Manager))return true;__result=false;return false;}
    private static void MenuArt(PlayableMenuCharacter __instance,PlayableCharacterInfo __0)
    {if(__0?.Id==ForgeRules.Key)ForgeAvatar.Attach(__instance.gameObject);}
    private static void MenuSkinArt(CharMenuSkinController __instance,PlayableCharacterInfo __0)
    {
        if(__0?.Id!=ForgeRules.Key)return;
        var menu=__instance.GetComponent<PlayableMenuCharacter>();
        if(menu!=null)ForgeAvatar.Attach(menu.gameObject);
    }
    private static void PlayerArt(CharManager __instance)
    {if(Is(__instance))ForgeAvatar.Attach(__instance.gameObject,__instance);}
    private static void MenuSpawned(MenuCharacterSpawner __instance,CharacterType __1,bool __2)
    {if(__2 && __1==Type && __instance._menuCharacter!=null)ForgeAvatar.Attach(__instance._menuCharacter.gameObject);}
    private static bool ResultCharacter(StatisticPanelView __instance)
    {
        // Native result naming switches over the fixed stock-character enum.
        if(!Is(__instance._selectedCharacter))return true;
        __instance.characterNameText.text="FORGE";
        __instance.characterPortrait.sprite=ForgeAvatar.Portrait;
        return false;
    }
    private static bool SelectCard(UIButton __instance)
    {
        var selected=__instance.GetComponentInParent<CharacterSelectorCard>();
        if(selected==null || selected.CharacterInfo?.Id!=ForgeRules.Key)return true;
        if(Enabled && ForgeUnlock.Unlocked && ForgeLobby.CanChoose){EnsureProfile();selected.selectorPanel.Select(selected);}
        return false;
    }
    private static void Panel(CharacterSelectorPanel __instance)
    {
        selector=__instance;
        try
        {
            Ensure(DatabaseInfoProvider.Gatekeepers);
            EnsureCard(__instance);
        }
        catch(Exception e){Report(e);}
    }
    private static void EnsureCard(CharacterSelectorPanel __instance)
    {
        if(failed || Info==null || __instance.CharacterSelectorCards==null)return;
        var cards=Cards(__instance);
        var existing=cards.FirstOrDefault(c=>c.CharacterInfo?.Id==ForgeRules.Key);
        if(existing!=null){card=existing;return;}
        var donor=cards.FirstOrDefault(c=>c.CharacterInfo?.type==CharacterType.Hybrid);
        if(donor==null)return;
        stage="adding character selection card";
        var clone=Object.Instantiate(donor,storage!.transform,false);
        clone.name="GKMM Forge selection";clone.CharacterInfo=Info;clone.selectorPanel=__instance;
        // Hybrid's full-body portrait deliberately overflows its mask, with a
        // high pivot. Forge has its own bust portrait, fitted to the card.
        var image=clone.characterIcon;image.sprite=ForgeAvatar.Portrait;image.overrideSprite=null;
        image.type=UnityEngine.UI.Image.Type.Simple;image.preserveAspect=false;image.useSpriteMesh=false;
        var portraitRect=image.rectTransform;portraitRect.anchorMin=Vector2.zero;portraitRect.anchorMax=Vector2.one;
        portraitRect.pivot=new Vector2(.5f,.5f);portraitRect.anchoredPosition=Vector2.zero;portraitRect.sizeDelta=Vector2.zero;
        clone.transform.SetParent(donor.transform.parent,false);
        clone.transform.SetAsLastSibling();
        if(donor.transform.parent.GetComponent<UnityEngine.UI.LayoutGroup>()==null)
        {
            var last=__instance.CharacterSelectorCards[__instance.CharacterSelectorCards.Count-1].GetComponent<RectTransform>();
            var rect=clone.GetComponent<RectTransform>();rect.anchoredPosition=last.anchoredPosition+new Vector2(Math.Max(48,last.rect.width)+8,0);
        }
        __instance.CharacterSelectorCards.Add(clone);
        clone.gameObject.SetActive(true);clone.UpdateInfo();clone.CheckUnlockCard();
        selector=__instance;card=clone;
        Plugin.Logger.LogInfo("Forge added to character selection.");
        Plugin.WriteStatus("ready");
    }
    private static CharacterSelectorCard[] Cards(CharacterSelectorPanel panel)
    {var list=new List<CharacterSelectorCard>();if(panel.CharacterSelectorCards!=null)foreach(var c in panel.CharacterSelectorCards)if(c!=null)list.Add(c);return list.ToArray();}
    internal static int CardIndex
    {
        get
        {
            if(selector==null)return -1;
            for(var i=0;i<selector.CharacterSelectorCards.Count;i++)if(selector.CharacterSelectorCards[i].CharacterInfo?.Id==ForgeRules.Key)return i;
            return -1;
        }
    }
    internal static string Roster()
    {
        if(selector==null || card==null || !PrefabReady)return "";
        var value=string.Join("|",Cards(selector).Select(c=>$"{(int)c.CharacterInfo.type}:{c.CharacterInfo.Id}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
    private static void Ensure(GatekeepersInfoProvider? provider)
    {
        if(failed || provider==null || provider.PlayableCharacterInfos==null || provider.PlayableCharacterSkillInfos==null)return;
        if(Info!=null){EnsureProfile();return;}
        if(provider.PlayableCharacterInfos.TryGetValue(Type,out var existing))throw new InvalidOperationException("Forge character ID is occupied: "+existing.Id);
        if(!provider.PlayableCharacterInfos.TryGetValue(CharacterType.Hybrid,out var donor) || donor==null ||
            donor.Stats==null || donor.SkillInfos==null || donor.characterPrefab==null || donor.menuPrefab==null)return;
        for(var i=1;i<=5;i++)
            if(!donor.SkillInfos.TryGetValue((SkillType)i,out var pad) || pad==null || pad.SkillInfo==null)return;
        stage="cloning character data";
        storage=new GameObject("GKMM Forge templates");storage.SetActive(false);Object.DontDestroyOnLoad(storage);
        var info=Object.Instantiate(donor);Object.DontDestroyOnLoad(info);
        info.name="GKMM Forge prototype";info.Id=ForgeRules.Key;info.type=Type;
        info.dlcId=Gatekeeper.DownloadableContent.DlcID.None;info.unlocked=ForgeUnlock.Unlocked;info.wasSeen=true;
        info.characterNameKey=ForgeRules.Key+".name";info.characterDescriptionKey=ForgeRules.Key+".description";info.characterHowToOpenKey=ForgeRules.Key+".unlock";
        info.Stats=new();foreach(var pair in donor.Stats)info.Stats.Add(pair.Key,new StatInfo{ValueBase=pair.Value.ValueBase});
        info.Stats[StatType.MoveSpeed]=new StatInfo{ValueBase=ForgeRules.BaseMoveSpeed};
        info.Stats[StatType.Health]=new StatInfo{ValueBase=ForgeRules.BaseHealth};
        info.Stats[StatType.HealthRegeneration]=new StatInfo{ValueBase=ForgeRules.BaseRegeneration};
        info.SkillInfos=new();
        for(var i=1;i<=5;i++)
        {
            var skill=Object.Instantiate(donor.SkillInfos[(SkillType)i].SkillInfo);Object.DontDestroyOnLoad(skill);
            skill.name="Forge "+names[i];skill.SkillNameKey=ForgeRules.Key+".skill."+i;terms[skill.SkillNameKey]=names[i];
            skill.SkillDescriptionKey=skill.SkillNameKey+".description";terms[skill.SkillDescriptionKey]=descriptions[i];
            var keys=new string[3];for(var j=0;j<3;j++){keys[j]=skill.SkillNameKey+".evo."+j;terms[keys[j]]=upgrades[i][j];}
            skill.SkillEvoDescriptionKeys=keys;skill.SkillParams=new();skill.SkillEvoParams=new();
            // Native metadata stores integer seconds; the primary's host-owned
            // attack-speed recharge uses ForgeRules.RivetInterval instead.
            skill.Charges=i==5 ? 0:1;skill.CooldownTime=i switch{1=>1,2=>10,3=>6,4=>16,_=>0};
            skill.CooldownType=i==5 ? Gatekeeper.CameraScripts.HUD.SkillPanelStuff.SkillCooldownType.NonCD:Gatekeeper.CameraScripts.HUD.SkillPanelStuff.SkillCooldownType.CD;
            skill.OriginalSprite=skill.AlternativeSprite=ForgeArt.Icon(i);
            for(var j=1;j<=3;j++)skill.SkillEvoParams.Add(j,new());
            skill.Setup();provider.PlayableCharacterSkillInfos.Add(skill);info.SkillInfos.Add((SkillType)i,new CharacterSkillInfoPad{SkillInfo=skill});
        }
        info.DatabaseIcon=info.characterMiniPortraitSprite=ForgeAvatar.Portrait;
        stage="cloning gameplay prefab";
        info.characterPrefab=Object.Instantiate(donor.characterPrefab,storage.transform,false);
        info.characterPrefab.name="GKMM Forge gameplay template";
        // Hybrid_Character is a network container; its CharManager is on the
        // inactive Hybrid child, not on the prefab root.
        var character=info.characterPrefab.GetComponentInChildren<CharManager>(true);
        if(character==null)throw new InvalidOperationException("Forge's cloned gameplay prefab has no child character controller.");
        character.PlayableCharacterInfo=info;
        info.characterPrefab.SetActive(true);
        stage="cloning menu prefab";
        info.menuPrefab=Object.Instantiate(donor.menuPrefab,storage.transform,false);info.menuPrefab.name="GKMM Forge menu template";info.menuPrefab.gameObject.SetActive(true);
        var artMarker=new GameObject("GKMM Forge model marker");artMarker.transform.SetParent(info.menuPrefab.transform,false);
        stage="preparing Forge menu visual";ForgeAvatar.PrepareMenu(info.menuPrefab.gameObject);
        stage="preparing Forge sentry graphics";ForgeEquipment.Prepare();ForgeArt.PrepareRivets();ForgeShotFx.Prepare();
        provider.PlayableCharacterInfos.Add(Type,info);Info=info;EnsureProfile();
        Plugin.Logger.LogInfo("Forge prototype registered with independent character/skill data. Awaiting network prefab registration.");
        Plugin.WriteStatus("ready");
    }
    internal static void Tick()
    {
        if(!PatchesReady || failed || Time.unscaledTime<next)return;next=Time.unscaledTime+.5f;
        try
        {
            var provider=DatabaseInfoProvider.Gatekeepers;if(provider==null)return;Ensure(provider);
            var manager=InstanceFinder.NetworkManager;
            if(network!=null && manager!=null && manager.Pointer==network.Pointer && !manager._runtimeSpawnablePrefabs.ContainsKey(ForgeRules.CollectionId)){network=null;PrefabReady=false;}
            if(Info!=null && manager!=null && manager.Initialized && (network==null || network.Pointer!=manager.Pointer))
            {
                stage="registering network prefab";
                PrefabReady=false;
                if(manager._runtimeSpawnablePrefabs.ContainsKey(ForgeRules.CollectionId))throw new InvalidOperationException("Forge network collection is already occupied.");
                var collection=ScriptableObject.CreateInstance<SinglePrefabObjects>();collection.SetCollectionId(ForgeRules.CollectionId);
                Object.DontDestroyOnLoad(collection);
                collection.AddObject(Info.characterPrefab.GetComponent<NetworkObject>(),false,true);
                manager._runtimeSpawnablePrefabs.Add(ForgeRules.CollectionId,collection);
                network=manager;PrefabReady=true;
                Plugin.Logger.LogInfo("Forge network prefab registered in dedicated collection "+ForgeRules.CollectionId);
                Plugin.WriteStatus("ready");
            }
            if(Info!=null && selector!=null)EnsureCard(selector);
            // The concrete menu spawner covers inlined Setup paths without
            // repeatedly searching the scene. Gameplay has a native roster.
            foreach(var player in GameWorldIndex.Players())
                if(Is(player))ForgeAvatar.Attach(player.gameObject,player);
            if(card!=null){card.CheckUnlockCard();card.SetLock(!ForgeUnlock.Unlocked || !ForgeLobby.CanChoose);}
            if(SaveLoadManager.IsReady)WriteMarker(SaveLoadManager.SessionSaveData);
        }
        catch(Exception e){PrefabReady=false;Report(e);}
    }
    private static void Report(Exception e){failed=true;var message=stage+": "+e.Message;if(error==message)return;error=message;Plugin.Logger.LogError("Forge registration ("+stage+"): "+e);Plugin.WriteStatus("ready");}
}
