using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Draws vertical scroll edge fades (top and/or bottom) inside a scrollable child region.
/// Call after content is drawn but before EndChild, using the child's screen-space bounds.
/// </summary>
internal static class ScrollFade
{
    /// <summary>
    /// Draw top and bottom edge fades for the current scrollable region.
    /// <paramref name="min"/> and <paramref name="max"/> are the screen-space bounds
    /// of the scrollable child window.
    /// </summary>
    public static void DrawVertical(
        Vector2 min,
        Vector2 max,
        Vector4? background = null,
        float fadeHeight = 0f,
        float horizontalInset = 0f,
        bool drawTop = true,
        bool drawBottom = true,
        ImDrawListPtr drawList = default)
    {
        if (max.X <= min.X || max.Y <= min.Y)
            return;

        var bg = background ?? ThemeScope.Resolved.Surface;
        if (bg.W < 1f)
            return;

        var fade = fadeHeight > 0f
                       ? fadeHeight
                       : MetricsScope.Scale(SlapPx.Space6);
        if (fade <= 0f)
            return;

        var scrollY = ImGui.GetScrollY();
        var scrollMaxY = ImGui.GetScrollMaxY();
        var epsilon = MetricsScope.Scale(1f);
        if (scrollMaxY <= epsilon)
            return;

        var inset = MathF.Max(0f, horizontalInset);
        var fadeMinX = min.X + inset;
        var fadeMaxX = max.X - inset;
        if (fadeMaxX <= fadeMinX)
            return;

        fade = MathF.Min(fade, (max.Y - min.Y) * 0.5f);
        var dl = drawList.IsNull ? ImGui.GetWindowDrawList() : drawList;
        var solid = ImGui.ColorConvertFloat4ToU32(bg);
        var clear = ImGui.ColorConvertFloat4ToU32(bg with { W = 0f });

        if (drawTop && scrollY > epsilon)
        {
            dl.AddRectFilledMultiColor(
                new Vector2(fadeMinX, min.Y),
                new Vector2(fadeMaxX, min.Y + fade),
                solid, solid, clear, clear);
        }

        if (drawBottom && scrollY < scrollMaxY - epsilon)
        {
            dl.AddRectFilledMultiColor(
                new Vector2(fadeMinX, max.Y - fade),
                new Vector2(fadeMaxX, max.Y),
                clear, clear, solid, solid);
        }
    }
}
