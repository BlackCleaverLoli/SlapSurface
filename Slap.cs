using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
namespace SlapSurface;

internal static partial class Slap
{
    private static Action<ImDrawListPtr, Vector2, Vector2, float, ImDrawFlags, Vector4>? _frostedBackground;
    private static Func<uint, bool, IDalamudTextureWrap?>? _gameIconLoader;
    /// <summary>Current frame unit height in pixels, adjusted by scale and density.</summary>
    public static float UnitHeight => Metrics.MetricsScope.UnitHeight;

    /// <summary>Current width unit in pixels, adjusted by scale and density.</summary>
    /// <remarks>V2: <c>UnitWidth = 70f</c>. Use for control width calculations.
    /// <see cref="UnitHeight"/> (V2 <c>UnitHeight = 38f</c>) is for height.</remarks>
    public static float UnitWidth => Metrics.MetricsScope.UnitWidth;

    /// <summary>Current theme corner radius in scaled pixels.</summary>
    public static float CornerRadius => Theme.SlapCorners.ControlRadius;

    /// <summary>Current media (game icon / texture) corner radius in scaled pixels.</summary>
    public static float MediaCornerRadius => Theme.SlapCorners.MediaRadius;

    /// <summary>Scale an unscaled pixel value by the current metrics scale factor.</summary>
    public static float Scale(float value) => Metrics.MetricsScope.Scale(value);

    /// <summary>
    /// Register the global icon font used by all SlapSurface components
    /// for <see cref="Dalamud.Interface.FontAwesomeIcon"/> rendering.
    /// Call once during host initialization or in PreDraw
    /// (e.g. <c>Slap.RegisterIconFont(UiBuilder.IconFont)</c>).
    /// </summary>
    public static void RegisterIconFont(ImFontPtr font) => SlapIcon.RegisterFont(font);

    /// <summary>
    /// Clear the host-registered icon font. Call when the host releases its
    /// font lock or cannot re-acquire it, so components fall back to not
    /// pushing an icon font instead of pushing a stale pointer.
    /// </summary>
    public static void UnregisterIconFont() => SlapIcon.UnregisterFont();

    /// <summary>
    /// Register a host-provided frosted background renderer for popup/overlay
    /// panels. The callback receives screen-space bounds, corner rounding,
    /// corner flags, and an optional overlay color.
    /// </summary>
    public static void RegisterFrostedBackground(
        Action<ImDrawListPtr, Vector2, Vector2, float, ImDrawFlags, Vector4> draw) =>
        _frostedBackground = draw;

    internal static Action<ImDrawListPtr, Vector2, Vector2, float, ImDrawFlags, Vector4>? FrostedBackground =>
        _frostedBackground;

    /// <summary>
    /// Register a host-provided game icon loader used by <c>GameIconId</c>
    /// components. The callback returns a drawable texture wrap for a game
    /// icon id, or null when unavailable.
    /// </summary>
    public static void RegisterGameIconLoader(
        Func<uint, bool, IDalamudTextureWrap?> loader) =>
        _gameIconLoader = loader;

    internal static Func<uint, bool, IDalamudTextureWrap?>? GameIconLoader =>
        _gameIconLoader;
    /// <summary>
    /// Resolve a <see cref="FontAwesomeIcon"/>? to (iconText, iconFont).
    /// Returns (null, null) when <paramref name="icon"/> is null.
    /// Use to draw FA icons directly with <c>drawList.AddText</c>.
    /// </summary>
    public static (string? Text, ImFontPtr? Font) ResolveIcon(FontAwesomeIcon? icon) => SlapIcon.Resolve(icon);

    public static IDisposable PushTheme(SlapTheme theme) => Theme.ThemeScope.Push(theme);

    /// <summary>Current theme seed; falls back to the ambient ImGui style when no theme is pushed.</summary>
    public static SlapTheme CurrentTheme => Theme.ThemeScope.Current;

    /// <summary>Current resolved neutral border / hairline color.</summary>
    public static Vector4 Border => Theme.ThemeScope.Resolved.Border;

    public static IDisposable PushMetrics(SlapMetricsConfig config) => Metrics.MetricsScope.Push(config);

    public static IDisposable PushTypography(
        SlapTypographySpec spec,
        SlapFontSize size = SlapFontSize.Regular,
        SlapFontWeight weight = SlapFontWeight.Regular) =>
        TypographyScope.Push(spec, size, weight);

    /// <summary>
    /// Push a font using the currently active typography spec.
    /// Useful for page code that needs to switch font size/weight inside
    /// SlapSurface component content callbacks.
    /// </summary>
    public static IDisposable PushFont(
        SlapFontSize size = SlapFontSize.Regular,
        SlapFontWeight weight = SlapFontWeight.Regular) =>
        TypographyScope.PushCurrent(size, weight);
}
