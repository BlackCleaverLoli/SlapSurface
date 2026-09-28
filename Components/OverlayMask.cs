using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct OverlayMaskSpec(
    string Key,
    bool ShowOverlay = true,
    float OverlayAlpha = 0.4f,
    bool NoMove = false
);

/// <summary>
/// Shared dim-mask layer for modal dialogs and popup sidebars: a title-bar
/// strip plus a full-host-content-area overlay. This component is a
/// window-level-only protocol: it must be drawn at the top-level host window
/// scope (never inside a child window); violations throw
/// <see cref="InvalidOperationException"/>. The caller draws its panel on
/// top, then calls <see cref="CaptureOutsideClick"/> before beginning the
/// panel child window.
/// </summary>
internal sealed class OverlayMaskScope : IDisposable
{
    private bool disposed;
    private readonly bool overlayChildBegun;
    private readonly string key;

    internal OverlayMaskScope(
        bool open,
        bool overlayChildBegun,
        string key,
        Vector2 windowMin,
        Vector2 windowMax,
        Vector2 hostMin,
        Vector2 hostMax
    )
    {
        IsOpen = open;
        this.overlayChildBegun = overlayChildBegun;
        this.key = key;
        WindowMin = windowMin;
        WindowMax = windowMax;
        HostMin = hostMin;
        HostMax = hostMax;
    }

    public bool IsOpen { get; }
    public Vector2 WindowMin { get; }
    public Vector2 WindowMax { get; }
    public Vector2 WindowSize => WindowMax - WindowMin;
    public Vector2 HostMin { get; }
    public Vector2 HostMax { get; }
    public Vector2 HostSize => HostMax - HostMin;

    /// <summary>
    /// Draws an invisible full-host hit target and reports whether the mouse
    /// was clicked outside <paramref name="panelMin"/>..<paramref name="panelMax"/>.
    /// </summary>
    internal bool CaptureOutsideClick(Vector2 panelMin, Vector2 panelMax)
    {
        if (!IsOpen)
            return false;

        ImGui.SetCursorScreenPos(HostMin);
        var clicked = ImGui.InvisibleButton($"##{key}OverlayMaskHit", HostSize);
        if (!clicked)
            return false;

        var mouse = ImGui.GetIO().MousePos;
        var insidePanel =
            mouse.X >= panelMin.X && mouse.X <= panelMax.X
            && mouse.Y >= panelMin.Y && mouse.Y <= panelMax.Y;
        return !insidePanel;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (overlayChildBegun)
        {
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
        }
    }
}

internal static class OverlayMaskComponent
{
    internal static OverlayMaskScope Begin(OverlayMaskSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key))
            throw new ArgumentException("OverlayMaskSpec.Key must not be empty.", nameof(spec));

        var currentWindow = ImGuiP.GetCurrentWindow();
        if (!currentWindow.ParentWindow.IsNull)
        {
            throw new InvalidOperationException(
                "OverlayMask (modal dialogs and popup sidebars) must be drawn at the "
                + "top-level host window scope, not inside a child window.");
        }

        var windowPos = ImGui.GetWindowPos();
        var windowMax = windowPos + ImGui.GetWindowSize();
        var contentTop = windowPos.Y + ImGui.GetWindowContentRegionMin().Y;
        var titleRounding = SlapCorners.ForRect(
            windowPos,
            new Vector2(windowMax.X, contentTop));
        // Transparent title bars have no opaque fill, so no strip is drawn above
        // the content top (that would show through as a partial band). The scrim
        // top stays at the semantic title-bar bottom (contentTop); in the
        // transparent/frosted theme the visual backdrop extends beyond the
        // window rect by BackdropExtension, so the scrim is drawn over the
        // expanded rect (inset one border thickness to stay clear of the border
        // line) and the clip is widened inside the overlay child to reach it.
        var isTransparent = ThemeScope.Resolved.IsTransparentTheme;
        var extension = isTransparent ? SlapSurface.Metrics.MetricsScope.Scale(6f) : 0f;
        var borderInset = isTransparent ? SlapSurface.Metrics.MetricsScope.BorderThickness : 0f;
        var overlayTop = isTransparent
            ? contentTop
            : titleRounding > 0f
                ? MathF.Max(windowPos.Y, contentTop - titleRounding)
                : contentTop;
        var hostMin = new Vector2(windowPos.X, contentTop);
        var hostMax = windowMax;
        var hostSize = hostMax - hostMin;
        var scrimMin = new Vector2(windowPos.X - extension + borderInset, contentTop);
        var scrimMax = new Vector2(windowMax.X + extension - borderInset, windowMax.Y + extension - borderInset);
        var alpha = Math.Clamp(spec.OverlayAlpha, 0f, 1f);

        // Opaque theme only: the overlay child cannot reach above the content
        // area top due to its clip rect, so this host strip covers the
        // rounded-corner gap between overlayTop and contentTop (hidden under the
        // redrawn opaque title bar).
        if (!isTransparent && spec.ShowOverlay && overlayTop < contentTop)
        {
            var parentDrawList = ImGui.GetWindowDrawList();
            parentDrawList.PushClipRect(windowPos, windowMax, false);
            parentDrawList.AddRectFilled(
                new Vector2(windowPos.X, overlayTop),
                new Vector2(windowMax.X, contentTop),
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, alpha)));
            parentDrawList.PopClipRect();
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.SetCursorScreenPos(hostMin);
        var overlayFlags = ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoBackground;
        if (spec.NoMove)
            overlayFlags |= ImGuiWindowFlags.NoMove;
        var overlayVisible = ImGui.BeginChild(
            $"##{spec.Key}Overlay",
            hostSize,
            false,
            overlayFlags);
        if (!overlayVisible)
        {
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
            return new OverlayMaskScope(false, false, spec.Key, windowPos, windowMax, hostMin, hostMax);
        }

        if (spec.ShowOverlay)
        {
            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(scrimMin, scrimMax, false);
            drawList.AddRectFilled(
                scrimMin,
                scrimMax,
                ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, alpha)),
                SlapCorners.ControlRadius,
                ImDrawFlags.RoundCornersBottom);
            drawList.PopClipRect();
        }

        return new OverlayMaskScope(true, true, spec.Key, windowPos, windowMax, hostMin, hostMax);
    }
}
