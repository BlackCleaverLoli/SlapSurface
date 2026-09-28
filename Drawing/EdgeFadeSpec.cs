using System.Numerics;

namespace SlapSurface;

/// <summary>
/// Structured edge fade specification. Supports vertical inset and custom background.
/// <para>
/// <see cref="FadeWidth"/> semantics:
/// <list type="bullet">
/// <item>&gt; 0 — explicit unscaled pixel value (passed through <c>MetricsScope.Scale</c>).</item>
/// <item>0 — auto: fade width is 5% of the draw dimension at draw time.</item>
/// <item>&lt; 0 — disabled; no fade is drawn.</item>
/// </list>
/// </para>
/// </summary>
internal readonly record struct EdgeFadeSpec(
    float FadeWidth = 0f,
    Vector4? Background = null,
    float VerticalInset = 0f
)
{
    public static EdgeFadeSpec None => new(-1f);
    public bool IsEnabled => FadeWidth >= 0f;
}
