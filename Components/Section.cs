using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;

namespace SlapSurface;

internal readonly record struct SectionSpec(
    ControlKey Key,
    SurfaceSize Size,
    SurfaceHeaderSpec Header,
    ControlState State,
    LayeredShadowSpec Shadow = default,
    Vector2? Padding = null,
    float HeaderGap = SlapPx.Space8
);

internal static class SectionComponent
{
    public static void Draw(SectionSpec spec, Action<SurfaceContentContext> drawContent)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("SectionSpec.Key.Value must not be empty.", nameof(spec));

        var padding = spec.Padding ?? new Vector2(SlapPx.Space12, SlapPx.Space8);
        SurfaceComponent.Draw(
            new SurfaceSpec(
                spec.Key,
                spec.Size,
                spec.State,
                spec.Shadow,
                padding),
            surface => DrawBody(spec, surface, drawContent));
    }

    private static void DrawBody(
        SectionSpec spec,
        SurfaceContentContext surface,
        Action<SurfaceContentContext> drawContent)
    {
        var headerHeight = SurfaceHeaderComponent.ResolveHeight(spec.Header);
        var contentWidth = surface.ContentSize.X;
        if (contentWidth <= 0f || surface.ContentSize.Y <= 0f)
            return;

        var headerMin = surface.ContentMin;
        var headerMax = new Vector2(surface.ContentMax.X, MathF.Min(surface.ContentMax.Y, headerMin.Y + headerHeight));
        SurfaceHeaderComponent.DrawInRect(
            ImGui.GetWindowDrawList(),
            headerMin,
            headerMax,
            spec.Header,
            surface.Background);

        var bodyMin = new Vector2(surface.ContentMin.X, headerMax.Y + MetricsScope.ScaleGap(spec.HeaderGap));
        var bodyMax = surface.ContentMax;
        if (bodyMax.X <= bodyMin.X || bodyMax.Y <= bodyMin.Y)
            return;

        ImGui.SetCursorScreenPos(bodyMin);
        drawContent(new SurfaceContentContext(
                        surface.Min,
                        surface.Max,
                        bodyMin,
                        bodyMax,
                        surface.Background));
    }
}
