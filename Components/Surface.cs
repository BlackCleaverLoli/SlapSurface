using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal readonly record struct SurfaceSpec(
    ControlKey Key,
    SurfaceSize Size = default,
    ControlState State = ControlState.None,
    LayeredShadowSpec Shadow = default,
    Vector2? Padding = null
);

internal readonly record struct SurfaceContentContext(
    Vector2 Min,
    Vector2 Max,
    Vector2 ContentMin,
    Vector2 ContentMax,
    Vector4 Background
)
{
    public Vector2 Size => Max - Min;
    public Vector2 ContentSize => Vector2.Max(Vector2.Zero, ContentMax - ContentMin);
}

internal static class SurfaceComponent
{
    private static readonly Vector2 DefaultPadding = new(SlapPx.Space12, SlapPx.Space8);
    private const float TransparentSecondaryBorderAlpha = 0.5f;
    private const float PopupFrostedOverlayAlpha = 0.24f;
    private const float PopupFrostedFillAlpha = 0.33f;

    public static void Draw(SurfaceSpec spec, Action<SurfaceContentContext> drawContent)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("SurfaceSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        try
        {
            var surfaceSize = ResolveSurfaceSize(spec.Size);

            ImGui.Dummy(surfaceSize);
            using var cursor = new CursorScope();
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var background = ResolveVisualBlockBackground(spec.State);
            var rounding = SlapCorners.ControlRadius;
            var drawList = ImGui.GetWindowDrawList();

            LayeredShadow.Draw(
                drawList,
                min,
                max,
                rounding,
                spec.Shadow);
            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(background), rounding);

            var padding = MetricsScope.ScalePadding(spec.Padding ?? DefaultPadding);
            var contentMin = min + padding;
            var contentMax = max - padding;
            var context = new SurfaceContentContext(
                min,
                max,
                contentMin,
                contentMax,
                background);

            if (contentMax.X > contentMin.X && contentMax.Y > contentMin.Y)
            {
                ImGui.SetCursorScreenPos(contentMin);
                drawContent(context);
            }

        } finally
        {
            ImGui.PopID();
        }
    }

    internal static Vector2 ResolveSurfaceSize(SurfaceSize size)
    {
        var available = ImGui.GetContentRegionAvail();
        var width = MathF.Max(0f, available.X);
        var unit = MetricsScope.UnitHeight;
        return size.Kind switch
        {
            SurfaceSizeKind.Auto => new Vector2(width, unit),
            SurfaceSizeKind.FillWidth => new Vector2(width, unit * size.HeightUnits),
            SurfaceSizeKind.FillAvailable => Vector2.Max(Vector2.Zero, available),
            _ => new Vector2(width, unit)
        };
    }

    internal static Vector4 ResolveColors(ControlState state)
    {
        var rt = ThemeScope.Resolved;
        var disabled = state.HasFlag(ControlState.Disabled);

        var background = rt.Surface;
        if (disabled)
            background = SlapColor.WithDisabledAlpha(background, disabled);

        return background;
    }

    /// <summary>
    /// Background for a main-window visual block. Transparent themes leave the
    /// block transparent so the full-window surface layer is not duplicated.
    /// </summary>
    internal static Vector4 ResolveVisualBlockBackground(ControlState state)
    {
        var rt = ThemeScope.Resolved;
        if (rt.IsTransparentTheme)
            return Vector4.Zero;

        return ResolveColors(state);
    }

    /// <summary>
    /// Resolve background, border, and text for a control frame from its
    /// palette, interaction state, and optional semantic accent. Semantic
    /// accents tint text and border from the same <see cref="TextSlot"/>;
    /// disabled state always wins over hover/active.
    /// </summary>
    internal static SurfaceColors ResolveControlColors(
        ControlPalette palette,
        ControlState state,
        TextSlot? semantic = null,
        bool hovered = false,
        bool active = false,
        BorderStrength strength = BorderStrength.Standard,
        Variant variant = Variant.Base)
    {
        var rt = ThemeScope.Resolved;
        var disabled = state.HasFlag(ControlState.Disabled);

        var background = disabled ? palette.Disabled
            : active ? palette.Active
            : hovered ? palette.Hovered
            : palette.Base;

        var semanticAccent = semantic.HasValue ? rt.GetTextSlot(semantic.Value) : (Vector4?)null;
        var text = SlapColor.WithDisabledAlpha(semanticAccent ?? palette.Text, disabled);
        var border = ResolveVariantBorder(rt, variant, palette, strength, semanticAccent, disabled);

        return new SurfaceColors(background, border, text);
    }

    private static Vector4 ResolveVariantBorder(
        ResolvedTheme rt,
        Variant variant,
        ControlPalette palette,
        BorderStrength strength,
        Vector4? semanticAccent,
        bool disabled)
    {
        if (semanticAccent.HasValue)
            return SlapColor.WithDisabledAlpha(semanticAccent.Value, disabled);

        if (strength == BorderStrength.Subtle)
            return SlapColor.WithDisabledAlpha(palette.BorderSubtle, disabled);

        if (rt.IsTransparentTheme)
        {
            if (variant == Variant.Action)
                return SlapColor.WithDisabledAlpha(rt.Border, disabled);
            if (variant is Variant.Base or Variant.Flat or Variant.FlatNoBorder)
                return SlapColor.WithDisabledAlpha(
                    SlapColor.WithAlpha(rt.Border, TransparentSecondaryBorderAlpha),
                    disabled);
        }

        var borderColor = strength switch
        {
            BorderStrength.Subtle => palette.BorderSubtle,
            BorderStrength.Strong => palette.BorderStrong,
            _ => palette.Border,
        };
        return SlapColor.WithDisabledAlpha(borderColor, disabled);
    }

    /// <summary>
    /// Border for dense list rows. Transparent themes use the neutral border
    /// color; other themes keep the subtle palette border.
    /// </summary>
    internal static Vector4 ResolveListRowBorder(ControlPalette palette, bool disabled)
    {
        var rt = ThemeScope.Resolved;
        if (rt.IsTransparentTheme)
            return SlapColor.WithDisabledAlpha(rt.Border, disabled);

        return SlapColor.WithDisabledAlpha(palette.BorderSubtle, disabled);
    }

    /// <summary>
    /// Resolve the background for a transparent flat surface item whose base is
    /// the panel surface: hover lightens, active darkens, disabled dims.
    /// Shared by sidebar items, identity tabs and tab-strip tabs.
    /// </summary>
    internal static Vector4 ResolveFlatSurfaceBackground(bool hovered, bool active, bool disabled)
    {
        var palette = ThemeScope.Resolved.GetButtonPalette(Variant.Flat);
        return disabled ? palette.Disabled
            : active ? palette.Active
            : hovered ? palette.Hovered
            : palette.Base;
    }

    /// <summary>
    /// Border strength for a control that follows its variant: Flat variants
    /// use the strong chip border; other variants use the standard frame
    /// border. List rows and badges resolve their own strengths explicitly.
    /// </summary>
    internal static BorderStrength ResolveBorderStrength(Variant variant) =>
        variant == Variant.Flat ? BorderStrength.Strong : BorderStrength.Standard;

    /// <summary>
    /// Draw a control family's frame border.
    /// <see cref="Variant.FlatNoBorder"/> draws no border; other variants use
    /// the standard border thickness.
    /// </summary>
    internal static void DrawControlBorder(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        Variant variant,
        Vector4 borderColor,
        float rounding)
    {
        if (variant == Variant.FlatNoBorder)
            return;

        drawList.AddRect(
            min,
            max,
            ImGui.ColorConvertFloat4ToU32(borderColor),
            rounding,
            ImDrawFlags.RoundCornersAll,
            MetricsScope.BorderThickness);
    }

    /// <summary>
    /// Draw a border inset by half its thickness so it is not clipped by the
    /// owning window/child edge.
    /// </summary>
    internal static void DrawPanelBorder(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        Vector4 color,
        float rounding)
    {
        var thickness = MetricsScope.BorderThickness;
        if (max.X <= min.X || max.Y <= min.Y)
            return;

        var displaySize = ImGui.GetIO().DisplaySize;
        drawList.PushClipRect(Vector2.Zero, displaySize, false);
        try
        {
            drawList.AddRect(
                min,
                max,
                ImGui.ColorConvertFloat4ToU32(color),
                rounding,
                ImDrawFlags.RoundCornersAll,
                thickness);
        }
        finally
        {
            drawList.PopClipRect();
        }
    }

    /// <summary>
    /// Draw a frosted backdrop behind a popup/overlay panel on transparent
    /// themes. Non-transparent themes intentionally do not draw this layer.
    /// </summary>
    internal static void DrawFrostedPanelBackground(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding)
    {
        if (!ThemeScope.Resolved.IsTransparentTheme)
            return;

        var rt = ThemeScope.Resolved;
        var overlay = SlapColor.WithAlpha(
            rt.IsLightSurface ? Vector4.One : new Vector4(0f, 0f, 0f, 1f),
            PopupFrostedOverlayAlpha);
        var fill = SlapColor.WithAlpha(SlapColor.Desaturate(rt.Surface), PopupFrostedFillAlpha);
        DrawFrostedRect(drawList, min, max, rounding, fill, overlay);
    }

    /// <summary>
    /// Frosted backdrop that matches the owner window's surface composite
    /// (the window's overlay alpha and desaturated surface fill). In-window
    /// overlays such as the collapsing floating header should use this so they
    /// read the same shade as the window instead of the deeper popup scrim.
    /// </summary>
    internal static void DrawWindowFrostedBackdrop(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding)
    {
        if (!ThemeScope.Resolved.IsTransparentTheme)
            return;

        var rt = ThemeScope.Resolved;
        var overlay = SlapColor.WithAlpha(
            rt.IsLightSurface ? Vector4.One : new Vector4(0f, 0f, 0f, 1f),
            SlapWindowFrameDefaults.DefaultFrostedOverlayAlpha);
        var fill = SlapColor.WithAlpha(
            SlapColor.Desaturate(rt.Surface),
            SlapWindowFrameDefaults.DefaultFrostedFillAlpha);
        DrawFrostedRect(drawList, min, max, rounding, fill, overlay);
    }

    private static void DrawFrostedRect(
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        float rounding,
        Vector4 fill,
        Vector4 overlay)
    {
        if (Slap.FrostedBackground is not { } frosted)
            return;

        var displaySize = ImGui.GetIO().DisplaySize;
        drawList.PushClipRect(Vector2.Zero, displaySize, false);
        try
        {
            frosted(drawList, min, max, rounding, ImDrawFlags.RoundCornersAll, overlay);
            drawList.AddRectFilled(
                min,
                max,
                ImGui.ColorConvertFloat4ToU32(fill),
                rounding,
                ImDrawFlags.RoundCornersAll);
        }
        finally
        {
            drawList.PopClipRect();
        }
    }
}

internal readonly record struct SurfaceColors(Vector4 Background, Vector4 Border, Vector4 Text);
