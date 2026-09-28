using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Specification for a content card with sequential zone stacking.
/// <para>
/// The card draws a background and shadow, and applies padding to establish
/// a content area. Zones are then stacked vertically in caller-defined heights;
/// explicit gaps keep inter-zone rhythm separate from component geometry.
/// </para>
/// </summary>
internal readonly record struct ContentCardSpec(
    ControlKey Key,
    ControlState State = ControlState.None,
    LayeredShadowSpec Shadow = default,
    Vector2? Padding = null,
    /// <summary>
    /// Extra right-side inset (unscaled px).  When <c>null</c> the
    /// component's built-in default is used.  Set to <c>0f</c> to
    /// suppress the inset entirely.
    /// </summary>
    float? RightInset = null
);

// ── Split adaptive spec ───────────────────────────────────

/// <summary>
/// Specification for an adaptive split with right anchor and left fill.
/// Groups the parameters of <see cref="ContentZoneContext.SplitAdaptive"/>
/// so callers don't need to remember parameter order.
/// </summary>
internal readonly record struct SplitAdaptiveSpec(
    float RightNaturalWidth,
    float LeftProtectedWidth,
    float Gap,
    float LeftContentHeight = 0f,
    float RightContentHeight = 0f
);

/// <summary>Horizontal anchor for row and stack scopes.</summary>
internal enum HAnchor { Left, Right }

// ── Scope helpers ─────────────────────────────────────────

/// <summary>
/// Shared width resolution for button-like controls inside stack scopes.
/// Resolves the pixel width from <see cref="ButtonSize"/> so that scope
/// callers never need to measure controls manually.
/// </summary>
internal static class StackMeasure
{
    public static float ResolveButtonWidth(ButtonSpec spec)
    {
        if (spec.ResolvedWidth > 0f)
            return SlapMeasure.ClampResolvedWidth(spec.ResolvedWidth);
        return spec.Size.Kind switch
        {
            ButtonSizeKind.Auto => Slap.ResolveButtonAutoWidth(spec.Label, spec.Icon, spec.GameIconId),
            ButtonSizeKind.Rect => spec.Size.WidthUnits * Slap.UnitWidth,
            ButtonSizeKind.Square => spec.Size.WidthUnits * Slap.UnitHeight,
            _ => Slap.ResolveButtonAutoWidth(spec.Label, spec.Icon, spec.GameIconId)
        };
    }

    public static float ResolveSplitButtonWidth(SplitButtonSpec spec)
    {
        if (spec.ResolvedWidth > 0f)
            return MathF.Max(SplitButtonComponent.ResolveMinWidth(spec), spec.ResolvedWidth);
        return spec.Size.Kind == ButtonSizeKind.Rect
            ? spec.Size.WidthUnits * Slap.UnitWidth
            : Slap.ResolveSplitButtonNaturalWidth(spec);
    }

    public static float ResolveToggleSwitchWidth(
        string? label,
        LabelPosition position,
        FontAwesomeIcon? icon = null,
        string? tooltip = null)
    {
        return ToggleSwitchComponent.ResolveNaturalWidth(label, position, icon, tooltip: tooltip);
    }

    public static float ResolveGameIconButtonWidth()
    {
        return Slap.UnitHeight;
    }

    public static float ResolveBadgeWidth(BadgeSpec spec)
    {
        if (spec.ResolvedWidth > 0f)
            return SlapMeasure.ClampResolvedWidth(spec.ResolvedWidth);
        return BadgeComponent.ResolveWidth(spec);
    }

    public static float ResolveCheckboxWidth(CheckboxSpec spec) =>
        CheckboxComponent.ResolveNaturalWidth(spec);
}

/// <summary>
/// Internal layout math helpers shared by scopes and zone context.
/// </summary>
internal static class SlapLayout
{
    /// <summary>
    /// Vertical centering offset for a component within a container.
    /// Returns the Y offset from the container's top edge so that the
    /// component is vertically centered (clamped to non-negative),
    /// plus an optional <paramref name="offset"/>.
    /// </summary>
    public static float CenterOffset(float container, float component, float offset = 0f)
        => MathF.Max(0f, (container - component) * 0.5f) + offset;
}

/// <summary>
/// Base class for scope objects providing disposed-state guard and
/// <see cref="IDisposable"/> implementation. Eliminates per-class
/// <c>_disposed</c> field and <c>ThrowIfDisposed</c> boilerplate.
/// </summary>
internal abstract class DisposableScope : IDisposable
{
    protected bool Disposed;

    protected void ThrowIfDisposed()
    {
        if (Disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    public virtual void Dispose() => Disposed = true;
}

// ── Vertical stack scope ──────────────────────────────────

/// <summary>
/// Scope for vertical stacking of controls within a zone, either
/// right-aligned or left-aligned. Each draw method resolves the control's
/// width, positions it at the configured edge, draws it, and advances the
/// cursor down by one unit height plus line gap.
/// <para>
/// Use <see cref="ContentZoneContext.BeginStack"/> to create instances.
/// </para>
/// </summary>
internal sealed class VerticalStackScope : DisposableScope
{
    private readonly float _edge;
    private readonly float _lineGap;
    private readonly HAnchor _anchor;
    private float _cursorY;

    internal VerticalStackScope(float edge, float startY, float lineGap, HAnchor anchor)
    {
        _edge = edge;
        _cursorY = startY;
        _lineGap = lineGap;
        _anchor = anchor;
    }

    /// <summary>
    /// Draw a button at the current stack row and advance to the next row.
    /// </summary>
    public ControlResult Button(ButtonSpec spec)
        => DrawControl(StackMeasure.ResolveButtonWidth(spec), () => Slap.Button(spec));

    /// <summary>
    /// Draw a split button at the current stack row and advance to the next row.
    /// </summary>
    public SplitButtonResult SplitButton(SplitButtonSpec spec)
        => DrawControl(StackMeasure.ResolveSplitButtonWidth(spec), () => Slap.SplitButton(spec));

    /// <summary>
    /// Draw a toggle switch at the current stack row and advance to the next row.
    /// </summary>
    public ToggleSwitchResult ToggleSwitch(ToggleSwitchSpec spec)
        => DrawControl(
            StackMeasure.ResolveToggleSwitchWidth(spec.Label, spec.Position, spec.Icon, spec.Tooltip),
            () => Slap.ToggleSwitch(spec));

    /// <summary>
    /// Draw a game icon button (square, 1×1 unit) at the current stack row
    /// and advance to the next row.
    /// </summary>
    public ControlResult GameIconButton(GameIconSpec spec)
        => DrawControl(StackMeasure.ResolveGameIconButtonWidth(), () => Slap.GameIconButton(spec));

    /// <summary>
    /// Shared draw template: resolve horizontal position from edge and
    /// alignment, set cursor, draw, then advance Y.
    /// </summary>
    private T DrawControl<T>(float width, Func<T> draw)
    {
        ThrowIfDisposed();
        var x = _anchor == HAnchor.Right ? _edge - width : _edge;
        ImGui.SetCursorScreenPos(new Vector2(x, _cursorY));
        var result = draw();
        _cursorY += Slap.UnitHeight + _lineGap;
        return result;
    }

    /// <summary>
    /// Dispose the scope and restore the ImGui cursor Y to the next
    /// available row position so that subsequent scopes or manual drawing
    /// start at the correct vertical offset (including the last line gap).
    /// </summary>
    public override void Dispose()
    {
        if (!Disposed)
        {
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, _cursorY));
        }
        base.Dispose();
    }
}

// ── Horizontal row scope ──────────────────────────────────

/// <summary>
/// Scope for horizontal placement of controls within a zone. Controls are
/// drawn through <see cref="Left"/> or <see cref="Right"/> sides; multiple
/// controls on the same side stack inward from the edge with a gap.
/// Both sides are always available, so a single row can mix left- and
/// right-anchored controls for space-between layouts.
/// <para>
/// All controls share the same top Y (top-aligned). Dispose advances Y
/// to the next row (<c>UnitHeight + rowGap</c>) so that consecutive
/// <c>BeginRow</c> calls stack rows without manual Y computation.
/// </para>
/// <para>
/// Use <see cref="ContentZoneContext.BeginRow"/> to create instances.
/// </para>
/// </summary>
internal sealed class HorizontalRowScope : DisposableScope
{
    private readonly float _rowY;
    private readonly float _gap;
    private readonly float _rowGap;
    private float _leftAnchor;
    private float _rightAnchor;

    internal HorizontalRowScope(float leftEdge, float rightEdge, float startY, float gap, float rowGap)
    {
        _rowY = startY;
        _gap = gap;
        _rowGap = rowGap;
        _leftAnchor = leftEdge;
        _rightAnchor = rightEdge;
    }

    /// <summary>Left side of the row, anchored at the zone's left edge.</summary>
    public RowSide Left => new(this, isRight: false);

    /// <summary>Right side of the row, anchored at the zone's right edge.</summary>
    public RowSide Right => new(this, isRight: true);

    /// <summary>
    /// Remaining horizontal space between the left and right anchors,
    /// not including the inter-item gap that would be applied after the next draw.
    /// Use this to cap a control's width before placing it on either side.
    /// </summary>
    public float RemainingWidth => MathF.Max(0f, _rightAnchor - _leftAnchor);

    internal T DrawOnLeft<T>(float width, Func<T> draw)
    {
        ThrowIfDisposed();
        ImGui.SetCursorScreenPos(new Vector2(_leftAnchor, _rowY));
        var result = draw();
        _leftAnchor += width + _gap;
        return result;
    }

    internal T DrawOnRight<T>(float width, Func<T> draw)
    {
        ThrowIfDisposed();
        _rightAnchor -= width;
        ImGui.SetCursorScreenPos(new Vector2(_rightAnchor, _rowY));
        var result = draw();
        _rightAnchor -= _gap;
        return result;
    }

    public override void Dispose()
    {
        if (!Disposed)
            ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, _rowY + Slap.UnitHeight + _rowGap));
        base.Dispose();
    }
}

// ── Space-between row side ────────────────────────────────

/// <summary>
/// One side of a <see cref="HorizontalRowScope"/>. Controls drawn through
/// this side are anchored at the zone's corresponding edge and stack inward.
/// Instances are obtained via <see cref="HorizontalRowScope.Left"/> or
/// <see cref="HorizontalRowScope.Right"/>.
/// </summary>
internal sealed class RowSide
{
    private readonly HorizontalRowScope _owner;
    private readonly bool _isRight;

    internal RowSide(HorizontalRowScope owner, bool isRight)
    {
        _owner = owner;
        _isRight = isRight;
    }

    /// <summary>Draw a button on this side, stacking inward from the edge.</summary>
    public ControlResult Button(ButtonSpec spec)
        => DrawOnSide(StackMeasure.ResolveButtonWidth(spec), () => Slap.Button(spec));

    /// <summary>
    /// Draw a button on this side, capping its natural width to
    /// <paramref name="maxWidth"/> (e.g. <see cref="HorizontalRowScope.RemainingWidth"/>).
    /// When the button's natural width fits, it is drawn unchanged; otherwise
    /// <see cref="ButtonSpec.ResolvedWidth"/> is set so the label clips/fades.
    /// </summary>
    public ControlResult Button(ButtonSpec spec, float maxWidth)
    {
        var natural = StackMeasure.ResolveButtonWidth(spec with { ResolvedWidth = 0f });
        var capped = MathF.Min(natural, MathF.Max(0f, maxWidth));
        var final = StackMeasure.ResolveButtonWidth(spec with { ResolvedWidth = capped });
        return DrawOnSide(final, () => Slap.Button(spec with { ResolvedWidth = final }));
    }

    /// <summary>Draw a split button on this side, stacking inward from the edge.</summary>
    public SplitButtonResult SplitButton(SplitButtonSpec spec)
        => DrawOnSide(StackMeasure.ResolveSplitButtonWidth(spec), () => Slap.SplitButton(spec));

    /// <summary>
    /// Draw a split button on this side, capping its natural width to
    /// <paramref name="maxWidth"/> (e.g. <see cref="HorizontalRowScope.RemainingWidth"/>).
    /// When the split button's natural width fits, it is drawn unchanged;
    /// otherwise <see cref="SplitButtonSpec.ResolvedWidth"/> is set so the
    /// label clips/fades.
    /// </summary>
    public SplitButtonResult SplitButton(SplitButtonSpec spec, float maxWidth)
    {
        var natural = Slap.ResolveSplitButtonNaturalWidth(spec);
        var capped = MathF.Min(natural, MathF.Max(0f, maxWidth));
        var final = StackMeasure.ResolveSplitButtonWidth(spec with { ResolvedWidth = capped });
        return DrawOnSide(final, () => Slap.SplitButton(spec with { ResolvedWidth = final }));
    }

    /// <summary>Draw a toggle switch on this side, stacking inward from the edge.</summary>
    public ToggleSwitchResult ToggleSwitch(ToggleSwitchSpec spec)
        => DrawOnSide(
            StackMeasure.ResolveToggleSwitchWidth(spec.Label, spec.Position, spec.Icon, spec.Tooltip),
            () => Slap.ToggleSwitch(spec));

    /// <summary>Draw a checkbox on this side, stacking inward from the edge.</summary>
    public CheckboxResult Checkbox(CheckboxSpec spec)
        => DrawOnSide(StackMeasure.ResolveCheckboxWidth(spec), () => Slap.Checkbox(spec));

    /// <summary>Draw a game icon button on this side, stacking inward from the edge.</summary>
    public ControlResult GameIconButton(GameIconSpec spec)
        => DrawOnSide(StackMeasure.ResolveGameIconButtonWidth(), () => Slap.GameIconButton(spec));

    /// <summary>Draw a badge on this side, stacking inward from the edge.</summary>
    public ControlResult Badge(BadgeSpec spec)
        => DrawOnSide(StackMeasure.ResolveBadgeWidth(spec), () => Slap.Badge(spec));

    /// <summary>
    /// Draw a static badge on this side, capping its natural width to
    /// <paramref name="maxWidth"/>. When the badge's natural width fits, it is
    /// drawn unchanged; otherwise <see cref="BadgeSpec.ResolvedWidth"/> is set
    /// so the label is suppressed (icon-only degradation) or clipped.
    /// </summary>
    public ControlResult Badge(BadgeSpec spec, float maxWidth)
    {
        var natural = BadgeComponent.ResolveWidth(spec with { ResolvedWidth = 0f });
        var capped = MathF.Min(natural, MathF.Max(0f, maxWidth));
        var final = SlapMeasure.ClampResolvedWidth(capped);
        return DrawOnSide(final, () => Slap.Badge(spec with { ResolvedWidth = final }));
    }

    private T DrawOnSide<T>(float width, Func<T> draw)
        => _isRight ? _owner.DrawOnRight(width, draw) : _owner.DrawOnLeft(width, draw);

    private void DrawOnSide(float width, Action draw)
    {
        if (_isRight)
            _owner.DrawOnRight(width, () => { draw(); return 0; });
        else
            _owner.DrawOnLeft(width, () => { draw(); return 0; });
    }
}

// ── Scroll area ───────────────────────────────────────────

/// <summary>
/// Scope for a scrollable child area within a zone.
/// Dispose to end the child window.
/// </summary>
internal sealed class ScrollAreaScope : IDisposable
{
    private bool _disposed;
    private readonly bool _ownsChild;
    private readonly bool _drawEdgeFade;
    private readonly Vector2 _fadeMin;
    private readonly Vector2 _fadeMax;
    private readonly Vector4 _fadeBackground;

    internal ScrollAreaScope(
        bool ownsChild,
        bool drawEdgeFade,
        Vector2 fadeMin,
        Vector2 fadeMax,
        Vector4 fadeBackground)
    {
        _ownsChild = ownsChild;
        _drawEdgeFade = drawEdgeFade;
        _fadeMin = fadeMin;
        _fadeMax = fadeMax;
        _fadeBackground = fadeBackground;
    }

    /// <summary>Current vertical scroll position in pixels.</summary>
    public float ScrollY => ImGui.GetScrollY();

    /// <summary>Maximum scrollable vertical distance in pixels.</summary>
    public float ScrollMaxY => ImGui.GetScrollMaxY();

    /// <summary>True when the content is scrollable (exceeds the visible area).</summary>
    public bool CanScroll => ScrollMaxY > 0.5f;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsChild)
        {
            if (_drawEdgeFade)
                ScrollFade.DrawVertical(_fadeMin, _fadeMax, _fadeBackground);
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
    }
}

/// <summary>
/// Scope returned by <see cref="ContentZoneContext.BeginDetailScroll"/>.
/// Dispose to end the child window and restore padding.
/// </summary>
internal sealed class DetailScrollScope : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        ImGui.EndChild();
        ImGui.PopStyleVar();
    }
}

/// <summary>
/// Context passed to each zone draw callback, providing the zone rect
/// and the card's surface colors for fade/overlay painting.
/// </summary>
/// <param name="IsolateRowInteraction">
/// When true, <see cref="ContentZoneContext.ResponsiveRow"/> draws each
/// side's slots inside a compact hit-test child over the visible slots, so
/// clicks in the unused row space fall through to the content below. Used by
/// floating command overlays.
/// </param>
internal readonly record struct ContentZoneContext(
    Vector2 Min,
    Vector2 Max,
    Vector4 Background,
    bool IsolateRowInteraction = false
)
{
    public Vector2 Size => Max - Min;
    public float Width => MathF.Max(0f, Max.X - Min.X);
    public float Height => MathF.Max(0f, Max.Y - Min.Y);
    public float CenterY => Min.Y + Height * 0.5f;

    /// <summary>
    /// Split the zone into left and right halves by ratio (0–1).
    /// Both callbacks are positioned automatically; no manual SameLine needed.
    /// </summary>
    public void Split(
        float leftRatio,
        Action<ContentZoneContext> drawLeft,
        Action<ContentZoneContext> drawRight)
    {
        var gap = MetricsScope.ScaleGap(SlapPx.Space4);
        var totalWidth = MathF.Max(0f, Max.X - Min.X);
        var leftWidth = MathF.Max(0f, totalWidth * leftRatio - gap * 0.5f);
        var rightWidth = MathF.Max(0f, totalWidth * (1f - leftRatio) - gap * 0.5f);

        var leftMin = Min;
        var leftMax = new Vector2(Min.X + leftWidth, Max.Y);
        var rightMin = new Vector2(leftMax.X + gap, Min.Y);
        var rightMax = new Vector2(rightMin.X + rightWidth, Max.Y);

        var cursorY = ImGui.GetCursorScreenPos().Y;

        ImGui.SetCursorScreenPos(leftMin);
        using (HoverArbitration.Push())
            drawLeft(new ContentZoneContext(leftMin, leftMax, Background));

        ImGui.SetCursorScreenPos(new Vector2(rightMin.X, cursorY));
        using (HoverArbitration.Push())
            drawRight(new ContentZoneContext(rightMin, rightMax, Background));
    }

    /// <summary>
    /// Split the zone using content-aware adaptive measurement.
    /// The right item (anchor) gets its natural width first; the left item
    /// (fill) gets the remainder. When <paramref name="leftProtectedWidth"/>
    /// is greater than the remainder, left is guaranteed at least that width
    /// and right is re-measured with the reduced space.
    /// <para>
    /// Use this instead of <see cref="Split(float, Action{ContentZoneContext}, Action{ContentZoneContext})"/>
    /// when one side has a natural content width that should not be stretched
    /// (e.g. a start button) and the other side fills remaining space
    /// (e.g. a badge strip that may overflow).
    /// </para>
    /// <para>
    /// When <paramref name="leftContentHeight"/> or <paramref name="rightContentHeight"/>
    /// is greater than 0 and less than the zone height, that side's cursor is
    /// vertically centered within the zone.
    /// </para>
    /// </summary>
    public void SplitAdaptive(
        float rightNaturalWidth,
        float leftProtectedWidth,
        float gap,
        Action<ContentZoneContext> drawLeft,
        Action<ContentZoneContext> drawRight,
        float leftContentHeight = 0f,
        float rightContentHeight = 0f)
    {
        var totalWidth = MathF.Max(0f, Max.X - Min.X);
        var measure = SlapMeasure.MeasureAdaptiveSplit(
            totalWidth, gap, rightNaturalWidth, leftProtectedWidth);

        var leftMin = Min;
        var leftMax = new Vector2(Min.X + measure.LeftWidth, Max.Y);
        var rightMin = new Vector2(leftMax.X + measure.Gap, Min.Y);
        var rightMax = new Vector2(rightMin.X + measure.RightWidth, Max.Y);

        var cursorY = ImGui.GetCursorScreenPos().Y;

        if (measure.LeftWidth > 0f)
        {
            var leftOffsetY = leftContentHeight > 0f && leftContentHeight < Height
                ? SlapLayout.CenterOffset(Height, leftContentHeight)
                : 0f;
            var leftCursor = new Vector2(leftMin.X, leftMin.Y + leftOffsetY);
            ImGui.SetCursorScreenPos(leftCursor);
            using (HoverArbitration.Push())
                drawLeft(new ContentZoneContext(leftCursor, leftMax, Background));
        }

        if (measure.RightWidth > 0f)
        {
            var rightOffsetY = rightContentHeight > 0f && rightContentHeight < Height
                ? SlapLayout.CenterOffset(Height, rightContentHeight)
                : 0f;
            var rightCursor = new Vector2(rightMin.X, MathF.Max(cursorY, rightMin.Y + rightOffsetY));
            ImGui.SetCursorScreenPos(rightCursor);
            using (HoverArbitration.Push())
                drawRight(new ContentZoneContext(rightCursor, rightMax, Background));
        }
    }

    /// <summary>
    /// Split the zone using a <see cref="SplitAdaptiveSpec"/>.
    /// This is a convenience overload that groups the parameters.
    /// </summary>
    public void SplitAdaptive(
        SplitAdaptiveSpec spec,
        Action<ContentZoneContext> drawLeft,
        Action<ContentZoneContext> drawRight) =>
        SplitAdaptive(
            spec.RightNaturalWidth,
            spec.LeftProtectedWidth,
            spec.Gap,
            drawLeft,
            drawRight,
            spec.LeftContentHeight,
            spec.RightContentHeight);

    // ── Responsive rows ───────────────────────────────────

    /// <summary>
    /// Draw a surface header on the left with responsive controls on the right.
    /// Right-side slots are declared from the outer edge inward.
    /// </summary>
    public void HeaderRow(
        SurfaceHeaderSpec header,
        Action<ResponsiveSlotGroup> collectRightSlots)
    {
        var itemGap = ImGui.GetStyle().ItemSpacing.X;
        var headerControlsGap = MetricsScope.ScaleGap(SlapPx.Space16);
        var rowY = Min.Y;
        var rightSlots = new ResponsiveSlotGroup();
        collectRightSlots(rightSlots);
        var entries = rightSlots.Entries;

        if (entries.Count == 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(Min.X, rowY));
            Slap.SurfaceHeader(header with { Width = Width });
            return;
        }

        var titleNaturalWidth = SurfaceHeaderComponent.ResolveNaturalWidth(header);
        var stripLayout = SlapMeasure.MeasureStripLayout(
            Width,
            titleNaturalWidth,
            MetricsScope.UnitHeight * 3f,
            headerControlsGap,
            BuildWidthRanges(entries),
            BuildPriorities(entries),
            itemGap);
        var titleWidth = MathF.Min(stripLayout.LeftWidth, titleNaturalWidth);

        if (titleWidth > 0f)
        {
            ImGui.SetCursorScreenPos(new Vector2(Min.X, rowY));
            Slap.SurfaceHeader(header with { Width = titleWidth });
        }

        ResponsiveSlotLayout.DrawRightStrip(
            entries,
            stripLayout.RightLayouts,
            Max.X,
            rowY,
            itemGap);
    }

    /// <summary>
    /// Draw a two-sided responsive row without a title. Slots on each side are
    /// declared from their outer edge inward. The right group shrinks first,
    /// followed by the left group. The row uses the current cursor Y so it can
    /// follow an earlier row within the same content zone.
    /// </summary>
    public void ResponsiveRow(Action<ResponsiveRowBuilder> collectSlots)
    {
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var rowY = MathF.Max(Min.Y, ImGui.GetCursorScreenPos().Y);
        var row = new ResponsiveRowBuilder();
        collectSlots(row);

        var leftEntries = row.Left.Entries;
        var rightEntries = row.Right.Entries;
        if (leftEntries.Count == 0 && rightEntries.Count == 0)
            return;

        var leftWidthRanges = BuildWidthRanges(leftEntries);
        var rightWidthRanges = BuildWidthRanges(rightEntries);
        var layoutResult = SlapMeasure.MeasureTwoSidedLayout(
            Width,
            gap,
            leftWidthRanges,
            BuildPriorities(leftEntries),
            rightWidthRanges,
            BuildPriorities(rightEntries),
            gap);

        if (IsolateRowInteraction)
        {
            ResponsiveSlotLayout.DrawLeftStripIsolated(
                leftEntries,
                layoutResult.LeftLayouts,
                Min.X,
                rowY,
                gap);
            ResponsiveSlotLayout.DrawRightStripIsolated(
                rightEntries,
                layoutResult.RightLayouts,
                Max.X,
                rowY,
                gap);
            return;
        }

        ResponsiveSlotLayout.DrawLeftStrip(
            leftEntries,
            layoutResult.LeftLayouts,
            Min.X,
            rowY,
            gap);

        ResponsiveSlotLayout.DrawRightStrip(
            rightEntries,
            layoutResult.RightLayouts,
            Max.X,
            rowY,
            gap);
    }

    private static ResponsiveWidthRange[] BuildWidthRanges(
        IReadOnlyList<ResponsiveSlotEntry> entries)
    {
        var widthRanges = new ResponsiveWidthRange[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            widthRanges[i] = entries[i].WidthRange;
        return widthRanges;
    }

    private static int[] BuildPriorities(
        IReadOnlyList<ResponsiveSlotEntry> entries)
    {
        var priorities = new int[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            priorities[i] = entries[i].Priority;
        return priorities;
    }

    // ── Scroll area ───────────────────────────────────────

    /// <summary>
    /// Begin a zero-padding scrollable child area within this zone.
    /// The area fills the zone width (plus optional <paramref name="widthExtension"/>
    /// for scrollbar placement) and the specified height.
    /// Dispose the returned scope to end the child.
    /// </summary>
    /// <param name="id">ImGui child window ID.</param>
    /// <param name="height">Override height; defaults to zone height.</param>
    /// <param name="widthExtension">Extra width beyond zone width (e.g. for scrollbar offset).</param>
    /// <param name="drawEdgeFade">
    /// Draws top and bottom edge fades when the content can scroll. Disable only
    /// when the caller owns custom fade geometry or drawing order.
    /// </param>
    public ScrollAreaScope ScrollArea(
        string id,
        float? height = null,
        float widthExtension = 0f,
        bool drawEdgeFade = true)
    {
        var h = height ?? Height;
        var w = Width + widthExtension;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild(id, new Vector2(w, h), false, ImGuiWindowFlags.None);
        var fadeMin = ImGui.GetWindowPos();
        var fadeMax = fadeMin + new Vector2(Width, ImGui.GetWindowSize().Y);
        return new ScrollAreaScope(true, drawEdgeFade, fadeMin, fadeMax, Background);
    }

    // ── Vertical stack scope ───────────────────────────────

    /// <summary>
    /// Begin a vertical stack at the current cursor position.
    /// Each control drawn through the returned scope is X-aligned to
    /// <see cref="Min"/>.X (<see cref="HAnchor.Left"/>) or
    /// <see cref="Max"/>.X (<see cref="HAnchor.Right"/>),
    /// and stacked vertically with standard line gap.
    /// </summary>
    public VerticalStackScope BeginStack(HAnchor anchor)
    {
        var edge = anchor == HAnchor.Right ? Max.X : Min.X;
        return new VerticalStackScope(edge, ImGui.GetCursorScreenPos().Y, Slap.Scale(SlapPx.Space4), anchor);
    }

    // ── Detail scroll child ────────────────────────────────

    /// <summary>
    /// Begin a scroll child that extends width past the zone edge by
    /// <c>ScrollbarSize + Space6</c> so the scrollbar sits outside the
    /// content column. <paramref name="contentIndentX"/> sets the child
    /// window's <c>WindowPadding.X</c> so content auto-indents.
    /// Dispose to end the child and restore padding.
    /// </summary>
    public DetailScrollScope BeginDetailScroll(string id, float contentIndentX = 0f)
    {
        var detailScrollExtension =
            ImGui.GetStyle().ScrollbarSize + Slap.Scale(SlapPx.Space6);
        var scrollW = Width + detailScrollExtension;
        var scrollH = Height;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            new Vector2(contentIndentX, 0f));
        ImGui.BeginChild(id, new Vector2(scrollW, scrollH), false, ImGuiWindowFlags.None);

        return new DetailScrollScope();
    }

    // ── Horizontal row scope ───────────────────────────────

    /// <summary>
    /// Begin a horizontal row at the current cursor position.
    /// Use <see cref="HorizontalRowScope.Left"/> and
    /// <see cref="HorizontalRowScope.Right"/> to place controls on
    /// either side; multiple controls on the same side stack inward
    /// with <paramref name="gap"/> between them. Both sides are always
    /// available, so a single row can mix left- and right-anchored
    /// controls for space-between layouts.
    /// All controls share the same top Y. Dispose advances Y by
    /// <c>UnitHeight + rowGap</c> so consecutive <c>BeginRow</c> calls
    /// stack rows automatically.
    /// </summary>
    /// <param name="gap">Horizontal gap between controls in scaled pixels.
    /// Defaults to <c>ImGui.GetStyle().ItemSpacing.X</c> when 0 or negative.</param>
    /// <param name="rowGap">Vertical gap applied on dispose to advance to
    /// the next row. Defaults to <c>Scale(SlapPx.Space4)</c> when 0 or negative.</param>
    public HorizontalRowScope BeginRow(float gap = 0f, float rowGap = 0f)
    {
        var actualGap = gap > 0f ? gap : ImGui.GetStyle().ItemSpacing.X;
        var actualRowGap = rowGap > 0f ? rowGap : Slap.Scale(SlapPx.Space4);
        return new HorizontalRowScope(Min.X, Max.X, ImGui.GetCursorScreenPos().Y, actualGap, actualRowGap);
    }

    // ── Centered text ─────────────────────────────────────

    /// <summary>
    /// Draw text centered (both horizontally and vertically) within this zone.
    /// Uses <c>ImGuiCol.TextDisabled</c> color, suitable for placeholder /
    /// empty-state messages.
    /// </summary>
    public void DrawCenteredText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var size = ImGui.CalcTextSize(text);
        var pos = new Vector2(
            Min.X + MathF.Max(0f, (Width - size.X) * 0.5f),
            Min.Y + MathF.Max(0f, (Height - size.Y) * 0.5f));
        ImGui.SetCursorScreenPos(pos);
        ImGui.TextDisabled(text);
    }

}

/// <summary>
/// Scope for a content card that provides sequential zone stacking.
/// Call <see cref="Zone"/> for each zone in top-to-bottom order.
/// </summary>
internal sealed class ContentCardScope : IDisposable
{
    private bool _disposed;
    private readonly Vector2 _contentMin;
    private readonly Vector2 _contentMax;
    private readonly Vector2 _cardMin;
    private readonly Vector2 _cardMax;
    private readonly float _contentRightEdge;
    private readonly Vector4 _background;
    private float _cursorY;
    private float _contentBottom;

    internal ContentCardScope(
        Vector2 cardMin,
        Vector2 cardMax,
        Vector2 contentMin,
        Vector2 contentMax,
        float contentRightEdge,
        Vector4 background)
    {
        _cardMin = cardMin;
        _cardMax = cardMax;
        _contentMin = contentMin;
        _contentMax = contentMax;
        _contentRightEdge = contentRightEdge;
        _background = background;
        _cursorY = contentMin.Y;
        _contentBottom = contentMax.Y;
    }

    /// <summary>Remaining vertical space from the current cursor to the content area bottom.</summary>
    public float RemainingHeight => MathF.Max(0f, _contentBottom - _cursorY);

    /// <summary>
    /// Reserve and draw a fixed status zone after the scrollable content.
    /// The zone is omitted when <paramref name="draw"/> is null, so it never
    /// consumes layout space without visible state.
    /// </summary>
    public void BottomStatus(
        float height,
        Action<ContentZoneContext>? draw,
        float? rightInset = null)
    {
        if (_disposed || draw == null || height <= 0f)
            return;

        var scaledHeight = MetricsScope.Scale(height);
        var bottom = _contentBottom;
        var top = MathF.Max(_cursorY, bottom - scaledHeight);
        _contentBottom = top;
        var min = new Vector2(_contentMin.X, top);
        var max = new Vector2(ResolveContentRightEdge(rightInset), bottom);
        ImGui.SetCursorScreenPos(min);
        draw(new ContentZoneContext(min, max, _background));
    }

    /// <summary>
    /// Advance by an explicit inter-zone gap in unscaled pixels. The gap is
    /// clamped to the remaining content area and does not invoke ImGui item layout.
    /// </summary>
    public void Gap(float unscaledGap)
    {
        if (_disposed || unscaledGap <= 0f)
            return;

        var min = new Vector2(_contentMin.X, _cursorY);
        _cursorY = MathF.Min(
            _contentMax.Y,
            _cursorY + MetricsScope.ScaleGap(unscaledGap)
        );
        var max = new Vector2(_contentMax.X, _cursorY);
        ImGui.SetCursorScreenPos(new Vector2(_contentMin.X, _cursorY));
    }

    /// <summary>
    /// Draw a zone of explicit <paramref name="height"/>.
    /// <para>
    /// When <paramref name="componentHeight"/> &gt; 0, the cursor is positioned so that a
    /// component of that height is vertically centered within the zone (plus optional
    /// <paramref name="offset"/>). When <paramref name="componentHeight"/> is 0 (default),
    /// the cursor is placed at the zone's top-left — the callback handles its own positioning.
    /// </para>
    /// <para>
    /// Use <see cref="Gap"/> for spacing between independent zones. Include
    /// extra height here only when it belongs to this zone's alignment budget.
    /// </para>
    /// </summary>
    /// <param name="rightInset">
    /// Optional zone-specific right inset in unscaled pixels. <c>null</c>
    /// uses the card's default content edge; <c>0</c> retains only the card's
    /// base horizontal padding.
    /// </param>
    public void Zone(
        float height,
        Action<ContentZoneContext> draw,
        float componentHeight = 0f,
        float offset = 0f,
        float? rightInset = null)
    {
        if (_disposed || draw == null)
            return;

        var zoneMin = new Vector2(_contentMin.X, _cursorY);
        var zoneMax = new Vector2(ResolveContentRightEdge(rightInset), zoneMin.Y + height);

        var cursorY = componentHeight > 0f
            ? zoneMin.Y + SlapLayout.CenterOffset(height, componentHeight, offset)
            : zoneMin.Y + offset;
        ImGui.SetCursorScreenPos(new Vector2(zoneMin.X, cursorY));

        draw(new ContentZoneContext(zoneMin, zoneMax, _background));
        _cursorY = zoneMax.Y;
    }

    /// <summary>
    /// Draw a zone whose height is auto-measured from the cursor
    /// advancement during <paramref name="draw"/>. After drawing, the
    /// cursor advances by the exact height consumed.
    /// <para>
    /// The callback must not restore the cursor to its entry position
    /// (e.g. via <c>SetCursorScreenPos(savedPos)</c>), otherwise the
    /// measured height will be zero.
    /// </para>
    /// </summary>
    /// <param name="rightInset">
    /// Optional zone-specific right inset in unscaled pixels. <c>null</c>
    /// uses the card's default content edge; <c>0</c> retains only the card's
    /// base horizontal padding.
    /// </param>
    /// <returns>The actual content height consumed.</returns>
    public float ZoneAuto(
        Action<ContentZoneContext> draw,
        float offset = 0f,
        float? rightInset = null)
    {
        if (_disposed || draw == null)
            return 0f;

        var zoneMin = new Vector2(_contentMin.X, _cursorY);
        var zoneMax = new Vector2(ResolveContentRightEdge(rightInset), _contentMax.Y);

        var cursorY = zoneMin.Y + offset;
        ImGui.SetCursorScreenPos(new Vector2(zoneMin.X, cursorY));

        draw(new ContentZoneContext(zoneMin, zoneMax, _background));

        var actualEndY = ImGui.GetCursorScreenPos().Y;
        var actualHeight = actualEndY - zoneMin.Y;
        _cursorY = actualEndY;
        return actualHeight;
    }

    private float ResolveContentRightEdge(float? rightInset)
    {
        return rightInset.HasValue
            ? _contentRightEdge - MetricsScope.Scale(rightInset.Value)
            : _contentMax.X;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
    }
}

internal static class ContentCardComponent
{
    private static readonly Vector2 DefaultPadding = new(SlapPx.Space12, SlapPx.Space12);
    /// <summary>
    /// Built-in right inset (24px) so ContentCard content area right edge
    /// defaults to 36px (12px padding + 24px inset), matching SettingPage
    /// (16px + 20px = 36px).  Callers override via
    /// <see cref="ContentCardSpec.RightInset"/>.
    /// </summary>
    private const float DefaultRightInset = SlapPx.Space24;

    internal static float ResolveNaturalHeight(ContentCardSpec spec, float contentHeight)
    {
        var padding = ResolvePadding(spec);
        return MathF.Max(0f, contentHeight) + padding.Y * 2f;
    }

    public static ContentCardScope Begin(ContentCardSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("ContentCardSpec.Key.Value must not be empty.", nameof(spec));

        var padding = ResolvePadding(spec);
        var rightInset = MetricsScope.Scale(spec.RightInset ?? DefaultRightInset);
        var available = ImGui.GetContentRegionAvail();
        var cardMin = ImGui.GetCursorScreenPos();
        var cardMax = cardMin + new Vector2(
            MathF.Max(0f, available.X),
            MathF.Max(0f, available.Y));
        var contentMin = cardMin + padding;
        var contentRightEdge = cardMax.X - padding.X;
        var contentMax = new Vector2(contentRightEdge - rightInset, cardMax.Y - padding.Y);
        var background = SurfaceComponent.ResolveVisualBlockBackground(spec.State);
        var rounding = SlapCorners.ControlRadius;
        var drawList = ImGui.GetWindowDrawList();

        LayeredShadow.Draw(drawList, cardMin, cardMax, rounding, spec.Shadow);
        drawList.AddRectFilled(cardMin, cardMax, ImGui.ColorConvertFloat4ToU32(background), rounding);

        // Reserve card space via Dummy
        ImGui.Dummy(cardMax - cardMin);

        return new ContentCardScope(
            cardMin,
            cardMax,
            contentMin,
            contentMax,
            contentRightEdge,
            background);
    }

    private static Vector2 ResolvePadding(ContentCardSpec spec) =>
        MetricsScope.ScalePadding(spec.Padding ?? DefaultPadding);
}
