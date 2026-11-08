using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.NPC;
using HarmonyLib;

namespace Gatekeeper.CustomMods;

// Follow native spawn/pool lifetimes instead of searching every loaded Unity
// object several times a second. Players already have a native roster.
internal static class GameWorldIndex
{
    private static readonly Dictionary<IntPtr,NpcCharacter> npcs=new();
    private static NpcCharacter[] snapshot=Array.Empty<NpcCharacter>();
    private static bool dirty;
    internal static void Patch(Harmony h)
    {
        foreach(var name in new[]{"OnEnable","OnStartNetwork"})
            h.Patch(AccessTools.DeclaredMethod(typeof(NpcCharacter),name),postfix:new(typeof(GameWorldIndex),nameof(Added)));
        foreach(var name in new[]{"OnDisable","OnStopNetwork","OnDestroy"})
            h.Patch(AccessTools.DeclaredMethod(typeof(NpcCharacter),name),prefix:new(typeof(GameWorldIndex),nameof(Removed)));
    }
    private static void Added(NpcCharacter __instance)
    {if(npcs.TryAdd(__instance.Pointer,__instance))dirty=true;}
    private static void Removed(NpcCharacter __instance)
    {if(npcs.Remove(__instance.Pointer))dirty=true;ForgeWorld.ForgetConstruct(__instance.Pointer);}
    internal static NpcCharacter[] Npcs
    {
        get
        {
            if(dirty){snapshot=npcs.Values.ToArray();dirty=false;}
            return snapshot;
        }
    }
    internal static IEnumerable<CharManager> Players()
    {
        var roster=PlayableCharactersHolder.PlayableCharactersList;
        if(roster==null)yield break;
        foreach(var pair in roster)if(pair.Value!=null)yield return pair.Value;
    }
}
