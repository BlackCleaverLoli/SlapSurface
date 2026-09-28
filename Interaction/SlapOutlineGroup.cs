using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Drawing;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

internal enum SlapOutlineShape
{
    RoundedRect,
    Circle,
}

/// <summary>
/// Captures one dense-item outline per frame. Hovered captures win over selected captures.
/// </summary>
internal struct SlapOutlineGroup
{
    private const float OutlineFadeInSpeed = 40f;
    private const float OutlineFadeOutSpeed = 40f;

    // Centralized cross-frame animation cache. Keyed by a caller-provided string
    // (typically the control key). This lets stateless components (Button, RowList,
    // etc.) get animated fade in/out without each maintaining their own dict.
    // The cache grows with unique keys (= unique control instances) and never
    // shrinks; in practice this is bounded by the UI structure (< 100 entries).
    private static readonly Dictionary<string, SlapOutlineAnimationState> _animations = new();

    private SlapOutlineCapture? capture;
    private Vector2? clipMin;
    private Vector2? clipMax;

    /// <summary>
    /// Sets a clip rect that constrains all outline drawing to the given bounds.
    /// Use this in scrollable containers to prevent glow from rendering outside
    /// the visible area. When not called, the clip defaults to the full display.
    /// </summary>
    public void SetClipRect(Vector2 min, Vector2 max)
    {
        clipMin = min;
        clipMax = max;
    }

    public void CaptureLastItem(
        bool hovered,
        bool selected,
        Vector4 accent,
        SlapOutlineCutout? cutout = null,
        bool drawOutline = true,
        float? rounding = null
    )
    {
        Capture(
            ImGui.GetItemRectMin(),
            ImGui.GetItemRectMax(),
            hovered,
            selected,
            accent,
            cutout,
            drawOutline,
            rounding
        );
    }

    public void Capture(
        Vector2 min,
        Vector2 max,
        bool hovered,
        bool selected,
        Vector4 accent,
        SlapOutlineCutout? cutout = null,
        bool drawOutline = true,
        float? rounding = null
    )
    {
        CaptureCore(
            min,
            max,
            hovered,
            selected,
            accent,
            cutout,
            drawOutline,
            rounding,
            SlapOutlineShape.RoundedRect
        );
    }

    public void CaptureCircle(
        Vector2 min,
        Vector2 max,
        bool hovered,
        bool selected,
        Vector4 accent,
        bool drawOutline = true
    )
    {
        CaptureCore(
            min,
            max,
            hovered,
            selected,
            accent,
            cutout: null,
            drawOutline,
            rounding: 0f,
            SlapOutlineShape.Circle
        );
    }

    private void CaptureCore(
        Vector2 min,
        Vector2 max,
        bool hovered,
        bool selected,
        Vector4 accent,
        SlapOutlineCutout? cutout,
        bool drawOutline,
        float? rounding,
        SlapOutlineShape shape
    )
    {
        if (!hovered && !selected)
            return;

        if (capture.HasValue && (capture.Value.Hovered || !hovered))
            return;

        if (max.X <= min.X || max.Y <= min.Y)
            return;

        var validCutout =
            cutout.HasValue
            && cutout.Value.Max.X > cutout.Value.Min.X
            && cutout.Value.Max.Y > cutout.Value.Min.Y
                ? cutout
                : null;

        capture = new SlapOutlineCapture(
            min,
            max,
            hovered,
            validCutout,
            drawOutline,
            rounding ?? SlapCorners.ControlRadius,
            shape,
            accent
        );
    }

    public void Flush(float thickness = 1f)
    {
        var item = capture;
        capture = null;
        if (!item.HasValue || !item.Value.DrawOutline)
            return;

        Draw(item.Value, 1f, thickness, clipMin, clipMax);
    }

    /// <summary>
    /// Animated flush using a centralized cross-frame cache keyed by
    /// <paramref name="animationKey"/>. Call this every frame (even when not
    /// hovered) so the fade-out phase can run. When no new capture was made
    /// this frame, the previous outline fades out automatically.
    /// </summary>
    public void Flush(string animationKey, float thickness = 1f)
    {
        if (!_animations.TryGetValue(animationKey, out var animation))
        {
            animation = new SlapOutlineAnimationState();
            _animations.Add(animationKey, animation);
        }
        Flush(animation, thickness);
    }

    /// <summary>Convenience overload accepting a <see cref="ControlKey"/>.</summary>
    public void Flush(ControlKey animationKey, float thickness = 1f)
        => Flush(animationKey.Value, thickness);

    public void Flush(SlapOutlineAnimationState animation)
    {
        Flush(animation, thickness: 1f);
    }

    public void Flush(SlapOutlineAnimationState animation, float thickness)
    {
        var item = capture;
        capture = null;

        if (item.HasValue && item.Value.DrawOutline)
            Activate(animation, item.Value);
        else if (animation.Active != null)
        {
            animation.Fading = animation.Active;
            animation.Active = null;
        }

        var deltaTime = MathF.Min(ImGui.GetIO().DeltaTime, 0.05f);
        if (animation.Fading != null)
        {
            animation.Fading.Opacity = Approach(
                animation.Fading.Opacity,
                0f,
                OutlineFadeOutSpeed,
                deltaTime
            );
            if (animation.Fading.Opacity <= 0.01f)
                animation.Fading = null;
            else
                Draw(animation.Fading, thickness, clipMin, clipMax);
        }

        if (animation.Active == null)
            return;

        animation.Active.Opacity = Approach(
            animation.Active.Opacity,
            1f,
            OutlineFadeInSpeed,
            deltaTime
        );
        Draw(animation.Active, thickness, clipMin, clipMax);
    }

    private static void Activate(SlapOutlineAnimationState animation, SlapOutlineCapture capture)
    {
        if (animation.Active != null && animation.Active.Matches(capture))
        {
            animation.Active.Update(capture);
            return;
        }

        if (animation.Fading != null && animation.Fading.Matches(capture))
        {
            animation.Active = animation.Fading;
            animation.Fading = null;
            animation.Active.Update(capture);
            return;
        }

        if (animation.Active != null && animation.Active.Opacity > 0.01f)
            animation.Fading = animation.Active;

        animation.Active = new SlapOutlineAnimationFrame(capture);
    }

    private static float Approach(float current, float target, float speed, float deltaTime)
    {
        var t = 1f - MathF.Exp(-speed * deltaTime);
        return current + ((target - current) * t);
    }

    private static void Draw(SlapOutlineAnimationFrame frame, float thickness, Vector2? clipMin, Vector2? clipMax)
    {
        Draw(frame.Capture, frame.Opacity, thickness, clipMin, clipMax);
    }

    private static void Draw(SlapOutlineCapture item, float opacity, float thickness, Vector2? clipMin, Vector2? clipMax)
    {
        if (opacity <= 0.01f)
            return;

        SlapOutlineDrawing.DrawGlow(
            ImGui.GetWindowDrawList(),
            item.Min,
            item.Max,
            item.Accent with { W = Math.Clamp(opacity, 0f, 1f) },
            item.Rounding,
            MathF.Max(0.1f, thickness),
            item.Cutout?.Min,
            item.Cutout?.Max,
            clipMin,
            clipMax,
            item.Shape
        );
    }
}

internal sealed class SlapOutlineAnimationState
{
    internal SlapOutlineAnimationFrame? Active { get; set; }
    internal SlapOutlineAnimationFrame? Fading { get; set; }
}

internal sealed class SlapOutlineAnimationFrame
{
    internal SlapOutlineAnimationFrame(SlapOutlineCapture capture)
    {
        Update(capture);
    }

    internal SlapOutlineCapture Capture { get; private set; }
    internal float Opacity { get; set; }

    internal void Update(SlapOutlineCapture capture)
    {
        Capture = capture;
    }

    internal bool Matches(SlapOutlineCapture capture)
    {
        var threshold = MetricsScope.Scale(2f);
        return Vector2.DistanceSquared(GetCenter(Capture.Min, Capture.Max), GetCenter(capture.Min, capture.Max))
                <= threshold * threshold
            && Vector2.DistanceSquared(Capture.Max - Capture.Min, capture.Max - capture.Min)
                <= threshold * threshold
            && Capture.Shape == capture.Shape;
    }

    private static Vector2 GetCenter(Vector2 min, Vector2 max)
    {
        return (min + max) * 0.5f;
    }
}

internal readonly record struct SlapOutlineCutout(Vector2 Min, Vector2 Max);

internal readonly record struct SlapOutlineCapture(
    Vector2 Min,
    Vector2 Max,
    bool Hovered,
    SlapOutlineCutout? Cutout,
    bool DrawOutline,
    float Rounding,
    SlapOutlineShape Shape,
    Vector4 Accent
);
