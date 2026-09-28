using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct IconTextSpec(
    ControlKey Key,
    string Label,
    FontAwesomeIcon? Icon = null,
    string? Tooltip = null,
    ControlState State = ControlState.None,
    float HeightUnits = 1f,
    float IconTextGap = SlapPx.Space6,
    float HorizontalPadding = 0f,
    Vector4? Color = null,
    Vector4? RightFadeBackground = null,
    uint GameIconId = 0
);

internal static class IconTextComponent
{
    /// <summary>Unscaled GameIcon alignment spacing split around its slot.</summary>
    internal const float GameIconLeftPadding = SlapPx.Space2;
    internal const float GameIconTextGap = SlapPx.Space2;

    internal readonly record struct IconTextLeadingLayout(
        float LeftPadding,
        float IconSlotWidth,
        float TextGap)
    {
        public float TotalWidth => LeftPadding + IconSlotWidth + TextGap;
    }

    internal static IconTextLeadingLayout ResolveLeadingLayout(
        bool hasGameIcon,
        bool hasFaIcon,
        bool hasText,
        float faLeftPadding,
        float faTextGap,
        float iconSlotWidth = 0f,
        bool centered = false,
        bool applyFaLeftPadding = true)
    {
        var hasIcon = hasGameIcon || hasFaIcon;
        if (!hasIcon)
            return default;

        var resolvedSlot = iconSlotWidth > 0f
            ? iconSlotWidth
            : MetricsScope.ResolveIconSlotWidth(centered);
        var usesGameIconAlignmentSpacing = hasGameIcon && hasText;
        var leftPadding = hasFaIcon && !hasGameIcon && applyFaLeftPadding
            ? MetricsScope.ScalePadding(faLeftPadding)
            : usesGameIconAlignmentSpacing
                ? MetricsScope.ScalePadding(GameIconLeftPadding)
                : 0f;
        var textGap = !hasText
            ? 0f
            : hasGameIcon
                ? usesGameIconAlignmentSpacing
                    ? MetricsScope.ScalePadding(GameIconTextGap)
                    : 0f
                : MetricsScope.ScaleGap(faTextGap);
        return new IconTextLeadingLayout(leftPadding, resolvedSlot, textGap);
    }

    public static void Draw(IconTextSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("IconTextSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        try
        {
            var height = MetricsScope.UnitHeight * Normalize(spec.HeightUnits);
            var width = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(width, height);
            var color = ResolveColor(spec);

            var (iconText, iconFont) = SlapIcon.Resolve(spec.Icon);
            var degraded = DrawInRect(
                ImGui.GetWindowDrawList(),
                min,
                max,
                spec.Label,
                iconText,
                iconFont,
                color,
                spec.RightFadeBackground,
                iconTextGap: spec.IconTextGap,
                horizontalPadding: spec.HorizontalPadding,
                gameIconId: spec.GameIconId,
                gameIconVisualMode: spec.State.HasFlag(ControlState.Disabled)
                    ? ControlVisualMode.Disabled
                    : ControlVisualMode.Base);

            ImGui.Dummy(new Vector2(width, height));
            HoverArbitration.Current.TryShowTooltip(
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled),
                ResponsiveTooltip.Compose(degraded, spec.Label, spec.Tooltip));
        } finally
        {
            ImGui.PopID();
        }
    }

    internal static bool DrawInRect(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        string label,
        string? iconText,
        ImFontPtr? iconFont,
        Vector4 color,
        Vector4? rightFadeBackground = null,
        float rightFadeVerticalBorderInset = 0f,
        float iconTextGap = 4f,
        float horizontalPadding = 0f,
        float? leftPadding = null,
        float? rightPadding = null,
        bool centerContent = false,
        float iconSlotWidth = 0f,
        bool progressiveCenter = false,
        uint gameIconId = 0,
        GameIconShape gameIconShape = GameIconShape.Standard,
        ControlVisualMode gameIconVisualMode = ControlVisualMode.Base,
        bool gameIconShadow = true
    )
    {
        if (max.X <= min.X || max.Y <= min.Y)
            return false;

        var rectWidth = max.X - min.X;

        // Pre-calculate icon and text presence for padding decisions.
        // Game icon and FA icon are mutually exclusive — game icon takes priority.
        var hasGameIcon = GameIconComponent.IsAvailable(gameIconId);
        var hasFaIcon = !string.IsNullOrEmpty(iconText);
        var hasIcon = hasGameIcon || hasFaIcon;
        var hasLabel = !string.IsNullOrEmpty(label);

        var effectiveCenterContent = centerContent;

        var requestedLeftPadding = leftPadding ?? (hasIcon ? 0f : horizontalPadding);
        var leading = ResolveLeadingLayout(
            hasGameIcon,
            hasFaIcon,
            hasLabel,
            requestedLeftPadding,
            iconTextGap,
            iconSlotWidth,
            effectiveCenterContent,
            hasLabel || leftPadding.HasValue);
        var scaledLeftPad = hasIcon
            ? leading.LeftPadding
            : MetricsScope.ScalePadding(requestedLeftPadding);
        var scaledRightPad = MetricsScope.ScalePadding(rightPadding ?? horizontalPadding);
        var contentMin = new Vector2(min.X + scaledLeftPad, min.Y);
        var contentMax = new Vector2(MathF.Max(contentMin.X, max.X - scaledRightPad),
                                     max.Y);
        if (contentMax.X <= contentMin.X)
            return false;

        var colorU32 = ImGui.ColorConvertFloat4ToU32(color);

        Vector2 iconSize = Vector2.Zero;
        if (hasFaIcon && !hasGameIcon)
        {
            if (iconFont.HasValue)
                ImGui.PushFont(iconFont.Value);
            try
            {
                iconSize = ImGui.CalcTextSize(iconText);
            }
            finally
            {
                if (iconFont.HasValue)
                    ImGui.PopFont();
            }
        }

        Vector2 textSize = Vector2.Zero;
        if (hasLabel)
            textSize = ImGui.CalcTextSize(label);

        var gap = leading.TextGap;
        var resolvedSlot = leading.IconSlotWidth;
        var useSlot = hasIcon && resolvedSlot > 0f;
        var iconAreaWidth = useSlot ? resolvedSlot : (hasIcon ? iconSize.X : 0f);
        var contentWidth = iconAreaWidth + gap + (hasLabel ? textSize.X : 0f);
        var x = effectiveCenterContent
            ? contentMin.X + MathF.Max(0f, (contentMax.X - contentMin.X - contentWidth) * 0.5f)
            : contentMin.X;

        // Progressive centering — two-phase model:
        //   Phase 1 (FullWidth → textAreaZeroWidth): label clips/fades to 0,
        //     icon stays fixed in its slot. No movement.
        //   Phase 2 (textAreaZeroWidth → UnitHeight): label is gone, icon
        //     slides monotonically from slot to rect center. Since the slot
        //     sits to the right of center (padding > 0), the slide is leftward.
        // The icon reaches center exactly when the rect becomes a 1:1 square.
        var centerProgress = 0f;
        if (progressiveCenter && hasIcon)
        {
            var unit = MetricsScope.UnitHeight;
            // Use symmetric padding for threshold so progressive centering
            // is not sensitive to leftPadding / rightPadding overrides.
            var textAreaZeroWidth = MetricsScope.ScalePadding(horizontalPadding) + iconAreaWidth + gap + MetricsScope.ScalePadding(horizontalPadding);
            if (textAreaZeroWidth > unit)
                centerProgress = 1f - Math.Clamp((rectWidth - unit) / (textAreaZeroWidth - unit), 0f, 1f);
            else
                centerProgress = 1f;
        }
        // Pure-icon: keep the icon anchored in its slot rather than sliding
        // toward the rect center during progressive collapse.
        if (hasIcon && !hasLabel)
            centerProgress = 0f;

        if (hasIcon)
        {
            var iconSlotX = x + (hasGameIcon
                ? 0f
                : SlapIcon.ResolveHorizontalCenteringOffset(iconFont, iconText!, iconAreaWidth));
            // Absolute center of the rect (where icon ends up at 1:1 square).
            var centeredWidth = hasGameIcon ? iconAreaWidth : iconSize.X;
            var iconCenterX = min.X + MathF.Max(0f, (rectWidth - centeredWidth) * 0.5f);
            var iconDrawX = iconSlotX + (iconCenterX - iconSlotX) * centerProgress;
            Vector2 iconDrawMin;
            Vector2 iconDrawMax;
            if (hasGameIcon)
            {
                (iconDrawMin, iconDrawMax) = GameIconComponent.ResolveControlIconRect(
                    new Vector2(iconDrawX, min.Y),
                    new Vector2(iconDrawX + iconAreaWidth, max.Y)
                );
            }
            else
            {
                var iconY = min.Y + MathF.Max(0f, ((max.Y - min.Y) - iconSize.Y) * 0.5f);
                iconDrawMin = new Vector2(iconDrawX, iconY);
                iconDrawMax = iconDrawMin + iconSize;
            }

            // Icon clip boundary is always the full rect (max.X), not the
            // padded contentMax. Reserved regions (e.g. arrow slot) are
            // already excluded from max.X by the caller. The right padding
            // (scaledRightPad) is purely a text aesthetic — it must not eat
            // into the icon's fixed slot. Otherwise, at minimum widths
            // (e.g. dropdown 2-icon state), the padding steals enough space
            // to trigger a false-positive clip + fade on the icon.
            var iconClipBoundary = max.X;
            var iconRight = iconDrawMax.X;
            var iconNeedsClip = iconRight > iconClipBoundary;
            // Only fade when icon is partially visible — not fully outside.
            var iconPartiallyVisible = iconDrawMin.X < iconClipBoundary;

            if (hasGameIcon)
            {
                if (iconNeedsClip)
                    drawList.PushClipRect(new Vector2(min.X, min.Y), new Vector2(iconClipBoundary, max.Y), true);
                GameIconComponent.Draw(
                    drawList, gameIconId,
                    iconDrawMin,
                    iconDrawMax,
                    shape: gameIconShape,
                    visualMode: gameIconVisualMode,
                    shadow: gameIconShadow);
                if (iconNeedsClip)
                    drawList.PopClipRect();
            }
            else
            {
                if (iconFont.HasValue)
                    ImGui.PushFont(iconFont.Value);
                try
                {
                    if (iconNeedsClip)
                        drawList.PushClipRect(new Vector2(min.X, min.Y), new Vector2(iconClipBoundary, max.Y), true);
                    drawList.AddText(iconDrawMin, colorU32, iconText);
                    if (iconNeedsClip)
                        drawList.PopClipRect();
                }
                finally
                {
                    if (iconFont.HasValue)
                        ImGui.PopFont();
                }
            }

            // Right-edge fade only when icon is partially clipped — not when
            // fully outside (nothing to fade) or fully inside (no overflow).
            if (iconNeedsClip && iconPartiallyVisible && rightFadeBackground.HasValue)
            {
                EdgeFade.DrawRight(
                    drawList,
                    new Vector2(min.X, min.Y),
                    iconClipBoundary - min.X,
                    max.Y - min.Y,
                    rightFadeBackground.Value,
                    verticalBorderInset: rightFadeVerticalBorderInset);
            }

            x += iconAreaWidth + gap;
        }

        if (!hasLabel)
            return false;

        var clipMin = new Vector2(x, contentMin.Y);
        var clipMax = contentMax;
        if (clipMax.X <= clipMin.X)
            return true;

        var textY = min.Y + MathF.Max(0f, ((max.Y - min.Y) - textSize.Y) * 0.5f);
        drawList.PushClipRect(clipMin, clipMax, true);
        drawList.AddText(new Vector2(x, textY), colorU32, label);
        drawList.PopClipRect();

        var textAreaWidth = clipMax.X - clipMin.X;
        var degraded = textSize.X > textAreaWidth;
        if (rightFadeBackground.HasValue && degraded)
        {
            EdgeFade.DrawRight(
                drawList,
                clipMin,
                textAreaWidth,
                max.Y - min.Y,
                rightFadeBackground.Value,
                verticalBorderInset: rightFadeVerticalBorderInset);
        }

        return degraded;
    }

    private static Vector4 ResolveColor(IconTextSpec spec)
    {
        if (spec.Color.HasValue)
            return spec.Color.Value;

        return spec.State.HasFlag(ControlState.Disabled)
                   ? SlapColor.WithDisabledAlpha(ThemeScope.Resolved.Body, true)
                   : ThemeScope.Resolved.Body;
    }

    private static float Normalize(float value) => value > 0f && !float.IsInfinity(value) ? value : 1f;
}
