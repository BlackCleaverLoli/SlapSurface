using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Per-row configuration returned by the configureRow callback.
/// </summary>
/// <param name="RetainHoverVisual">
/// Keeps hover rendering without changing the row's hit-test result.
/// </param>
internal readonly record struct SurfaceRowState(
    Variant Variant = Variant.Flat,
    ControlState State = ControlState.None,
    LabeledOutlineSpec? Outline = null,
    bool RetainHoverVisual = false,
    SurfaceRowSelectionSpec? Selection = null,
    SurfaceRowIconActionSpec? IconAction = null
)
{
    public SurfaceRowState()
        : this(Variant.Flat, ControlState.None, null, false, null, null)
    {
    }
}

/// <summary>
/// Optional interaction owned by the leading icon of a surface row.
/// The icon receives pointer input before the enclosing row while right-click
/// continues to surface through the row context menu result.
/// </summary>
internal readonly record struct SurfaceRowIconActionSpec(
    FontAwesomeIcon OverlayIcon,
    string? Tooltip = null
);

/// <summary>Resolved leading-icon interaction for the current row.</summary>
internal readonly record struct SurfaceRowIconActionState(
    SurfaceRowIconActionSpec Spec,
    ControlResult Result
);

/// <summary>
/// Selection visual owned by a surface row. Unlike
/// <see cref="ControlState.Selected"/>, this is rendered by the row content
/// (for example as a centered FA overlay) so it can express multi-selection.
/// </summary>
internal readonly record struct SurfaceRowSelectionSpec(
    FontAwesomeIcon Icon = FontAwesomeIcon.Check,
    Vector4? IconColor = null,
    Vector4? Tint = null
);

/// <summary>
/// Specification for a scrollable surface list with outline/hover arbitration,
/// optional labeled outlines and overlay channels. Rows are viewport-virtualized,
/// so arbitrarily large item sets render with bounded per-frame cost.
/// </summary>
internal readonly record struct SurfaceListSpec(
    ControlKey Key,
    float RowHeightUnits,
    string? EmptyText = null,
    float OutlineBleedPadding = 2f,
    float RowGap = 8f
);

/// <summary>Slots for overlay drawing inside a surface list.</summary>
/// <param name="OverlayDraw">Drawn on a separate channel before rows (list overlay buttons).</param>
/// <para>
/// Slap controls (Button, SplitButton, etc.) drawn inside this callback
/// naturally participate in <see cref="HoverArbitration"/> — they capture
/// hover before rows are drawn, so row hover/click is automatically
/// suppressed underneath overlay buttons without any manual rect tracking.
/// </para>
/// <para>
/// For overlay content that does <b>not</b> use Slap controls (e.g. raw
/// draw-list primitives without an <c>InvisibleButton</c>), the caller can
/// manually suppress row hover by calling
/// <c>HoverArbitration.Current.CaptureHover(true)</c> inside the callback.
/// </para>
/// <param name="PostRowDecoration">Drawn after all rows on the main channel (e.g. scroll fade).</param>
internal readonly record struct SurfaceListSlots(
    Action? OverlayDraw = null,
    Action? PostRowDecoration = null
);

/// <summary>Context for drawing a single row's content.</summary>
internal readonly record struct SurfaceRowContext(
    Vector2 Min,
    Vector2 Max,
    Vector2 ContentMin,
    Vector2 ContentMax,
    bool Hovered,
    bool VisuallyHovered,
    bool Active,
    bool Selected,
    SurfaceRowSelectionSpec? Selection,
    bool Disabled,
    bool Clicked,
    bool RightClicked,
    bool DoubleClicked,
    Vector4 Background,
    Vector4 Border,
    SurfaceRowIconActionState? IconAction,
    int Index
)
{
    public Vector2 Size => Max - Min;
    public Vector2 ContentSize => Vector2.Max(Vector2.Zero, ContentMax - ContentMin);
    public ControlVisualMode VisualMode =>
        SlapInteraction.ResolveVisualMode(VisuallyHovered, Active, Disabled);
}

internal static class SurfaceListComponent
{
    private static readonly Vector2 RowPadding = new(SlapPx.Space8, 0f);

    public static void Draw<T>(
        ContentZoneContext zone,
        SurfaceListSpec spec,
        SurfaceListSlots slots,
        IReadOnlyList<T> items,
        Func<T, int, SurfaceRowState> configureRow,
        Action<T, int, SurfaceRowContext> drawRow)
    {
        Draw<T, byte>(
            zone,
            spec,
            slots,
            items,
            (item, index) => (byte)0,
            (item, index, _) => configureRow(item, index),
            (item, index, _, context) => drawRow(item, index, context));
    }

    public static void Draw<T, TRow>(
        ContentZoneContext zone,
        SurfaceListSpec spec,
        SurfaceListSlots slots,
        IReadOnlyList<T> items,
        Func<T, int, TRow> buildRow,
        Func<T, int, TRow, SurfaceRowState> configureRow,
        Action<T, int, TRow, SurfaceRowContext> drawRow)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("SurfaceListSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        try
        {
            using var _hover = HoverArbitration.Push();

            var useOverlayChannel = slots.OverlayDraw is not null;
            if (useOverlayChannel)
            {
                var drawList = ImGui.GetWindowDrawList();
                drawList.ChannelsSplit(2);
            }

            try
            {
                // Overlay draws happen BEFORE rows on channel 1 so that:
                // 1. Visually, channel 1 renders on top of channel 0 (rows).
                // 2. Hover-arbitration: overlay buttons and the floating
                //    command call CaptureHover before rows do,
                //    so hovered buttons naturally suppress row hover — no
                //    manual rect pre-computation or frame-persistent tracking.
                //    This works because each Slap control evaluates
                //    IsItemHovered + CaptureHover immediately after its
                //    InvisibleButton, in submission order — first hovered
                //    control wins, and subsequent row CaptureHover calls
                //    return false.
                if (useOverlayChannel)
                {
                    var contentCursorPos = ImGui.GetCursorPos();
                    var drawList = ImGui.GetWindowDrawList();
                    drawList.ChannelsSetCurrent(1);
                    slots.OverlayDraw?.Invoke();
                    ImGui.SetCursorPos(contentCursorPos);
                    drawList.ChannelsSetCurrent(0);
                }

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
                }
                else
                {
                    DrawRowList(
                        zone.Width,
                        spec,
                        items,
                        buildRow,
                        configureRow,
                        drawRow,
                        slots.PostRowDecoration);
                }
            }
            finally
            {
                if (useOverlayChannel)
                    ImGui.GetWindowDrawList().ChannelsMerge();
            }

        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static void DrawRowList<T, TRow>(
        float contentWidth,
        SurfaceListSpec spec,
        IReadOnlyList<T> items,
        Func<T, int, TRow> buildRow,
        Func<T, int, TRow, SurfaceRowState> configureRow,
        Action<T, int, TRow, SurfaceRowContext> drawRow,
        Action? postRowDecoration = null)
    {
        var totalRows = items.Count;
        var rowGap = MetricsScope.ScaleGap(spec.RowGap);
        var hGap = MetricsScope.ScaleGap(SlapPx.Space4);
        var bleedPadding = MetricsScope.Scale(spec.OutlineBleedPadding);
        var rowWidth = MathF.Max(0f, contentWidth - bleedPadding);
        var rowSize = new Vector2(rowWidth, MetricsScope.UnitHeight * spec.RowHeightUnits);
        var step = rowSize.Y + rowGap;

        // 描边标签绘制在行上方，滚动区顶部会裁剪；无条件预留顶部空隙
        // （与 RowList 一致），避免首行描边状态变化时整列布局跳动。
        var rowScreenTop = ImGui.GetCursorScreenPos().Y
            + LabeledOutlineDrawing.ResolveTopClearance();

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(hGap, rowGap));
        try
        {

            // Clip outline glow only in the scroll direction (Y). X is left at
            // full display width so horizontal glow expansion isn't cut off at
            // the window edges.
            var _winPos = ImGui.GetWindowPos();
            var _winSize = ImGui.GetWindowSize();
            var _displaySize = ImGui.GetIO().DisplaySize;
            HoverArbitration.Current.SetOutlineClipRect(
                new Vector2(0f, _winPos.Y),
                new Vector2(_displaySize.X, _winPos.Y + _winSize.Y));
            var outlineOverlays = new List<(
                Vector2 Min,
                Vector2 Max,
                LabeledOutlineSpec Outline
            )>();

            if (bleedPadding > 0f)
                ImGui.Indent(bleedPadding);

            var padding = MetricsScope.ScalePadding(RowPadding);
            var (firstRow, rowCount) = ResolveVisibleRowRange(
                rowScreenTop,
                step,
                totalRows,
                _winPos.Y,
                _winPos.Y + _winSize.Y);
            if (rowCount > 0)
            {
                ImGui.SetCursorScreenPos(
                    new Vector2(
                        ImGui.GetCursorScreenPos().X,
                        rowScreenTop + firstRow * step));
                for (var i = 0; i < rowCount; i++)
                {
                    var index = firstRow + i;
                    var item = items[index];
                    var row = buildRow(item, index);
                    var rowState = configureRow(item, index, row);
                    DrawSingleRow(item, index, rowSize, padding, rowState, outlineOverlays, row, drawRow);
                }
            }

            postRowDecoration?.Invoke();

            // 用空白占位补足整列高度，让滚动条反映本帧未绘制的行。
            var remaining = rowScreenTop + totalRows * step
                - ImGui.GetCursorScreenPos().Y
                - rowGap;
            if (remaining > 0f)
                ImGui.Dummy(new Vector2(0f, remaining));

            HoverArbitration.Current.FlushOutline(spec.Key, 1f);

            // Draw labeled outlines after the interaction outline flush.
            foreach (var (min, max, outline) in outlineOverlays)
            {
                LabeledOutlineDrawing.DrawOverlay(
                    ImGui.GetWindowDrawList(),
                    min,
                    max,
                    outline,
                    SlapCorners.ControlRadius
                );
            }

            if (bleedPadding > 0f)
                ImGui.Unindent(bleedPadding);
        }
        finally
        {
            ImGui.PopStyleVar();
        }
    }

    private static (int First, int Count) ResolveVisibleRowRange(
        float rowScreenTop,
        float step,
        int totalRows,
        float windowTop,
        float windowBottom)
    {
        if (totalRows <= 0)
            return (0, 0);

        var visibleTop = MathF.Max(windowTop, rowScreenTop);
        var visibleBottom = MathF.Min(windowBottom, rowScreenTop + totalRows * step);
        if (visibleBottom <= visibleTop)
            return (0, 0);

        // 上下各多绘制两行，避免快速滚动时短暂出现空白。
        const int overscanRows = 2;
        var first = Math.Clamp(
            (int)MathF.Floor((visibleTop - rowScreenTop) / step) - overscanRows,
            0,
            totalRows - 1);
        var last = Math.Clamp(
            (int)MathF.Ceiling((visibleBottom - rowScreenTop) / step) - 1 + overscanRows,
            0,
            totalRows - 1);
        return (first, last - first + 1);
    }

    private static void DrawSingleRow<T, TRow>(
        T item,
        int index,
        Vector2 rowSize,
        Vector2 padding,
        SurfaceRowState rowState,
        List<(Vector2 Min, Vector2 Max, LabeledOutlineSpec Outline)> outlineOverlays,
        TRow row,
        Action<T, int, TRow, SurfaceRowContext> drawRow)
    {
        var rowMin = ImGui.GetCursorScreenPos();
        var rowMax = rowMin + rowSize;
        var contentMin = rowMin + padding;
        var contentMax = rowMax - padding;
        SurfaceRowIconActionState? iconAction = null;
        if (rowState.IconAction is { } iconActionSpec)
        {
            var (actionMin, actionMax) = RowIconTwoLineComponent.ResolveIconSlot(
                rowMin,
                rowMax,
                contentMin,
                contentMax
            );
            ImGui.SetCursorScreenPos(actionMin);
            var rawIconClicked = ImGui.InvisibleButton(
                $"##rowIconAction{index}",
                actionMax - actionMin
            );
            var iconIx = SlapInteraction.Capture(
                rowState.State,
                rawIconClicked,
                captureRightClick: true
            );
            HoverArbitration.Current.TryShowTooltip(
                iconIx.Hovered,
                iconActionSpec.Tooltip
            );
            iconAction = new SurfaceRowIconActionState(
                iconActionSpec,
                iconIx.ToControlResult(actionMin, actionMax)
            );
            ImGui.SetCursorScreenPos(rowMin);
        }

        var rawClicked = ImGui.InvisibleButton($"##row{index}", rowSize);
        using var cursor = new CursorScope();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var ix = SlapInteraction.Capture(rowState.State, rawClicked, captureRightClick: true);
        var selection = rowState.Selection
            ?? (ix.Selected ? new SurfaceRowSelectionSpec() : null);
        var hovered = ix.Hovered && !ix.Disabled;
        var iconActionHovered = iconAction?.Result.Hovered == true;
        var visualHovered = !ix.Disabled
            && (hovered || iconActionHovered || rowState.RetainHoverVisual);

        var rt = ThemeScope.Resolved;
        var palette = rt.GetButtonPalette(rowState.Variant);

        var colors = SurfaceComponent.ResolveControlColors(
            palette,
            rowState.State,
            hovered: visualHovered,
            active: ix.Active,
            strength: BorderStrength.Subtle,
            variant: rowState.Variant);
        var background = colors.Background;
        var border = rowState.Outline is { } rowOutline && !string.IsNullOrWhiteSpace(rowOutline.Label)
            ? LabeledOutlineDrawing.ResolveColor(rowOutline)
            : SurfaceComponent.ResolveListRowBorder(palette, ix.Disabled);

        var drawList = ImGui.GetWindowDrawList();
        var rounding = SlapCorners.ControlRadius;

        // Background
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(background), rounding);

        // Keep the control border and interaction outline clear of the label.
        SlapOutlineCutout? cutout = null;
        if (rowState.Outline is { } outline && !string.IsNullOrWhiteSpace(outline.Label))
            cutout = LabeledOutlineDrawing.ComputeLabelCutout(min, outline);

        if (rowState.Variant != Variant.FlatNoBorder)
        {
            LabeledOutlineDrawing.DrawBorderWithCutout(
                drawList,
                min,
                max,
                cutout,
                border,
                rounding,
                MetricsScope.BorderThickness);
        }

        HoverArbitration.Current.CaptureOutline(
            min,
            max,
            visualHovered,
            false,
            rowState.Outline is { } hoverOutline
                ? LabeledOutlineDrawing.ResolveColor(hoverOutline)
                : rt.ResolveOutlineAccent(rowState.Variant),
            cutout,
            true,
            rounding);

        contentMin = min + padding;
        contentMax = max - padding;
        var context = new SurfaceRowContext(
            min,
            max,
            contentMin,
            contentMax,
            hovered,
            visualHovered,
            ix.Active,
            ix.Selected,
            selection,
            ix.Disabled,
            ix.Clicked,
            ix.RightClicked || iconAction?.Result.RightClicked == true,
            ix.DoubleClicked,
            background,
            border,
            iconAction,
            index
        );

        drawList.PushClipRect(contentMin, contentMax, true);
        try
        {
            drawRow(item, index, row, context);
        }
        finally
        {
            drawList.PopClipRect();
        }

        if (rowState.Outline is { } outlineSpec && !string.IsNullOrWhiteSpace(outlineSpec.Label))
            outlineOverlays.Add((min, max, outlineSpec));
    }

}
