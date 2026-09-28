using System;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;

namespace SlapSurface;

/// <summary>
/// Specification for a collapsible text input: it renders as an icon-only
/// trigger button until clicked, then expands into a text input. The input
/// reverts to the trigger button when it loses focus (or commits with Enter /
/// cancels with Escape) while the text is empty. The expanded width follows
/// <see cref="WidthUnits"/> like a normal responsive text input. A trailing
/// clear action is standard while the input has text; callers can opt out with
/// <see cref="Clearable"/> set to false.
/// </summary>
internal readonly record struct CollapsibleTextInputSpec(
    ControlKey Key,
    string Placeholder = "",
    string? Tooltip = null,
    float WidthUnits = 0f,
    Variant Variant = Variant.Base,
    bool ReportEveryChange = false,
    bool AutoSelectAll = false,
    FontAwesomeIcon? Icon = null,
    /// <summary>
    /// Resolved input width in scaled pixels, set by an external layout system
    /// (e.g. <see cref="ResponsiveSlotGroup"/>). When &gt; 0, takes precedence
    /// over <see cref="WidthUnits"/> while the input is expanded.
    /// </summary>
    float ResolvedWidth = 0f,
    /// <summary>Show a trailing clear action while the input has text. On by default.</summary>
    bool Clearable = true,
    string? ClearTooltip = "",
    /// <summary>
    /// When false, the expanded input's frame border renders in the danger
    /// semantic color to signal invalid input. Defaults to true.
    /// </summary>
    bool IsValid = true,
    ControlState State = ControlState.None
);

/// <summary>Result of a collapsible text input frame.</summary>
internal readonly record struct CollapsibleTextInputResult(
    string Value,
    bool Changed,
    bool Entered,
    bool Canceled,
    bool Deactivated,
    bool IsExpanded,
    bool Cleared
);

internal static class CollapsibleTextInputComponent
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
        CollapsibleTextInputSpec spec)
    {
        if (!IsExpanded(spec.Key.Value))
            return ButtonComponent.MeasureResponsiveWidth(ToButtonSpec(spec));
        return TextInputComponent.MeasureResponsiveWidth(ToInputSpec(spec));
    }

    internal static int ResolvePriority(CollapsibleTextInputSpec spec) =>
        IsExpanded(spec.Key.Value) ? 1 : 0;

    public static CollapsibleTextInputResult Draw(
        CollapsibleTextInputSpec spec,
        string value,
        int maxLength = 256)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException(
                "CollapsibleTextInputSpec.Key.Value must not be empty.",
                nameof(spec));

        var key = spec.Key.Value;
        if (!IsExpanded(key))
        {
            var clicked = ButtonComponent.Draw(ToButtonSpec(spec)).Clicked;
            if (clicked)
            {
                ExpandedByKey[key] = true;
                FocusFramesByKey[key] = 2;
            }

            return new CollapsibleTextInputResult(
                value,
                false,
                false,
                false,
                false,
                false,
                false);
        }

        var focusFrames = FocusFramesByKey.TryGetValue(key, out var cachedFrames)
            ? cachedFrames
            : 0;
        if (focusFrames > 0)
        {
            ImGui.SetKeyboardFocusHere();
            FocusFramesByKey[key] = focusFrames - 1;
        }

        var result = TextInputComponent.Draw(
            ToInputSpec(spec) with { ShowClearWhenEmpty = string.IsNullOrEmpty(value) },
            value,
            maxLength);

        var collapsed =
            ((result.Deactivated || result.Entered || result.Canceled)
                && string.IsNullOrEmpty(result.Value))
            || (result.Cleared && string.IsNullOrEmpty(value));
        if (collapsed)
        {
            ExpandedByKey.Remove(key);
            FocusFramesByKey.Remove(key);
        }

        return new CollapsibleTextInputResult(
            result.Value,
            result.Changed,
            result.Entered,
            result.Canceled,
            result.Deactivated,
            !collapsed,
            result.Cleared);
    }

    private static bool IsExpanded(string key) =>
        ExpandedByKey.TryGetValue(key, out var expanded) && expanded;

    private static ButtonSpec ToButtonSpec(CollapsibleTextInputSpec spec) =>
        new(
            spec.Key,
            null,
            Variant: Variant.FlatNoBorder,
            State: spec.State,
            Tooltip: spec.Tooltip ?? spec.Placeholder,
            Icon: spec.Icon);

    private static TextInputSpec ToInputSpec(CollapsibleTextInputSpec spec) =>
        new(
            spec.Key,
            spec.Placeholder,
            spec.Tooltip,
            WidthUnits: spec.WidthUnits,
            Variant: spec.Variant,
            ReportEveryChange: spec.ReportEveryChange,
            AutoSelectAll: spec.AutoSelectAll,
            Icon: spec.Icon,
            ResolvedWidth: spec.ResolvedWidth,
            Clearable: spec.Clearable,
            ClearTooltip: spec.ClearTooltip,
            IsValid: spec.IsValid,
            State: spec.State);
}

internal static partial class Slap
{
    /// <summary>
    /// Draw a collapsible text input: an icon-only trigger button that expands
    /// into the input on click and reverts when it loses focus with empty text.
    /// </summary>
    public static CollapsibleTextInputResult CollapsibleTextInput(
        CollapsibleTextInputSpec spec,
        string value,
        int maxLength = 256) =>
        CollapsibleTextInputComponent.Draw(spec, value, maxLength);

    /// <summary>
    /// Clears the static expand/focus state of collapsible text inputs. Host
    /// windows should call this when they hide, so an input left open at hide
    /// time cannot re-expand on the next open.
    /// </summary>
    public static void ResetCollapsibleTextInputState() =>
        CollapsibleTextInputComponent.Reset();

    /// <summary>
    /// Requests that the collapsible text input identified by
    /// <paramref name="key"/> expand and receive keyboard focus on its next
    /// frame. Callers use this when a navigation or programmatic text prefill
    /// should reveal the input instead of leaving it collapsed.
    /// </summary>
    public static void RequestCollapsibleTextInputExpansion(ControlKey key) =>
        CollapsibleTextInputComponent.RequestExpansion(key.Value);
}
