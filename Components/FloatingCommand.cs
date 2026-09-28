using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlapSurface;

internal enum FloatingCommandBackgroundMode
{
    Fade,
    None,
}

/// <summary>
/// Command content drawn over the bottom edge of a page's scrollable content.
/// The page skeleton owns the overlay child and occlusion reservation; the
/// overlay itself is click-through and command rows isolate interaction into
/// hit-test children over the drawn slots, so unused overlay space stays
/// available to the content below. Callers only declare the row content and
/// row count.
/// </summary>
/// <param name="Key">Unique key for the overlay child ID.</param>
/// <param name="Draw">Draws controls inside the resolved command zone.</param>
/// <param name="RowCount">Positive number of command rows. Use a nullable slot to omit it.</param>
/// <param name="BackgroundOverride">Optional background sampled by the command fade.</param>
/// <param name="RightInset">
/// Optional right inset for command controls, in unscaled pixels. The fade
/// still covers the full source zone when enabled.
/// </param>
/// <param name="BackgroundMode">Controls whether the command backdrop is drawn.</param>
internal readonly record struct FloatingCommandSlot(
    ControlKey Key,
    Action<ContentZoneContext> Draw,
    int RowCount = 1,
    Vector4? BackgroundOverride = null,
    float RightInset = 0f,
    FloatingCommandBackgroundMode BackgroundMode = FloatingCommandBackgroundMode.Fade
);

internal readonly record struct FloatingCommandLayout(
    float ZoneHeight,
    float ComponentHeight,
    float Offset,
    float Indent
);

internal static class FloatingCommandComponent
{
    private const float RowGap = SlapPx.Space4;
    private const float SingleRowExtraHeight = 44f;
    private const float MultiRowExtraHeight = 34.5f;
    private const float MultiRowOffset = -4.75f;
    private const float ContentIndent = SlapPx.Space12;

    public static FloatingCommandLayout ResolveLayout(int rowCount)
    {
        if (rowCount < 1)
            throw new ArgumentOutOfRangeException(nameof(rowCount), "RowCount must be positive.");

        var rows = rowCount;
        var rowGap = Slap.Scale(RowGap);
        var contentHeight = rows == 1
            ? Slap.UnitHeight
            : rows * Slap.UnitHeight + (rows - 1) * rowGap;
        var isSingleRow = rows == 1;
        var extraHeight = Slap.Scale(
            isSingleRow ? SingleRowExtraHeight : MultiRowExtraHeight
        );
        var offset = isSingleRow ? 0f : Slap.Scale(MultiRowOffset);
        var indent = Slap.Scale(ContentIndent);

        return new FloatingCommandLayout(
            contentHeight + extraHeight,
            contentHeight,
            offset,
            indent
        );
    }

    /// <summary>
    /// Draw a sibling overlay child over the bottom of <paramref name="zone"/>.
    /// The overlay itself is click-through (<c>NoMouseInputs</c>); command
    /// rows drawn via <see cref="ContentZoneContext.ResponsiveRow"/> isolate
    /// interaction into compact hit-test children over the drawn slots, so
    /// clicks in the unused overlay space fall through to the content below.
    /// The overlay does not scroll with the content it covers.
    /// </summary>
    public static void DrawOverlay(
        ContentZoneContext zone,
        FloatingCommandSlot slot)
    {
        if (string.IsNullOrWhiteSpace(slot.Key.Value))
            throw new ArgumentException("FloatingCommandSlot.Key must not be empty.", nameof(slot));

        var layout = ResolveLayout(slot.RowCount);
        var savedCursor = ImGui.GetCursorScreenPos();
        var overlayMin = new Vector2(zone.Min.X, zone.Max.Y - layout.ZoneHeight);
        ImGui.SetCursorScreenPos(overlayMin);
        ImGui.PushID(slot.Key.Value);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        var visible = ImGui.BeginChild(
            "##floatingCommandOverlay",
            new Vector2(zone.Width, layout.ZoneHeight),
            false,
            ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.NoMouseInputs
        );
        try
        {
            if (visible)
                Draw(zone, layout, slot);
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar();
            ImGui.PopID();
            ImGui.SetCursorScreenPos(savedCursor);
        }
    }

    public static void Draw(
        ContentZoneContext zone,
        FloatingCommandLayout layout,
        FloatingCommandSlot slot)
    {
        var commandTop = zone.Max.Y - layout.ZoneHeight;
        var commandMin = new Vector2(zone.Min.X + layout.Indent, commandTop);
        var commandRight = zone.Max.X - Slap.Scale(MathF.Max(0f, slot.RightInset));
        var commandMax = new Vector2(MathF.Max(commandMin.X, commandRight), zone.Max.Y);
        var background = slot.BackgroundOverride ?? zone.Background;

        if (slot.BackgroundMode == FloatingCommandBackgroundMode.Fade)
        {
            var solid = ImGui.ColorConvertFloat4ToU32(background);
            var clear = ImGui.ColorConvertFloat4ToU32(background with { W = 0f });
            // Cover the full width so row outlines cannot bleed through the command area.
            ImGui.GetWindowDrawList().AddRectFilledMultiColor(
                new Vector2(zone.Min.X, commandTop),
                zone.Max,
                clear,
                clear,
                solid,
                solid
            );
        }

        // Vertically position the row inside the command zone so the extra
        // height resolves into breathing room above and below the content.
        var centeredY = commandTop
            + SlapLayout.CenterOffset(
                layout.ZoneHeight,
                layout.ComponentHeight,
                layout.Offset
            );
        ImGui.SetCursorScreenPos(new Vector2(commandMin.X, centeredY));

        slot.Draw(new ContentZoneContext(commandMin, commandMax, background)
        {
            IsolateRowInteraction = true,
        });
    }
}
