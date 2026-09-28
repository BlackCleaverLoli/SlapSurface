using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Specification for a clearable dropdown filter: a flat split button whose
/// left segment is a text-only dropdown trigger showing the current option
/// label and whose right segment is a clear action. Clicking the trigger opens
/// the option popup; clicking the clear action reports
/// <see cref="ClearableDropdownResult.Cleared"/>.
/// </summary>
internal readonly record struct ClearableDropdownSpec(
    ControlKey Key,
    IReadOnlyList<DropdownOptionSpec> Options,
    int SelectedIndex = 0,
    string? Tooltip = null,
    string? ClearTooltip = "",
    Variant Variant = Variant.FlatNoBorder,
    ControlState State = ControlState.None,
    ButtonSize Size = default,
    /// <summary>
    /// Resolved trigger width in scaled pixels, set by an external layout system
    /// (e.g. <see cref="ResponsiveSlotGroup"/>). When &gt; 0, bypasses
    /// <see cref="Size"/> and may shrink toward the control's structural minimum.
    /// </summary>
    float ResolvedWidth = 0f,
    /// <summary>
    /// Optional option source invoked only while the popup is opening or open.
    /// Use this when enumerating the full option list is expensive; the static
    /// <see cref="Options"/> still supplies the closed trigger preview.
    /// </summary>
    Func<DropdownOptionsSnapshot>? OpenOptionsProvider = null,
    LayeredShadowSpec Shadow = default
);

/// <summary>Result of a collapsible dropdown filter frame.</summary>
internal readonly record struct ClearableDropdownResult(
    int SelectedIndex,
    bool Changed,
    bool Cleared
);

internal static class ClearableDropdownComponent
{
    private const float TextPaddingX = SlapPx.Space16;
    private const string PopupId = "##collapsibleDropdownPopup";

    internal static float ResolveMinWidth(ClearableDropdownSpec spec)
    {
        var h = MetricsScope.UnitHeight;
        return h * 2f;
    }

    internal static float ResolveNaturalWidth(ClearableDropdownSpec spec)
    {
        var label = ResolveLabel(spec);
        float labelWidth;
        using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold))
            labelWidth = ImGui.CalcTextSize(label).X;

        return labelWidth
            + MetricsScope.ScalePadding(TextPaddingX * 2f)
            + MetricsScope.UnitHeight;
    }

    internal static ResponsiveWidthRange MeasureResponsiveWidth(
        ClearableDropdownSpec spec)
    {
        var preferredBaseline = spec.Size.Kind == ButtonSizeKind.Rect
            ? MetricsScope.UnitWidth * spec.Size.WidthUnits
            : 0f;
        return ResponsiveWidthRange.Create(
            ResolveNaturalWidth(spec),
            preferredBaseline,
            ResolveMinWidth(spec));
    }

    public static ClearableDropdownResult Draw(ClearableDropdownSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "ClearableDropdownSpec.Key.Value must not be empty.",
                nameof(spec));
        if (spec.Options.Count == 0)
            return new ClearableDropdownResult(-1, false, false);

        ImGui.PushID(spec.Key.Value);
        using var _hover = HoverArbitration.Push();
        try
        {
            var h = MetricsScope.UnitHeight;
            var size = spec.ResolvedWidth > 0f
                ? new Vector2(MathF.Max(ResolveMinWidth(spec), spec.ResolvedWidth), h)
                : ResolveSize(spec);

            var clearWidth = h;
            var leftWidth = MathF.Max(0f, size.X - clearWidth);
            if (leftWidth <= 0f || size.Y <= 0f)
                return new ClearableDropdownResult(-1, false, false);

            var start = ImGui.GetCursorScreenPos();
            var leftMin = start;
            var leftMax = leftMin + new Vector2(leftWidth, size.Y);
            var clearMin = new Vector2(leftMax.X, leftMin.Y);
            var clearMax = clearMin + new Vector2(clearWidth, size.Y);
            var fullMin = leftMin;
            var fullMax = clearMax;

            var disabled = spec.State.HasFlag(ControlState.Disabled);
            var selectedIndex = Math.Clamp(spec.SelectedIndex, 0, spec.Options.Count - 1);
            var selectedOption = spec.Options[selectedIndex];

            ImGui.SetCursorScreenPos(leftMin);
            var leftRawClicked = ImGui.InvisibleButton("##trigger", leftMax - leftMin);
            var leftIx = SlapInteraction.Capture(spec.State, leftRawClicked);
            var popupOpen = ImGui.IsPopupOpen(PopupId);
            if (leftIx.Clicked)
                ImGui.OpenPopup(PopupId);

            ImGui.SetCursorScreenPos(clearMin);
            ImGui.InvisibleButton("##clear", clearMax - clearMin);
            var clearHovered = HoverArbitration.Current.CaptureHover(ImGui.IsItemHovered());
            var clearClicked = !disabled
                && clearHovered
                && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

            var rt = ThemeScope.Resolved;
            var rounding = SlapCorners.ControlRadius;
            var drawList = ImGui.GetWindowDrawList();
            var palette = rt.GetButtonPalette(spec.Variant);
            var borderStrength = SurfaceComponent.ResolveBorderStrength(spec.Variant);
            var leftColors = SurfaceComponent.ResolveControlColors(
                palette,
                spec.State,
                null,
                leftIx.Hovered,
                leftIx.Active || popupOpen,
                borderStrength,
                spec.Variant);
            var clearColors = SurfaceComponent.ResolveControlColors(
                palette,
                spec.State,
                null,
                clearHovered,
                clearHovered && ImGui.IsItemActive(),
                borderStrength,
                spec.Variant);

            LayeredShadow.Draw(
                drawList,
                fullMin,
                fullMax,
                rounding,
                LayeredShadow.ResolveControlDefault(spec.Shadow, spec.Variant) with { ExpandHorizontalClip = true });

            drawList.AddRectFilled(
                leftMin,
                leftMax,
                ImGui.ColorConvertFloat4ToU32(leftColors.Background),
                rounding,
                ImDrawFlags.RoundCornersLeft);
            drawList.AddRectFilled(
                clearMin,
                clearMax,
                ImGui.ColorConvertFloat4ToU32(clearColors.Background),
                rounding,
                ImDrawFlags.RoundCornersRight);

            var borderColor = ImGui.ColorConvertFloat4ToU32(leftColors.Border);
            SurfaceComponent.DrawControlBorder(
                drawList,
                fullMin,
                fullMax,
                spec.Variant,
                leftColors.Border,
                rounding);

            if (spec.Variant != Variant.FlatNoBorder)
            {
                drawList.AddLine(
                    new Vector2(
                        clearMin.X,
                        clearMin.Y + MetricsScope.ScaleBorder(SlapPx.Space1)),
                    new Vector2(
                        clearMin.X,
                        clearMax.Y - MetricsScope.ScaleBorder(SlapPx.Space1)),
                    borderColor,
                    MetricsScope.BorderThickness);
            }

            var outline = new SlapOutlineGroup();
            var buttonHovered = leftIx.Hovered || clearHovered;
            if (buttonHovered)
                outline.Capture(
                    fullMin,
                    fullMax,
                    buttonHovered,
                    selected: false,
                    accent: rt.ResolveOutlineAccent(spec.Variant),
                    rounding: rounding);
            outline.Flush(
                spec.Key,
                1f);

            var triggerDegraded = DrawTrigger(
                selectedOption.Label,
                leftMin,
                leftMax,
                leftColors.Text,
                leftColors.Background);
            DrawClear(drawList, clearMin, clearMax, clearColors.Text);

            var triggerTooltip = ResponsiveTooltip.Compose(
                triggerDegraded,
                selectedOption.Label,
                spec.Tooltip ?? selectedOption.Tooltip);
            HoverArbitration.Current.TryShowTooltip(leftIx.Hovered, triggerTooltip, leftMin, leftMax);
            HoverArbitration.Current.TryShowTooltip(clearHovered, spec.ClearTooltip, clearMin, clearMax);

            var popupOptions = spec.Options;
            var popupSelectedIndex = selectedIndex;
            if (spec.OpenOptionsProvider != null && (popupOpen || leftIx.Clicked))
            {
                var snapshot = spec.OpenOptionsProvider();
                if (snapshot.Options.Count > 0)
                {
                    popupOptions = snapshot.Options;
                    popupSelectedIndex = Math.Clamp(
                        snapshot.SelectedIndex,
                        0,
                        popupOptions.Count - 1);
                }
            }

            var nextSelected = popupSelectedIndex;
            DropdownPopupHelper.Draw(
                PopupId,
                popupOptions,
	                popupSelectedIndex,
	                fullMin,
	                fullMax,
	                spec.Variant,
	                ref nextSelected);

            return new ClearableDropdownResult(
                nextSelected,
                nextSelected != popupSelectedIndex,
                clearClicked);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static string ResolveLabel(ClearableDropdownSpec spec)
    {
        if (spec.Options.Count == 0)
            return string.Empty;
        return spec.Options[Math.Clamp(spec.SelectedIndex, 0, spec.Options.Count - 1)].Label
            ?? string.Empty;
    }

    private static Vector2 ResolveSize(ClearableDropdownSpec spec)
    {
        if (spec.Size.Kind == ButtonSizeKind.Auto)
            return new Vector2(ResolveNaturalWidth(spec), MetricsScope.UnitHeight);

        var w = MetricsScope.UnitWidth;
        var h = MetricsScope.UnitHeight;
        return spec.Size.Kind switch
        {
            ButtonSizeKind.Rect => new Vector2(w * spec.Size.WidthUnits, h * spec.Size.HeightUnits),
            _ => new Vector2(h * spec.Size.WidthUnits, h * spec.Size.HeightUnits),
        };
    }

    private static bool DrawTrigger(
        string label,
        Vector2 min,
        Vector2 max,
        Vector4 textColor,
        Vector4 background)
    {
        var drawList = ImGui.GetWindowDrawList();
        bool degraded;
        using (Slap.PushFont(SlapFontSize.Regular, SlapFontWeight.Bold))
        {
            degraded = IconTextComponent.DrawInRect(
                drawList,
                min,
                max,
                label,
                null,
                null,
                textColor,
                rightFadeBackground: background,
                rightFadeVerticalBorderInset: MetricsScope.BorderThickness,
                horizontalPadding: TextPaddingX,
                centerContent: false);
        }
        return degraded;
    }

    private static void DrawClear(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        Vector4 color)
    {
        var (clearText, clearFont) = SlapIcon.Resolve(FontAwesomeIcon.Times);
        var iconText = clearText ?? "x";
        var colorU32 = ImGui.ColorConvertFloat4ToU32(color);

        if (clearFont.HasValue)
            ImGui.PushFont(clearFont.Value);
        try
        {
            var iconSize = ImGui.CalcTextSize(iconText);
            var pos = min + (max - min - iconSize) * 0.5f;
            drawList.AddText(pos, colorU32, iconText);
        }
        finally
        {
            if (clearFont.HasValue)
                ImGui.PopFont();
        }
    }

}
