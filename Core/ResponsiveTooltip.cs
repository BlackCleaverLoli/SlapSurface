namespace SlapSurface;

internal static class ResponsiveTooltip
{
    internal static string? BuildDisplayLabel(string? label, string? tooltip)
    {
        if (string.IsNullOrWhiteSpace(label))
            return label;

        return !string.IsNullOrWhiteSpace(tooltip) ? $"{label} (?)" : label;
    }

    internal static string? Compose(bool degraded, string? displayedText, string? tooltip)
    {
        if (!degraded || string.IsNullOrWhiteSpace(displayedText))
            return tooltip;
        if (string.IsNullOrEmpty(tooltip))
            return displayedText;

        var label = displayedText.Trim();
        foreach (var line in tooltip.Split('\n'))
        {
            if (string.Equals(line.Trim(), label))
                return tooltip;
        }

        return $"{displayedText}\n{tooltip}";
    }
}
