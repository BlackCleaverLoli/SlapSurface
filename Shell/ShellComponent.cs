using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

// ── ShellComponent ───────────────────────────────────────

internal static class ShellComponent
{
    internal const float SidebarSurfacePaddingX = SlapPx.Space6;
    internal const float SidebarSurfacePaddingY = SlapPx.Space12;
    internal const float SidebarItemGap = SlapPx.Space4;
    private const float TextPaddingX = SlapPx.Space16;
    private const float IconLeftPad = SlapPx.Space4;
    private const float IconGap = 0f;
    private const float SidebarSeparatorGap = SlapPx.Space8;
    private const float SidebarSeparatorAlpha = 0.30f;
    private const float SidebarIconBarPadding = SlapPx.Space4;
    private const float SidebarIconBarGap = SlapPx.Space4;

    internal static float MeasureSidebarWidthUnits(IReadOnlyList<string> labels) =>
        MeasureSidebarWidthUnitsCore(labels, identity: null, pushFont: true);

    internal static float MeasureSidebarWidthUnits(
        IReadOnlyList<string> labels,
        SidebarIdentityTabSpec? identityTab
    ) => MeasureSidebarWidthUnitsCore(labels, identityTab, pushFont: true);

    internal static float MeasureSidebarWidthUnits(
        IReadOnlyList<string> labels,
        SlapTheme theme,
        SlapMetricsConfig metrics,
        SlapTypographySpec typography
    )
    {
        using var themeScope = ThemeScope.Push(theme);
        using var metricsScope = MetricsScope.Push(metrics);
        using var typographyScope = TypographyScope.Push(
            typography,
            SlapFontSize.Regular,
            SlapFontWeight.Bold
        );
        return MeasureSidebarWidthUnitsCore(labels, identity: null, pushFont: false);
    }

    private static float MeasureSidebarWidthUnitsCore(
        IReadOnlyList<string> labels,
        SidebarIdentityTabSpec? identity,
        bool pushFont
    )
    {
        var unit = MetricsScope.UnitHeight;
        if (unit <= 0f)
            return 4f;

        return MeasureSidebarWidthPixels(labels, identity, pushFont) / unit;
    }

    private static float MeasureSidebarWidthPixels(
        IReadOnlyList<string> labels,
        SidebarIdentityTabSpec? identity,
        bool pushFont
    )
    {
        if (labels.Count <= 0 && !identity.HasValue)
            return MetricsScope.UnitHeight + MetricsScope.ScalePadding(SidebarSurfacePaddingX) * 2f;

        IDisposable? itemFont = null;
        if (pushFont)
            itemFont = TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold);

        try
        {
            var contentWidth = MetricsScope.UnitHeight;
            foreach (var label in labels)
                contentWidth = MathF.Max(contentWidth, MeasureSidebarTabButtonWidth(label));
            if (identity.HasValue)
                contentWidth = MathF.Max(
                    contentWidth,
                    SidebarIdentityTabComponent.MeasurePreferredWidth(identity.Value)
                );

            return contentWidth + MetricsScope.ScalePadding(SidebarSurfacePaddingX) * 2f;
        }
        finally
        {
            itemFont?.Dispose();
        }
    }

    private static float MeasureSidebarTabButtonWidth(string label)
    {
        var height = MetricsScope.UnitHeight;
        var textWidth = ImGui.CalcTextSize(label).X;
        return MetricsScope.ScalePadding(IconLeftPad)
            + height
            + MetricsScope.ScaleGap(IconGap)
            + textWidth
            + MetricsScope.ScalePadding(TextPaddingX);
    }

    public static ShellScope Begin(ShellSpec spec) => BeginCore(spec, default);

    internal static ShellScope Begin(ShellSpec spec, SlapSidebarLayout layout) =>
        BeginCore(spec, layout);

    private static ShellScope BeginCore(ShellSpec spec, SlapSidebarLayout layout)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("ShellSpec.Key.Value must not be empty.", nameof(spec));

        var avail = ImGui.GetContentRegionAvail();
        var unit = MetricsScope.UnitHeight;

        var sidebarPx = unit * ClampSidebarWidth(spec.SidebarWidth);
        var gapPx = sidebarPx > 0f ? unit * ClampGap(spec.Gap) : 0f;
        var heightPx = spec.Size.Kind == ShellSizeKind.FillAvailable ? avail.Y : unit;
        var contentPx = Math.Max(0f, avail.X - sidebarPx - gapPx);
        var shellStart = ImGui.GetCursorScreenPos();

        ImGui.PushID(spec.Key.Value);

        var bounds = new ShellBounds(
            sbMin: shellStart,
            sbMax: shellStart + new Vector2(sidebarPx, heightPx),
            ctMin: shellStart + new Vector2(sidebarPx + gapPx, 0f),
            ctMax: shellStart + new Vector2(sidebarPx + gapPx + contentPx, heightPx)
        );

        return new ShellScope(
            spec.Key.Value,
            sidebarPx,
            gapPx,
            contentPx,
            heightPx,
            spec.SurfaceBg,
            bounds,
            spec.Shadow,
            layout.Collapsed,
            layout.CanExpand
        );
    }

    private static float ClampSidebarWidth(float value) =>
        value > 0f && !float.IsInfinity(value) ? value : 0f;

    private static float ClampGap(float value) =>
        value >= 0f && !float.IsInfinity(value) ? value : 0f;

    // ── Sidebar item drawing ──────────────────────────

    internal static ControlResult DrawSidebarItem(SidebarItemSpec spec)
    {
        return DrawSidebarItemInternal(spec);
    }

    internal static ControlResult DrawSidebarItemInternal(SidebarItemSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "SidebarItemSpec.Key.Value must not be empty.",
                nameof(spec)
            );

        ImGui.PushID(spec.Key.Value);
        try
        {
            using var itemFont = TypographyScope.PushCurrent(
                SlapFontSize.Regular,
                SlapFontWeight.Bold
            );
            var availWidth = ImGui.GetContentRegionAvail().X;
            var height = MetricsScope.UnitHeight;
            var size = new Vector2(MathF.Max(height, availWidth), height);
            var itemMin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var rt = ThemeScope.Resolved;
            var rounding = SlapCorners.ControlRadius;

            ImGui.SetCursorScreenPos(itemMin);
            var clicked = ImGui.InvisibleButton("##item", size);
            var actualMin = ImGui.GetItemRectMin();
            var actualMax = ImGui.GetItemRectMax();
            var interaction = SlapInteraction.Capture(
                spec.Disabled,
                spec.Selected,
                clicked,
                captureRightClick: true
            );
            var hovered = interaction.Hovered;
            var active = interaction.Active;
            var background = ResolveSidebarTabBackground(
                spec.Selected,
                hovered && !spec.Disabled,
                active,
                spec.Disabled,
                spec.Variant,
                rt
            );

            drawList.AddRectFilled(
                actualMin,
                actualMax,
                ImGui.ColorConvertFloat4ToU32(background),
                rounding,
                ImDrawFlags.RoundCornersAll
            );

            if (spec.Selected && rt.IsTransparentTheme)
            {
                SurfaceComponent.DrawControlBorder(
                    drawList,
                    actualMin,
                    actualMax,
                    spec.Variant,
                    SlapColor.WithDisabledAlpha(rt.Border, spec.Disabled),
                    rounding);
            }

            HoverArbitration.Current.CaptureOutline(
                actualMin,
                actualMax,
                hovered && !spec.Selected && !spec.Disabled,
                selected: false,
                accent: rt.ResolveOutlineAccent(spec.Variant),
                rounding: rounding
            );

            var itemContent = DrawSidebarItemContent(
                spec,
                drawList,
                actualMin,
                actualMax,
                size,
                background,
                rt,
                interaction.VisualMode
            );
            HoverArbitration.Current.TryShowTooltip(
                hovered,
                ResolveSidebarItemTooltip(spec, itemContent.IsIconOnly, itemContent.Degraded),
                preferredDirection: SlapTooltipDirection.Right);

            ImGui.SetCursorScreenPos(
                itemMin + new Vector2(0f, size.Y + MetricsScope.ScaleGap(SidebarItemGap))
            );

            return interaction.ToControlResult(actualMin, actualMax);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static (bool IsIconOnly, bool Degraded) DrawSidebarItemContent(
        SidebarItemSpec spec,
        ImDrawListPtr drawList,
        Vector2 itemMin,
        Vector2 itemMax,
        Vector2 size,
        Vector4 background,
        ResolvedTheme rt,
        ControlVisualMode visualMode
    )
    {
        var textColor = ResolveSidebarTabTextColor(spec, rt);
        var hasGameIcon = spec.GameIconId != 0;
        var (sidebarIconText, sidebarIconFont) = SlapIcon.Resolve(spec.Icon);

        var hasIcon = hasGameIcon || spec.Icon.HasValue;
        // Detect visual collapse: text area would be ≤ 0 when the item
        // rect is too narrow to fit slot + gap + right padding.
        var textAvailableWidth = (itemMax.X - itemMin.X) - MetricsScope.UnitHeight
            - MetricsScope.ScaleGap(IconGap)
            - MetricsScope.ScalePadding(TextPaddingX);
        var hasVisualLabel = !string.IsNullOrEmpty(spec.Label) && textAvailableWidth > 0f;
        var isIconOnly = hasIcon && !hasVisualLabel;

        var degraded = IconTextComponent.DrawInRect(
            drawList,
            itemMin,
            itemMax,
            isIconOnly ? string.Empty : spec.Label,
            sidebarIconText,
            sidebarIconFont,
            textColor,
            rightFadeBackground: background,
            rightFadeVerticalBorderInset: MetricsScope.BorderThickness,
            iconTextGap: IconGap,
            horizontalPadding: isIconOnly ? 0f : TextPaddingX,
            leftPadding: isIconOnly ? null : (hasIcon ? IconLeftPad : null),
            centerContent: false,
            gameIconId: spec.GameIconId,
            gameIconVisualMode: visualMode
        );
        return (isIconOnly, degraded);
    }

    private static string? ResolveSidebarItemTooltip(
        SidebarItemSpec spec,
        bool isIconOnly,
        bool degraded)
    {
        if (!string.IsNullOrEmpty(spec.Tooltip))
            return spec.Tooltip;
        if (spec.AutoLabelTooltip && (isIconOnly || degraded))
            return spec.Label;
        return null;
    }

    private static Vector4 ResolveSidebarTabBackground(
        bool selected,
        bool hovered,
        bool active,
        bool disabled,
        Variant variant,
        ResolvedTheme rt
    )
    {
        if (selected)
        {
            var selectedPalette = rt.GetButtonPalette(variant);
            // 当前 tab 故意不响应 hover 变色，保持 base 色不动（Design Intent）
            return disabled ? selectedPalette.Disabled
                : active ? selectedPalette.Active
                : selectedPalette.Base;
        }

        return SurfaceComponent.ResolveFlatSurfaceBackground(hovered, active, disabled);
    }

    private static Vector4 ResolveSidebarTabTextColor(SidebarItemSpec spec, ResolvedTheme rt)
    {
        var normalColor = spec.Selected ? rt.GetButtonPalette(spec.Variant).Text : rt.Subtle;
        return SlapColor.WithDisabledAlpha(normalColor, spec.Disabled);
    }

    internal static void DrawSidebarSeparator(SidebarSeparatorSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "SidebarSeparatorSpec.Key.Value must not be empty.",
                nameof(spec)
            );

        var gap = MetricsScope.ScaleGap(
            spec.Gap > 0f && !float.IsInfinity(spec.Gap) ? spec.Gap : SidebarSeparatorGap
        );
        ImGui.Dummy(new Vector2(0f, gap));

        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var color = SlapColor.WithAlpha(ThemeScope.Resolved.Border, SidebarSeparatorAlpha);
        drawList.AddLine(
            pos,
            new Vector2(pos.X + width, pos.Y),
            ImGui.ColorConvertFloat4ToU32(color),
            MetricsScope.BorderThickness
        );
        ImGui.Dummy(new Vector2(0f, gap));
    }

    internal static SidebarScrollPanelScope BeginSidebarScrollPanel(
        SidebarScrollPanelSpec spec,
        SidebarIconBarSpec iconBarSpec
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "SidebarScrollPanelSpec.Key.Value must not be empty.",
                nameof(spec)
            );

        var available = ImGui.GetContentRegionAvail();
        var iconBarHeight = ResolveSidebarIconBarLayout(available.X, iconBarSpec).Height;
        var panelSize = new Vector2(
            MathF.Max(0f, available.X),
            MathF.Max(0f, available.Y - iconBarHeight)
        );

        ImGui.PushID(spec.Key.Value);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild(
            "##scrollPanel",
            panelSize,
            false,
            ImGuiWindowFlags.NoBackground
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoSavedSettings
        );

        var min = ImGui.GetWindowPos();
        return new SidebarScrollPanelScope(
            min,
            min + ImGui.GetWindowSize(),
            HoverArbitration.Push()
        );
    }

    internal static SidebarIconBarScope BeginSidebarIconBar(
        SidebarIconBarSpec spec,
        bool collapsed,
        bool canExpand
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "SidebarIconBarSpec.Key.Value must not be empty.",
                nameof(spec)
            );

        var current = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail();
        var width = MathF.Max(0f, available.X);
        var layout = ResolveSidebarIconBarLayout(width, spec);
        var padding = layout.Padding;
        var height = layout.Height;
        var min = new Vector2(current.X, MathF.Max(current.Y, current.Y + available.Y - height));
        var max = min + new Vector2(width, height);

        ImGui.SetCursorScreenPos(min + padding);
        return new SidebarIconBarScope(
            spec.Key.Value,
            min,
            max,
            padding,
            MetricsScope.ScaleGap(
                spec.Gap >= 0f && !float.IsInfinity(spec.Gap)
                    ? spec.Gap
                    : SidebarIconBarGap
            ),
            collapsed,
            canExpand
        );
    }

    private static (Vector2 Padding, float Height) ResolveSidebarIconBarLayout(
        float width,
        SidebarIconBarSpec spec
    )
    {
        var unit = MetricsScope.UnitHeight;
        var requestedPadding = spec.Padding == default
            ? new Vector2(SidebarIconBarPadding)
            : Vector2.Max(Vector2.Zero, spec.Padding);
        var padding = MetricsScope.ScalePadding(requestedPadding);
        padding.X = MathF.Min(padding.X, MathF.Max(0f, (width - unit) * 0.5f));
        return (padding, unit + padding.Y * 2f);
    }

    internal static void DrawSidebarNav<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, string> labelSelector,
        Func<T, bool> selectedSelector,
        Action<T>? onClick,
        Func<T, FontAwesomeIcon?>? iconSelector,
        Func<T, string?>? tooltipSelector
    )
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var selected = selectedSelector(item);
            if (
                DrawSidebarItemInternal(
                    new SidebarItemSpec(
                        keySelector(item),
                        labelSelector(item),
                        selected,
                        Variant: selected ? Variant.Sidebar : Variant.Base,
                        Icon: iconSelector?.Invoke(item),
                        Tooltip: tooltipSelector?.Invoke(item)
                    )
                )
            )
            {
                onClick?.Invoke(item);
            }
        }
    }
}

// ── ShellScope ───────────────────────────────────────────

internal sealed class ShellScope : IDisposable
{
    private readonly string _key;
    private readonly float _sidebarPx;
    private readonly float _gapPx;
    private readonly float _contentPx;
    private readonly float _heightPx;
    private readonly bool _sidebarSurfaceBg;
    private readonly bool _collapsed;
    private readonly bool _canExpand;
    private readonly uint _surfaceBgU32;
    private readonly Vector4 _surfaceBgColor;
    private readonly LayeredShadowSpec _shadow;
    private int _flags; // bit 0: sidebarBegun, 1: contentBegun, 2: disposed

    /// <summary>Read-only screen-space bounds for sidebar and content slots.</summary>
    public ShellBounds Bounds { get; }

    internal ShellScope(
        string key,
        float sidebarPx,
        float gapPx,
        float contentPx,
        float heightPx,
        bool sidebarSurfaceBg,
        ShellBounds bounds,
        LayeredShadowSpec shadow,
        bool collapsed,
        bool canExpand
    )
    {
        _key = key;
        _sidebarPx = sidebarPx;
        _gapPx = gapPx;
        _contentPx = contentPx;
        _heightPx = heightPx;
        _sidebarSurfaceBg = sidebarSurfaceBg;
        _collapsed = collapsed;
        _canExpand = canExpand;
        _surfaceBgColor = ThemeScope.Resolved.IsTransparentTheme
            ? Vector4.Zero
            : ThemeScope.Resolved.Surface;
        _surfaceBgU32 = ImGui.ColorConvertFloat4ToU32(_surfaceBgColor);
        Bounds = bounds;
        _shadow = shadow;
        _flags = 0;
    }

    /// <summary>Begin the sidebar slot. Call at most once; must be called before <see cref="BeginContent"/>.</summary>
    public SidebarScope BeginSidebar()
    {
        EnsureNotDisposed();
        if ((_flags & 1) != 0)
            throw new InvalidOperationException("BeginSidebar already called.");
        _flags |= 1;

        if (_sidebarPx <= 0f)
        {
            ImGui.BeginChild(
                $"{_key}_sb",
                Vector2.Zero,
                false,
                ImGuiWindowFlags.NoBackground);
            return new SidebarScope(
                styleVarCount: 0,
                styleColorCount: 0,
                collapsed: _collapsed,
                canExpand: _canExpand);
        }

        var childRounding = SlapCorners.ControlRadius;
        var parentDrawList = ImGui.GetWindowDrawList();
        var cardMin = ImGui.GetCursorScreenPos();
        var cardMax = cardMin + new Vector2(_sidebarPx, _heightPx);

        if (_sidebarSurfaceBg)
        {
            LayeredShadow.Draw(
                parentDrawList,
                cardMin,
                cardMax,
                childRounding,
                _shadow);
            parentDrawList.AddRectFilled(
                cardMin,
                cardMax,
                _surfaceBgU32,
                childRounding,
                ImDrawFlags.RoundCornersAll
            );
        }

        var padding = MetricsScope.ScalePadding(
            new Vector2(
                ShellComponent.SidebarSurfacePaddingX,
                ShellComponent.SidebarSurfacePaddingY
            )
        );
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        ImGui.BeginChild(
            $"{_key}_sb",
            new Vector2(_sidebarPx, _heightPx),
            false,
            ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse
                | ImGuiWindowFlags.AlwaysUseWindowPadding
                | ImGuiWindowFlags.NoBackground
        );

        return new SidebarScope(
            styleVarCount: 1,
            styleColorCount: 0,
            collapsed: _collapsed,
            canExpand: _canExpand
        );
    }

    /// <summary>Begin the content slot. Call at most once; must be called after <see cref="BeginSidebar"/>.</summary>
    public ContentScope BeginContent()
    {
        EnsureNotDisposed();
        if ((_flags & 1) == 0)
            throw new InvalidOperationException("BeginSidebar must be called before BeginContent.");
        if ((_flags & 2) != 0)
            throw new InvalidOperationException("BeginContent already called.");
        _flags |= 2;

        ImGui.SetCursorScreenPos(Bounds.ContentMin);

        ImGui.BeginChild(
            $"{_key}_ct",
            new Vector2(_contentPx, _heightPx),
            false,
            ImGuiWindowFlags.NoBackground
        );

        return new ContentScope();
    }

    private void EnsureNotDisposed()
    {
        if ((_flags & 4) != 0)
            throw new ObjectDisposedException(nameof(ShellScope));
    }

    public void Dispose()
    {
        if ((_flags & 4) != 0)
            return;

        _flags |= 4;
        // Reset cursor below the shell area (mirrors V2 DrawMainShell epilogue)
        var afterY = Bounds.SidebarMin.Y + _heightPx;
        var cur = ImGui.GetCursorScreenPos();
        if (cur.Y < afterY)
            ImGui.SetCursorScreenPos(new Vector2(cur.X, afterY));
        ImGui.PopID();
    }
}

// ── Slot scopes ──────────────────────────────────────────

internal sealed class SidebarScope : IDisposable
{
    private bool _disposed;
    private bool _iconBarDrawn;
    private readonly IDisposable _hoverScope;
    private readonly bool _collapsed;
    private readonly bool _canExpand;
    private readonly bool _isTemporaryOverlay;

    private readonly int _styleVarCount;
    private readonly int _styleColorCount;

    /// <summary>Effective collapse state of the sidebar for the current frame.</summary>
    public bool SidebarCollapsed => _collapsed;

    /// <summary>True when this sidebar is rendered as a temporary overlay.</summary>
    public bool IsTemporaryOverlay => _isTemporaryOverlay;

    internal SidebarScope(
        int styleVarCount = 0,
        int styleColorCount = 0,
        bool collapsed = false,
        bool canExpand = true,
        bool isTemporaryOverlay = false
    )
    {
        _disposed = false;
        _hoverScope = HoverArbitration.Push();
        _styleVarCount = styleVarCount;
        _styleColorCount = styleColorCount;
        _collapsed = collapsed;
        _canExpand = canExpand;
        _isTemporaryOverlay = isTemporaryOverlay;
    }

    /// <summary>
    /// Draw a sidebar item and return its interaction state. Hover, tooltip and
    /// outline arbitration are handled automatically via the ambient hover scope.
    /// </summary>
    public ControlResult SidebarItem(SidebarItemSpec spec)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SidebarScope));

        return ShellComponent.DrawSidebarItemInternal(spec);
    }

    /// <summary>
    /// Draw an interactive texture and two-line identity tab and return its interaction state.
    /// </summary>
    public ControlResult IdentityTab(SidebarIdentityTabSpec spec)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SidebarScope));

        return SidebarIdentityTabComponent.Draw(spec);
    }

    /// <summary>
    /// Draw a visual separator in the sidebar list.
    /// </summary>
    public void Separator(string key = "sep", float gap = 8f)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SidebarScope));

        ShellComponent.DrawSidebarSeparator(new SidebarSeparatorSpec(key, gap));
    }

    /// <summary>
    /// Batch-draw navigation items from a list, invoking <paramref name="onClick"/>
    /// when any item is clicked.
    /// </summary>
    /// <typeparam name="T">The page/item descriptor type owned by the caller.</typeparam>
    /// <param name="items">The items to render as sidebar tabs.</param>
    /// <param name="keySelector">Unique key for each item (used for ImGui ID).</param>
    /// <param name="labelSelector">Display label for each item.</param>
    /// <param name="selectedSelector">Whether this item is the currently selected page.</param>
    /// <param name="onClick">Callback invoked with the clicked item (may be null).</param>
    /// <param name="iconSelector">Optional icon for each item.</param>
    /// <param name="tooltipSelector">Optional tooltip text shown on hover.</param>
    public void Nav<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, string> labelSelector,
        Func<T, bool> selectedSelector,
        Action<T>? onClick = null,
        Func<T, FontAwesomeIcon?>? iconSelector = null,
        Func<T, string?>? tooltipSelector = null
    )
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SidebarScope));

        ShellComponent.DrawSidebarNav(
            items,
            keySelector,
            labelSelector,
            selectedSelector,
            onClick,
            iconSelector,
            tooltipSelector
        );
    }

    /// <summary>
    /// Draw a zero-padding scrollable navigation region followed by a fixed
    /// bottom icon bar. The panel hides its scrollbar and draws edge fades when
    /// more navigation content is available in either direction.
    /// </summary>
    public void ScrollPanel(
        SidebarScrollPanelSpec spec,
        SidebarIconBarSpec iconBarSpec,
        Action<SidebarScrollPanelScope> drawContent,
        Action<SidebarIconBarScope> drawIconBar
    )
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SidebarScope));
        if (_iconBarDrawn)
            throw new InvalidOperationException("Sidebar icon bar already drawn.");

        using (var panel = ShellComponent.BeginSidebarScrollPanel(spec, iconBarSpec))
            drawContent(panel);

        IconBar(iconBarSpec, drawIconBar);
    }

    /// <summary>
    /// Draw a bottom-aligned icon-only command bar. Call at most once and after
    /// the normal sidebar navigation flow.
    /// </summary>
    public void IconBar(SidebarIconBarSpec spec, Action<SidebarIconBarScope> draw)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SidebarScope));
        if (_iconBarDrawn)
            throw new InvalidOperationException("Sidebar icon bar already drawn.");

        _iconBarDrawn = true;
        using var iconBar = ShellComponent.BeginSidebarIconBar(
            spec,
            _collapsed,
            _canExpand
        );
        draw(iconBar);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        HoverArbitration.Current.FlushOutline("sidebarOutline", 1f);
        _hoverScope.Dispose();
        ImGui.EndChild();
        if (_styleColorCount > 0)
            ImGui.PopStyleColor(_styleColorCount);
        if (_styleVarCount > 0)
            ImGui.PopStyleVar(_styleVarCount);
    }
}

/// <summary>
/// Navigation content inside a zero-padding sidebar scroll region.
/// </summary>
internal sealed class SidebarScrollPanelScope : IDisposable
{
    private readonly Vector2 _min;
    private readonly Vector2 _max;
    private readonly IDisposable _hoverScope;
    private bool _disposed;

    internal SidebarScrollPanelScope(Vector2 min, Vector2 max, IDisposable hoverScope)
    {
        _min = min;
        _max = max;
        _hoverScope = hoverScope;
    }

    public ControlResult SidebarItem(SidebarItemSpec spec)
    {
        EnsureNotDisposed();
        return ShellComponent.DrawSidebarItemInternal(spec);
    }

    public void Separator(string key = "sep", float gap = 8f)
    {
        EnsureNotDisposed();
        ShellComponent.DrawSidebarSeparator(new SidebarSeparatorSpec(key, gap));
    }

    public void Nav<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, string> labelSelector,
        Func<T, bool> selectedSelector,
        Action<T>? onClick = null,
        Func<T, FontAwesomeIcon?>? iconSelector = null,
        Func<T, string?>? tooltipSelector = null
    )
    {
        EnsureNotDisposed();
        ShellComponent.DrawSidebarNav(
            items,
            keySelector,
            labelSelector,
            selectedSelector,
            onClick,
            iconSelector,
            tooltipSelector
        );
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SidebarScrollPanelScope));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var displaySize = ImGui.GetIO().DisplaySize;
        var scrollY = ImGui.GetScrollY();
        var scrollMaxY = ImGui.GetScrollMaxY();
        var edgeEpsilon = MetricsScope.Scale(1f);
        var clipMinY = scrollY <= edgeEpsilon ? 0f : _min.Y;
        var clipMaxY = scrollY >= scrollMaxY - edgeEpsilon ? displaySize.Y : _max.Y;
        HoverArbitration.Current.SetOutlineClipRect(
            new Vector2(0f, clipMinY),
            new Vector2(displaySize.X, clipMaxY)
        );
        HoverArbitration.Current.FlushOutline(
            "sidebarScrollOutline",
            1f
        );
        _hoverScope.Dispose();
        ScrollFade.DrawVertical(_min, _max);
        ImGui.EndChild();
        ImGui.SetCursorScreenPos(new Vector2(_min.X, _max.Y));
        ImGui.PopStyleVar();
        ImGui.PopID();
    }
}

/// <summary>
/// Horizontal icon flow owned by a shell sidebar's bottom command bar.
/// </summary>
internal sealed class SidebarIconBarScope : IDisposable
{
    private readonly Vector2 _min;
    private readonly Vector2 _max;
    private readonly Vector2 _padding;
    private readonly float _gap;
    private readonly bool _collapsed;
    private readonly bool _canExpand;
    private int _itemCount;
    private bool _disposed;

    internal SidebarIconBarScope(
        string key,
        Vector2 min,
        Vector2 max,
        Vector2 padding,
        float gap,
        bool collapsed,
        bool canExpand
    )
    {
        _min = min;
        _max = max;
        _padding = padding;
        _gap = gap;
        _collapsed = collapsed;
        _canExpand = canExpand;
        ImGui.PushID(key);
    }

    /// <summary>Draw the next icon-only command from left to right.</summary>
    public ControlResult IconButton(SidebarIconButtonSpec spec)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SidebarIconBarScope));

        return DrawCommandIconButton(spec);
    }

    /// <summary>
    /// Draw the explicit sidebar collapse/expand command at the next flow position.
    /// The icon, disabled state, and tooltip follow the current sidebar layout;
    /// clicking returns the requested collapse state so the caller can persist it.
    /// </summary>
    public SlapSidebarCollapseResult CollapseToggle(SidebarCollapseToggleSpec spec)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SidebarIconBarScope));

        var temporaryExpand = _collapsed && !_canExpand;
        var result = DrawCommandIconButton(
            new SidebarIconButtonSpec(
                spec.Key,
                _collapsed ? FontAwesomeIcon.AngleRight : FontAwesomeIcon.AngleLeft,
                Tooltip: _collapsed ? spec.ExpandTooltip : spec.CollapseTooltip,
                Disabled: false,
                Variant: spec.Variant
            )
        );
        if (!result.Clicked)
            return default;
        return temporaryExpand
            ? new SlapSidebarCollapseResult(TemporaryExpandRequested: true, Collapsed: _collapsed)
            : new SlapSidebarCollapseResult(Changed: true, Collapsed: !_collapsed);
    }

    private ControlResult DrawCommandIconButton(SidebarIconButtonSpec spec)
    {
        var unit = MetricsScope.UnitHeight;
        var x = _min.X + _padding.X + _itemCount * (unit + _gap);
        if (x + unit > _max.X - _padding.X + 0.5f)
            return default;

        ImGui.SetCursorScreenPos(new Vector2(x, _min.Y + _padding.Y));
        _itemCount++;
        var state = spec.Disabled ? ControlState.Disabled : ControlState.None;

        return Slap.Button(
            new ButtonSpec(
                spec.Key,
                Label: null,
                ButtonSize.Auto,
                spec.Variant,
                state,
                spec.Tooltip,
                spec.Icon
            )
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ImGui.SetCursorScreenPos(new Vector2(_min.X, _max.Y));
        ImGui.PopID();
    }
}

internal sealed class ContentScope : IDisposable
{
    private bool _disposed;
    private bool _bottomCardDrawn;

    internal ContentScope()
    {
        _disposed = false;
    }

    /// <summary>
    /// Draw a main content slot followed by a naturally-sized bottom card.
    /// The shell owns the split, gap, child cleanup, and card bounds.
    /// </summary>
    public ShellBottomCardBounds BottomCard(
        ShellBottomCardSpec spec,
        Action<ShellSlotBounds> drawMain,
        Action<ContentZoneContext> drawCard
    )
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ContentScope));
        if (_bottomCardDrawn)
            throw new InvalidOperationException("BottomCard already called.");

        _bottomCardDrawn = true;

        return ShellBottomCardComponent.Draw(spec, drawMain, drawCard);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ImGui.EndChild();
    }
}
