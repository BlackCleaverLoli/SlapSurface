using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Left-anchored temporary sidebar overlay: a floating full-sidebar panel over
/// a dim mask. Reuses <see cref="SidebarScope"/> so callers can render the same
/// sidebar body both in-flow and as a temporary overlay.
/// </summary>
internal readonly record struct SidebarOverlaySpec(
    ControlKey Key,
    Vector2 Min,
    Vector2 Max,
    bool SurfaceBg = true,
    float OverlayAlpha = 0.33f
);

/// <summary>Scope for a temporary floating sidebar overlay.</summary>
internal sealed class SidebarOverlayScope : IDisposable
{
    private bool _disposed;
    private readonly OverlayMaskScope? _mask;
    private readonly SidebarScope? _sidebar;
    private readonly bool _idPushed;

    public bool IsOpen { get; }
    public bool CloseRequested { get; }

    /// <summary>Sidebar content scope. Null when <see cref="IsOpen"/> is false.</summary>
    public SidebarScope? Sidebar => _sidebar;

    internal SidebarOverlayScope(
        bool isOpen,
        bool closeRequested,
        OverlayMaskScope? mask,
        SidebarScope? sidebar,
        bool idPushed)
    {
        IsOpen = isOpen;
        CloseRequested = closeRequested;
        _mask = mask;
        _sidebar = sidebar;
        _idPushed = idPushed;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sidebar?.Dispose();
        _mask?.Dispose();
        if (_idPushed)
            ImGui.PopID();
    }
}

internal static class SidebarOverlayComponent
{
    public static SidebarOverlayScope Begin(SidebarOverlaySpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("SidebarOverlaySpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        var mask = OverlayMaskComponent.Begin(
            new OverlayMaskSpec(spec.Key.Value, ShowOverlay: true, spec.OverlayAlpha));
        if (!mask.IsOpen)
        {
            mask.Dispose();
            ImGui.PopID();
            return new SidebarOverlayScope(false, false, null, null, false);
        }

        var panelMin = spec.Min;
        var panelMax = spec.Max;
        var panelSize = panelMax - panelMin;
        if (panelSize.X <= 0f || panelSize.Y <= 0f)
        {
            mask.Dispose();
            ImGui.PopID();
            return new SidebarOverlayScope(false, false, null, null, false);
        }

        var rt = ThemeScope.Resolved;
        var rounding = SlapCorners.ControlRadius;
        var drawList = ImGui.GetWindowDrawList();

        if (spec.SurfaceBg)
        {
            LayeredShadow.Draw(
                drawList,
                panelMin,
                panelMax,
                rounding,
                LayeredShadowSpec.ResolveStandard(default));
            SurfaceComponent.DrawFrostedPanelBackground(drawList, panelMin, panelMax, rounding);
            drawList.AddRectFilled(
                panelMin,
                panelMax,
                ImGui.ColorConvertFloat4ToU32(rt.Surface),
                rounding,
                ImDrawFlags.RoundCornersAll);
            if (rt.IsTransparentTheme)
            {
                SurfaceComponent.DrawPanelBorder(
                    drawList,
                    panelMin,
                    panelMax,
                    rt.Border,
                    rounding);
            }
        }

        var closeRequested = mask.CaptureOutsideClick(panelMin, panelMax);

        var padding = MetricsScope.ScalePadding(
            new Vector2(ShellComponent.SidebarSurfacePaddingX, ShellComponent.SidebarSurfacePaddingY));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        if (spec.SurfaceBg)
            ImGui.PushStyleColor(ImGuiCol.ChildBg, rt.Surface);

        ImGui.SetCursorScreenPos(panelMin);
        var panelVisible = ImGui.BeginChild(
            $"##{spec.Key.Value}SidebarOverlayPanel",
            panelSize,
            false,
            ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!panelVisible)
        {
            ImGui.EndChild();
            if (spec.SurfaceBg)
                ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            mask.Dispose();
            ImGui.PopID();
            return new SidebarOverlayScope(false, false, null, null, false);
        }

        var sidebar = new SidebarScope(
            styleVarCount: 1,
            styleColorCount: spec.SurfaceBg ? 1 : 0,
            collapsed: false,
            canExpand: false,
            isTemporaryOverlay: true);

        return new SidebarOverlayScope(true, closeRequested, mask, sidebar, true);
    }
}
