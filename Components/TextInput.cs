using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct TextInputSpec(
    ControlKey Key,
    string Placeholder = "",
    string? Tooltip = null,
    uint? IconId = null,
    bool HighQualityIcon = false,
    float WidthUnits = 0f,
    EdgeFadeSpec? EdgeFade = null,
    Variant Variant = Variant.Base,
    bool ReportEveryChange = false,
    bool AutoSelectAll = false,
    FontAwesomeIcon? Icon = null,
    /// <summary>
    /// Resolved input width in scaled pixels, set by an external layout
    /// system (e.g. <see cref="ResponsiveSlotGroup"/>). When &gt; 0, takes
    /// precedence over <see cref="WidthUnits"/>. The explicit unit width is
    /// still used as the responsive preferred baseline and may be crossed
    /// while shrinking toward the input's structural minimum.
    /// </summary>
    float ResolvedWidth = 0f,
    /// <summary>Optional labeled cutout outline around the input frame.</summary>
    LabeledOutlineSpec? Outline = null,
    /// <summary>Show a trailing clear action while the input has text. On by default.</summary>
    bool Clearable = true,
    string? ClearTooltip = "",
    /// <summary>
    /// When false, the frame border renders in the danger semantic color to
    /// signal invalid input. Defaults to true.
    /// </summary>
    bool IsValid = true,
    ControlState State = ControlState.None,
    LayeredShadowSpec Shadow = default,
    bool ShowClearWhenEmpty = false,
    /// <summary>
    /// When true, clicking the clear action re-focuses the input so the user can
    /// continue typing. Set false when clearing dismisses the input state.
    /// </summary>
    bool RefocusAfterClear = true,
    /// <summary>When true, the input is read-only and only allows selection/copy.</summary>
    bool ReadOnly = false
);

internal readonly record struct TextInputResult(
    string Value,
    bool Changed,
    bool Hovered,
    bool Active,
    bool Focused,
    bool Activated,
    bool Cleared,
    bool Entered,
    bool Deactivated,
    bool Canceled,
    Vector2 Min,
    Vector2 Max
)
{
    public Vector2 Size => Max - Min;
}

internal static class TextInputComponent
{
    // Padding and gap constants:
    // - TextPaddingX = Space16: horizontal padding from frame edge to content.
    //   The right side gets Space16. Without an icon both sides get Space16.
    // - IconLeftPad = Space4: left padding for an FA icon slot.
    // - IconGap = 0: GameIcon splits the FA Space4 alignment spacing around its slot.
    private const float IconGap = 0f;
    private const float TextPaddingX = SlapPx.Space16;
    private const float IconLeftPad = SlapPx.Space4;

    private static float ResolveIconSlotWidth() => MetricsScope.ResolveIconSlotWidth(false);

    public static TextInputResult Draw(TextInputSpec spec, string value, int maxLength = 256)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("TextInputSpec.Key.Value must not be empty.", nameof(spec));

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
            var minWidth = MetricsScope.UnitWidth * 2f;
            var responsiveMinimum = MeasureResponsiveWidth(spec).MinimumWidth;
            var width =
                spec.ResolvedWidth > 0f ? MathF.Max(responsiveMinimum, spec.ResolvedWidth)
                : spec.WidthUnits > 0f ? MetricsScope.UnitWidth * spec.WidthUnits
                : MathF.Max(minWidth, ImGui.GetContentRegionAvail().X);

            var (faIconText, faIconFont) = SlapIcon.Resolve(spec.Icon);
            var gameIconId = spec.IconId.GetValueOrDefault();
            var hasGameIcon = spec.IconId.HasValue;
            var hasFaIcon = !string.IsNullOrEmpty(faIconText);
            var hasIcon = hasGameIcon || hasFaIcon;
            var iconSlotWidth = hasIcon ? ResolveIconSlotWidth() : 0f;
            var textPadX = MetricsScope.ScalePadding(TextPaddingX);

            if (
                spec.Outline is { } labeledOutline
                && !string.IsNullOrWhiteSpace(labeledOutline.Label)
            )
                ImGui.Dummy(new Vector2(0f, LabeledOutlineDrawing.ResolveTopClearance()));

            var min = ImGui.GetCursorScreenPos();
            var max = new Vector2(min.X + width, min.Y + frameHeight);
            var drawList = ImGui.GetWindowDrawList();
            var rounding = SlapCorners.ControlRadius;
            var cutout = spec.Outline is { } borderOutline
                && !string.IsNullOrWhiteSpace(borderOutline.Label)
                ? LabeledOutlineDrawing.ComputeLabelCutout(min, borderOutline)
                : (SlapOutlineCutout?)null;

            // Resolve control-level colors: variant-aware background + border
            var palette = spec.Variant == Variant.Action ? rt.ActionPalette : rt.InputPalette;
            var borderStrength = SurfaceComponent.ResolveBorderStrength(spec.Variant);
            var colors = SurfaceComponent.ResolveControlColors(
                palette,
                spec.State,
                strength: borderStrength,
                variant: spec.Variant);
            var borderColor = spec.IsValid
                ? colors.Border
                : rt.GetTextSlot(TextSlot.Danger);

            var background = colors.Background;

            LayeredShadow.Draw(
                drawList,
                min,
                max,
                rounding,
                spec.Shadow with { ExpandHorizontalClip = true });

            // Draw frame background
            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(background), rounding);

            // Draw icon (game icon or FontAwesome icon) — centered within a fixed
            // icon slot. When an icon is present the slot starts at the left edge;
            // without an icon the text area gets left padding.
            // Icon color remains stable across interaction states.
            // GameIcon+text splits the FA alignment spacing around the slot.
            var isIconOnly = hasIcon && width <= MetricsScope.UnitHeight + 0.5f;
            var leading = IconTextComponent.ResolveLeadingLayout(
                hasGameIcon,
                hasFaIcon,
                !isIconOnly,
                IconLeftPad,
                IconGap,
                iconSlotWidth,
                applyFaLeftPadding: !isIconOnly);
            var textOffsetX = hasIcon
                ? min.X + leading.LeftPadding
                : min.X + textPadX;
            var iconColor = SlapColor.WithDisabledAlpha(palette.Text, disabled);
            if (hasGameIcon && iconSlotWidth > 0f)
            {
                var (gameIconMin, gameIconMax) = GameIconComponent.ResolveControlIconRect(
                    new Vector2(textOffsetX, min.Y),
                    new Vector2(textOffsetX + iconSlotWidth, max.Y)
                );
                GameIconComponent.Draw(
                    drawList,
                    gameIconId,
                    gameIconMin,
                    gameIconMax,
                    spec.HighQualityIcon);
                textOffsetX += leading.IconSlotWidth + leading.TextGap;
            }
            else if (hasFaIcon && iconSlotWidth > 0f)
            {
                if (faIconFont.HasValue)
                    ImGui.PushFont(faIconFont.Value);
                try
                {
                    var faIconSize = ImGui.CalcTextSize(faIconText);
                    var iconPos = new Vector2(
                        textOffsetX + SlapIcon.ResolveHorizontalCenteringOffset(
                            faIconFont,
                            faIconText!,
                            iconSlotWidth),
                        min.Y + MathF.Max(0f, (frameHeight - faIconSize.Y) * 0.5f)
                    );
                    drawList.AddText(iconPos, ImGui.ColorConvertFloat4ToU32(iconColor), faIconText);
                    textOffsetX += leading.IconSlotWidth + leading.TextGap;
                }
                finally
                {
                    if (faIconFont.HasValue)
                        ImGui.PopFont();
                }
            }

            // Position the input to cover the FULL control width (including the
            // icon area) so that hovering / clicking the icon interacts with the
            // input — matching V2 behavior. FramePadding places the native input
            // text at the same resolved content origin used by the custom icon
            // layout, including the label-only horizontal padding.
            var framePadX = textOffsetX - min.X;
            var framePadY = MathF.Max(0f, (frameHeight - ImGui.GetTextLineHeight()) * 0.5f);
            ImGui.SetCursorScreenPos(min);

            // Transparent input styling — the frame is already drawn via draw list.
            // Text + hint colors follow the variant palette.
            var textColor = spec.IsValid ? palette.Text : rt.GetTextSlot(TextSlot.Danger);
            var hintColor = SlapColor.WithDisabledAlpha(palette.Text, disabled);
            hintColor.W *= 0.33f;
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
            ImGui.PushStyleColor(
                ImGuiCol.Text,
                SlapColor.WithDisabledAlpha(textColor, disabled)
            );
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, hintColor);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(framePadX, framePadY));
            var inputFlags = spec.ReportEveryChange
                ? (spec.AutoSelectAll ? ImGuiInputTextFlags.AutoSelectAll : (ImGuiInputTextFlags)0)
                : (
                    spec.AutoSelectAll
                        ? ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll
                        : ImGuiInputTextFlags.EnterReturnsTrue
                );
            if (spec.ReadOnly)
                inputFlags |= ImGuiInputTextFlags.ReadOnly;

            ImGui.SetNextItemWidth(width);
            var buffer = value ?? "";
            if (disabled)
                ImGui.BeginDisabled();
            var changed = ImGui.InputTextWithHint(
                "##input",
                spec.Placeholder ?? "",
                ref buffer,
                Math.Max(1, maxLength),
                inputFlags
            );
            if (disabled)
                ImGui.EndDisabled();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);

            var rawInputHovered = ImGui.IsItemHovered();
            var active = ImGui.IsItemActive();
            var focused = ImGui.IsItemFocused();
            var activated = ImGui.IsItemActivated();
            var deactivated = ImGui.IsItemDeactivated();
            var enterPressed = ImGui.IsKeyPressed(ImGuiKey.Enter, false)
                || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false);
            var entered = enterPressed && (active || deactivated);
            var canceled = deactivated && ImGui.IsKeyPressed(ImGuiKey.Escape, false);
            var cleared = false;
            var clearHovered = false;
            if (spec.Clearable && (!string.IsNullOrEmpty(buffer) || spec.ShowClearWhenEmpty))
            {
                var clearMin = new Vector2(max.X - frameHeight, min.Y);
                // 自绘命中区不走 ImGui item 流程，需先判窗口顶层；
                // AllowWhenBlockedByActiveItem 保证输入编辑态下清空按钮仍可用。
                var rawClearHovered = ImGui.IsMouseHoveringRect(clearMin, max)
                    && ImGui.IsWindowHovered(
                        ImGuiHoveredFlags.RootAndChildWindows
                        | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
                clearHovered = HoverArbitration.Current.CaptureHover(rawClearHovered);
                cleared = clearHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
                if (cleared)
                {
                    buffer = string.Empty;
                    changed = true;
                    ImGuiP.ClearActiveID();
                    if (spec.RefocusAfterClear)
                        ImGui.SetKeyboardFocusHere(-1);
                }

                DrawClearAction(
                    drawList,
                    clearMin,
                    max,
                    background,
                    palette.Text,
                    clearHovered
                );
                HoverArbitration.Current.TryShowTooltip(clearHovered, spec.ClearTooltip, clearMin, max);
            }

            var hovered = clearHovered
                || HoverArbitration.Current.CaptureHover(rawInputHovered);

            // Draw border after input so it stays on top
            LabeledOutlineDrawing.DrawBorderWithCutout(
                drawList,
                min,
                max,
                cutout,
                borderColor,
                rounding,
                MetricsScope.BorderThickness
            );

            // Focus-only outline: V2 text boxes only show the glow outline
            // when the input is active (IsItemActive), not on hover.
            // This differs from buttons which show outline on hover.
            var focusAccent = spec.Outline is { } focusOutline
                ? LabeledOutlineDrawing.ResolveColor(focusOutline)
                : rt.ResolveOutlineAccent(spec.Variant);
            var interactionOutline = new SlapOutlineGroup();
            if (active)
                interactionOutline.Capture(
                    min,
                    max,
                    true,
                    selected: false,
                    accent: focusAccent,
                    cutout,
                    rounding: rounding
                );

            // Edge fade
            if (spec.EdgeFade is { } fade && fade.IsEnabled)
            {
                var fadeBg = fade.Background ?? background;
                var verticalInset = fade.VerticalInset > 0f ? fade.VerticalInset : MetricsScope.BorderThickness;
                EdgeFade.DrawRight(
                    drawList,
                    min,
                    width,
                    frameHeight,
                    fadeBg,
                    fade.FadeWidth,
                    verticalInset
                );
            }

            interactionOutline.Flush(
                spec.Key,
                1f
            );

            if (spec.Outline is { } overlayOutline)
            {
                LabeledOutlineDrawing.DrawOverlay(
                    drawList,
                    min,
                    max,
                    overlayOutline,
                    rounding
                );
            }

            var tooltip = ResponsiveTooltip.Compose(
                spec.ResolvedWidth > 0f
                    && string.IsNullOrEmpty(buffer)
                    && width + 0.5f < ResolvePlaceholderContentWidth(spec),
                spec.Placeholder,
                spec.Tooltip);
            HoverArbitration.Current.TryShowTooltip(hovered, tooltip, min, max);

            ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y));
            return new TextInputResult(
                buffer,
                changed,
                hovered,
                active,
                focused,
                activated,
                cleared,
                entered,
                deactivated,
                canceled,
                min,
                max
            );
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>
    /// Measure the preferred and structural-minimum widths for responsive layout.
    /// </summary>
    public static ResponsiveWidthRange MeasureResponsiveWidth(TextInputSpec spec)
    {
        var h = MetricsScope.UnitHeight;
        var (faIconText, _) = SlapIcon.Resolve(spec.Icon);
        var hasIcon = spec.IconId.HasValue || !string.IsNullOrEmpty(faIconText);
        var contentWidth = ResolvePlaceholderContentWidth(spec);
        var preferredBaseline = MetricsScope.UnitWidth
            * (spec.WidthUnits > 0f ? spec.WidthUnits : 2f);
        var minimumWidth = hasIcon ? h : MetricsScope.UnitWidth * 2f;
        return ResponsiveWidthRange.Create(contentWidth, preferredBaseline, minimumWidth);
    }

    private static float ResolvePlaceholderContentWidth(TextInputSpec spec)
    {
        var (faIconText, _) = SlapIcon.Resolve(spec.Icon);
        var hasGameIcon = spec.IconId.HasValue;
        var hasFaIcon = !string.IsNullOrEmpty(faIconText);
        var hasIcon = hasGameIcon || hasFaIcon;
        var iconSlot = hasIcon ? ResolveIconSlotWidth() : 0f;
        var pad = MetricsScope.ScalePadding(TextPaddingX);
        var leading = IconTextComponent.ResolveLeadingLayout(
            hasGameIcon,
            hasFaIcon,
            true,
            IconLeftPad,
            IconGap,
            iconSlot);

        float textW = 0f;
        using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold))
            textW = ImGui.CalcTextSize(spec.Placeholder ?? string.Empty).X;

        return hasIcon
            ? leading.TotalWidth + textW + pad
            : iconSlot + textW + pad;
    }

    private static void DrawClearAction(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        Vector4 background,
        Vector4 textColor,
        bool hovered
    )
    {
        var fill = hovered
            ? SlapColor.DeriveHoverColor(background)
            : background;
        drawList.AddRectFilled(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(fill),
            SlapCorners.ControlRadius,
            ImDrawFlags.RoundCornersRight
        );

        var (iconText, iconFont) = SlapIcon.Resolve(FontAwesomeIcon.Times);
        if (string.IsNullOrEmpty(iconText) || !iconFont.HasValue)
            return;

        ImGui.PushFont(iconFont.Value);
        try
        {
            var iconSize = ImGui.CalcTextSize(iconText);
            drawList.AddText(
                min + ((max - min) - iconSize) * 0.5f,
                ImGui.ColorConvertFloat4ToU32(textColor),
                iconText
            );
        }
        finally
        {
            ImGui.PopFont();
        }
    }
}
