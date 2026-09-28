using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Standardized row list container. Manages top outline clearance, row spacing,
/// outline batching, ambient hover scope, and optional scroll edge fade.
/// </summary>
internal readonly record struct RowListSpec(
    ControlKey Key,
    int MaxVisibleRows = 80,
    float RowGap = 8f,
    float ScrollbarGap = 6f,
    float OutlineBleedPadding = 2f,
    EdgeFadeSpec? ScrollFade = null,
    string? EmptyText = null,
    string? OverflowText = null,
    /// <summary>
    /// Fixed content width for row layout. When set, rows are sized to this
    /// width regardless of scrollbar visibility. The host window should be
    /// wide enough to accommodate the scrollbar outside this width.
    /// When null, falls back to <c>GetContentRegionAvail().X</c>.
    /// </summary>
    float? ContentWidth = null
);

/// <summary>Context for drawing a single row in a RowList.</summary>
internal readonly record struct RowListRowContext(
    int Index,
    bool Hovered,
    bool Active,
    Vector2 Min,
    Vector2 Max,
    Vector2 ContentMin,
    Vector2 ContentMax
)
{
    public Vector2 Size => Max - Min;
    public Vector2 ContentSize => Vector2.Max(Vector2.Zero, ContentMax - ContentMin);
}

internal static class RowListComponent
{
    private static readonly Vector2 RowPadding = new(10f, 6f);

    public static void Draw<T>(
        RowListSpec spec,
        IReadOnlyList<T> items,
        float rowHeight,
        Action<T, int, RowListRowContext> drawRow)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("RowListSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        try
        {
            using var _hover = HoverArbitration.Push();
            // Clip outline glow only in the scroll direction (Y). X is left at
            // full display width so horizontal glow expansion isn't cut off at
            // the window edges.
            var _winPos = ImGui.GetWindowPos();
            var _winSize = ImGui.GetWindowSize();
            var _displaySize = ImGui.GetIO().DisplaySize;
            HoverArbitration.Current.SetOutlineClipRect(
                new Vector2(0f, _winPos.Y),
                new Vector2(_displaySize.X, _winPos.Y + _winSize.Y));
            var maxRows = Math.Min(items.Count, spec.MaxVisibleRows);

            if (items.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(spec.EmptyText))
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ThemeScope.Resolved.Body);
                    ImGui.PushTextWrapPos();
                    ImGui.TextUnformatted(spec.EmptyText);
                    ImGui.PopTextWrapPos();
                    ImGui.PopStyleColor();
                }

                return;
            }

            var rowGap = MetricsScope.ScaleGap(spec.RowGap);
            var hGap = MetricsScope.ScaleGap(SlapPx.Space4);
            var outlineTopClearance = MetricsScope.ScaleGap(SlapPx.Space6);
            var bleedPadding = MetricsScope.Scale(spec.OutlineBleedPadding);
            var scrollbarGap = MetricsScope.Scale(spec.ScrollbarGap);
            var contentWidth = spec.ContentWidth ?? MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            var rightGap = spec.ContentWidth.HasValue ? 0f : scrollbarGap;
            var rowWidth = MathF.Max(0f, contentWidth - bleedPadding - rightGap); 
            var scaledRowHeight = MetricsScope.Scale(rowHeight); 
            var rowSize = new Vector2(rowWidth, scaledRowHeight);
            var padding = MetricsScope.ScalePadding(RowPadding);

            ImGui.Dummy(new Vector2(0f, outlineTopClearance));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(hGap, rowGap));
            try
            {
                if (bleedPadding > 0f)
                    ImGui.Indent(bleedPadding);

                for (var i = 0; i < maxRows; i++)
                {
                    var item = items[i];
                    DrawSingleRow(item, i, rowSize, padding, drawRow);
                }

                HoverArbitration.Current.FlushOutline(spec.Key, 1f);

                if (bleedPadding > 0f)
                    ImGui.Unindent(bleedPadding);

                if (items.Count > maxRows && !string.IsNullOrWhiteSpace(spec.OverflowText))
                {
                    ImGui.Dummy(new Vector2(0f, MetricsScope.ScaleGap(SlapPx.Space8)));
                    ImGui.PushStyleColor(ImGuiCol.Text, ThemeScope.Resolved.Body);
                    ImGui.TextUnformatted(spec.OverflowText);
                    ImGui.PopStyleColor();
                }
            } finally
            {
                ImGui.PopStyleVar();
            }
        } finally
        {
            ImGui.PopID();
        }
    }

    private static void DrawSingleRow<T>(
        T item,
        int index,
        Vector2 rowSize,
        Vector2 padding,
        Action<T, int, RowListRowContext> drawRow)
    {
        ImGui.InvisibleButton($"##rlrow{index}", rowSize);
        using var cursor = new CursorScope();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var rawHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
        var hovered = HoverArbitration.Current.CaptureHover(rawHovered);
        var active = hovered && ImGui.IsItemActive();

        var palette = ThemeScope.Resolved.GetButtonPalette(Variant.Flat);
        var background = active ? palette.Active
            : hovered ? palette.Hovered
            : palette.Base;

        var drawList = ImGui.GetWindowDrawList();
        var rounding = SlapCorners.ControlRadius;

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(background), rounding);
        HoverArbitration.Current.CaptureOutline(
            min,
            max,
            hovered,
            selected: false,
            accent: ThemeScope.Resolved.ResolveOutlineAccent(Variant.Flat),
            rounding: rounding);

        var contentMin = min + padding;
        var contentMax = max - padding;
        var context = new RowListRowContext(index, hovered, active, min, max, contentMin, contentMax);

        drawList.PushClipRect(contentMin, contentMax, true);
        try
        {
            drawRow(item, index, context);
        } finally
        {
            drawList.PopClipRect();
        }
    }
}
