using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;

namespace SlapSurface;

/// <summary>
/// Shared drawing orchestration for responsive slot strips. Content-zone rows
/// and setting rows both allocate slots with SlapMeasure, then draw visible
/// slots edge-to-center through this helper so the positioning protocol stays
/// in one place.
/// </summary>
internal static class ResponsiveSlotLayout
{
    /// <summary>
    /// Draw slots from the left edge inward. Slots are declared from the outer
    /// edge inward, so the first entry is the leftmost item.
    /// </summary>
    public static void DrawLeftStrip(
        IReadOnlyList<ResponsiveSlotEntry> entries,
        IReadOnlyList<ResponsiveItemLayout> layouts,
        float leftEdge,
        float rowY,
        float gap)
    {
        var cursorX = leftEdge;
        var drawnCount = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var layout = layouts[i];
            if (!layout.Visible)
                continue;

            if (drawnCount > 0)
                cursorX += gap;
            DrawSlot(entries[i], layout.Width, new Vector2(cursorX, rowY));
            cursorX += layout.Width;
            drawnCount++;
        }
    }

    /// <summary>
    /// Draw slots from the right edge inward. Slots are declared from the outer
    /// edge inward, so the first entry is the rightmost item.
    /// </summary>
    public static void DrawRightStrip(
        IReadOnlyList<ResponsiveSlotEntry> entries,
        IReadOnlyList<ResponsiveItemLayout> layouts,
        float rightEdge,
        float rowY,
        float gap)
    {
        var cursorX = rightEdge;
        var drawnCount = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var layout = layouts[i];
            if (!layout.Visible)
                continue;

            if (drawnCount > 0)
                cursorX -= gap;
            var position = new Vector2(cursorX - layout.Width, rowY);
            cursorX = position.X;
            DrawSlot(entries[i], layout.Width, position);
            drawnCount++;
        }
    }

    /// <summary>
    /// Draw the left strip inside a compact hit-test child covering only the
    /// visible slots' extent, so clicks in the unused row space pass through
    /// to the content below (floating command overlays). No-op when no slot
    /// is visible.
    /// </summary>
    public static void DrawLeftStripIsolated(
        IReadOnlyList<ResponsiveSlotEntry> entries,
        IReadOnlyList<ResponsiveItemLayout> layouts,
        float leftEdge,
        float rowY,
        float gap)
    {
        var endX = leftEdge;
        var drawnCount = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (!layouts[i].Visible)
                continue;
            if (drawnCount > 0)
                endX += gap;
            endX += layouts[i].Width;
            drawnCount++;
        }

        if (drawnCount == 0 || endX <= leftEdge)
            return;

        DrawStripIsolated(
            new Vector2(leftEdge, rowY),
            new Vector2(endX, rowY + MetricsScope.UnitHeight),
            $"L{MathF.Round(leftEdge)}_{MathF.Round(rowY)}",
            () => DrawLeftStrip(entries, layouts, leftEdge, rowY, gap));
    }

    /// <summary>
    /// Draw the right strip inside a compact hit-test child covering only the
    /// visible slots' extent, so clicks in the unused row space pass through
    /// to the content below (floating command overlays). No-op when no slot
    /// is visible.
    /// </summary>
    public static void DrawRightStripIsolated(
        IReadOnlyList<ResponsiveSlotEntry> entries,
        IReadOnlyList<ResponsiveItemLayout> layouts,
        float rightEdge,
        float rowY,
        float gap)
    {
        var endX = rightEdge;
        var drawnCount = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (!layouts[i].Visible)
                continue;
            if (drawnCount > 0)
                endX -= gap;
            endX -= layouts[i].Width;
            drawnCount++;
        }

        if (drawnCount == 0 || endX >= rightEdge)
            return;

        DrawStripIsolated(
            new Vector2(endX, rowY),
            new Vector2(rightEdge, rowY + MetricsScope.UnitHeight),
            $"R{MathF.Round(rightEdge)}_{MathF.Round(rowY)}",
            () => DrawRightStrip(entries, layouts, rightEdge, rowY, gap));
    }

    private static void DrawStripIsolated(Vector2 min, Vector2 max, string idSuffix, Action draw)
    {
        // The margin keeps control shadows (vertically clipped to the host
        // window bounds) and hover outlines inside the hit-test child.
        var margin = MetricsScope.Scale(SlapPx.Space16);
        var savedCursor = ImGui.GetCursorScreenPos();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.SetCursorScreenPos(min - new Vector2(margin));
        try
        {
            if (
                ImGui.BeginChild(
                    $"##responsiveRowHit{idSuffix}",
                    (max - min) + new Vector2(margin * 2f),
                    false,
                    ImGuiWindowFlags.NoBackground
                        | ImGuiWindowFlags.NoScrollbar
                        | ImGuiWindowFlags.NoScrollWithMouse
                )
            )
                draw();
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            ImGui.SetCursorScreenPos(savedCursor);
        }
    }

    private static void DrawSlot(ResponsiveSlotEntry entry, float width, Vector2 position)
    {
        ImGui.SetCursorScreenPos(position);
        entry.DrawAtWidth(width);
    }
}
