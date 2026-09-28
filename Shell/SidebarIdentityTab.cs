using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Interactive identity tab rendered above shell sidebar navigation. The expanded
/// layout shows a texture and two-line caption; the compact layout shows only the texture.
/// </summary>
internal readonly record struct SidebarIdentityTabSpec(
    ControlKey Key,
    IDalamudTextureWrap? Texture,
    string Line1,
    string Line2,
    bool Selected = false,
    bool Disabled = false,
    Variant Variant = Variant.Sidebar,
    string? Tooltip = null,
    TextSlot? Line2Semantic = null,
    float LineGap = 0f
);

internal static class SidebarIdentityTabComponent
{
    private const float CompactWidthEpsilon = 0.5f;
    private const float IconLeftPadding = IconTextComponent.GameIconLeftPadding;
    private const float IconTextGap = IconTextComponent.GameIconTextGap;
    private const float TextRightPadding = SlapPx.Space16;

    internal static float MeasurePreferredWidth(SidebarIdentityTabSpec spec)
    {
        var unit = MetricsScope.UnitHeight;
        var captionWidth = TwoLineTextComponent.Measure(BuildTextSpec(spec)).Width;
        return MetricsScope.ScalePadding(IconLeftPadding)
            + unit
            + MetricsScope.ScalePadding(IconTextGap)
            + captionWidth
            + MetricsScope.ScalePadding(TextRightPadding);
    }

    internal static ControlResult Draw(SidebarIdentityTabSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "SidebarIdentityTabSpec.Key.Value must not be empty.",
                nameof(spec)
            );

        ImGui.PushID(spec.Key.Value);
        try
        {
            var unit = MetricsScope.UnitHeight;
            var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            var start = ImGui.GetCursorScreenPos();
            var compact = availableWidth <= unit + MetricsScope.Scale(CompactWidthEpsilon);
            var size = new Vector2(MathF.Max(unit, availableWidth), unit);

            ImGui.SetCursorScreenPos(start);
            var rawClicked = ImGui.InvisibleButton("##identityTab", size);
            var actualMin = ImGui.GetItemRectMin();
            var actualMax = ImGui.GetItemRectMax();
            var interaction = SlapInteraction.Capture(
                spec.Disabled,
                spec.Selected,
                rawClicked,
                captureRightClick: true
            );

            var rt = ThemeScope.Resolved;
            var palette = rt.GetButtonPalette(spec.Variant);
            Vector4 background;
            if (spec.Selected)
            {
                background = spec.Disabled ? palette.Disabled
                    : interaction.Active ? palette.Active
                    : palette.Base;
            }
            else
            {
                background = SurfaceComponent.ResolveFlatSurfaceBackground(
                    interaction.Hovered,
                    interaction.Active,
                    spec.Disabled);
            }
            var rounding = SlapCorners.ControlRadius;
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(
                actualMin,
                actualMax,
                ImGui.ColorConvertFloat4ToU32(background),
                rounding,
                ImDrawFlags.RoundCornersAll
            );

            HoverArbitration.Current.CaptureOutline(
                actualMin,
                actualMax,
                interaction.Hovered && !spec.Selected && !spec.Disabled,
                selected: false,
                accent: rt.ResolveOutlineAccent(spec.Variant),
                rounding: rounding
            );
            HoverArbitration.Current.TryShowTooltip(
                interaction.Hovered,
                spec.Tooltip,
                preferredDirection: SlapTooltipDirection.Right);

            if (compact)
            {
                DrawTextureInSlot(
                    drawList,
                    spec.Texture,
                    actualMin,
                    actualMax,
                    interaction.VisualMode
                );
            }
            else
            {
                DrawExpandedContent(
                    spec,
                    drawList,
                    actualMin,
                    actualMax,
                    background,
                    palette.Text,
                    rt,
                    interaction.VisualMode
                );
            }

            ImGui.SetCursorScreenPos(
                start + new Vector2(
                    0f,
                    size.Y + MetricsScope.ScaleGap(ShellComponent.SidebarItemGap)
                )
            );
            return interaction.ToControlResult(actualMin, actualMax);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static TwoLineTextSpec BuildTextSpec(
        SidebarIdentityTabSpec spec,
        Vector4? line1Color = null,
        Vector4? line2Color = null,
        Vector4? rightFadeBackground = null
    ) =>
        new(
            spec.Line1,
            spec.Line2,
            SlapFontSize.Small,
            SlapFontWeight.Bold,
            SlapFontSize.Small,
            SlapFontWeight.Bold,
            line1Color,
            line2Color,
            spec.LineGap,
            RightFadeBackground: rightFadeBackground
        );

    private static void DrawExpandedContent(
        SidebarIdentityTabSpec spec,
        ImDrawListPtr drawList,
        Vector2 min,
        Vector2 max,
        Vector4 background,
        Vector4 selectedText,
        ResolvedTheme theme,
        ControlVisualMode visualMode
    )
    {
        var unit = MetricsScope.UnitHeight;
        var iconSlotMin = new Vector2(
            min.X + MetricsScope.ScalePadding(IconLeftPadding),
            min.Y
        );
        var iconSlotMax = iconSlotMin + new Vector2(unit, unit);
        DrawTextureInSlot(
            drawList,
            spec.Texture,
            iconSlotMin,
            iconSlotMax,
            visualMode
        );

        var textMin = new Vector2(
            iconSlotMax.X + MetricsScope.ScalePadding(IconTextGap),
            min.Y
        );
        var line1Color = SlapColor.WithDisabledAlpha(
            spec.Selected ? selectedText : theme.Body,
            spec.Disabled
        );
        var line2Color = SlapColor.WithDisabledAlpha(
            spec.Selected
                ? selectedText
                : spec.Line2Semantic.HasValue
                    ? theme.GetTextSlot(spec.Line2Semantic.Value)
                    : theme.Body,
            spec.Disabled
        );
        TwoLineTextComponent.DrawInRect(
            drawList,
            BuildTextSpec(spec, line1Color, line2Color, background),
            textMin,
            new Vector2(
                max.X - MetricsScope.ScalePadding(TextRightPadding),
                max.Y
            )
        );
    }

    private static void DrawTextureInSlot(
        ImDrawListPtr drawList,
        IDalamudTextureWrap? texture,
        Vector2 slotMin,
        Vector2 slotMax,
        ControlVisualMode visualMode
    )
    {
        var (textureMin, textureMax) = GameIconComponent.ResolveControlIconRect(
            slotMin,
            slotMax
        );
        GameIconComponent.DrawResolved(
            drawList,
            texture,
            textureMin,
            textureMax,
            visualMode
        );
    }
}
