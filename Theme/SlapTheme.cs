using System.Numerics;

namespace SlapSurface;

/// <summary>
/// Semantic seed theme. Callers provide design tokens; SlapSurface derives
/// hover, active, and disabled state colors internally.
/// <para>
/// Four control color groups (<see cref="BaseFill"/>/<see cref="BaseText"/>,
/// <see cref="ActionFill"/>/<see cref="ActionText"/>, etc.) define the
/// structural appearance of <see cref="Variant"/> controls. Four
/// <see cref="TextSlot"/> values provide semantic accents for text and
/// borders without adding more variants.
/// </para>
/// </summary>
internal readonly record struct SlapTheme(
    // ── Surface ──
    Vector4 SurfaceBg,
    Vector4 TitleBarBg,

    // ── Control color groups ──
    Vector4 BaseFill,
    Vector4 BaseText,
    Vector4 ActionFill,
    Vector4 ActionText,
    Vector4 SidebarFill,
    Vector4 SidebarText,
    Vector4 TabFill,
    Vector4 TabText,

    // ── Input control colors ──
    Vector4 InputFill,
    Vector4 InputText,
    Vector4 InputTextAlt,

    // ── Semantic text slots ──
    Vector4 Body,
    Vector4 Subtle,
    Vector4 Emphasis,
    Vector4 Danger,

    // ── Other ──
    Vector4 Border,
    /// <summary>
    /// Requested corner radius as a fraction of the current control unit height.
    /// The default <c>0.5</c> produces maximum rounding for one-unit controls;
    /// use <c>0</c> for square corners.
    /// </summary>
    float CornerRadiusUnits = 0.5f,
    /// <summary>
    /// Marks a translucent (frosted/clear) theme. Solid-fill primitives use
    /// <see cref="AccentPrimary"/> (and optionally <see cref="AccentSecondary"/>)
    /// and <see cref="Body"/> instead of deriving from the semi-transparent surface.
    /// </summary>
    bool IsTransparentTheme = false,
    /// <summary>Primary opaque accent color for solid-fill primitives on transparent themes.</summary>
    Vector4 AccentPrimary = default,
    /// <summary>Secondary opaque accent color; reserved until a consumer is defined.</summary>
    Vector4 AccentSecondary = default
)
{
    public const float DefaultCornerRadiusUnits = 0.5f;
}
