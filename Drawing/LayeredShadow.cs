using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;

namespace SlapSurface;

internal readonly record struct LayeredShadowSpec(
    int Layers,
    float Spread,
    float MaxAlpha,
    Vector2 Offset = default,
    ImDrawFlags CornerFlags = ImDrawFlags.RoundCornersAll,
    bool PushFullscreenClip = false,
    bool ExpandHorizontalClip = false
)
{
    /// <summary>无阴影。<c>default</c> 与此值等价。</summary>
    public static readonly LayeredShadowSpec None = default;

    /// <summary>标准阴影（8 层、15px 扩散）。需要阴影效果时使用此值，勿与 <c>default</c>/<see cref="None"/> 混淆。</summary>
    public static readonly LayeredShadowSpec Standard = new(
        Layers: 8,
        Spread: 15f,
        MaxAlpha: 0.125f,
        Offset: Vector2.Zero,
        CornerFlags: ImDrawFlags.RoundCornersAll,
        PushFullscreenClip: false
    );

    /// <summary>
    /// Resolves a spec whose default parameter used to be
    /// <see cref="Standard"/> before <c>default</c> became the only
    /// compile-time constant allowed for a record-struct default value.
    /// </summary>
    public static LayeredShadowSpec ResolveStandard(LayeredShadowSpec spec) =>
        spec == default ? Standard : spec;
}

internal static class LayeredShadow
{
    public static LayeredShadowSpec ResolveControlDefault(LayeredShadowSpec spec, Variant variant) =>
        spec == default
            ? (variant is Variant.Flat or Variant.FlatNoBorder ? LayeredShadowSpec.None : LayeredShadowSpec.Standard)
            : spec;

    public static void Draw(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding,
        LayeredShadowSpec spec
    )
    {
        if (max.X <= min.X || max.Y <= min.Y || spec.Layers <= 0 || spec.Spread <= 0f || spec.MaxAlpha <= 0f)
            return;

        var spread = MetricsScope.Scale(spec.Spread);
        var offset = MetricsScope.Scale(spec.Offset);
        if (spread <= 0f)
            return;

        var pushedClip = false;
        if (spec.PushFullscreenClip)
        {
            var displaySize = ImGui.GetIO().DisplaySize;
            drawList.PushClipRect(Vector2.Zero, displaySize, false);
            pushedClip = true;
        }
        else if (spec.ExpandHorizontalClip)
        {
            var displaySize = ImGui.GetIO().DisplaySize;
            var windowPos = ImGui.GetWindowPos();
            var windowMax = windowPos + ImGui.GetWindowSize();
            drawList.PushClipRect(
                new Vector2(0f, windowPos.Y),
                new Vector2(displaySize.X, windowMax.Y),
                false);
            pushedClip = true;
        }

        try
        {
            DrawUnchecked(drawList, min + offset, max + offset, rounding, spec.Layers, spread, spec.MaxAlpha, spec.CornerFlags);
        }
        finally
        {
            if (pushedClip)
                drawList.PopClipRect();
        }
    }

    private static void DrawUnchecked(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding,
        int layers,
        float spread,
        float maxAlpha,
        ImDrawFlags cornerFlags
    )
    {
        var step = spread / layers;
        for (var i = layers - 1; i >= 0; i--)
        {
            var t = (i + 1) / (float)layers;
            var expansion = i * step;
            var thickness = step;
            var fade = 1f - t;
            var alpha = maxAlpha * fade * fade * fade;
            if (alpha <= 0f)
                continue;

            var rectInset = expansion + thickness * 0.5f;
            var color = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, alpha));
            drawList.AddRect(
                min - new Vector2(rectInset),
                max + new Vector2(rectInset),
                color,
                rounding + rectInset,
                cornerFlags,
                thickness
            );
        }
    }
}
