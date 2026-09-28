using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <remarks>
/// <see cref="Variant"/> drives the button base color and style.
/// Only <see cref="ControlState.Disabled"/> is currently consumed
/// (greys out the button and prevents clicks).
/// <see cref="ControlState.Selected"/> and <see cref="ControlState.Emphasis"/>
/// are reserved for future visual commitments (no-op in skeleton).
/// <para>
/// All buttons render via <c>InvisibleButton</c> + draw list, providing
/// consistent border, hover outline, and shadow support regardless of
/// whether <see cref="Icon"/> is set.
/// When <see cref="Label"/> is null/empty and <see cref="Icon"/> is set,
/// the button behaves as an icon-only button (default size = 1×1 square).
/// </para>
/// <para>
/// <see cref="ButtonSize.Auto"/> measures the natural content width
/// (icon slot + gap + bold text + horizontal padding) so the button
/// shrinks or grows to fit its content, aligned with V2 tab-strip buttons.
/// For icon-only buttons, <see cref="ButtonSize.Auto"/> resolves to a 1×1 square.
/// In responsive layouts, an explicit <see cref="ButtonSize.Rect(float, float)"/>
/// width is a preferred baseline, not a structural minimum.
/// </para>
/// <para>
/// <see cref="Shadow"/> draws a layered shadow behind the button via draw list.
/// </para>
/// </remarks>
internal readonly record struct ButtonSpec(
    ControlKey Key,
    string? Label,
    ButtonSize Size = default,
    Variant Variant = Variant.Base,
    ControlState State = ControlState.None,
    string? Tooltip = null,
    FontAwesomeIcon? Icon = null,
    LayeredShadowSpec Shadow = default,
    /// <summary>Optional semantic accent. When set, overrides the variant’s
    /// default text and border color with the corresponding
    /// <see cref="TextSlot"/> value.</summary>
    TextSlot? Semantic = null,
    /// <summary>
    /// Resolved button width in scaled pixels, set by an external layout
    /// system (e.g. <see cref="ResponsiveSlotGroup"/>). When &gt; 0 and less
    /// than the natural content width, the label clips/fades and the icon
    /// progressively centers. At the structural 1×1 minimum, an icon-bearing
    /// button explicitly switches to icon-only content.
    /// Bypasses <see cref="Size"/>.
    /// </summary>
    float ResolvedWidth = 0f,
    uint GameIconId = 0,
    SlapTooltipDirection? TooltipDirection = null
);

internal static class ButtonComponent
{
    // Padding and gap constants:
    // - TextPaddingX = Space16: symmetric horizontal padding for label-only
    //   buttons and right padding for all label-bearing buttons.
    // - IconLeftPad = Space4: left padding for icon+label buttons.
    // - IconGap = 0: text starts flush against the icon slot end, matching left-aligned controls.
    private const float TextPaddingX = SlapPx.Space16;
    private const float IconLeftPad = SlapPx.Space4;
    private const float IconOnlyPaddingX = 0f;
    private const float IconGap = 0f;

    // Icon slot width = UnitHeight (button content is centered, slot matches left-aligned controls).
    private static float ResolveIconSlotWidth() => MetricsScope.ResolveIconSlotWidth(true);

    /// <summary>
    /// Pre-measures the natural width of an auto-sized icon+text button.
    /// Call this before drawing to reserve layout space (e.g. tab strip right slot).
    /// </summary>
    public static float ResolveAutoWidth(FontAwesomeIcon? icon, string? label, uint gameIconId = 0)
    {
        var (iconText, _) = SlapIcon.Resolve(icon);
        var hasGameIcon = gameIconId != 0;
        var hasFaIcon = !string.IsNullOrEmpty(iconText);
        var hasIcon = hasGameIcon || hasFaIcon;
        var hasLabel = !string.IsNullOrEmpty(label);
        var unit = MetricsScope.UnitHeight;
        if (hasIcon && !hasLabel)
            return unit;

        var leading = IconTextComponent.ResolveLeadingLayout(
            hasGameIcon,
            hasFaIcon,
            hasLabel,
            IconLeftPad,
            IconGap,
            ResolveIconSlotWidth(),
            applyFaLeftPadding: hasLabel);
        var contentWidth = leading.TotalWidth;

        if (hasLabel)
        {
            using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold))
                contentWidth += ImGui.CalcTextSize(label!).X;
        }

        var totalPadding = hasIcon
            ? MetricsScope.ScalePadding(TextPaddingX)
            : MetricsScope.ScalePadding(TextPaddingX) * 2f;

        return MathF.Max(unit, contentWidth + totalPadding);
    }

    public static ControlResult Draw(ButtonSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("ButtonSpec.Key.Value must not be empty.", nameof(spec));

        // When ResolvedWidth is set, enforce the control's responsive minimum.
        // Label clip/fade and icon progressive centering are handled in
        // DrawIconLabelContent. The structural minimum is also the explicit
        // icon-only state for icon-bearing buttons.
        if (spec.ResolvedWidth > 0f)
        {
            var minimumWidth = MeasureResponsiveWidth(spec).MinimumWidth;
            spec = spec with { ResolvedWidth = MathF.Max(minimumWidth, spec.ResolvedWidth) };
        }

        ImGui.PushID(spec.Key.Value);
        try
        {
            var size = ResolveSize(spec);
            var disabled = spec.State.HasFlag(ControlState.Disabled);

            var (iconText, iconFont) = SlapIcon.Resolve(spec.Icon);
            return DrawWithDrawList(spec, iconText, iconFont, size, disabled);
        } finally
        {
            ImGui.PopID();
        }
    }

    private static ControlResult DrawWithDrawList(ButtonSpec spec, string? iconText, ImFontPtr? iconFont, Vector2 size, bool disabled)
    {
        var rt = ThemeScope.Resolved;
        var palette = rt.GetButtonPalette(spec.Variant);
        var rounding = SlapCorners.ControlRadius;
        var drawList = ImGui.GetWindowDrawList();

        var min = ImGui.GetCursorScreenPos();
        var max = min + size;

        // Shadow (drawn before hit test so it doesn't interfere). Push a
        // fullscreen clip so the spread isn't cut off by the enclosing
        // window/child bounds (matches ContentCard/Popup shadow handling).
        LayeredShadow.Draw(
            drawList,
            min,
            max,
            rounding,
            LayeredShadow.ResolveControlDefault(spec.Shadow, spec.Variant) with { ExpandHorizontalClip = true });

        var rawClicked = ImGui.InvisibleButton("##btn", size);
        var ix = SlapInteraction.Capture(spec.State, rawClicked, captureRightClick: true);

        // Background
        var borderStrength = SurfaceComponent.ResolveBorderStrength(spec.Variant);
        var colors = SurfaceComponent.ResolveControlColors(
            palette,
            spec.State,
            spec.Semantic,
            ix.Hovered,
            ix.Active,
            borderStrength,
            spec.Variant);
        var bg = colors.Background;
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(bg), rounding);

        // Border — semantic tint overrides variant default
        var border = colors.Border;
        SurfaceComponent.DrawControlBorder(
            drawList,
            min,
            max,
            spec.Variant,
            border,
            rounding);

        // Hover outline — use semantic color if set, else variant accent
        var outlineAccent = rt.ResolveOutlineAccent(spec.Variant, spec.Semantic);
        var outline = new SlapOutlineGroup();
        if (ix.Hovered && !disabled)
            outline.Capture(
                min,
                max,
                true,
                selected: false,
                accent: outlineAccent,
                rounding: rounding);

        // Content (icon + optional label) — semantic overrides text color
        var textColor = colors.Text;
        DrawIconLabelContent(
            drawList,
            spec,
            iconText,
            iconFont,
            min,
            max,
            textColor,
            bg,
            ix.VisualMode);

        outline.Flush(spec.Key, 1f);

        var naturalWidth = ResolveAutoWidth(spec.Icon, spec.Label, spec.GameIconId);
        var degraded = spec.ResolvedWidth > 0f && size.X + 0.5f < naturalWidth;
        var tooltip = ResponsiveTooltip.Compose(degraded, spec.Label, spec.Tooltip);
        HoverArbitration.Current.TryShowTooltip(
            ix.Hovered,
            tooltip,
            null,
            null,
            spec.TooltipDirection);

        ImGui.SetCursorScreenPos(new Vector2(min.X, max.Y));
        return ix.ToControlResult(min, max);
    }

    private static void DrawIconLabelContent(
        ImDrawListPtr drawList,
        ButtonSpec spec,
        string? iconText,
        ImFontPtr? iconFont,
        Vector2 min,
        Vector2 max,
        Vector4 textColor,
        Vector4 fadeBackground,
        ControlVisualMode visualMode)
    {
        var hasIcon = !string.IsNullOrEmpty(iconText) || spec.GameIconId != 0;
        var isIconOnly = hasIcon
            && (
                string.IsNullOrEmpty(spec.Label)
                || spec.ResolvedWidth > 0f
                    && max.X - min.X <= MetricsScope.UnitHeight + 0.5f
            );
        var visibleLabel = isIconOnly ? string.Empty : spec.Label ?? string.Empty;
        var padding = isIconOnly ? IconOnlyPaddingX : TextPaddingX;

        using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold))
        {
            IconTextComponent.DrawInRect(
                drawList,
                min,
                max,
                visibleLabel,
                iconText,
                iconFont,
                textColor,
                rightFadeBackground: fadeBackground,
                rightFadeVerticalBorderInset: MetricsScope.BorderThickness,
                iconTextGap: IconGap,
                horizontalPadding: padding,
                leftPadding: hasIcon && !isIconOnly ? IconLeftPad : null,
                centerContent: true,
                progressiveCenter: true,
                gameIconId: spec.GameIconId,
                gameIconVisualMode: visualMode);
        }
    }

    private static Vector2 ResolveSize(ButtonSpec spec)
    {
        var w = MetricsScope.UnitWidth;
        var h = MetricsScope.UnitHeight;
        
        // When ResolvedWidth is set, use it directly.
        if (spec.ResolvedWidth > 0f)
        {
            return new Vector2(spec.ResolvedWidth, h);
        }

        var hasLabel = !string.IsNullOrEmpty(spec.Label);
        var hasIcon = spec.Icon.HasValue || spec.GameIconId != 0;

        // Icon-only button: default to 1×1 square (like former IconButton).
        if (!hasLabel && hasIcon)
        {
            return spec.Size.Kind switch
            {
                ButtonSizeKind.Square => new Vector2(h * spec.Size.WidthUnits, h * spec.Size.HeightUnits),
                ButtonSizeKind.Rect => new Vector2(w * spec.Size.WidthUnits, h * spec.Size.HeightUnits),
                _ => new Vector2(h, h) // Auto and default → square
            };
        }

        return spec.Size.Kind switch
        {
            ButtonSizeKind.Auto when hasIcon
                => new Vector2(ResolveAutoWidth(spec.Icon, spec.Label, spec.GameIconId), h),
            ButtonSizeKind.Auto when hasLabel
                => new Vector2(ResolveAutoWidth(null, spec.Label), h),
            ButtonSizeKind.Auto => new Vector2(h, h), // fallback: empty → square
            ButtonSizeKind.Square => new Vector2(h * spec.Size.WidthUnits, h * spec.Size.HeightUnits),
            ButtonSizeKind.Rect => new Vector2(w * spec.Size.WidthUnits, h * spec.Size.HeightUnits),
            _ => new Vector2(h, h)
        };
    }

    /// <summary>
    /// Measure the preferred and structural-minimum widths for responsive layout.
    /// <para>
    /// The layout system interpolates continuously from the preferred width to
    /// the control's structural minimum. Label clip/fade is handled by <see cref="Draw"/>.
    /// </para>
    /// </summary>
    public static ResponsiveWidthRange MeasureResponsiveWidth(ButtonSpec spec)
    {
        var h = MetricsScope.UnitHeight;
        var fullWidth = ResolveAutoWidth(spec.Icon, spec.Label, spec.GameIconId);
        var hasIcon = spec.Icon.HasValue || spec.GameIconId != 0;
        var minimumWidth = hasIcon ? h : fullWidth;
        var preferredBaseline = spec.Size.Kind == ButtonSizeKind.Rect
            ? MetricsScope.UnitWidth * spec.Size.WidthUnits
            : 0f;
        return ResponsiveWidthRange.Create(fullWidth, preferredBaseline, minimumWidth);
    }

}
