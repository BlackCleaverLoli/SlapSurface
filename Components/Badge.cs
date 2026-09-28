using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Label with an optional icon. Always has a border. Interaction is reported
/// through <see cref="ControlResult"/> so callers can own any business action.
/// Variant drives fill and border colour; defaults to <see cref="Variant.Flat"/>.
/// </summary>
internal readonly record struct BadgeSpec(
    ControlKey Key,
    string Label,
    Variant Variant = Variant.Flat,
    FontAwesomeIcon? Icon = null,
    uint GameIconId = 0,
    GameIconShape GameIconShape = GameIconShape.Standard,
    string? Tooltip = null,
    float ResolvedWidth = 0f,
    /// <summary>Optional semantic accent. When set, overrides the variant's
    /// default text and border color with the corresponding
    /// <see cref="TextSlot"/> value.</summary>
    TextSlot? Semantic = null,
    SlapFontSize FontSize = SlapFontSize.Regular,
    SlapFontWeight FontWeight = SlapFontWeight.Bold,
    /// <summary>When true and <see cref="ResolvedWidth"/> does not trigger
    /// responsive mode, content is left-aligned instead of centered.</summary>
    bool LeftAlignContent = false,
    /// <summary>Whether game icons rendered by this badge keep their shadow.</summary>
    bool IconShadow = true,
    /// <summary>
    /// Optional leading icon list. When present, these icons are drawn
    /// left-to-right before <see cref="Label"/> and take priority over the
    /// single <see cref="GameIconId"/> and FA <see cref="Icon"/> fields.
    /// </summary>
    IReadOnlyList<BadgeIcon>? LeadingIcons = null,
    /// <summary>
    /// Optional labeled outline. When present, the badge border is drawn as a
    /// labeled outline (border with a label cutout) instead of a plain control
    /// border.
    /// </summary>
    LabeledOutlineSpec? Outline = null
);

/// <summary>
/// One leading badge icon. Exactly one of <see cref="GameIconId"/> or
/// <see cref="Icon"/> should be set; game icons take priority.
/// </summary>
internal readonly record struct BadgeIcon(
    uint GameIconId = 0,
    FontAwesomeIcon Icon = FontAwesomeIcon.None
);

internal static class BadgeComponent
{
    private const float IconGap = 0f;

    internal readonly record struct ContentContext(
        ImDrawListPtr DrawList,
        Vector2 Min,
        Vector2 Max,
        Vector4 TextColor,
        Vector4 Background
    );

    public static ControlResult Draw(BadgeSpec spec)
    {
        using var _font = TypographyScope.PushCurrent(spec.FontSize, spec.FontWeight);
        var h = MetricsScope.UnitHeight;
        var widthRange = MeasureResponsiveWidthCurrentFont(spec);
        var width = spec.ResolvedWidth > 0f
            ? MathF.Max(widthRange.MinimumWidth, spec.ResolvedWidth)
            : widthRange.PreferredWidth;
        var naturalWidth = widthRange.PreferredWidth;
        var isResponsive = spec.ResolvedWidth > 0f && width < naturalWidth;
        var (iconText, iconFont) = SlapIcon.Resolve(spec.Icon);

        var tooltip = ResponsiveTooltip.Compose(isResponsive, spec.Label, spec.Tooltip);
        return DrawCustom(
            spec.Key,
            new Vector2(width, h),
            spec.Variant,
            spec.Semantic,
            tooltip,
            context =>
                DrawContent(
                    context.DrawList,
                    spec,
                    iconText,
                    iconFont,
                    context.Min,
                    context.Max,
                    context.TextColor,
                    context.Background,
                    isResponsive
                ),
            spec.Outline
        );
    }

    internal static ControlResult DrawCustom(
        ControlKey key,
        Vector2 size,
        Variant variant,
        TextSlot? semantic,
        string? tooltip,
        Action<ContentContext> drawContent,
        LabeledOutlineSpec? outline = null
    )
    {
        if (string.IsNullOrWhiteSpace(key.Value))
            throw new ArgumentException("Badge key must not be empty.", nameof(key));

        ImGui.PushID(key.Value);
        try
        {
            var min = ImGui.GetCursorScreenPos();
            var max = min + size;
            var rt = ThemeScope.Resolved;
            var palette = rt.GetButtonPalette(variant);
            var colors = SurfaceComponent.ResolveControlColors(
                palette,
                ControlState.None,
                semantic,
                strength: BorderStrength.Strong,
                variant: variant);
            var textColor = colors.Text;
            var borderColor = colors.Border;
            var drawList = ImGui.GetWindowDrawList();
            var rounding = MathF.Min(SlapCorners.ControlRadius, MathF.Min(size.X, size.Y) * 0.5f);

            drawList.AddRectFilled(
                min,
                max,
                ImGui.ColorConvertFloat4ToU32(palette.Base),
                rounding
            );
            drawContent(new ContentContext(drawList, min, max, textColor, palette.Base));

            if (outline is { } labeledOutline)
            {
                LabeledOutlineDrawing.DrawOverlay(
                    drawList,
                    min,
                    max,
                    labeledOutline,
                    rounding);
            }
            else
            {
                SurfaceComponent.DrawControlBorder(
                    drawList,
                    min,
                    max,
                    variant,
                    borderColor,
                    rounding);
            }

            ImGui.SetCursorScreenPos(min);

            var rawClicked = ImGui.InvisibleButton("##badge", size);
            var itemMin = ImGui.GetItemRectMin();
            var itemMax = ImGui.GetItemRectMax();
            var interaction = SlapInteraction.Capture(
                disabled: false,
                selected: false,
                rawClicked: rawClicked
            );
            HoverArbitration.Current.TryShowTooltip(interaction.Hovered, tooltip);
            return interaction.ToControlResult(itemMin, itemMax);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    internal static float ResolveWidth(BadgeSpec spec)
    {
        using var _font = TypographyScope.PushCurrent(spec.FontSize, spec.FontWeight);
        return ResolveWidthCurrentFont(spec);
    }

    private static float ResolveWidthCurrentFont(BadgeSpec spec)
    {
        var unit = MetricsScope.UnitHeight;

        if (spec.LeadingIcons is { Count: > 0 } leadingIcons)
        {
            var leadingHasLabel = !string.IsNullOrWhiteSpace(spec.Label);
            var iconGap = MetricsScope.ScaleGap(IconTextComponent.GameIconTextGap);
            if (!leadingHasLabel)
            {
                return MathF.Max(
                    unit,
                    (leadingIcons.Count * unit)
                    + ((leadingIcons.Count - 1) * iconGap));
            }

            var leftPadding = MetricsScope.ScalePadding(
                IconTextComponent.GameIconLeftPadding);
            var rightPadding = MetricsScope.ScalePadding(SlapPx.Space16);
            var labelWidth = ImGui.CalcTextSize(spec.Label).X;
            var leadingContentWidth = leftPadding
                + (leadingIcons.Count * unit)
                + ((leadingIcons.Count - 1) * iconGap)
                + iconGap
                + labelWidth;
            return MathF.Max(unit, leadingContentWidth + rightPadding);
        }

        var leadingGameIconCount = ResolveLeadingGameIconCount(spec);
        var hasGameIcon = leadingGameIconCount > 0;
        var (iconText, iconFont) = SlapIcon.Resolve(spec.Icon);
        var hasFaIcon = !hasGameIcon && !string.IsNullOrEmpty(iconText);
        var hasIcon = hasGameIcon || hasFaIcon;
        var hasLabel = !string.IsNullOrEmpty(spec.Label);
        if (hasIcon && !hasLabel)
        {
            if (!hasGameIcon)
                return unit;

            var iconGap = MetricsScope.ScaleGap(IconTextComponent.GameIconTextGap);
            return MathF.Max(
                unit,
                (leadingGameIconCount * unit)
                + ((leadingGameIconCount - 1) * iconGap));
        }

        float contentWidth;
        if (hasGameIcon)
        {
            var iconGap = MetricsScope.ScaleGap(IconTextComponent.GameIconTextGap);
            var gameIconLeftPadding = MetricsScope.ScalePadding(
                IconTextComponent.GameIconLeftPadding);
            var labelWidth = hasLabel ? ImGui.CalcTextSize(spec.Label).X : 0f;
            contentWidth = gameIconLeftPadding
                + (leadingGameIconCount * unit)
                + ((leadingGameIconCount - 1) * iconGap)
                + (hasLabel ? iconGap : 0f)
                + labelWidth;
        }
        else
        {
            var leading = IconTextComponent.ResolveLeadingLayout(
                hasGameIcon,
                hasFaIcon,
                hasLabel,
                SlapPx.Space4,
                IconGap,
                unit,
                applyFaLeftPadding: hasLabel);
            contentWidth = leading.TotalWidth;
            if (hasLabel)
                contentWidth += ImGui.CalcTextSize(spec.Label).X;
        }

        var leftPad = hasIcon && hasLabel
            ? 0f
            : MetricsScope.ScalePadding(SlapPx.Space16);
        var rightPad = MetricsScope.ScalePadding(SlapPx.Space16);
        return MathF.Max(unit, contentWidth + leftPad + rightPad);
    }

    /// <summary>
    /// Measure the preferred and structural-minimum widths for responsive layout.
    /// A badge with an icon can shrink to its icon form. A badge without an
    /// icon keeps its natural width so it never becomes an empty square.
    /// </summary>
    public static ResponsiveWidthRange MeasureResponsiveWidth(BadgeSpec spec)
    {
        using var _font = TypographyScope.PushCurrent(spec.FontSize, spec.FontWeight);
        return MeasureResponsiveWidthCurrentFont(spec);
    }

    private static ResponsiveWidthRange MeasureResponsiveWidthCurrentFont(BadgeSpec spec)
    {
        var unit = MetricsScope.UnitHeight;
        var fullWidth = ResolveWidthCurrentFont(spec);

        if (spec.LeadingIcons is { Count: > 0 })
            return ResponsiveWidthRange.Create(fullWidth, 0f, unit);

        var leadingGameIconCount = ResolveLeadingGameIconCount(spec);
        var (iconText, iconFont) = SlapIcon.Resolve(spec.Icon);
        var hasGameIcon = leadingGameIconCount > 0;
        var hasIcon = hasGameIcon || !string.IsNullOrEmpty(iconText);

        if (!hasIcon)
            return ResponsiveWidthRange.Create(fullWidth, 0f, fullWidth);

        // Minimum width (= icon-only collapsed state). For a leading game-icon
        // list the collapse target is one icon, which keeps the responsive
        // behavior consistent with the single-icon badge.
        var iconSize = hasGameIcon
            ? unit
            : iconFont.HasValue
                ? MeasureFaIcon(iconText!, iconFont.Value)
                : ImGui.CalcTextSize(iconText).X;
        var minimumWidth = MathF.Max(unit, iconSize);

        return ResponsiveWidthRange.Create(fullWidth, 0f, minimumWidth);
    }

    private static float MeasureFaIcon(string iconText, ImFontPtr iconFont)
    {
        ImGui.PushFont(iconFont);
        try
        {
            return ImGui.CalcTextSize(iconText).X;
        }
        finally
        {
            ImGui.PopFont();
        }
    }

    private static void DrawContent(
        ImDrawListPtr drawList,
        BadgeSpec spec,
        string? iconText,
        ImFontPtr? iconFont,
        Vector2 min,
        Vector2 max,
        Vector4 textColor,
        Vector4 fadeBackground,
        bool isResponsive = false
    )
    {
        if (spec.LeadingIcons is { Count: > 0 } leadingIcons)
        {
            var leadingLeftAlign = isResponsive || spec.LeftAlignContent;
            DrawLeadingIconContent(
                drawList,
                spec,
                leadingIcons,
                min,
                max,
                textColor,
                isResponsive,
                leadingLeftAlign);
            return;
        }

        var leadingGameIconCount = ResolveLeadingGameIconCount(spec);
        var hasGameIcon = leadingGameIconCount > 0;
        var hasFaIcon = !hasGameIcon && !string.IsNullOrEmpty(iconText);
        var hasIcon = hasGameIcon || hasFaIcon;
        var hasLabel = !string.IsNullOrEmpty(spec.Label);
        if (!hasIcon && !hasLabel)
            return;

        var leftAlign = isResponsive || spec.LeftAlignContent;

        // Delegate to IconTextComponent with Button-identical parameters.
        // DrawInRect's shared leading layout keeps FA icon and label aligned.
        IconTextComponent.DrawInRect(
            drawList,
            min, max,
            spec.Label ?? string.Empty,
            iconText, iconFont,
            textColor,
            rightFadeBackground: fadeBackground,
            rightFadeVerticalBorderInset: MetricsScope.BorderThickness,
            iconTextGap: IconGap,
            horizontalPadding: hasLabel ? SlapPx.Space16 : 0f,
            leftPadding: (hasIcon && hasLabel) ? SlapPx.Space4 : null,
            centerContent: !leftAlign,
            progressiveCenter: isResponsive,
            gameIconId: spec.GameIconId,
            gameIconShape: spec.GameIconShape,
            gameIconShadow: spec.IconShadow
        );
    }

    private static int ResolveLeadingGameIconCount(BadgeSpec spec)
    {
        return spec.GameIconId != 0 ? 1 : 0;
    }

    private static void DrawLeadingIconContent(
        ImDrawListPtr drawList,
        BadgeSpec spec,
        IReadOnlyList<BadgeIcon> icons,
        Vector2 min,
        Vector2 max,
        Vector4 textColor,
        bool isResponsive,
        bool leftAlign)
    {
        var iconCount = icons.Count;
        var hasLabel = !string.IsNullOrWhiteSpace(spec.Label);
        var unit = MetricsScope.UnitHeight;
        var iconGap = MetricsScope.ScaleGap(IconTextComponent.GameIconTextGap);
        var leftPadding = hasLabel
            ? MetricsScope.ScalePadding(IconTextComponent.GameIconLeftPadding)
            : 0f;
        var rightPadding = hasLabel
            ? MetricsScope.ScalePadding(SlapPx.Space16)
            : 0f;
        var labelWidth = hasLabel ? ImGui.CalcTextSize(spec.Label).X : 0f;
        var contentWidth = (iconCount * unit)
            + ((iconCount - 1) * iconGap)
            + (hasLabel ? iconGap + labelWidth : 0f);
        var contentMinX = min.X + leftPadding;
        var contentMaxX = max.X - rightPadding;
        var x = !leftAlign && hasLabel
            ? contentMinX + MathF.Max(0f, (contentMaxX - contentMinX - contentWidth) * 0.5f)
            : contentMinX;

        var textColorU32 = ImGui.ColorConvertFloat4ToU32(textColor);
        for (var i = 0; i < iconCount; i++)
        {
            var slotMin = new Vector2(x, min.Y);
            var slotMax = new Vector2(x + unit, max.Y);
            var icon = icons[i];
            if (icon.GameIconId != 0)
            {
                var (iconMin, iconMax) = GameIconComponent.ResolveControlIconRect(
                    slotMin,
                    slotMax);
                GameIconComponent.Draw(
                    drawList,
                    icon.GameIconId,
                    iconMin,
                    iconMax,
                    shape: spec.GameIconShape,
                    shadow: spec.IconShadow);
            }
            else if (icon.Icon != FontAwesomeIcon.None)
            {
                var (iconText, iconFont) = SlapIcon.Resolve(icon.Icon);
                if (!string.IsNullOrEmpty(iconText))
                {
                    if (iconFont.HasValue)
                        ImGui.PushFont(iconFont.Value);
                    try
                    {
                        var iconSize = ImGui.CalcTextSize(iconText);
                        var iconPos = new Vector2(
                            slotMin.X + MathF.Max(0f, (unit - iconSize.X) * 0.5f),
                            slotMin.Y + MathF.Max(0f, (unit - iconSize.Y) * 0.5f));
                        drawList.AddText(iconPos, textColorU32, iconText);
                    }
                    finally
                    {
                        if (iconFont.HasValue)
                            ImGui.PopFont();
                    }
                }
            }

            x += unit;
            if (i + 1 < iconCount)
                x += iconGap;
        }

        if (!hasLabel)
            return;

        x += iconGap;
        var textSize = ImGui.CalcTextSize(spec.Label);
        var textY = min.Y + MathF.Max(0f, ((max.Y - min.Y) - textSize.Y) * 0.5f);
        var textMaxX = max.X - rightPadding;
        if (x < textMaxX)
        {
            drawList.PushClipRect(new Vector2(x, min.Y), new Vector2(textMaxX, max.Y), true);
            drawList.AddText(new Vector2(x, textY), textColorU32, spec.Label);
            drawList.PopClipRect();
        }

        if (isResponsive && textSize.X > textMaxX - x)
        {
            EdgeFade.DrawRight(
                drawList,
                new Vector2(x, min.Y),
                MathF.Max(0f, textMaxX - x),
                max.Y - min.Y,
                ThemeScope.Resolved.Surface,
                MetricsScope.BorderThickness);
        }
    }
}
