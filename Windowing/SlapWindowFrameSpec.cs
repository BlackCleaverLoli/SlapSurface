using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

namespace SlapSurface;

internal enum SlapWindowFramePreset
{
    Shell = 0,
    FrostedShell = 1,
}

internal readonly record struct SlapTitleBarNavigationSpec(
    bool Visible = false,
    bool BackEnabled = false,
    bool ForwardEnabled = false,
    string? BackTooltip = null,
    string? ForwardTooltip = null
);

internal readonly record struct SlapTitleBarNavigationResult(
    bool BackClicked = false,
    bool ForwardClicked = false
);

/// <summary>
/// Optional leading texture for the title bar.
/// </summary>
internal readonly record struct SlapTitleBarIconSpec(
    IDalamudTextureWrap? Texture = null,
    uint GameIconId = 0,
    float? Size = null
);

/// <summary>
/// Top-level window skin contract. A caller supplies theme, metrics, and slot
/// content; SlapSurface owns native title-bar styling, shell surfaces, frosted
/// backdrop, shadow, and state style protocol.
/// </summary>
internal readonly record struct SlapWindowFrameSpec(
    ControlKey Key,
    string Title,
    SlapTheme Theme,
    SlapMetricsConfig Metrics,
    SlapWindowFramePreset Preset = SlapWindowFramePreset.FrostedShell,
    float TitleBarHeight = 3f,
    float BodyGap = 4f,
    bool ShowCloseButton = true,
    int TitleBarButtonReserveCount = 0,
    float SidebarWidth = 6f,
    float ShellGap = 4f,
    bool Separator = true,
    bool SurfaceBg = true,
    LayeredShadowSpec Shadow = default,
    float BackdropExtension = 6f,
    float FrostedOverlayAlpha = SlapWindowFrameDefaults.DefaultFrostedOverlayAlpha,
    float FrostedFillAlpha = SlapWindowFrameDefaults.DefaultFrostedFillAlpha,
    Vector4? FrostedOverlayColor = null,
    Action<ImDrawListPtr, Vector2, Vector2, float, ImDrawFlags, Vector4>? DrawFrostedBackground = null,
    bool DesaturateFrostedFill = false,
    SlapTypographySpec Typography = default,
    IReadOnlyList<string>? AutoMeasureSidebarLabels = null,
    SlapWindowResizeConfig Resize = default,
    SidebarIdentityTabSpec? AutoMeasureSidebarIdentityTab = null,
    SlapSidebarCollapseConfig SidebarCollapse = default,
    SlapTitleBarNavigationSpec TitleBarNavigation = default,
    SlapTitleBarIconSpec? TitleBarIcon = null
)
{
    private const float SidebarMinContentWidthFactor = 1.25f;

    /// <summary>
    /// Resolve the effective sidebar width in frame units. Navigation labels and
    /// <see cref="AutoMeasureSidebarIdentityTab"/> contribute their natural width; when the window
    /// is too narrow to show the full sidebar alongside the scale-adjusted minimum content width,
    /// it auto-collapses to icon-only.
    /// Otherwise <see cref="SidebarWidth"/> is used.
    /// </summary>
    internal SlapSidebarLayout ResolveSidebarLayout()
    {
        var labels = AutoMeasureSidebarLabels ?? Array.Empty<string>();
        var autoMeasured = labels.Count > 0 || AutoMeasureSidebarIdentityTab.HasValue;
        var fullUnits = autoMeasured
            ? ShellComponent.MeasureSidebarWidthUnits(labels, AutoMeasureSidebarIdentityTab)
            : SidebarWidth;
        var compactUnits = ShellComponent.MeasureSidebarWidthUnits(Array.Empty<string>());
        var canExpand = true;
        if (autoMeasured)
        {
            var windowWidth = ImGui.GetWindowSize().X;
            if (windowWidth > 0f)
            {
                var unit = SlapSurface.Metrics.MetricsScope.UnitHeight;
                if (unit > 0f)
                {
                    var fullPx = fullUnits * unit;
                    var gapPx = SlapSurface.Metrics.MetricsScope.Scale(ShellGap);
                    var minContentPx = Resize.ResolveMinSize().X * SidebarMinContentWidthFactor;
                    if (windowWidth - fullPx - gapPx < minContentPx)
                        canExpand = false;
                }
            }
        }

        var collapsed = !canExpand || (SidebarCollapse.Enabled && SidebarCollapse.Collapsed);
        return new SlapSidebarLayout(
            collapsed ? compactUnits : fullUnits,
            fullUnits,
            collapsed,
            canExpand
        );
    }
}

internal static class SlapWindowFrameDefaults
{
    public const float DefaultFrostedOverlayAlpha = 0.12f;
    public const float DefaultFrostedFillAlpha = 0.33f;

    public const ImGuiWindowFlags WindowFlags =
        ImGuiWindowFlags.NoSavedSettings
        | ImGuiWindowFlags.NoBackground
        | ImGuiWindowFlags.NoScrollbar
        | ImGuiWindowFlags.NoScrollWithMouse
        | ImGuiWindowFlags.NoDocking;

    public static ImGuiWindowFlags WithDefaultFlags(ImGuiWindowFlags flags) =>
        flags | WindowFlags;
}
