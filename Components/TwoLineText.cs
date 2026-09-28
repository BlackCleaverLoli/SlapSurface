using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct TwoLineTextSpec(
    string Line1,
    string? Line2,
    SlapFontSize Line1Font,
    SlapFontWeight Line1Weight,
    SlapFontSize Line2Font,
    SlapFontWeight Line2Weight,
    Vector4? Line1Color = null,
    Vector4? Line2Color = null,
    float LineGap = 0f,
    Vector4? RightFadeBackground = null
);

internal readonly record struct TwoLineTextMetrics(
    float Width,
    float Height,
    float Line1Width,
    float Line1Height,
    float Line2Width,
    float Line2Height,
    float LineGap,
    bool HasLine1,
    bool HasLine2
)
{
    internal bool IsLine1Clipped(float availableWidth) =>
        HasLine1 && Line1Width > availableWidth;

    internal float ResolveBlockY(Vector2 min, Vector2 max) =>
        min.Y + SlapLayout.CenterOffset(MathF.Max(0f, max.Y - min.Y), Height);

    internal float ResolveLine2Y(float blockY) =>
        blockY + (HasLine1 ? Line1Height + LineGap : 0f);
}

internal static class TwoLineTextComponent
{
    internal static TwoLineTextMetrics Measure(
        TwoLineTextSpec spec,
        float availableHeight = float.PositiveInfinity
    )
    {
        var hasLine1 = !string.IsNullOrWhiteSpace(spec.Line1);
        var hasLine2 = !string.IsNullOrWhiteSpace(spec.Line2);
        var line1Size = hasLine1
            ? MeasureLine(spec.Line1, spec.Line1Font, spec.Line1Weight)
            : Vector2.Zero;
        var line2Size = hasLine2
            ? MeasureLine(spec.Line2!, spec.Line2Font, spec.Line2Weight)
            : Vector2.Zero;
        var preferredGap = hasLine1 && hasLine2
            ? ResolveLineGap(spec.LineGap)
            : 0f;
        var lineGap = float.IsPositiveInfinity(availableHeight)
            ? preferredGap
            : MathF.Min(
                preferredGap,
                MathF.Max(0f, availableHeight - line1Size.Y - line2Size.Y)
            );

        return new TwoLineTextMetrics(
            MathF.Max(line1Size.X, line2Size.X),
            line1Size.Y + lineGap + line2Size.Y,
            line1Size.X,
            line1Size.Y,
            line2Size.X,
            line2Size.Y,
            lineGap,
            hasLine1,
            hasLine2
        );
    }

    internal static TwoLineTextMetrics DrawInRect(
        ImDrawListPtr drawList,
        TwoLineTextSpec spec,
        Vector2 min,
        Vector2 max
    )
    {
        var availableSize = Vector2.Max(Vector2.Zero, max - min);
        var metrics = Measure(spec, availableSize.Y);
        if (availableSize.X <= 0f || availableSize.Y <= 0f || metrics.Height <= 0f)
            return metrics;

        var theme = ThemeScope.Resolved;
        var line1Color = ImGui.ColorConvertFloat4ToU32(spec.Line1Color ?? theme.Body);
        var line2Color = ImGui.ColorConvertFloat4ToU32(spec.Line2Color ?? theme.Subtle);
        var blockY = metrics.ResolveBlockY(min, max);

        drawList.PushClipRect(min, max, true);
        try
        {
            if (metrics.HasLine1)
            {
                using var line1Font = Slap.PushFont(spec.Line1Font, spec.Line1Weight);
                drawList.AddText(new Vector2(min.X, blockY), line1Color, spec.Line1);
            }

            if (metrics.HasLine2)
            {
                using var line2Font = Slap.PushFont(spec.Line2Font, spec.Line2Weight);
                drawList.AddText(
                    new Vector2(min.X, metrics.ResolveLine2Y(blockY)),
                    line2Color,
                    spec.Line2!
                );
            }
        }
        finally
        {
            drawList.PopClipRect();
        }

        if (
            spec.RightFadeBackground.HasValue
            && (metrics.Line1Width > availableSize.X || metrics.Line2Width > availableSize.X)
        )
        {
            EdgeFade.DrawRight(
                drawList,
                min,
                availableSize.X,
                availableSize.Y,
                spec.RightFadeBackground.Value
            );
        }

        return metrics;
    }

    private static Vector2 MeasureLine(
        string text,
        SlapFontSize fontSize,
        SlapFontWeight fontWeight
    )
    {
        using var font = Slap.PushFont(fontSize, fontWeight);
        return ImGui.CalcTextSize(text);
    }

    private static float ResolveLineGap(float lineGap)
    {
        if (lineGap <= 0f || float.IsInfinity(lineGap))
            return 0f;
        return MetricsScope.ScaleGap(lineGap);
    }
}
