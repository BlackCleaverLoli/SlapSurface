using System.Collections.Generic;
using Dalamud.Interface;

namespace SlapSurface;

/// <summary>
/// Specification for a reusable filter trio: an icon-only dropdown trigger on
/// the outer edge, caller-declared middle controls, and an optional split
/// button (text trigger + clear action) on the inner edge while a filter
/// is active. The composite owns slot order and conditional visibility; the
/// caller owns option meaning, the active state, and result mapping.
/// </summary>
internal readonly record struct FilterTrioSpec(
    ControlKey Key,
    IReadOnlyList<DropdownOptionSpec> Options,
    int SelectedIndex = 0,
    bool IsActive = false,
    string? Tooltip = null,
    FontAwesomeIcon? TriggerIcon = null,
    string? ClearTooltip = "",
    Variant ClearableVariant = Variant.Base
);

/// <summary>Result of a filter trio frame.</summary>
internal readonly record struct FilterTrioResult(
    int SelectedIndex,
    bool Changed,
    bool Cleared
);
