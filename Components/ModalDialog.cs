using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct ModalDialogSpec(
    ControlKey Key,
    string Title,
    ControlState State = ControlState.None,
    LayeredShadowSpec Shadow = default,
    Vector2? Padding = null,
    float OverlayAlpha = 0.5f,
    /// <summary>Fixed panel width in <see cref="MetricsScope.UnitWidth"/> multiples.</summary>
    float WidthUnits = 6f,
    /// <summary>
    /// Maximum panel height in <see cref="MetricsScope.UnitHeight"/> multiples.
    /// The panel shrinks to fit its content and never exceeds this cap.
    /// </summary>
    float MaxHeightUnits = 7f,
    bool ShowSafeExit = true,
    bool DismissOnMaskClick = false
);

internal readonly record struct ModalDialogResult(bool Dismissed, bool SafeExitRequested);

internal sealed class ModalDialogPanelScope : IDisposable
{
    private bool disposed;
    private readonly bool idPushed;
    private readonly bool panelChildBegun;
    private readonly OverlayMaskScope? overlayMask;

    internal ModalDialogPanelScope(
        bool isOpen,
        bool dismissed,
        bool idPushed,
        bool panelChildBegun,
        OverlayMaskScope? overlayMask,
        float maxPanelHeight
    )
    {
        IsOpen = isOpen;
        Dismissed = dismissed;
        this.idPushed = idPushed;
        this.panelChildBegun = panelChildBegun;
        this.overlayMask = overlayMask;
        MaxPanelHeight = maxPanelHeight;
    }

    public bool IsOpen { get; }
    public bool Dismissed { get; }
    public float MaxPanelHeight { get; }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (panelChildBegun)
        {
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }
        overlayMask?.Dispose();
        if (idPushed)
            ImGui.PopID();
    }
}

internal static class ModalDialogComponent
{
    private static readonly Vector2 PanelPadding = new(SlapPx.Space16, SlapPx.Space16);
    private const float HostMargin = SlapPx.Space12;
    private static readonly Dictionary<string, float> PanelHeights = new();

    internal static ModalDialogResult Draw(
        ModalDialogSpec spec,
        Action drawBody,
        Action<ResponsiveSlotGroup>? collectActions,
        Action? drawScrollableContent
    )
    {
        ArgumentNullException.ThrowIfNull(drawBody);
        if (string.IsNullOrWhiteSpace(spec.Title))
            throw new ArgumentException("ModalDialogSpec.Title must not be empty.", nameof(spec));

        using var modal = BeginPanel(spec);
        if (!modal.IsOpen)
            return new ModalDialogResult(modal.Dismissed, false);

        var padding = MetricsScope.ScalePadding(spec.Padding ?? PanelPadding);
        var contentStartY = ImGui.GetCursorScreenPos().Y;

        var gap = ImGui.GetTextLineHeight();
        var safeExitRequested = DrawHeader(spec, gap);

        var bodyMin = ImGui.GetCursorScreenPos();
        var available = Vector2.Max(Vector2.Zero, ImGui.GetContentRegionAvail());
        var hasActions = collectActions != null;
        var actionHeight = hasActions ? MetricsScope.UnitHeight : 0f;
        var contentBoxHeight = MathF.Max(
            0f,
            available.Y - (hasActions ? gap + actionHeight : 0f));
        var contentBoxNatural = DrawContent(
            available.X,
            contentBoxHeight,
            drawBody,
            drawScrollableContent);

        if (collectActions != null)
        {
            var actionMin = new Vector2(bodyMin.X, bodyMin.Y + contentBoxHeight + gap);
            var actionMax = actionMin + new Vector2(available.X, actionHeight);
            ImGui.SetCursorScreenPos(actionMin);
            new ContentZoneContext(actionMin, actionMax, default)
                .ResponsiveRow(row => collectActions(row.Right));
        }

        var contentEndY = hasActions
            ? bodyMin.Y + contentBoxNatural + gap + actionHeight
            : bodyMin.Y + contentBoxNatural;
        var naturalContentHeight = MathF.Max(0f, contentEndY - contentStartY);
        var naturalPanelHeight = padding.Y * 2f + naturalContentHeight;
        RecordPanelHeight(spec.Key.Value, naturalPanelHeight, modal.MaxPanelHeight);

        return new ModalDialogResult(modal.Dismissed, safeExitRequested);
    }

    private static bool DrawHeader(ModalDialogSpec spec, float gap)
    {
        var lineMin = ImGui.GetCursorScreenPos();
        var lineWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        var closeSize = spec.ShowSafeExit ? MetricsScope.UnitHeight : 0f;
        var closeGap = spec.ShowSafeExit ? Slap.Scale(SlapPx.Space8) : 0f;
        var titleWidth = MathF.Max(0f, lineWidth - closeSize - closeGap);
        var titleLineHeight = ImGui.GetTextLineHeight();
        var headerHeight = titleLineHeight;

        using (Slap.PushFont(SlapFontSize.Regular, SlapFontWeight.Bold))
        {
            titleLineHeight = ImGui.GetTextLineHeight();
            headerHeight = spec.ShowSafeExit ? closeSize : titleLineHeight;
            if (titleWidth > 0f)
            {
                var titleY = lineMin.Y + MathF.Max(
                    0f,
                    (headerHeight - titleLineHeight) * 0.5f);
                ImGui.SetCursorScreenPos(new Vector2(lineMin.X, titleY));
                var title = NormalizeSingleLineTitle(spec.Title);
                ImGui.TextUnformatted(TrimTextToWidth(title, titleWidth));
            }
        }

        var titleBottom = spec.ShowSafeExit
            ? lineMin.Y + headerHeight
            : ImGui.GetCursorScreenPos().Y;

        var safeExitRequested = false;
        if (spec.ShowSafeExit)
        {
            var closeMin = new Vector2(
                lineMin.X + lineWidth - closeSize,
                lineMin.Y);
            ImGui.SetCursorScreenPos(closeMin);
            var closeResult = Slap.Button(
                new ButtonSpec(
                    $"{spec.Key.Value}SafeExit",
                    null,
                    ButtonSize.Square(1f),
                    Variant.FlatNoBorder,
                    Icon: FontAwesomeIcon.Times));
            safeExitRequested = closeResult.Clicked;
            titleBottom = MathF.Max(titleBottom, closeMin.Y + closeSize);
        }

        var dividerY = titleBottom + gap * 0.25f;
        var drawList = ImGui.GetWindowDrawList();
        var dividerColor = SlapColor.WithAlpha(ThemeScope.Resolved.Border, 0.35f);
        drawList.AddLine(
            new Vector2(lineMin.X, dividerY),
            new Vector2(lineMin.X + lineWidth, dividerY),
            ImGui.ColorConvertFloat4ToU32(dividerColor),
            MetricsScope.BorderThickness);

        ImGui.SetCursorScreenPos(new Vector2(lineMin.X, titleBottom + gap));
        return safeExitRequested;
    }

    private static float DrawContent(
        float width,
        float height,
        Action drawBody,
        Action? drawScrollableContent
    )
    {
        if (width <= 0f || height <= 0f)
            return 0f;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var visible = ImGui.BeginChild(
            "##modalContent",
            new Vector2(width, height),
            false,
            ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoBackground
        );
        var contentHeight = 0f;
        try
        {
            if (visible)
            {
                using (Slap.PushFont(SlapFontSize.Regular, SlapFontWeight.Regular))
                {
                    var bodyStartY = ImGui.GetCursorScreenPos().Y;
                    drawBody();
                    var bodyNatural = MathF.Max(
                        0f,
                        ImGui.GetCursorScreenPos().Y - bodyStartY);
                    contentHeight = bodyNatural;

                    if (drawScrollableContent != null)
                    {
                        var remainingHeight = ImGui.GetContentRegionAvail().Y;
                        var gap = bodyNatural > 0f && remainingHeight > 0f
                            ? MathF.Min(ImGui.GetTextLineHeight(), remainingHeight)
                            : 0f;
                        if (gap > 0f)
                        {
                            var cursor = ImGui.GetCursorScreenPos();
                            ImGui.SetCursorScreenPos(new Vector2(cursor.X, cursor.Y + gap));
                        }

                        var scrollNatural = DrawScrollableContent(drawScrollableContent);
                        contentHeight = bodyNatural + gap + scrollNatural;
                    }
                }
            }
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }

        return contentHeight;
    }

    internal static void DrawBodyText(string text, TextSlot semantic)
    {
        if (string.IsNullOrEmpty(text))
            return;

        using (Slap.PushFont(SlapFontSize.Regular, SlapFontWeight.Regular))
        {
            var color = ThemeScope.Resolved.GetTextSlot(semantic);
            DrawWrappedTextCentered(text, color);
        }
    }

    private static void DrawWrappedTextCentered(string text, Vector4 color)
    {
        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        var cursor = ImGui.GetCursorScreenPos();
        if (availableWidth <= 0f)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted(text);
            ImGui.PopStyleColor();
            return;
        }

        var lines = TextWrapUtility.Wrap(text, availableWidth);
        var lineHeight = ImGui.GetTextLineHeight();
        var drawList = ImGui.GetWindowDrawList();
        var colorU32 = ImGui.ColorConvertFloat4ToU32(color);
        for (var index = 0; index < lines.Count; index++)
        {
            var size = ImGui.CalcTextSize(lines[index]);
            var x = cursor.X + MathF.Max(0f, (availableWidth - size.X) * 0.5f);
            var y = cursor.Y + index * lineHeight;
            drawList.AddText(new Vector2(x, y), colorU32, lines[index]);
        }

        ImGui.SetCursorScreenPos(new Vector2(cursor.X, cursor.Y + lines.Count * lineHeight));
    }

    private static float DrawScrollableContent(Action drawContent)
    {
        var available = Vector2.Max(Vector2.Zero, ImGui.GetContentRegionAvail());
        if (available.X <= 0f || available.Y <= 0f)
            return 0f;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        var visible = ImGui.BeginChild(
            "##modalScrollableContent",
            available,
            false,
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground
        );
        var naturalHeight = 0f;
        try
        {
            if (visible)
            {
                drawContent();
                naturalHeight = ImGui.GetCursorPosY();
            }
        }
        finally
        {
            ImGui.EndChild();
            ImGui.PopStyleVar();
        }

        return naturalHeight;
    }

    private static string NormalizeSingleLineTitle(string text) =>
        text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');

    private static string TrimTextToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            return string.Empty;

        if (ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        var ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
        if (ellipsisWidth >= maxWidth)
            return string.Empty;

        var lo = 0;
        var hi = text.Length;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            var candidate = text[..mid] + ellipsis;
            if (ImGui.CalcTextSize(candidate).X <= maxWidth)
                lo = mid;
            else
                hi = mid - 1;
        }

        return lo <= 0 ? ellipsis : text[..lo] + ellipsis;
    }

    private static ModalDialogPanelScope BeginPanel(ModalDialogSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
        {
            throw new ArgumentException(
                "ModalDialogSpec.Key.Value must not be empty.",
                nameof(spec)
            );
        }

        ImGui.PushID(spec.Key.Value);
        var mask = OverlayMaskComponent.Begin(
            new OverlayMaskSpec(spec.Key.Value, ShowOverlay: true, spec.OverlayAlpha)
        );
        if (!mask.IsOpen)
        {
            mask.Dispose();
            ImGui.PopID();
            return new ModalDialogPanelScope(false, false, false, false, null, 0f);
        }

        var hostMin = mask.HostMin;
        var hostMax = mask.HostMax;
        var hostSize = mask.HostSize;
        var windowSize = mask.WindowSize;

        var margin = MetricsScope.ScalePadding(HostMargin);
        var maxPanelSize = new Vector2(
            MathF.Max(0f, hostSize.X - margin * 2f),
            MathF.Max(0f, hostSize.Y - margin * 2f)
        );
        var panelWidth = MathF.Min(
            maxPanelSize.X,
            MetricsScope.UnitWidth * Normalize(spec.WidthUnits, 6f));
        var maxPanelHeight = MathF.Min(
            maxPanelSize.Y,
            MetricsScope.UnitHeight * Normalize(spec.MaxHeightUnits, 7f));
        var panelHeight = ResolvePanelHeight(spec.Key.Value, maxPanelHeight);
        var panelSize = new Vector2(panelWidth, panelHeight);
        var panelMin = mask.WindowMin + (windowSize - panelSize) * 0.5f;
        panelMin.Y = Math.Clamp(panelMin.Y, hostMin.Y, hostMax.Y - panelSize.Y);
        var panelMax = panelMin + panelSize;

        var dismissed = spec.DismissOnMaskClick && mask.CaptureOutsideClick(panelMin, panelMax);

        var background = SurfaceComponent.ResolveColors(spec.State);
        var drawList = ImGui.GetWindowDrawList();
        var rounding = SlapCorners.ControlRadius;
        LayeredShadow.Draw(
            drawList,
            panelMin,
            panelMax,
            rounding,
            LayeredShadowSpec.ResolveStandard(spec.Shadow) with { PushFullscreenClip = true });
        SurfaceComponent.DrawFrostedPanelBackground(drawList, panelMin, panelMax, rounding);
        drawList.AddRectFilled(
            panelMin,
            panelMax,
            ImGui.ColorConvertFloat4ToU32(background),
            rounding
        );
        if (ThemeScope.Resolved.IsTransparentTheme)
        {
            SurfaceComponent.DrawPanelBorder(
                drawList,
                panelMin,
                panelMax,
                ThemeScope.Resolved.Border,
                rounding);
        }

        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowPadding,
            MetricsScope.ScalePadding(spec.Padding ?? PanelPadding)
        );
        ImGui.SetCursorScreenPos(panelMin);
        var panelVisible = ImGui.BeginChild(
            "##modalPanel",
            panelSize,
            false,
            ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysUseWindowPadding
        );
        if (!panelVisible)
        {
            ImGui.EndChild();
            ImGui.PopStyleVar();
            mask.Dispose();
            ImGui.PopID();
            return new ModalDialogPanelScope(false, dismissed, false, false, null, maxPanelHeight);
        }

        return new ModalDialogPanelScope(true, dismissed, true, true, mask, maxPanelHeight);
    }

    private static float ResolvePanelHeight(string key, float maxPanelHeight)
    {
        var height = PanelHeights.TryGetValue(key, out var cached)
            ? cached
            : maxPanelHeight;
        return Math.Clamp(height, 0f, maxPanelHeight);
    }

    private static void RecordPanelHeight(string key, float panelHeight, float maxPanelHeight)
    {
        var clamped = Math.Clamp(panelHeight, 0f, maxPanelHeight);
        if (clamped > 0f)
            PanelHeights[key] = clamped;
        else
            PanelHeights.Remove(key);
    }

    private static float Normalize(float value, float fallback) =>
        value > 0f && !float.IsInfinity(value) ? value : fallback;
}
