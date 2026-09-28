using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct PopupColumnLayout(
	    bool HasSelectionColumn,
	    bool ReserveLeadingIcon,
	    bool ReserveTrailingIcon)
{
    public static PopupColumnLayout Default { get; } = new(
        HasSelectionColumn: true,
        ReserveLeadingIcon: true,
	        ReserveTrailingIcon: false);
	}

internal readonly record struct PopupMeasureItem(
    string Label,
    bool HasLeadingIcon = false,
    bool HasTrailingIcon = false,
    bool IsSelected = false);

internal readonly record struct PopupPanelSpec(
	    ControlKey Key,
	    Variant Variant = Variant.Base,
    LayeredShadowSpec Shadow = default,
    float MinWidthUnits = PopupPanelComponent.DefaultMinWidthUnits,
    /// <summary>
    /// Maximum popup height in <see cref="MetricsScope.UnitHeight"/> multiples.
    /// When the content exceeds this limit, the popup body becomes scrollable.
    /// Defaults to 7.5 units (7.5 items at the fixed item height).
    /// Set to <see cref="float.PositiveInfinity"/> for no height cap.
    /// </summary>
    float MaxHeightUnits = 8f,
    /// <summary>
    /// Number of separator rows rendered inside this popup. Used by height
    /// estimation so separators are not omitted when calculating scroll height.
    /// </summary>
    int SeparatorCount = 0,
    /// <summary>
	    /// Item descriptions used to measure natural popup width and derive
	    /// selection/leading/trailing columns. Must match the items drawn through
	    /// <see cref="PopupPanelScope.Item"/>.
	    /// </summary>
	    IReadOnlyList<PopupMeasureItem>? MeasureItems = null,
	    /// <summary>
	    /// Optional explicit background color override. When null,
    /// <see cref="Variant"/> resolves the background via
	    /// <see cref="PopupPanelComponent.ResolvePopupColors"/>.
	    /// </summary>
	    Vector4? Background = null,
	    /// <summary>
	    /// Optional palette override. When non-null, bypasses <see cref="Variant"/>
    /// resolution entirely for popup background, border, and text colours.
    /// </summary>
    ControlPalette? Palette = null,
    /// <summary>Optional screen-space position resolved by the owning trigger.</summary>
    Vector2? Anchor = null,
    /// <summary>
    /// Optional trigger top-left. When the popup cannot fit below
    /// <see cref="Anchor"/>, it flips upward so its bottom edge aligns with
    /// this point's Y.
    /// </summary>
    Vector2? TriggerTopLeft = null,
    /// <summary>Optional resolved popup width in scaled pixels.</summary>
    float ResolvedWidth = 0f,
    /// <summary>Whether opening the popup should move keyboard focus into it.</summary>
    bool FocusOnOpen = true,
    /// <summary>
    /// Optional panel padding in unscaled pixels. When null, the shared
    /// popup-panel default is used so popup panels and quick-input popups
    /// stay consistent.
    /// </summary>
    Vector2? Padding = null
);

internal readonly record struct PopupItemSpec(
    ControlKey Key,
    string Label,
    FontAwesomeIcon? Icon = null,
    Variant Variant = Variant.Base,
    ControlState State = ControlState.None,
    string? Tooltip = null,
    uint GameIconId = 0,
    /// <summary>
    /// Optional semantic text accent for commands such as destructive actions.
    /// </summary>
    TextSlot? Semantic = null,
    /// <summary>
    /// Optional icon drawn at the trailing edge, typically a submenu chevron.
    /// </summary>
    FontAwesomeIcon? TrailingIcon = null
);

internal readonly record struct DropdownSpec(
ControlKey Key,
IReadOnlyList<DropdownOptionSpec> Options,
int SelectedIndex = 0,
ButtonSize Size = default,
    ControlState State = ControlState.None,
    string? Tooltip = null,
    string? Placeholder = null,
    FontAwesomeIcon? TriggerIcon = null,
    FontAwesomeIcon? ArrowIcon = FontAwesomeIcon.ChevronDown,
    /// <summary>
    /// Resolved trigger width in scaled pixels, set by an external layout
    /// system (e.g. <see cref="ResponsiveSlotGroup"/>). When &gt; 0, the
    /// Dropdown renders at this allocated width, bypassing <see cref="Size"/>.
    /// An explicit Rect width remains the responsive preferred baseline and
    /// may be crossed while shrinking toward the trigger's structural minimum.
    /// Label clipping and fading remain owned by the Dropdown component.
    /// </summary>
    float ResolvedWidth = 0f,
    uint TriggerGameIconId = 0,
    /// <summary>
    /// Optional option source invoked only while the popup is opening or open.
    /// Use this when enumerating the full option list is expensive; the static
    /// <see cref="Options"/> still supplies the closed trigger preview.
    /// </summary>
    Func<DropdownOptionsSnapshot>? OpenOptionsProvider = null,
    Variant Variant = Variant.Base,
    LayeredShadowSpec Shadow = default
);

internal readonly record struct DropdownOptionsSnapshot(
    IReadOnlyList<DropdownOptionSpec> Options,
    int SelectedIndex
);

internal readonly record struct DropdownOptionSpec(
    string Label,
    FontAwesomeIcon? Icon = null,
    string? Tooltip = null,
    ControlState State = ControlState.None,
    uint GameIconId = 0
);

internal readonly record struct DropdownResult(
    int SelectedIndex,
    int ClickedIndex,
    bool Changed,
    ControlResult TriggerResult
);

internal sealed class PopupPanelScope : IDisposable
{
private bool _disposed;
private readonly bool _stylePushed;
private readonly bool _isOpen;
private readonly string _key;
	private readonly Vector4? _panelBackground;
		private readonly Vector4? _panelText;
		private readonly ControlPalette? _panelPalette;
		private readonly Vector4 _borderColor;
		private readonly bool _hasChild;
		private readonly PopupColumnLayout _columns;
		private readonly IDisposable? _hoverScope;
private bool _dismissWhenPointerLeaves;
private bool _submenuTriggerEnabled;
private bool _submenuTriggerHovered;

    public bool IsOpen => _isOpen;

	    internal PopupPanelScope(bool isOpen, bool stylePushed, string key = "", Vector4? panelBackground = null,
	        Vector4? panelText = null, Vector4 borderColor = default, bool hasChild = false,
	        IDisposable? hoverScope = null, ControlPalette? panelPalette = null,
	        PopupColumnLayout? columns = null)
	    {
	_disposed = false;
	_isOpen = isOpen;
	_stylePushed = stylePushed;
	_key = key;
	_panelBackground = panelBackground;
	_panelText = panelText;
		_panelPalette = panelPalette;
		_borderColor = borderColor;
		_hasChild = hasChild;
		_columns = columns ?? PopupColumnLayout.Default;
		_hoverScope = hoverScope;
_dismissWhenPointerLeaves = false;
_submenuTriggerEnabled = false;
_submenuTriggerHovered = false;
    }

    public ControlResult Item(PopupItemSpec spec)
    {
        return Item(spec, retainHoverVisual: false);
    }

    private ControlResult Item(
        PopupItemSpec spec,
        bool retainHoverVisual,
        ImGuiHoveredFlags additionalHoverFlags = ImGuiHoveredFlags.None)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PopupPanelScope));
        if (!_isOpen)
            return default;

	        return PopupPanelComponent.DrawItemInternal(
	            spec,
	            _panelBackground,
	            _panelText,
	            retainHoverVisual,
	            additionalHoverFlags,
	            _panelPalette,
	            _columns
	        );
	    }

    public PopupPanelScope Submenu(PopupItemSpec trigger, PopupPanelSpec submenu)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PopupPanelScope));
        if (!_isOpen)
            return new PopupPanelScope(false, false);

        var submenuOpen = ImGui.IsPopupOpen(submenu.Key.Value);
        var additionalHoverFlags = submenuOpen
            ? ImGuiHoveredFlags.AllowWhenBlockedByPopup
                | ImGuiHoveredFlags.AllowWhenOverlapped
            : ImGuiHoveredFlags.None;
        var triggerResult = Item(
            trigger with { TrailingIcon = FontAwesomeIcon.ChevronRight },
            retainHoverVisual: submenuOpen,
            additionalHoverFlags: additionalHoverFlags
        );
        var triggerEnabled = (trigger.State & ControlState.Disabled) == ControlState.None;
        if (!submenuOpen && triggerEnabled && triggerResult.Hovered)
        {
            ImGui.OpenPopup(submenu.Key.Value);
            submenuOpen = true;
        }

        if (!submenuOpen)
            return new PopupPanelScope(false, false);

        ImGui.SetNextWindowPos(
            new Vector2(triggerResult.Max.X, triggerResult.Min.Y),
            ImGuiCond.Always
        );
        var scope = PopupPanelComponent.Begin(submenu);
        if (!scope.IsOpen)
            return scope;

        scope.ConfigureSubmenuDismissal(
            triggerEnabled,
            triggerResult.Hovered
        );

        return scope;
    }

    private void ConfigureSubmenuDismissal(
        bool triggerEnabled,
        bool triggerHovered
    )
    {
        _dismissWhenPointerLeaves = true;
        _submenuTriggerEnabled = triggerEnabled;
        _submenuTriggerHovered = triggerHovered;
    }

    public void Separator()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PopupPanelScope));
        if (!_isOpen)
            return;

        PopupPanelComponent.DrawSeparatorInternal(_borderColor);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (_isOpen)
            {
                // A scroll child owns the item draw list, so flush its hover
                // outline before leaving the child window.
                var rt = ThemeScope.Resolved;
                var displaySize = ImGui.GetIO().DisplaySize;
                var windowMin = ImGui.GetWindowPos();
                var windowMax = windowMin + ImGui.GetWindowSize();
                var scrollY = ImGui.GetScrollY();
                var scrollMaxY = ImGui.GetScrollMaxY();
                var edgeEpsilon = MetricsScope.Scale(1f);
                var clipMinY = scrollY <= edgeEpsilon ? 0f : windowMin.Y;
                var clipMaxY = scrollY >= scrollMaxY - edgeEpsilon
                    ? displaySize.Y
                    : windowMax.Y;
                HoverArbitration.Current.SetOutlineClipRect(
                    new Vector2(0f, clipMinY),
                    new Vector2(displaySize.X, clipMaxY));
                HoverArbitration.Current.FlushOutline(_key, 1f);
                if (_hasChild)
                    ImGui.EndChild();

                if (_dismissWhenPointerLeaves)
                {
                    var pointer = ImGui.GetIO().MousePos;
                    var submenuMin = ImGui.GetWindowPos();
                    var submenuMax = submenuMin + ImGui.GetWindowSize();
                    var submenuHovered = pointer.X >= submenuMin.X
                        && pointer.Y >= submenuMin.Y
                        && pointer.X < submenuMax.X
                        && pointer.Y < submenuMax.Y;
                    if (
                        !_submenuTriggerEnabled
                        || (!_submenuTriggerHovered && !submenuHovered)
                    )
                    {
                        ImGui.CloseCurrentPopup();
                    }
                }

                ImGui.EndPopup();
            }
        }
        finally
        {
            _hoverScope?.Dispose();
            if (_stylePushed)
            {
                ImGui.PopStyleColor(6);
                ImGui.PopStyleVar(5);
            }
        }
    }
}

internal static class PopupPanelComponent
{
    internal const float ItemPaddingX = SlapPx.Space16;
    internal const float ItemIconLeftPad = SlapPx.Space4;
    internal const float ItemSpacingY = SlapPx.Space1;
    /// <summary>Shared default panel padding for popup panels and quick-input popups.</summary>
    internal static readonly Vector2 PanelPadding = new(SlapPx.Space4, SlapPx.Space4);
    private const float SeparatorHeight = SlapPx.Space8;
    private const float SeparatorInset = SlapPx.Space8;

    /// <summary>
    /// Default minimum width for popup panels, expressed in UnitWidth multiples.
    /// V2 uses <c>StyledPopupPanelMinWidth = 140f</c> raw pixels (= UnitWidth * 2
    /// = 70 * 2). With UnitWidth = 70, this is exactly 2 units.
    /// </summary>
    internal const float DefaultMinWidthUnits = 2f;

    public static PopupPanelScope Begin(PopupPanelSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("PopupPanelSpec.Key.Value must not be empty.", nameof(spec));

        var itemSpacingY = MetricsScope.ScaleGap(ItemSpacingY);
	        var windowPadding = MetricsScope.ScalePadding(spec.Padding ?? PanelPadding);
	        var radius = SlapCorners.ControlRadius;
	        var colors = ResolvePopupColors(spec.Variant, spec.Background, spec.Palette);
	        var measureItems = spec.MeasureItems is { Count: > 0 } items ? items : null;
	        var columns = ResolvePopupColumns(measureItems);
	        var minWidth = MetricsScope.UnitWidth * Normalize(spec.MinWidthUnits, DefaultMinWidthUnits);
        var maxHeight = float.IsInfinity(spec.MaxHeightUnits)
            ? float.MaxValue
            : MetricsScope.UnitHeight * spec.MaxHeightUnits;
        // The scroll child lives inside the popup window padding, so its
        // height cap subtracts the vertical padding to keep the total popup
        // height within MaxHeightUnits.
        var childMaxHeight = MathF.Max(0f, maxHeight - windowPadding.Y * 2f);
        var itemLabels = measureItems?.Select(item => item.Label ?? string.Empty).ToList();
        var estimatedContentHeight = itemLabels is null
            ? 0f
            : itemLabels.Count * MetricsScope.UnitHeight
                + Math.Max(0, itemLabels.Count - 1) * itemSpacingY;
        estimatedContentHeight += spec.SeparatorCount
            * (MetricsScope.ScaleGap(SeparatorHeight) + itemSpacingY);
        // Popup windows clip to their own bounds; push fullscreen clip so
        // the shadow extends beyond the popup rect (matches V2 behaviour).
        var resolvedShadow = LayeredShadowSpec.ResolveStandard(spec.Shadow);
        var shadow = resolvedShadow.Layers > 0 && !resolvedShadow.PushFullscreenClip
            ? resolvedShadow with { PushFullscreenClip = true }
            : resolvedShadow;

        // Resolve popup width: max(measured content, hard minimum).
        var popupWidth = spec.ResolvedWidth > 0f
            ? MathF.Max(minWidth, spec.ResolvedWidth)
            : MetricsScope.UnitWidth * Normalize(spec.MinWidthUnits, DefaultMinWidthUnits);
        if (spec.ResolvedWidth <= 0f && itemLabels is not null)
        {
	            var iconSlot = MetricsScope.ResolveIconSlotWidth(false);
	            var selectionSlot = columns.HasSelectionColumn ? iconSlot : 0f;
	            var leadingIconSlot = columns.ReserveLeadingIcon ? iconSlot : 0f;
	            var leftPad = MetricsScope.ScalePadding(ItemIconLeftPad);
	            var rightPad = MetricsScope.ScalePadding(ItemPaddingX);
	            if (columns.ReserveTrailingIcon)
	                rightPad += MetricsScope.ResolveIconSlotWidth(false);
            var measuredContentWidth = 0f;
            using (Slap.PushFont(SlapFontSize.Regular, SlapFontWeight.Bold))
            {
                foreach (var label in itemLabels)
                {
	                    var labelWidth = ImGui.CalcTextSize(label ?? string.Empty).X;
	                    measuredContentWidth = MathF.Max(
	                        measuredContentWidth,
	                        leftPad + selectionSlot + leadingIconSlot + labelWidth + rightPad);
                }
            }

            if (estimatedContentHeight > childMaxHeight)
                measuredContentWidth += ImGui.GetStyle().ScrollbarSize;
            popupWidth = MathF.Max(popupWidth, measuredContentWidth + windowPadding.X * 2f);
        }

        if (spec.Anchor.HasValue)
        {
            var estimatedHeight = itemLabels is not null && estimatedContentHeight > 0f
                ? MathF.Min(estimatedContentHeight + windowPadding.Y * 2f, maxHeight)
                : float.IsInfinity(maxHeight) ? 0f : maxHeight;
            var anchor = ResolveAnchoredPopupPos(
                spec.Anchor.Value,
                spec.TriggerTopLeft,
                popupWidth,
                estimatedHeight
            );
            ImGui.SetNextWindowPos(anchor, ImGuiCond.Always);
        }
        ImGui.SetNextWindowSize(new Vector2(popupWidth, 0f));
        ImGui.SetNextWindowSizeConstraints(new Vector2(minWidth, 0f), new Vector2(float.MaxValue, maxHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, windowPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0f, itemSpacingY));
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, radius);
        ImGui.PushStyleVar(ImGuiStyleVar.PopupBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, radius);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, colors.Background);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, colors.Text);
        ImGui.PushStyleColor(
            ImGuiCol.ScrollbarGrabHovered,
            SlapColor.DeriveHoverColor(colors.Text));
        ImGui.PushStyleColor(
            ImGuiCol.ScrollbarGrabActive,
            SlapColor.DeriveActiveColor(colors.Text));

        var popupFlags = spec.FocusOnOpen
            ? ImGuiWindowFlags.NoMove
            : ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoFocusOnAppearing
                | ImGuiWindowFlags.NoNav;
        if (!ImGui.BeginPopup(spec.Key.Value, popupFlags))
            return new PopupPanelScope(false, true, spec.Key.Value, hasChild: false);

        if (!spec.FocusOnOpen)
            ImGuiP.BringWindowToDisplayFront(ImGuiP.GetCurrentWindow());

        // Popup items must not inherit an enclosing SurfaceList's hover state
        // or outline clip rect. Nested popups naturally receive another scope.
        var hoverScope = HoverArbitration.Push();

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        SurfaceComponent.DrawFrostedPanelBackground(drawList, pos, pos + size, radius);
        if (size.X > 0f && size.Y > 0f)
        {
            // Clip shadow above the popup top edge so it doesn't bleed
            // onto the host button/trigger placed immediately above.
            var displaySize = ImGui.GetIO().DisplaySize;
            drawList.PushClipRect(new Vector2(0f, pos.Y), displaySize, false);
            LayeredShadow.Draw(drawList, pos, pos + size, radius, shadow);
            drawList.PopClipRect();
            if (ThemeScope.Resolved.IsTransparentTheme)
            {
                SurfaceComponent.DrawPanelBorder(
                    drawList,
                    pos,
                    pos + size,
                    ThemeScope.Resolved.Border,
                    radius);
            }
        }

        var hasChild = false;
        if (!float.IsInfinity(spec.MaxHeightUnits))
        {
            // Estimate content height from ItemLabels when provided so the
            // child isn't forced to maxHeight when items fit without scrolling.
            // Without ItemLabels, fall back to the full childMaxHeight.
            float childHeight = childMaxHeight;
            if (itemLabels is not null && estimatedContentHeight > 0f)
                childHeight = MathF.Min(estimatedContentHeight, childMaxHeight);

            ImGui.BeginChild("##popupScroll", new Vector2(0f, childHeight));
            if (ImGui.IsWindowAppearing())
                ImGui.SetScrollY(0f);
            hasChild = true;
        }

	        return new PopupPanelScope(
	            true,
	            true,
	            spec.Key.Value,
	            colors.Background,
	            colors.Text,
	            colors.Border,
	            hasChild,
	            hoverScope,
	            colors.Palette,
	            columns);
    }

    public static ControlResult DrawItem(PopupItemSpec spec) => DrawItemInternal(spec, null);

    internal static void DrawSeparatorInternal(Vector4 color)
    {
        var width = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        var height = MetricsScope.ScaleGap(SeparatorHeight);
        var inset = MetricsScope.ScalePadding(SeparatorInset);
        var min = ImGui.GetCursorScreenPos();
        var y = min.Y + height * 0.5f;
        if (width > 0f)
        {
            var resolvedInset = MathF.Min(inset, width * 0.5f);
            ImGui.GetWindowDrawList().AddLine(
                new Vector2(min.X + resolvedInset, y),
                new Vector2(min.X + width - resolvedInset, y),
                ImGui.ColorConvertFloat4ToU32(SlapColor.WithAlpha(color, 0.55f)),
                MetricsScope.BorderThickness
            );
        }
        ImGui.Dummy(new Vector2(width, height));
    }

	    internal static ControlResult DrawItemInternal(
	        PopupItemSpec spec,
	        Vector4? panelBackground = null,
	        Vector4? panelText = null,
	        bool retainHoverVisual = false,
	        ImGuiHoveredFlags additionalHoverFlags = ImGuiHoveredFlags.None,
	        ControlPalette? panelPalette = null,
	        PopupColumnLayout? columns = null)
	    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("PopupItemSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        try
        {
            var width = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            var height = MetricsScope.UnitHeight;
            var rawClicked = ImGui.InvisibleButton("##item", new Vector2(width, height));
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var ix = SlapInteraction.Capture(
                spec.State,
                rawClicked,
                additionalHoverFlags: additionalHoverFlags
            );
	            var visuallyHovered = !ix.Disabled
	                && (ix.Hovered || ix.Highlighted || retainHoverVisual);
	            var colors = ResolveItemColors(
	                spec.Variant,
	                visuallyHovered,
	                ix.Active,
	                ix.Disabled,
	                panelBackground,
	                panelText,
	                panelPalette);
            if (spec.Semantic is { } itemSemantic)
            {
                colors = colors with
                {
                    Text = SlapColor.WithDisabledAlpha(
                        ThemeScope.Resolved.GetTextSlot(itemSemantic),
                        ix.Disabled
                    ),
                };
            }
            var drawList = ImGui.GetWindowDrawList();
            var windowMin = ImGui.GetWindowPos();
            var windowMax = windowMin + ImGui.GetWindowSize();
            var visualMin = new Vector2(min.X, MathF.Max(min.Y, windowMin.Y));
            var visualMax = new Vector2(max.X, MathF.Min(max.Y, windowMax.Y));
            var hasVisibleArea = visualMax.X > visualMin.X && visualMax.Y > visualMin.Y;
            var visualRounding = SlapCorners.ForRect(visualMin, visualMax);

	            var shouldFill = visuallyHovered
	                || ix.Active
	                || colors.Background.W > 0f;
            if (shouldFill && hasVisibleArea)
                drawList.AddRectFilled(
                    visualMin,
                    visualMax,
                    ImGui.ColorConvertFloat4ToU32(colors.Background),
                    visualRounding);

            // When hover/active fill is suppressed and the item is not selected,
            // there is no painted background behind the text — pass transparent
            // so the edge fade (if any) blends into the actual (empty) backdrop
            // rather than an unpainted colour.
	            var effectiveFadeBg = colors.Background;
	            var visualMode = SlapInteraction.ResolveVisualMode(
	                visuallyHovered,
	                ix.Active,
	                ix.Disabled);
	            DrawItemContent(
	                drawList,
	                spec,
	                min,
	                max,
	                colors.Text,
	                effectiveFadeBg,
	                visualMode,
	                columns ?? PopupColumnLayout.Default,
	                ix.Selected);

            // Capture hover outline for deferred flush (matches V2's
            // CapturePopupSelectableOutlineForLastItem pattern).
            if (visuallyHovered && hasVisibleArea)
            {
                var outlineAccent = spec.Semantic is { } outlineSemantic
                    ? ThemeScope.Resolved.GetTextSlot(outlineSemantic)
                    : !ix.Selected
                        && panelBackground is { } panelBg
                        && !ThemeScope.Resolved.IsTransparentTheme
                        ? panelBg
                        : ThemeScope.Resolved.ResolveOutlineAccent(spec.Variant);
                HoverArbitration.Current.CaptureOutline(
                    min,
                    max,
                    true,
                    selected: false,
                    accent: outlineAccent,
                    rounding: SlapCorners.ControlRadius);
            }

            HoverArbitration.Current.TryShowTooltip(ix.Hovered, spec.Tooltip);

            return ix.ToControlResult(min, max);
        }
        finally
        {
            ImGui.PopID();
        }
    }

	    private static void DrawItemContent(
	        ImDrawListPtr drawList,
	        PopupItemSpec spec,
	        Vector2 min,
	        Vector2 max,
	        Vector4 textColor,
	        Vector4 fadeBackground,
	        ControlVisualMode visualMode,
	        PopupColumnLayout columns,
	        bool selected)
    {
        var (iconText, iconFont) = SlapIcon.Resolve(spec.Icon);
        var hasGameIcon = spec.GameIconId != 0;
        var hasFaIcon = !string.IsNullOrEmpty(iconText);
        var hasIcon = hasGameIcon || hasFaIcon;
        var iconSlotWidth = MetricsScope.ResolveIconSlotWidth(false);
        var leading = IconTextComponent.ResolveLeadingLayout(
            hasGameIcon,
            hasFaIcon,
            !string.IsNullOrEmpty(spec.Label),
            ItemIconLeftPad,
            0f,
            iconSlotWidth,
            centered: false);
        // GameIcon splits the FA Space4 alignment spacing around its slot so both
        // icon types keep the same text origin in left-aligned and centered layouts.
        // Left-aligned items without an icon still reserve the leading icon
        // slot so their labels align with icon-bearing sibling items.
	        var selectionSlotWidth = columns.HasSelectionColumn ? iconSlotWidth : 0f;
	        var baseLeftPad = hasIcon ? leading.LeftPadding
	            : MetricsScope.ScalePadding(ItemIconLeftPad);
	        var leftPad = baseLeftPad + selectionSlotWidth;
	        var rightPad = MetricsScope.ScalePadding(ItemPaddingX)
	            + (spec.TrailingIcon.HasValue ? iconSlotWidth : 0f);
	        var contentMin = new Vector2(min.X + leftPad, min.Y);
	        var contentMax = new Vector2(MathF.Max(contentMin.X, max.X - rightPad), max.Y);
	        var x = contentMin.X;
	        var colorU32 = ImGui.ColorConvertFloat4ToU32(textColor);

	        if (columns.HasSelectionColumn && selected)
	        {
	            var selectionMin = new Vector2(min.X + baseLeftPad, min.Y);
	            var (checkText, checkFont) = SlapIcon.Resolve(FontAwesomeIcon.Check);
	            if (!string.IsNullOrEmpty(checkText))
	            {
	                if (checkFont.HasValue)
	                    ImGui.PushFont(checkFont.Value);
	                try
	                {
	                    var checkSize = ImGui.CalcTextSize(checkText);
	                    var checkX = selectionMin.X + SlapIcon.ResolveHorizontalCenteringOffset(
	                        checkFont,
	                        checkText,
	                        iconSlotWidth);
	                    var checkY = min.Y + MathF.Max(
	                        0f,
	                        ((max.Y - min.Y) - checkSize.Y) * 0.5f);
	                    drawList.AddText(new Vector2(checkX, checkY), colorU32, checkText);
	                }
	                finally
	                {
	                    if (checkFont.HasValue)
	                        ImGui.PopFont();
	                }
	            }
	        }

        if (hasIcon)
        {
            if (hasGameIcon)
            {
                var (iconMin, iconMax) = GameIconComponent.ResolveControlIconRect(
                    new Vector2(x, min.Y),
                    new Vector2(x + iconSlotWidth, max.Y)
                );
                GameIconComponent.Draw(
                    drawList, spec.GameIconId, iconMin, iconMax,
                    false,
                    visualMode: visualMode);
            }
            else
            {
                Vector2 iconSize;
                if (iconFont.HasValue)
                    ImGui.PushFont(iconFont.Value);
                try
                {
                    iconSize = ImGui.CalcTextSize(iconText);
                    var iconY = min.Y + MathF.Max(0f, ((max.Y - min.Y) - iconSize.Y) * 0.5f);
                    var iconX = x + SlapIcon.ResolveHorizontalCenteringOffset(
                        iconFont,
                        iconText!,
                        iconSlotWidth);
                    drawList.PushClipRect(contentMin, new Vector2(MathF.Min(contentMax.X, contentMin.X + iconSlotWidth), contentMax.Y), true);
                    drawList.AddText(new Vector2(iconX, iconY), colorU32, iconText);
                    drawList.PopClipRect();
                }
                finally
                {
                    if (iconFont.HasValue)
                        ImGui.PopFont();
                }
            }

            x += leading.IconSlotWidth + leading.TextGap;
	        }
	        else
	        {
	            if (columns.ReserveLeadingIcon)
	                x += iconSlotWidth;
	        }

        var textMin = new Vector2(x, contentMin.Y);
        if (textMin.X >= contentMax.X)
            return;

        // Push bold font for label text to match V2 popup item styling.
        using (Slap.PushFont(SlapFontSize.Regular, SlapFontWeight.Bold))
        {
            var textSize = ImGui.CalcTextSize(spec.Label);
            var textY = min.Y + MathF.Max(0f, ((max.Y - min.Y) - textSize.Y) * 0.5f);
            drawList.PushClipRect(textMin, contentMax, true);
            drawList.AddText(new Vector2(textMin.X, textY), colorU32, spec.Label);
            drawList.PopClipRect();

            if (textSize.X > contentMax.X - textMin.X)
                EdgeFade.DrawRight(drawList, textMin, contentMax.X - textMin.X, max.Y - min.Y, fadeBackground);
        }

        if (spec.TrailingIcon is { } trailingIcon)
        {
            var (trailingText, trailingFont) = SlapIcon.Resolve(trailingIcon);
            if (!string.IsNullOrEmpty(trailingText))
            {
                if (trailingFont.HasValue)
                    ImGui.PushFont(trailingFont.Value);
                try
                {
                    var trailingSize = ImGui.CalcTextSize(trailingText);
                    var trailingX = max.X
                        - MetricsScope.ScalePadding(ItemPaddingX)
                        - trailingSize.X;
                    var trailingY = min.Y
                        + MathF.Max(0f, ((max.Y - min.Y) - trailingSize.Y) * 0.5f);
                    drawList.AddText(
                        new Vector2(trailingX, trailingY),
                        colorU32,
                        trailingText
                    );
                }
                finally
                {
                    if (trailingFont.HasValue)
                        ImGui.PopFont();
                }
            }
        }
    }

	    private static PopupColumnLayout ResolvePopupColumns(
	        IReadOnlyList<PopupMeasureItem>? measureItems)
	    {
	        if (measureItems is null || measureItems.Count == 0)
	            return PopupColumnLayout.Default;

	        return new PopupColumnLayout(
	            HasSelectionColumn: measureItems.Any(item => item.IsSelected),
	            ReserveLeadingIcon: measureItems.Any(item => item.HasLeadingIcon),
	            ReserveTrailingIcon: measureItems.Any(item => item.HasTrailingIcon));
	    }

	    private static PopupColors ResolvePopupColors(Variant variant, Vector4? backgroundOverride = null, ControlPalette? paletteOverride = null)
	    {
	        var rt = ThemeScope.Resolved;
	        var palette = paletteOverride ?? rt.GetButtonPalette(variant);
	        var bg = backgroundOverride ?? palette.Base;

        return new PopupColors(palette, bg, ThemeScope.Resolved.Border, palette.Text);
	    }

	    private static ItemColors ResolveItemColors(
	        Variant variant,
	        bool hovered,
	        bool active,
	        bool disabled,
	        Vector4? panelBackground = null,
	        Vector4? panelText = null,
	        ControlPalette? panelPalette = null)
	    {
	        var rt = ThemeScope.Resolved;
	        var itemPalette = rt.GetButtonPalette(variant);
	        var palette = panelPalette is { } resolvedPanel
	            ? resolvedPanel
	            : itemPalette;
	        var bg = panelBackground ?? palette.Base;
	        var panelTextColor = panelText ?? palette.Text;

	        var background = active ? SlapColor.DeriveActiveColor(bg)
	                       : hovered ? SlapColor.DeriveHoverColor(bg)
	                       : SlapColor.WithAlpha(bg, 0f);
	        var textColor = variant == Variant.Tab ? rt.Subtle : panelTextColor;

	        if (disabled)
	            background = SlapColor.WithDisabledAlpha(background, disabled);

	        var text = SlapColor.WithDisabledAlpha(textColor, disabled);
	        return new ItemColors(background, text);
	    }

    private static float Normalize(float value, float fallback) =>
        value > 0f && !float.IsInfinity(value) ? value : fallback;

    private static Vector2 ResolveAnchoredPopupPos(
        Vector2 anchor,
        Vector2? triggerTopLeft,
        float width,
        float estimatedHeight
    )
    {
        var display = ImGui.GetIO().DisplaySize;
        var margin = MetricsScope.Scale(SlapPx.Space8);
        var x = MathF.Max(margin, MathF.Min(anchor.X, display.X - width - margin));
        var y = anchor.Y;
        if (
            estimatedHeight > 0f
            && y + estimatedHeight > display.Y - margin
            && triggerTopLeft is { } topLeft
            && topLeft.Y - estimatedHeight >= margin
        )
        {
            y = topLeft.Y - estimatedHeight;
        }

        return new Vector2(x, y);
    }

	    private readonly record struct PopupColors(
	        ControlPalette Palette,
	        Vector4 Background,
	        Vector4 Border,
	        Vector4 Text);
    private readonly record struct ItemColors(Vector4 Background, Vector4 Text);
}

internal static class DropdownComponent
{
    public static DropdownResult Draw(DropdownSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("DropdownSpec.Key.Value must not be empty.", nameof(spec));
        if (spec.Options.Count == 0)
            return new DropdownResult(-1, -1, false, default);

        ImGui.PushID(spec.Key.Value);
        using var _hover = HoverArbitration.Push();
        try
        {
            var selectedIndex = Math.Clamp(spec.SelectedIndex, 0, spec.Options.Count - 1);
            var selectedOption = spec.Options[selectedIndex];

            // When ResolvedWidth is set, use it directly — the external layout
            // has already determined the exact width. Label clip/fade is handled
            // purely in DrawTrigger via IconTextComponent.DrawInRect. The minimum
            // width is the 2-icon form (trigger icon slot + arrow slot) when a
            // trigger icon is present, or UnitHeight (arrow only) otherwise.
            // This matches MeasureResponsiveWidth's minimum width.
            Vector2 size;
            if (spec.ResolvedWidth > 0f)
            {
                var h = MetricsScope.UnitHeight;
                var hasTriggerIcon = spec.TriggerGameIconId != 0
                    || spec.TriggerIcon.HasValue
                    || (spec.Options.Count > 0
                        && (spec.Options[0].GameIconId != 0 || spec.Options[0].Icon.HasValue));
                var minWidth = hasTriggerIcon
                    ? MathF.Max(h, MetricsScope.ResolveIconSlotWidth(false) * 2f)
                    : h;
                var resolvedW = MathF.Max(minWidth, spec.ResolvedWidth);
                size = new Vector2(resolvedW, h);
            }
            else
            {
                size = ResolveButtonSize(spec, selectedOption);
            }

            var min = ImGui.GetCursorScreenPos();
            var rawClicked = ImGui.InvisibleButton("##trigger", size);
            var itemMin = ImGui.GetItemRectMin();
            var itemMax = ImGui.GetItemRectMax();
            var ix = SlapInteraction.Capture(spec.State, rawClicked);
            var popupOpen = ImGui.IsPopupOpen("##dropdownPopup");
            if (ix.Clicked)
                ImGui.OpenPopup("##dropdownPopup");

            var triggerDegraded = DrawTrigger(
                spec,
                selectedOption,
                itemMin,
                itemMax,
                ix.Hovered && !ix.Disabled,
                ix.Active || popupOpen,
                ix.Disabled);
            var dropdownTooltip = ResponsiveTooltip.Compose(
                triggerDegraded,
                selectedOption.Label,
                spec.Tooltip ?? selectedOption.Tooltip);
            HoverArbitration.Current.TryShowTooltip(ix.Hovered, dropdownTooltip);

            var popupOptions = spec.Options;
            var popupSelectedIndex = selectedIndex;
            if (spec.OpenOptionsProvider != null && (popupOpen || ix.Clicked))
            {
                var snapshot = spec.OpenOptionsProvider();
                if (snapshot.Options.Count > 0)
                {
                    popupOptions = snapshot.Options;
                    popupSelectedIndex = Math.Clamp(snapshot.SelectedIndex, 0, popupOptions.Count - 1);
                }
            }

            var nextSelected = popupSelectedIndex;
            var clickedIndex = -1;

	            var measureItems = popupOptions
	                .Select((option, index) => new PopupMeasureItem(
	                    option.Label ?? string.Empty,
	                    HasLeadingIcon: option.GameIconId != 0 || option.Icon.HasValue,
	                    HasTrailingIcon: false,
	                    IsSelected: index == popupSelectedIndex))
	                .ToList();
	            using (var popup = PopupPanelComponent.Begin(new PopupPanelSpec(
	                "##dropdownPopup", spec.Variant,
	                MeasureItems: measureItems,
	                Anchor: new Vector2(itemMin.X, itemMax.Y),
	                TriggerTopLeft: itemMin)))
            {
                if (popup.IsOpen)
                {
                    for (var i = 0; i < popupOptions.Count; i++)
                    {
                        var option = popupOptions[i];
                        var state = option.State;
                        if (i == popupSelectedIndex)
                            state |= ControlState.Selected;
                        var result = popup.Item(new PopupItemSpec(
                            $"option{i}",
                            option.Label,
                        option.Icon,
                        spec.Variant,
                            state,
                            option.Tooltip,
                            GameIconId: option.GameIconId));
                        if (result.Clicked)
                        {
                            nextSelected = i;
                            clickedIndex = i;
                            ImGui.CloseCurrentPopup();
                        }

                        if (i == popupSelectedIndex)
                            ImGui.SetItemDefaultFocus();
                    }
                }
            }

            var triggerResult = ix.ToControlResult(itemMin, itemMax);
            return new DropdownResult(
                nextSelected,
                clickedIndex,
                nextSelected != popupSelectedIndex,
                triggerResult
            );
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static Vector2 ResolveButtonSize(DropdownSpec spec, DropdownOptionSpec selectedOption)
    {
        var w = MetricsScope.UnitWidth;
        var h = MetricsScope.UnitHeight;
        var width = spec.Size.Kind switch
        {
            ButtonSizeKind.Rect => w * spec.Size.WidthUnits,
            ButtonSizeKind.Square => h * spec.Size.WidthUnits,
            _ => ResolveNaturalWidth(spec, selectedOption)
        };
        var height = spec.Size.Kind switch
        {
            ButtonSizeKind.Rect => h * spec.Size.HeightUnits,
            ButtonSizeKind.Square => h * spec.Size.HeightUnits,
            _ => h
        };
        return new Vector2(MathF.Max(h, width), MathF.Max(h, height));
    }

    private static float ResolveNaturalWidth(DropdownSpec spec, DropdownOptionSpec selectedOption)
    {
        var h = MetricsScope.UnitHeight;
        var iconSlotWidth = MetricsScope.ResolveIconSlotWidth(false);

        var triggerGameIconId = spec.TriggerGameIconId;
        var (triggerText, _) = triggerGameIconId != 0
            ? (null, (ImFontPtr?)null)
            : SlapIcon.Resolve(spec.TriggerIcon);
        if (triggerGameIconId == 0 && string.IsNullOrEmpty(triggerText))
        {
            triggerGameIconId = selectedOption.GameIconId;
            if (triggerGameIconId == 0)
                (triggerText, _) = SlapIcon.Resolve(selectedOption.Icon);
        }

        var hasGameIcon = triggerGameIconId != 0;
        var hasFaIcon = !string.IsNullOrEmpty(triggerText);
        var hasIcon = hasGameIcon || hasFaIcon;
        var hasLabel = !string.IsNullOrEmpty(selectedOption.Label);

        var leading = IconTextComponent.ResolveLeadingLayout(
            hasGameIcon,
            hasFaIcon,
            hasLabel,
            SlapPx.Space4,
            0f,
            iconSlotWidth,
            false,
            hasLabel || hasIcon);

        var contentWidth = hasIcon
            ? leading.TotalWidth
            : MetricsScope.ScalePadding(SlapPx.Space16);

        if (hasLabel)
        {
            using (Slap.PushFont(SlapFontSize.Regular, SlapFontWeight.Bold))
                contentWidth += ImGui.CalcTextSize(selectedOption.Label).X;
        }

        contentWidth += MetricsScope.ScalePadding(SlapPx.Space16);
        contentWidth += iconSlotWidth;

        return MathF.Max(h, contentWidth);
    }

/// <summary>
    /// Measure the preferred host width and structural minimum for this Dropdown.
    /// <see cref="ButtonSize.Auto"/> measures the selected trigger content, while
    /// explicit sizes keep their unit-based preferred baseline. Popup items are
    /// measured independently when the popup opens.
/// <para>
/// When a trigger icon is present, the minimum form is the 2-icon width
/// (trigger icon slot + arrow slot),
/// never a single icon. This matches V2's <c>CalcIconTextDropdownIconOnlyWidth</c>
/// which returns <c>rowHeight * 2</c> when a trigger icon is present.
/// Label clip/fade is handled purely in <see cref="DrawTrigger"/> via
/// <see cref="IconTextComponent.DrawInRect"/>: as width shrinks, the label
/// area narrows and the right-edge fade blends
/// the text into the background. At the minimum width the label area
/// is zero — the trigger icon fills its slot and the arrow fills its slot,
/// with no fade artifacts.
/// </para>
/// <para>
/// Without a trigger icon, the minimum width is <c>UnitHeight</c>, leaving
/// only the arrow visible.
/// </para>
/// </summary>
    public static ResponsiveWidthRange MeasureResponsiveWidth(DropdownSpec spec)
    {
        var h = MetricsScope.UnitHeight;
        var iconSlot = MetricsScope.ResolveIconSlotWidth(false);
        var hasTriggerGameIcon = spec.TriggerGameIconId != 0
            || (spec.Options.Count > 0 && spec.Options[0].GameIconId != 0);
        var hasTriggerIcon = hasTriggerGameIcon
            || spec.TriggerIcon.HasValue
            || (spec.Options.Count > 0 && spec.Options[0].Icon.HasValue);

        var minimumWidth = hasTriggerIcon
            ? MathF.Max(h, iconSlot * 2f)
            : h;

        var contentWidth = 0f;
        if (spec.Options.Count > 0 && spec.Size.Kind == ButtonSizeKind.Auto)
        {
            var selectedIndex = Math.Clamp(spec.SelectedIndex, 0, spec.Options.Count - 1);
            contentWidth = ResolveNaturalWidth(spec, spec.Options[selectedIndex]);
        }

        var preferredBaseline = spec.Size.Kind switch
        {
            ButtonSizeKind.Rect => MetricsScope.UnitWidth * spec.Size.WidthUnits,
            ButtonSizeKind.Square => h * spec.Size.WidthUnits,
            _ => 0f,
        };

        return ResponsiveWidthRange.Create(contentWidth, preferredBaseline, minimumWidth);
    }

private static bool DrawTrigger(DropdownSpec spec, DropdownOptionSpec selectedOption, Vector2 min, Vector2 max, bool hovered, bool active, bool disabled)
    {
        var drawList = ImGui.GetWindowDrawList();
        var rt = ThemeScope.Resolved;
        var palette = rt.GetButtonPalette(spec.Variant);
        var colors = SurfaceComponent.ResolveControlColors(
            palette,
            disabled ? ControlState.Disabled : ControlState.None,
            hovered: hovered && !disabled,
            active: active && !disabled,
            strength: SurfaceComponent.ResolveBorderStrength(spec.Variant),
            variant: spec.Variant);
        var textColor = colors.Text;
        var bg = colors.Background;
        var border = colors.Border;
        var rounding = SlapCorners.ControlRadius;
        LayeredShadow.Draw(
            drawList,
            min,
            max,
            rounding,
            LayeredShadow.ResolveControlDefault(spec.Shadow, spec.Variant) with { ExpandHorizontalClip = true });
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(bg), rounding);
        SurfaceComponent.DrawControlBorder(drawList, min, max, spec.Variant, border, rounding);

        // Hover outline (matches ButtonComponent pattern)
        var outline = new SlapOutlineGroup();
        if (hovered && !disabled)
            outline.Capture(
                min,
                max,
                true,
                selected: false,
                accent: rt.ResolveOutlineAccent(spec.Variant),
                rounding: rounding);

        var iconSlotWidth = MetricsScope.ResolveIconSlotWidth(false);
        var arrowSlotWidth = iconSlotWidth;
        var (arrowIconText, arrowIconFont) = SlapIcon.Resolve(spec.ArrowIcon);
        var arrowText = arrowIconText ?? "v";
        var arrowFont = arrowIconFont;

        // Trigger icon + clipped/faded label + arrow.
        // The trigger icon stays in its slot (no progressiveCenter). As width
        // shrinks, the label clips and fades to zero, leaving the trigger icon
        // + arrow as the final 2-icon state — no icon fade at this width
        // because DrawInRect clips the icon against the full rect (max.X),
        // not the padded contentMax, so the right padding never eats into
        // the icon's reserved slot.
        // Game icon and FA icon are mutually exclusive (game icon wins).
        var triggerGameIconId = spec.TriggerGameIconId;
        var (triggerText, triggerFont) = triggerGameIconId != 0
            ? (null, (ImFontPtr?)null)
            : SlapIcon.Resolve(spec.TriggerIcon);
        // If no explicit trigger icon on spec, fall back to selected option.
        if (triggerGameIconId == 0 && string.IsNullOrEmpty(triggerText))
        {
            triggerGameIconId = selectedOption.GameIconId;
            if (triggerGameIconId == 0)
                (triggerText, triggerFont) = SlapIcon.Resolve(selectedOption.Icon);
        }
        var contentMax = new Vector2(max.X - arrowSlotWidth, max.Y);
        var hasTriggerIcon = triggerGameIconId != 0 || !string.IsNullOrEmpty(triggerText);
        var degraded = false;
        using (Slap.PushFont(SlapFontSize.Regular, SlapFontWeight.Bold))
        {
            degraded = IconTextComponent.DrawInRect(
                drawList,
                min,
                contentMax,
                selectedOption.Label,
                triggerText,
                triggerFont,
                textColor,
                rightFadeBackground: bg,
                rightFadeVerticalBorderInset: MetricsScope.BorderThickness,
                iconTextGap: 0f,
                horizontalPadding: SlapPx.Space16,
                leftPadding: hasTriggerIcon ? SlapPx.Space4 : null,
                iconSlotWidth: iconSlotWidth,
                gameIconId: triggerGameIconId,
                gameIconVisualMode: SlapInteraction.ResolveVisualMode(
                    hovered,
                    active,
                    disabled));
        }

        DrawDropdownArrow(drawList, new Vector2(max.X - arrowSlotWidth, min.Y), max, arrowText, arrowFont, textColor);
        outline.Flush(spec.Key, 1f);
        return degraded;
    }

    private static void DrawDropdownArrow(ImDrawListPtr drawList, Vector2 min, Vector2 max, string arrowText, ImFontPtr? arrowFont, Vector4 color)
    {
        var colorU32 = ImGui.ColorConvertFloat4ToU32(color);
        var height = max.Y - min.Y;

        if (arrowFont.HasValue)
            ImGui.PushFont(arrowFont.Value);
        try
        {
            var arrowSize = ImGui.CalcTextSize(arrowText);
            var pos = new Vector2(
                min.X + MathF.Max(0f, ((max.X - min.X) - arrowSize.X) * 0.5f),
                min.Y + MathF.Max(0f, (height - arrowSize.Y) * 0.5f));
            drawList.AddText(pos, colorU32, arrowText);
        }
        finally
        {
            if (arrowFont.HasValue)
                ImGui.PopFont();
        }
    }

}
