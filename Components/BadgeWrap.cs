using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;

namespace SlapSurface;

internal readonly record struct BadgeWrapSpec(
    ControlKey Key,
    IReadOnlyList<BadgeSpec> Items,
    HAnchor Anchor = HAnchor.Left,
    float ItemGap = SlapPx.Space4,
    float RowGap = SlapPx.Space4
);

/// <summary>Interaction indices reported by a wrapped badge collection.</summary>
internal readonly record struct BadgeWrapResult(
    int HoveredIndex,
    int ClickedIndex,
    int DoubleClickedIndex,
    ControlResult LastResult
);

internal static class BadgeWrapComponent
{
    public static BadgeWrapResult Draw(BadgeWrapSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "BadgeWrapSpec.Key.Value must not be empty.",
                nameof(spec)
            );

        ImGui.PushID(spec.Key.Value);
        try
        {
            using var _hover = HoverArbitration.Push();
            var origin = ImGui.GetCursorScreenPos();
            var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            if (spec.Items.Count == 0 || availableWidth <= 0f)
            {
                ImGui.Dummy(Vector2.Zero);
                return new BadgeWrapResult(-1, -1, -1, default);
            }

            var itemGap = MetricsScope.ScaleGap(spec.ItemGap);
            var rowGap = MetricsScope.ScaleGap(spec.RowGap);
            var widths = new float[spec.Items.Count];
            var rows = BuildRows(spec.Items, widths, availableWidth, itemGap);
            var hoveredIndex = -1;
            var clickedIndex = -1;
            var doubleClickedIndex = -1;
            var lastResult = default(ControlResult);

            var y = origin.Y;
            foreach (var row in rows)
            {
                var x = spec.Anchor == HAnchor.Right
                    ? origin.X + availableWidth - row.Width
                    : origin.X;

                for (var i = row.StartIndex; i < row.StartIndex + row.Count; i++)
                {
                    ImGui.SetCursorScreenPos(new Vector2(x, y));
                    var result = Slap.Badge(
                        spec.Items[i] with { ResolvedWidth = widths[i] }
                    );
                    lastResult = result;
                    if (result.Hovered)
                        hoveredIndex = i;
                    if (result.Clicked)
                        clickedIndex = i;
                    if (result.DoubleClicked)
                        doubleClickedIndex = i;
                    x += widths[i] + itemGap;
                }

                y += Slap.UnitHeight + rowGap;
            }

            var totalHeight = rows.Count * Slap.UnitHeight + (rows.Count - 1) * rowGap;
            ImGui.SetCursorScreenPos(origin + new Vector2(0f, totalHeight));
            ImGui.Dummy(Vector2.Zero);
            return new BadgeWrapResult(
                hoveredIndex,
                clickedIndex,
                doubleClickedIndex,
                lastResult
            );
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static List<BadgeWrapRow> BuildRows(
        IReadOnlyList<BadgeSpec> items,
        float[] widths,
        float availableWidth,
        float itemGap
    )
    {
        var rows = new List<BadgeWrapRow>();
        var rowStart = 0;
        var rowCount = 0;
        var rowWidth = 0f;

        for (var i = 0; i < items.Count; i++)
        {
            var width = MathF.Min(availableWidth, StackMeasure.ResolveBadgeWidth(items[i]));
            widths[i] = width;

            var nextWidth = rowCount == 0 ? width : rowWidth + itemGap + width;
            if (rowCount > 0 && nextWidth > availableWidth)
            {
                rows.Add(new BadgeWrapRow(rowStart, rowCount, rowWidth));
                rowStart = i;
                rowCount = 1;
                rowWidth = width;
                continue;
            }

            rowCount++;
            rowWidth = nextWidth;
        }

        if (rowCount > 0)
            rows.Add(new BadgeWrapRow(rowStart, rowCount, rowWidth));

        return rows;
    }

    private readonly record struct BadgeWrapRow(int StartIndex, int Count, float Width);
}
