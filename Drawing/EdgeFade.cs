using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal static class EdgeFade
{
    /// <summary>
    /// Draw right-edge fade using a structured spec.
    /// </summary>
    public static void DrawRight(
        ImDrawListPtr drawList,
        Vector2 origin,
        float width,
        float height,
        EdgeFadeSpec spec)
    {
        if (!spec.IsEnabled || width <= 0f || height <= 0f)
            return;

        var bg = spec.Background ?? ThemeScope.Resolved.Surface;
        DrawRight(drawList, origin, width, height, bg, spec.FadeWidth, spec.VerticalInset);
    }

    public static void DrawRight(
        ImDrawListPtr drawList,
        Vector2 origin,
        float width,
        float height,
        Vector4 background,
        float fadeWidth = 0f,
        float verticalBorderInset = 0f
    )
    {
        if (width <= 0f || height <= 0f)
            return;

        // A fade blends from its anchor colour into full transparency. If the
        // anchor already carries alpha, there is no opaque base to reveal the
        // boundary, so the result reads as a translucent band rather than an
        // edge fade. Skip in that case.
        if (background.W < 1f)
            return;

        verticalBorderInset = MathF.Max(0f, verticalBorderInset);
        var fadeHeight = height - verticalBorderInset * 2f;
        if (fadeHeight <= 0f)
            return;

        // fadeWidth <= 0 → auto: Scale(Space6) ≈ 6px
        var fadeW = fadeWidth > 0f
                        ? MathF.Min(MetricsScope.Scale(fadeWidth), width)
                        : MetricsScope.Scale(SlapPx.Space6);
        if (fadeW <= 0f)
            return;

        var min = new Vector2(origin.X + width - fadeW, origin.Y + verticalBorderInset);
        var max = new Vector2(origin.X + width, origin.Y + verticalBorderInset + fadeHeight);
        var clear = ImGui.ColorConvertFloat4ToU32(background with { W = 0f });
        var solid = ImGui.ColorConvertFloat4ToU32(background);
        drawList.AddRectFilledMultiColor(min, max, clear, solid, solid, clear);
    }
}
