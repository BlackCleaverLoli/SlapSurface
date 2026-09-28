using System;
using System.Numerics;

namespace SlapSurface.Metrics;

internal static class MetricsScope
{
    private const float BaseUnitHeight = 38f;
    private const float BaseUnitWidth = 70f;
    private static SlapMetricsConfig _current = new();

    public static float UnitHeight => BaseUnitHeight * _current.Scale * _current.Density;

    internal static float ResolveUnitHeight(SlapMetricsConfig config)
    {
        var normalized = Normalize(config);
        return BaseUnitHeight * normalized.Scale * normalized.Density;
    }

    /// <summary>
    /// Width unit in pixels, adjusted by scale and density.
    /// V2: <c>UnitWidth = 70f</c>. Used for control width calculations where
    /// <see cref="UnitHeight"/> (V2 <c>UnitHeight = 38f</c>) would be too narrow.
    /// </summary>
    public static float UnitWidth => BaseUnitWidth * _current.Scale * _current.Density;

    public static float FadeWidth => ScaleGap(SlapPx.Space8);
    public static float BorderThickness => ScaleBorder(SlapPx.Space1);

    /// <summary>
    /// Icon slot width derived from <see cref="UnitHeight"/>.
    /// Both centered and left-aligned layouts use 1×.
    /// </summary>
    public static float ResolveIconSlotWidth(bool centered) =>
        UnitHeight;

    public static float Scale(float value) => value * _current.Scale;
    public static Vector2 Scale(Vector2 value) => value * _current.Scale;
    public static float ScalePadding(float value) => Scale(value) * _current.PaddingScale;
    public static Vector2 ScalePadding(Vector2 value) => Scale(value) * _current.PaddingScale;
    public static float ScaleGap(float value) => Scale(value) * _current.GapScale;
    public static float ScaleBorder(float value) => Scale(value) * _current.BorderScale;
    /// <summary>
    /// Scale a width expressed in height-based units by the current UI scale
    /// only (no density), matching window-level width budgets such as the
    /// shell sidebar collapse threshold.
    /// </summary>
    internal static float ScaleHeightUnits(float units) => Scale(BaseUnitHeight * units);

    internal static IDisposable Push(SlapMetricsConfig config)
    {
        var previous = _current;
        _current = Normalize(config);
        return new Scope(previous);
    }

    private static SlapMetricsConfig Normalize(SlapMetricsConfig config)
    {
        return new SlapMetricsConfig(
            NormalizeFactor(config.Scale),
            NormalizeFactor(config.Density),
            NormalizeFactor(config.PaddingScale),
            NormalizeFactor(config.GapScale),
            NormalizeFactor(config.BorderScale));
    }

    private static float NormalizeFactor(float value) => value > 0f && !float.IsInfinity(value) ? value : 1f;

    private readonly struct Scope : IDisposable
    {
        private readonly SlapMetricsConfig _previous;
        public Scope(SlapMetricsConfig previous) => _previous = previous;
        public void Dispose() => _current = _previous;
    }
}
