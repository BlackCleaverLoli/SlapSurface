using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace SlapSurface;

internal static partial class Slap
{
    // --- Panel ---

    public static IDisposable BeginPanel(PanelSpec spec) => PanelComponent.Begin(spec);

    // --- Surface Kit ---

    public static void Surface(SurfaceSpec spec, Action<SurfaceContentContext> drawContent) =>
        SurfaceComponent.Draw(spec, drawContent);

    public static void Section(SectionSpec spec, Action<SurfaceContentContext> drawContent) =>
        SectionComponent.Draw(spec, drawContent);

    /// <summary>Convenience section: key + title + content, everything else default.
    /// <code>
    /// Slap.Section("routes", "航线列表", content => { ... })
    /// Slap.Section("routes", "航线列表", content => { ... }, subtitle: "近期", icon: FontAwesomeIcon.Ship)
    /// </code></summary>
    public static void Section(
        ControlKey key,
        string title,
        Action<SurfaceContentContext> drawContent,
        string? subtitle = null,
        FontAwesomeIcon? icon = null,
        Vector2? padding = null) =>
        SectionComponent.Draw(
            new SectionSpec(
                key,
                SurfaceSize.FillAvailable,
                new SurfaceHeaderSpec(key, title, subtitle, icon),
                ControlState.None,
                Padding: padding),
            drawContent);

    /// <summary>Natural header height in scaled pixels for layout pre-measurement.</summary>
    public static float ResolveSurfaceHeaderHeight(SurfaceHeaderSpec spec) =>
        SurfaceHeaderComponent.ResolveHeight(spec);

    public static void SurfaceHeader(SurfaceHeaderSpec spec) => SurfaceHeaderComponent.Draw(spec);

    /// <summary>Convenience header: key + title, optional subtitle / icon.</summary>
    public static void SurfaceHeader(
        ControlKey key,
        string title,
        string? subtitle = null,
        FontAwesomeIcon? icon = null) =>
        SurfaceHeaderComponent.Draw(new SurfaceHeaderSpec(key, title, subtitle, icon));

    public static void IconText(IconTextSpec spec) => IconTextComponent.Draw(spec);

    // --- Badge ---

    public static ControlResult Badge(BadgeSpec spec) => BadgeComponent.Draw(spec);

    /// <summary>
    /// Draw measured badges with automatic row wrapping and anchoring, and report
    /// which badge received interaction this frame.
    /// </summary>
    public static BadgeWrapResult BadgeWrap(BadgeWrapSpec spec) => BadgeWrapComponent.Draw(spec);

    /// <summary>Resolve a badge's natural width under its configured font.</summary>
    public static float ResolveBadgeWidth(BadgeSpec spec) => BadgeComponent.ResolveWidth(spec);

    // --- IconTwoLine ---

    public static void IconTwoLine(IconTwoLineSpec spec) => IconTwoLineComponent.Draw(spec);

    public static float ResolveIconTwoLineWidth(IconTwoLineSpec spec) => IconTwoLineComponent.ResolveWidth(spec);

    public static float ResolveIconTwoLineHeight(IconTwoLineSpec spec) => IconTwoLineComponent.ResolveHeight(spec);

    public static string? ResolveIconTwoLineTooltip(IconTwoLineSpec spec, float startX) =>
        IconTwoLineComponent.ComposeTooltip(spec, startX);

    // --- RowIconTwoLine ---

    /// <summary>
    /// Draw a leading-icon + game-icon + two-line-text block inside a
    /// <see cref="SurfaceRowContext"/>. All elements are vertically centered
    /// within the row's content area. Non-interactive; for use inside
    /// <see cref="SurfaceList"/> row content callbacks.
    /// </summary>
    public static void RowIconTwoLine(SurfaceRowContext context, RowIconTwoLineSpec spec)
        => RowIconTwoLineComponent.Draw(context, spec);

    // --- SettingPage ---

    /// <summary>
    /// Secondary-tab settings page layout: a horizontal section tab strip
    /// above a single scrollable content area with all sections stacked.
    /// Clicking a tab scrolls to the section; the active tab follows the
    /// scroll position.
    /// </summary>
    public static void SettingPage(
        SettingPageSpec spec,
        ContentZoneContext zone,
        Action<SettingSectionSpec, ContentZoneContext> drawSection) =>
        SettingPageComponent.Draw(spec, zone, drawSection);

    // --- SettingSubSection ---

    /// <summary>
    /// Begin a sub-section scope inside a settings page. Draws a sub-title at
    /// Level1 indent, then returns a scope whose leaf methods
    /// (<see cref="SettingSubSectionScope.CheckboxRow"/> etc.) automatically
    /// indent at Level2. Call <see cref="SettingSubSectionScope.BeginSubSection"/>
    /// on the returned scope for deeper nesting. Dispose to release.
    /// </summary>
    public static SettingSubSectionScope BeginSettingSubSection(string title) =>
        SettingSubSectionComponent.Begin(title);

    // --- Drawer ---

    /// <summary>
    /// Collapsible drawer. Wraps <see cref="ImGui.CollapsingHeader"/> with
    /// SlapSurface theme colors, hover outline, and tooltip.
    /// <code>
    /// var dr = Slap.Drawer(new DrawerSpec("key", "Title", Tooltip: "说明"));
    /// if (dr.IsOpen) { /* draw content */ }
    /// </code>
    /// </summary>
    public static DrawerResult Drawer(DrawerSpec spec) =>
        DrawerComponent.Draw(spec);

    /// <summary>
    /// Forces every drawer to re-apply its <see cref="DrawerSpec.DefaultOpen"/>
    /// on the next draw, discarding user toggled state.
    /// </summary>
    public static void ResetDrawerState() =>
        DrawerComponent.Reset();
}
