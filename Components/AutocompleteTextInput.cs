using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Bindings.ImGui;

namespace SlapSurface;

internal readonly record struct AutocompleteOptionSpec(
    ControlKey Key,
    string Label,
    uint GameIconId = 0,
    string? Tooltip = null
);

internal readonly record struct AutocompleteTextInputSpec(
    TextInputSpec Input,
    long OptionsVersion = 0,
    float MaxPopupHeightUnits = 6f,
    LayeredShadowSpec PopupShadow = default
);

internal readonly record struct AutocompleteTextInputResult(
    string Value,
    bool Changed,
    bool Cleared,
    bool Focused,
    bool Deactivated,
    AutocompleteOptionSpec? SelectedOption
);

internal sealed class AutocompleteTextInputState
{
    private string cachedValue = string.Empty;
    private long cachedVersion = long.MinValue;
    private IReadOnlyList<AutocompleteOptionSpec> options = [];

    internal int HighlightedIndex { get; set; } = -1;

    internal IReadOnlyList<AutocompleteOptionSpec> ResolveOptions(
        string value,
        long version,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> provider
    )
    {
        if (
            cachedVersion == version
            && string.Equals(cachedValue, value, StringComparison.Ordinal)
        )
        {
            return options;
        }

        cachedValue = value;
        cachedVersion = version;
        options = provider(value) ?? [];
        HighlightedIndex = -1;
        return options;
    }
}

internal static class AutocompleteTextInputComponent
{
    internal static AutocompleteTextInputResult Draw(
        AutocompleteTextInputSpec spec,
        AutocompleteTextInputState state,
        string value,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider,
        int maxLength = 256
    )
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(optionsProvider);

        var input = TextInputComponent.Draw(
            spec.Input with { ReportEveryChange = true },
            value,
            maxLength
        );
        var nextValue = input.Value;
        var changed = input.Changed;
        var options = state.ResolveOptions(
            nextValue,
            spec.OptionsVersion,
            optionsProvider
        );
        var popupId = $"{spec.Input.Key.Value}##autocompletePopup";

        var selectedIndex = ResolveKeyboardSelection(input, state, options);
        var popupOpen = ImGui.IsPopupOpen(popupId);
        var shouldOpen = options.Count > 0
            && (input.Focused || input.Cleared)
            && (input.Activated || input.Changed || input.Cleared);
        if (shouldOpen && !popupOpen)
        {
            ImGui.OpenPopup(popupId);
            popupOpen = true;
        }

        if (popupOpen && options.Count > 0)
        {
	            var measureItems = options
	                .Select((option, index) => new PopupMeasureItem(
	                    option.Label ?? string.Empty,
	                    HasLeadingIcon: option.GameIconId != 0,
	                    HasTrailingIcon: false,
	                    IsSelected: false))
	                .ToList();

	            using var popup = PopupPanelComponent.Begin(
	                new PopupPanelSpec(
	                    popupId,
	                    Variant: spec.Input.Variant,
	                    Shadow: LayeredShadowSpec.ResolveStandard(spec.PopupShadow),
	                    MaxHeightUnits: spec.MaxPopupHeightUnits,
	                    MeasureItems: measureItems,
                    Anchor: new(input.Min.X, input.Max.Y),
                    ResolvedWidth: input.Size.X,
                    FocusOnOpen: false
                )
            );
            if (popup.IsOpen)
            {
                for (var i = 0; i < options.Count; i++)
                {
                    var option = options[i];
                    var item = popup.Item(
                        new PopupItemSpec(
                            option.Key,
                            option.Label,
                            Variant: spec.Input.Variant,
                            State: i == state.HighlightedIndex
                                ? ControlState.Highlighted
                                : ControlState.None,
                            Tooltip: option.Tooltip,
                            GameIconId: option.GameIconId
                        )
                    );
                    if (item.Hovered)
                        state.HighlightedIndex = i;
                    // Accept on mouse-down too so the selection is applied in the
                    // same frame the text input reports deactivation.
                    if (item.Active || item.Clicked)
                        selectedIndex = i;
                }

                if (selectedIndex >= 0 && selectedIndex < options.Count)
                    ImGui.CloseCurrentPopup();
            }
        }

        AutocompleteOptionSpec? selected = null;
        if (selectedIndex >= 0 && selectedIndex < options.Count)
        {
            selected = options[selectedIndex];
            nextValue = selected.Value.Label;
            changed = true;
            state.HighlightedIndex = -1;
        }

        return new AutocompleteTextInputResult(
            nextValue,
            changed,
            input.Cleared,
            input.Focused,
            input.Deactivated,
            selected
        );
    }

    private static int ResolveKeyboardSelection(
        TextInputResult input,
        AutocompleteTextInputState state,
        IReadOnlyList<AutocompleteOptionSpec> options
    )
    {
        if (!input.Focused || options.Count == 0)
            return -1;

        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow, true))
        {
            state.HighlightedIndex = state.HighlightedIndex < 0
                ? 0
                : (state.HighlightedIndex + 1) % options.Count;
        }
        else if (ImGui.IsKeyPressed(ImGuiKey.UpArrow, true))
        {
            state.HighlightedIndex = state.HighlightedIndex < 0
                ? options.Count - 1
                : (state.HighlightedIndex - 1 + options.Count) % options.Count;
        }

        var confirmPressed = ImGui.IsKeyPressed(ImGuiKey.Enter, false)
            || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false);
        return confirmPressed ? state.HighlightedIndex : -1;
    }
}
