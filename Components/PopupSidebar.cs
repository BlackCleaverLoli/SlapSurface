using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Specification for a right-anchored popup sidebar.
/// </summary>
internal readonly record struct PopupSidebarSpec(
    ControlKey Key,
    string Title,
    Action<ResponsiveSlotGroup>? TitleRight = null,
    ControlState State = ControlState.None,
    bool ShowOverlay = true,
    LayeredShadowSpec Shadow = default,
    Vector2? Padding = null,
    float OverlayAlpha = 0.33f,
    float WidthUnits = 5f,
    Vector2 HostMin = default,
    Vector2 HostMax = default,
    /// <summary>
    /// Resets the content scroll offset to the top when the sidebar is reopened
    /// after having been closed, including after a plugin reload.
    /// </summary>
    bool ResetScrollOnOpen = true,
    /// <summary>Optional hover tooltip shown over the sidebar title.</summary>
    string? TitleTooltip = null
)
{
    internal bool HasValidHost => HostMax.X > HostMin.X && HostMax.Y > HostMin.Y;
}

/// <summary>Scope for a popup sidebar content region.</summary>
internal sealed class PopupSidebarScope : IDisposable
{
    private bool _disposed;
    private readonly bool _idPushed;
    private readonly bool _contentChildBegun;
    private readonly bool _panelChildBegun;
    private readonly OverlayMaskScope? _overlayMask;
    private readonly Vector2 _contentMin;
    private readonly Vector2 _contentMax;

    public bool IsOpen { get; }
    public bool CloseRequested { get; internal set; }
    public Vector2 Min { get; }
    public Vector2 Max { get; }
    public Vector2 ContentMin => _contentMin;
    public Vector2 ContentMax => _contentMax;

    internal PopupSidebarScope(
        bool open,
        OverlayMaskScope? overlayMask,
        bool panelChildBegun,
        bool contentChildBegun,
        bool idPushed,
        bool closeRequested,
        Vector2 min,
        Vector2 max,
        Vector2 contentMin,
        Vector2 contentMax)
    {
        IsOpen = open;
        _overlayMask = overlayMask;
        _panelChildBegun = panelChildBegun;
        _contentChildBegun = contentChildBegun;
        _idPushed = idPushed;
        CloseRequested = closeRequested;
        Min = min;
        Max = max;
        _contentMin = contentMin;
        _contentMax = contentMax;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_contentChildBegun)
        {
            ScrollFade.DrawVertical(_contentMin, _contentMax, horizontalInset: MetricsScope.BorderThickness);
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
        if (_panelChildBegun)
        {
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
        _overlayMask?.Dispose();
        if (_idPushed)
            ImGui.PopID();
    }
}

internal static class PopupSidebarComponent
{
    private static readonly Vector2 PanelPadding = new(SlapPx.Space12, SlapPx.Space12);
    private const float DetailContentIndentX = SlapPx.Space12;
    private const float HeaderContentGap = SlapPx.Space8;
    private static readonly Dictionary<string, int> LastBeginFrameByKey = new();

    public static PopupSidebarScope Begin(PopupSidebarSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("PopupSidebarSpec.Key.Value must not be empty.", nameof(spec));
        ImGui.PushID(spec.Key.Value);
        var rt = ThemeScope.Resolved;
        var rounding = SlapCorners.ControlRadius;

        var mask = OverlayMaskComponent.Begin(
            new OverlayMaskSpec(spec.Key.Value, spec.ShowOverlay, spec.OverlayAlpha));
        if (!mask.IsOpen)
        {
            mask.Dispose();
            ImGui.PopID();
            return new PopupSidebarScope(false, null, false, false, false, false, default, default, default, default);
        }

        var hostMin = mask.HostMin;
        var hostMax = mask.HostMax;

        float width;
        Vector2 panelMin;
        Vector2 panelMax;

        if (spec.HasValidHost)
        {
            var hostWidth = spec.HostMax.X - spec.HostMin.X;
            width = MathF.Min(hostWidth, MetricsScope.UnitWidth * Normalize(spec.WidthUnits, 5f));
            panelMin = new Vector2(spec.HostMax.X - width, spec.HostMin.Y);
            panelMax = new Vector2(spec.HostMax.X, hostMax.Y);
        }
        else
        {
            var hostWidth = hostMax.X - hostMin.X;
            width = MathF.Min(hostWidth, MetricsScope.UnitWidth * Normalize(spec.WidthUnits, 5f));
            panelMin = new Vector2(hostMax.X - width, hostMin.Y);
            panelMax = hostMax;
        }

        var panelSize = panelMax - panelMin;

        // Panel card: shadow + background + border (drawn on top of mask)
        var overlayDrawList = ImGui.GetWindowDrawList();
        var background = SurfaceComponent.ResolveColors(spec.State);
        LayeredShadow.Draw(
            overlayDrawList,
            panelMin,
            panelMax,
            rounding,
            LayeredShadowSpec.ResolveStandard(spec.Shadow));
        SurfaceComponent.DrawFrostedPanelBackground(overlayDrawList, panelMin, panelMax, rounding);
        overlayDrawList.AddRectFilled(
            panelMin, panelMax,
            ImGui.ColorConvertFloat4ToU32(background),
            rounding, ImDrawFlags.RoundCornersAll);
        if (ThemeScope.Resolved.IsTransparentTheme)
        {
            SurfaceComponent.DrawPanelBorder(
                overlayDrawList,
                panelMin,
                panelMax,
                ThemeScope.Resolved.Border,
                rounding);
        }

        // Invisible button for outside-click close (entire overlay except panel).
        var closeRequested = mask.CaptureOutsideClick(panelMin, panelMax);

        // ── Layer 1: Panel child window ───────────────────────
        // A child with padding that contains the header and content.
        var padding = MetricsScope.ScalePadding(spec.Padding ?? PanelPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, padding);
        ImGui.SetCursorScreenPos(panelMin);
        var panelVisible = ImGui.BeginChild("##popupSidebarPanel", panelSize, false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (!panelVisible)
        {
            ImGui.EndChild(); // panel
            ImGui.PopStyleVar(); // panel: WindowPadding
            mask.Dispose(); // overlay
            ImGui.PopID();
            return new PopupSidebarScope(true, null, false, false, false, closeRequested, panelMin, panelMax, default, default);
        }

        // ── Header ────────────────────────────────────────────
        var headerMax = panelMax - padding;
        var titleRight = new ResponsiveSlotGroup();
        spec.TitleRight?.Invoke(titleRight);
        DrawTitleRow(spec, ImGui.GetCursorScreenPos(), headerMax, titleRight);

        // ── Layer 2: Content child window ─────────────────────
        // Header bottom is determined by the wrapped title.
        var headerEndY = ImGui.GetCursorScreenPos().Y;
        var contentMin = new Vector2(
            panelMin.X + padding.X,
            headerEndY + MetricsScope.ScaleGap(HeaderContentGap));
        var contentMax = panelMax - padding;
        if (contentMax.X <= contentMin.X || contentMax.Y <= contentMin.Y)
        {
            // Content area too small — end panel + overlay and return
            ImGui.EndChild(); // panel
            ImGui.PopStyleVar(); // panel: WindowPadding
            mask.Dispose(); // overlay
            ImGui.PopID();
            return new PopupSidebarScope(true, null, false, false, false, closeRequested, panelMin, panelMax, contentMin, contentMax);
        }

        ImGui.SetCursorScreenPos(contentMin);
        var contentSize = contentMax - contentMin;
        var contentPadding = new Vector2(MetricsScope.Scale(DetailContentIndentX), 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, contentPadding);

        var frame = ImGui.GetFrameCount();
        var resetScroll = spec.ResetScrollOnOpen
            && (!LastBeginFrameByKey.TryGetValue(spec.Key.Value, out var lastFrame)
                || frame != lastFrame + 1);
        LastBeginFrameByKey[spec.Key.Value] = frame;

        ImGui.BeginChild("##popupSidebarContent", contentSize, false,
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysUseWindowPadding);
        if (resetScroll)
            ImGui.SetScrollY(0f);

        return new PopupSidebarScope(
            true, mask, true, true, true,
            closeRequested, panelMin, panelMax, contentMin, contentMax);
    }

    private static void DrawTitleRow(
        PopupSidebarSpec spec,
        Vector2 min,
        Vector2 max,
        ResponsiveSlotGroup titleRight
    )
    {
        var rt = ThemeScope.Resolved;
        var disabled = spec.State.HasFlag(ControlState.Disabled);
        var textColor = SlapColor.WithDisabledAlpha(rt.Body, disabled);
        var titleMinX = min.X + MetricsScope.Scale(DetailContentIndentX);
        var rightEntries = titleRight.Entries;
        var hasRightSlots = rightEntries.Count > 0;
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var titleWidth = MathF.Max(0f, max.X - titleMinX);

        if (hasRightSlots)
        {
            var titleNaturalWidth = 0f;
            if (!string.IsNullOrEmpty(spec.Title))
            {
                using (TypographyScope.PushCurrent(SlapFontSize.Large, SlapFontWeight.Bold))
                    titleNaturalWidth = ImGui.CalcTextSize(spec.Title).X;
            }

            var layout = SlapMeasure.MeasureStripLayout(
                max.X - titleMinX,
                titleNaturalWidth,
                0f,
                gap,
                BuildWidthRanges(rightEntries),
                BuildPriorities(rightEntries),
                gap);
            titleWidth = MathF.Max(0f, layout.LeftWidth);

            ResponsiveSlotLayout.DrawRightStrip(
                rightEntries,
                layout.RightLayouts,
                max.X,
                min.Y,
                gap);
        }

        var wrappedTitleHeight = 0f;
        if (titleWidth > 0f && !string.IsNullOrEmpty(spec.Title))
        {
            using (TypographyScope.PushCurrent(SlapFontSize.Large, SlapFontWeight.Bold))
                wrappedTitleHeight = ImGui.CalcTextSize(spec.Title, false, titleWidth).Y;
        }

        var rowHeight = MathF.Max(MetricsScope.UnitHeight, wrappedTitleHeight);
        using (TypographyScope.PushCurrent(SlapFontSize.Large, SlapFontWeight.Bold))
        {
            if (titleWidth > 0f && !string.IsNullOrEmpty(spec.Title))
            {
                var titleY = min.Y + MathF.Max(0f, (rowHeight - wrappedTitleHeight) * 0.5f);
                ImGui.SetCursorScreenPos(new Vector2(titleMinX, titleY));
                ImGui.PushStyleColor(ImGuiCol.Text, textColor);
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + titleWidth);
                ImGui.TextUnformatted(spec.Title);
                if (spec.TitleTooltip is { } titleTooltip)
                    HoverArbitration.Current.TryShowTooltip(ImGui.IsItemHovered(), titleTooltip);
                ImGui.PopTextWrapPos();
                ImGui.PopStyleColor();
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(min.X, min.Y + rowHeight));
    }

    private static ResponsiveWidthRange[] BuildWidthRanges(
        IReadOnlyList<ResponsiveSlotEntry> entries
    )
    {
        var ranges = new ResponsiveWidthRange[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            ranges[i] = entries[i].WidthRange;
        return ranges;
    }

    private static int[] BuildPriorities(IReadOnlyList<ResponsiveSlotEntry> entries)
    {
        var priorities = new int[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            priorities[i] = entries[i].Priority;
        return priorities;
    }

    private static float Normalize(float value, float fallback) =>
        value > 0f && !float.IsInfinity(value) ? value : fallback;
}
