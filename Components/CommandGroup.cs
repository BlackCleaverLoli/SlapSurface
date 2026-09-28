using System;

namespace SlapSurface;

/// <summary>
/// Neutral command group. The primary control is anchored at the right edge;
/// an optional leading button is placed immediately to its left. Page-owned
/// controls can be collected into the responsive left edge by the overload on
/// <see cref="Slap.CommandGroup(ContentZoneContext, CommandGroupSpec, Action{ResponsiveSlotGroup})"/>.
/// </summary>
internal readonly record struct CommandGroupSpec(
    ButtonSpec? LeadingButton = null,
    ButtonSpec? PrimaryButton = null,
    SplitButtonSpec? PrimarySplitButton = null
);

internal readonly record struct CommandGroupResult(
    ControlResult Leading,
    ControlResult Primary,
    SplitButtonResult PrimarySplit
);

internal static class CommandGroupComponent
{
    public static CommandGroupResult Draw(
        ContentZoneContext zone,
        CommandGroupSpec spec,
        Action<ResponsiveSlotGroup>? collectLeft = null)
    {
        if (spec.PrimaryButton.HasValue && spec.PrimarySplitButton.HasValue)
        {
            throw new ArgumentException(
                "CommandGroupSpec accepts either PrimaryButton or PrimarySplitButton, not both.",
                nameof(spec)
            );
        }

        var primary = default(ControlResult);
        var primarySplit = default(SplitButtonResult);
        var leading = default(ControlResult);

        zone.ResponsiveRow(row =>
        {
            collectLeft?.Invoke(row.Left);

            // Register the right group from the outer edge inward so the
            // primary command remains the final survivor when space is tight.
            if (spec.PrimaryButton.HasValue)
            {
                row.Right.Button(spec.PrimaryButton.Value, result => primary = result);
            }
            else if (spec.PrimarySplitButton.HasValue)
            {
                row.Right.SplitButton(
                    spec.PrimarySplitButton.Value,
                    result => primarySplit = result
                );
            }

            if (spec.LeadingButton.HasValue)
                row.Right.Button(spec.LeadingButton.Value, result => leading = result);
        });

        return new CommandGroupResult(leading, primary, primarySplit);
    }
}
