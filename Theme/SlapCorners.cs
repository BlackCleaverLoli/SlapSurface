using System;
using System.Numerics;
using SlapSurface.Metrics;

namespace SlapSurface.Theme;

internal static class SlapCorners
{
    private const float DefaultMediaRadiusUnits = 1f / 3f;

    internal static float ControlRadius =>
        Resolve(ThemeScope.Resolved.CornerRadiusUnits, MetricsScope.UnitHeight);

    internal static float MediaRadius =>
        MathF.Min(
            ControlRadius,
            Resolve(DefaultMediaRadiusUnits, MetricsScope.UnitHeight));

    internal static bool Enabled => ControlRadius > 0f;

    internal static float Resolve(SlapTheme theme, SlapMetricsConfig metrics) =>
        Resolve(theme.CornerRadiusUnits, MetricsScope.ResolveUnitHeight(metrics));

    internal static float ForRect(Vector2 min, Vector2 max) =>
        MathF.Min(ControlRadius, MaxForRect(min, max));

    internal static float MaxForRect(Vector2 min, Vector2 max) =>
        MathF.Max(0f, MathF.Min(max.X - min.X, max.Y - min.Y) * 0.5f);

    private static float Resolve(float radiusUnits, float unitHeight)
    {
        var normalizedUnits = radiusUnits >= 0f && !float.IsInfinity(radiusUnits)
            ? radiusUnits
            : 0f;
        return normalizedUnits * MathF.Max(0f, unitHeight);
    }
}
