using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct TabStripSpec(
    ControlKey Key,
    IReadOnlyList<TabItemSpec> Tabs,
    int SelectedIndex = 0,
    float WidthUnits = 0f,
    Variant Variant = Variant.Tab,
    ControlState State = ControlState.None,
    FontAwesomeIcon? OverflowIcon = null,
    /// <summary>
    /// Resolved strip width in scaled pixels, set by an external layout system
    /// (e.g. <see cref="ResponsiveSlotGroup"/>). When &gt; 0, bypasses
    /// <see cref="WidthUnits"/> and the content-region fallback. Narrower than
    /// the tabs' natural width, the strip degrades to visible tabs + overflow.
    /// </summary>
    float ResolvedWidth = 0f
);

internal readonly record struct TabItemSpec(
    string Label,
    FontAwesomeIcon? Icon = null,
    string? Tooltip = null,
    ControlState State = ControlState.None
);

internal readonly record struct TabStripResult(
    int SelectedIndex,
    int ClickedIndex,
    bool Changed,
    ControlResult LastResult);

internal static class TabStripComponent
{
    // V2 layout: UnitWidth=70, UnitHeight=38
    // MinTabWidth  = UnitWidth*1.05 = 73.5px
    // MaxTabWidth  = UnitWidth*2.2  = 154px
    // StripHeight  = UnitHeight      = 38px
    // ButtonHeight = UnitHeight      = 38px
    // TabGap       = 4px
    // OverflowWidth= UnitHeight      = 38px
    // TextPaddingX = 16 (Px.Space16)
    private const float MinTabWidthUnits = 1.05f;
    private const float MaxTabWidthUnits = 2.2f;
    private const float StripHeight = 1.0f;
    private const float ButtonHeight = 1.0f;
    private const float TabGap = SlapPx.Space4;
    private const float OverflowWidth = 1.0f;
    private const float TextPaddingX = SlapPx.Space16;
    private const float IconLeftPad = SlapPx.Space4;
    private const float IconGap = 0f;
    private const float IconOnlyPaddingX = 0f;

    /// <summary>
    /// Natural strip height in scaled pixels. Zone allocators should call this
    /// instead of guessing, so that internal layout changes propagate automatically.
    /// </summary>
    public static float ResolveStripHeight() => MetricsScope.UnitHeight * StripHeight;

    /// <summary>
    /// Estimate the total natural width of all tabs (including inter-tab gaps).
    /// Use this to reserve space for the tab strip before calling <see cref="Draw"/>,
    /// e.g. when calculating how much width to give a right slot.
    /// </summary>
    public static float EstimateTabsWidth(IReadOnlyList<TabItemSpec> tabs)
    {
        if (tabs.Count == 0)
            return 0f;
        var gap = MetricsScope.ScaleGap(TabGap);
        return ResolveTabsWidth(tabs, tabs.Count, gap);
    }

    public static TabStripResult Draw(TabStripSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("TabStripSpec.Key.Value must not be empty.", nameof(spec));
        if (spec.Tabs.Count == 0)
            return new TabStripResult(-1, -1, false, default);

        ImGui.PushID(spec.Key.Value);
        try
        {
            var unit = MetricsScope.UnitHeight;
            var stripHeight = unit * StripHeight;
            var buttonHeight = MathF.Min(stripHeight, unit * ButtonHeight);
            var gap = MetricsScope.ScaleGap(TabGap);
            var totalWidth = ResolveWidth(spec);

            var start = ImGui.GetCursorScreenPos();
            var selectedIndex = Math.Clamp(spec.SelectedIndex, 0, spec.Tabs.Count - 1);
            var disabled = spec.State.HasFlag(ControlState.Disabled);
            var layout = ResolveLayout(spec.Tabs, totalWidth, gap, unit);
            var fullCount = layout.FullCount;
            var hasDegraded = layout.DegradedWidth > 0f;
            var visibleCount = fullCount + (hasDegraded ? 1 : 0);
            var hasOverflow = layout.HasOverflow;
            var overflowWidth = hasOverflow ? MathF.Min(totalWidth, unit * OverflowWidth) : 0f;

            using var _hover = HoverArbitration.Push();
            var nextSelected = selectedIndex;
            var clickedIndex = -1;
            var lastResult = default(ControlResult);
            var y = start.Y + MathF.Max(0f, (stripHeight - buttonHeight) * 0.5f);
            var x = start.X;
            for (var i = 0; i < visibleCount; i++)
            {
                var tabWidth = hasDegraded && i == fullCount
                    ? layout.DegradedWidth
                    : ResolveTabWidth(spec.Tabs[i]);
                var result = DrawTabButton(
                    spec,
                    spec.Tabs[i],
                    i,
                    selectedIndex == i,
                    disabled,
                    new Vector2(x, y),
                    new Vector2(tabWidth, buttonHeight)
                );
                lastResult = result;
                if (result.Clicked)
                {
                    nextSelected = i;
                    clickedIndex = i;
                }

                x += tabWidth + gap;
            }

            if (hasOverflow)
            {
                var overflowPos = new Vector2(
                    start.X + totalWidth - overflowWidth,
                    y
                );
                var result = DrawOverflowButton(
                    spec,
                    selectedIndex >= visibleCount,
                    disabled,
                    overflowPos,
                    new Vector2(overflowWidth, buttonHeight)
                );
                lastResult = result;
                if (result.Clicked)
                    ImGui.OpenPopup("##overflowPopup");

                DrawOverflowPopup(
                    spec,
                    visibleCount,
                    selectedIndex,
                    ref nextSelected,
                    ref clickedIndex,
                    overflowPos,
                    overflowWidth
                );
            }

            // V2: outline uses ButtonBorderColor = raw Border token, with fade animation
            HoverArbitration.Current.FlushOutline(spec.Key, 1f);
            ImGui.SetCursorScreenPos(start + new Vector2(0f, stripHeight));

            return new TabStripResult(
                nextSelected,
                clickedIndex,
                nextSelected != selectedIndex,
                lastResult);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>
    /// Responsive width range for embedding the strip in a responsive slot:
    /// preferred is all tabs at natural width; the structural minimum is the
    /// overflow square so the strip degrades to its overflow popup instead of
    /// disappearing under pressure.
    /// </summary>
    public static ResponsiveWidthRange MeasureResponsiveWidth(TabStripSpec spec)
    {
        var preferred = EstimateTabsWidth(spec.Tabs);
        return ResponsiveWidthRange.Create(preferred, 0f, MetricsScope.UnitHeight);
    }

    private static float ResolveWidth(TabStripSpec spec)
    {
        if (spec.ResolvedWidth > 0f && !float.IsInfinity(spec.ResolvedWidth))
            return spec.ResolvedWidth;
        if (spec.WidthUnits > 0f && !float.IsInfinity(spec.WidthUnits))
            return MetricsScope.UnitWidth * spec.WidthUnits;

        return MathF.Max(MetricsScope.UnitHeight, ImGui.GetContentRegionAvail().X);
    }

    private readonly record struct TabStripLayout(
        int FullCount,
        float DegradedWidth,
        bool HasOverflow);

    private static TabStripLayout ResolveLayout(
        IReadOnlyList<TabItemSpec> tabs,
        float totalWidth,
        float gap,
        float unit)
    {
        var count = tabs.Count;
        if (count == 0)
            return new TabStripLayout(0, 0f, false);

        var fullWidth = ResolveTabsWidth(tabs, count, gap);

        if (fullWidth <= totalWidth)
            return new TabStripLayout(count, 0f, false);

        var overflowWidth = MathF.Min(totalWidth, unit * OverflowWidth);
        var tabsLimit = MathF.Max(0f, totalWidth - overflowWidth - gap);

        var fullCount = 0;
        var fullUsed = 0f;
        for (var i = 0; i < count; i++)
        {
            var tabWidth = ResolveTabWidth(tabs[i]);
            var next = fullCount == 0 ? tabWidth : fullUsed + gap + tabWidth;
            if (next > tabsLimit)
                break;
            fullUsed = next;
            fullCount++;
        }

        var degradedWidth = 0f;
        if (fullCount < count && tabs[fullCount].Icon.HasValue)
        {
            var remaining = tabsLimit - fullUsed - (fullCount > 0 ? gap : 0f);
            if (remaining >= unit)
                degradedWidth = remaining;
        }

        return new TabStripLayout(fullCount, degradedWidth, true);
    }

    private static float ResolveTabsWidth(IReadOnlyList<TabItemSpec> tabs, int count, float gap)
    {
        count = Math.Clamp(count, 0, tabs.Count);
        if (count == 0)
            return 0f;

        var width = 0f;
        for (var i = 0; i < count; i++)
            width += ResolveTabWidth(tabs[i]);

        return width + gap * (count - 1);
    }

    private static float ResolveTabWidth(TabItemSpec tab)
    {
        var unitW = MetricsScope.UnitWidth;
        return MathF.Min(
            unitW * MaxTabWidthUnits,
            MathF.Max(unitW * MinTabWidthUnits, ResolveUnclampedTabWidth(tab)));
    }

    private static float ResolveUnclampedTabWidth(TabItemSpec tab)
    {
        var totalPad = tab.Icon.HasValue
            ? MetricsScope.ScalePadding(IconLeftPad) + MetricsScope.ScalePadding(TextPaddingX)  // left=4, right=16
            : MetricsScope.ScalePadding(TextPaddingX * 2f);  // symmetric 16
        float textWidth;
        using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold))
            textWidth = ImGui.CalcTextSize(tab.Label).X + totalPad;
        if (tab.Icon.HasValue)
        {
            // Use icon slot width (UnitHeight) for measurement, matching
            // DrawInRect's auto-derived slot for centered content.
            textWidth += MetricsScope.ResolveIconSlotWidth(true) + MetricsScope.ScaleGap(IconGap);
        }

        return textWidth;
    }

    private static ControlResult DrawTabButton(
        TabStripSpec spec,
        TabItemSpec tab,
        int index,
        bool selected,
        bool stripDisabled,
        Vector2 min,
        Vector2 size
    )
    {
        var disabled = stripDisabled || tab.State.HasFlag(ControlState.Disabled);
        ImGui.SetCursorScreenPos(min);
        var rawClicked = ImGui.InvisibleButton($"##tab{index}", size);
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var ix = SlapInteraction.Capture(disabled, selected, rawClicked);
        var drawList = ImGui.GetWindowDrawList();
        var rounding = SlapCorners.ControlRadius;
        var colors = ResolveTabColors(
            spec.Variant,
            selected,
            ix.Hovered && !ix.Disabled,
            ix.Active,
            ix.Disabled
        );

        if (colors.Background.W > 0f)
            drawList.AddRectFilled(
                itemMin,
                itemMax,
                ImGui.ColorConvertFloat4ToU32(colors.Background),
                rounding
            );

        if (selected && ThemeScope.Resolved.IsTransparentTheme)
        {
            SurfaceComponent.DrawControlBorder(
                drawList,
                itemMin,
                itemMax,
                spec.Variant,
                SlapColor.WithDisabledAlpha(ThemeScope.Resolved.Border, disabled),
                rounding);
        }

        using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold))
            DrawTabContent(drawList, tab, itemMin, itemMax, colors.Text, colors.Background);
        var tabTooltip = ResponsiveTooltip.Compose(
            size.X + 0.5f < ResolveUnclampedTabWidth(tab),
            tab.Label,
            tab.Tooltip);
        HoverArbitration.Current.TryShowTooltip(ix.Hovered, tabTooltip);
        HoverArbitration.Current.CaptureOutline(
            itemMin,
            itemMax,
            ix.Hovered && !selected && !ix.Disabled,
            selected: false,
            accent: ThemeScope.Resolved.Border,
            rounding: rounding
        );
        return ix.ToControlResult(itemMin, itemMax);
    }

    private static ControlResult DrawOverflowButton(
        TabStripSpec spec,
        bool selected,
        bool disabled,
        Vector2 min,
        Vector2 size
    )
    {
        ImGui.SetCursorScreenPos(min);
        var rawClicked = ImGui.InvisibleButton("##overflow", size);
        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        var ix = SlapInteraction.Capture(disabled, selected, rawClicked);
        var colors = ResolveTabColors(
            spec.Variant,
            selected,
            ix.Hovered && !ix.Disabled,
            ix.Active,
            ix.Disabled
        );
        var drawList = ImGui.GetWindowDrawList();
        var rounding = SlapCorners.ControlRadius;

        if (colors.Background.W > 0f)
            drawList.AddRectFilled(
                itemMin,
                itemMax,
                ImGui.ColorConvertFloat4ToU32(colors.Background),
                rounding
            );

        // Use custom icon if provided, otherwise fall back to "...".
        var (overflowIconText, overflowIconFont) = SlapIcon.Resolve(spec.OverflowIcon);
        var iconText = overflowIconText ?? "...";
        var iconFont = overflowIconFont;
        if (iconFont.HasValue)
            ImGui.PushFont(iconFont.Value);
        try
        {
            var textSize = ImGui.CalcTextSize(iconText);
            var textPos = itemMin + (size - textSize) * 0.5f;
            drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(colors.Text), iconText);
        }
        finally
        {
            if (iconFont.HasValue)
                ImGui.PopFont();
        }
        HoverArbitration.Current.CaptureOutline(
            itemMin,
            itemMax,
            ix.Hovered && !selected && !ix.Disabled,
            selected: false,
            accent: ThemeScope.Resolved.Border,
            rounding: rounding
        );
        return ix.ToControlResult(itemMin, itemMax);
    }

    private static void DrawOverflowPopup(
        TabStripSpec spec,
        int firstOverflowIndex,
        int selectedIndex,
        ref int nextSelected,
        ref int clickedIndex,
        Vector2 buttonMin,
        float buttonWidth
    )
    {
        if (firstOverflowIndex >= spec.Tabs.Count)
            return;

	        ImGui.SetNextWindowPos(buttonMin + new Vector2(0f, MetricsScope.UnitHeight * ButtonHeight));

	        var rt = ThemeScope.Resolved;
	        var measureItems = spec.Tabs
	            .Skip(firstOverflowIndex)
	            .Select((tab, offset) => new PopupMeasureItem(
	                tab.Label ?? string.Empty,
	                HasLeadingIcon: tab.Icon.HasValue,
	                HasTrailingIcon: false,
	                IsSelected: firstOverflowIndex + offset == selectedIndex))
	            .ToList();
	        using var popup = PopupPanelComponent.Begin(
            new PopupPanelSpec(
                "##overflowPopup",
	                Variant: spec.Variant,
	                Palette: rt.FlatPalette,
	                MinWidthUnits: 1f,
	                MeasureItems: measureItems
            )
        );
        if (!popup.IsOpen)
            return;

        for (var i = firstOverflowIndex; i < spec.Tabs.Count; i++)
        {
            var tab = spec.Tabs[i];
            var isSelected = i == selectedIndex;
            var state = isSelected ? ControlState.Selected : ControlState.None;
            state |= tab.State;
            var result = popup.Item(
                new PopupItemSpec(
                    $"overflowItem{i}",
                    tab.Label,
                    Icon: tab.Icon,
                    spec.Variant,
                    state,
                    tab.Tooltip,
	                    Semantic: null
                )
            );
            if (result.Clicked)
            {
                nextSelected = i;
                clickedIndex = i;
                ImGui.CloseCurrentPopup();
            }

            if (i == selectedIndex)
                ImGui.SetItemDefaultFocus();
        }
    }

    private static void DrawTabContent(
        ImDrawListPtr drawList,
        TabItemSpec tab,
        Vector2 min,
        Vector2 max,
        Vector4 textColor,
        Vector4 fadeBackground
    )
    {
        var (iconText, iconFont) = SlapIcon.Resolve(tab.Icon);
        var hasIcon = tab.Icon.HasValue;
        var isIconOnly = hasIcon && max.X - min.X <= MetricsScope.UnitHeight + 0.5f;
        var visibleLabel = isIconOnly ? string.Empty : tab.Label;
        var padding = isIconOnly ? IconOnlyPaddingX : TextPaddingX;
        IconTextComponent.DrawInRect(
            drawList,
            min,
            max,
            visibleLabel,
            iconText,
            iconFont,
            textColor,
            fadeBackground,
            iconTextGap: IconGap,
            horizontalPadding: padding,
            leftPadding: hasIcon && !isIconOnly ? IconLeftPad : null,
            centerContent: true,
            progressiveCenter: true
        );
    }

    private static TabColors ResolveTabColors(
        Variant variant,
        bool selected,
        bool hovered,
        bool active,
        bool disabled
    )
    {
        var rt = ThemeScope.Resolved;
        if (selected)
        {
            var palette = rt.GetButtonPalette(variant);
            var bg = disabled ? palette.Disabled : palette.Base;
            // 当前 tab 故意不响应 hover 变色，保持 base 色不动（Design Intent）
            // 如需恢复选中 tab 的 hover 效果，取消注释下面一行并删除本注释
            // if (hovered && !disabled) bg = palette.Hovered;
            if (active && !disabled)
                bg = palette.Active;
            var text = SlapColor.WithDisabledAlpha(palette.Text, disabled);
            return new TabColors(bg, text);
        }

        // Unselected tabs are transparent-on-surface items; hover lightens and
        // active darkens via the shared flat surface background.
        var unselectedBackground = SurfaceComponent.ResolveFlatSurfaceBackground(hovered, active, disabled);
        var unselectedText = SlapColor.WithDisabledAlpha(rt.Subtle, disabled);
        return new TabColors(unselectedBackground, unselectedText);
    }

    private readonly record struct TabColors(Vector4 Background, Vector4 Text);
}
