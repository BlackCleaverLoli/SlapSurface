using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using SlapSurface.Metrics;

namespace SlapSurface;

/// <summary>
/// Static, non-interactive icon + two-line text block. Icon and text are
/// independently centered within the component's resolved natural height.
/// </summary>
internal readonly record struct IconTwoLineSpec(
    ControlKey Key,
    uint GameIconId,
    string Line1,
    string Line2,
    float UnitIconSize = 0f,
    SlapFontSize Line1Font = SlapFontSize.Regular,
    SlapFontWeight Line1Weight = SlapFontWeight.Bold,
    SlapFontSize Line2Font = SlapFontSize.Small,
    SlapFontWeight Line2Weight = SlapFontWeight.Bold,
    Vector4? Line1Color = null,
    Vector4? Line2Color = null,
    float IconTextGap = SlapPx.Space8,
    bool ShowIconPlaceholder = false,
    float TextMaxX = float.MaxValue,
    Vector4? RightFadeBackground = null,
    float LineGap = 0f,
    string? Tooltip = null,
    IDalamudTextureWrap? Texture = null
);

internal static class IconTwoLineComponent
{
    private const float DefaultIconUnits = 1.25f;

    public static void Draw(IconTwoLineSpec spec)
    {
        var iconSize = ResolveIconSize(spec);
        var hasIconSlot =
            spec.GameIconId != 0
            || spec.Texture != null
            || spec.ShowIconPlaceholder;
        var iconTextGap = hasIconSlot ? MetricsScope.ScaleGap(spec.IconTextGap) : 0f;
        var textSpec = BuildTextSpec(spec);
        var textMetrics = TwoLineTextComponent.Measure(textSpec);
        var height = MathF.Max(hasIconSlot ? iconSize : 0f, textMetrics.Height);
        var totalWidth =
            (hasIconSlot ? iconSize + iconTextGap : 0f)
            + textMetrics.Width;
        var min = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        if (hasIconSlot)
        {
            var iconMin = new Vector2(
                min.X,
                min.Y + SlapLayout.CenterOffset(height, iconSize)
            );
            var iconMax = iconMin + new Vector2(iconSize);
            if (spec.GameIconId != 0)
                GameIconComponent.DrawInSlot(drawList, spec.GameIconId, iconMin, iconMax);
            else
                GameIconComponent.DrawInSlot(drawList, spec.Texture, iconMin, iconMax);
        }

        var textX = min.X + (hasIconSlot ? iconSize + iconTextGap : 0f);
        var textMaxX = spec.TextMaxX < float.MaxValue
            ? MathF.Max(textX, spec.TextMaxX)
            : textX + textMetrics.Width;
        TwoLineTextComponent.DrawInRect(
            drawList,
            textSpec,
            new Vector2(textX, min.Y),
            new Vector2(textMaxX, min.Y + height)
        );

        ImGui.Dummy(new Vector2(totalWidth, height));
    }

    internal static float ResolveWidth(IconTwoLineSpec spec)
    {
        var hasIconSlot =
            spec.GameIconId != 0
            || spec.Texture != null
            || spec.ShowIconPlaceholder;
        var iconWidth = hasIconSlot ? ResolveIconSize(spec) : 0f;
        var iconTextGap = hasIconSlot ? MetricsScope.ScaleGap(spec.IconTextGap) : 0f;
        return iconWidth
            + iconTextGap
            + TwoLineTextComponent.Measure(BuildTextSpec(spec)).Width;
    }

    internal static float ResolveHeight(IconTwoLineSpec spec) =>
        MathF.Max(
            spec.GameIconId != 0
                || spec.Texture != null
                || spec.ShowIconPlaceholder
                ? ResolveIconSize(spec)
                : 0f,
            TwoLineTextComponent.Measure(BuildTextSpec(spec)).Height
        );

    internal static string? ComposeTooltip(IconTwoLineSpec spec, float startX)
    {
        var iconSize = ResolveIconSize(spec);
        var hasIconSlot =
            spec.GameIconId != 0
            || spec.Texture != null
            || spec.ShowIconPlaceholder;
        var iconTextGap = hasIconSlot ? MetricsScope.ScaleGap(spec.IconTextGap) : 0f;
        var textX = startX + (hasIconSlot ? iconSize + iconTextGap : 0f);
        var textMetrics = TwoLineTextComponent.Measure(BuildTextSpec(spec));
        var textMaxX = spec.TextMaxX < float.MaxValue
            ? MathF.Max(textX, spec.TextMaxX)
            : textX + textMetrics.Width;
        var degraded = textMetrics.IsLine1Clipped(textMaxX - textX);
        return ResponsiveTooltip.Compose(degraded, spec.Line1, spec.Tooltip);
    }

    private static TwoLineTextSpec BuildTextSpec(IconTwoLineSpec spec) =>
        new(
            spec.Line1,
            spec.Line2,
            spec.Line1Font,
            spec.Line1Weight,
            spec.Line2Font,
            spec.Line2Weight,
            spec.Line1Color,
            spec.Line2Color,
            spec.LineGap,
            spec.RightFadeBackground
        );

    private static float ResolveIconSize(IconTwoLineSpec spec) =>
        spec.UnitIconSize > 0f
            ? MetricsScope.UnitHeight * spec.UnitIconSize
            : MetricsScope.UnitHeight * DefaultIconUnits;
}
