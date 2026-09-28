using System;

namespace SlapSurface;

internal readonly record struct ControlKey(string Value)
{
    public static implicit operator ControlKey(string value) => new(value);
}

/// <summary>
/// Structural control variant. Determines fill color and default text color;
/// semantic meaning (danger, info) is expressed via <see cref="TextSlot"/>
/// overlay on text and border, not by adding more variants.
/// </summary>
internal enum Variant
{
    /// <summary>Default control: fill=<see cref="SlapTheme.BaseFill"/>,
    /// text=<see cref="SlapTheme.BaseText"/>.</summary>
    Base = 0,

    /// <summary>Primary action: fill=<see cref="SlapTheme.ActionFill"/>,
    /// text=<see cref="SlapTheme.ActionText"/>.</summary>
    Action,

    /// <summary>Sidebar selected tab: fill=<see cref="SlapTheme.SidebarFill"/>,
    /// text=<see cref="SlapTheme.SidebarText"/>.</summary>
    Sidebar,

    /// <summary>Tab strip default: fill=<see cref="SlapTheme.TabFill"/>,
    /// text=<see cref="SlapTheme.TabText"/>.</summary>
    Tab,

    /// <summary>Flat control: fill=<see cref="SlapTheme.SurfaceBg"/>,
    /// text=<see cref="SlapTheme.Body"/>. For secondary actions that should
    /// blend into the surface rather than stand out.</summary>
    Flat,

    /// <summary>Flat without border: same fill/text as <see cref="Flat"/>,
    /// but no control frame border is drawn.</summary>
    FlatNoBorder,
}

/// <summary>
/// Border visual strength for a control family. Rows use
/// <see cref="Subtle"/>, buttons and inputs use <see cref="Standard"/>,
/// and chips with visually independent borders use <see cref="Strong"/>.
/// </summary>
internal enum BorderStrength
{
    Subtle = 0,
    Standard = 1,
    Strong = 2,
}

/// <summary>
/// Semantic text/border accent. When applied to a control, overrides its
/// default text and border color with the corresponding slot value.
/// </summary>
internal enum TextSlot
{
    Body     = 0,
    Subtle,
    Emphasis,
    Danger,
}

/// <summary>
/// Resolved interaction mode used by custom-drawn control content.
/// The owning control derives this from its single hit-test source so nested
/// visuals do not need to repeat hover or active detection.
/// </summary>
internal enum ControlVisualMode
{
    Base = 0,
    Hovered,
    Active,
    Disabled,
}

[Flags]
internal enum ControlState
{
	    None      = 0,
	    Disabled  = 1 << 0,
	    Selected  = 1 << 1,
	    Emphasis  = 1 << 2,
	    Highlighted = 1 << 3
	}
