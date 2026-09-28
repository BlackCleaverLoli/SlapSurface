using System;
using System.Collections.Generic;

namespace SlapSurface;

/// <summary>
/// A responsive slot whose control owns measurement and drawing at the
/// allocated width.
/// </summary>
internal readonly record struct ResponsiveSlotEntry(
    ResponsiveWidthRange WidthRange,
    /// <summary>
    /// Degradation priority. Higher values shrink and hide later, so a
    /// protected slot keeps its width while other slots still have room.
    /// Default slots use 0.
    /// </summary>
    int Priority,
    Action<float> DrawAtWidth
);

/// <summary>
/// Declarative control group for one edge of a responsive row. Slots are
/// registered from the outer edge inward and retain that order when drawn.
/// </summary>
internal sealed class ResponsiveSlotGroup
{
    private readonly List<ResponsiveSlotEntry> entries = new();

    internal ResponsiveSlotGroup()
    {
    }

    internal IReadOnlyList<ResponsiveSlotEntry> Entries => entries;

    public void Dropdown(
        DropdownSpec spec,
        Action<DropdownResult>? onResult = null)
    {
        var widthRange = DropdownComponent.MeasureResponsiveWidth(spec);
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = DropdownComponent.Draw(spec with { ResolvedWidth = width });
            onResult?.Invoke(result);
        }));
    }

    public void TextInput(
        TextInputSpec spec,
        string currentValue,
        int maxLength = 256,
        Action<TextInputResult>? onResult = null)
    {
        var widthRange = TextInputComponent.MeasureResponsiveWidth(spec);
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = TextInputComponent.Draw(
                spec with { ResolvedWidth = width },
                currentValue,
                maxLength);
            onResult?.Invoke(result);
        }));
    }

    /// <summary>
    /// Add a collapsible text input: an icon-only trigger button that expands
    /// into the input on click and reverts when it loses focus with empty text.
    /// While expanded the slot degrades later than normal slots, so other row
    /// controls shrink or hide first.
    /// </summary>
    public void CollapsibleTextInput(
        CollapsibleTextInputSpec spec,
        string currentValue,
        int maxLength = 256,
        Action<CollapsibleTextInputResult>? onResult = null)
    {
        var widthRange = CollapsibleTextInputComponent.MeasureResponsiveWidth(spec);
        var priority = CollapsibleTextInputComponent.ResolvePriority(spec);
        entries.Add(new ResponsiveSlotEntry(widthRange, priority, width =>
        {
            var result = CollapsibleTextInputComponent.Draw(
                spec with { ResolvedWidth = width },
                currentValue,
                maxLength);
            onResult?.Invoke(result);
        }));
    }

    /// <summary>
    /// Add a reusable filter trio: an icon-only dropdown trigger on the outer
    /// edge, caller-declared middle controls, and — while
    /// <see cref="FilterTrioSpec.IsActive"/> — a flat split button
    /// (dropdown trigger + clear action) on the inner edge. Both triggers open
    /// the same option list; results are forwarded through one callback.
    /// </summary>
    public void FilterTrio(
        FilterTrioSpec spec,
        Action<ResponsiveSlotGroup> collectMiddle,
        Action<FilterTrioResult>? onResult = null)
    {
        AddIconDropdownEntry(spec, onResult);
        collectMiddle(this);

        if (spec.IsActive)
            AddClearableDropdownEntry(spec, onResult);
    }

    private void AddIconDropdownEntry(
        FilterTrioSpec spec,
        Action<FilterTrioResult>? onResult)
    {
        var iconSpec = new IconDropdownSpec(
            $"{spec.Key.Value}_trigger",
            spec.Options,
            spec.SelectedIndex,
            spec.Tooltip,
            spec.TriggerIcon);
        var widthRange = IconDropdownComponent.MeasureResponsiveWidth(iconSpec);
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = IconDropdownComponent.Draw(iconSpec with { ResolvedWidth = width });
            onResult?.Invoke(new FilterTrioResult(
                result.SelectedIndex,
                result.Changed,
                Cleared: false));
        }));
    }

    private void AddClearableDropdownEntry(
        FilterTrioSpec spec,
        Action<FilterTrioResult>? onResult)
    {
        var splitSpec = new ClearableDropdownSpec(
            $"{spec.Key.Value}_split",
            spec.Options,
            spec.SelectedIndex,
            spec.Tooltip,
            ClearTooltip: spec.ClearTooltip,
            Variant: spec.ClearableVariant);
        var widthRange = ClearableDropdownComponent.MeasureResponsiveWidth(splitSpec);
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = ClearableDropdownComponent.Draw(splitSpec with { ResolvedWidth = width });
            onResult?.Invoke(new FilterTrioResult(
                result.SelectedIndex,
                result.Changed,
                result.Cleared));
        }));
    }

    public void AutocompleteTextInput(
        AutocompleteTextInputSpec spec,
        AutocompleteTextInputState state,
        string currentValue,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider,
        int maxLength = 256,
        Action<AutocompleteTextInputResult>? onResult = null
    )
    {
        var widthRange = TextInputComponent.MeasureResponsiveWidth(spec.Input);
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = AutocompleteTextInputComponent.Draw(
                spec with
                {
                    Input = spec.Input with { ResolvedWidth = width },
                },
                state,
                currentValue,
                optionsProvider,
                maxLength
            );
            onResult?.Invoke(result);
        }));
    }

    public void CollapsibleAutocompleteTextInput(
        CollapsibleAutocompleteTextInputSpec spec,
        AutocompleteTextInputState state,
        string currentValue,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider,
        int maxLength = 256,
        Action<AutocompleteTextInputResult>? onResult = null)
    {
        var widthRange = CollapsibleAutocompleteTextInputComponent.MeasureResponsiveWidth(spec);
        var priority = CollapsibleAutocompleteTextInputComponent.ResolvePriority(spec);
        entries.Add(new ResponsiveSlotEntry(widthRange, priority, width =>
        {
            var resolved = spec.Autocomplete with
            {
                Input = spec.Autocomplete.Input with { ResolvedWidth = width },
            };
            var result = CollapsibleAutocompleteTextInputComponent.Draw(
                spec with { Autocomplete = resolved },
                state,
                currentValue,
                optionsProvider,
                maxLength);
            onResult?.Invoke(result);
        }));
    }

    public void Button(
        ButtonSpec spec,
        Action<ControlResult>? onResult = null)
    {
        var widthRange = ButtonComponent.MeasureResponsiveWidth(spec);
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = ButtonComponent.Draw(spec with { ResolvedWidth = width });
            onResult?.Invoke(result);
        }));
    }

    /// <summary>
    /// Add a tab strip as a responsive slot. The strip prefers its natural
    /// tab width and degrades to visible tabs + overflow popup under pressure.
    /// </summary>
    public void TabStrip(
        TabStripSpec spec,
        Action<TabStripResult>? onResult = null)
    {
        var widthRange = TabStripComponent.MeasureResponsiveWidth(spec);
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = TabStripComponent.Draw(spec with { ResolvedWidth = width });
            onResult?.Invoke(result);
        }));
    }

    public void Checkbox(
        CheckboxSpec spec,
        Action<CheckboxResult>? onResult = null
    )
    {
        var width = CheckboxComponent.ResolveNaturalWidth(spec);
        entries.Add(new ResponsiveSlotEntry(
            new ResponsiveWidthRange(width, width),
            0,
            _ => onResult?.Invoke(CheckboxComponent.Draw(spec))
        ));
    }

    public void ToggleSwitch(
        ToggleSwitchSpec spec,
        Action<ToggleSwitchResult>? onResult = null)
    {
        var widthRange = ToggleSwitchComponent.MeasureResponsiveWidth(spec);
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = ToggleSwitchComponent.Draw(spec with { ResolvedWidth = width });
            onResult?.Invoke(result);
        }));
    }

    public void SplitButton(
        SplitButtonSpec spec,
        Action<SplitButtonResult>? onResult = null)
    {
        var widthRange = SplitButtonComponent.MeasureResponsiveWidth(spec);
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = SplitButtonComponent.Draw(spec with { ResolvedWidth = width });
            onResult?.Invoke(result);
        }));
    }

    public void Badge(
        BadgeSpec spec,
        Action<ControlResult>? onResult = null)
        => Badge(spec, float.PositiveInfinity, onResult);

    /// <summary>
    /// Add a responsive badge whose preferred width is capped by
    /// <paramref name="maxWidth"/> without crossing its structural minimum.
    /// </summary>
    public void Badge(
        BadgeSpec spec,
        float maxWidth,
        Action<ControlResult>? onResult = null)
    {
        var widthRange = BadgeComponent.MeasureResponsiveWidth(spec);
        if (maxWidth > 0f && !float.IsPositiveInfinity(maxWidth))
        {
            widthRange = new ResponsiveWidthRange(
                MathF.Max(
                    widthRange.MinimumWidth,
                    MathF.Min(widthRange.PreferredWidth, maxWidth)
                ),
                widthRange.MinimumWidth
            );
        }
        entries.Add(new ResponsiveSlotEntry(widthRange, 0, width =>
        {
            var result = BadgeComponent.Draw(spec with { ResolvedWidth = width });
            onResult?.Invoke(result);
        }));
    }

    /// <summary>
    /// Add a drag-float slider as a responsive slot. The slot's preferred width
    /// comes from <see cref="DragFloatSpec.WidthUnits"/> (defaulting to two
    /// units); its structural minimum is a one-unit square.
    /// </summary>
    public void DragFloat(
        DragFloatSpec spec,
        float value,
        Action<DragFloatResult>? onResult = null)
    {
        var widthRange = DragFloatComponent.MeasureResponsiveWidth(spec);
        entries.Add(new ResponsiveSlotEntry(
            widthRange,
            0,
            width =>
            {
                var result = DragFloatComponent.Draw(
                    spec with { ResolvedWidth = width },
                    value);
                onResult?.Invoke(result);
            }));
    }
}

/// <summary>
/// Declarative left and right groups for a two-sided responsive row.
/// </summary>
internal sealed class ResponsiveRowBuilder
{
    internal ResponsiveRowBuilder()
    {
        Left = new ResponsiveSlotGroup();
        Right = new ResponsiveSlotGroup();
    }

    public ResponsiveSlotGroup Left { get; }
    public ResponsiveSlotGroup Right { get; }
}
