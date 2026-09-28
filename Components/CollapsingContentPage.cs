using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;

namespace SlapSurface;

/// <summary>
/// Content slot context for a content page: the logical content zone
/// (scroll-aware) plus the page's scroll surface metrics needed to anchor
/// screen-space overlays to the visible content area.
/// </summary>
internal readonly record struct CollapsingContentPageContext(
    ContentZoneContext Zone,
    Vector2 ScrollChildMin,
    Vector2 ScrollChildMax)
{
    public Vector2 Min => Zone.Min;
    public Vector2 Max => Zone.Max;
    public Vector2 Size => Zone.Size;
    public float Width => Zone.Width;
    public float Height => Zone.Height;
    public float CenterY => Zone.CenterY;
    public Vector4 Background => Zone.Background;

    public static implicit operator ContentZoneContext(
        CollapsingContentPageContext context) => context.Zone;
}

/// <summary>
/// Content page skeleton: optional fixed title row, optional fixed view strip
/// row, optional fixed actions row, and the remaining scrollable content.
/// Header rows stay fixed above the scroll surface and never collapse.
/// </summary>
internal static class CollapsingContentPageComponent
{
    internal static void Draw(
        ContentCardScope card,
        Action<ContentZoneContext>? drawTitle,
        Action<ContentZoneContext>? drawViewStrip,
        Action<ContentZoneContext>? drawActions,
        Action<CollapsingContentPageContext> drawContent,
        bool resetScroll,
        float? contentRightInset,
        FloatingCommandSlot? floatingCommand)
    {
        var height = MathF.Max(0f, card.RemainingHeight);
        if (height <= 0f)
            return;

        card.Zone(height, zone => DrawInZone(
            zone,
            drawTitle,
            drawViewStrip,
            drawActions,
            drawContent,
            resetScroll,
            contentRightInset,
            floatingCommand));
    }

    private static void DrawInZone(
        ContentZoneContext zone,
        Action<ContentZoneContext>? drawTitle,
        Action<ContentZoneContext>? drawViewStrip,
        Action<ContentZoneContext>? drawActions,
        Action<CollapsingContentPageContext> drawContent,
        bool resetScroll,
        float? contentRightInset,
        FloatingCommandSlot? floatingCommand)
    {
        var unit = MetricsScope.UnitHeight;
        var cardGap = Slap.Scale(10f);
        var headerBottomGap = cardGap - Slap.Scale(2f);
        var hasFloatingCommand = floatingCommand is { Draw: not null };

        var hasTitle = drawTitle != null;
        var hasViewStrip = drawViewStrip != null;
        var hasActions = drawActions != null;
        var titleHeight = hasTitle ? unit : 0f;
        var titleGap = hasTitle ? headerBottomGap : 0f;
        var viewStripHeight = hasViewStrip ? unit : 0f;
        var viewGap = hasViewStrip ? cardGap : 0f;
        var actionsHeight = hasActions ? unit : 0f;
        var actionsGap = hasActions ? cardGap : 0f;

        var headerHeight = titleHeight
            + titleGap
            + viewStripHeight
            + viewGap
            + actionsHeight
            + actionsGap;
        var scrollHeight = MathF.Max(0f, zone.Height - headerHeight);

        var savedCursor = ImGui.GetCursorScreenPos();
        ImGui.SetCursorScreenPos(zone.Min);

        var y = zone.Min.Y;
        if (hasTitle)
        {
            var titleZone = new ContentZoneContext(
                new Vector2(zone.Min.X, y),
                new Vector2(zone.Max.X, y + titleHeight),
                zone.Background);
            ImGui.SetCursorScreenPos(titleZone.Min);
            drawTitle!(titleZone);
            y += titleHeight + titleGap;
        }

        if (hasViewStrip)
        {
            var viewStripZone = new ContentZoneContext(
                new Vector2(zone.Min.X, y),
                new Vector2(zone.Max.X, y + viewStripHeight),
                zone.Background);
            ImGui.SetCursorScreenPos(viewStripZone.Min);
            drawViewStrip!(viewStripZone);
            y += viewStripHeight + viewGap;
        }

        if (hasActions)
        {
            var actionsZone = new ContentZoneContext(
                new Vector2(zone.Min.X, y),
                new Vector2(zone.Max.X, y + actionsHeight),
                zone.Background);
            ImGui.SetCursorScreenPos(actionsZone.Min);
            drawActions!(actionsZone);
            y += actionsHeight + actionsGap;
        }

        if (scrollHeight <= 0f)
        {
            ImGui.SetCursorScreenPos(savedCursor + new Vector2(0f, zone.Height));
            return;
        }

        var scrollMin = new Vector2(zone.Min.X, y);
        ImGui.SetCursorScreenPos(scrollMin);
        var scrollExtension = ImGui.GetStyle().ScrollbarSize
            + MetricsScope.Scale(SlapPx.Space6);
        using (zone.ScrollArea(
                   "##v3ContentPageScroll",
                   scrollHeight,
                   widthExtension: scrollExtension,
                   drawEdgeFade: !hasFloatingCommand))
        {
            if (resetScroll)
                ImGui.SetScrollY(0f);

            var contentMin = ImGui.GetCursorScreenPos();
            var contentRightInsetPx = contentRightInset.HasValue
                ? MetricsScope.Scale(contentRightInset.Value)
                : 0f;
            var contentMax = new Vector2(
                contentMin.X + zone.Width - contentRightInsetPx,
                contentMin.Y + scrollHeight);
            var childMin = ImGui.GetWindowPos();
            var childMax = childMin + ImGui.GetWindowSize();

            drawContent(new CollapsingContentPageContext(
                new ContentZoneContext(
                    contentMin,
                    contentMax,
                    zone.Background),
                childMin,
                childMax));
        }

        if (floatingCommand is { Draw: not null } command)
            FloatingCommandComponent.DrawOverlay(zone, command);

        ImGui.SetCursorScreenPos(savedCursor + new Vector2(0f, zone.Height));
    }
}
