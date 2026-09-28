using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct GameIconStripSpec(
    ControlKey Key,
    IReadOnlyList<GameIconSpec> Items,
    float HeightUnits = 1f,
    ControlState State = ControlState.None,
    bool OverflowFade = true,
    Vector4? FadeBackground = null,
    string? Title = null,
    bool TitleBold = false,
    bool TitleClickable = false,
    Variant TitleVariant = Variant.Flat,
    FontAwesomeIcon TitleIcon = FontAwesomeIcon.None,
    string? EmptyText = null,
    EdgeFadeSpec? EdgeFade = null,
    bool Wrap = false,
    float WrapRowGap = SlapPx.Space4,
    LabeledOutlineSpec? Outline = null
);

internal readonly record struct GameIconSpec(
    ControlKey Key,
    string Label = "",
    uint GameIconId = 0,
    bool HighQualityIcon = false,
    string? Tooltip = null,
    ControlState State = ControlState.None,
    bool IconOnly = false,
    Vector4? TextColor = null,
    LayeredShadowSpec Shadow = default,
    GameIconShape Shape = GameIconShape.Standard,
    Vector4? Tint = null,
    FontAwesomeIcon? OverlayIcon = null,
    Vector4? OverlayColor = null,
    bool CornerCheck = false
);

internal readonly record struct GameIconStripResult(
    int HoveredIndex,
    int ClickedIndex,
    int DoubleClickedIndex,
    int RightClickedIndex,
    bool TitleClicked,
    ControlResult LastResult
);

internal static class GameIconStripComponent
{
    private const float ItemGap = SlapPx.Space4;
    private const float TextPaddingX = SlapPx.Space6;
    private const float IconGap = SlapPx.Space4;

    public static GameIconStripResult Draw(GameIconStripSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "GameIconStripSpec.Key.Value must not be empty.",
                nameof(spec)
            );

        if (spec.Wrap)
            return DrawWrap(spec);

        return DrawSingleRow(spec);
    }

    private static GameIconStripResult DrawWrap(GameIconStripSpec spec)
    {
        ImGui.PushID(spec.Key.Value);
        try
        {
            using var _hover = HoverArbitration.Push();
            var height = MetricsScope.UnitHeight * Normalize(spec.HeightUnits, 1f);
            var width = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            var initialOrigin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var gap = MetricsScope.ScaleGap(ItemGap);
            var rowGap = MetricsScope.ScaleGap(spec.WrapRowGap);

            var hasTitle = !string.IsNullOrEmpty(spec.Title);
            float titleHeight = 0f;
            if (hasTitle)
            {
                using (
                    spec.TitleBold
                        ? TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold)
                        : null
                )
                {
                    titleHeight = ImGui.GetTextLineHeight();
                }
            }

            var itemOriginX = initialOrigin.X;
            var currentY = initialOrigin.Y;

            if (hasTitle)
            {
                var titleY = currentY + MathF.Max(0f, (height - titleHeight) * 0.5f);
                using (
                    spec.TitleBold
                        ? TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold)
                        : null
                )
                {
                    drawList.AddText(
                        new Vector2(initialOrigin.X, titleY),
                        ImGui.ColorConvertFloat4ToU32(ThemeScope.Resolved.Body),
                        spec.Title
                    );
                }
                currentY += height + rowGap;
            }

            var outline = spec.Outline;
            var hasOutline = false;

            // 带标签外描边时，标签骑在分组顶边上、下半部伸进分组内；
            // 预留的顶部空隙让"标签下半部 + 边框"与首行图标保持间距。
            float outlineTopClearance = 0f;
            if (outline is { Label: { } outlineLabel } && !string.IsNullOrWhiteSpace(outlineLabel) && spec.Items.Count > 0)
            {
                hasOutline = true;
                using (Slap.PushFont(SlapFontSize.Small, SlapFontWeight.Bold))
                {
                    var labelSize = ImGui.CalcTextSize(outlineLabel);
                    var offset = MetricsScope.ScalePadding(outline.Value.ResolvedLabelOffset);
                    outlineTopClearance = labelSize.Y * 0.5f
                        + offset.Y
                        + MetricsScope.BorderThickness
                        + MetricsScope.ScaleGap(SlapPx.Space4);
                }
                ImGui.Dummy(new Vector2(0f, outlineTopClearance));
                currentY += outlineTopClearance;
            }
            var outlineMinY = currentY - outlineTopClearance;

            var itemAreaWidth = MathF.Max(0f, width);
            var x = itemOriginX;
            var hoveredIndex = -1;
            var clickedIndex = -1;
            var doubleClickedIndex = -1;
            var rightClickedIndex = -1;
            var lastResult = default(ControlResult);

            // 换行分组轮廓几何：已排满行的右缘固定，末行右缘决定 L 形缺口。
            var groupMaxRight = itemOriginX;
            var lastRowTop = currentY;
            var lastRowRight = itemOriginX;
            var firstIntermediateRight = float.NaN;
            var rowsAllFull = true;
            var rowTop = currentY;

            if (spec.Items.Count == 0 && !string.IsNullOrWhiteSpace(spec.EmptyText))
            {
                var emptyHeight = ImGui.GetTextLineHeight();
                var emptyY = currentY + MathF.Max(0f, (height - emptyHeight) * 0.5f);
                drawList.AddText(
                    new Vector2(x, emptyY),
                    ImGui.ColorConvertFloat4ToU32(ThemeScope.Resolved.Subtle),
                    spec.EmptyText
                );
            }

            for (var i = 0; i < spec.Items.Count; i++)
            {
                var item = spec.Items[i];
                var itemW = ResolveItemWidth(item, height);

                if (x + itemW > itemOriginX + itemAreaWidth && x > itemOriginX)
                {
                    // 完成的一行必须整宽，否则混宽图标下的轮廓不适用 L 形。
                    if (hasOutline)
                    {
                        var completedRight = x - gap;
                        if (float.IsNaN(firstIntermediateRight))
                            firstIntermediateRight = completedRight;
                        else
                            rowsAllFull &= MathF.Abs(firstIntermediateRight - completedRight) <= 0.5f;
                    }

                    x = itemOriginX;
                    currentY += height + rowGap;
                    rowTop = currentY;
                }

                var result = DrawItem(
                    item,
                    spec,
                    i,
                    new Vector2(x, currentY),
                    new Vector2(itemW, height)
                );
                lastResult = result;
                if (result.Hovered)
                    hoveredIndex = i;
                if (result.Clicked)
                    clickedIndex = i;
                if (result.DoubleClicked)
                    doubleClickedIndex = i;
                if (result.RightClicked)
                    rightClickedIndex = i;

                groupMaxRight = MathF.Max(groupMaxRight, x + itemW);
                lastRowTop = rowTop;
                lastRowRight = x + itemW;
                x += itemW + gap;
            }

            if (hasOutline && outline is { } outlineSpec)
            {
                LabeledOutlineDrawing.DrawWrappedGroupOverlay(
                    drawList,
                    new Vector2(itemOriginX, outlineMinY),
                    groupMaxRight,
                    lastRowTop,
                    rowsAllFull ? lastRowRight : groupMaxRight,
                    lastRowTop + height,
                    outlineSpec,
                    SlapCorners.ControlRadius
                );
            }

            var totalHeight = currentY + height - initialOrigin.Y;
            ImGui.SetCursorScreenPos(initialOrigin + new Vector2(0f, totalHeight));
            return new GameIconStripResult(
                hoveredIndex,
                clickedIndex,
                doubleClickedIndex,
                rightClickedIndex,
                false,
                lastResult
            );
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static GameIconStripResult DrawSingleRow(GameIconStripSpec spec)
    {
        ImGui.PushID(spec.Key.Value);
        try
        {
            using var _hover = HoverArbitration.Push();
            var height = MetricsScope.UnitHeight * Normalize(spec.HeightUnits, 1f);
            var width = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            var initialOrigin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var gap = MetricsScope.ScaleGap(ItemGap);

            var hasTitle = !string.IsNullOrEmpty(spec.Title);
            float titleWidth = 0f;
            float titleHeight = 0f;
            var titleGap = hasTitle ? MetricsScope.ScaleGap(SlapPx.Space6) : 0f;
            if (hasTitle)
            {
                using (
                    spec.TitleBold
                        ? TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold)
                        : null
                )
                {
                    titleWidth = ImGui.CalcTextSize(spec.Title).X;
                    titleHeight = ImGui.GetTextLineHeight();
                }
            }

            var titleClicked = false;

            if (hasTitle && spec.TitleClickable)
            {
                var disabled = spec.State.HasFlag(ControlState.Disabled);
                var titlePadding = MetricsScope.ScalePadding(TextPaddingX * 2f);

                // Resolve icon
                var hasIcon = spec.TitleIcon != FontAwesomeIcon.None;
                float iconWidth = 0f;
                string? iconText = null;
                ImFontPtr? iconFont = null;
                if (hasIcon)
                {
                    (iconText, iconFont) = SlapIcon.Resolve(spec.TitleIcon);
                    if (string.IsNullOrEmpty(iconText))
                        hasIcon = false;
                    else
                        iconWidth = ImGui.CalcTextSize(iconText).X;
                }

                var iconGap = hasIcon ? MetricsScope.ScaleGap(IconGap) : 0f;
                var titleBtnWidth = titlePadding + iconWidth + iconGap + titleWidth;

                ImGui.SetCursorScreenPos(initialOrigin);
                var rawClicked = ImGui.InvisibleButton(
                    "##titlebtn",
                    new Vector2(titleBtnWidth, height)
                );
                var titleHovered = HoverArbitration.Current.CaptureHover(
                    ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)
                );
                var titleActive = !disabled && titleHovered && ImGui.IsItemActive();
                titleClicked = !disabled && titleHovered && rawClicked;

                var rounding = SlapCorners.ControlRadius;
                var rt = ThemeScope.Resolved;
                var palette = rt.GetButtonPalette(spec.TitleVariant);
                var colors = SurfaceComponent.ResolveControlColors(
                    palette,
                    disabled ? ControlState.Disabled : ControlState.None,
                    hovered: titleHovered && !disabled,
                    active: titleActive && !disabled,
                    strength: SurfaceComponent.ResolveBorderStrength(spec.TitleVariant),
                    variant: spec.TitleVariant);
                var bg = colors.Background;
                var btnMin = initialOrigin;
                var btnMax = btnMin + new Vector2(titleBtnWidth, height);
                drawList.AddRectFilled(btnMin, btnMax, ImGui.ColorConvertFloat4ToU32(bg), rounding);
                SurfaceComponent.DrawControlBorder(
                    drawList,
                    btnMin,
                    btnMax,
                    spec.TitleVariant,
                    colors.Border,
                    rounding);

                var textColor = ImGui.ColorConvertFloat4ToU32(colors.Text);
                var drawX = initialOrigin.X + MetricsScope.ScalePadding(TextPaddingX);

                if (hasIcon && iconText != null)
                {
                    if (iconFont.HasValue)
                        ImGui.PushFont(iconFont.Value);
                    try
                    {
                        var iconSize = ImGui.CalcTextSize(iconText);
                        var iconPos = new Vector2(
                            drawX,
                            initialOrigin.Y + (height - iconSize.Y) * 0.5f
                        );
                        drawList.AddText(iconPos, textColor, iconText);
                    }
                    finally
                    {
                        if (iconFont.HasValue)
                            ImGui.PopFont();
                    }
                    drawX += iconWidth + iconGap;
                }

                var titleY = initialOrigin.Y + MathF.Max(0f, (height - titleHeight) * 0.5f);
                using (
                    spec.TitleBold
                        ? TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold)
                        : null
                )
                {
                    drawList.AddText(new Vector2(drawX, titleY), textColor, spec.Title);
                }

                titleWidth = titleBtnWidth;
            }
            else if (hasTitle)
            {
                var titleY = initialOrigin.Y + MathF.Max(0f, (height - titleHeight) * 0.5f);
                using (
                    spec.TitleBold
                        ? TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold)
                        : null
                )
                {
                    drawList.AddText(
                        new Vector2(initialOrigin.X, titleY),
                        ImGui.ColorConvertFloat4ToU32(ThemeScope.Resolved.Body),
                        spec.Title
                    );
                }
            }

            var itemContentWidth = MathF.Max(0f, width - titleWidth - titleGap);
            var fullWidth = ResolveStripWidth(spec.Items, height, gap);
            var overflows = fullWidth > itemContentWidth;

            var itemOrigin = new Vector2(initialOrigin.X + titleWidth + titleGap, initialOrigin.Y);
            var x = itemOrigin.X;

            var clipPushed = false;
            var hoveredIndex = -1;
            var clickedIndex = -1;
            var doubleClickedIndex = -1;
            var rightClickedIndex = -1;
            var lastResult = default(ControlResult);

            if (spec.OverflowFade)
            {
                var shadowPad = MetricsScope.Scale(SlapPx.Space2);
                var clipMaxX = itemOrigin.X + itemContentWidth + (overflows ? 0f : shadowPad);
                drawList.PushClipRect(
                    new Vector2(itemOrigin.X - shadowPad, itemOrigin.Y - shadowPad),
                    new Vector2(clipMaxX, itemOrigin.Y + height + shadowPad),
                    true
                );
                clipPushed = true;
            }

            try
            {
                if (spec.Items.Count == 0 && !string.IsNullOrWhiteSpace(spec.EmptyText))
                {
                    var emptyHeight = ImGui.GetTextLineHeight();
                    var emptyY = itemOrigin.Y + MathF.Max(0f, (height - emptyHeight) * 0.5f);
                    drawList.AddText(
                        new Vector2(x, emptyY),
                        ImGui.ColorConvertFloat4ToU32(ThemeScope.Resolved.Subtle),
                        spec.EmptyText
                    );
                }

                for (var i = 0; i < spec.Items.Count; i++)
                {
                    var item = spec.Items[i];
                    var itemWidth = ResolveItemWidth(item, height);
                    if (!spec.OverflowFade && x + itemWidth > itemOrigin.X + itemContentWidth)
                        break;
                    if (x >= itemOrigin.X + itemContentWidth)
                        break;

                    var result = DrawItem(
                        item,
                        spec,
                        i,
                        new Vector2(x, itemOrigin.Y),
                        new Vector2(itemWidth, height)
                    );
                    lastResult = result;
                if (result.Hovered)
                    hoveredIndex = i;
                if (result.Clicked)
                    clickedIndex = i;
                if (result.DoubleClicked)
                    doubleClickedIndex = i;
                if (result.RightClicked)
                    rightClickedIndex = i;

                x += itemWidth + gap;
                }
            }
            finally
            {
                if (clipPushed)
                    drawList.PopClipRect();
            }

            if (spec.OverflowFade && overflows)
            {
                var fadeSpec =
                    spec.EdgeFade
                    ?? new EdgeFadeSpec(
                        Background: spec.FadeBackground ?? ThemeScope.Resolved.Surface,
                        VerticalInset: MetricsScope.BorderThickness
                    );
                if (fadeSpec.IsEnabled)
                    EdgeFade.DrawRight(drawList, itemOrigin, itemContentWidth, height, fadeSpec);
            }

            // Reset cursor to just below the chip line. The InvisibleButton calls
            // in DrawItem already advanced the cursor past height + ItemSpacing.Y;
            // using Dummy(height) on top would double the line height. A
            // zero-height Dummy at the correct position yields exactly one
            // ItemSpacing.Y gap for the next widget.
            ImGui.SetCursorScreenPos(initialOrigin + new Vector2(0f, height));
            ImGui.Dummy(Vector2.Zero);
            return new GameIconStripResult(
                hoveredIndex,
                clickedIndex,
                doubleClickedIndex,
                rightClickedIndex,
                titleClicked,
                lastResult
            );
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>
    /// Draw a single-row icon strip at absolute draw-list coordinates.
    /// No ImGui interaction (invisible buttons, hover, or click tracking).
    /// Used by callers that manage their own coordinate space (e.g. SurfaceList rows).
    /// </summary>
    public static void DrawAbsolute(
        GameIconStripSpec spec,
        ImDrawListPtr drawList,
        Vector2 origin,
        float width,
        float? height = null,
        ControlVisualMode visualMode = ControlVisualMode.Base)
    {
        var h = height ?? MetricsScope.UnitHeight * Normalize(spec.HeightUnits, 1f);
        if (width <= 0f || h <= 0f)
            return;

        var gap = MetricsScope.ScaleGap(ItemGap);
        var itemContentWidth = width;

        var fullWidth = ResolveStripWidth(spec.Items, h, gap);
        var overflows = fullWidth > itemContentWidth;

        if (spec.OverflowFade)
        {
            // Expand the clip rect by the icon shadow padding so the embossed
            // shadow drawn by GameIconComponent (which extends slightly beyond
            // the icon slot) is not clipped. intersectWithCurrent=true ensures
            // the expansion never exceeds the host's existing clip rect. When
            // overflowing, the right edge is hard-clipped to the content width
            // so the right fade's solid edge lines up with the clipped boundary.
            var shadowPad = MetricsScope.Scale(SlapPx.Space2);
            var clipMaxX = origin.X + itemContentWidth + (overflows ? 0f : shadowPad);
            drawList.PushClipRect(
                new Vector2(origin.X - shadowPad, origin.Y - shadowPad),
                new Vector2(clipMaxX, origin.Y + h + shadowPad),
                true
            );
        }

        try
        {
            var stripVisualMode = spec.State.HasFlag(ControlState.Disabled)
                ? ControlVisualMode.Disabled
                : visualMode;
            var x = origin.X;
            for (var i = 0; i < spec.Items.Count; i++)
            {
                var item = spec.Items[i];
                var itemWidth = ResolveItemWidth(item, h);
                if (x >= origin.X + itemContentWidth)
                    break;

                var itemMin = new Vector2(x, origin.Y);
                var itemMax = itemMin + new Vector2(itemWidth, h);
                var textColor = ImGui.ColorConvertFloat4ToU32(
                    item.TextColor ?? ThemeScope.Resolved.Body);
                DrawItemVisual(drawList, item, itemMin, itemMax, textColor, stripVisualMode);
                x += itemWidth + gap;
            }
        }
        finally
        {
            if (spec.OverflowFade)
                drawList.PopClipRect();
        }

        if (spec.OverflowFade && overflows)
        {
            var fadeSpec =
                spec.EdgeFade
                ?? new EdgeFadeSpec(
                    Background: spec.FadeBackground ?? ThemeScope.Resolved.Surface,
                    VerticalInset: MetricsScope.BorderThickness
                );
            if (fadeSpec.IsEnabled)
                EdgeFade.DrawRight(drawList, origin, itemContentWidth, h, fadeSpec);
        }
    }

    /// <summary>Standalone game icon button with hit testing.</summary>
    public static ControlResult DrawGameIconButton(GameIconSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("GameIconSpec.Key.Value must not be empty.", nameof(spec));

        using var _hover = HoverArbitration.Push();
        ImGui.PushID(spec.Key.Value);
        try
        {
            var size = MetricsScope.UnitHeight;
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(size, size);

            var drawList = ImGui.GetWindowDrawList();
            var rounding = GameIconComponent.ResolveRounding(min, max, spec.Shape);
            LayeredShadow.Draw(drawList, min, max, rounding, spec.Shadow);

            var rawClicked = ImGui.InvisibleButton("##icon", new Vector2(size, size));
            var itemMin = ImGui.GetItemRectMin();
            var itemMax = itemMin + new Vector2(size, size);
            var ix = SlapInteraction.Capture(spec.State, rawClicked);

            if (spec.GameIconId != 0)
            {
                GameIconComponent.DrawInSlot(
                    ImGui.GetWindowDrawList(),
                    spec.GameIconId,
                    itemMin,
                    itemMax,
                    spec.HighQualityIcon,
                    shape: spec.Shape,
                    tint: spec.Tint,
                    overlayIcon: spec.OverlayIcon,
                    overlayColor: spec.OverlayColor,
                    visualMode: ix.VisualMode,
                    cornerCheck: spec.CornerCheck
                );
            }

            HoverArbitration.Current.TryShowTooltip(ix.Hovered, spec.Tooltip);
            return ix.ToControlResult(itemMin, itemMax);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static bool DrawItemVisual(
        ImDrawListPtr drawList,
        GameIconSpec item,
        Vector2 itemMin,
        Vector2 itemMax,
        uint textColorU32,
        ControlVisualMode visualMode)
    {
        var resolvedVisualMode = item.State.HasFlag(ControlState.Disabled)
            ? ControlVisualMode.Disabled
            : visualMode;

        if (item.IconOnly && item.GameIconId != 0)
        {
            GameIconComponent.DrawInSlot(
                drawList,
                item.GameIconId,
                itemMin,
                itemMax,
                item.HighQualityIcon,
                shape: item.Shape,
                tint: item.Tint,
                overlayIcon: item.OverlayIcon,
                overlayColor: item.OverlayColor,
                visualMode: resolvedVisualMode,
                cornerCheck: item.CornerCheck
            );
            return false;
        }
        else
        {
            var sizeY = itemMax.Y - itemMin.Y;
            var x = itemMin.X + MetricsScope.ScalePadding(TextPaddingX);
            if (item.GameIconId != 0)
            {
                var iconSize = MathF.Max(0f, sizeY - MetricsScope.ScalePadding(SlapPx.Space4));
                var iconMin = new Vector2(x, itemMin.Y + (sizeY - iconSize) * 0.5f);
                GameIconComponent.Draw(
                    drawList,
                    item.GameIconId,
                    iconMin,
                    iconMin + new Vector2(iconSize),
                    item.HighQualityIcon,
                    shape: item.Shape,
                    tint: item.Tint,
                    overlayIcon: item.OverlayIcon,
                    overlayColor: item.OverlayColor,
                    visualMode: resolvedVisualMode,
                    cornerCheck: item.CornerCheck
                );
                x += iconSize + MetricsScope.ScaleGap(IconGap);
            }

            var degraded = false;
            if (!string.IsNullOrEmpty(item.Label))
            {
                if (x < itemMax.X)
                {
                    var textSize = ImGui.CalcTextSize(item.Label);
                    var textY = itemMin.Y + MathF.Max(0f, (sizeY - textSize.Y) * 0.5f);
                    var clipMin = new Vector2(x, itemMin.Y);
                    var clipMax = new Vector2(
                        itemMax.X - MetricsScope.ScalePadding(TextPaddingX),
                        itemMax.Y
                    );
                    drawList.PushClipRect(clipMin, clipMax, true);
                    drawList.AddText(new Vector2(x, textY), textColorU32, item.Label);
                    drawList.PopClipRect();
                    degraded = textSize.X > clipMax.X - x;
                }
                else
                {
                    degraded = true;
                }
            }

            return degraded;
        }
    }

    private static ControlResult DrawItem(
        GameIconSpec item,
        GameIconStripSpec strip,
        int index,
        Vector2 min,
        Vector2 size
    )
    {
        ImGui.SetCursorScreenPos(min);
        var disabled =
            strip.State.HasFlag(ControlState.Disabled) || item.State.HasFlag(ControlState.Disabled);
        var selected = item.State.HasFlag(ControlState.Selected);
        var rawClicked = ImGui.InvisibleButton($"##gameicon{index}", size);
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = itemMin + size;
        var ix = SlapInteraction.Capture(disabled, selected, rawClicked, captureRightClick: true);
        var drawList = ImGui.GetWindowDrawList();
        var textColor = ResolveTextColor(item.TextColor, ix.Disabled, ix.Selected);

        var itemDegraded = DrawItemVisual(
            drawList,
            item,
            itemMin,
            itemMax,
            ImGui.ColorConvertFloat4ToU32(textColor),
            ix.VisualMode);

        var itemTooltip = ResponsiveTooltip.Compose(itemDegraded, item.Label, item.Tooltip);
        HoverArbitration.Current.TryShowTooltip(ix.Hovered, itemTooltip);
        return ix.ToControlResult(itemMin, itemMax);
    }

    private static float ResolveStripWidth(
        IReadOnlyList<GameIconSpec> items,
        float height,
        float gap
    )
    {
        if (items.Count == 0)
            return 0f;

        var width = 0f;
        for (var i = 0; i < items.Count; i++)
            width += ResolveItemWidth(items[i], height);

        return width + gap * (items.Count - 1);
    }

    private static float ResolveItemWidth(GameIconSpec item, float height)
    {
        if (item.IconOnly && item.GameIconId != 0)
            return height;

        var width = MetricsScope.ScalePadding(TextPaddingX * 2f);
        if (item.GameIconId != 0)
            width +=
                MathF.Max(0f, height - MetricsScope.ScalePadding(SlapPx.Space4))
                + MetricsScope.ScaleGap(IconGap);
        if (!string.IsNullOrEmpty(item.Label))
            width += ImGui.CalcTextSize(item.Label).X;
        return width;
    }

    private static Vector4 ResolveTextColor(Vector4? overrideColor, bool disabled, bool selected)
    {
        var color = overrideColor.HasValue
            ? overrideColor.Value
            : selected
                ? ThemeScope.Resolved.Emphasis
                : ThemeScope.Resolved.Body;
        return SlapColor.WithDisabledAlpha(color, disabled);
    }

    private static float Normalize(float value, float fallback) =>
        value > 0f && !float.IsInfinity(value) ? value : fallback;
}
