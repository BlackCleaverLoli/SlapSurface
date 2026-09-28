namespace SlapSurface;

internal readonly record struct SlapMetricsConfig(
    float Scale = 1f,
    float Density = 1f,
    float PaddingScale = 1f,
    float GapScale = 1f,
    float BorderScale = 1f
);
