using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Drawing;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct LabeledOutlineSpec
{
    public string Label { get; }
    public Vector2? LabelOffset { get; }
    internal TextSlot Semantic { get; }

    public LabeledOutlineSpec(
        string label,
        TextSlot semantic,
        Vector2? labelOffset = null
    )
    {
        Label = label;
        LabelOffset = labelOffset;
        Semantic = semantic;
    }

    internal Vector2 ResolvedLabelOffset => LabelOffset ?? DefaultLabelOffset;
    private static readonly Vector2 DefaultLabelOffset = new(SlapPx.Space12, SlapPx.Space2);
}

/// <summary>
/// Resolved label geometry for a labeled outline: where the label is drawn,
/// which part of the border it cuts out, and how far its text may run.
/// </summary>
internal readonly record struct LabeledOutlineLayout(
    Vector2 LabelPos,
    Vector2 LabelSize,
    SlapOutlineCutout Cutout,
    float TextClipRight
)
{
    internal bool IsTextClipped => LabelSize.X > TextClipRight - LabelPos.X;
}

internal static class LabeledOutlineDrawing
{
    private const float LabelClearance = SlapPx.Space6;
    private const float Opacity = 1f;

    internal static float ResolveTopClearance() => MetricsScope.ScaleGap(SlapPx.Space6);

    internal static LabeledOutlineLayout ResolveLayout(
        Vector2 rectMin,
        Vector2 rectMax,
        LabeledOutlineSpec outline
    )
    {
        var offset = MetricsScope.ScalePadding(outline.ResolvedLabelOffset);
        var clearance = MetricsScope.ScalePadding(LabelClearance);

        Vector2 labelSize;
        using (Slap.PushFont(SlapFontSize.Small, SlapFontWeight.Bold))
            labelSize = ImGui.CalcTextSize(outline.Label);

        var labelPos = new Vector2(
            rectMin.X + offset.X,
            rectMin.Y - labelSize.Y * 0.5f + offset.Y
        );

        // The label starts one offset inside the left border and stops the same
        // distance before the right border, so the notch keeps the top-right
        // corner intact and both ends read alike. Only the text itself stops
        // there; the fade covers the cut.
        var textEnd = rectMax.X - offset.X;
        var cutoutLeft = labelPos.X - clearance;
        var cutoutRight = MathF.Max(
            cutoutLeft,
            MathF.Min(labelPos.X + labelSize.X + clearance, textEnd + clearance)
        );
        return new LabeledOutlineLayout(
            labelPos,
            labelSize,
            new SlapOutlineCutout(
                new Vector2(cutoutLeft, labelPos.Y - clearance),
                new Vector2(cutoutRight, labelPos.Y + labelSize.Y + clearance)
            ),
            MathF.Max(labelPos.X, MathF.Min(labelPos.X + labelSize.X, textEnd))
        );
    }

    internal static void DrawBorderWithCutout(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        SlapOutlineCutout? cutout,
        Vector4 borderColor,
        float rounding,
        float thickness
    )
    {
        var color = ImGui.ColorConvertFloat4ToU32(borderColor);
        if (cutout.HasValue)
        {
            SlapOutlineDrawing.DrawRoundedRectOutlineOutsideCutout(
                drawList,
                min,
                max,
                cutout.Value.Min,
                cutout.Value.Max,
                color,
                rounding,
                thickness
            );
            return;
        }

        drawList.AddRect(
            min,
            max,
            color,
            rounding,
            ImDrawFlags.RoundCornersAll,
            thickness
        );
    }

    internal static void DrawOverlay(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        LabeledOutlineSpec outline,
        float rounding,
        Vector4 background
    )
    {
        if (string.IsNullOrWhiteSpace(outline.Label))
            return;

        var layout = ResolveLayout(min, max, outline);
        var color = ImGui.ColorConvertFloat4ToU32(ResolveColor(outline));
        SlapOutlineDrawing.DrawRoundedRectOutlineOutsideCutout(
            drawList,
            min,
            max,
            layout.Cutout.Min,
            layout.Cutout.Max,
            color,
            rounding,
            MetricsScope.BorderThickness
        );

        var textWidth = layout.TextClipRight - layout.LabelPos.X;
        var clipped = layout.IsTextClipped;

        // An unclipped label is drawn as before; only an overflow needs its own
        // clip and the fade that covers the cut.
        if (clipped)
        {
            if (textWidth <= 0f)
                return;

            drawList.PushClipRect(
                layout.LabelPos,
                new Vector2(layout.TextClipRight, layout.LabelPos.Y + layout.LabelSize.Y),
                true
            );
        }

        try
        {
            using (Slap.PushFont(SlapFontSize.Small, SlapFontWeight.Bold))
                drawList.AddText(layout.LabelPos, color, outline.Label);
        }
        finally
        {
            if (clipped)
                drawList.PopClipRect();
        }

        if (clipped)
            DrawLabelFade(drawList, layout, min.Y, background);
    }

    /// <summary>
    /// Covers the clipped label tail. The label straddles the control's top
    /// edge, so the band above the edge fades into the panel surface and the
    /// band below it into the control background. Transparent backgrounds make
    /// the fade a no-op; the tightened clip edge then ends the text inside the
    /// border instead of past it.
    /// </summary>
    private static void DrawLabelFade(
        ImDrawListPtr drawList,
        LabeledOutlineLayout layout,
        float topEdge,
        Vector4 background
    )
    {
        var width = layout.TextClipRight - layout.LabelPos.X;
        var top = layout.LabelPos.Y;
        var bottom = layout.LabelPos.Y + layout.LabelSize.Y;
        var split = Math.Clamp(topEdge, top, bottom);

        EdgeFade.DrawRight(
            drawList,
            new Vector2(layout.LabelPos.X, top),
            width,
            split - top,
            ThemeScope.Resolved.Surface
        );
        EdgeFade.DrawRight(
            drawList,
            new Vector2(layout.LabelPos.X, split),
            width,
            bottom - split,
            background
        );
    }

    internal static Vector4 ResolveColor(LabeledOutlineSpec outline)
    {
        var baseColor = ThemeScope.Resolved.GetTextSlot(outline.Semantic);
        return SlapColor.WithAlpha(baseColor, Opacity);
    }

}
