namespace SlapSurface;

/// <summary>
/// Standard content indentation levels for SettingRow, expressed as
/// multiples of <see cref="Metrics.MetricsScope.UnitWidth"/>.
/// Use with <c>SettingRowSpec.IndentUnits</c> / <c>SettingCheckboxRowSpec.IndentUnits</c> /
/// <c>SettingComboRowSpec.IndentUnits</c> / <c>SettingActionRowSpec.IndentUnits</c>.
/// </summary>
internal static class SettingIndent
{
    /// <summary>Level 1 — half a unit width (0.5×), like content under a sub-title.</summary>
    public const float Level1 = 0.5f;

    /// <summary>Level 2 — one unit width (1.0×).</summary>
    public const float Level2 = 1.0f;

    /// <summary>Level 3 — 1.5 unit widths (1.5×).</summary>
    public const float Level3 = 1.5f;
}
