using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace SlapSurface;

internal readonly record struct CollapsibleAutocompleteTextInputSpec(
    AutocompleteTextInputSpec Autocomplete,
    string? Tooltip = null,
    ControlState State = ControlState.None
);

internal static class CollapsibleAutocompleteTextInputComponent
{
    private static readonly Dictionary<string, bool> ExpandedByKey = new();
    private static readonly Dictionary<string, int> FocusFramesByKey = new();

    internal static void Reset()
    {
        ExpandedByKey.Clear();
        FocusFramesByKey.Clear();
    }

    internal static void RequestExpansion(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        ExpandedByKey[key] = true;
        FocusFramesByKey[key] = 2;
    }

    internal static ResponsiveWidthRange MeasureResponsiveWidth(
        CollapsibleAutocompleteTextInputSpec spec)
    {
        if (!IsExpanded(spec.Autocomplete.Input.Key.Value))
            return ButtonComponent.MeasureResponsiveWidth(BuildButtonSpec(spec));
        return TextInputComponent.MeasureResponsiveWidth(spec.Autocomplete.Input);
    }

    internal static int ResolvePriority(CollapsibleAutocompleteTextInputSpec spec) =>
        IsExpanded(spec.Autocomplete.Input.Key.Value) ? 1 : 0;

    internal static AutocompleteTextInputResult Draw(
        CollapsibleAutocompleteTextInputSpec spec,
        AutocompleteTextInputState state,
        string value,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider,
        int maxLength = 256)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(optionsProvider);

        var key = spec.Autocomplete.Input.Key.Value;
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException(
                "CollapsibleAutocompleteTextInputSpec.Autocomplete.Input.Key.Value must not be empty.",
                nameof(spec));

        if (!IsExpanded(key))
        {
            var clicked = ButtonComponent.Draw(BuildButtonSpec(spec)).Clicked;
            if (clicked)
            {
                ExpandedByKey[key] = true;
                FocusFramesByKey[key] = 2;
            }

            return new AutocompleteTextInputResult(value, false, false, false, false, null);
        }

        var focusFrames = FocusFramesByKey.TryGetValue(key, out var cachedFrames)
            ? cachedFrames
            : 0;
        if (focusFrames > 0)
        {
            ImGui.SetKeyboardFocusHere();
            FocusFramesByKey[key] = focusFrames - 1;
        }

        var result = AutocompleteTextInputComponent.Draw(
            spec.Autocomplete with
            {
                Input = spec.Autocomplete.Input with
                {
                    ShowClearWhenEmpty = string.IsNullOrEmpty(value),
                },
            },
            state,
            value,
            optionsProvider,
            maxLength);

        var collapsed =
            (result.Deactivated && string.IsNullOrEmpty(result.Value))
            || (result.Cleared && string.IsNullOrEmpty(value));
        if (collapsed)
        {
            ExpandedByKey.Remove(key);
            FocusFramesByKey.Remove(key);
        }

        return result;
    }

    private static bool IsExpanded(string key) =>
        ExpandedByKey.TryGetValue(key, out var expanded) && expanded;

    private static ButtonSpec BuildButtonSpec(CollapsibleAutocompleteTextInputSpec spec) =>
        new(
            spec.Autocomplete.Input.Key,
            null,
            Variant: Variant.FlatNoBorder,
            State: spec.State,
            Tooltip: spec.Tooltip ?? spec.Autocomplete.Input.Placeholder,
            Icon: spec.Autocomplete.Input.Icon);
}

internal static partial class Slap
{
    public static AutocompleteTextInputResult CollapsibleAutocompleteTextInput(
        CollapsibleAutocompleteTextInputSpec spec,
        AutocompleteTextInputState state,
        string value,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider,
        int maxLength = 256) =>
        CollapsibleAutocompleteTextInputComponent.Draw(
            spec,
            state,
            value,
            optionsProvider,
            maxLength);

    public static void ResetCollapsibleAutocompleteTextInputState() =>
        CollapsibleAutocompleteTextInputComponent.Reset();

    public static void RequestCollapsibleAutocompleteTextInputExpansion(ControlKey key) =>
        CollapsibleAutocompleteTextInputComponent.RequestExpansion(key.Value);
}
