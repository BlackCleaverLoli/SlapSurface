using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>Visual shape of a game icon.</summary>
internal enum GameIconShape
{
    /// <summary>Use the active theme's derived media corner radius.</summary>
    Standard,

    /// <summary>Use the maximum safe corner radius regardless of theme.</summary>
    Circle,
}

/// <summary>
/// Standard game icon rendering: multi-layer embossed shadow, semi-transparent
/// background, rounded texture image, and optional overlays. No border.
/// This is the canonical rendering for game item icons in lists and tag chips.
/// </summary>
internal static class GameIconComponent
{
    // --- Layered shadow constants ---
    private const float BottomShadowAlpha = 0.75f;
    private const float TopShadowAlpha = 0.66f;
    private const float SideShadowTopAlpha = 0.66f;
    private const float BottomShadowHeight = 1.5f;
    private const float SideShadowWidth = 1f;
    private const float CircleRoundingGuardBand = 1f;

    // Persistent caller-supplied tint strength.
    private const float TintAlpha = 0.50f;

    // Interaction filters use Slap's existing control-state strengths as
    // final overlay alpha values; they are intentionally not scaled by TintAlpha.
    private const float HoverFilterAlpha = SlapColor.HoverLightnessDelta;
    private const float ActiveFilterAlpha = -SlapColor.ActivePressedLightnessDelta;
    private const float DisabledFilterAlpha = 1f - SlapColor.DisabledAlpha;

    private const float ShadowPadding = SlapPx.Space2;
    // Keeps control media and its shadow clear of capsule corners and inner hover glow.
    private const float ControlFillRatio = 0.75f;
    private const float CornerCheckSizeRatio = 0.33f;
    private const float CornerCheckInset = SlapPx.Space2;
    private const float CornerCheckUnderlayOffset = SlapPx.Space1;
    private static readonly Vector4 CornerCheckDefaultColor = new(1f, 160f / 255f, 0f, 1f);
    private static readonly Vector4 CornerCheckDefaultUnderlayColor = new(0f, 0f, 0f, 1f);
    private static readonly Vector2[] CornerCheckUnderlayDirections =
    {
        new(0f, -1f),
        new(0f, 1f),
        new(-1f, 0f),
        new(1f, 0f)
    };

    internal static bool IsAvailable(uint iconId, bool highQuality = false)
    {
        if (iconId == 0)
            return false;

        var texture = ResolveGameIcon(iconId, highQuality);
        return texture != null && texture.Handle != nint.Zero;
    }

    public static void DrawInSlot(
        ImDrawListPtr drawList,
        uint iconId,
        Vector2 slotMin,
        Vector2 slotMax,
        bool highQuality = false,
        GameIconShape shape = GameIconShape.Standard,
        Vector4? tint = null,
        FontAwesomeIcon? overlayIcon = null,
        Vector4? overlayColor = null,
        ControlVisualMode visualMode = ControlVisualMode.Base,
        bool cornerCheck = false,
        bool shadow = true)
    {
        var (iconMin, iconMax) = ResolveIconRect(slotMin, slotMax);
        Draw(
            drawList,
            iconId,
            iconMin,
            iconMax,
            highQuality,
            shape,
            tint,
            overlayIcon,
            overlayColor,
            visualMode,
            cornerCheck,
            shadow);
    }

    public static void DrawInSlot(
        ImDrawListPtr drawList,
        IDalamudTextureWrap? texture,
        Vector2 slotMin,
        Vector2 slotMax,
        ControlVisualMode visualMode = ControlVisualMode.Base,
        bool shadow = true)
    {
        var (iconMin, iconMax) = ResolveIconRect(slotMin, slotMax);
        DrawResolved(drawList, texture, iconMin, iconMax, visualMode, shadow);
    }

    internal static (Vector2 Min, Vector2 Max) ResolveControlIconRect(
        Vector2 slotMin,
        Vector2 slotMax
    ) => ResolveIconRect(slotMin, slotMax, ControlFillRatio);

    /// <summary>
    /// Gap between an icon slot edge and the drawn icon, so callers can mirror the
    /// icon's optical inset on the opposite content edge.
    /// </summary>
    internal static float ResolveSlotInset() => MetricsScope.Scale(ShadowPadding);

    internal static (Vector2 Min, Vector2 Max) ResolveIconRect(
        Vector2 slotMin,
        Vector2 slotMax,
        float fillRatio = 1f
    )
    {
        var pad = MetricsScope.Scale(ShadowPadding);
        var slotSize = MathF.Min(slotMax.X - slotMin.X, slotMax.Y - slotMin.Y);
        var normalizedFill = fillRatio >= 0f && !float.IsInfinity(fillRatio)
            ? Math.Clamp(fillRatio, 0f, 1f)
            : 1f;
        var size = MathF.Min(slotSize - pad * 2f, slotSize * normalizedFill);
        if (size <= 0f)
            return (slotMin, slotMin);
        var iconMin = new Vector2(
            slotMin.X + ((slotMax.X - slotMin.X) - size) * 0.5f,
            slotMin.Y + ((slotMax.Y - slotMin.Y) - size) * 0.5f);
        return ResolvePixelAlignedRect(iconMin, iconMin + new Vector2(size));
    }

    private static IDalamudTextureWrap? ResolveGameIcon(uint iconId, bool highQuality) =>
        iconId == 0 ? null : Slap.GameIconLoader?.Invoke(iconId, highQuality);

    private static (Vector2 Min, Vector2 Max) ResolvePixelAlignedRect(Vector2 min, Vector2 max)
    {
        var rawSize = MathF.Min(max.X - min.X, max.Y - min.Y);
        if (rawSize <= 0f)
            return (min, min);

        var size = MathF.Max(
            1f,
            MathF.Round(rawSize, MidpointRounding.AwayFromZero));
        var center = (min + max) * 0.5f;
        var alignedMin = new Vector2(
            MathF.Round(center.X - size * 0.5f, MidpointRounding.AwayFromZero),
            MathF.Round(center.Y - size * 0.5f, MidpointRounding.AwayFromZero));
        return (alignedMin, alignedMin + new Vector2(size));
    }

    public static void Draw(
        ImDrawListPtr drawList,
        uint iconId,
        Vector2 min,
        Vector2 max,
        bool highQuality = false,
        GameIconShape shape = GameIconShape.Standard,
        Vector4? tint = null,
        FontAwesomeIcon? overlayIcon = null,
        Vector4? overlayColor = null,
        ControlVisualMode visualMode = ControlVisualMode.Base,
        bool cornerCheck = false,
        bool shadow = true)
    {
        (min, max) = ResolvePixelAlignedRect(min, max);
        var size = MathF.Min(max.X - min.X, max.Y - min.Y);
        if (size <= 0f)
            return;

        var rounding = ResolveRounding(min, max, shape);
        var background = ResolveBackground();

        if (shadow)
            DrawShadow(drawList, min, max, rounding);
        drawList.AddRectFilled(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(background),
            rounding);

        var texture = iconId == 0 ? null : ResolveGameIcon(iconId, highQuality);
        if (texture != null && texture.Handle != nint.Zero)
        {
            drawList.AddImageRounded(
                texture.Handle,
                min,
                max,
                Vector2.Zero,
                Vector2.One,
                ImGui.ColorConvertFloat4ToU32(Vector4.One),
                rounding,
                ImDrawFlags.RoundCornersAll);
        }

        if (tint.HasValue)
            DrawColorOverlay(drawList, min, max, rounding, tint.Value, TintAlpha);

        DrawResolvedInteractionFilter(drawList, min, max, rounding, visualMode);

        if (overlayIcon.HasValue)
        {
            var (overlayText, overlayFont) = SlapIcon.Resolve(overlayIcon);
            if (!string.IsNullOrEmpty(overlayText) && overlayFont.HasValue)
            {
                var overlayColorUint = overlayColor.HasValue
                    ? ImGui.ColorConvertFloat4ToU32(overlayColor.Value)
                    : ImGui.ColorConvertFloat4ToU32(Vector4.One);
                ImGui.PushFont(overlayFont.Value);
                try
                {
                    var overlaySize = ImGui.CalcTextSize(overlayText);
                    var overlayX = min.X + ((max.X - min.X) - overlaySize.X) * 0.5f;
                    var overlayY = min.Y + ((max.Y - min.Y) - overlaySize.Y) * 0.5f;
                    drawList.AddText(new Vector2(overlayX, overlayY), overlayColorUint, overlayText);
                }
                finally
                {
                    ImGui.PopFont();
                }
            }
        }

        var cornerCheckPosition = Vector2.Zero;
        var cornerCheckUnderlayOffset = 0f;
        var cornerCheckSize = 0f;
        if (cornerCheck)
        {
            (cornerCheckPosition, cornerCheckUnderlayOffset, cornerCheckSize) =
                ResolveCornerCheckLayout(min, max);
        }

        if (cornerCheck && cornerCheckSize > 0f)
        {
            DrawCornerCheck(
                drawList,
                cornerCheckPosition,
                cornerCheckUnderlayOffset,
                cornerCheckSize,
                visualMode);
        }
    }

    /// <summary>
    /// Draw a standard game icon using a pre-resolved texture.
    /// </summary>
    public static void DrawResolved(
        ImDrawListPtr drawList,
        IDalamudTextureWrap? texture,
        Vector2 min,
        Vector2 max,
        ControlVisualMode visualMode = ControlVisualMode.Base,
        bool shadow = true)
    {
        (min, max) = ResolvePixelAlignedRect(min, max);
        var size = MathF.Min(max.X - min.X, max.Y - min.Y);
        if (size <= 0f)
            return;

        var rounding = ResolveRounding(min, max, GameIconShape.Standard);
        var background = ResolveBackground();

        if (shadow)
            DrawShadow(drawList, min, max, rounding);
        drawList.AddRectFilled(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(background),
            rounding);

        if (texture != null && texture.Handle != nint.Zero)
        {
            drawList.AddImageRounded(
                texture.Handle,
                min,
                max,
                Vector2.Zero,
                Vector2.One,
                ImGui.ColorConvertFloat4ToU32(Vector4.One),
                rounding,
                ImDrawFlags.RoundCornersAll);
        }

        DrawResolvedInteractionFilter(drawList, min, max, rounding, visualMode);
    }

    internal static void DrawInteractionFilter(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        GameIconShape shape,
        ControlVisualMode visualMode)
    {
        (min, max) = ResolvePixelAlignedRect(min, max);
        var size = MathF.Min(max.X - min.X, max.Y - min.Y);
        if (size <= 0f)
            return;

        var rounding = ResolveRounding(min, max, shape);
        DrawResolvedInteractionFilter(drawList, min, max, rounding, visualMode);
    }

    private static void DrawResolvedInteractionFilter(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding,
        ControlVisualMode visualMode)
    {
        if (ResolveInteractionFilter(visualMode) is { } interactionFilter)
            DrawColorOverlay(drawList, min, max, rounding, interactionFilter, 1f);
    }

    private static Vector4? ResolveInteractionFilter(ControlVisualMode visualMode) =>
        visualMode switch
        {
            ControlVisualMode.Hovered => new Vector4(
                1f,
                1f,
                1f,
                HoverFilterAlpha),
            ControlVisualMode.Active => new Vector4(
                0f,
                0f,
                0f,
                ActiveFilterAlpha),
            ControlVisualMode.Disabled => new Vector4(
                0f,
                0f,
                0f,
                DisabledFilterAlpha),
            _ => null,
        };

    private static void DrawColorOverlay(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding,
        Vector4 color,
        float alphaMultiplier)
    {
        var overlay = color with { W = color.W * alphaMultiplier };
        if (overlay.W <= 0f)
            return;

        drawList.AddRectFilled(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(overlay),
            rounding);
    }

    public static float ResolveRounding(
        Vector2 min,
        Vector2 max,
        GameIconShape shape)
    {
        var radius = SlapCorners.MaxForRect(min, max);
        var requestedRounding = SlapCorners.MediaRadius;
        // ImGui clamps all-corner rounding to half the short side minus 1 px.
        var rounding = shape == GameIconShape.Circle ? radius : requestedRounding;
        return rounding >= MathF.Max(0f, radius - CircleRoundingGuardBand)
            ? radius
            : Math.Clamp(rounding, 0f, radius);
    }

    private static Vector4 ResolveBackground()
    {
        return ThemeScope.Resolved.Surface;
    }

    private static (Vector2 Position, float UnderlayOffset, float Size) ResolveCornerCheckLayout(
        Vector2 min,
        Vector2 max)
    {
        var iconSize = MathF.Min(max.X - min.X, max.Y - min.Y);
        var inset = MathF.Max(
            0f,
            MathF.Round(MetricsScope.Scale(CornerCheckInset), MidpointRounding.AwayFromZero));
        var underlayOffset = MathF.Max(
            1f,
            MathF.Round(MetricsScope.Scale(CornerCheckUnderlayOffset), MidpointRounding.AwayFromZero));
        var availableSize = iconSize - (inset + underlayOffset) * 2f;
        if (availableSize < 1f)
            return (Vector2.Zero, 0f, 0f);

        var checkSize = MathF.Min(
            availableSize,
            MathF.Max(
                1f,
                MathF.Round(iconSize * CornerCheckSizeRatio, MidpointRounding.AwayFromZero)));
        var position = new Vector2(
            MathF.Round(max.X - inset - underlayOffset - checkSize, MidpointRounding.AwayFromZero),
            MathF.Round(max.Y - inset - underlayOffset - checkSize, MidpointRounding.AwayFromZero));
        return (position, underlayOffset, checkSize);
    }

    private static void DrawCornerCheck(
        ImDrawListPtr drawList,
        Vector2 position,
        float underlayOffset,
        float size,
        ControlVisualMode visualMode)
    {
        var disabled = visualMode == ControlVisualMode.Disabled;
        var underlayColor = SlapColor.WithDisabledAlpha(CornerCheckDefaultUnderlayColor, disabled);
        var checkColor = SlapColor.WithDisabledAlpha(CornerCheckDefaultColor, disabled);
        var underlayColorU32 = ImGui.ColorConvertFloat4ToU32(underlayColor);

        foreach (var direction in CornerCheckUnderlayDirections)
        {
            ImGuiP.RenderCheckMark(
                drawList,
                position + direction * underlayOffset,
                underlayColorU32,
                size);
        }

        ImGuiP.RenderCheckMark(
            drawList,
            position,
            ImGui.ColorConvertFloat4ToU32(checkColor),
            size);
    }

    // --- Multi-layer embossed shadow (ported from V2 DrawGameIconShadow) ---

    private static void DrawShadow(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding)
    {
        var color = new Vector4(0f, 0f, 0f, BottomShadowAlpha);
        var solid = ImGui.ColorConvertFloat4ToU32(color);
        var topShadowColor = ImGui.ColorConvertFloat4ToU32(color with { W = TopShadowAlpha });
        var sideTopColor = ImGui.ColorConvertFloat4ToU32(color with { W = SideShadowTopAlpha });
        var shadowHeight = MetricsScope.Scale(BottomShadowHeight);
        var sideWidth = SideShadowWidth;

        if (shadowHeight > 0f)
        {
            var shadowRounding = rounding + MathF.Max(sideWidth, shadowHeight);
            var clipMinY = min.Y - shadowHeight - shadowRounding;
            var clipMaxY = max.Y + shadowHeight + shadowRounding;
            DrawTopShadow(drawList, min, max, topShadowColor, rounding, shadowHeight, sideWidth, shadowRounding, clipMinY, clipMaxY);
            DrawBottomShadow(drawList, min, max, solid, rounding, shadowHeight, sideWidth, shadowRounding, clipMinY, clipMaxY);
        }

        if (sideWidth <= 0f)
            return;

        const float sideOverlap = 1f;
        var sideStartY = MathF.Min(max.Y, min.Y + rounding);
        var sideEndY = MathF.Max(sideStartY, max.Y - rounding);
        var sideMinY = sideStartY - sideOverlap;
        var sideMaxY = sideEndY + sideOverlap;
        var sideClipMinY = min.Y - shadowHeight - rounding - MathF.Max(sideWidth, shadowHeight);
        var sideClipMaxY = max.Y + shadowHeight + rounding + MathF.Max(sideWidth, shadowHeight);
        drawList.PushClipRect(
            new Vector2(min.X - sideWidth, sideClipMinY),
            new Vector2(MathF.Ceiling(min.X + sideOverlap), sideClipMaxY),
            true);
        drawList.AddRectFilledMultiColor(
            new Vector2(min.X - sideWidth, sideMinY),
            new Vector2(MathF.Min(max.X, min.X + sideOverlap), sideMaxY),
            sideTopColor,
            sideTopColor,
            solid,
            solid);
        drawList.PopClipRect();
        drawList.PushClipRect(
            new Vector2(MathF.Floor(max.X - sideOverlap), sideClipMinY),
            new Vector2(max.X + sideWidth, sideClipMaxY),
            true);
        drawList.AddRectFilledMultiColor(
            new Vector2(MathF.Max(min.X, max.X - sideOverlap), sideMinY),
            new Vector2(max.X + sideWidth, sideMaxY),
            sideTopColor,
            sideTopColor,
            solid,
            solid);
        drawList.PopClipRect();
    }

    private static void DrawTopShadow(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        uint color,
        float cornerRadius,
        float height,
        float sideWidth,
        float shadowRounding,
        float clipMinY,
        float clipMaxY)
    {
        if (height <= 0f)
            return;

        var shadowMin = new Vector2(min.X - sideWidth, min.Y - height);
        var shadowMax = new Vector2(max.X + sideWidth, MathF.Min(max.Y, min.Y + cornerRadius));
        drawList.PushClipRect(
            new Vector2(shadowMin.X, clipMinY),
            new Vector2(shadowMax.X, clipMaxY),
            true);
        drawList.AddRectFilled(
            shadowMin,
            shadowMax,
            color,
            shadowRounding,
            ImDrawFlags.RoundCornersTop);
        drawList.PopClipRect();
    }

    private static void DrawBottomShadow(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        uint color,
        float cornerRadius,
        float height,
        float sideWidth,
        float shadowRounding,
        float clipMinY,
        float clipMaxY)
    {
        if (height <= 0f)
            return;

        var shadowMin = new Vector2(min.X - sideWidth, MathF.Max(min.Y, max.Y - cornerRadius));
        var shadowMax = new Vector2(max.X + sideWidth, max.Y + height);
        drawList.PushClipRect(
            new Vector2(shadowMin.X, clipMinY),
            new Vector2(shadowMax.X, clipMaxY),
            true);
        drawList.AddRectFilled(
            shadowMin,
            shadowMax,
            color,
            shadowRounding,
            ImDrawFlags.RoundCornersBottom);
        drawList.PopClipRect();
    }

}
