using SlapSurface.Metrics;

namespace SlapSurface;

/// <summary>
/// Canonical spacing tokens in unscaled pixels. Pass through
/// <see cref="MetricsScope.ScalePadding(float)"/> or
/// <see cref="MetricsScope.ScaleGap(float)"/> to get scaled values.
/// </summary>
internal static class SlapPx
{
    public const float Space1 = 1f;
    public const float Space2 = 2f;
    public const float Space3 = 3f;
    public const float Space4 = 4f;
    public const float Space5 = 5f;
    public const float Space6 = 6f;
    public const float Space7 = 7f;
    public const float Space8 = 8f;
    public const float Space9 = 9f;
    public const float Space10 = 10f;
    public const float Space12 = 12f;
    public const float Space16 = 16f;
    public const float Space24 = 24f;
    public const float Space36 = 36f;

    /// <summary>
    /// Ratio of game icon size to SurfaceList row height. Applied to the
    /// full row height (not content area) so that icon fill is consistent
    /// regardless of row padding configuration.
    /// </summary>
    public const float RowIconFillRatio = 0.75f;
}
