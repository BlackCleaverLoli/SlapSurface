using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface.Drawing;

internal static class SlapOutlineDrawing
{
    private static readonly Vector4 White = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 Black = new(0f, 0f, 0f, 1f);

    public static void DrawGlow(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        Vector4 accentColor,
        float rounding,
        float thicknessScale = 1f,
        Vector2? cutoutMin = null,
        Vector2? cutoutMax = null,
        Vector2? clipMin = null,
        Vector2? clipMax = null,
        SlapOutlineShape shape = SlapOutlineShape.RoundedRect
    )
    {
        if (max.X <= min.X || max.Y <= min.Y)
            return;

        var opacity = Math.Clamp(accentColor.W, 0f, 1f);
        if (opacity <= 0f)
            return;

        var accent = accentColor with { W = 1f };
        var tm = MathF.Max(0.1f, thicknessScale);

        var effectiveClipMin = clipMin ?? Vector2.Zero;
        var effectiveClipMax = clipMax ?? ImGui.GetIO().DisplaySize;
        drawList.PushClipRect(effectiveClipMin, effectiveClipMax, false);
        try
        {
            if (ThemeScope.Resolved.IsLightSurface)
                DrawLightOutline(drawList, min, max, rounding, accent, opacity, tm, cutoutMin, cutoutMax, shape);
            else
                DrawDarkOutline(drawList, min, max, rounding, accent, opacity, tm, cutoutMin, cutoutMax, shape);
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    // ── Dark-surface path: 7-layer glow (aligned with V2 DrawButtonHoverOutline) ──

    private static void DrawDarkOutline(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding,
        Vector4 accent,
        float opacity,
        float tm,
        Vector2? cutoutMin,
        Vector2? cutoutMax,
        SlapOutlineShape shape)
    {
        // 7-layer glow, sRGB Lerp toward White/Black (matching V2 MixGlowColor).

        // Layer 1 — outer dark glow
        DrawExpandedOutlineLayer(
            drawList, min, max, rounding,
            MetricsScope.Scale(3.3f), MetricsScope.Scale(2.8f * tm),
            Vector4.Lerp(accent, Black, 0.18f),
            0.06f * opacity,
            cutoutMin, cutoutMax, shape);

        // Layer 2 — outer tint
        DrawExpandedOutlineLayer(
            drawList, min, max, rounding,
            MetricsScope.Scale(2.1f), MetricsScope.Scale(2.2f * tm),
            Vector4.Lerp(accent, White, 0.14f),
            0.14f * opacity,
            cutoutMin, cutoutMax, shape);

        // Layer 3 — tight glow
        DrawExpandedOutlineLayer(
            drawList, min, max, rounding,
            MetricsScope.Scale(0.75f), MetricsScope.Scale(1.8f * tm),
            Vector4.Lerp(accent, White, 0.24f),
            0.28f * opacity,
            cutoutMin, cutoutMax, shape);

        // Layer 4 — bright border (expanded, zero expansion)
        DrawExpandedOutlineLayer(
            drawList, min, max, rounding,
            0f, MetricsScope.Scale(1.4f * tm),
            Vector4.Lerp(accent, White, 0.38f),
            0.38f * opacity,
            cutoutMin, cutoutMax, shape);

        // Layer 5 — exact border
        DrawOutlineLayer(
            drawList, min, max,
            Vector4.Lerp(accent, White, 0.38f),
            0.58f * opacity,
            rounding, MetricsScope.Scale(1f * tm),
            cutoutMin, cutoutMax, shape);

        // Layer 6 — inner glow (inset 2px)
        var innerGlowInset = MetricsScope.Scale(2f);
        if (max.X - min.X > innerGlowInset * 2f && max.Y - min.Y > innerGlowInset * 2f)
        {
            DrawOutlineLayer(
                drawList,
                min + new Vector2(innerGlowInset),
                max - new Vector2(innerGlowInset),
                Vector4.Lerp(accent, White, 0.64f),
                0.18f * opacity,
                MathF.Max(0f, rounding - innerGlowInset),
                MetricsScope.Scale(1.2f * tm),
                cutoutMin, cutoutMax, shape);
        }

        // Layer 7 — core (inset 1px)
        var innerInset = MetricsScope.Scale(1f);
        if (max.X - min.X > innerInset * 2f && max.Y - min.Y > innerInset * 2f)
        {
            DrawOutlineLayer(
                drawList,
                min + new Vector2(innerInset),
                max - new Vector2(innerInset),
                Vector4.Lerp(accent, White, 0.72f),
                0.92f * opacity,
                MathF.Max(0f, rounding - innerInset),
                MetricsScope.Scale(1f * tm),
                cutoutMin, cutoutMax, shape);
        }
    }

    // ── Light-surface path: 2-layer narrow shadow (aligned with V2 DrawButtonHoverLiftShadow) ──

    private static void DrawLightOutline(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding,
        Vector4 accent,
        float opacity,
        float tm,
        Vector2? cutoutMin,
        Vector2? cutoutMax,
        SlapOutlineShape shape)
    {
        var shadowColor = Vector4.Lerp(accent, Black, 1f);

        // Layer 1 — expanded shadow
        DrawExpandedOutlineLayer(
            drawList, min, max, rounding,
            MetricsScope.Scale(0.25f), MetricsScope.Scale(1.5f * tm),
            shadowColor,
            0.25f * opacity,
            cutoutMin, cutoutMax, shape);

        // Layer 2 — exact edge
        DrawOutlineLayer(
            drawList, min, max,
            shadowColor,
            0.25f * opacity,
            rounding, MetricsScope.Scale(1f * tm),
            cutoutMin, cutoutMax, shape);
    }

    private static void DrawExpandedOutlineLayer(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding,
        float expansion,
        float thickness,
        Vector4 color,
        float alpha,
        Vector2? cutoutMin = null,
        Vector2? cutoutMax = null,
        SlapOutlineShape shape = SlapOutlineShape.RoundedRect
    )
    {
        var inset = expansion + thickness * 0.5f;
        DrawOutlineLayer(
            drawList,
            min - new Vector2(inset),
            max + new Vector2(inset),
            color,
            alpha,
            rounding + inset,
            thickness,
            cutoutMin,
            cutoutMax,
            shape
        );
    }

    private static void DrawOutlineLayer(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        Vector4 color,
        float alpha,
        float rounding,
        float thickness,
        Vector2? cutoutMin = null,
        Vector2? cutoutMax = null,
        SlapOutlineShape shape = SlapOutlineShape.RoundedRect
    )
    {
        if (alpha <= 0f || thickness <= 0f)
            return;

        var colorU32 = ImGui.ColorConvertFloat4ToU32(color with { W = Math.Clamp(alpha, 0f, 1f) });
        if (
            cutoutMin.HasValue
            && cutoutMax.HasValue
            && RectanglesOverlap(min, max, cutoutMin.Value, cutoutMax.Value)
        )
        {
            DrawRoundedRectOutlineOutsideCutout(
                drawList,
                min,
                max,
                cutoutMin.Value,
                cutoutMax.Value,
                colorU32,
                rounding,
                thickness
            );
            return;
        }

        if (shape == SlapOutlineShape.Circle)
        {
            var center = (min + max) * 0.5f;
            var radius = MathF.Max(0f, MathF.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
            drawList.AddCircle(center, radius, colorU32, thickness);
            return;
        }

        drawList.AddRect(min, max, colorU32, rounding, ImDrawFlags.RoundCornersAll, thickness);
    }

    /// <summary>
    /// Draws a rounded-rect outline while excluding a rectangular cutout, by
    /// splitting the border into four clip regions. Shared by the hover-glow
    /// outline and the labeled static border so their cutout geometry stays in sync.
    /// </summary>
    internal static void DrawRoundedRectOutlineOutsideCutout(
        ImDrawListPtr drawList,
        Vector2 rectMin,
        Vector2 rectMax,
        Vector2 cutoutMin,
        Vector2 cutoutMax,
        uint color,
        float rounding,
        float thickness
    )
    {
        var clipPadding = MathF.Max(thickness, 1f);
        var outerClipMin = rectMin - new Vector2(clipPadding);
        var outerClipMax = rectMax + new Vector2(clipPadding);
        var cutoutLeft = Math.Clamp(cutoutMin.X, outerClipMin.X, outerClipMax.X);
        var cutoutRight = Math.Clamp(cutoutMax.X, outerClipMin.X, outerClipMax.X);
        var cutoutTop = Math.Clamp(cutoutMin.Y, outerClipMin.Y, outerClipMax.Y);
        var cutoutBottom = Math.Clamp(cutoutMax.Y, outerClipMin.Y, outerClipMax.Y);

        DrawSegmentInClip(
            drawList,
            rectMin,
            rectMax,
            outerClipMin,
            new Vector2(outerClipMax.X, cutoutTop),
            color,
            rounding,
            thickness
        );
        DrawSegmentInClip(
            drawList,
            rectMin,
            rectMax,
            new Vector2(outerClipMin.X, cutoutBottom),
            outerClipMax,
            color,
            rounding,
            thickness
        );
        DrawSegmentInClip(
            drawList,
            rectMin,
            rectMax,
            new Vector2(outerClipMin.X, cutoutTop),
            new Vector2(cutoutLeft, cutoutBottom),
            color,
            rounding,
            thickness
        );
        DrawSegmentInClip(
            drawList,
            rectMin,
            rectMax,
            new Vector2(cutoutRight, cutoutTop),
            new Vector2(outerClipMax.X, cutoutBottom),
            color,
            rounding,
            thickness
        );
    }

    private static void DrawSegmentInClip(
        ImDrawListPtr drawList,
        Vector2 rectMin,
        Vector2 rectMax,
        Vector2 clipMin,
        Vector2 clipMax,
        uint color,
        float rounding,
        float thickness
    )
    {
        if (clipMax.X <= clipMin.X || clipMax.Y <= clipMin.Y)
            return;

        drawList.PushClipRect(clipMin, clipMax, true);
        drawList.AddRect(rectMin, rectMax, color, rounding, ImDrawFlags.RoundCornersAll, thickness);
        drawList.PopClipRect();
    }

    private static bool RectanglesOverlap(Vector2 minA, Vector2 maxA, Vector2 minB, Vector2 maxB) =>
        maxA.X > minB.X
        && minA.X < maxB.X
        && maxA.Y > minB.Y
        && minA.Y < maxB.Y;
}
