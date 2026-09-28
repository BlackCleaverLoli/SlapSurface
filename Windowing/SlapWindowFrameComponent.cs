using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal static class SlapWindowFrameComponent
{
    public static SlapWindowFrameScope Begin(
        SlapWindowFrameSpec spec,
        SlapWindowResizeHandler? resizeHandler = null,
        SlapSidebarCollapseHandler? sidebarCollapseHandler = null,
        SlapTitleBarNavigationHandler? titleBarNavigationHandler = null
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("SlapWindowFrameSpec.Key.Value must not be empty.", nameof(spec));

        var themeScope = ThemeScope.Push(spec.Theme);
        var metricsScope = MetricsScope.Push(spec.Metrics);
        var styleScope = SlapWindowStyleScope.PushFallback(spec.Theme, spec.Metrics);
        var typographyScope = TypographyScope.PushFallback(spec.Typography);

        ImGui.PushID(spec.Key.Value);
        DrawWindowBackdrop(spec);
        var titleBarContentMin = ImGui.GetCursorScreenPos();
        var titleBarNavigationResult = DrawNativeTitleBarOverlay(
            spec,
            titleBarContentMin,
            titleBarNavigationHandler,
            handleInput: true);

        var handler = resizeHandler ?? new SlapWindowResizeHandler();
        var resizeExpansion = spec.Theme.IsTransparentTheme
            ? MetricsScope.Scale(Normalize(spec.BackdropExtension, 6f))
            : 0f;
        handler.Handle(spec.Resize, resizeExpansion);
        var collapseHandler = sidebarCollapseHandler ?? new SlapSidebarCollapseHandler();

        return new SlapWindowFrameScope(
            spec,
            themeScope,
            metricsScope,
            styleScope,
            typographyScope,
            handler,
            collapseHandler,
            titleBarNavigationResult,
            s => DrawNativeTitleBarOverlay(s, titleBarContentMin, null, handleInput: false)
        );
    }

    internal static void DrawWindowBackdrop(SlapWindowFrameSpec spec)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
            return;

        var extension = MetricsScope.Scale(Normalize(spec.BackdropExtension, 6f));
        var min = windowPos - new Vector2(extension);
        var max = windowPos + windowSize + new Vector2(extension);
        var contentMin = ImGui.GetCursorScreenPos();
        var bodyMinY = ResolveBodyMinY(windowPos, windowSize, contentMin);
        var bodyMin = new Vector2(min.X, bodyMinY);
        var bodyMax = max;
        var rounding = SlapCorners.ControlRadius;
        var drawList = ImGui.GetWindowDrawList();
        var fill = ResolveBackdropFill(spec);
        var isFrosted = spec.Preset == SlapWindowFramePreset.FrostedShell;
        var backdropMin = isFrosted ? min : bodyMin;
        var backdropMax = isFrosted ? max : bodyMax;
        var backdropCornerFlags = isFrosted ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersBottom;

        var shadowSpec = LayeredShadowSpec.Standard with { PushFullscreenClip = true };

        LayeredShadow.Draw(
            drawList,
            backdropMin,
            backdropMax,
            rounding,
            shadowSpec);

        if (isFrosted)
        {
            using (PushExpandedDrawClip(drawList, backdropMin, backdropMax, extension + MetricsScope.Scale(1f)))
            {
                var overlay = spec.FrostedOverlayColor ?? ResolveFrostedOverlay(spec);
                if (spec.DrawFrostedBackground is { } drawFrosted)
                {
                    drawFrosted(
                        drawList,
                        backdropMin,
                        backdropMax,
                        rounding,
                        backdropCornerFlags,
                        overlay);
                }
                else
                {
                    drawList.AddRectFilled(
                        backdropMin,
                        backdropMax,
                        ImGui.ColorConvertFloat4ToU32(overlay),
                        rounding,
                        backdropCornerFlags);
                }

                drawList.AddRectFilled(
                    backdropMin,
                    backdropMax,
                    ImGui.ColorConvertFloat4ToU32(fill),
                    rounding,
                    backdropCornerFlags);
                if (spec.Theme.IsTransparentTheme)
                {
                    drawList.AddRect(
                        backdropMin,
                        backdropMax,
                        ImGui.ColorConvertFloat4ToU32(spec.Theme.Border),
                        rounding,
                        backdropCornerFlags,
                        MetricsScope.BorderThickness);
                }
            }
        }
        else
        {
            drawList.AddRectFilled(
                backdropMin,
                backdropMax,
                ImGui.ColorConvertFloat4ToU32(fill),
                rounding,
                backdropCornerFlags);
        }

    }

    private static float ResolveBodyMinY(Vector2 windowPos, Vector2 windowSize, Vector2 contentMin)
    {
        var windowMaxY = windowPos.Y + windowSize.Y;
        var titleHeight = MathF.Max(0f, contentMin.Y - windowPos.Y);
        if (titleHeight <= 0f)
            return windowPos.Y;

        var rounding = MathF.Min(SlapCorners.ControlRadius, MathF.Min(windowSize.X, titleHeight) * 0.5f);
        return MathF.Min(windowMaxY, MathF.Max(windowPos.Y, contentMin.Y - rounding));
    }

    private static SlapTitleBarNavigationResult DrawNativeTitleBarOverlay(
        SlapWindowFrameSpec spec,
        Vector2 contentMin,
        SlapTitleBarNavigationHandler? navigationHandler,
        bool handleInput)
    {
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();
        if (windowSize.X <= 0f || windowSize.Y <= 0f)
            return default;

        var titleMax = new Vector2(windowPos.X + windowSize.X, contentMin.Y);
        if (titleMax.Y <= windowPos.Y)
            return default;

        var bounds = new TitleBarBounds(
            windowPos,
            titleMax,
            GetTitleBarRounding(windowSize.X, titleMax.Y - windowPos.Y));
        if (bounds.Max.X <= bounds.Min.X || bounds.Max.Y <= bounds.Min.Y)
            return default;

        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(bounds.Min, bounds.Max, false);
        if (!spec.Theme.IsTransparentTheme)
            DrawTitleBarBackground(drawList, spec, bounds);
        var result = DrawTitleBarDecorations(
            drawList,
            spec,
            bounds,
            navigationHandler,
            handleInput);
        drawList.PopClipRect();
        return result;
    }

    private static void DrawTitleBarBackground(ImDrawListPtr drawList, SlapWindowFrameSpec spec, TitleBarBounds bounds)
    {
        drawList.AddRectFilled(
            bounds.Min,
            bounds.Max,
            ImGui.ColorConvertFloat4ToU32(ResolveTitleBarFill(spec)),
            bounds.Rounding,
            ImDrawFlags.RoundCornersAll);
    }

    private static SlapTitleBarNavigationResult DrawTitleBarDecorations(
        ImDrawListPtr drawList,
        SlapWindowFrameSpec spec,
        TitleBarBounds bounds,
        SlapTitleBarNavigationHandler? navigationHandler,
        bool handleInput)
    {
        var style = ImGui.GetStyle();
        var fontSize = ImGui.GetFontSize();
        var nativeFramePadding = SlapWindowStyleScope.ResolveTitleBarFramePadding(spec.Metrics);
        var cornerInset = SlapWindowStyleScope.ResolveTitleBarCornerInset(spec.Theme, spec.Metrics);
        var itemInnerSpacing = style.ItemInnerSpacing;
        var titleAlign = SlapWindowStyleScope.WindowTitleAlign;
        var flags = ImGuiP.GetCurrentWindow().Flags;
        var hasCloseButton = spec.ShowCloseButton;
        var hasCollapseButton =
            !flags.HasFlag(ImGuiWindowFlags.NoCollapse)
            && style.WindowMenuButtonPosition != ImGuiDir.None;
        var buttonSize = fontSize;
        var buttonGap = itemInnerSpacing.X;
        var hasLeftCollapseButton =
            hasCollapseButton && style.WindowMenuButtonPosition == ImGuiDir.Left;
        var rightNativeButtonCount = 0;
        if (hasCloseButton)
            rightNativeButtonCount++;
        if (hasCollapseButton && style.WindowMenuButtonPosition == ImGuiDir.Right)
            rightNativeButtonCount++;

        var rightButtonCount =
            rightNativeButtonCount + Math.Max(0, spec.TitleBarButtonReserveCount);
        var padR = rightButtonCount > 0
            ? rightButtonCount * (buttonSize + buttonGap)
            : MathF.Max(nativeFramePadding.X, cornerInset);
        var leftTitleReserve = hasLeftCollapseButton
            ? nativeFramePadding.X + buttonSize + buttonGap
            : MathF.Max(nativeFramePadding.X, cornerInset);

        var navigationLeftInset = hasLeftCollapseButton
            ? nativeFramePadding.X + buttonSize + buttonGap
            : cornerInset;

        var navigationResult = DrawTitleBarNavigation(
            drawList,
            spec,
            bounds,
            nativeFramePadding,
            buttonSize,
            buttonGap,
            navigationLeftInset,
            titleTextColor: ImGui.ColorConvertFloat4ToU32(spec.Theme.Emphasis),
            navigationHandler,
            handleInput,
            out var navigationRightInset);
        var padL = MathF.Max(leftTitleReserve, navigationRightInset);

        var titleText = spec.Title;
        var titleWidth = bounds.Max.X - bounds.Min.X;
        var symmetricPadding = MathF.Max(padL, padR);
        var availableTextWidth = titleWidth - symmetricPadding * 2f;

        var titleTextColor = ImGui.ColorConvertFloat4ToU32(spec.Theme.Emphasis);
        DrawTitleText(
            drawList,
            titleText,
            new Vector2(bounds.Min.X + symmetricPadding, bounds.Min.Y),
            new Vector2(bounds.Max.X - symmetricPadding, bounds.Max.Y),
            MathF.Max(0f, availableTextWidth),
            titleAlign,
            titleTextColor,
            spec.TitleBarIcon,
            MetricsScope.ScaleGap(SlapPx.Space8),
            bounds.Min.X + padL - buttonGap);

        if (hasCollapseButton)
        {
            var nativeRightPad = nativeFramePadding.X;
            if (hasCloseButton)
                nativeRightPad += buttonSize + itemInnerSpacing.X;

            var collapseMin = style.WindowMenuButtonPosition == ImGuiDir.Right
                ? new Vector2(bounds.Max.X - nativeRightPad - buttonSize, bounds.Min.Y + nativeFramePadding.Y)
                : new Vector2(bounds.Min.X + nativeFramePadding.X, bounds.Min.Y + nativeFramePadding.Y);
            DrawTitleBarCollapseButton(drawList, collapseMin, buttonSize, titleTextColor);
        }

        if (hasCloseButton)
        {
            var closeInsetX = MetricsScope.Scale(TitleBarCloseInset);
            var closeMin = new Vector2(
                bounds.Max.X - closeInsetX - buttonSize,
                bounds.Min.Y + nativeFramePadding.Y);
            DrawTitleBarCloseButton(
                drawList,
                closeMin,
                buttonSize,
                new Vector2(closeInsetX, nativeFramePadding.Y),
                titleTextColor);
        }

        return navigationResult;
    }

    private static SlapTitleBarNavigationResult DrawTitleBarNavigation(
        ImDrawListPtr drawList,
        SlapWindowFrameSpec spec,
        TitleBarBounds bounds,
        Vector2 framePadding,
        float buttonSize,
        float gap,
        float leftInset,
        uint titleTextColor,
        SlapTitleBarNavigationHandler? navigationHandler,
        bool handleInput,
        out float occupiedRightInset)
    {
        var navigation = spec.TitleBarNavigation;
        occupiedRightInset = 0f;
        if (!navigation.Visible)
        {
            navigationHandler?.SetBounds(false, default, default);
            return default;
        }

        var leftPad = MetricsScope.ScaleGap(SlapPx.Space4);
        var min = new Vector2(
            bounds.Min.X + leftInset + leftPad,
            bounds.Min.Y + framePadding.Y);
        var backMin = min;
        var forwardMin = new Vector2(min.X + buttonSize + gap, min.Y);
        var max = forwardMin + new Vector2(buttonSize);
        occupiedRightInset = leftInset + leftPad + buttonSize * 2f + gap * 2f;
        navigationHandler?.SetBounds(true, min, max);

        var backClicked = DrawTitleBarNavigationButton(
            drawList,
            backMin,
            buttonSize,
            ImGuiDir.Left,
            navigation.BackEnabled,
            navigation.BackTooltip,
            titleTextColor,
            handleInput);
        var forwardClicked = DrawTitleBarNavigationButton(
            drawList,
            forwardMin,
            buttonSize,
            ImGuiDir.Right,
            navigation.ForwardEnabled,
            navigation.ForwardTooltip,
            titleTextColor,
            handleInput);
        return new SlapTitleBarNavigationResult(backClicked, forwardClicked);
    }

    private static bool DrawTitleBarNavigationButton(
        ImDrawListPtr drawList,
        Vector2 min,
        float size,
        ImGuiDir direction,
        bool enabled,
        string? tooltip,
        uint color,
        bool handleInput)
    {
        var max = min + new Vector2(size);
        var mousePos = ImGui.GetIO().MousePos;
        var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows)
            && mousePos.X >= min.X
            && mousePos.X <= max.X
            && mousePos.Y >= min.Y
            && mousePos.Y <= max.Y;
        if (hovered && enabled)
            DrawTitleBarButtonBackground(drawList, min, max);

        var arrowColor = enabled
            ? color
            : ImGui.ColorConvertFloat4ToU32(
                SlapColor.WithDisabledAlpha(ThemeScope.Resolved.Emphasis, true));
        DrawImGuiArrow(drawList, min, arrowColor, direction, size);

        if (handleInput)
            SlapTooltip.TryShow(hovered, tooltip, min, max);

        return handleInput
            && enabled
            && hovered
            && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
    }

    private static void DrawTitleText(
        ImDrawListPtr drawList,
        string text,
        Vector2 layoutMin,
        Vector2 layoutMax,
        float availableWidth,
        Vector2 align,
        uint color,
        SlapTitleBarIconSpec? titleBarIcon = null,
        float iconGap = 0f,
        float iconMinAllowedX = 0f)
    {
        if (string.IsNullOrEmpty(text) || availableWidth <= 0f || layoutMax.X <= layoutMin.X)
            return;

        using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold))
        {
            var displayText = TrimTextToWidth(text, availableWidth);
            if (string.IsNullOrEmpty(displayText))
                return;

            var textSize = ImGui.CalcTextSize(displayText);
            var x = layoutMin.X + MathF.Max(0f, availableWidth - textSize.X) * align.X;
            var y = layoutMin.Y + MathF.Max(0f, layoutMax.Y - layoutMin.Y - textSize.Y) * align.Y;

            if (titleBarIcon.HasValue)
                DrawTitleLeadingIcon(
                    drawList,
                    titleBarIcon.Value,
                    iconGap,
                    new Vector2(x, y),
                    textSize,
                    iconMinAllowedX);
            drawList.AddText(new Vector2(x, y), color, displayText);
        }
    }

    private static void DrawTitleLeadingIcon(
        ImDrawListPtr drawList,
        SlapTitleBarIconSpec icon,
        float gap,
        Vector2 textPos,
        Vector2 textSize,
        float minAllowedX)
    {
        var size = Normalize(icon.Size ?? textSize.Y, textSize.Y);
        if (size <= 0f)
            return;
        var hasTexture = icon.Texture != null && icon.Texture.Handle != nint.Zero;
        var hasGameIcon = icon.GameIconId != 0;
        if (!hasTexture && !hasGameIcon)
            return;

        var iconMin = new Vector2(
            textPos.X - gap - size,
            textPos.Y + MathF.Max(0f, (textSize.Y - size) * 0.5f));
        if (iconMin.X < minAllowedX)
            return;
        var iconMax = iconMin + new Vector2(size);

        if (hasGameIcon)
            GameIconComponent.DrawInSlot(drawList, icon.GameIconId, iconMin, iconMax);
        else
            GameIconComponent.DrawInSlot(drawList, icon.Texture, iconMin, iconMax);
    }

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

    // 关闭钮右缘内缩（unscaled px）：与 SlapSurfaceWindow.PreDraw 压的 FramePadding.x
    // 一致，使原生关闭钮命中区在默认 4px 间距下与宿主按钮相切，避免 hover 抢夺。
    internal const float TitleBarCloseInset = SlapPx.Space2;

    private static void DrawTitleBarCloseButton(
        ImDrawListPtr drawList,
        Vector2 min,
        float size,
        Vector2 framePadding,
        uint color)
    {
        var max = min + new Vector2(size);
        DrawNativeTitleBarCloseHover(drawList, min, size, framePadding);

        var center = (min + max) * 0.5f - new Vector2(0.5f);
        var extent = size * 0.5f * 0.7071f - MetricsScope.ScaleBorder(SlapPx.Space1);
        if (extent <= 0f)
            return;

        var thickness = MathF.Max(MetricsScope.ScaleBorder(SlapPx.Space1), 1f);
        drawList.AddLine(center + new Vector2(extent, extent), center - new Vector2(extent, extent), color, thickness);
        drawList.AddLine(center + new Vector2(extent, -extent), center + new Vector2(-extent, extent), color, thickness);
    }

    private static void DrawTitleBarCollapseButton(
        ImDrawListPtr drawList,
        Vector2 min,
        float size,
        uint color)
    {
        if (size <= 0f)
            return;

        DrawTitleBarButtonBackground(drawList, min, min + new Vector2(size));
        DrawImGuiArrow(drawList, min, color, ImGui.IsWindowCollapsed() ? ImGuiDir.Right : ImGuiDir.Down, size);
    }

    private static void DrawImGuiArrow(
        ImDrawListPtr drawList,
        Vector2 pos,
        uint color,
        ImGuiDir direction,
        float fontSize)
    {
        var h = fontSize;
        var r = h * 0.4f;
        var center = pos + new Vector2(h * 0.5f, h * 0.5f);
        Vector2 a;
        Vector2 b;
        Vector2 c;
        switch (direction)
        {
            case ImGuiDir.Up:
            case ImGuiDir.Down:
                if (direction == ImGuiDir.Up)
                    r = -r;
                a = new Vector2(0f, 0.75f) * r;
                b = new Vector2(-0.866f, -0.75f) * r;
                c = new Vector2(0.866f, -0.75f) * r;
                break;
            case ImGuiDir.Left:
            case ImGuiDir.Right:
                if (direction == ImGuiDir.Left)
                    r = -r;
                a = new Vector2(0.75f, 0f) * r;
                b = new Vector2(-0.75f, 0.866f) * r;
                c = new Vector2(-0.75f, -0.866f) * r;
                break;
            default:
                return;
        }

        drawList.AddTriangleFilled(center + a, center + b, center + c, color);
    }

    private static void DrawTitleBarButtonBackground(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max)
    {
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows))
            return;

        var mousePos = ImGui.GetIO().MousePos;
        var hovered =
            mousePos.X >= min.X
            && mousePos.X <= max.X
            && mousePos.Y >= min.Y
            && mousePos.Y <= max.Y;
        if (!hovered)
            return;

        var color = ImGui.GetColorU32(
            ImGui.GetIO().MouseDown[(int)ImGuiMouseButton.Left]
                ? ImGuiCol.ButtonActive
                : ImGuiCol.ButtonHovered);
        var size = max - min;
        var center = (min + max) * 0.5f - new Vector2(0f, 0.5f);
        var radius = MathF.Min(size.X, size.Y) * 0.5f + MetricsScope.ScaleBorder(SlapPx.Space1);
        drawList.AddCircleFilled(center, radius, color);
    }

    // 与运行时原生 CloseButton（Dalamud 内嵌 imgui）一致：命中区为方块外扩
    // FramePadding，hover 为以方块中心为圆心、半径 FontSize/2+1 的圆。
    private static void DrawNativeTitleBarCloseHover(
        ImDrawListPtr drawList,
        Vector2 min,
        float size,
        Vector2 framePadding)
    {
        if (!ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows))
            return;

        var hitMin = min - framePadding;
        var hitMax = min + new Vector2(size) + framePadding;
        var mousePos = ImGui.GetIO().MousePos;
        if (mousePos.X < hitMin.X || mousePos.X > hitMax.X || mousePos.Y < hitMin.Y || mousePos.Y > hitMax.Y)
            return;

        var color = ImGui.GetColorU32(
            ImGui.GetIO().MouseDown[(int)ImGuiMouseButton.Left]
                ? ImGuiCol.ButtonActive
                : ImGuiCol.ButtonHovered);
        var center = min + new Vector2(size * 0.5f);
        var radius = MathF.Max(2f, size * 0.5f + 1f);
        drawList.AddCircleFilled(center, radius, color, 12);
    }

    private static float GetTitleBarRounding(float width, float height)
    {
        var rounding = SlapCorners.ControlRadius;
        if (rounding <= 0f || height <= 0f || width <= 0f)
            return 0f;

        return MathF.Min(rounding, MathF.Min(width, height) * 0.5f);
    }

    private readonly record struct TitleBarBounds(Vector2 Min, Vector2 Max, float Rounding);

    private static Vector4 ResolveFrostedOverlay(SlapWindowFrameSpec spec)
    {
        if (spec.Preset != SlapWindowFramePreset.FrostedShell)
            return Vector4.Zero;

        var overlay = SlapColor.PerceivedLightness(spec.Theme.SurfaceBg) >= SlapColor.LightSurfaceThreshold
            ? Vector4.One
            : new Vector4(0f, 0f, 0f, 1f);
        return SlapColor.WithAlpha(
            overlay,
            Math.Clamp(
                Normalize(spec.FrostedOverlayAlpha, SlapWindowFrameDefaults.DefaultFrostedOverlayAlpha),
                0f,
                1f));
    }

    private static Vector4 ResolveBackdropFill(SlapWindowFrameSpec spec)
    {
        if (spec.Preset != SlapWindowFramePreset.FrostedShell)
            return ThemeScope.Resolved.Surface;

        var alpha = Math.Clamp(
            Normalize(spec.FrostedFillAlpha, SlapWindowFrameDefaults.DefaultFrostedFillAlpha),
            0f,
            1f);
        var surface = spec.DesaturateFrostedFill
            ? SlapColor.Desaturate(spec.Theme.SurfaceBg)
            : spec.Theme.SurfaceBg;
        return SlapColor.WithAlpha(surface, alpha);
    }

    private static Vector4 ResolveTitleBarFill(SlapWindowFrameSpec spec)
    {
        return spec.Theme.TitleBarBg;
    }

    private static IDisposable PushExpandedDrawClip(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float expansion)
    {
        expansion = MathF.Max(0f, expansion);
        drawList.PushClipRect(min - new Vector2(expansion), max + new Vector2(expansion), false);
        return new DrawClipScope(drawList);
    }

    private static float Normalize(float value, float fallback) =>
        value >= 0f && !float.IsInfinity(value) ? value : fallback;
}

internal readonly struct DrawClipScope : IDisposable
{
    private readonly ImDrawListPtr drawList;

    public DrawClipScope(ImDrawListPtr drawList)
    {
        this.drawList = drawList;
    }

    public void Dispose()
    {
        drawList.PopClipRect();
    }
}

internal sealed class SlapWindowFrameScope : IDisposable
{
    private readonly SlapWindowFrameSpec _spec;
    private readonly IDisposable _themeScope;
    private readonly IDisposable _metricsScope;
    private readonly SlapWindowStyleScope _styleScope;
    private readonly IDisposable _typographyScope;
    private readonly SlapWindowResizeHandler _resizeHandler;
    private readonly SlapSidebarCollapseHandler _sidebarCollapseHandler;
    private readonly Action<SlapWindowFrameSpec> _redrawTitleBar;
    private ShellScope? _shell;
    private float _sidebarFullWidthUnits;
    private int _flags; // bit 0: shellBegun, 1: disposed

    internal SlapWindowFrameScope(
        SlapWindowFrameSpec spec,
        IDisposable themeScope,
        IDisposable metricsScope,
        SlapWindowStyleScope styleScope,
        IDisposable typographyScope,
        SlapWindowResizeHandler resizeHandler,
        SlapSidebarCollapseHandler sidebarCollapseHandler,
        SlapTitleBarNavigationResult titleBarNavigationResult,
        Action<SlapWindowFrameSpec> redrawTitleBar)
    {
        _spec = spec;
        _themeScope = themeScope;
        _metricsScope = metricsScope;
        _styleScope = styleScope;
        _typographyScope = typographyScope;
        _resizeHandler = resizeHandler;
        _sidebarCollapseHandler = sidebarCollapseHandler;
        TitleBarNavigationResult = titleBarNavigationResult;
        _redrawTitleBar = redrawTitleBar;
    }

    /// <summary>
    /// Redraw the title bar overlay on top of any content drawn after the frame began.
    /// Call this after drawing overlays that extend into the title bar area
    /// (e.g. popup sidebar dim strips) so the title bar decorations are not obscured.
    /// </summary>
    public void RedrawTitleBar() => _redrawTitleBar(_spec);

    public SlapTitleBarNavigationResult TitleBarNavigationResult { get; }

    /// <summary>
    /// True when the window should have NoMove flag set for a frame-level drag interaction.
    /// Callers should OR this into their ImGui window flags.
    /// </summary>
    public bool ShouldDisableMove =>
        _resizeHandler.ShouldDisableMove || _sidebarCollapseHandler.ShouldDisableMove;

    /// <summary>One-shot persistence request produced by the sidebar gap drag.</summary>
    public SlapSidebarCollapseResult SidebarCollapseResult { get; private set; }

    public SidebarScope BeginSidebar()
    {
        EnsureShell();
        return _shell!.BeginSidebar();
    }

    public ContentScope BeginContent()
    {
        EnsureShell();
        return _shell!.BeginContent();
    }

    /// <summary>
    /// Begin sidebar, run <paramref name="draw"/>, then dispose the scope automatically.
    /// </summary>
    public void Sidebar(Action<SidebarScope> draw)
    {
        using var sidebar = BeginSidebar();
        draw(sidebar);
    }

    /// <summary>
    /// Begin content, run <paramref name="draw"/>, then dispose the scope automatically.
    /// </summary>
    public void Content(Action<ContentScope> draw)
    {
        using var content = BeginContent();
        draw(content);
    }

    /// <summary>Read-only screen-space bounds for sidebar and content slots.
    /// Valid after <see cref="BeginSidebar"/> or <see cref="BeginContent"/> has been called.</summary>
    public ShellBounds Bounds => _shell?.Bounds ?? default;

    /// <summary>Expanded sidebar width in frame units, resolved for the current frame.</summary>
    public float SidebarFullWidthUnits => _sidebarFullWidthUnits;

    private void EnsureShell()
    {
        EnsureNotDisposed();

        if ((_flags & 1) != 0)
            return;

        _flags |= 1;
        HoverArbitration.Reset();
        var bodyGap = MetricsScope.Scale(NormalizeFrameValue(_spec.BodyGap, 4f));
        if (bodyGap > 0f)
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + bodyGap);

        var sidebarLayout = _spec.ResolveSidebarLayout();
        _sidebarFullWidthUnits = sidebarLayout.FullWidthUnits;
        _shell = ShellComponent.Begin(
            new ShellSpec(
                $"{_spec.Key.Value}_shell",
                ShellSize.FillAvailable,
                sidebarLayout.WidthUnits,
                ResolveShellGapUnits(_spec.ShellGap),
                SurfaceBg: _spec.SurfaceBg,
                Shadow: _spec.Shadow
            ),
            sidebarLayout
        );
        SidebarCollapseResult = _sidebarCollapseHandler.Handle(
            _spec.SidebarCollapse,
            sidebarLayout,
            _shell.Bounds
        );
    }

    private static float NormalizeFrameValue(float value, float fallback) =>
        value >= 0f && !float.IsInfinity(value) ? value : fallback;
    private static float ResolveShellGapUnits(float shellGap)
    {
        var frameUnit = MetricsScope.UnitHeight;
        if (frameUnit <= 0f)
            return 0f;

        return MetricsScope.Scale(NormalizeFrameValue(shellGap, 4f)) / frameUnit;
    }
    private void EnsureNotDisposed()
    {
        if ((_flags & 2) != 0)
            throw new ObjectDisposedException(nameof(SlapWindowFrameScope));
    }

    public void Dispose()
    {
        if ((_flags & 2) != 0)
            return;

        _flags |= 2;
        _shell?.Dispose();
        _shell = null;
        ImGui.PopID();
        _typographyScope.Dispose();
        _styleScope.Dispose();
        _metricsScope.Dispose();
        _themeScope.Dispose();
    }
}

internal sealed class SlapWindowFrameHostStyleScope : IDisposable
{
    private readonly IDisposable _typographyScope;
    private readonly SlapWindowStyleScope _styleScope;
    private bool _disposed;

    private SlapWindowFrameHostStyleScope(IDisposable typographyScope, SlapWindowStyleScope styleScope)
    {
        _typographyScope = typographyScope;
        _styleScope = styleScope;
    }

    public static SlapWindowFrameHostStyleScope Push(
        SlapTheme theme,
        SlapMetricsConfig metrics,
        SlapTypographySpec typography,
        bool transparentTitleBar)
    {
        var typographyScope = TypographyScope.Push(typography);
        var styleScope = SlapWindowStyleScope.Push(theme, metrics, transparentTitleBar);
        return new SlapWindowFrameHostStyleScope(typographyScope, styleScope);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _styleScope.Dispose();
        _typographyScope.Dispose();
    }
}

internal readonly struct SlapWindowStyleScope : IDisposable
{
    private static readonly Vector2 TitleAlign = new(0.5f, 0.5f);
    private static readonly Vector2 BaseFramePadding = new(SlapPx.Space6, SlapPx.Space4);
    private static readonly Vector2 BaseItemInnerSpacing = new(SlapPx.Space4, SlapPx.Space4);
    private static readonly Vector2 BaseCellPadding = new(SlapPx.Space4, SlapPx.Space2);
    private const float BaseIndentSpacing = 21f;
    // Half the corner radius is deliberate: title bar text and navigation
    // controls sit below the top corner arc, so the full radius would push
    // them too far from the window edge.
    private const float RoundedTitleBarHorizontalInsetRatio = 0.5f;
    private const float RoundedTitleBarHorizontalInsetMax = SlapPx.Space16;

    private readonly int _styleColorCount;
    private readonly int _styleVarCount;
    private static int activeScopeCount;

    private readonly bool _tracksActiveScope;

    private SlapWindowStyleScope(int styleColorCount, int styleVarCount, bool tracksActiveScope)
    {
        _styleColorCount = styleColorCount;
        _styleVarCount = styleVarCount;
        _tracksActiveScope = tracksActiveScope;
    }

    internal static Vector2 WindowTitleAlign => TitleAlign;

    public static SlapWindowStyleScope Push(SlapTheme theme, SlapMetricsConfig metrics, bool transparentTitleBar = true)
    {
        var normalizedMetrics = NormalizeMetrics(metrics);
        var rounding = SlapCorners.Resolve(theme, normalizedMetrics);
        var titleText = theme.Emphasis;
        var pressedTitleBg = SlapColor.DerivePressedColor(theme.TitleBarBg);
        var hoveredTitleBg = SlapColor.DeriveHoverColor(pressedTitleBg);
        var titleBg = transparentTitleBar ? Vector4.Zero : theme.TitleBarBg;
        var nativeBorderSize = theme.IsTransparentTheme
            ? SlapPx.Space1 * normalizedMetrics.Scale * normalizedMetrics.BorderScale
            : 0f;
        var nativeBorderColor = theme.IsTransparentTheme ? theme.Border : Vector4.Zero;
        var titleBarBg = theme.IsTransparentTheme
            ? ResolveFrostedTitleBarFill(theme)
            : titleBg;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ResolveTitleBarFramePaddingNormalized(normalizedMetrics));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowTitleAlign, TitleAlign);
        // SlapSurface 窗口的透明度由主题颜色承载；这里强制不透明，避免宿主恢复的
        // 窗口级 opacity（旧版汉堡菜单的透明度设置）残留到新窗口。
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, nativeBorderSize);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        var s = normalizedMetrics.Scale;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, BaseItemInnerSpacing * s);
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, BaseIndentSpacing * s);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, BaseCellPadding * s);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Border, nativeBorderColor);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, titleBarBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, titleBarBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, titleBarBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, pressedTitleBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, hoveredTitleBg);
        ImGui.PushStyleColor(ImGuiCol.Text, titleText);
        PushScrollbarStyle(theme, normalizedMetrics.Scale, rounding);
        activeScopeCount++;
        return new SlapWindowStyleScope(styleColorCount: 13, styleVarCount: 18, tracksActiveScope: true);
    }

    public static SlapWindowStyleScope PushFallback(SlapTheme theme, SlapMetricsConfig metrics) =>
        activeScopeCount > 0 ? PushContentStyle(theme, metrics) : PushStandaloneContentStyle(theme, metrics);

    private static SlapWindowStyleScope PushContentStyle(SlapTheme theme, SlapMetricsConfig metrics)
    {
        var normalizedMetrics = NormalizeMetrics(metrics);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ResolveContentFramePadding(normalizedMetrics));
        ImGui.PushStyleColor(ImGuiCol.Text, theme.Body);
        return new SlapWindowStyleScope(styleColorCount: 1, styleVarCount: 1, tracksActiveScope: false);
    }

    private static SlapWindowStyleScope PushStandaloneContentStyle(SlapTheme theme, SlapMetricsConfig metrics)
    {
        var normalizedMetrics = NormalizeMetrics(metrics);
        var rounding = SlapCorners.Resolve(theme, normalizedMetrics);
        var titleText = theme.Emphasis;
        var pressedTitleBg = SlapColor.DerivePressedColor(theme.TitleBarBg);
        var hoveredTitleBg = SlapColor.DeriveHoverColor(pressedTitleBg);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ResolveContentFramePadding(normalizedMetrics));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        var s = normalizedMetrics.Scale;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, BaseItemInnerSpacing * s);
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, BaseIndentSpacing * s);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, BaseCellPadding * s);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Border, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, pressedTitleBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, hoveredTitleBg);
        ImGui.PushStyleColor(ImGuiCol.Text, theme.Body);
        PushScrollbarStyle(theme, normalizedMetrics.Scale, rounding);
        activeScopeCount++;
        return new SlapWindowStyleScope(styleColorCount: 13, styleVarCount: 16, tracksActiveScope: true);
    }

    private static void PushScrollbarStyle(SlapTheme theme, float scale, float rounding)
    {
        var grabColor = theme.IsTransparentTheme ? theme.Body : theme.BaseFill;
        var grabHovered = SlapColor.DeriveHoverColor(grabColor);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, grabColor);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, grabHovered);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, grabHovered);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, scale * 14f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, rounding);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, scale * 38f);
    }

    private static Vector4 ResolveFrostedTitleBarFill(SlapTheme theme)
    {
        var fill = SlapColor.WithAlpha(
            SlapColor.Desaturate(theme.SurfaceBg),
            SlapWindowFrameDefaults.DefaultFrostedFillAlpha);
        var overlay = SlapColor.PerceivedLightness(theme.SurfaceBg) >= SlapColor.LightSurfaceThreshold
            ? new Vector4(1f, 1f, 1f, SlapWindowFrameDefaults.DefaultFrostedOverlayAlpha)
            : new Vector4(0f, 0f, 0f, SlapWindowFrameDefaults.DefaultFrostedOverlayAlpha);
        return SlapColor.CompositeOver(fill, overlay);
    }

    private static SlapMetricsConfig NormalizeMetrics(SlapMetricsConfig metrics)
    {
        return new SlapMetricsConfig(
            NormalizeFactor(metrics.Scale),
            NormalizeFactor(metrics.Density),
            NormalizeFactor(metrics.PaddingScale),
            NormalizeFactor(metrics.GapScale),
            NormalizeFactor(metrics.BorderScale));
    }

    internal static Vector2 ResolveTitleBarFramePadding(SlapMetricsConfig metrics)
    {
        return ResolveTitleBarFramePaddingNormalized(NormalizeMetrics(metrics));
    }

    internal static float ResolveTitleBarCornerInset(SlapTheme theme, SlapMetricsConfig metrics)
    {
        return ResolveTitleBarCornerInsetNormalized(theme, NormalizeMetrics(metrics));
    }

    private static Vector2 ResolveTitleBarFramePaddingNormalized(SlapMetricsConfig metrics)
    {
        return BaseFramePadding * metrics.Scale;
    }

    private static float ResolveTitleBarCornerInsetNormalized(
        SlapTheme theme,
        SlapMetricsConfig metrics)
    {
        var rounding = SlapCorners.Resolve(theme, metrics);
        return rounding <= 0f
            ? 0f
            : MathF.Min(
                rounding * RoundedTitleBarHorizontalInsetRatio,
                RoundedTitleBarHorizontalInsetMax * metrics.Scale);
    }

    private static Vector2 ResolveContentFramePadding(SlapMetricsConfig metrics) =>
        BaseFramePadding * metrics.Scale;

    private static float NormalizeFactor(float value) =>
        value > 0f && !float.IsInfinity(value) ? value : 1f;

    public void Dispose()
    {
        if (_styleColorCount > 0)
            ImGui.PopStyleColor(_styleColorCount);
        if (_styleVarCount > 0)
            ImGui.PopStyleVar(_styleVarCount);
        if (_tracksActiveScope)
            activeScopeCount--;
    }
}
