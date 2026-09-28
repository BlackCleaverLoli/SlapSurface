using System.Collections.Generic;
using Dalamud.Bindings.ImGui;

namespace SlapSurface;

/// <summary>
/// Font-aware text wrapping measured with the ImGui font active at the call
/// site. Callers wrap the call in the desired <see cref="Slap.PushFont"/>
/// scope so widths match the rendered glyphs.
/// </summary>
internal static class TextWrapUtility
{
    /// <summary>
    /// Length of the longest prefix of <paramref name="text"/> that fits
    /// within <paramref name="maxWidth"/>. When
    /// <paramref name="preferWordBoundary"/> is true and the full text does
    /// not fit, the result backs off to the last space/tab inside the fitted
    /// prefix so Latin text is not cut mid-word; CJK text breaks per char.
    /// </summary>
    internal static int FitPrefixLength(
        string text,
        float maxWidth,
        bool preferWordBoundary = false)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 0f)
            return 0;

        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = low + (high - low + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }

        if (!preferWordBoundary || low >= text.Length)
            return low;

        var lastSpace = text.LastIndexOfAny([' ', '\t'], low - 1);
        return lastSpace > 0 ? lastSpace : low;
    }

    /// <summary>
    /// Wraps <paramref name="text"/> into lines each fitting within
    /// <paramref name="maxWidth"/>. Explicit line breaks and empty lines are
    /// preserved.
    /// </summary>
    internal static List<string> Wrap(
        string text,
        float maxWidth,
        bool preferWordBoundary = false)
    {
        var lines = new List<string>();
        var segments = text.Replace("\r\n", "\n").Split('\n');
        foreach (var segment in segments)
        {
            if (segment.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var remaining = segment;
            while (remaining.Length > 0)
            {
                var fit = FitPrefixLength(remaining, maxWidth, preferWordBoundary);
                if (fit <= 0)
                    fit = 1;
                lines.Add(remaining[..fit]);
                remaining = remaining[fit..];
            }
        }

        return lines;
    }
}
