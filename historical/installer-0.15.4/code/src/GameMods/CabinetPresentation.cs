using Gatekeeper.EnvironmentStuff;
using Gatekeeper.EnvironmentStuff.Emporium;
using Gatekeeper.StaticDataHolders;
using UnityEngine;

namespace Gatekeeper.CustomMods;

// Only the visual child wiggles. Placement, interaction range, co-op pose and
// the enlarged root collider stay stable throughout the selection animation.
internal sealed class CabinetPresentation
{
    internal const float Size = 1.21f;
    internal Transform Visual { get; }
    internal Material? Template { get; }
    private readonly InteractiveMaterialInfo? info;
    private readonly List<(Material material, int id, Color color)> colors = new();
    private readonly List<(Material material, int id, float value)> floats = new();
    private bool selected;
    private float entered = -100;

    internal CabinetPresentation(GameObject root, Emporium anchor)
    {
        root.transform.localScale = Vector3.one * Size;
        var model = new GameObject("Cabinet visual"); model.transform.SetParent(root.transform, false);
        Visual = model.transform;
        var controller = anchor.GetComponentInChildren<EmporiumMaterialController>();
        info = controller != null ? controller.matInfo : null;
        Template = info != null ? info.defaultMaterial : null;
        if (Template == null)
            Template = anchor.GetComponentsInChildren<Renderer>().Select(r => r.sharedMaterial)
                .FirstOrDefault(m => m != null && m.renderQueue < 2500 && m.HasProperty(MatParamsData.OutlineWidthParam));
    }

    internal void Bind(IEnumerable<Material> materials)
    {
        foreach (var material in materials)
        {
            foreach (var id in new[] { MatParamsData.BaseColorParam, MatParamsData.ColorParam, MatParamsData.OutlineColorParam, MatParamsData.OutlineColorVertexParam })
                if (material.HasProperty(id)) colors.Add((material, id, material.GetColor(id)));
            foreach (var id in new[] { MatParamsData.OutlineWidthParam, MatParamsData.AlbedoSatParam })
                if (material.HasProperty(id)) floats.Add((material, id, material.GetFloat(id)));
        }
    }
    internal void Focus(bool value)
    {
        if (selected == value) return;
        selected = value;
        if (value) entered = Time.unscaledTime;
        var yellow = info != null ? info.selectedOutlineColor : new Color(1, .8f, .15f, 1);
        foreach (var (material, id, original) in colors)
        {
            if (material == null) continue;
            var outline = id == MatParamsData.OutlineColorParam || id == MatParamsData.OutlineColorVertexParam;
            var tint = new Color(Mathf.Clamp01(yellow.r), Mathf.Clamp01(yellow.g), Mathf.Clamp01(yellow.b), original.a);
            material.SetColor(id, !value ? original : outline ? yellow : Color.Lerp(original, tint, .20f));
        }
        foreach (var (material, id, original) in floats)
        {
            if (material == null) continue;
            material.SetFloat(id, !value ? original : id == MatParamsData.OutlineWidthParam
                ? info != null ? info.selectedOutlineWidth : Math.Max(original, .018f)
                : info != null ? info.selectedAlbedoSat : original);
        }
    }
    internal void Tick()
    {
        if (Visual == null) return;
        var age = Time.unscaledTime - entered;
        var sway = age >= 0 && age < .7f ? Mathf.Sin(age * 31) * Mathf.Exp(-age * 6) * 2.5f : 0;
        Visual.localRotation = Quaternion.Euler(0, 0, sway);
    }
}
