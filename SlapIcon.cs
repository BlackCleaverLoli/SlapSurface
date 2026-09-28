using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Utility;

namespace SlapSurface;

/// <summary>
/// Global icon font registration and FontAwesomeIcon resolver.
/// The host registers the Dalamud icon font once (e.g. in PreDraw or init);
/// all SlapSurface components use it internally when rendering
/// <see cref="FontAwesomeIcon"/>-based icons.
/// </summary>
internal static class SlapIcon
{
    private static ImFontPtr? _registeredFont;

    public static void RegisterFont(ImFontPtr font) => _registeredFont = font;

    /// <summary>
    /// Clear the registered icon font so no stale pointer survives after the
    /// host releases its font lock or fails to re-acquire it.
    /// </summary>
    public static void UnregisterFont() => _registeredFont = null;

    /// <summary>
    /// Resolve a <see cref="FontAwesomeIcon"/>? to (iconText, iconFont).
    /// Returns (null, null) when <paramref name="icon"/> is null.
    /// </summary>
    internal static (string? Text, ImFontPtr? Font) Resolve(FontAwesomeIcon? icon)
    {
        if (!icon.HasValue)
            return (null, null);
        return (icon.Value.ToIconString(), _registeredFont);
    }

    /// <summary>
    /// Horizontal offset that centers a single-icon glyph's visible ink within
    /// a slot of <paramref name="slotWidth"/>. Uses the glyph's actual ink bounds
    /// rather than its advance box, so fixed-width fonts with asymmetric side
    /// bearings still center optically.
    /// </summary>
    internal static unsafe float ResolveHorizontalCenteringOffset(
        ImFontPtr? font,
        string iconText,
        float slotWidth)
    {
        if (!font.HasValue || string.IsNullOrEmpty(iconText) || slotWidth <= 0f)
            return 0f;

        var resolvedFont = font.Value;
        ImFontGlyphPtr glyph = resolvedFont.FindGlyphNoFallback((ushort)iconText[0]);

        var emSize = resolvedFont.FontSize;
        if (emSize <= 0f)
            emSize = 1f;
        float renderScale;
        ImGui.PushFont(resolvedFont);
        try
        {
            renderScale = ImGui.GetFontSize() / emSize;
        }
        finally
        {
            ImGui.PopFont();
        }

        if (glyph.IsNull)
            return (slotWidth - resolvedFont.GetCharAdvance((ushort)iconText[0]) * renderScale) * 0.5f;

        return slotWidth * 0.5f - (glyph.X0 + glyph.X1) * 0.5f * renderScale;
    }
}
