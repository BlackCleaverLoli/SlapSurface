using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>Specification for a detail page with a scroll-collapsing floating header.</summary>
internal readonly record struct CollapsingHeaderDetailPageSpec(
    ControlKey Key,
    string Title,
    bool ResetScroll = false,
    string? Summary = null,
    float MediaTextGap = SlapPx.Space12,
    float IdentityActionGap = SlapPx.Space12,
    float HeaderContentGap = SlapPx.Space12,
    /// <summary>
    /// Optional replacement for the static title text. When set, the page draws
    /// the title row's left content (e.g. an inline input) inside the provided
    /// zone; <c>collectTitleControls</c> still render in the right slot.
    /// </summary>
    Action<ContentZoneContext>? DrawTitleContent = null,
    /// <summary>
    /// Optional replacement for the static summary line. When set, the page
    /// draws the full summary row content (the <paramref name="Summary"/> text
    /// is not rendered) inside the provided zone.
    /// </summary>
    Action<ContentZoneContext>? DrawSummaryContent = null,
    /// <summary>Font size for the static summary line.</summary>
    SlapFontSize SummaryFontSize = SlapFontSize.Regular,
    /// <summary>
    /// When true, the header always renders in its collapsed state and ignores
    /// the scroll position; content starts directly below the collapsed header.
    /// </summary>
    bool ForceCollapsed = false
);

/// <summary>Resolved media slot and interaction state for a collapsing detail header.</summary>
internal readonly record struct CollapsingHeaderDetailMediaContext(
    Vector2 Min,
    Vector2 Max,
    float CollapseProgress,
    ControlResult Interaction,
    ControlVisualMode VisualMode
);

internal static class CollapsingHeaderDetailPageComponent
{
    internal static void Draw(
        ContentCardScope card,
        CollapsingHeaderDetailPageSpec spec,
        Action<CollapsingHeaderDetailMediaContext> drawMedia,
        Action<ResponsiveSlotGroup>? collectTitleControls,
        Action<ResponsiveRowBuilder>? collectActions,
        Action<ContentZoneContext> drawContent,
        FloatingCommandSlot? floatingCommand)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("CollapsingHeaderDetailPageSpec.Key.Value must not be empty.", nameof(spec));

        var height = MathF.Max(0f, card.RemainingHeight);
        if (height <= 0f)
            return;

        card.Zone(height, zone => DrawInZone(
            zone,
            spec,
            drawMedia,
            collectTitleControls,
            collectActions,
            drawContent,
            floatingCommand));
    }

    private static void DrawInZone(
        ContentZoneContext zone,
        CollapsingHeaderDetailPageSpec spec,
        Action<CollapsingHeaderDetailMediaContext> drawMedia,
        Action<ResponsiveSlotGroup>? collectTitleControls,
        Action<ResponsiveRowBuilder>? collectActions,
        Action<ContentZoneContext> drawContent,
        FloatingCommandSlot? floatingCommand)
    {
        var unit = MetricsScope.UnitHeight;
        var mediaTextGap = MetricsScope.ScaleGap(spec.MediaTextGap);
        var identityActionGap = MetricsScope.ScaleGap(spec.IdentityActionGap);
        var headerContentGap = MetricsScope.ScaleGap(spec.HeaderContentGap);
        var hasFloatingCommand = floatingCommand is { Draw: not null };
        var hasSummary = !string.IsNullOrWhiteSpace(spec.Summary)
            || spec.DrawSummaryContent is not null;
        var hasActions = collectActions is not null;
        var expandedIdentityHeight = unit * 2f;
        var collapsedIdentityHeight = unit;
        var actionsRowHeight = hasActions ? identityActionGap + unit : 0f;
        var expandedHeaderHeight = expandedIdentityHeight
            + actionsRowHeight
            + headerContentGap;
        var collapsedHeaderHeight = collapsedIdentityHeight
            + actionsRowHeight
            + headerContentGap;
        var collapseDistance = unit;
        var scrollExtension = ImGui.GetStyle().ScrollbarSize
            + MetricsScope.Scale(SlapPx.Space6);

        var savedCursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(zone.Min);
        using (zone.ScrollArea(
                   $"##{spec.Key.Value}Content",
                   widthExtension: scrollExtension,
                   drawEdgeFade: false))
        {
            if (spec.ResetScroll)
                ImGui.SetScrollY(0f);

            var scrollY = spec.ResetScroll ? 0f : ImGui.GetScrollY();
            var consumedScroll = spec.ForceCollapsed
                ? collapseDistance
                : Math.Clamp(scrollY, 0f, collapseDistance);
            var collapseProgress = collapseDistance > 0f
                ? consumedScroll / collapseDistance
                : 1f;
            var identityHeight = expandedIdentityHeight - consumedScroll;
            var headerHeight = expandedHeaderHeight - consumedScroll;
            var childMin = ImGui.GetWindowPos();
            var childMax = childMin + ImGui.GetWindowSize();

            ImGui.Dummy(new Vector2(
                zone.Width,
                spec.ForceCollapsed ? collapsedHeaderHeight : expandedHeaderHeight));
            var contentMin = ImGui.GetCursorScreenPos();
            drawContent(new ContentZoneContext(
                contentMin,
                new Vector2(
                    zone.Max.X,
                    contentMin.Y
                        + MathF.Max(0f, zone.Height - collapsedHeaderHeight)),
                zone.Background));

            ScrollFade.DrawVertical(
                new Vector2(childMin.X, childMin.Y + headerHeight),
                childMax,
                drawTop: scrollY > collapseDistance,
                drawBottom: !hasFloatingCommand);

            DrawFloatingHeader(
                zone,
                spec,
                headerHeight,
                identityHeight,
                collapseProgress,
                mediaTextGap,
                identityActionGap,
                hasSummary,
                drawMedia,
                collectTitleControls,
                collectActions);
        }

        if (floatingCommand is { Draw: not null } command)
            FloatingCommandComponent.DrawOverlay(zone, command);

        ImGui.SetCursorScreenPos(savedCursor + new Vector2(0f, zone.Height));
    }

    private static void DrawFloatingHeader(
        ContentZoneContext zone,
        CollapsingHeaderDetailPageSpec spec,
        float headerHeight,
        float identityHeight,
        float collapseProgress,
        float mediaTextGap,
        float identityActionGap,
        bool hasSummary,
        Action<CollapsingHeaderDetailMediaContext> drawMedia,
        Action<ResponsiveSlotGroup>? collectTitleControls,
        Action<ResponsiveRowBuilder>? collectActions)
    {
        var parentCursor = ImGui.GetCursorPos();
        ImGui.SetCursorScreenPos(zone.Min);
        ImGui.PushID(spec.Key.Value);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 0f);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, zone.Background);
        // NoScrollWithMouse forwards the wheel to the parent scroll child only
        // while NoScrollbar is intentionally absent.
        // Rows scrolled under the floating header end exactly at the zone's
        // right edge, and their border/glow protrudes a pixel or two past it.
        // Widen the overlay background by a small right bleed so that sliver
        // stays covered; content rects keep stopping at the original edge.
        var overlayBleed = MetricsScope.Scale(SlapPx.Space4);
        var visible = ImGui.BeginChild(
            "##collapsingHeaderOverlay",
            new Vector2(zone.Width + overlayBleed, headerHeight),
            false,
            ImGuiWindowFlags.NoScrollWithMouse);
        try
        {
            if (visible)
            {
                var unit = MetricsScope.UnitHeight;
                var headerMin = ImGui.GetWindowPos();
                // Transparent themes give zone.Background an alpha of zero, so
                // the header child paints nothing and rows scrolled underneath
                // would show through. Lay a frosted backdrop over the header
                // rect that matches the owner window's surface composite, so it
                // masks the content beneath it without reading deeper than the
                // window (unlike the popup scrim).
                var drawList = ImGui.GetWindowDrawList();
                var headerMax = headerMin + ImGui.GetWindowSize();
                SurfaceComponent.DrawWindowFrostedBackdrop(
                    drawList,
                    headerMin,
                    headerMax,
                    0f);
                var contentMaxX = headerMin.X + zone.Width;
                var mediaMin = headerMin;
                var mediaMax = mediaMin + new Vector2(identityHeight);
                ImGui.SetCursorScreenPos(mediaMin);
                var rawClicked = ImGui.InvisibleButton("##media", mediaMax - mediaMin);
                var interaction = SlapInteraction.Capture(
                    ControlState.None,
                    rawClicked,
                    captureRightClick: true);
                drawMedia(new CollapsingHeaderDetailMediaContext(
                    mediaMin,
                    mediaMax,
                    collapseProgress,
                    interaction.ToControlResult(mediaMin, mediaMax),
                    interaction.VisualMode));

                var textMinX = headerMin.X + identityHeight + mediaTextGap;

                var titleZone = new ContentZoneContext(
                    new Vector2(textMinX, headerMin.Y),
                    new Vector2(contentMaxX, headerMin.Y + unit),
                    zone.Background);
                DrawTitleRow(
                    titleZone,
                    spec.Title,
                    spec.DrawTitleContent,
                    collectTitleControls);

                if (hasSummary)
                {
                    var summaryZone = new ContentZoneContext(
                        new Vector2(textMinX, headerMin.Y + unit),
                        new Vector2(contentMaxX, headerMin.Y + unit * 2f),
                        zone.Background);
                    if (spec.DrawSummaryContent is { } drawSummaryContent)
                        drawSummaryContent(summaryZone);
                    else if (spec.Summary is { } visibleSummary)
                        DrawSummaryLine(
                            summaryZone,
                            visibleSummary,
                            headerMin.Y + identityHeight,
                            spec.SummaryFontSize);
                }

                if (collectActions is not null)
                {
                    var actionsMin = new Vector2(
                        headerMin.X,
                        headerMin.Y + identityHeight + identityActionGap);
                    var actionsZone = new ContentZoneContext(
                        actionsMin,
                        new Vector2(contentMaxX, actionsMin.Y + unit),
                        zone.Background);
                    ImGui.SetCursorScreenPos(actionsMin);
                    actionsZone.ResponsiveRow(collectActions);
                }
            }
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
            ImGui.PopID();
            ImGui.SetCursorPos(parentCursor);
        }
    }

    private static void DrawTitleRow(
        ContentZoneContext zone,
        string title,
        Action<ContentZoneContext>? drawTitleContent,
        Action<ResponsiveSlotGroup>? collectTitleControls)
    {
        var controls = new ResponsiveSlotGroup();
        collectTitleControls?.Invoke(controls);
        var entries = controls.Entries;
        var itemGap = ImGui.GetStyle().ItemSpacing.X;
        var titleControlsGap = MetricsScope.ScaleGap(SlapPx.Space16);

        Vector2 titleSize = default;
        float leftPreferred;
        float leftMinimum;
        if (drawTitleContent is not null)
        {
            leftPreferred = zone.Width;
            leftMinimum = 0f;
        }
        else
        {
            using (Slap.PushFont(SlapFontSize.Large, SlapFontWeight.Bold))
                titleSize = ImGui.CalcTextSize(title ?? string.Empty);
            leftPreferred = titleSize.X;
            leftMinimum = MathF.Min(titleSize.X, MetricsScope.UnitHeight * 2f);
        }

        var ranges = BuildWidthRanges(entries);
        var layout = SlapMeasure.MeasureStripLayout(
            zone.Width,
            leftPreferred,
            leftMinimum,
            titleControlsGap,
            ranges,
            BuildPriorities(entries),
            itemGap);

        if (layout.LeftWidth > 0f)
        {
            var contentZone = new ContentZoneContext(
                zone.Min,
                new Vector2(zone.Min.X + layout.LeftWidth, zone.Max.Y),
                zone.Background);
            if (drawTitleContent is { } content)
            {
                content(contentZone);
            }
            else
            {
                var titleY = zone.Min.Y + SlapLayout.CenterOffset(zone.Height, titleSize.Y);
                var titleMaxX = zone.Min.X + layout.LeftWidth;
                var drawList = ImGui.GetWindowDrawList();
                drawList.PushClipRect(
                    zone.Min,
                    new Vector2(titleMaxX, zone.Max.Y),
                    true);
                try
                {
                    using (Slap.PushFont(SlapFontSize.Large, SlapFontWeight.Bold))
                    {
                        drawList.AddText(
                            new Vector2(zone.Min.X, titleY),
                            ImGui.ColorConvertFloat4ToU32(ThemeScope.Resolved.Body),
                            title ?? string.Empty);
                    }

                    var degraded = titleSize.X > layout.LeftWidth;
                    if (degraded)
                    {
                        EdgeFade.DrawRight(
                            drawList,
                            zone.Min,
                            layout.LeftWidth,
                            zone.Height,
                            zone.Background,
                            SlapPx.Space16);
                    }

                    var hoverMin = zone.Min;
                    var hoverMax = new Vector2(titleMaxX, zone.Max.Y);
                    var hovered = ImGui.IsMouseHoveringRect(hoverMin, hoverMax)
                        && ImGui.IsWindowHovered(
                            ImGuiHoveredFlags.RootAndChildWindows
                            | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
                    var tooltip = ResponsiveTooltip.Compose(degraded, title, null);
                    HoverArbitration.Current.TryShowTooltip(hovered, tooltip, hoverMin, hoverMax);
                }
                finally
                {
                    drawList.PopClipRect();
                }
            }
        }

        var rightEdge = zone.Max.X;
        var drawnCount = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var item = layout.RightLayouts[i];
            if (!item.Visible)
                continue;
            if (drawnCount > 0)
                rightEdge -= itemGap;
            var position = new Vector2(rightEdge - item.Width, zone.Min.Y);
            rightEdge = position.X;
            ImGui.SetCursorScreenPos(position);
            entries[i].DrawAtWidth(item.Width);
            drawnCount++;
        }
    }

    private static void DrawSummaryLine(
        ContentZoneContext zone,
        string text,
        float visibleMaxY,
        SlapFontSize fontSize)
    {
        var clipMaxY = MathF.Min(zone.Max.Y, visibleMaxY);
        if (string.IsNullOrWhiteSpace(text)
            || zone.Width <= 0f
            || zone.Height <= 0f
            || clipMaxY <= zone.Min.Y)
        {
            return;
        }

        var singleLineText = text
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ');
        var drawList = ImGui.GetWindowDrawList();
        Vector2 textSize;
        using (Slap.PushFont(fontSize, SlapFontWeight.Bold))
            textSize = ImGui.CalcTextSize(singleLineText);

        var textPosition = new Vector2(
            zone.Min.X,
            zone.Min.Y + SlapLayout.CenterOffset(zone.Height, textSize.Y));
        drawList.PushClipRect(zone.Min, new Vector2(zone.Max.X, clipMaxY), true);
        try
        {
            using (Slap.PushFont(fontSize, SlapFontWeight.Bold))
            {
                drawList.AddText(
                    textPosition,
                    ImGui.ColorConvertFloat4ToU32(ThemeScope.Resolved.Subtle),
                    singleLineText);
            }

            if (textSize.X > zone.Width)
            {
                EdgeFade.DrawRight(
                    drawList,
                    zone.Min,
                    zone.Width,
                    zone.Height,
                    zone.Background,
                    SlapPx.Space16);
            }
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    private static ResponsiveWidthRange[] BuildWidthRanges(
        IReadOnlyList<ResponsiveSlotEntry> entries)
    {
        var ranges = new ResponsiveWidthRange[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            ranges[i] = entries[i].WidthRange;
        return ranges;
    }

    private static int[] BuildPriorities(
        IReadOnlyList<ResponsiveSlotEntry> entries)
    {
        var priorities = new int[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            priorities[i] = entries[i].Priority;
        return priorities;
    }
}
