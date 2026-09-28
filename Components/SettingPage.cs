using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct SettingPageSpec(
    ControlKey Key,
    IReadOnlyList<SettingSectionSpec> Sections,
    Func<string, string>? ResolveLabel = null,
    /// <summary>
    /// Draw top and bottom scroll edge fades in the content area when the
    /// sections overflow, matching the ContentCard scroll-area behavior.
    /// </summary>
    bool DrawEdgeFade = true,
    /// <summary>
    /// When <c>true</c>, the content scroll is reset to the top this frame.
    /// </summary>
    bool ResetScroll = false,
    /// <summary>
    /// Optional right-side controls collected into the tab strip row, e.g.
    /// page actions. When <c>null</c>, the tab strip spans the full width.
    /// </summary>
    Action<ResponsiveRowBuilder>? CollectTabActions = null,
    /// <summary>
    /// Reserved bottom height in scaled pixels, appended as bottom padding to
    /// the scroll content so it can scroll above an external overlay (e.g. a
    /// floating command bar). The scroll viewport is not reduced.
    /// </summary>
    float ReservedBottomHeight = 0f
);

internal readonly record struct SettingSectionSpec(
    ControlKey Key,
    string Title,
    FontAwesomeIcon? Icon = null,
    string? Tooltip = null,
    ControlState State = ControlState.None
);

internal static class SettingPageComponent
{
    private const float TabsGap = SlapPx.Space8;

    /// <summary>
    /// Per-page cache of whether the content overflowed the scroll child
    /// last frame, keyed by page ID. Keeps the content body width stable
    /// whether or not the scrollbar is visible.
    /// </summary>
    private static readonly Dictionary<string, bool> _pageOverflowed = new();
    private static readonly Dictionary<string, string> _pageActiveSection = new();

    public static void Draw(
        SettingPageSpec spec,
        ContentZoneContext zone,
        Action<SettingSectionSpec, ContentZoneContext> drawSection)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("SettingPageSpec.Key.Value must not be empty.", nameof(spec));
        if (spec.Sections.Count == 0)
            return;

        var pageMin = zone.Min;
        var pageMax = zone.Max;
        var height = pageMax.Y - pageMin.Y;
        if (height <= 0f)
            return;

        ImGui.SetCursorScreenPos(pageMin);

        ImGui.PushID(spec.Key.Value);
        try
        {
            var key = spec.Key.Value;
            var resolvedSections = ResolveSectionTitles(spec.Sections, spec.ResolveLabel);

            // ── 操作行：横向次级 tab（可带右侧操作控件）──
            var activeKey = _pageActiveSection.GetValueOrDefault(key, resolvedSections[0].Key.Value);
            var activeIndex = FindSectionIndex(resolvedSections, activeKey);
            string? jumpTarget = null;
            var tabs = new TabItemSpec[resolvedSections.Length];
            for (var i = 0; i < resolvedSections.Length; i++)
            {
                var section = resolvedSections[i];
                tabs[i] = new TabItemSpec(section.Title, section.Icon, section.Tooltip, section.State);
            }
            var tabSpec = new TabStripSpec(
                $"{key}_tabs",
                tabs,
                activeIndex,
                OverflowIcon: FontAwesomeIcon.AngleDown);
            var tabResult = default(TabStripResult);
            if (spec.CollectTabActions is { } collectTabActions)
            {
                zone.ResponsiveRow(row =>
                {
                    row.Left.TabStrip(tabSpec, result => tabResult = result);
                    collectTabActions(row);
                });
            }
            else
            {
                tabResult = TabStripComponent.Draw(
                    tabSpec with { ResolvedWidth = MathF.Max(0f, pageMax.X - pageMin.X) });
            }

            if (tabResult.Changed
                && tabResult.SelectedIndex >= 0
                && tabResult.SelectedIndex < resolvedSections.Length)
            {
                jumpTarget = resolvedSections[tabResult.SelectedIndex].Key.Value;
            }

            // ── 滚动区域：全部分区顺序堆叠 ──
            var gap = MetricsScope.ScaleGap(TabsGap);
            var stripHeight = TabStripComponent.ResolveStripHeight();
            var contentTop = pageMin.Y + stripHeight + gap;
            var contentHeight = MathF.Max(0f, pageMax.Y - contentTop);
            if (contentHeight <= 0f)
                return;

            var hadScrollbar = _pageOverflowed.TryGetValue(key, out var overflowed) && overflowed;
            var scrollbarExt = hadScrollbar ? ImGui.GetStyle().ScrollbarSize : 0f;
            var contentWidth = MathF.Max(0f, pageMax.X - pageMin.X);
            ImGui.SetCursorScreenPos(new Vector2(pageMin.X, contentTop));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.BeginChild(
                $"{key}_scroll",
                new Vector2(contentWidth + scrollbarExt, contentHeight),
                false,
                ImGuiWindowFlags.None);
            try
            {
                if (spec.ResetScroll)
                    ImGui.SetScrollY(0f);

                var contentBodyMin = ImGui.GetCursorScreenPos();
                var contentAvail = ImGui.GetContentRegionAvail();
                var contentBodyMax = contentBodyMin + new Vector2(
                    MathF.Max(0f, contentAvail.X),
                    MathF.Max(0f, contentAvail.Y));
                var background = SurfaceComponent.ResolveVisualBlockBackground(ControlState.None);

                var sectionOffsets = new Dictionary<string, float>(resolvedSections.Length);
                foreach (var section in resolvedSections)
                {
                    sectionOffsets[section.Key.Value] =
                        ImGui.GetCursorScreenPos().Y - contentTop + ImGui.GetScrollY();
                    drawSection(
                        section,
                        new ContentZoneContext(
                            contentBodyMin, contentBodyMax,
                            background));
                }

                if (spec.ReservedBottomHeight > 0f)
                    ImGui.Dummy(new Vector2(contentWidth, spec.ReservedBottomHeight));

                if (jumpTarget != null && sectionOffsets.TryGetValue(jumpTarget, out var jumpY))
                    ImGui.SetScrollY(MathF.Max(0f, jumpY));

                var activeSection = resolvedSections[0].Key.Value;
                var scrollY = ImGui.GetScrollY();
                var viewTop = scrollY + 1f;
                foreach (var section in resolvedSections)
                {
                    if (
                        sectionOffsets.TryGetValue(section.Key.Value, out var sectionTop)
                        && sectionTop <= viewTop
                    )
                        activeSection = section.Key.Value;
                }

                // 滚动到底时，若最后分区顶部无法越过视口上沿（分区高度小于视口），
                // 顶部规则会让高亮停在上一分区；以视口底部为界让高亮与偏移对应。
                var lastSection = resolvedSections[resolvedSections.Length - 1];
                var scrollMaxY = ImGui.GetScrollMaxY();
                if (
                    activeSection != lastSection.Key.Value
                    && scrollMaxY > 1f
                    && scrollY >= scrollMaxY - 1f
                    && sectionOffsets.TryGetValue(lastSection.Key.Value, out var lastTop)
                    && lastTop <= scrollY + contentHeight
                )
                {
                    activeSection = lastSection.Key.Value;
                }
                _pageActiveSection[key] = activeSection;

                if (spec.DrawEdgeFade)
                {
                    ScrollFade.DrawVertical(
                        new Vector2(pageMin.X, contentTop),
                        new Vector2(pageMin.X + contentWidth, contentTop + contentHeight),
                        background);
                }
            }
            finally
            {
                // GetCursorScreenPos() returns screen-space Y offset by -Scroll.y, so add
                // ScrollY back to recover the true ContentSize.y.  ImGui computes
                // ScrollMax at the NEXT Begin() from ContentSize, so caching this
                // now keeps the detection in lockstep — zero frame skew.
                var contentHeightFinal = ImGui.GetCursorScreenPos().Y
                    - ImGui.GetWindowPos().Y
                    + ImGui.GetScrollY();
                _pageOverflowed[key] = contentHeightFinal > contentHeight;
                ImGui.EndChild();
                ImGui.PopStyleVar();
            }
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static int FindSectionIndex(SettingSectionSpec[] sections, string key)
    {
        for (var i = 0; i < sections.Length; i++)
            if (sections[i].Key.Value == key)
                return i;
        return 0;
    }

    private static SettingSectionSpec[] ResolveSectionTitles(
        IReadOnlyList<SettingSectionSpec> sections,
        Func<string, string>? resolveLabel)
    {
        if (resolveLabel == null)
        {
            var result = new SettingSectionSpec[sections.Count];
            for (var i = 0; i < sections.Count; i++)
                result[i] = sections[i];
            return result;
        }

        var resolved = new SettingSectionSpec[sections.Count];
        for (var i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            resolved[i] = new SettingSectionSpec(
                s.Key, resolveLabel(s.Title), s.Icon, s.Tooltip, s.State);
        }
        return resolved;
    }
}
