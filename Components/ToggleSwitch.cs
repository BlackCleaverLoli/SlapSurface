using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>Which side the label appears on.</summary>
internal enum LabelPosition
{
    Left,
    Right,
}

/// <summary>
/// Specification for a toggle switch. The track height is 0.66 unit and the
/// width is twice the height; the knob sits inset on the left when off and on
/// the right when on. The ON fill follows <see cref="Variant.Action"/>; the
/// track border uses the neutral theme border color.
/// </summary>
internal readonly record struct ToggleSwitchSpec(
    ControlKey Key,
    string Label,
    bool Checked,
    LabelPosition Position = LabelPosition.Left,
    ControlState State = ControlState.None,
    string? Tooltip = null,
    float ResolvedWidth = 0f,
    bool Interactable = true,
    LayeredShadowSpec Shadow = default,
    FontAwesomeIcon? Icon = null,
    SlapFontWeight LabelWeight = SlapFontWeight.Bold
);

/// <summary>Result of a toggle switch interaction.</summary>
internal readonly record struct ToggleSwitchResult(
    bool Checked,
    bool Changed,
    bool Hovered
);

internal static class ToggleSwitchComponent
{
    internal const float TrackHeightRatio = 0.66f;
    internal const float TrackWidthRatio = 2f;
    internal const float LabelGap = SlapPx.Space8;
    private const float IconTextGap = SlapPx.Space4;
    private const float KnobInsetPx = SlapPx.Space3;
    private const float OffTrackLightnessDelta = 0.10f;
    private const float OffTrackAlpha = 0.18f;
    private const float OnTrackContrastThreshold = 0.22f;
    private const float OnTrackContrastDelta = 0.12f;

    internal static float ResolveTrackWidth(float resolvedWidth) =>
        resolvedWidth > 0f
            ? resolvedWidth
            : MetricsScope.UnitHeight * TrackHeightRatio * TrackWidthRatio;

    /// <summary>
    /// Resolves the natural pixel width of a toggle switch with the given label
    /// and label position. The total width is the same regardless of position
    /// (label + gap + track).
    /// </summary>
    internal static float ResolveNaturalWidth(
        string? label,
        LabelPosition position,
        FontAwesomeIcon? icon = null,
        SlapFontWeight labelWeight = SlapFontWeight.Bold,
        string? tooltip = null
    )
    {
        var trackWidth = ResolveTrackWidth(0f);
        var displayLabel = ResponsiveTooltip.BuildDisplayLabel(label, tooltip);

        var contentWidth = ResolveLabelContentWidth(displayLabel, icon, labelWeight);
        if (contentWidth <= 0f)
            return trackWidth;

        var gap = ResolveLabelGap(displayLabel, icon);
        return contentWidth + gap + trackWidth;
    }

    private static float ResolveLabelContentWidth(
        string? label,
        FontAwesomeIcon? icon,
        SlapFontWeight labelWeight)
    {
        var (iconText, iconFont) = SlapIcon.Resolve(icon);
        var hasIcon = !string.IsNullOrEmpty(iconText);
        var hasLabel = !string.IsNullOrWhiteSpace(label);

        if (hasIcon && !hasLabel)
            return MeasureFaIconWidth(iconText!, iconFont);

        var contentWidth = 0f;
        if (hasIcon)
        {
            var leading = IconTextComponent.ResolveLeadingLayout(
                hasGameIcon: false,
                hasFaIcon: true,
                hasText: hasLabel,
                faLeftPadding: SlapPx.Space4,
                faTextGap: IconTextGap,
                iconSlotWidth: MetricsScope.UnitHeight,
                centered: false,
                applyFaLeftPadding: hasLabel);
            contentWidth = leading.TotalWidth;
        }

        if (hasLabel)
        {
            using (TypographyScope.PushCurrent(SlapFontSize.Regular, labelWeight))
                contentWidth += ImGui.CalcTextSize(label).X;
        }

        return contentWidth;
    }

    private static float ResolveLabelGap(string? label, FontAwesomeIcon? icon) =>
        MetricsScope.ScaleGap(
            icon.HasValue && string.IsNullOrWhiteSpace(label)
                ? IconTextGap
                : LabelGap);

    private static float MeasureFaIconWidth(string iconText, ImFontPtr? iconFont)
    {
        if (iconFont.HasValue)
            ImGui.PushFont(iconFont.Value);
        try
        {
            return ImGui.CalcTextSize(iconText).X;
        }
        finally
        {
            if (iconFont.HasValue)
                ImGui.PopFont();
        }
    }

    public static ToggleSwitchResult Draw(ToggleSwitchSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("ToggleSwitchSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        try
        {
            var rt = ThemeScope.Resolved;
            var disabled = spec.State.HasFlag(ControlState.Disabled);
            var unitH = MetricsScope.UnitHeight;
            var cursor = ImGui.GetCursorScreenPos();

            // Label measurement
            var (iconText, iconFont) = SlapIcon.Resolve(spec.Icon);
            var displayLabel = ResponsiveTooltip.BuildDisplayLabel(spec.Label, spec.Tooltip);
            var hasIcon = !string.IsNullOrEmpty(iconText);
            var hasLabel = !string.IsNullOrWhiteSpace(displayLabel);
            var iconOnly = hasIcon && !hasLabel;
            var labelContentWidth = ResolveLabelContentWidth(displayLabel, spec.Icon, spec.LabelWeight);

            var gap = ResolveLabelGap(displayLabel, spec.Icon);
            var trackWidth = ResolveTrackWidth(0f);
            var naturalTotalWidth = trackWidth + (labelContentWidth > 0f ? labelContentWidth + gap : 0f);
            var allocatedWidth = spec.ResolvedWidth > 0f
                ? spec.ResolvedWidth
                : naturalTotalWidth;
            var trackHeight = unitH * TrackHeightRatio;
            var knobInset = MetricsScope.ScalePadding(KnobInsetPx);
            var knobDiameter = MathF.Max(1f, trackHeight - knobInset * 2f);
            var knobRadius = knobDiameter * 0.5f;
            var trackY = cursor.Y + (unitH - trackHeight) * 0.5f;

            float trackMinX;
            float labelMinX;
            float labelMaxX;

            if (spec.Position == LabelPosition.Left)
            {
                trackMinX = cursor.X + allocatedWidth - trackWidth;
                labelMinX = cursor.X;
                labelMaxX = trackMinX - gap;
            }
            else
            {
                trackMinX = cursor.X;
                labelMinX = cursor.X + trackWidth + gap;
                labelMaxX = cursor.X + allocatedWidth;
            }

            var totalHeight = unitH;

            // Hit detection
            var rawClicked = false;
            if (spec.Interactable)
            {
                ImGui.SetCursorScreenPos(cursor);
                rawClicked = ImGui.InvisibleButton(
                    "##toggle",
                    new Vector2(allocatedWidth, totalHeight));
            }

            var ix = SlapInteraction.Capture(spec.State, rawClicked);

            var value = spec.Checked;
            if (ix.Clicked)
                value = !value;

            // Colors
            var actionPalette = rt.GetButtonPalette(Variant.Action);
            var trackBase = rt.IsTransparentTheme ? rt.Border : actionPalette.Base;
            var trackColorOn = SlapColor.WithDisabledAlpha(
                trackBase,
                disabled);

            Vector4 trackColorOff;
            if (rt.IsTransparentTheme)
            {
                trackColorOff = SlapColor.WithDisabledAlpha(
                    SlapColor.WithAlpha(rt.Body, OffTrackAlpha),
                    disabled);
            }
            else
            {
                var offTrack = SlapColor.PerceivedLightness(rt.Surface) - OffTrackLightnessDelta <= SlapColor.LightnessFloor
                    ? SlapColor.Lighten(rt.Surface, OffTrackLightnessDelta)
                    : SlapColor.Darken(rt.Surface, OffTrackLightnessDelta);
                trackColorOff = SlapColor.WithDisabledAlpha(offTrack, disabled);
            }

            var knobColor = SlapColor.WithDisabledAlpha(
                rt.IsTransparentTheme ? rt.Body : actionPalette.Text,
                disabled);
            var textColor = SlapColor.WithDisabledAlpha(rt.Body, disabled);

            trackBase = EnsureOnTrackContrast(trackBase, knobColor);
            trackColorOn = SlapColor.WithDisabledAlpha(trackBase, disabled);

            if (spec.Interactable && !ix.Disabled)
            {
                if (ix.Active)
                    trackColorOn = SlapColor.DeriveActiveColor(trackBase);
                else if (ix.Hovered)
                    trackColorOn = SlapColor.DeriveHoverColor(trackBase);
            }

            var drawList = ImGui.GetWindowDrawList();
            var trackMin = new Vector2(trackMinX, trackY);
            var trackMax = new Vector2(trackMinX + trackWidth, trackY + trackHeight);
            var trackRadius = SlapCorners.Enabled ? trackHeight * 0.5f : 0f;

            LayeredShadow.Draw(
                drawList,
                trackMin,
                trackMax,
                trackRadius,
                spec.Shadow with { ExpandHorizontalClip = true });

            // Track background
            drawList.AddRectFilled(trackMin, trackMax,
                ImGui.ColorConvertFloat4ToU32(value ? trackColorOn : trackColorOff), trackRadius);

            // Track border
            var trackBorder = SlapColor.WithDisabledAlpha(rt.Border, disabled);
            SurfaceComponent.DrawControlBorder(
                drawList,
                trackMin,
                trackMax,
                Variant.Base,
                trackBorder,
                trackRadius);

            // Knob: off = left + inset, on = right + inset; the knob uses the
            // background color so it reads as a cut-out of the track.
            var knobX = value
                ? trackMax.X - knobInset - knobDiameter
                : trackMin.X + knobInset;
            var knobY = trackY + (trackHeight - knobDiameter) * 0.5f;
            var knobCenter = new Vector2(knobX + knobRadius, knobY + knobRadius);

            if (SlapCorners.Enabled)
            {
                drawList.AddCircleFilled(knobCenter, knobRadius,
                    ImGui.ColorConvertFloat4ToU32(knobColor));
            }
            else
            {
                drawList.AddRectFilled(
                    new Vector2(knobX, knobY),
                    new Vector2(knobX + knobDiameter, knobY + knobDiameter),
                    ImGui.ColorConvertFloat4ToU32(knobColor));
            }

            // Label. The track keeps its natural width; the label clips and
            // fades against the surrounding surface when the allocated width
            // cannot fit the full text.
            var labelAvailableWidth = MathF.Max(0f, labelMaxX - labelMinX);
            var degraded = (hasLabel || hasIcon)
                && labelContentWidth > labelAvailableWidth;
            if (hasLabel || hasIcon)
            {
                var labelRectMin = new Vector2(labelMinX, cursor.Y);
                var labelRectMax = new Vector2(
                    MathF.Max(labelMinX, labelMaxX),
                    cursor.Y + totalHeight);
                using (TypographyScope.PushCurrent(SlapFontSize.Regular, spec.LabelWeight))
                {
                    degraded |= IconTextComponent.DrawInRect(
                        drawList,
                        labelRectMin,
                        labelRectMax,
                        displayLabel ?? string.Empty,
                        iconText,
                        iconFont,
                        textColor,
                        rightFadeBackground: rt.Surface,
                        rightFadeVerticalBorderInset: MetricsScope.BorderThickness,
                        iconTextGap: IconTextGap,
                        horizontalPadding: 0f,
                        leftPadding: (hasIcon && hasLabel) ? SlapPx.Space4 : null,
                        centerContent: false,
                        iconSlotWidth: iconOnly ? labelContentWidth : 0f);
                }
            }

            // Tooltip
            HoverArbitration.Current.TryShowTooltip(
                ix.Hovered,
                ResponsiveTooltip.Compose(degraded, displayLabel, spec.Tooltip));

            return new ToggleSwitchResult(value, value != spec.Checked, ix.Hovered);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    public static ResponsiveWidthRange MeasureResponsiveWidth(ToggleSwitchSpec spec)
    {
        var preferred = ResolveNaturalWidth(
            spec.Label,
            spec.Position,
            spec.Icon,
            spec.LabelWeight,
            spec.Tooltip);
        var minimum = ResolveTrackWidth(0f);
        return new ResponsiveWidthRange(preferred, minimum);
    }

    private static Vector4 EnsureOnTrackContrast(Vector4 track, Vector4 knob)
    {
        var trackLightness = SlapColor.PerceivedLightness(track);
        var knobLightness = SlapColor.PerceivedLightness(knob);
        var delta = MathF.Abs(trackLightness - knobLightness);
        if (delta >= OnTrackContrastThreshold)
            return track;

        var knobIsLight = knobLightness >= SlapColor.LightSurfaceThreshold;
        var amount = MathF.Max(OnTrackContrastDelta, OnTrackContrastThreshold - delta);
        return knobIsLight
            ? SlapColor.Darken(track, amount)
            : SlapColor.Lighten(track, amount);
    }
}
