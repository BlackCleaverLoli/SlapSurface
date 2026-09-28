using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Specification for a quick-input dialog: a window-centered popup over a
/// modal-style dim mask whose content is a caller-drawn slot. The panel
/// auto-fits the content size with a 6px default panel padding. It commits on outside click
/// and tolerates invalid intermediate states. Escape is intentionally not
/// handled: when no text input is focused, the host window consumes it.
/// Must be drawn at the top-level host window scope (see
/// <see cref="OverlayMaskComponent"/>).
/// </summary>
internal readonly record struct QuickInputPopupSpec(
    ControlKey Key,
    float OverlayAlpha = 0.5f,
    ControlState State = ControlState.None,
    LayeredShadowSpec Shadow = default,
    bool CommitOnOutsideClick = true,
    /// <summary>
    /// Optional panel padding in unscaled pixels. Defaults to 6px on each side.
    /// </summary>
    Vector2? Padding = null
);

/// <summary>Result of a quick-input popup frame.</summary>
internal readonly record struct QuickInputPopupResult(
    bool IsOpen,
    bool Committed
);

internal static class QuickInputPopupComponent
{
    private static readonly Dictionary<string, bool> WasOpen = new();

    internal static void Reset()
    {
        WasOpen.Clear();
    }

    public static QuickInputPopupResult Draw(
        QuickInputPopupSpec spec,
        Action drawContent,
        Func<bool>? canCommit,
        Action? onCommit)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "QuickInputPopupSpec.Key.Value must not be empty.",
                nameof(spec)
            );
        if (drawContent is null)
            throw new ArgumentNullException(nameof(drawContent));

        var key = spec.Key.Value;
        var wasOpen = WasOpen.TryGetValue(key, out var open) && open;

        // Open only on the frame the caller begins drawing it. An open ImGui
        // popup stays open until closed; re-opening every frame would defeat
        // the outside-click close (ImGui closes the popup in NewFrame, then
        // this would reopen it in the same frame).
        if (!wasOpen)
            ImGui.OpenPopup(key);

        OverlayMaskScope? mask = null;
        Vector2 windowCenter = default;
        try
        {
            mask = OverlayMaskComponent.Begin(
                new OverlayMaskSpec(
                    key,
                    ShowOverlay: true,
                    spec.OverlayAlpha
                )
            );
            windowCenter = mask.WindowMin + mask.WindowSize * 0.5f;
        }
        finally
        {
            mask?.Dispose();
        }

        var background = SurfaceComponent.ResolveColors(spec.State);
        var rounding = SlapCorners.ControlRadius;
        ImGui.SetNextWindowPos(
            windowCenter,
            ImGuiCond.Always,
            new Vector2(0.5f, 0.5f)
        );
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, Vector4.Zero);
        var visible = ImGui.BeginPopup(
            key,
            ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoMove
        );
        try
        {
            if (visible)
            {
                WasOpen[key] = true;
                var pos = ImGui.GetWindowPos();
                var size = ImGui.GetWindowSize();
                var drawList = ImGui.GetWindowDrawList();
                var shadow = LayeredShadowSpec.ResolveStandard(spec.Shadow);
                if (shadow.Layers > 0)
                {
                    drawList.PushClipRect(
                        new Vector2(0f, pos.Y),
                        ImGui.GetIO().DisplaySize,
                        false
                    );
                    LayeredShadow.Draw(
                        drawList,
                        pos,
                        pos + size,
                        rounding,
                        shadow
                    );
                    drawList.PopClipRect();
                }
                SurfaceComponent.DrawFrostedPanelBackground(drawList, pos, pos + size, rounding);
                drawList.AddRectFilled(
                    pos,
                    pos + size,
                    ImGui.ColorConvertFloat4ToU32(background),
                    rounding
                );
                if (ThemeScope.Resolved.IsTransparentTheme)
                {
                    SurfaceComponent.DrawPanelBorder(
                        drawList,
                        pos,
                        pos + size,
                        ThemeScope.Resolved.Border,
                        rounding);
                }

                var padding = MetricsScope.ScalePadding(
                    spec.Padding ?? new Vector2(SlapPx.Space6, SlapPx.Space6));
                ImGui.SetCursorScreenPos(pos + padding);
                drawContent();
                ImGui.SetCursorScreenPos(ImGui.GetItemRectMax() + padding);
            }
            else if (wasOpen)
            {
                WasOpen.Remove(key);
                if (
                    spec.CommitOnOutsideClick
                    && (canCommit?.Invoke() ?? true)
                )
                {
                    onCommit?.Invoke();
                    return new QuickInputPopupResult(false, true);
                }
            }
        }
        finally
        {
            if (visible)
                ImGui.EndPopup();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(4);
        }

        return new QuickInputPopupResult(visible, false);
    }
}

internal static partial class Slap
{
    /// <summary>
    /// Draw a quick-input dialog centered over a modal-style dim mask. The
    /// panel auto-fits its content with a 6px default panel padding. The component owns
    /// outside-click commit and close-transition handling; callers provide the
    /// content slot, validity predicate and commit action. Escape is not
    /// intercepted (the host window consumes it when no input is focused).
    /// Must be drawn at the top-level host window scope.
    /// </summary>
    public static QuickInputPopupResult QuickInputPopup(
        QuickInputPopupSpec spec,
        Action drawContent,
        Func<bool>? canCommit = null,
        Action? onCommit = null) =>
        QuickInputPopupComponent.Draw(
            spec,
            drawContent,
            canCommit,
            onCommit);

    /// <summary>
    /// Clears the static open-transition state of quick-input popups. Host
    /// windows should call this when they hide, so a quick input left open at
    /// hide time cannot re-enter its close transition (or commit) on the next
    /// open.
    /// </summary>
    public static void ResetQuickInputPopupState() =>
        QuickInputPopupComponent.Reset();
}
