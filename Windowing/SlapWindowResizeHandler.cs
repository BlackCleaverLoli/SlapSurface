using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Flags indicating which edges of a window are being resized.
/// </summary>
[Flags]
internal enum SlapResizeEdge
{
    None   = 0,
    Left   = 1,
    Right  = 2,
    Top    = 4,
    Bottom = 8,
}

/// <summary>
/// Configuration for self-managed window resizing.
/// </summary>
internal readonly record struct SlapWindowResizeConfig(
    bool Enabled = false,
    float EdgeHitExtent = 4f,
    float CornerHitExtent = 8f,
    Vector2 MinSize = default,
    Vector2 MaxSize = default,
    /// <summary>Margin from the display edge that the window must stay within.</summary>
    float ViewportMargin = 24f
)
{
    public static SlapWindowResizeConfig Default => new(
        Enabled: true,
        EdgeHitExtent: SlapPx.Space4,
        CornerHitExtent: SlapPx.Space8,
        MinSize: new Vector2(400f, 300f),
        MaxSize: default,
        ViewportMargin: SlapPx.Space24
    );

    internal Vector2 ResolveMinSize() => MetricsScope.Scale(
        MinSize == default ? new Vector2(400f, 300f) : MinSize
    );

    internal Vector2 ResolveMaxSize(Vector2 displaySize, Vector2 windowPos)
    {
        if (MaxSize != default)
            return MaxSize;

        var margin = MetricsScope.Scale(ViewportMargin);
        var minimum = ResolveMinSize();
        if (displaySize.X <= 0f || displaySize.Y <= 0f)
            return new Vector2(float.MaxValue, float.MaxValue);

        return new Vector2(
            MathF.Max(minimum.X, displaySize.X - windowPos.X - margin),
            MathF.Max(minimum.Y, displaySize.Y - windowPos.Y - margin));
    }
}

/// <summary>
/// Self-managed window resize handler. Supports 4 edges (top, bottom, left, right)
/// and 2 bottom corners (bottom-left, bottom-right).
/// Persist across frames so <see cref="ShouldDisableMove"/> can be checked before
/// the ImGui window begins.
/// </summary>
internal sealed class SlapWindowResizeHandler
{
    private SlapResizeEdge _activeEdge;
    private Vector2 _resizeStartMousePos;
    private Vector2 _resizeStartWindowPos;
    private Vector2 _resizeStartWindowSize;
    private Vector2 _lastWindowPos;
    private Vector2 _lastWindowSize;
    private bool _hasLastWindowRect;
    // Store last scaled hit extents so ShouldDisableMove can use them outside the metrics scope
    private float _lastEdgeHit = SlapPx.Space4;
    private float _lastCornerHit = SlapPx.Space8;
    private float _lastEdgeExpansion;
    // Corner inset is resolved from the ambient theme/metrics scope, so cache it like the hit extents.
    private float _lastCornerInset;

    /// <summary>
    /// True when the window should have NoMove flag set
    /// (active resize or hovering a resize edge).
    /// </summary>
    public bool ShouldDisableMove =>
        _activeEdge != SlapResizeEdge.None
        || (_hasLastWindowRect
            && GetHoveredEdge(_lastWindowPos, _lastWindowSize, ImGui.GetIO().MousePos,
                _lastEdgeHit, _lastCornerHit, _lastCornerInset, _lastEdgeExpansion) != SlapResizeEdge.None);

    /// <summary>
    /// Process resize logic for this frame. Call once per frame after the window is begun.
    /// </summary>
    public void Handle(SlapWindowResizeConfig config, float edgeExpansion = 0f)
    {
        if (!config.Enabled)
            return;

        var io = ImGui.GetIO();
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        _lastWindowPos = windowPos;
        _lastWindowSize = windowSize;
        _hasLastWindowRect = true;

        var edgeHit = MetricsScope.Scale(config.EdgeHitExtent);
        var cornerHit = MetricsScope.Scale(config.CornerHitExtent);
        // Half the radius: the full radius pulls the corner hit zone too far inward,
        // so halving keeps it hugging the visible rounded corner without making the
        // actual corner edge feel un-grabbable.
        var cornerInset = SlapCorners.ControlRadius / 2f;
        _lastEdgeHit = edgeHit;
        _lastCornerHit = cornerHit;
        _lastEdgeExpansion = edgeExpansion;
        _lastCornerInset = cornerInset;
        var hoveredEdge = GetHoveredEdge(windowPos, windowSize, io.MousePos, edgeHit, cornerHit, cornerInset, edgeExpansion);

        // Only allow resize hit when the window is hovered/focused, or the
        // point lies in the expanded edge band that no other window claims.
        if (_activeEdge == SlapResizeEdge.None
            && hoveredEdge != SlapResizeEdge.None
            && !CanUseManualResizeHit())
        {
            hoveredEdge = SlapResizeEdge.None;
        }

        // Update cursor
        var edgeForCursor = _activeEdge != SlapResizeEdge.None ? _activeEdge : hoveredEdge;
        if (edgeForCursor != SlapResizeEdge.None)
        {
            ImGui.SetMouseCursor(GetResizeCursor(edgeForCursor));
            // The overhang band sits outside the ImGui window hover rect, so the
            // input backend hands the click to the game instead of ImGui and the
            // resize drag never starts. Force the next frame to capture the mouse
            // while a resize edge is active so the click is delivered to ImGui.
            ImGui.SetNextFrameWantCaptureMouse(true);
        }

        // Begin resize drag
        if (_activeEdge == SlapResizeEdge.None
            && hoveredEdge != SlapResizeEdge.None
            && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && !ImGui.IsAnyItemActive())
        {
            _activeEdge = hoveredEdge;
            _resizeStartMousePos = io.MousePos;
            _resizeStartWindowPos = windowPos;
            _resizeStartWindowSize = windowSize;
        }

        if (_activeEdge == SlapResizeEdge.None)
            return;

        // End resize drag
        if (!io.MouseDown[(int)ImGuiMouseButton.Left])
        {
            _activeEdge = SlapResizeEdge.None;
            return;
        }

        // Apply resize delta
        var delta = io.MousePos - _resizeStartMousePos;
        var nextPos = _resizeStartWindowPos;
        var nextSize = _resizeStartWindowSize;

        if ((_activeEdge & SlapResizeEdge.Right) != 0)
            nextSize.X += delta.X;

        if ((_activeEdge & SlapResizeEdge.Bottom) != 0)
            nextSize.Y += delta.Y;

        if ((_activeEdge & SlapResizeEdge.Left) != 0)
        {
            nextSize.X -= delta.X;
            nextPos.X += delta.X;
        }

        if ((_activeEdge & SlapResizeEdge.Top) != 0)
        {
            nextSize.Y -= delta.Y;
            nextPos.Y += delta.Y;
        }

        // Clamp size; if left/top resize was clamped, adjust position to keep the opposite edge fixed
        var minSize = config.ResolveMinSize();
        var maxSize = config.ResolveMaxSize(io.DisplaySize, _resizeStartWindowPos);

        if (nextSize.X < minSize.X)
        {
            if ((_activeEdge & SlapResizeEdge.Left) != 0)
                nextPos.X -= minSize.X - nextSize.X;
            nextSize.X = minSize.X;
        }
        if (nextSize.Y < minSize.Y)
        {
            if ((_activeEdge & SlapResizeEdge.Top) != 0)
                nextPos.Y -= minSize.Y - nextSize.Y;
            nextSize.Y = minSize.Y;
        }

        nextSize.X = MathF.Min(nextSize.X, maxSize.X);
        nextSize.Y = MathF.Min(nextSize.Y, maxSize.Y);

        ImGui.SetWindowSize(nextSize, ImGuiCond.Always);
        if ((_activeEdge & (SlapResizeEdge.Left | SlapResizeEdge.Top)) != 0)
            ImGui.SetWindowPos(nextPos, ImGuiCond.Always);
    }

    private static SlapResizeEdge GetHoveredEdge(
        Vector2 windowPos, Vector2 windowSize, Vector2 mousePos,
        float edgeHit, float cornerHit, float cornerInset, float edgeExpansion)
    {
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
            return SlapResizeEdge.None;

        var expansion = new Vector2(edgeExpansion);
        var min = windowPos - expansion;
        var max = windowPos + windowSize + expansion;

        // Check bottom corners first (higher priority than edges)
        // Inset the corner reference point along the diagonal toward the window
        // interior so the square hit zone hugs the rounded corner instead of
        // covering the clipped corner square.
        // Bottom-right corner
        if (IsInCorner(mousePos, max - new Vector2(cornerInset), cornerHit))
            return SlapResizeEdge.Right | SlapResizeEdge.Bottom;

        // Bottom-left corner
        if (IsInCorner(mousePos, new Vector2(min.X + cornerInset, max.Y - cornerInset), cornerHit))
            return SlapResizeEdge.Left | SlapResizeEdge.Bottom;

        // Check edges. Bottom corners are handled above, so shorten the edge hit
        // zones there to match the rounded-corner inset: vertical edges stop short
        // of the bottom corner and the bottom edge stops short of both corners.
        var right = windowPos.X + windowSize.X;
        var bottom = windowPos.Y + windowSize.Y;
        var inLeft = IsInVerticalEdge(mousePos, windowPos.X, right, windowPos.Y, bottom, edgeHit, cornerInset, edgeExpansion, isLeft: true);
        var inRight = IsInVerticalEdge(mousePos, windowPos.X, right, windowPos.Y, bottom, edgeHit, cornerInset, edgeExpansion, isLeft: false);
        var inTop = IsInHorizontalEdge(mousePos, windowPos.X, right, windowPos.Y, bottom, edgeHit, cornerInset, edgeExpansion, isTop: true);
        var inBottom = IsInHorizontalEdge(mousePos, windowPos.X, right, windowPos.Y, bottom, edgeHit, cornerInset, edgeExpansion, isTop: false);

        if (inRight)  return SlapResizeEdge.Right;
        if (inBottom) return SlapResizeEdge.Bottom;
        if (inLeft)   return SlapResizeEdge.Left;
        if (inTop)    return SlapResizeEdge.Top;

        return SlapResizeEdge.None;
    }

    // Edge hit bands hug the window edge: reach edgeHit inward, and outward by
    // edgeHit + expansion so the frosted backdrop overhang stays grabbable.
    private static bool IsInVerticalEdge(
        Vector2 mousePos, float left, float right, float top, float bottom,
        float hitExtent, float cornerInset, float expansion, bool isLeft)
    {
        if (hitExtent <= 0f)
            return false;

        var edgeX = isLeft ? left : right;
        var minX = edgeX - (isLeft ? expansion + hitExtent : hitExtent);
        var maxX = edgeX + (isLeft ? hitExtent : expansion + hitExtent);
        return IsInRange(mousePos.X, minX, maxX)
               && IsInRange(mousePos.Y, top - expansion, bottom + expansion - cornerInset);
    }

    private static bool IsInHorizontalEdge(
        Vector2 mousePos, float left, float right, float top, float bottom,
        float hitExtent, float cornerInset, float expansion, bool isTop)
    {
        if (hitExtent <= 0f)
            return false;

        var edgeY = isTop ? top : bottom;
        var minY = edgeY - (isTop ? expansion + hitExtent : hitExtent);
        var maxY = edgeY + (isTop ? hitExtent : expansion + hitExtent);
        var minX = (isTop ? left : left + cornerInset) - expansion;
        var maxX = (isTop ? right : right - cornerInset) + expansion;
        return IsInRange(mousePos.Y, minY, maxY)
               && IsInRange(mousePos.X, minX, maxX);
    }

    private static bool IsInCorner(Vector2 mousePos, Vector2 cornerPos, float hitExtent)
    {
        if (hitExtent <= 0f)
            return false;

        return IsInRange(mousePos.X, cornerPos.X - hitExtent, cornerPos.X + hitExtent)
               && IsInRange(mousePos.Y, cornerPos.Y - hitExtent, cornerPos.Y + hitExtent);
    }

    // The window being NoResize keeps ImGui from reporting the overhang band as
    // hovered, so also accept the edge/corner hit when nothing else is under the
    // cursor (no window claims the point, e.g. the frosted backdrop overhang).
    private static bool CanUseManualResizeHit() =>
        ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
        || ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)
        || !ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow);

    private static ImGuiMouseCursor GetResizeCursor(SlapResizeEdge edge)
    {
        var hasH = (edge & (SlapResizeEdge.Left | SlapResizeEdge.Right)) != 0;
        var hasV = (edge & (SlapResizeEdge.Top | SlapResizeEdge.Bottom)) != 0;

        if (hasH && hasV)
            // Bottom-left uses NESW, bottom-right uses NWSE
            return (edge & SlapResizeEdge.Left) != 0
                ? ImGuiMouseCursor.ResizeNesw
                : ImGuiMouseCursor.ResizeNwse;

        if (hasH)
            return ImGuiMouseCursor.ResizeEw;

        return ImGuiMouseCursor.ResizeNs;
    }

    private static bool IsInRange(float value, float min, float max) =>
        value >= min && value <= max;
}
