using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;
 
/// <summary>
/// Spec for drawing an icon + two-line text block inside a
/// <see cref="SurfaceRowContext"/>. All elements are vertically centered
/// within the row's content area.
/// <para>
/// Layout (left to right):
/// <code>
/// [optional leading text, right-aligned column] → [game icon or fallback icon
/// at RowIconFillRatio, optional bottom-right check] → gap →
/// [bold regular name / bold small meta text block] → [optional right-aligned trailing text]
/// </code>
/// </para>
/// <para>
/// When <see cref="GameIconId"/> is 0 and <see cref="Icon"/> is set, the
/// FontAwesome icon is drawn in the same slot.
/// </para>
/// <para>
/// When <see cref="GameIconShape"/> is <see cref="SlapSurface.GameIconShape.Circle"/>
/// the game icon uses maximum safe corner rounding while retaining the standard embossed shadow.
/// </para>
/// <para>
/// When <see cref="TextMaxX"/> is less than <c>float.MaxValue</c>, name and
/// meta text that overflow are clipped and a right-edge fade is drawn using
/// the row's background colour, matching the SlapSurface text-fade convention.
/// </para>
/// <para>
/// When <see cref="Tooltip"/> is set and the row is hovered, the shared
/// responsive-tooltip protocol is used: a clipped name shows the name above
/// the tooltip, while an unclipped row shows only the tooltip.
/// </para>
/// </summary>
internal readonly record struct RowIconTwoLineSpec(
    uint GameIconId = 0,
    GameIconShape GameIconShape = GameIconShape.Standard,
    string Name = "",
    string? MetaText = null,
    float TextMaxX = float.MaxValue,
    string? TrailingText = null,
    /// <summary>Optional leading text drawn as a right-aligned column before
    /// the icon. <see cref="LeadingWidth"/> is the reserved column width;
    /// when 0 the column shrinks to the measured text width.</summary>
    string LeadingText = "",
    SlapFontSize LeadingFont = SlapFontSize.Large,
    SlapFontWeight LeadingWeight = SlapFontWeight.Bold,
    Vector4? LeadingColor = null,
    float LeadingWidth = 0f,
    bool CornerCheck = false,
    FontAwesomeIcon? Icon = null,
    float LineGap = 0f,
    IDalamudTextureWrap? Texture = null,
    bool IconShadow = true,
    string? Tooltip = null
);

internal static class RowIconTwoLineComponent
{
    private const float ElementGap = SlapPx.Space12;
    private const float TrailingRightPadding = SlapPx.Space4;

    public static void Draw(SurfaceRowContext context, RowIconTwoLineSpec spec)
    {
        var drawList = ImGui.GetWindowDrawList();
        var rt = ThemeScope.Resolved;
        var gap = MetricsScope.ScaleGap(ElementGap);
        var trailingRightPadding = MetricsScope.ScaleGap(TrailingRightPadding);

        var leadingWidth = ResolveLeadingWidth(spec);
        if (leadingWidth > 0f && !string.IsNullOrWhiteSpace(spec.LeadingText))
        {
            Vector2 leadingSize;
            using var leadingFont = Slap.PushFont(spec.LeadingFont, spec.LeadingWeight);
            leadingSize = ImGui.CalcTextSize(spec.LeadingText);
            var leadingColor = ImGui.ColorConvertFloat4ToU32(spec.LeadingColor ?? rt.Emphasis);
            var leadingMin = new Vector2(
                context.ContentMin.X + leadingWidth - leadingSize.X,
                context.ContentMin.Y + SlapLayout.CenterOffset(context.ContentSize.Y, leadingSize.Y));
            drawList.AddText(leadingMin, leadingColor, spec.LeadingText);
        }

        // ── 1. Game icon ──
        var leadingColumnWidth = leadingWidth > 0f ? leadingWidth + gap : 0f;
        var (gameIconMin, gameIconMax) = ResolveIconSlot(
            context.Min,
            context.Max,
            new Vector2(context.ContentMin.X + leadingColumnWidth, context.ContentMin.Y),
            context.ContentMax
        );
        var gameIconSize = gameIconMax.X - gameIconMin.X;
        var showIconAction = context.IconAction.HasValue
            && (context.Hovered || context.IconAction.Value.Result.Hovered);
        var selection = context.Selection;
        var showSelectedFilter = selection.HasValue;
        var selectedOverlayIcon = selection?.Icon;
        var selectedOverlayColor = selection?.IconColor ?? Vector4.One;
        var selectedTint = selection?.Tint ?? new Vector4(0.12f, 0.12f, 0.12f, 1f);
        var overlayIcon = showIconAction
            ? context.IconAction?.Spec.OverlayIcon
            : selectedOverlayIcon;
        Vector4? overlayColor = showIconAction
            ? Vector4.One
            : selectedOverlayIcon.HasValue
                ? selectedOverlayColor
                : null;

        if (GameIconComponent.IsAvailable(spec.GameIconId))
        {
            Slap.DrawGameIcon(
                spec.GameIconId,
                gameIconMin,
                gameIconMax,
                context.VisualMode,
                highQuality: false,
                shape: spec.GameIconShape,
                tint: showIconAction
                    ? new Vector4(0f, 0f, 0f, 1f)
                    : showSelectedFilter
                        ? selectedTint
                        : null,
                overlayIcon: overlayIcon,
                overlayColor: overlayColor,
                cornerCheck: spec.CornerCheck,
                shadow: spec.IconShadow
            );
        }
        else if (spec.Texture != null)
        {
            Slap.DrawGameIcon(
                spec.Texture,
                gameIconMin,
                gameIconMax,
                context.VisualMode
            );
        }
        else if (spec.Icon.HasValue)
        {
            var (iconText, iconFont) = SlapIcon.Resolve(spec.Icon);
            IconTextComponent.DrawInRect(
                drawList,
                gameIconMin,
                gameIconMax,
                string.Empty,
                iconText,
                iconFont,
                rt.Subtle,
                centerContent: true,
                iconSlotWidth: gameIconSize
            );
        }

        var textMinX = gameIconMin.X + gameIconSize + gap;
        // The icon is drawn inset inside its slot; mirror that inset on the text edge so
        // clipped text stops at the same optical distance from the border as the icon.
        var iconSlotInset =
            GameIconComponent.IsAvailable(spec.GameIconId) || spec.Texture != null
                ? GameIconComponent.ResolveSlotInset()
                : 0f;
        var textContentMaxX = MathF.Max(textMinX, context.ContentMax.X - iconSlotInset);
        var contentTextMaxX = spec.TextMaxX < float.MaxValue
            ? MathF.Min(spec.TextMaxX, textContentMaxX)
            : textContentMaxX;
        var textMaxX = contentTextMaxX;
        var trailingText = spec.TrailingText;
        Vector2 trailingSize = Vector2.Zero;
        if (!string.IsNullOrWhiteSpace(trailingText))
        {
            using var trailingFont = Slap.PushFont(
                SlapFontSize.Regular,
                SlapFontWeight.Bold
            );
            trailingSize = ImGui.CalcTextSize(trailingText);
            textMaxX = MathF.Max(
                textMinX,
                contentTextMaxX - trailingSize.X - gap - trailingRightPadding
            );
        }

        // ── 2. Container-aware two-line text block ──
        var textMetrics = TwoLineTextComponent.DrawInRect(
            drawList,
            new TwoLineTextSpec(
                spec.Name ?? string.Empty,
                spec.MetaText,
                SlapFontSize.Regular,
                SlapFontWeight.Bold,
                SlapFontSize.Small,
                SlapFontWeight.Bold,
                rt.Body,
                rt.Subtle,
                spec.LineGap,
                context.Background
            ),
            new Vector2(textMinX, context.ContentMin.Y),
            new Vector2(textMaxX, context.ContentMax.Y)
        );

        if (!string.IsNullOrWhiteSpace(trailingText))
        {
            var trailingMin = new Vector2(
                contentTextMaxX - trailingSize.X - trailingRightPadding,
                context.ContentMin.Y
                    + SlapLayout.CenterOffset(context.ContentSize.Y, trailingSize.Y)
            );
            drawList.PushClipRect(
                new Vector2(textMinX, context.ContentMin.Y),
                new Vector2(contentTextMaxX, context.ContentMax.Y),
                true
            );
            try
            {
                using var trailingFont = Slap.PushFont(
                    SlapFontSize.Regular,
                    SlapFontWeight.Bold
                );
                drawList.AddText(
                    trailingMin,
                    ImGui.ColorConvertFloat4ToU32(rt.Body),
                    trailingText
                );
            }
            finally
            {
                drawList.PopClipRect();
            }
        }

        var tooltip = ResponsiveTooltip.Compose(
            textMetrics.IsLine1Clipped(MathF.Max(0f, textMaxX - textMinX)),
            spec.Name,
            spec.Tooltip);
        HoverArbitration.Current.TryShowTooltip(context.Hovered, tooltip, context.Min, context.Max);
    }

    internal static (Vector2 Min, Vector2 Max) ResolveIconSlot(
        Vector2 rowMin,
        Vector2 rowMax,
        Vector2 contentMin,
        Vector2 contentMax
    )
    {
        var rowSize = Vector2.Max(Vector2.Zero, rowMax - rowMin);
        var contentSize = Vector2.Max(Vector2.Zero, contentMax - contentMin);
        var iconSize = rowSize.Y * SlapPx.RowIconFillRatio;
        var iconMin = new Vector2(
            contentMin.X,
            contentMin.Y + MathF.Max(0f, (contentSize.Y - iconSize) * 0.5f)
        );
        return (iconMin, iconMin + new Vector2(iconSize));
    }

    private static float ResolveLeadingWidth(RowIconTwoLineSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.LeadingText))
            return 0f;
        if (spec.LeadingWidth > 0f)
            return spec.LeadingWidth;

        using var font = Slap.PushFont(spec.LeadingFont, spec.LeadingWeight);
        return ImGui.CalcTextSize(spec.LeadingText).X;
    }
}
