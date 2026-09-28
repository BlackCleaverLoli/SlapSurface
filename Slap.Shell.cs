using System;
using System.Collections.Generic;

namespace SlapSurface;

internal static partial class Slap
{
    // --- Shell ---

    public static IDisposable PushWindowFrameHostStyle(
        SlapTheme theme,
        SlapMetricsConfig metrics,
        bool transparentTitleBar = true) =>
        SlapWindowStyleScope.Push(theme, metrics, transparentTitleBar);

    public static IDisposable PushWindowFrameHostStyle(
        SlapTheme theme,
        SlapMetricsConfig metrics,
        SlapTypographySpec typography,
        bool transparentTitleBar = true) =>
        SlapWindowFrameHostStyleScope.Push(theme, metrics, typography, transparentTitleBar);

    public static SlapWindowFrameScope BeginWindowFrame(SlapWindowFrameSpec spec) =>
        SlapWindowFrameComponent.Begin(spec);

    public static SlapWindowFrameScope BeginWindowFrame(
        SlapWindowFrameSpec spec, SlapWindowResizeHandler resizeHandler) =>
        SlapWindowFrameComponent.Begin(spec, resizeHandler);

    public static SlapWindowFrameScope BeginWindowFrame(
        SlapWindowFrameSpec spec,
        SlapWindowResizeHandler resizeHandler,
        SlapSidebarCollapseHandler sidebarCollapseHandler,
        SlapTitleBarNavigationHandler titleBarNavigationHandler) =>
        SlapWindowFrameComponent.Begin(
            spec,
            resizeHandler,
            sidebarCollapseHandler,
            titleBarNavigationHandler);

    public static ShellScope BeginShell(ShellSpec spec) => ShellComponent.Begin(spec);

    public static float MeasureSidebarWidthUnits(IReadOnlyList<string> labels) =>
        ShellComponent.MeasureSidebarWidthUnits(labels);

    public static float MeasureSidebarWidthUnits(
        IReadOnlyList<string> labels,
        SlapTheme theme,
        SlapMetricsConfig metrics,
        SlapTypographySpec typography) =>
        ShellComponent.MeasureSidebarWidthUnits(labels, theme, metrics, typography);

    public static ControlResult SidebarItem(SidebarItemSpec spec) => ShellComponent.DrawSidebarItem(spec);

    public static void SidebarSeparator(SidebarSeparatorSpec spec) => ShellComponent.DrawSidebarSeparator(spec);

    /// <summary>Begin a temporary floating sidebar overlay reusing the sidebar content protocol.</summary>
    public static SidebarOverlayScope BeginSidebarOverlay(SidebarOverlaySpec spec) =>
        SidebarOverlayComponent.Begin(spec);
}
