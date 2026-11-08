using System.Text.Json;
using Gatekeeper.Char_Scripts.General;
using Gatekeeper.EnvironmentStuff.Emporium;
using Gatekeeper.Mods;
using Gatekeeper.General.Predictor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Gatekeeper.CustomMods;

internal static class WorldSlots
{
    private static GameObject? cabinet;
    private static Emporium? anchorObject;
    private static readonly List<Material> materials = new();
    private static readonly List<Texture2D> textures = new();
    private static readonly List<Mesh> panelMeshes = new();
    private static CabinetPresentation? presentation;
    private static readonly Material?[] reels = new Material?[3];
    private static Material? lamps;
    private static Transform? lever;
    private static float nextAttempt;
    private static float[]? remotePose;
    private static int placedScene=-1,remoteScene=-1;
    private static string? lastError;
    internal static bool Exists => cabinet != null && placedScene==(int)GlobalPredictor.CurrentScene;
    internal static Transform? CelebrationOrigin=>Exists ? cabinet!.transform:null;
    internal static Emporium? Anchor => anchorObject;
    internal static float[]? Pose => !Exists ? null : new[] { cabinet!.transform.position.x, cabinet.transform.position.y, cabinet.transform.position.z,
        cabinet.transform.rotation.x, cabinet.transform.rotation.y, cabinet.transform.rotation.z, cabinet.transform.rotation.w };

    internal static void SetRemotePose(float[] pose,int scene)
    {
        if (scene!=(int)GlobalPredictor.CurrentScene || !CustomModRules.IsRestAreaScene(scene) || pose.Length != 7 || pose.Any(v => !float.IsFinite(v))) return;
        remotePose = pose;remoteScene=scene;
        if (cabinet != null) { cabinet.transform.position = new(pose[0],pose[1],pose[2]); cabinet.transform.rotation = new(pose[3],pose[4],pose[5],pose[6]); }
    }
    internal static void Tick()
    {
        if(cabinet!=null && placedScene!=(int)GlobalPredictor.CurrentScene)Clear();
        if (!Plugin.SlotsInRestArea) { Clear(); return; }
        if (cabinet != null) { Animate(); presentation?.Tick(); return; }
        if (Time.unscaledTime < nextAttempt) return;
        nextAttempt = Time.unscaledTime + 2;
        try
        {
            var emporium = Object.FindObjectsOfType<Emporium>().FirstOrDefault(e => e != null && e.isActiveAndEnabled && e.gameObject.scene.IsValid() && e.gameObject.scene.isLoaded);
            if (emporium == null) return;
            var anchor = emporium.CachedTransform ?? emporium.transform;
            Vector3 at; Quaternion rotation;
            if (SlotNetwork.IsHost)
            {
                if (Plugin.ShopActive && !WorldShop.Exists) return;
                var side = WorldShop.TerminalPosition is Vector3 shopAt && Vector3.Dot(shopAt - anchor.position, anchor.right) < 0 ? 1 : -1;
                if (!FindPosition(anchor, side, out at)) return;
                // Publish the final facing so remote players apply the same turn exactly once.
                rotation = anchor.rotation * Quaternion.Euler(0f, 180f, 0f);
            }
            else
            {
                if (remotePose == null || remoteScene!=(int)GlobalPredictor.CurrentScene) return;
                at = new(remotePose[0], remotePose[1], remotePose[2]);
                rotation = new(remotePose[3], remotePose[4], remotePose[5], remotePose[6]);
            }
            Build(at, rotation, emporium);
            SlotConfetti.Prepare();
            anchorObject = emporium;
            if (SlotNetwork.IsHost) SlotNetwork.PublishReady();
            Plugin.Logger.LogInfo($"Prism Jackpot cabinet placed at {at}; Emporium at {anchor.position}; host={SlotNetwork.IsHost}.");
        }
        catch (Exception ex)
        {
            Clear();
            if (lastError != ex.Message) { lastError = ex.Message; Plugin.Logger.LogWarning("Slot cabinet: " + ex); }
        }
    }

    private static bool FindPosition(Transform anchor, int side, out Vector3 position)
    {
        foreach (var distance in new[] { 4.8f, 6f, 7.2f })
        {
            var candidate = anchor.position + anchor.right * distance * side + anchor.forward * 1.2f;
            var onMesh = NavMesh.SamplePosition(candidate, out var nav, 1.5f, NavMesh.AllAreas);
            if (onMesh) candidate = nav.position;
            foreach (var hit in Physics.RaycastAll(candidate + Vector3.up * 5, Vector3.down, 10, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider == null || hit.collider.transform.IsChildOf(anchor) || hit.normal.y < .75f || Math.Abs(hit.point.y - anchor.position.y) > 2.5f) continue;
                var ground = hit.point + Vector3.up * .04f;
                if (Physics.OverlapBox(ground + Vector3.up * (1.6f * CabinetPresentation.Size), new Vector3(1.42f,1.32f,1f) * CabinetPresentation.Size, anchor.rotation, ~0, QueryTriggerInteraction.Ignore).Length != 0) continue;
                if (onMesh && !NavMesh.SamplePosition(ground - anchor.forward * 1.8f, out _, 1f, NavMesh.AllAreas)) continue;
                position = ground; return true;
            }
        }
        position = default; return false;
    }
    internal static float DistanceSquared(CharManager player) => cabinet == null ? float.PositiveInfinity : (player.CachedTransform.position - cabinet.transform.position).sqrMagnitude;
    internal static bool CanOffer(CharManager player)
    {
        if (!Plugin.SlotsInRestArea || cabinet == null || !SlotNetwork.Ready || ShopUI.IsOpen || !player.IsOwner || !player.IsActiveAndAlive ||
            Time.timeScale <= 0 || player.Controller.IsInputLocked && !SlotPlayer.OwnsInput(player) || DistanceSquared(player) > SlotRules.Reach * SlotRules.Reach) return false;
        var current = WorldShop.NativeSelection(player);
        return current == null || !current.IsActive || !current.IsCanInteract ||
            (current.CachedTransform.position - player.CachedTransform.position).sqrMagnitude >= DistanceSquared(player);
    }

    private static Material MakeMaterial(Color color, Texture2D? texture, bool glow, Material? template)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Unlit");
        var material = texture == null && template != null ? new Material(template) : shader != null ? new Material(shader) : throw new InvalidOperationException("No slot cabinet shader.");
        if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", null);
        if (material.HasProperty("_MaskMap")) material.SetTexture("_MaskMap", null);
        var tint = texture == null ? color : Color.white;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
        if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", .35f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .24f);
        if (glow && material.HasProperty("_EmissionColor"))
        { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", texture != null ? Color.white * .8f : color * 1.5f); if (texture != null) material.SetTexture("_EmissionMap", texture); }
        else if (material.HasProperty("_EmissionColor")) { material.DisableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black); }
        materials.Add(material); return material;
    }
    private static Texture2D LoadTexture(string name)
    {
        using var stream = typeof(WorldSlots).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets." + name) ?? throw new InvalidOperationException("Missing slot texture " + name);
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, buffer.ToArray(), false)) throw new InvalidOperationException("Could not decode " + name);
        texture.wrapMode = TextureWrapMode.Repeat; texture.filterMode = FilterMode.Bilinear;
        textures.Add(texture); return texture;
    }
    private static void Build(Vector3 at, Quaternion rotation, Emporium emporium)
    {
        cabinet = new GameObject("GKMM Prism Jackpot Slot Machine");
        placedScene=(int)GlobalPredictor.CurrentScene;
        SceneManager.MoveGameObjectToScene(cabinet, emporium.gameObject.scene);
        cabinet.transform.position = at; cabinet.transform.rotation = rotation;
        presentation = new(cabinet, emporium);
        // Retain the game's shader/lighting style when a native opaque material
        // is available. Our original panel art replaces its texture maps.
        var template = presentation.Template;
        using var stream = typeof(WorldSlots).Assembly.GetManifestResourceStream("Gatekeeper.CustomMods.Assets.slot-machine.json")!;
        using var model = JsonDocument.Parse(stream);
        var palette = model.RootElement.GetProperty("materials");
        var textureMap = new Dictionary<string, Texture2D>();
        var materialMap = new Dictionary<string, Material>();
        var leverPivot = new GameObject("Animated lever"); leverPivot.transform.SetParent(presentation.Visual,false);
        leverPivot.transform.localPosition = new(1.09f,1.62f,.15f); lever = leverPivot.transform;
        static Vector3 V(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
        foreach (var part in model.RootElement.GetProperty("parts").EnumerateArray())
        {
            var name = part.GetProperty("name").GetString()!;
            var key = part.GetProperty("material").GetString()!;
            var color = V(palette.GetProperty(key));
            var textureName = part.TryGetProperty("texture", out var textureValue) ? textureValue.GetString() : null;
            Texture2D? texture = null;
            if (textureName != null && !textureMap.TryGetValue(textureName, out texture)) { texture = LoadTexture(textureName); textureMap.Add(textureName, texture); }
            var materialKey = name.StartsWith("Reel ") ? name : key + ":" + textureName;
            if (!materialMap.TryGetValue(materialKey, out var material))
            {
                material = MakeMaterial(new(color.x,color.y,color.z), texture, key is "lamp" or "mint" || textureName != null && textureName != "slot-metal.png", template);
                materialMap.Add(materialKey, material);
            }
            var primitive = part.TryGetProperty("primitive", out var type) ? Enum.Parse<PrimitiveType>(type.GetString()!) : PrimitiveType.Cube;
            var obj = GameObject.CreatePrimitive(primitive); obj.name = name; obj.transform.SetParent(presentation.Visual, false);
            obj.transform.localPosition = V(part.GetProperty("at"));
            var size = V(part.GetProperty("size")); if (primitive == PrimitiveType.Cylinder) size.y /= 2;
            obj.transform.localScale = size;
            if (part.TryGetProperty("rotation", out var angles)) { var e = V(angles); obj.transform.localRotation = Quaternion.Euler(e.x,e.y,e.z); }
            obj.GetComponent<Renderer>().sharedMaterial = material;
            if (textureName is "slot-reels.png" or "slot-marquee.png" or "slot-rules.png")
            {
                // The cube's built-in UV orientation differs by face. Map the
                // cabinet's front explicitly: +X reads right and +Y reads up.
                var mesh = obj.GetComponent<MeshFilter>().mesh;
                panelMeshes.Add(mesh);
                var vertices = mesh.vertices;
                var uv = new Vector2[vertices.Length];
                for (var i = 0; i < uv.Length; i++) uv[i] = new(vertices[i].x + .5f, vertices[i].y + .5f);
                mesh.uv = uv;
            }
            var collider = obj.GetComponent<Collider>(); if (collider != null) collider.enabled = false;
            if (name.StartsWith("Reel ")) { var index = int.Parse(name[5..]); reels[index] = material; material.mainTextureScale = new(1,.1f); }
            if (key == "lamp") lamps = material;
            if (name is "Lever rod" or "Lever knob") obj.transform.SetParent(lever,true);
        }
        var body = cabinet.AddComponent<BoxCollider>(); body.center = new(0,1.5f,.08f); body.size = new(2.08f,3f,1.5f);
        presentation.Bind(materials);
        Animate();
    }
    private static void Animate()
    {
        for (var i = 0; i < reels.Length; i++) if (reels[i] != null) reels[i]!.mainTextureOffset = new(0,(9 - SlotPlayer.Reel(i)) / 10f);
        var pulse = SlotPlayer.WinFlash ? .8f + .7f * Mathf.Sin(Time.unscaledTime * 16) : .45f;
        if (lamps != null && lamps.HasProperty("_EmissionColor")) lamps.SetColor("_EmissionColor", new Color(1,.68f,.22f) * pulse * 2);
        if (lever != null) lever.localRotation = Quaternion.Euler(SlotPlayer.IsAnimating ? Mathf.Sin(Mathf.Min(1, SlotPlayer.AnimationAge * 2) * Mathf.PI) * 28 : 0,0,0);
    }
    internal static void Clear()
    {
        if (cabinet != null) Object.Destroy(cabinet);
        cabinet = null; anchorObject = null; presentation = null;placedScene=-1;
        foreach (var material in materials) if (material != null) Object.Destroy(material);
        foreach (var texture in textures) if (texture != null) Object.Destroy(texture);
        foreach (var mesh in panelMeshes) if (mesh != null) Object.Destroy(mesh);
        materials.Clear(); textures.Clear(); panelMeshes.Clear(); Array.Clear(reels,0,reels.Length); lamps = null; lever = null;
        if (!Plugin.RestAreaScene || remoteScene!=(int)GlobalPredictor.CurrentScene) {remotePose = null;remoteScene=-1;}
    }
    internal static void Focus(bool selected) => presentation?.Focus(selected);
}
