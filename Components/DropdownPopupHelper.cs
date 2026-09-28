using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Shared option-popup protocol for dropdown-family controls. Draws the popup
/// panel anchored below the owning trigger and reports the clicked option index.
/// </summary>
internal static class DropdownPopupHelper
{
    internal static void Draw(
        string popupId,
        IReadOnlyList<DropdownOptionSpec> popupOptions,
        int popupSelectedIndex,
        Vector2 buttonMin,
	        Vector2 buttonMax,
	        Variant variant,
	        ref int nextSelected)
	    {
	        var measureItems = popupOptions
	            .Select((option, index) => new PopupMeasureItem(
	                option.Label ?? string.Empty,
	                HasLeadingIcon: option.GameIconId != 0 || option.Icon.HasValue,
	                HasTrailingIcon: false,
	                IsSelected: index == popupSelectedIndex))
	            .ToList();

	        using var popup = PopupPanelComponent.Begin(
	            new PopupPanelSpec(
	                popupId,
	                variant,
	                MeasureItems: measureItems,
	                Anchor: new Vector2(buttonMin.X, buttonMax.Y),
	                TriggerTopLeft: buttonMin));
        if (!popup.IsOpen)
            return;

        for (var i = 0; i < popupOptions.Count; i++)
        {
            var option = popupOptions[i];
            var state = option.State;
            if (i == popupSelectedIndex)
                state |= ControlState.Selected;

            var result = popup.Item(
                new PopupItemSpec(
                    $"option{i}",
                    option.Label,
                    option.Icon,
	                    variant,
                    state,
                    option.Tooltip,
                    GameIconId: option.GameIconId));
            if (result.Clicked)
            {
                nextSelected = i;
                ImGui.CloseCurrentPopup();
            }

            if (i == popupSelectedIndex)
                ImGui.SetItemDefaultFocus();
        }
    }
}
