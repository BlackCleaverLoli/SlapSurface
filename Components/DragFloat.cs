using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct DragFloatSpec(
    ControlKey Key,
    float Min = 0f,
    float Max = 100f,
    float Speed = 1f,
    string Format = "%.0f",
    float WidthUnits = 0f,
    float ResolvedWidth = 0f,
    string? Tooltip = null,
    Variant Variant = Variant.Base,
    /// <summary>Interaction state; disabled controls render with disabled
    /// palette colors and cannot be dragged.</summary>
    ControlState State = ControlState.None,
    LayeredShadowSpec Shadow = default
);

internal readonly record struct DragFloatResult(
    float Value,
    bool Changed,
    bool Hovered,
    bool Active
);

internal static class DragFloatComponent
{
    private const float TextPaddingX = SlapPx.Space12;

    /// <summary>
    /// Measure the preferred and structural-minimum widths for responsive layout.
    /// <see cref="DragFloatSpec.WidthUnits"/> is the preferred baseline
    /// (defaulting to two units); the structural floor is a one-unit square.
    /// </summary>
    public static ResponsiveWidthRange MeasureResponsiveWidth(DragFloatSpec spec)
    {
        var preferredBaseline = MetricsScope.UnitWidth
            * (spec.WidthUnits > 0f ? spec.WidthUnits : 2f);
        return ResponsiveWidthRange.Create(
            0f,
            preferredBaseline,
            MetricsScope.UnitHeight);
    }

    public static DragFloatResult Draw(DragFloatSpec spec, float value)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("DragFloatSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        using var _hover = HoverArbitration.Push();
        using var _font = TypographyScope.PushCurrent(
            SlapFontSize.Regular,
            SlapFontWeight.Bold);
        try
        {
            var rt = ThemeScope.Resolved;
            var disabled = (spec.State & ControlState.Disabled) != 0;
            var frameHeight = MetricsScope.UnitHeight;
            var minWidth = MetricsScope.UnitHeight;
            var width =
                spec.ResolvedWidth > 0f ? SlapMeasure.ClampResolvedWidth(spec.ResolvedWidth)
                : spec.WidthUnits > 0f ? MetricsScope.UnitWidth * spec.WidthUnits
                : MathF.Max(minWidth, ImGui.GetContentRegionAvail().X);

            var min = ImGui.GetCursorScreenPos();
            var max = new Vector2(min.X + width, min.Y + frameHeight);
            var drawList = ImGui.GetWindowDrawList();
            var rounding = SlapCorners.ControlRadius;

            var palette = rt.InputPalette;
            var borderStrength = SurfaceComponent.ResolveBorderStrength(spec.Variant);
            var colors = SurfaceComponent.ResolveControlColors(
                spec.Variant == Variant.Action ? rt.ActionPalette : palette,
                spec.State,
                strength: borderStrength,
                variant: spec.Variant);

            var preHovered = ImGui.IsMouseHoveringRect(min, max)
                && ImGui.IsWindowHovered(
                    ImGuiHoveredFlags.RootAndChildWindows
                    | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            var background = !disabled && preHovered ? palette.Hovered : colors.Background;

            LayeredShadow.Draw(
                drawList,
                min,
                max,
                rounding,
                spec.Shadow with { ExpandHorizontalClip = true });

            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(background), rounding);

            var textPadX = MetricsScope.ScalePadding(TextPaddingX);
            var framePadY = MathF.Max(0f, (frameHeight - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.SetCursorScreenPos(min);

            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Text, SlapColor.WithDisabledAlpha(palette.Text, disabled));
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(textPadX, framePadY));

            ImGui.SetNextItemWidth(width);
            var v = value;
            if (disabled)
                ImGui.BeginDisabled();
            var changed = ImGui.DragFloat("##drag", ref v, spec.Speed, spec.Min, spec.Max, spec.Format);
            if (disabled)
                ImGui.EndDisabled();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(5);

            var hovered = HoverArbitration.Current.CaptureHover(ImGui.IsItemHovered());
            var active = ImGui.IsItemActive();

            SurfaceComponent.DrawControlBorder(
                drawList,
                min,
                max,
                spec.Variant,
                colors.Border,
                rounding);

            var outline = new SlapOutlineGroup();
            if (active)
                outline.Capture(
                    min,
                    max,
                    true,
                    selected: false,
                    accent: rt.ResolveOutlineAccent(spec.Variant),
                    rounding: rounding);
            outline.Flush(spec.Key, 1f);

            HoverArbitration.Current.TryShowTooltip(hovered, spec.Tooltip);

            ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y));
            return new DragFloatResult(v, changed, hovered, active);
        }
        finally
        {
            ImGui.PopID();
        }
    }
}
