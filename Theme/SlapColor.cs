using System;
using System.Numerics;

namespace SlapSurface.Theme;

internal static class SlapColor
{
    /// <summary>
    /// Alpha multiplier applied to all rendered colors when a control is
    /// disabled, matching ImGui's <c>style.DisabledAlpha</c> default.
    /// Applied uniformly to background, border, text, and other primitives.
    /// </summary>
    public const float DisabledAlpha = 0.60f;

    // ── sRGB helpers ───────────────────────────────────────

    public static Vector4 Mix(Vector4 a, Vector4 b, float t) =>
        Clamp(
            new Vector4(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t,
                a.W + (b.W - a.W) * t
            )
        );

    /// <summary>Lighten in OKLCH: increase L by <paramref name="amount"/>,
    /// preserve C and h, clamp sRGB result.</summary>
    public static Vector4 Lighten(Vector4 c, float amount) => AdjustLightness(c, amount);

    /// <summary>Darken in OKLCH: decrease L by <paramref name="amount"/>,
    /// preserve C and h, clamp sRGB result.</summary>
    public static Vector4 Darken(Vector4 c, float amount) => AdjustLightness(c, -amount);

    public static Vector4 WithAlpha(Vector4 c, float alpha) =>
        Clamp(new Vector4(c.X, c.Y, c.Z, alpha));

    /// <summary>Source-over compositing of <paramref name="src"/> over <paramref name="dst"/>.</summary>
    public static Vector4 CompositeOver(Vector4 src, Vector4 dst)
    {
        var outAlpha = src.W + dst.W * (1f - src.W);
        if (outAlpha <= 0f)
            return Vector4.Zero;

        var invAlpha = 1f / outAlpha;
        var r = (src.X * src.W + dst.X * dst.W * (1f - src.W)) * invAlpha;
        var g = (src.Y * src.W + dst.Y * dst.W * (1f - src.W)) * invAlpha;
        var b = (src.Z * src.W + dst.Z * dst.W * (1f - src.W)) * invAlpha;
        return new Vector4(r, g, b, outAlpha);
    }

    /// <summary>
    /// When <paramref name="disabled"/> is true, multiplies the color's alpha
    /// by <see cref="DisabledAlpha"/>, matching ImGui's <c>BeginDisabled</c>
    /// behavior. Returns the original color unchanged when not disabled.
    /// </summary>
    public static Vector4 WithDisabledAlpha(Vector4 c, bool disabled) =>
        disabled ? WithAlpha(c, c.W * DisabledAlpha) : c;

    /// <summary>
    /// Remove chroma while preserving OKLCH lightness, producing a neutral gray.
    /// </summary>
    public static Vector4 Desaturate(Vector4 c)
    {
        var (l, _, _) = RgbToOklch(c);
        var gray = OklchToRgb(l, 0f, 0f);
        gray.W = c.W;
        return gray;
    }

    /// <summary>
    /// OKLCH perceptual lightness (L channel) of an sRGB color.
    /// Range [0, 1]. Use this for light/dark surface classification
    /// instead of WCAG relative luminance.
    /// </summary>
    public static float PerceivedLightness(Vector4 rgb) => RgbToOklch(rgb).L;

    /// <summary>
    /// OKLCH L threshold above which a surface is considered "light".
    /// </summary>
    internal const float LightSurfaceThreshold = 0.90f;

    /// <summary>OKLCH L floor used when clamping lightness, treated as "black".</summary>
    internal const float LightnessFloor = 0.005f;

    /// <summary>OKLCH L ceiling used when clamping lightness, treated as "white".</summary>
    internal const float LightnessCeiling = 0.995f;

    // ── State color derivation (OKLCH) ─────────────────────

    internal const float HoverLightnessDelta = 0.03f;
    internal const float ActivePressedLightnessDelta = -0.03f;

    /// <summary>
    /// Derive hover color by increasing OKLCH L by <see cref="HoverLightnessDelta"/>,
    /// keeping C and h unchanged.
    /// </summary>
    public static Vector4 DeriveHoverColor(Vector4 baseColor) =>
        AdjustLightness(baseColor, HoverLightnessDelta);

    /// <summary>
    /// Derive active / pressed color by decreasing OKLCH L by
    /// <see cref="ActivePressedLightnessDelta"/>, keeping C and h unchanged.
    /// </summary>
    public static Vector4 DeriveActiveColor(Vector4 baseColor) =>
        AdjustLightness(baseColor, ActivePressedLightnessDelta);

    /// <inheritdoc cref="DeriveActiveColor"/>
    public static Vector4 DerivePressedColor(Vector4 baseColor) => DeriveActiveColor(baseColor);

    /// <summary>
    /// Shift OKLCH L by <paramref name="delta"/>, preserve C and h,
    /// clamp L to [0.005, 0.995], and clamp sRGB result to [0,1].
    /// </summary>
    private static Vector4 AdjustLightness(Vector4 rgb, float delta)
    {
        var (L, C, h) = RgbToOklch(rgb);
        var newL = Math.Clamp(L + delta, LightnessFloor, LightnessCeiling);
        var result = OklchToRgb(newL, C, h);
        result.W = rgb.W; // preserve original alpha
        return Clamp(result);
    }

    // ── sRGB ↔ linear ↔ OKLab ↔ OKLCH ─────────────────────

    private static (float L, float C, float h) RgbToOklch(Vector4 rgb)
    {
        var lr = ToLinearRgb(rgb.X);
        var lg = ToLinearRgb(rgb.Y);
        var lb = ToLinearRgb(rgb.Z);
        return OklchFromLinear(lr, lg, lb);
    }

    private static Vector4 OklchToRgb(float L, float C, float h)
    {
        var (r, g, b) = OklchToLinearRgb(L, C, h);
        return new Vector4(
            Math.Clamp(FromLinearRgb(r), 0f, 1f),
            Math.Clamp(FromLinearRgb(g), 0f, 1f),
            Math.Clamp(FromLinearRgb(b), 0f, 1f),
            1f
        );
    }

    private static (float r, float g, float b) OklchToLinearRgb(float L, float C, float h)
    {
        // OKLCH → OKLab
        var a = C * MathF.Cos(h);
        var b_ = C * MathF.Sin(h);

        // OKLab → LMS^⅓
        var l_ = L + 0.3963377774f * a + 0.2158037573f * b_;
        var m_ = L - 0.1055613458f * a - 0.0638541728f * b_;
        var s_ = L - 0.0894841775f * a - 1.2914855480f * b_;

        // LMS^⅓ → LMS
        var l = l_ * l_ * l_;
        var m = m_ * m_ * m_;
        var s = s_ * s_ * s_;

        // LMS → linear sRGB
        var r = 4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s;
        var g = -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s;
        var b = -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s;

        return (r, g, b);
    }

    private static (float L, float C, float h) OklchFromLinear(float r, float g, float b)
    {
        // Linear sRGB → LMS
        var l_ = 0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b;
        var m_ = 0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b;
        var s_ = 0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b;

        // LMS → LMS^⅓
        var l = MathF.Cbrt(l_);
        var m = MathF.Cbrt(m_);
        var s = MathF.Cbrt(s_);

        // LMS^⅓ → OKLab
        var L = 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s;
        var a = 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s;
        var b_ = 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s;

        // OKLab → OKLCH
        var C = MathF.Sqrt(a * a + b_ * b_);
        var h = C > 0.0001f ? MathF.Atan2(b_, a) : 0f;

        return (L, C, h);
    }

    // ── Internal utilities ─────────────────────────────────

    private static Vector4 Clamp(Vector4 c) =>
        new(Clamp01(c.X), Clamp01(c.Y), Clamp01(c.Z), Clamp01(c.W));

    private static float Clamp01(float value) =>
        value < 0f ? 0f
        : value > 1f ? 1f
        : value;

    private static float ToLinearRgb(float value) =>
        value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static float FromLinearRgb(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value <= 0.0031308f
            ? value * 12.92f
            : (1.055f * MathF.Pow(value, 1f / 2.4f)) - 0.055f;
    }
}
