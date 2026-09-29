using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct GameIconStripSpec(
    ControlKey Key,
    IReadOnlyList<GameIconSpec> Items,
    float HeightUnits = 1f,
    ControlState State = ControlState.None,
    bool OverflowFade = true,
    Vector4? FadeBackground = null,
    EdgeFadeSpec? EdgeFade = null
);

internal readonly record struct GameIconSpec(
    ControlKey Key,
    string Label = "",
    uint GameIconId = 0,
    bool HighQualityIcon = false,
    string? Tooltip = null,
    ControlState State = ControlState.None,
    bool IconOnly = false,
    Vector4? TextColor = null,
    LayeredShadowSpec Shadow = default,
    GameIconShape Shape = GameIconShape.Standard,
    Vector4? Tint = null,
    FontAwesomeIcon? OverlayIcon = null,
    Vector4? OverlayColor = null,
    bool CornerCheck = false
);

internal static class GameIconStripComponent
{
    private const float ItemGap = SlapPx.Space4;
    private const float TextPaddingX = SlapPx.Space6;
    private const float IconGap = SlapPx.Space4;

    /// <summary>
    /// Draw a single-row icon strip at absolute draw-list coordinates.
    /// No ImGui interaction (invisible buttons, hover, or click tracking).
    /// Used by callers that manage their own coordinate space (e.g. SurfaceList rows).
    /// </summary>
    public static void DrawAbsolute(
        GameIconStripSpec spec,
        ImDrawListPtr drawList,
        Vector2 origin,
        float width,
        float? height = null,
        ControlVisualMode visualMode = ControlVisualMode.Base)
    {
        var h = height ?? MetricsScope.UnitHeight * Normalize(spec.HeightUnits, 1f);
        if (width <= 0f || h <= 0f)
            return;

        var gap = MetricsScope.ScaleGap(ItemGap);
        var itemContentWidth = width;

        var fullWidth = ResolveStripWidth(spec.Items, h, gap);
        var overflows = fullWidth > itemContentWidth;

        if (spec.OverflowFade)
        {
            // Expand the clip rect by the icon shadow padding so the embossed
            // shadow drawn by GameIconComponent (which extends slightly beyond
            // the icon slot) is not clipped. intersectWithCurrent=true ensures
            // the expansion never exceeds the host's existing clip rect. When
            // overflowing, the right edge is hard-clipped to the content width
            // so the right fade's solid edge lines up with the clipped boundary.
            var shadowPad = MetricsScope.Scale(SlapPx.Space2);
            var clipMaxX = origin.X + itemContentWidth + (overflows ? 0f : shadowPad);
            drawList.PushClipRect(
                new Vector2(origin.X - shadowPad, origin.Y - shadowPad),
                new Vector2(clipMaxX, origin.Y + h + shadowPad),
                true
            );
        }

        try
        {
            var stripVisualMode = spec.State.HasFlag(ControlState.Disabled)
                ? ControlVisualMode.Disabled
                : visualMode;
            var x = origin.X;
            for (var i = 0; i < spec.Items.Count; i++)
            {
                var item = spec.Items[i];
                var itemWidth = ResolveItemWidth(item, h);
                if (x >= origin.X + itemContentWidth)
                    break;

                var itemMin = new Vector2(x, origin.Y);
                var itemMax = itemMin + new Vector2(itemWidth, h);
                var textColor = ImGui.ColorConvertFloat4ToU32(
                    item.TextColor ?? ThemeScope.Resolved.Body);
                DrawItemVisual(drawList, item, itemMin, itemMax, textColor, stripVisualMode);
                x += itemWidth + gap;
            }
        }
        finally
        {
            if (spec.OverflowFade)
                drawList.PopClipRect();
        }

        if (spec.OverflowFade && overflows)
        {
            var fadeSpec =
                spec.EdgeFade
                ?? new EdgeFadeSpec(
                    Background: spec.FadeBackground ?? ThemeScope.Resolved.Surface,
                    VerticalInset: MetricsScope.BorderThickness
                );
            if (fadeSpec.IsEnabled)
                EdgeFade.DrawRight(drawList, origin, itemContentWidth, h, fadeSpec);
        }
    }

    /// <summary>Standalone game icon button with hit testing.</summary>
    public static ControlResult DrawGameIconButton(GameIconSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("GameIconSpec.Key.Value must not be empty.", nameof(spec));

        using var _hover = HoverArbitration.Push();
        ImGui.PushID(spec.Key.Value);
        try
        {
            var size = MetricsScope.UnitHeight;
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(size, size);

            var drawList = ImGui.GetWindowDrawList();
            var rounding = GameIconComponent.ResolveRounding(min, max, spec.Shape);
            LayeredShadow.Draw(drawList, min, max, rounding, spec.Shadow);

            var rawClicked = ImGui.InvisibleButton("##icon", new Vector2(size, size));
            var itemMin = ImGui.GetItemRectMin();
            var itemMax = itemMin + new Vector2(size, size);
            var ix = SlapInteraction.Capture(spec.State, rawClicked);

            if (spec.GameIconId != 0)
            {
                GameIconComponent.DrawInSlot(
                    ImGui.GetWindowDrawList(),
                    spec.GameIconId,
                    itemMin,
                    itemMax,
                    spec.HighQualityIcon,
                    shape: spec.Shape,
                    tint: spec.Tint,
                    overlayIcon: spec.OverlayIcon,
                    overlayColor: spec.OverlayColor,
                    visualMode: ix.VisualMode,
                    cornerCheck: spec.CornerCheck
                );
            }

            HoverArbitration.Current.TryShowTooltip(ix.Hovered, spec.Tooltip);
            return ix.ToControlResult(itemMin, itemMax);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static bool DrawItemVisual(
        ImDrawListPtr drawList,
        GameIconSpec item,
        Vector2 itemMin,
        Vector2 itemMax,
        uint textColorU32,
        ControlVisualMode visualMode)
    {
        var resolvedVisualMode = item.State.HasFlag(ControlState.Disabled)
            ? ControlVisualMode.Disabled
            : visualMode;

        if (item.IconOnly && item.GameIconId != 0)
        {
            GameIconComponent.DrawInSlot(
                drawList,
                item.GameIconId,
                itemMin,
                itemMax,
                item.HighQualityIcon,
                shape: item.Shape,
                tint: item.Tint,
                overlayIcon: item.OverlayIcon,
                overlayColor: item.OverlayColor,
                visualMode: resolvedVisualMode,
                cornerCheck: item.CornerCheck
            );
            return false;
        }
        else
        {
            var sizeY = itemMax.Y - itemMin.Y;
            var x = itemMin.X + MetricsScope.ScalePadding(TextPaddingX);
            if (item.GameIconId != 0)
            {
                var iconSize = MathF.Max(0f, sizeY - MetricsScope.ScalePadding(SlapPx.Space4));
                var iconMin = new Vector2(x, itemMin.Y + (sizeY - iconSize) * 0.5f);
                GameIconComponent.Draw(
                    drawList,
                    item.GameIconId,
                    iconMin,
                    iconMin + new Vector2(iconSize),
                    item.HighQualityIcon,
                    shape: item.Shape,
                    tint: item.Tint,
                    overlayIcon: item.OverlayIcon,
                    overlayColor: item.OverlayColor,
                    visualMode: resolvedVisualMode,
                    cornerCheck: item.CornerCheck
                );
                x += iconSize + MetricsScope.ScaleGap(IconGap);
            }

            var degraded = false;
            if (!string.IsNullOrEmpty(item.Label))
            {
                if (x < itemMax.X)
                {
                    var textSize = ImGui.CalcTextSize(item.Label);
                    var textY = itemMin.Y + MathF.Max(0f, (sizeY - textSize.Y) * 0.5f);
                    var clipMin = new Vector2(x, itemMin.Y);
                    var clipMax = new Vector2(
                        itemMax.X - MetricsScope.ScalePadding(TextPaddingX),
                        itemMax.Y
                    );
                    drawList.PushClipRect(clipMin, clipMax, true);
                    drawList.AddText(new Vector2(x, textY), textColorU32, item.Label);
                    drawList.PopClipRect();
                    degraded = textSize.X > clipMax.X - x;
                }
                else
                {
                    degraded = true;
                }
            }

            return degraded;
        }
    }

    private static float ResolveStripWidth(
        IReadOnlyList<GameIconSpec> items,
        float height,
        float gap
    )
    {
        if (items.Count == 0)
            return 0f;

        var width = 0f;
        for (var i = 0; i < items.Count; i++)
            width += ResolveItemWidth(items[i], height);

        return width + gap * (items.Count - 1);
    }

    private static float ResolveItemWidth(GameIconSpec item, float height)
    {
        if (item.IconOnly && item.GameIconId != 0)
            return height;

        var width = MetricsScope.ScalePadding(TextPaddingX * 2f);
        if (item.GameIconId != 0)
            width +=
                MathF.Max(0f, height - MetricsScope.ScalePadding(SlapPx.Space4))
                + MetricsScope.ScaleGap(IconGap);
        if (!string.IsNullOrEmpty(item.Label))
            width += ImGui.CalcTextSize(item.Label).X;
        return width;
    }

    private static float Normalize(float value, float fallback) =>
        value > 0f && !float.IsInfinity(value) ? value : fallback;
}
