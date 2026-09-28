using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct SurfaceHeaderSpec(
    ControlKey Key,
    string Title,
    string? Subtitle = null,
    FontAwesomeIcon? Icon = null,
    ControlState State = ControlState.None,
    string? Tooltip = null,
    float HeightUnits = 1.85f,
    float Width = 0f,
    float LineGap = 0f
);

internal static class SurfaceHeaderComponent
{
    public static void Draw(SurfaceHeaderSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("SurfaceHeaderSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        try
        {
            var width = spec.Width > 0f
                ? spec.Width
                : MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            var height = ResolveHeight(spec);
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(width, height);
            DrawInRect(ImGui.GetWindowDrawList(), min, max, spec, ThemeScope.Resolved.Surface);
            ImGui.Dummy(new Vector2(width, height));
            var headerTooltip = ResponsiveTooltip.Compose(
                width + 0.5f < ResolveNaturalWidth(spec),
                spec.Title,
                spec.Tooltip);
            HoverArbitration.Current.TryShowTooltip(
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled),
                headerTooltip);
        } finally
        {
            ImGui.PopID();
        }
    }

    internal static float ResolveHeight(SurfaceHeaderSpec spec) =>
        MetricsScope.UnitHeight * Normalize(spec.HeightUnits);

    /// <summary>
    /// Measure the natural content width of the header (icon slot + title text + padding).
    /// Used by <see cref="ContentZoneContext.HeaderRow"/> to give the title its
    /// natural width before degrading right-aligned controls.
    /// </summary>
    internal static float ResolveNaturalWidth(SurfaceHeaderSpec spec)
    {
        // Only left padding (Space10); no explicit right padding in DrawInRect.
        var padding = MetricsScope.ScalePadding(SlapPx.Space10);
        var iconSlotWidth = spec.Icon.HasValue
            ? MetricsScope.ResolveIconSlotWidth(false)
            : 0f;
        float titleWidth;
        using (TypographyScope.PushCurrent(SlapFontSize.Large, SlapFontWeight.Bold))
            titleWidth = ImGui.CalcTextSize(spec.Title ?? string.Empty).X;

        return padding + iconSlotWidth + titleWidth;
    }

    internal static void DrawInRect(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        SurfaceHeaderSpec spec,
        Vector4 background)
    {
        if (max.X <= min.X || max.Y <= min.Y)
            return;

        var disabled = spec.State.HasFlag(ControlState.Disabled);
        var titleColor = SlapColor.WithDisabledAlpha(ThemeScope.Resolved.Body, disabled);
        var subtitleColor = SlapColor.WithDisabledAlpha(
            ThemeScope.Resolved.Body, disabled);
        var height = max.Y - min.Y;

        var textMin = min + new Vector2(MetricsScope.ScalePadding(SlapPx.Space10), 0f);
        var textMax = max;
        var textSpec = new TwoLineTextSpec(
            spec.Title,
            spec.Subtitle,
            SlapFontSize.Large,
            SlapFontWeight.Bold,
            SlapFontSize.Small,
            SlapFontWeight.Regular,
            titleColor,
            subtitleColor,
            spec.LineGap,
            background
        );
        var textMetrics = TwoLineTextComponent.Measure(textSpec, height);
        var y = textMetrics.ResolveBlockY(min, max);

        var titleMax = new Vector2(textMax.X, y + textMetrics.Line1Height);
        var (headerIconText, headerIconFont) = SlapIcon.Resolve(spec.Icon);
        using (TypographyScope.PushCurrent(SlapFontSize.Large, SlapFontWeight.Bold))
        {
            // iconSlotWidth is auto-derived by DrawInRect (UnitHeight, left-aligned).
            // Gap = 0: title text starts flush against the icon slot end.
            IconTextComponent.DrawInRect(
                drawList,
                new Vector2(textMin.X, y),
                titleMax,
                spec.Title,
                headerIconText,
                headerIconFont,
                titleColor,
                background,
                iconTextGap: 0f);
        }

        if (textMetrics.HasLine2)
        {
            var subtitleY = textMetrics.ResolveLine2Y(y);
            using (TypographyScope.PushCurrent(SlapFontSize.Small, SlapFontWeight.Regular))
            {
                IconTextComponent.DrawInRect(
                    drawList,
                    new Vector2(textMin.X, subtitleY),
                    new Vector2(textMax.X, subtitleY + textMetrics.Line2Height),
                    spec.Subtitle ?? string.Empty,
                    iconText: null,
                    iconFont: null,
                    subtitleColor,
                    background);
            }
        }
    }

    private static float Normalize(float value) => value > 0f && !float.IsInfinity(value) ? value : 1.85f;
}
