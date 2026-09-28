using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;

namespace SlapSurface;

/// <summary>
/// Caller-owned sidebar collapse preference. SlapSurface reports changes but does not persist them.
/// </summary>
internal readonly record struct SlapSidebarCollapseConfig(
    bool Enabled = false,
    bool Collapsed = false
);

/// <summary>Collapse preference requested by a sidebar-gap drag or an explicit collapse toggle.</summary>
internal readonly record struct SlapSidebarCollapseResult(
    bool Changed = false,
    bool Collapsed = false,
    bool TemporaryExpandRequested = false
);

internal readonly record struct SlapSidebarLayout(
    float WidthUnits,
    float FullWidthUnits,
    bool Collapsed,
    bool CanExpand
);

/// <summary>
/// Tracks the drag gesture across frames. The anchor is the original boundary
/// (the sidebar edge where the handle hover took effect) minus the drag
/// threshold when starting expanded: leaving the starting state needs one
/// threshold of travel, and returning to the original boundary toggles back.
/// A single continuous drag can therefore collapse and expand repeatedly.
/// </summary>
internal sealed class SlapSidebarCollapseHandler
{
    private const float HandleHitExtent = SlapPx.Space8;
    private const float DragThresholdUnits = 1f;
    private Vector2 dragAnchorMousePos;
    private Vector2 lastHandleMin;
    private Vector2 lastHandleMax;
    private bool hasLastHandleRect;
    private bool dragActive;

    /// <summary>
    /// True when the host window should have <see cref="ImGuiWindowFlags.NoMove"/> set
    /// because the sidebar gap is hovered or its drag gesture is active.
    /// </summary>
    public bool ShouldDisableMove =>
        dragActive
        || (hasLastHandleRect && IsInHandleRect(ImGui.GetIO().MousePos, lastHandleMin, lastHandleMax));

    internal SlapSidebarCollapseResult Handle(
        SlapSidebarCollapseConfig config,
        SlapSidebarLayout layout,
        ShellBounds bounds
    )
    {
        var io = ImGui.GetIO();
        if (!io.MouseDown[(int)ImGuiMouseButton.Left])
        {
            dragActive = false;
        }

        // A collapsed sidebar that cannot expand in-flow (window too narrow) still
        // hosts the handle, but dragging it opens the temporary overlay instead.
        var temporaryExpand = layout.Collapsed && !layout.CanExpand;
        if (!config.Enabled || bounds.SidebarSize.Y <= 0f)
        {
            hasLastHandleRect = false;
            return new SlapSidebarCollapseResult(Collapsed: layout.Collapsed);
        }

        var threshold = MetricsScope.UnitHeight * DragThresholdUnits;
        var handleWidth = MetricsScope.Scale(HandleHitExtent);
        lastHandleMin = new Vector2(bounds.SidebarMax.X, bounds.SidebarMin.Y);
        lastHandleMax = new Vector2(bounds.SidebarMax.X + handleWidth, bounds.SidebarMax.Y);
        hasLastHandleRect = true;

        var hovered = IsInHandleRect(io.MousePos, lastHandleMin, lastHandleMax)
            && CanUseManualDragHit();
        if (dragActive || hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        if (!dragActive
            && hovered
            && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && !ImGui.IsAnyItemActive())
        {
            dragActive = true;
            // Anchor at 原位 − 触发距离 when starting expanded: collapsing fires
            // when the mouse crosses the anchor, expanding when it returns to
            // the original edge (anchor + threshold). Starting collapsed mirrors
            // this, so the away toggle always needs one threshold of travel.
            dragAnchorMousePos = new Vector2(
                lastHandleMin.X - (layout.Collapsed ? 0f : threshold),
                io.MousePos.Y);
        }

        if (!dragActive)
            return new SlapSidebarCollapseResult(Collapsed: layout.Collapsed);

        var dragX = io.MousePos.X - dragAnchorMousePos.X;
        var toggled = layout.Collapsed ? dragX >= threshold : dragX <= 0f;
        if (!toggled)
            return new SlapSidebarCollapseResult(Collapsed: layout.Collapsed);

        if (temporaryExpand)
            return new SlapSidebarCollapseResult(TemporaryExpandRequested: true, Collapsed: layout.Collapsed);

        return new SlapSidebarCollapseResult(Changed: true, Collapsed: !layout.Collapsed);
    }

    private static bool CanUseManualDragHit() =>
        ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
        || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);

    private static bool IsInHandleRect(Vector2 mousePos, Vector2 min, Vector2 max) =>
        mousePos.X >= min.X
        && mousePos.X <= max.X
        && mousePos.Y >= min.Y
        && mousePos.Y <= max.Y;
}
