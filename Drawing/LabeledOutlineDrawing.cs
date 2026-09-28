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

internal static class LabeledOutlineDrawing
{
    private const float LabelClearance = SlapPx.Space6;
    private const float Opacity = 1f;

    internal static float ResolveTopClearance() => MetricsScope.ScaleGap(SlapPx.Space6);

    internal static SlapOutlineCutout ComputeLabelCutout(
        Vector2 rectMin,
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
        return new SlapOutlineCutout(
            labelPos - new Vector2(clearance),
            labelPos + labelSize + new Vector2(clearance)
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
        float rounding
    )
    {
        if (string.IsNullOrWhiteSpace(outline.Label))
            return;

        var color = ImGui.ColorConvertFloat4ToU32(ResolveColor(outline));
        var cutout = ComputeLabelCutout(min, outline);
        SlapOutlineDrawing.DrawRoundedRectOutlineOutsideCutout(
            drawList,
            min,
            max,
            cutout.Min,
            cutout.Max,
            color,
            rounding,
            MetricsScope.BorderThickness
        );

        var offset = MetricsScope.ScalePadding(outline.ResolvedLabelOffset);
        using (Slap.PushFont(SlapFontSize.Small, SlapFontWeight.Bold))
        {
            var labelSize = ImGui.CalcTextSize(outline.Label);
            var labelPos = new Vector2(
                min.X + offset.X,
                min.Y - labelSize.Y * 0.5f + offset.Y
            );
            drawList.AddText(labelPos, color, outline.Label);
        }
    }

    internal static Vector4 ResolveColor(LabeledOutlineSpec outline)
    {
        var baseColor = ThemeScope.Resolved.GetTextSlot(outline.Semantic);
        return SlapColor.WithAlpha(baseColor, Opacity);
    }

}
