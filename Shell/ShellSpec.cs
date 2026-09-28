using System.Numerics;
using Dalamud.Interface;

namespace SlapSurface;

// ── Shell ────────────────────────────────────────────────

/// <summary>
/// Optional stateless two-slot layout: sidebar + content.
/// The caller owns selected-page state; Shell does not own routing or configuration.
/// </summary>
internal readonly record struct ShellSpec(
    ControlKey Key,
    ShellSize Size = default,
    float SidebarWidth = 4f,
    float Gap = 0f,
    bool SurfaceBg = true,
    LayeredShadowSpec Shadow = default
);

// ── ShellSize ────────────────────────────────────────────

internal enum ShellSizeKind
{
    FillAvailable = 0,
}

internal readonly record struct ShellSize
{
    public ShellSizeKind Kind { get; }

    private ShellSize(ShellSizeKind kind)
    {
        Kind = kind;
    }

    public static ShellSize FillAvailable => new(ShellSizeKind.FillAvailable);
}

// ── SidebarItem ──────────────────────────────────────────

/// <summary>
/// A single v2-style rounded tab item in the shell sidebar. <see cref="Selected"/>
/// drives the full selected fill; <see cref="Disabled"/> greys out the item and prevents clicks.
/// <see cref="GameIconId"/> takes visual priority over <see cref="Icon"/> when its
/// texture is available; otherwise <see cref="Icon"/> is used as the fallback.
/// </summary>
internal readonly record struct SidebarItemSpec(
    ControlKey Key,
    string Label,
    bool Selected = false,
    bool Disabled = false,
    Variant Variant = Variant.Base,
    ControlState State = ControlState.None,
    FontAwesomeIcon? Icon = null,
    string? Tooltip = null,
    /// <summary>
    /// When true, the label is shown as a tooltip whenever it is not fully
    /// visible (icon-only collapsed state or clipped/faded text).
    /// </summary>
    bool AutoLabelTooltip = false,
    uint GameIconId = 0
);

// ── SidebarSeparator ─────────────────────────────────────

internal readonly record struct SidebarSeparatorSpec(ControlKey Key, float Gap = 8f);

// ── SidebarScrollPanel ──────────────────────────────────

/// <summary>
/// A scrollable sidebar navigation region with no visible scrollbar, background,
/// border, padding, or additional item spacing.
/// </summary>
internal readonly record struct SidebarScrollPanelSpec(ControlKey Key);

// ── SidebarIconBar ───────────────────────────────────────

/// <summary>
/// A bottom-aligned, horizontally flowing icon-only command bar for a shell sidebar.
/// The transparent bar owns its padding and item positioning without adding a
/// separate background or border around the commands.
/// </summary>
internal readonly record struct SidebarIconBarSpec(
    ControlKey Key,
    Vector2 Padding = default,
    float Gap = 4f
);

/// <summary>A single caller-defined icon command inside a <see cref="SidebarIconBarSpec"/>.</summary>
internal readonly record struct SidebarIconButtonSpec(
    ControlKey Key,
    FontAwesomeIcon Icon,
    string? Tooltip = null,
    bool Disabled = false,
    Variant Variant = Variant.FlatNoBorder
);

/// <summary>
/// An explicit sidebar collapse/expand command for a <see cref="SidebarIconBarSpec"/>.
/// The icon, disabled state, and tooltip are resolved from the current sidebar layout;
/// callers receive the requested collapse state and own persistence.
/// </summary>
internal readonly record struct SidebarCollapseToggleSpec(
    ControlKey Key,
    string? CollapseTooltip = null,
    string? ExpandTooltip = null,
    Variant Variant = Variant.FlatNoBorder
);

// ── ShellBounds ──────────────────────────────────────────

/// <summary>
/// Read-only screen-space bounds for shell sidebar and content slots.
/// Captured once per frame at <see cref="SlapSurface.BeginShell"/> time;
/// valid for overlay positioning within the current frame.
/// </summary>
internal readonly struct ShellBounds
{
    /// <summary>Sidebar slot top-left in screen coordinates.</summary>
    public Vector2 SidebarMin { get; }

    /// <summary>Sidebar slot bottom-right in screen coordinates.</summary>
    public Vector2 SidebarMax { get; }

    /// <summary>Content slot top-left in screen coordinates.</summary>
    public Vector2 ContentMin { get; }

    /// <summary>Content slot bottom-right in screen coordinates.</summary>
    public Vector2 ContentMax { get; }

    /// <summary>Sidebar slot size.</summary>
    public Vector2 SidebarSize => SidebarMax - SidebarMin;

    /// <summary>Content slot size.</summary>
    public Vector2 ContentSize => ContentMax - ContentMin;

    internal ShellBounds(Vector2 sbMin, Vector2 sbMax, Vector2 ctMin, Vector2 ctMax)
    {
        SidebarMin = sbMin;
        SidebarMax = sbMax;
        ContentMin = ctMin;
        ContentMax = ctMax;
    }
}
