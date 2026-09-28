using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlapSurface;

internal readonly record struct SlapInteractionState(
    bool Hovered,
    bool Active,
    bool Clicked,
    bool RightClicked,
    bool DoubleClicked,
    bool Disabled,
    bool Selected,
    bool Highlighted)
{
    public ControlVisualMode VisualMode =>
        SlapInteraction.ResolveVisualMode(Hovered, Active, Disabled);

    public ControlResult ToControlResult(Vector2 min, Vector2 max) =>
        new(Hovered, Active, Clicked, RightClicked, DoubleClicked, min, max);
}

internal static class SlapInteraction
{
    public static ControlVisualMode ResolveVisualMode(
        bool hovered,
        bool active,
        bool disabled) =>
        disabled ? ControlVisualMode.Disabled
        : active ? ControlVisualMode.Active
        : hovered ? ControlVisualMode.Hovered
        : ControlVisualMode.Base;

    public static SlapInteractionState Capture(
        ControlState state,
        bool rawClicked,
        bool captureRightClick = false,
        ImGuiHoveredFlags additionalHoverFlags = ImGuiHoveredFlags.None)
    {
        var disabled = state.HasFlag(ControlState.Disabled);
        var selected = state.HasFlag(ControlState.Selected);
        var highlighted = state.HasFlag(ControlState.Highlighted);
        return Capture(
            disabled,
            selected,
            rawClicked,
            captureRightClick,
            additionalHoverFlags,
            highlighted
        );
    }

    public static SlapInteractionState Capture(
        bool disabled,
        bool selected,
        bool rawClicked,
        bool captureRightClick = false,
        ImGuiHoveredFlags additionalHoverFlags = ImGuiHoveredFlags.None,
        bool highlighted = false)
    {
        var hovered = HoverArbitration.Current.CaptureHover(
            ImGui.IsItemHovered(
                ImGuiHoveredFlags.AllowWhenDisabled | additionalHoverFlags
            ));
        var active = !disabled && hovered && ImGui.IsItemActive();
        var clicked = !disabled && hovered && rawClicked;
        var rightClicked = captureRightClick
            && !disabled && hovered && ImGui.IsItemClicked(ImGuiMouseButton.Right);
        var doubleClicked = !disabled && hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left);
        return new SlapInteractionState(
            hovered,
            active,
            clicked,
            rightClicked,
            doubleClicked,
            disabled,
            selected,
            highlighted);
    }
}
