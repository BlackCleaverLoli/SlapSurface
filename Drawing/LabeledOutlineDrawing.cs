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

    /// <summary>
    /// 换行图标组的带标签外描边。轮廓跟随"整宽行 + 最后一行左对齐图标"
    /// 的 L 形缺口：右下缺口由 <paramref name="lastRowTop"/> /
    /// <paramref name="lastRowRight"/> 描述。末行已排满或缺口过窄时退化为
    /// 普通圆角矩形，标签切口与 <see cref="DrawOverlay"/> 一致。
    /// </summary>
    internal static void DrawWrappedGroupOverlay(
        ImDrawListPtr drawList,
        Vector2 min,
        float maxRight,
        float lastRowTop,
        float lastRowRight,
        float bottom,
        LabeledOutlineSpec outline,
        float rounding)
    {
        if (string.IsNullOrWhiteSpace(outline.Label))
            return;

        var color = ImGui.ColorConvertFloat4ToU32(ResolveColor(outline));
        var thickness = MetricsScope.BorderThickness;
        var cutout = ComputeLabelCutout(min, outline);
        if (maxRight - lastRowRight <= 0.5f || lastRowTop - min.Y <= 0.5f)
        {
            DrawBorderWithCutout(
                drawList,
                min,
                new Vector2(maxRight, bottom),
                cutout,
                ResolveColor(outline),
                rounding,
                thickness);
        }
        else
        {
            DrawWrappedNotchedBorder(
                drawList,
                min,
                maxRight,
                lastRowTop,
                lastRowRight,
                bottom,
                cutout,
                color,
                rounding,
                thickness);
        }

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

    private static void DrawWrappedNotchedBorder(
        ImDrawListPtr drawList,
        Vector2 min,
        float maxRight,
        float lastRowTop,
        float lastRowRight,
        float bottom,
        SlapOutlineCutout? cutout,
        uint color,
        float rounding,
        float thickness)
    {
        var notchWidth = maxRight - lastRowRight;
        var radius = MathF.Min(
            rounding,
            MathF.Min(
                (bottom - min.Y) * 0.25f,
                (maxRight - min.X) * 0.25f));
        radius = MathF.Min(radius, (lastRowTop - min.Y) * 0.5f);
        radius = MathF.Min(radius, (bottom - lastRowTop) * 0.5f);
        radius = MathF.Min(radius, notchWidth * 0.5f);
        radius = MathF.Max(0f, radius);
        if (radius <= 0f)
        {
            drawList.AddRect(
                min,
                new Vector2(maxRight, bottom),
                color,
                0f,
                ImDrawFlags.RoundCornersAll,
                thickness);
            return;
        }

        // 顶点序列（顺时针）：TL → 顶边 → TR → 右缘 → L 形台阶 → BR → 底边 → BL → 左边。
        // 标签切口把顶边断成两段，整个轮廓拆成两条路径绘制。
        var cutoutLeft = Math.Clamp(
            cutout.HasValue ? cutout.Value.Min.X : min.X,
            min.X + radius,
            maxRight - radius);
        var cutoutRight = Math.Clamp(
            cutout.HasValue ? cutout.Value.Max.X : maxRight,
            min.X + radius,
            maxRight - radius);

        var tl = min;
        var tr = new Vector2(maxRight, min.Y);
        var jog = new Vector2(maxRight, lastRowTop);
        var notch = new Vector2(lastRowRight, lastRowTop);
        var br = new Vector2(lastRowRight, bottom);
        var bl = new Vector2(min.X, bottom);

        // 第一段：左上圆角 + 顶边到切口左端。
        drawList.PathClear();
        PathRoundedTurn(drawList, bl, tl, tr, radius);
        drawList.PathLineTo(new Vector2(MathF.Max(min.X + radius, cutoutLeft), min.Y));
        drawList.PathStroke(color, ImDrawFlags.None, thickness);

        // 第二段：切口右端 → 右上圆角 → 右缘 → 台阶 → 末行右缘 → 底边 → 左下圆角 → 左边。
        drawList.PathClear();
        drawList.PathLineTo(new Vector2(cutoutRight, min.Y));
        PathRoundedTurn(drawList, new Vector2(cutoutRight, min.Y), tr, jog, radius);
        PathRoundedTurn(drawList, tr, jog, notch, radius);
        PathRoundedTurn(drawList, jog, notch, br, radius);
        PathRoundedTurn(drawList, notch, br, bl, radius);
        PathRoundedTurn(drawList, br, bl, tl, radius);
        drawList.PathLineTo(new Vector2(min.X, min.Y + radius));
        drawList.PathStroke(color, ImDrawFlags.None, thickness);
    }

    /// <summary>
    /// 沿路径方向画一个半径为 <paramref name="radius"/> 的圆角转弯：
    /// 先连直线段到转弯的切入切点，再按短弧方向画 Arc。
    /// </summary>
    private static void PathRoundedTurn(
        ImDrawListPtr drawList,
        Vector2 prev,
        Vector2 corner,
        Vector2 next,
        float radius)
    {
        var eIn = Vector2.Normalize(corner - prev);
        var eOut = Vector2.Normalize(next - corner);
        var center = corner + radius * MathF.Sqrt(2f) * Vector2.Normalize(eOut - eIn);
        var entry = corner - eIn * radius;
        var exit = corner + eOut * radius;
        var a0 = MathF.Atan2(entry.Y - center.Y, entry.X - center.X);
        var delta = MathF.IEEERemainder(
            MathF.Atan2(exit.Y - center.Y, exit.X - center.X) - a0,
            MathF.PI * 2f);
        drawList.PathLineTo(entry);
        drawList.PathArcTo(center, radius, a0, a0 + delta, 0);
    }

    internal static Vector4 ResolveColor(LabeledOutlineSpec outline)
    {
        var baseColor = ThemeScope.Resolved.GetTextSlot(outline.Semantic);
        return SlapColor.WithAlpha(baseColor, Opacity);
    }

}
