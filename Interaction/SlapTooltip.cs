using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal enum SlapTooltipDirection
{
    Below,
    Above,
    Right,
    Left,
}

internal static class SlapTooltip
{
    private const float TooltipGap = SlapPx.Space4;

    private static readonly Vector2 Padding = new(SlapPx.Space9, SlapPx.Space5);
    private static readonly LayeredShadowSpec DefaultShadow =
        LayeredShadowSpec.Standard with { PushFullscreenClip = true };

    public static bool TryShow(
        bool hovered,
        string? tooltip,
        Vector2? hoverMin = null,
        Vector2? hoverMax = null,
        SlapTooltipDirection? preferredDirection = null)
    {
        if (!hovered || string.IsNullOrWhiteSpace(tooltip))
            return false;

        var rect = ResolveHoverRect(hoverMin, hoverMax);
        DrawThemedTooltip(tooltip, rect.Min, rect.Max, preferredDirection);
        return true;
    }

    public static bool TryShowFirst(
        ref bool tooltipShown,
        bool hovered,
        string? tooltip,
        Vector2? hoverMin = null,
        Vector2? hoverMax = null,
        SlapTooltipDirection? preferredDirection = null)
    {
        if (tooltipShown || !TryShow(hovered, tooltip, hoverMin, hoverMax, preferredDirection))
            return false;

        tooltipShown = true;
        return true;
    }

    private static (Vector2 Min, Vector2 Max) ResolveHoverRect(Vector2? hoverMin, Vector2? hoverMax)
    {
        if (hoverMin.HasValue
            && hoverMax.HasValue
            && hoverMax.Value.X > hoverMin.Value.X
            && hoverMax.Value.Y > hoverMin.Value.Y)
        {
            return (hoverMin.Value, hoverMax.Value);
        }

        var itemMin = ImGui.GetItemRectMin();
        var itemMax = ImGui.GetItemRectMax();
        if (itemMax.X > itemMin.X && itemMax.Y > itemMin.Y)
            return (itemMin, itemMax);

        var mouse = ImGui.GetIO().MousePos;
        return (mouse, mouse);
    }

    private static void DrawThemedTooltip(
        string text,
        Vector2 hoverMin,
        Vector2 hoverMax,
        SlapTooltipDirection? preferredDirection)
    {
        var rt = ThemeScope.Resolved;

        using var fontScope = TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold);

        var windowPadding = MetricsScope.ScalePadding(Padding);
        // Half the corner radius is a deliberate compromise: enough to keep a
        // single-line tooltip clear of the rounded corners without over-padding.
        windowPadding = new Vector2(
            MathF.Max(windowPadding.X, SlapCorners.ControlRadius * 0.5f),
            windowPadding.Y);
        var border = MetricsScope.BorderThickness;
        var tooltipSize = ImGui.CalcTextSize(text)
            + (windowPadding * 2f)
            + new Vector2(border * 2f, border * 2f);
        var viewport = ResolveViewportBounds();
        var position = ResolveTooltipPosition(
            hoverMin,
            hoverMax,
            tooltipSize,
            viewport.Min,
            viewport.Max,
            preferredDirection);

        ImGui.PushStyleColor(ImGuiCol.Text, rt.Body);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, rt.Surface);
        ImGui.PushStyleColor(ImGuiCol.Border, ThemeScope.Resolved.Border);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, border);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, windowPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, SlapCorners.ControlRadius);

        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGuiP.BeginTooltipEx(ImGuiTooltipFlags.OverridePreviousTooltip, ImGuiWindowFlags.None);
        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        ImGui.TextUnformatted(text);
        drawList.ChannelsSetCurrent(0);
        var min = ImGui.GetWindowPos();
        var max = min + ImGui.GetWindowSize();

        SurfaceComponent.DrawFrostedPanelBackground(drawList, min, max, SlapCorners.ControlRadius);
        drawList.ChannelsMerge();

        // Shadow — fullscreen clip so it extends beyond the tooltip rect.
        // All shadow layers are outside [min, max]; no bg/text redraw needed.
        LayeredShadow.Draw(
            drawList,
            min,
            max,
            SlapCorners.ControlRadius,
            DefaultShadow);
        if (rt.IsTransparentTheme)
        {
            SurfaceComponent.DrawPanelBorder(
                drawList,
                min,
                max,
                rt.Border,
                SlapCorners.ControlRadius);
        }

        ImGui.EndTooltip();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(3);
    }

    private static (Vector2 Min, Vector2 Max) ResolveViewportBounds()
    {
        var viewport = ImGui.GetMainViewport();
        var min = viewport.WorkPos;
        var size = viewport.WorkSize;
        if (size.X <= 0f || size.Y <= 0f)
        {
            min = Vector2.Zero;
            size = ImGui.GetIO().DisplaySize;
        }

        return (min, min + size);
    }

    private static readonly SlapTooltipDirection[] DefaultDirections =
    [
        SlapTooltipDirection.Above,
        SlapTooltipDirection.Below,
        SlapTooltipDirection.Right,
        SlapTooltipDirection.Left,
    ];

    private static Vector2 ResolveTooltipPosition(
        Vector2 hoverMin,
        Vector2 hoverMax,
        Vector2 tooltipSize,
        Vector2 viewportMin,
        Vector2 viewportMax,
        SlapTooltipDirection? preferredDirection)
    {
        var gap = MetricsScope.ScaleGap(TooltipGap);
        var edge = new Vector2(gap);
        var viewportInsetMin = viewportMin + edge;
        var viewportInsetMax = viewportMax - edge;
        var order = ResolveDirectionOrder(preferredDirection);
        var candidates = new (Vector2 Position, bool Vertical)[order.Length];
        for (var i = 0; i < order.Length; i++)
            candidates[i] = ResolveCandidate(order[i], hoverMin, hoverMax, tooltipSize, gap);

        foreach (var candidate in candidates)
        {
            var shifted = ShiftToFit(candidate.Position, candidate.Vertical, tooltipSize, viewportInsetMin, viewportInsetMax);
            if (FullyInside(shifted, tooltipSize, viewportInsetMin, viewportInsetMax))
                return ClampFinal(shifted, tooltipSize, viewportMin, viewportMax);
        }

        var best = candidates[0].Position;
        for (var i = 1; i < candidates.Length; i++)
        {
            if (IsBetterCandidate(candidates[i].Position, best, tooltipSize, viewportMin, viewportMax))
                best = candidates[i].Position;
        }

        return ClampFinal(best, tooltipSize, viewportMin, viewportMax);
    }

    private static SlapTooltipDirection[] ResolveDirectionOrder(SlapTooltipDirection? preferred)
    {
        if (preferred is not { } p)
            return DefaultDirections;

        var result = new SlapTooltipDirection[DefaultDirections.Length];
        result[0] = p;
        var index = 1;
        foreach (var direction in DefaultDirections)
        {
            if (direction != p)
                result[index++] = direction;
        }

        return result;
    }

    private static (Vector2 Position, bool Vertical) ResolveCandidate(
        SlapTooltipDirection direction,
        Vector2 hoverMin,
        Vector2 hoverMax,
        Vector2 tooltipSize,
        float gap) => direction switch
    {
        SlapTooltipDirection.Below => (new Vector2(CenterX(hoverMin, hoverMax, tooltipSize.X), hoverMax.Y + gap), true),
        SlapTooltipDirection.Above => (new Vector2(CenterX(hoverMin, hoverMax, tooltipSize.X), hoverMin.Y - gap - tooltipSize.Y), true),
        SlapTooltipDirection.Right => (new Vector2(hoverMax.X + gap, hoverMin.Y), false),
        SlapTooltipDirection.Left => (new Vector2(hoverMin.X - gap - tooltipSize.X, hoverMin.Y), false),
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    private static float CenterX(Vector2 hoverMin, Vector2 hoverMax, float tooltipWidth) =>
        hoverMin.X + ((hoverMax.X - hoverMin.X - tooltipWidth) * 0.5f);

    private static Vector2 ShiftToFit(
        Vector2 position,
        bool vertical,
        Vector2 size,
        Vector2 viewportMin,
        Vector2 viewportMax)
    {
        if (vertical)
            return new Vector2(ClampAxis(position.X, size.X, viewportMin.X, viewportMax.X), position.Y);

        return new Vector2(position.X, ClampAxis(position.Y, size.Y, viewportMin.Y, viewportMax.Y));
    }

    private static bool FullyInside(Vector2 position, Vector2 size, Vector2 min, Vector2 max) =>
        Overflow(position, size, min, max) == 0f;

    private static bool IsBetterCandidate(
        Vector2 candidate,
        Vector2 current,
        Vector2 size,
        Vector2 viewportMin,
        Vector2 viewportMax) =>
        Overflow(candidate, size, viewportMin, viewportMax)
        < Overflow(current, size, viewportMin, viewportMax);

    private static Vector2 ClampFinal(
        Vector2 position,
        Vector2 size,
        Vector2 viewportMin,
        Vector2 viewportMax)
    {
        if (Fits(size, viewportMin, viewportMax))
            position = ClampToRect(position, size, viewportMin, viewportMax);

        return position;
    }

    private static bool Fits(Vector2 size, Vector2 min, Vector2 max) =>
        size.X <= max.X - min.X && size.Y <= max.Y - min.Y;

    private static Vector2 ClampToRect(Vector2 position, Vector2 size, Vector2 min, Vector2 max) =>
        new(
            ClampAxis(position.X, size.X, min.X, max.X),
            ClampAxis(position.Y, size.Y, min.Y, max.Y));

    private static float ClampAxis(float position, float size, float min, float max) =>
        size >= max - min ? min : Math.Clamp(position, min, max - size);

    private static float Overflow(Vector2 position, Vector2 size, Vector2 min, Vector2 max) =>
        MathF.Max(0f, min.X - position.X)
        + MathF.Max(0f, position.X + size.X - max.X)
        + MathF.Max(0f, min.Y - position.Y)
        + MathF.Max(0f, position.Y + size.Y - max.Y);

}
