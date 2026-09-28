using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlapSurface;

internal static partial class Slap
{
    // --- Content Card ---

    public static ContentCardScope BeginContentCard(ContentCardSpec spec) => ContentCardComponent.Begin(spec);

    /// <summary>Convenience content card: key + zone callback, auto-disposes the scope.</summary>
    public static void ContentCard(
        ControlKey key,
        Action<ContentCardScope> drawZones,
        Vector2? padding = null)
    {
        using var card = ContentCardComponent.Begin(new ContentCardSpec(
            key, ControlState.None, Padding: padding));
        drawZones(card);
    }

    // --- Page layouts ---

    /// <summary>
    /// Neutral content page skeleton: optional fixed title row, optional fixed
    /// view strip row, optional fixed actions row, and the remaining scrollable
    /// content. All header rows stay fixed above the scroll surface; only the
    /// content scrolls.
    /// </summary>
    /// <param name="card">The content card scope.</param>
    /// <param name="drawTitle">
    /// Optional fixed title row. <c>null</c> hides the title entirely.
    /// </param>
    /// <param name="drawViewStrip">
    /// Optional fixed view strip row (e.g. a tab strip). <c>null</c> omits it.
    /// </param>
    /// <param name="drawActions">
    /// Optional fixed actions row. <c>null</c> omits the row entirely, e.g.
    /// when its controls are merged into the view strip row.
    /// </param>
    /// <param name="drawContent">
    /// The scrollable content slot; receives the scroll surface metrics for
    /// overlay placement.
    /// </param>
    /// <param name="resetScroll">
    /// When <c>true</c>, the page scroll is reset to the top this frame.
    /// </param>
    /// <param name="contentRightInset">
    /// Optional final-zone right inset in unscaled pixels. <c>null</c> keeps
    /// the card default; <c>0</c> retains only base card padding.
    /// </param>
    /// <param name="floatingCommand">
    /// Optional bottom floating command. When present, the page draws it as an
    /// overlay over the scrollable content's lower edge and disables the bottom
    /// scroll fade; callers pass its resolved height as content occlusion.
    /// </param>
    public static void ContentPage(
        ContentCardScope card,
        Action<ContentZoneContext>? drawTitle,
        Action<ContentZoneContext>? drawViewStrip,
        Action<ContentZoneContext>? drawActions,
        Action<CollapsingContentPageContext> drawContent,
        bool resetScroll = false,
        float? contentRightInset = null,
        FloatingCommandSlot? floatingCommand = null)
        => CollapsingContentPageComponent.Draw(
            card,
            drawTitle,
            drawViewStrip,
            drawActions,
            drawContent,
            resetScroll,
            contentRightInset,
            floatingCommand);

    /// <summary>Resolve the full height covered by a floating command fade.</summary>
    public static float ResolveFloatingCommandZoneHeight(int rowCount = 1) =>
        FloatingCommandComponent.ResolveLayout(rowCount).ZoneHeight;

    /// <summary>
    /// Detail page with a floating header that collapses against the page's
    /// single scroll surface.
    /// </summary>
    public static void CollapsingHeaderDetailPage(
        ContentCardScope card,
        CollapsingHeaderDetailPageSpec spec,
        Action<CollapsingHeaderDetailMediaContext> drawMedia,
        Action<ResponsiveSlotGroup>? collectTitleControls,
        Action<ResponsiveRowBuilder>? collectActions,
        Action<ContentZoneContext> drawContent,
        FloatingCommandSlot? floatingCommand = null) =>
        CollapsingHeaderDetailPageComponent.Draw(
            card,
            spec,
            drawMedia,
            collectTitleControls,
            collectActions,
            drawContent,
            floatingCommand);

    /// <summary>
    /// Detail page layout: nav row → optional auto-measured ID row → remaining content.
    /// <para>
    /// The nav row is <c>UnitHeight + gap</c>. The ID row height is
    /// auto-measured from the cursor advancement during
    /// <paramref name="drawIdRow"/> — the callback only draws; the
    /// framework measures and allocates the exact height consumed.
    /// The content zone takes all remaining card space.
    /// </para>
    /// </summary>
    /// <param name="card">The content card scope.</param>
    /// <param name="drawNav">Draws the navigation row (back button, title, etc.).</param>
    /// <param name="drawIdRow">Optionally draws the ID row (icon, name, tags, etc.).</param>
    /// <param name="drawContent">Draws the remaining content zone.</param>
    public static void DetailPage(
        ContentCardScope card,
        Action<ContentZoneContext> drawNav,
        Action<ContentZoneContext>? drawIdRow,
        Action<ContentZoneContext> drawContent)
    {
        var gap = Slap.Scale(SlapPx.Space8);
        var navHeight = Slap.UnitHeight + gap;

        card.Zone(navHeight, drawNav);
        if (drawIdRow != null)
            card.ZoneAuto(drawIdRow);

        var scrollHeight = MathF.Max(0f, card.RemainingHeight);
        if (scrollHeight > 0f)
            card.Zone(scrollHeight, drawContent);
    }

    public static void DetailScroll(
        ContentZoneContext zone,
        Action drawScrollContent,
        float contentIndentX = 0f,
        string scrollId = "##v3DetailScroll")
    {
        using var scroll = zone.BeginDetailScroll(scrollId, contentIndentX);
        drawScrollContent();
        var childMin = ImGui.GetWindowPos();
        var childMax = childMin + ImGui.GetWindowSize();
        ScrollFade.DrawVertical(childMin, childMax);
    }
}
