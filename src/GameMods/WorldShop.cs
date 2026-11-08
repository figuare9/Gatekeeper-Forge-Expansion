using Gatekeeper.Char_Scripts;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.EnvironmentStuff.Emporium;
using Gatekeeper.General.InputSystem;
using Gatekeeper.General.InputSystem.Enums;
using Gatekeeper.Interfaces;
using Gatekeeper.Mods;
using Gatekeeper.General.Predictor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using System.Text.Json;
using Object = UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static class WorldShop
{
    private static GameObject? terminal;
    private static Emporium? emporiumAnchor;
    private static CharInteractor? interactor;
    private static IntPtr interactorPlayer;
    private static readonly List<Material> materials = new();
    private static CabinetPresentation? presentation;
    private static float[]? remotePose;
    private static int placedScene=-1,remoteScene=-1;
    private static float nextAttempt;
    private static bool placementReported;
    internal static Emporium? EmporiumAnchor => emporiumAnchor;
    internal static bool Exists => terminal != null && placedScene==(int)GlobalPredictor.CurrentScene;
    internal static float[]? Pose => !Exists ? null : new[] { terminal!.transform.position.x, terminal.transform.position.y, terminal.transform.position.z,
        terminal.transform.rotation.x, terminal.transform.rotation.y, terminal.transform.rotation.z, terminal.transform.rotation.w };
    internal static void SetRemotePose(float[] pose,int scene)
    {
        if (scene!=(int)GlobalPredictor.CurrentScene || !CustomModRules.IsRestAreaScene(scene) || pose.Length != 7 || pose.Any(v => !float.IsFinite(v))) return;
        remotePose = pose;remoteScene=scene;
        if (terminal != null) { terminal.transform.position = new(pose[0],pose[1],pose[2]); terminal.transform.rotation = new(pose[3],pose[4],pose[5],pose[6]); }
    }
    internal static Vector3? TerminalPosition => terminal != null ? terminal.transform.position : null;

    internal static void Tick()
    {
        if(terminal!=null && placedScene!=(int)GlobalPredictor.CurrentScene)Clear();
        if (!ShopUI.InRestArea) { Clear(); placementReported = false; return; }
        if (!SlotNetwork.IsHost && (!SlotNetwork.Ready || remotePose == null || remoteScene!=(int)GlobalPredictor.CurrentScene)) return;
        if (terminal != null) { presentation?.Tick(); return; }
        if (Time.unscaledTime < nextAttempt) return;
        nextAttempt = Time.unscaledTime + 3;
        try
        {
            var emporium = Object.FindObjectsOfType<Emporium>().FirstOrDefault(e =>
                e != null && e.isActiveAndEnabled && e.gameObject.scene.IsValid() && e.gameObject.scene.isLoaded);
            if (emporium == null) return;
            var anchor = emporium.CachedTransform != null ? emporium.CachedTransform : emporium.transform;
            Vector3 position; Quaternion facing;
            if (!SlotNetwork.IsHost)
            { position = new(remotePose![0],remotePose[1],remotePose[2]); facing = new(remotePose[3],remotePose[4],remotePose[5],remotePose[6]); }
            else if (!FindPosition(anchor, out position))
            {
                if (!placementReported) Plugin.Logger.LogWarning("No clear ground beside the Emporium yet. Will retry terminal placement.");
                placementReported = true; return;
            }
            else facing = anchor.rotation * Quaternion.Euler(0f, 180f, 0f);
            Build(position, facing, emporium);
            emporiumAnchor = emporium;
            if (SlotNetwork.IsHost) SlotNetwork.PublishReady();
            Plugin.Logger.LogInfo($"Quiet Bay exchange terminal placed beside {emporium.name} at {position}; Emporium at {anchor.position}.");
        }
        catch (Exception ex)
        {
            Clear();
            if (!placementReported) Plugin.Logger.LogWarning("Exchange terminal placement: " + ex);
            placementReported = true;
        }
    }

    private static bool IsNear(CharManager player)
    {
        if (terminal == null || !player.IsActiveAndAlive) return false;
        var delta = player.CachedTransform.position - terminal.transform.position;
        return delta.sqrMagnitude <= SlotRules.Reach * SlotRules.Reach;
    }
    internal static float DistanceSquared(CharManager player) => terminal == null ? float.PositiveInfinity : (player.CachedTransform.position - terminal.transform.position).sqrMagnitude;

    internal static IInteractable? NativeSelection(CharManager player)
    {
        if (interactorPlayer != player.Pointer || interactor == null)
        {
            interactorPlayer = player.Pointer;
            interactor = player.GetComponentInChildren<CharInteractor>();
        }
        return interactor != null ? interactor.CurrentInteractable : null;
    }

    internal static bool CanInteract(CharManager player)
    {
        if (!ShopUI.InRestArea || !SlotNetwork.Ready || ExchangeClient.Visit.Length == 0 || ShopUI.IsOpen || !player.IsOwner || player.Controller.IsInputLocked || Time.timeScale <= 0 || !IsNear(player)) return false;
        // A closer native object keeps its normal prompt and interaction, e.g.
        // an item dropped at the player's feet beside the terminal.
        var current = NativeSelection(player);
        if (current == null || !current.IsActive || !current.IsCanInteract) return true;
        var position = player.CachedTransform.position;
        return (current.CachedTransform.position - position).sqrMagnitude >= (terminal!.transform.position - position).sqrMagnitude;
    }

    // Called before native owner input. This consumes the interaction frame so
    // the same press cannot also activate an ordinary nearby interactable.
    internal static bool TryInteract(CharManager player)
    {
        if (!CanInteract(player) || !GameplayInputManager.ButtonWasPressed(ActionName.Interaction)) return false;
        ShopUI.Open();
        return ShopUI.IsOpen;
    }

    private static bool FindPosition(Transform anchor, out Vector3 position)
    {
        // Try both sides before moving a little farther away. Ground and volume
        // checks avoid embedding the terminal in the Emporium, rocks or stairs.
        foreach (var distance in new[] { 4.8f, 6f, 7.2f })
        foreach (var side in new[] { 1f, -1f })
        {
            var candidate = anchor.position + anchor.right * (distance * side) + anchor.forward * 1.2f;
            var onMesh = NavMesh.SamplePosition(candidate, out var navHit, 1.5f, NavMesh.AllAreas);
            if (onMesh) candidate = navHit.position;
            var hits = Physics.RaycastAll(candidate + Vector3.up * 5, Vector3.down, 10, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits.OrderBy(h => h.distance))
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(anchor) || hit.normal.y < .75f ||
                    Math.Abs(hit.point.y - anchor.position.y) > 2.5f) continue;
                var ground = hit.point + Vector3.up * .04f;
                var occupied = Physics.OverlapBox(ground + Vector3.up * (1.5f * CabinetPresentation.Size), new Vector3(1.25f, 1.22f, 1f) * CabinetPresentation.Size, anchor.rotation, ~0, QueryTriggerInteraction.Ignore);
                if (occupied.Length != 0) continue;
                // If there is navigation data, require that the front is also
                // reachable. Some Quiet Bay builds provide colliders only.
                if (onMesh && !NavMesh.SamplePosition(ground - anchor.forward * 1.8f, out _, 1f, NavMesh.AllAreas)) continue;
                position = ground; return true;
            }
        }
        position = default; return false;
    }

    private static Material Material(Color color, bool glow = false)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) throw new InvalidOperationException("No supported terminal material shader is available.");
        var material = presentation?.Template != null ? new Material(presentation.Template) : new Material(shader);
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", null);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", null);
        if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", null);
        if (material.HasProperty("_MaskMap")) material.SetTexture("_MaskMap", null);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .35f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", .35f);
        if (glow && material.HasProperty("_EmissionColor"))
        { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * 1.7f); }
        else if (material.HasProperty("_EmissionColor")) { material.DisableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black); }
        materials.Add(material); return material;
    }

    private static void Part(string name, Vector3 at, Vector3 size, Material material)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name; part.transform.SetParent(presentation!.Visual, false);
        part.transform.localPosition = at; part.transform.localScale = size;
        part.GetComponent<Renderer>().sharedMaterial = material;
        var collider = part.GetComponent<Collider>(); if (collider != null) collider.enabled = false;
    }

    private static void Build(Vector3 position, Quaternion rotation, Emporium emporium)
    {
        terminal = new GameObject("GKMM Quiet Bay Item Exchange");
        placedScene=(int)GlobalPredictor.CurrentScene;
        SceneManager.MoveGameObjectToScene(terminal, emporium.gameObject.scene);
        terminal.transform.position = position; terminal.transform.rotation = rotation;
        presentation = new(terminal, emporium);
        using var source = typeof(WorldShop).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.exchange-terminal.json")
            ?? throw new InvalidOperationException("Terminal model resource is missing.");
        using var model = JsonDocument.Parse(source);
        var palette = new Dictionary<string, Material>();
        foreach (var entry in model.RootElement.GetProperty("materials").EnumerateObject())
        {
            var color = entry.Value;
            palette.Add(entry.Name, Material(new(color[0].GetSingle(), color[1].GetSingle(), color[2].GetSingle()), entry.Name is "mint" or "screen" or "gold"));
        }
        static Vector3 Vector(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
        foreach (var part in model.RootElement.GetProperty("parts").EnumerateArray())
            Part(part.GetProperty("name").GetString()!, Vector(part.GetProperty("at")), Vector(part.GetProperty("size")), palette[part.GetProperty("material").GetString()!]);
        var body = terminal.AddComponent<BoxCollider>(); body.center = new(0, 1.55f, .1f); body.size = new(2.15f, 3.1f, 1.4f);
        presentation.Bind(materials);
    }

    internal static void Focus(bool selected) => presentation?.Focus(selected);

    internal static void Clear()
    {
        if (terminal != null || emporiumAnchor != null) TerminalPrompt.Hide();
        if (terminal != null) Object.Destroy(terminal);
        terminal = null; emporiumAnchor = null; presentation = null;placedScene=-1;
        if (!Plugin.RestAreaScene || remoteScene!=(int)GlobalPredictor.CurrentScene) {remotePose = null;remoteScene=-1;}
        if (!Plugin.RestAreaGameplay) { interactor = null; interactorPlayer = IntPtr.Zero; }
        foreach (var material in materials) if (material != null) Object.Destroy(material);
        materials.Clear();
    }
}
