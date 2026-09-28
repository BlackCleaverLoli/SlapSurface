using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Specification for a collapsible drawer (CollapsingHeader wrapper).
/// </summary>
internal readonly record struct DrawerSpec(
    ControlKey Key,
    string Title,
    bool DefaultOpen = false,
    string? Tooltip = null,
    bool Disabled = false,
    Variant Variant = Variant.Base,
    float IndentUnits = 0f,
    LayeredShadowSpec Shadow = default
);

/// <summary>
/// Result of a drawer interaction.
/// </summary>
internal readonly record struct DrawerResult(
    bool IsOpen,
    bool Toggled
);

internal static class DrawerComponent
{
    private const float ItemSpacingY = SlapPx.Space8;
    private static readonly Dictionary<string, bool> _openState = new();

    public static void Reset() => _openState.Clear();

    public static DrawerResult Draw(DrawerSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("DrawerSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        using var _hover = HoverArbitration.Push();
        try
        {
            var rt = ThemeScope.Resolved;
            var disabled = spec.Disabled;
            var palette = rt.GetButtonPalette(spec.Variant);

            var cursorX = ImGui.GetCursorScreenPos().X;
            if (spec.IndentUnits > 0f)
                ImGui.SetCursorScreenPos(new Vector2(cursorX + MetricsScope.UnitWidth * spec.IndentUnits, ImGui.GetCursorScreenPos().Y));
            var headerAvailableWidth = ImGui.GetContentRegionAvail().X;

            // Header colors are popped before caller draws content
            ImGui.PushStyleColor(ImGuiCol.Header, palette.Base);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, palette.Hovered);
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, palette.Active);
            ImGui.PushStyleColor(ImGuiCol.Text, SlapColor.WithDisabledAlpha(palette.Text, disabled));

            var key = spec.Key.Value;
            if (!_openState.TryGetValue(key, out var isOpen))
                isOpen = spec.DefaultOpen;

            var wasOpen = isOpen;
            // 用 SetNextItemOpen + 无 ref 的 CollapsingHeader 控制「展开状态」，
            // 标题在收起时也始终可见；ref bool 重载是「可见性/可关闭」标记，
            // 传 false 时整条 header 都不渲染。
            ImGui.SetNextItemOpen(isOpen);
            bool nowOpen;
            if (disabled)
            {
                ImGui.BeginDisabled();
                nowOpen = ImGui.CollapsingHeader(spec.Title);
                ImGui.EndDisabled();
            }
            else
            {
                nowOpen = ImGui.CollapsingHeader(spec.Title);
            }
            isOpen = nowOpen;
            _openState[key] = isOpen;

            ImGui.PopStyleColor(4);

            var headerRectMin = ImGui.GetItemRectMin();
            var headerRectMax = ImGui.GetItemRectMax();
            LayeredShadow.Draw(
                ImGui.GetWindowDrawList(),
                headerRectMin,
                headerRectMax,
                SlapCorners.ControlRadius,
                LayeredShadow.ResolveControlDefault(spec.Shadow, spec.Variant) with { ExpandHorizontalClip = true });

            if (rt.IsTransparentTheme)
            {
                var border = SurfaceComponent.ResolveControlColors(
                    palette,
                    disabled ? ControlState.Disabled : ControlState.None,
                    strength: SurfaceComponent.ResolveBorderStrength(spec.Variant),
                    variant: spec.Variant).Border;
                SurfaceComponent.DrawControlBorder(
                    ImGui.GetWindowDrawList(),
                    headerRectMin,
                    headerRectMax,
                    spec.Variant,
                    border,
                    SlapCorners.ControlRadius);
            }

            // Content gap: match SettingRow spacing, keep indent X so
            // sub-section rows stack a second indent layer (same as Master)
            if (isOpen)
            {
                var headerMax = ImGui.GetItemRectMax();
                var contentGap = MetricsScope.Scale(ItemSpacingY);
                var indentX = spec.IndentUnits > 0f
                    ? cursorX + MetricsScope.UnitWidth * spec.IndentUnits
                    : cursorX;
                ImGui.SetCursorScreenPos(new Vector2(indentX, headerMax.Y + contentGap));
            }

            // Hover outline
            var outline = new SlapOutlineGroup();
            outline.CaptureLastItem(
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled),
                selected: false,
                accent: rt.ResolveOutlineAccent(spec.Variant),
                rounding: SlapCorners.ControlRadius);
            outline.Flush(spec.Key, 1f);

            // Tooltip
            var headerHovered = HoverArbitration.Current.CaptureHover(
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled));
            var titleDegraded = ImGui.CalcTextSize(spec.Title).X > headerAvailableWidth;
            var tooltip = ResponsiveTooltip.Compose(titleDegraded, spec.Title, spec.Tooltip);
            HoverArbitration.Current.TryShowTooltip(headerHovered, tooltip);

            return new DrawerResult(isOpen, wasOpen != isOpen);
        }
        finally
        {
            ImGui.PopID();
        }
    }

}
