using System.Numerics;

namespace SlapSurface.Theme;

/// <summary>
/// Internal derived theme. Built from <see cref="SlapTheme"/> seed;
/// control palettes (fill, text, hover, active, disabled, border) are
/// pre-computed per <see cref="Variant"/>. Text slots and surface
/// derivations are direct passthrough or simple Mix/alpha formulas.
/// </summary>
internal readonly record struct ResolvedTheme
{
    // ── Palette border derivation ──
    private const float BorderMixRatio = 0.35f;
    private const float SubtleBorderAlpha = 0.50f;
    private const float BorderAlpha = 0.75f;

    // ── Surface / neutral border ──
    public Vector4 Surface { get; init; }
    public Vector4 Border { get; init; }

    // ── Text slots ──
    public Vector4 Body { get; init; }
    public Vector4 Subtle { get; init; }
    public Vector4 Emphasis { get; init; }
    public Vector4 Danger { get; init; }

    // ── Control palettes (one per Variant) ──
    public ControlPalette BasePalette { get; init; }
    public ControlPalette ActionPalette { get; init; }
    public ControlPalette SidebarPalette { get; init; }
    public ControlPalette TabPalette { get; init; }
    public ControlPalette FlatPalette { get; init; }
    public ControlPalette InputPalette { get; init; }

    // ── Visual ──
    public float CornerRadiusUnits { get; init; }
    public bool IsLightSurface { get; init; }
    public bool IsTransparentTheme { get; init; }
    public Vector4 AccentPrimary { get; init; }
    public Vector4 AccentSecondary { get; init; }

    // ── Construction ──────────────────────────────────────

    public static ResolvedTheme FromSeed(SlapTheme seed)
    {
        return new ResolvedTheme
        {
            Surface       = seed.SurfaceBg,
            Border        = seed.Border,

            Body    = seed.Body,
            Subtle = seed.Subtle,
            Emphasis = seed.Emphasis,
            Danger  = seed.Danger,

            BasePalette     = DerivePalette(seed.BaseFill,     seed.BaseText),
            ActionPalette   = DerivePalette(seed.ActionFill,   seed.ActionText),
            SidebarPalette = DerivePalette(seed.SidebarFill, seed.SidebarText),
            TabPalette     = DerivePalette(seed.TabFill,     seed.TabText),
            FlatPalette        = DerivePalette(seed.SurfaceBg,   seed.Body),
            InputPalette   = DerivePalette(seed.InputFill,   seed.InputText),

            CornerRadiusUnits = NormalizeCornerRadiusUnits(seed.CornerRadiusUnits),
            IsLightSurface = SlapColor.PerceivedLightness(seed.SurfaceBg) >= SlapColor.LightSurfaceThreshold,
            IsTransparentTheme = seed.IsTransparentTheme,
            AccentPrimary = seed.AccentPrimary,
            AccentSecondary = seed.AccentSecondary
        };
    }

    // ── Queries ───────────────────────────────────────────

    public ControlPalette GetButtonPalette(Variant variant) => variant switch
    {
        Variant.Action   => ActionPalette,
        Variant.Sidebar => SidebarPalette,
        Variant.Tab     => TabPalette,
        Variant.Flat         => FlatPalette,
        Variant.FlatNoBorder => FlatPalette,
        _                => BasePalette
    };

    /// <summary>Resolve the hover-outline accent. Semantic colors win; otherwise
    /// the control fill is used, replaced by the primary emphasis color on
    /// transparent themes.</summary>
    public Vector4 ResolveOutlineAccent(Variant variant, TextSlot? semantic = null)
    {
        if (semantic.HasValue)
            return GetTextSlot(semantic.Value);

        if (IsTransparentTheme)
            return Border;

        return GetButtonPalette(variant).Base;
    }

    /// <summary>Resolve a <see cref="TextSlot"/> to its color value.</summary>
    public Vector4 GetTextSlot(TextSlot slot) => slot switch
    {
        TextSlot.Subtle => Subtle,
        TextSlot.Emphasis => Emphasis,
        TextSlot.Danger => Danger,
        _               => Body
    };

    // ── Derivation helpers ─────────────────────────────────

    private static ControlPalette DerivePalette(
        Vector4 fill, Vector4 text, float borderAlpha = BorderAlpha)
    {
        var borderColor = SlapColor.Mix(fill, text, BorderMixRatio);
        return new ControlPalette
        {
            Base          = fill,
            Text          = text,
            Hovered       = SlapColor.DeriveHoverColor(fill),
            Active        = SlapColor.DeriveActiveColor(fill),
            Disabled      = SlapColor.WithAlpha(fill, fill.W * SlapColor.DisabledAlpha),
            Border        = SlapColor.WithAlpha(borderColor, borderAlpha),
            BorderSubtle  = SlapColor.WithAlpha(borderColor, SubtleBorderAlpha),
            BorderStrong  = text with { W = 1f }
        };
    }

    private static float NormalizeCornerRadiusUnits(float value) =>
        value >= 0f && !float.IsInfinity(value) ? value : 0f;
}

/// <summary>
/// Pre-computed state colors for a single <see cref="Variant"/>.
/// </summary>
internal readonly record struct ControlPalette
{
    public Vector4 Base { get; init; }
    public Vector4 Text { get; init; }
    public Vector4 Hovered { get; init; }
    public Vector4 Active { get; init; }
    public Vector4 Disabled { get; init; }
    public Vector4 Border { get; init; }
    public Vector4 BorderSubtle { get; init; }
    public Vector4 BorderStrong { get; init; }
}
