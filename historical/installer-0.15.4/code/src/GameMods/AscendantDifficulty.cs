using Gatekeeper.CameraScripts.HUD.Difficulty;
using Gatekeeper.CameraScripts.HUD.StatisticPanel.Aspects;
using Gatekeeper.CameraScripts.HUD.EvolutionPanel.EvolutionStatSelector;
using Gatekeeper.Char_Scripts;
using Gatekeeper.General.Aspects;
using Gatekeeper.Infrastructure.Providers.InfoProviders;
using Gatekeeper.Interfaces;
using Gatekeeper.General.Predictor;
using Gatekeeper.MainMenuScripts.MainMenu.AspectsPanel;
using Gatekeeper.MainMenuScripts.MainMenu.CharacterSelectPanel;
using Gatekeeper.Mods;
using Gatekeeper.Network.GameLobby;
using HarmonyLib;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

namespace Gatekeeper.CustomMods;

// A native run-setup toggle. Its private Aspect ID is serialized/synchronized by
// the game's existing run systems, but never appears in the Aspect selection grid.
internal static class AscendantDifficulty
{
    private static Toggle? toggle;
    private static TMP_Text? label;
    private static Image? background;
    private static nint initializedLobby;
    private static BepInEx.Configuration.ConfigEntry<bool>? preferred;
    private static bool? savedChoice;
    private static string ChoicePath=>System.IO.Path.Combine(BepInEx.Paths.ConfigPath,Plugin.Id+".ascendant-choice.json");
    private static bool host;
    private static float next,sampleAt;
    internal static float Power {get;private set;}=1;
    internal static bool Active=>AspectGameplay.Active(AscendantRules.SaveId);
    internal static string? Error {get;private set;}
    internal static bool Selected
    {
        get
        {
            var panel=AspectsPanelController._instance;
            if(panel!=null)foreach(var info in panel._selectedAspects)if((byte)info.Type==AscendantRules.SaveId)return true;
            return false;
        }
    }
    internal static void Patch(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(PanelDifficulty),nameof(PanelDifficulty.Setup)),postfix:new(typeof(AscendantDifficulty),nameof(Setup)));
        h.Patch(AccessTools.Method(typeof(GameDifficultyIndicator),"UpdateDifficulty"),postfix:new(typeof(AscendantDifficulty),nameof(Hud)));
        h.Patch(AccessTools.PropertyGetter(typeof(AspectsPanelController),nameof(AspectsPanelController.SelectedAspectsCount)),postfix:new(typeof(AscendantDifficulty),nameof(Count)));
        // Native callers inline the count getter. Filter only while native UI
        // calculations execute, restoring the network/save flag even on error.
        foreach(var method in new[]{"UpdateText","CalculateBonus"})
            h.Patch(AccessTools.Method(typeof(AspectsPanelController),method),prefix:new(typeof(AscendantDifficulty),nameof(FilterPanel)),finalizer:new(typeof(AscendantDifficulty),nameof(RestorePanel)));
        h.Patch(AccessTools.Method(typeof(CharactersMenuController),"UpdateAspectCounter"),prefix:new(typeof(AscendantDifficulty),nameof(FilterCounter)),finalizer:new(typeof(AscendantDifficulty),nameof(RestoreCounter)));
        h.Patch(AccessTools.Method(typeof(AspectsManager),"GetAspects"),prefix:new(typeof(AscendantDifficulty),nameof(VisibleAspects)));
        h.Patch(AccessTools.PropertyGetter(typeof(AspectsManager),nameof(AspectsManager.TotalActiveCount)),postfix:new(typeof(AscendantDifficulty),nameof(ActiveCount)));
        h.Patch(AccessTools.PropertyGetter(typeof(StatisticAspectCalculator),nameof(StatisticAspectCalculator.HasAspects)),prefix:new(typeof(AscendantDifficulty),nameof(Score)));
        foreach(var method in new[]{"CalculateTotalScore","GetRawScoreModifier","GetViewScoreModifier"})
            h.Patch(AccessTools.Method(typeof(StatisticAspectCalculator),method),prefix:new(typeof(AscendantDifficulty),nameof(Score)));
        preferred=Plugin.Current!.Config.Bind("Ascendant","EnabledForNewRuns",true,"Start new runs with Ascendant enabled. Changing the menu toggle remembers your choice; continuing a saved run keeps that run's setting.");
        try{if(System.IO.File.Exists(ChoicePath)){using var choice=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(ChoicePath));savedChoice=choice.RootElement.GetProperty("enabled").GetBoolean();}}
        catch(Exception e){Plugin.Logger.LogWarning("Ascendant preference could not be read; using the default: "+e.Message);}
    }
    private static void Count(AspectsPanelController __instance,ref int __result)
    {
        foreach(var info in __instance._selectedAspects)if((byte)info.Type==AscendantRules.SaveId){__result=Math.Max(0,__result-1);break;}
    }
    private static void FilterPanel(AspectsPanelController __instance,out BaseAspectInfo? __state)
    {
        __state=null;
        foreach(var info in __instance._selectedAspects)if((byte)info.Type==AscendantRules.SaveId){__state=info;break;}
        if(__state!=null)__instance._selectedAspects.Remove(__state);
    }
    private static void RestorePanel(AspectsPanelController __instance,BaseAspectInfo? __state)
    {if(__state!=null)__instance._selectedAspects.Add(__state);}
    private static void FilterCounter(CharactersMenuController __instance,out BaseAspectInfo? __state)=>FilterPanel(__instance.PanelAspect,out __state);
    private static void RestoreCounter(CharactersMenuController __instance,BaseAspectInfo? __state)=>RestorePanel(__instance.PanelAspect,__state);
    private static bool VisibleAspects(AspectsManager __instance,bool __0,ref Il2CppSystem.Collections.Generic.IEnumerable<EAspect> __result)
    {
        var visible=new Il2CppSystem.Collections.Generic.List<EAspect>();
        foreach(var pair in __instance._aspects)
            if((byte)pair.Key!=AscendantRules.SaveId && __instance._activeAspectsSync.Contains(pair.Key) &&
                DatabaseInfoProvider.Aspects.GetAspectInfo(pair.Key).IsPositive==__0)visible.Add(pair.Key);
        __result=visible.Cast<Il2CppSystem.Collections.Generic.IEnumerable<EAspect>>();return false;
    }
    private static void ActiveCount(AspectsManager __instance,ref int __result)
    {if(__instance._activeAspectsSync.Contains((EAspect)AscendantRules.SaveId))__result=Math.Max(0,__result-1);}
    private static void Score(StatisticAspectCalculator __instance)
    {
        // The native result builder inlines the calculator constructor. Normalize
        // its fields at concrete score entry points as well as filtering the
        // manager's positive/negative lists. Recompute to remain idempotent.
        if(!GlobalPredictor.Aspects.Contains((EAspect)AscendantRules.SaveId))return;
        var positive=0;var negative=0;
        foreach(var id in GlobalPredictor.Aspects)
        {
            if((byte)id==AscendantRules.SaveId)continue;
            if(DatabaseInfoProvider.Aspects.GetAspectInfo(id).IsPositive)positive++;else negative++;
        }
        __instance._positiveAspectsCount=positive;__instance._negativeAspectsCount=negative;
        __instance._totalActiveCount=positive+negative;
    }
    private static void Setup(PanelDifficulty __instance,bool __0)
    {
        try
        {
            host=__0;
            if(toggle==null || toggle.transform.parent?.Pointer!=__instance.transform.Pointer)
            {
                var root=new GameObject("Forge Expansion - Ascendant difficulty");
                var rect=root.AddComponent<RectTransform>();rect.SetParent(__instance.transform,false);
                rect.anchorMin=new(0,0);rect.anchorMax=new(1,0);rect.pivot=new(.5f,1);rect.anchoredPosition=new(0,-5);rect.sizeDelta=new(0,62);
                root.AddComponent<LayoutElement>().ignoreLayout=true;
                background=root.AddComponent<Image>();background.color=Color.white;
                toggle=root.AddComponent<Toggle>();toggle.targetGraphic=background;
                var colors=toggle.colors;colors.normalColor=new(.13f,.22f,.29f,1);colors.highlightedColor=new(.22f,.37f,.46f,1);
                colors.pressedColor=new(.08f,.18f,.25f,1);colors.selectedColor=new(.20f,.34f,.43f,1);colors.disabledColor=new(.09f,.12f,.16f,1);toggle.colors=colors;
                var outline=root.AddComponent<Outline>();outline.effectColor=new(.32f,.68f,.82f,.8f);outline.effectDistance=new(1,-1);
                var mark=new GameObject("Ascendant check");var markRect=mark.AddComponent<RectTransform>();markRect.SetParent(rect,false);
                markRect.anchorMin=markRect.anchorMax=new(0,.5f);markRect.anchoredPosition=new(18,0);markRect.sizeDelta=new(18,18);
                var box=mark.AddComponent<Image>();box.color=new(.65f,.78f,.85f,1);box.raycastTarget=false;
                var fill=new GameObject("Enabled fill");var fillRect=fill.AddComponent<RectTransform>();fillRect.SetParent(markRect,false);
                fillRect.anchorMin=Vector2.zero;fillRect.anchorMax=Vector2.one;fillRect.offsetMin=new(3,3);fillRect.offsetMax=new(-3,-3);
                var graphic=fill.AddComponent<Image>();graphic.color=new(.10f,.65f,.85f,1);graphic.raycastTarget=false;toggle.graphic=graphic;
                label=Object.Instantiate(__instance.textPercentage,rect);label.name="Ascendant description";
                var textRect=label.rectTransform;textRect.anchorMin=Vector2.zero;textRect.anchorMax=Vector2.one;textRect.offsetMin=new(35,3);textRect.offsetMax=new(-7,-3);
                label.enableAutoSizing=false;label.fontSize=14;label.fontStyle=FontStyles.Normal;label.alignment=TextAlignmentOptions.MidlineLeft;label.richText=true;label.raycastTarget=false;
                // Remove localization controllers inherited from the native percentage label.
                foreach(var localize in label.GetComponents<I2.Loc.Localize>())Object.Destroy(localize);
                toggle.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<bool>>((Action<bool>)Changed));
            }
            Sync();
        }
        catch(Exception e){Report(e);}
    }
    private static void Changed(bool enabled)
    {
        var lobby=LobbyManager._instance;
        if(lobby==null || !lobby.IsServerStarted || lobby.IsLocked || !ForgeLobby.CanUseAspects){Sync();return;}
        lobby.ServerUpdateAspectsPanel((EAspect)AscendantRules.SaveId,enabled);
        if(preferred!=null)preferred.Value=enabled;
        savedChoice=enabled;
        try{System.IO.File.WriteAllText(ChoicePath+".tmp",System.Text.Json.JsonSerializer.Serialize(new{enabled}));System.IO.File.Move(ChoicePath+".tmp",ChoicePath,true);}
        catch(Exception e){Plugin.Logger.LogWarning("Ascendant choice could not be saved: "+e.Message);}
        Sync();
    }
    private static void Sync()
    {
        if(toggle==null)return;
        toggle.SetIsOnWithoutNotify(Selected);
        toggle.interactable=host && LobbyManager._instance?.IsLocked!=true && ForgeLobby.CanUseAspects;
        if(label!=null)label.text="<color=#8BE6FF><b>ASCENDANT</b></color>  <b>"+(Selected?"ON":"OFF")+"</b>"+
            "\n<size=90%>Enemies adapt as your build grows.\nTougher foes. More elite encounters.</size>";
    }
    private static void Hud(GameDifficultyIndicator __instance)
    {
        if(GlobalPredictor.Aspects.Contains((EAspect)AscendantRules.SaveId) || Active)
            __instance.difficultyText.text+="\n<size=70%><color=#65D6FF>ASCENDANT</color></size>";
    }
    internal static float SamplePower()
    {
        if(Time.unscaledTime<sampleAt)return Power;
        sampleAt=Time.unscaledTime+2;
        var powers=new List<float>(4);
        foreach(var p in GameWorldIndex.Players())
        {
            if(!p.IsSpawned || p.Data?.DamageData==null || p.Data.AttackSpeedData==null || p.PlayableCharacterInfo==null)continue;
            var stats=p.PlayableCharacterInfo.Stats;
            float Base(StatType stat,float fallback)=>stats.TryGetValue(stat,out var value)?value.ValueBase:fallback;
            var damage=p.Data.DamageData;
            var crit=AscendantRules.ExpectedCrit(damage.FinalCritChance,damage.FinalCritDamagePerc);
            var baseCrit=AscendantRules.ExpectedCrit(Base(StatType.CritChance,0),Base(StatType.CritDamagePercentage,1));
            // Native FinalDamage already includes Opal Eye and Adrenaline Pill.
            // Do not count their item modifiers a second time.
            powers.Add(AscendantRules.Power(damage.FinalDamage/Math.Max(.01f,Base(StatType.Damage,40)),
                p.Data.AttackSpeedData.AttackSpeed/Math.Max(.01f,Base(StatType.AttackSpeed,1)),crit/baseCrit));
        }
        Power=AscendantRules.TeamPower(powers);return Power;
    }
    internal static void Tick()
    {
        if(Time.unscaledTime<next)return;next=Time.unscaledTime+.5f;
        try
        {
            var lobby=LobbyManager._instance;
            if(toggle!=null && host && lobby!=null && lobby.IsServerStarted && !lobby.IsLocked &&
                initializedLobby!=lobby.Pointer && AspectsPanelController._instance!=null && ForgeLobby.CanUseAspects)
            {
                initializedLobby=lobby.Pointer;
                lobby.ServerUpdateAspectsPanel((EAspect)AscendantRules.SaveId,savedChoice??preferred?.Value??true);
            }
            if(lobby==null)initializedLobby=0;
            Sync();if(!Active){Power=1;sampleAt=0;}
        }
        catch(Exception e){Report(e);}
    }
    private static void Report(Exception e)
    {if(Error==e.Message)return;Error=e.Message;Plugin.Logger.LogError("Ascendant difficulty: "+e);}
}
