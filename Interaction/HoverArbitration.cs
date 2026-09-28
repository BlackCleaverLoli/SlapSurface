using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlapSurface;

/// <summary>
/// Ambient hover arbitration scope. Structural containers (Toolbar, GameIconStrip,
/// SplitButton, ContentZone.Split, etc.) push a scope on Begin/Draw; leaf controls
/// read <see cref="HoverArbitration.Current"/> to participate in first-hover-wins
/// arbitration without any API change to their specs.
/// <para>
/// Hover capture <b>bubbles up</b>: when a child scope captures hover, it
/// propagates to the parent scope so that items outside the child scope also
/// see hover as captured. This lets containers like <c>SplitButton</c> push
/// their own scope for internal arbitration while still suppressing hover in
/// the enclosing scope (e.g. <c>SurfaceList</c> rows).
/// </para>
/// </summary>
internal static class HoverArbitration
{
    [ThreadStatic] private static SlapHoverScope? _current;

    /// <summary>
    /// The current ambient hover scope. If no container has pushed a scope,
    /// a root scope is lazily created (behaves as per-frame auto-reset).
    /// </summary>
    public static SlapHoverScope Current => _current ??= new SlapHoverScope(null);

    /// <summary>
    /// Push a new child scope. Call within a <c>using</c> in structural containers.
    /// </summary>
    public static IDisposable Push()
    {
        var previous = _current;
        _current = new SlapHoverScope(previous);
        return new PopScope(previous);
    }

    /// <summary>
    /// Reset to root. Called by the window frame at the start of each frame.
    /// </summary>
    public static void Reset() => _current = null;

    private readonly struct PopScope(SlapHoverScope? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }
}

/// <summary>
/// Per-frame first-hover and first-tooltip arbitration for dense adjacent items.
/// Typically accessed via <see cref="HoverArbitration.Current"/>; can also be
/// instantiated directly for stack-allocated or test scenarios.
/// </summary>
internal sealed class SlapHoverScope
{
    private bool _hoverCaptured;
    private bool _tooltipShown;
    private SlapOutlineGroup _outline;
    private readonly SlapHoverScope? _parent;

    internal SlapHoverScope(SlapHoverScope? parent) => _parent = parent;

    public bool IsHoverCaptured => _hoverCaptured;
    public bool IsTooltipShown => _tooltipShown;

    public bool CaptureHover(bool hovered)
    {
        if (!hovered || _hoverCaptured)
            return false;

        _hoverCaptured = true;
        // Bubble up: propagate hover capture to the parent scope so that
        // items outside this scope (e.g. SurfaceList rows) also see hover
        // as captured.
        _parent?.MarkHoverCaptured();
        return true;
    }

    /// <summary>
    /// Called by a child scope to propagate hover capture upward.
    /// Unlike <see cref="CaptureHover"/>, this does not return a value —
    /// it simply marks the scope as captured.
    /// </summary>
    private void MarkHoverCaptured()
    {
        if (_hoverCaptured)
            return;
        _hoverCaptured = true;
        _parent?.MarkHoverCaptured();
    }

    public bool CaptureLastItem(ImGuiHoveredFlags flags = ImGuiHoveredFlags.None) =>
        CaptureHover(ImGui.IsItemHovered(flags));

    public bool TryShowTooltip(
        bool hovered,
        string? tooltip,
        Vector2? hoverMin = null,
        Vector2? hoverMax = null,
        SlapTooltipDirection? preferredDirection = null) =>
        SlapTooltip.TryShowFirst(ref _tooltipShown, hovered, tooltip, hoverMin, hoverMax, preferredDirection);

    public void SetOutlineClipRect(Vector2 min, Vector2 max) =>
        _outline.SetClipRect(min, max);

    public void CaptureOutline(
        Vector2 min, Vector2 max,
        bool hovered, bool selected,
        Vector4 accent,
        SlapOutlineCutout? cutout = null,
        bool drawOutline = true,
        float? rounding = null) =>
        _outline.Capture(min, max, hovered, selected, accent, cutout, drawOutline, rounding);

    public void CaptureOutlineLastItem(
        bool hovered, bool selected,
        Vector4 accent,
        SlapOutlineCutout? cutout = null,
        bool drawOutline = true,
        float? rounding = null) =>
        _outline.CaptureLastItem(hovered, selected, accent, cutout, drawOutline, rounding);

    public void FlushOutline(string animationKey, float thickness = 1f) =>
        _outline.Flush(animationKey, thickness);

    public void FlushOutline(ControlKey animationKey, float thickness = 1f) =>
        _outline.Flush(animationKey, thickness);

    public void Reset()
    {
        _hoverCaptured = false;
        _tooltipShown = false;
        _outline = default;
    }
}
