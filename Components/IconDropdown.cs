using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;

namespace SlapSurface;

/// <summary>
/// Specification for an icon-only dropdown trigger: a compact square button
/// that opens an option popup on click without showing a label or arrow.
/// </summary>
internal readonly record struct IconDropdownSpec(
    ControlKey Key,
    IReadOnlyList<DropdownOptionSpec> Options,
    int SelectedIndex = 0,
    string? Tooltip = null,
    FontAwesomeIcon? Icon = null,
    uint GameIconId = 0,
    Variant Variant = Variant.FlatNoBorder,
    ControlState State = ControlState.None,
    /// <summary>
    /// Resolved trigger width in scaled pixels, set by an external layout system
    /// (e.g. <see cref="ResponsiveSlotGroup"/>). When &gt; 0, bypasses the
    /// default square size.
    /// </summary>
    float ResolvedWidth = 0f,
    /// <summary>
    /// Optional option source invoked only while the popup is opening or open.
    /// Use this when enumerating the full option list is expensive; the static
    /// <see cref="Options"/> still supplies the closed trigger preview.
    /// </summary>
    Func<DropdownOptionsSnapshot>? OpenOptionsProvider = null
);

/// <summary>Result of an icon-only dropdown trigger frame.</summary>
internal readonly record struct IconDropdownResult(
    int SelectedIndex,
    bool Changed
);

internal static class IconDropdownComponent
{
    private const string PopupId = "##iconDropdownPopup";

    internal static ResponsiveWidthRange MeasureResponsiveWidth(
        IconDropdownSpec spec)
    {
        var unit = MetricsScope.UnitHeight;
        return ResponsiveWidthRange.Create(unit, unit, unit);
    }

    public static IconDropdownResult Draw(IconDropdownSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "IconDropdownSpec.Key.Value must not be empty.",
                nameof(spec));
        if (spec.Options.Count == 0)
            return new IconDropdownResult(-1, false);

        ImGui.PushID(spec.Key.Value);
        try
        {
            var triggerResult = ButtonComponent.Draw(
                new ButtonSpec(
                    spec.Key,
                    Label: null,
                    ButtonSize.Square(1f),
                    spec.Variant,
                    spec.State,
                    spec.Tooltip,
                    spec.Icon,
                    ResolvedWidth: spec.ResolvedWidth > 0f ? spec.ResolvedWidth : 0f,
                    GameIconId: spec.GameIconId));

            var popupOpen = ImGui.IsPopupOpen(PopupId);
            if (triggerResult.Clicked)
                ImGui.OpenPopup(PopupId);

            var popupOptions = spec.Options;
            var popupSelectedIndex = Math.Clamp(
                spec.SelectedIndex,
                0,
                popupOptions.Count - 1);
            if (spec.OpenOptionsProvider != null && (popupOpen || triggerResult.Clicked))
            {
                var snapshot = spec.OpenOptionsProvider();
                if (snapshot.Options.Count > 0)
                {
                    popupOptions = snapshot.Options;
                    popupSelectedIndex = Math.Clamp(
                        snapshot.SelectedIndex,
                        0,
                        popupOptions.Count - 1);
                }
            }

            var nextSelected = popupSelectedIndex;
            DropdownPopupHelper.Draw(
                PopupId,
                popupOptions,
	                popupSelectedIndex,
	                triggerResult.Min,
	                triggerResult.Max,
	                spec.Variant,
	                ref nextSelected);

            return new IconDropdownResult(
                nextSelected,
                nextSelected != popupSelectedIndex);
        }
        finally
        {
            ImGui.PopID();
        }
    }
}
