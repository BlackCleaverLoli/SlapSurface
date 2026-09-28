using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlapSurface;

/// <summary>
/// Shared interaction result for composite controls. Callers own the business action;
/// SlapSurface only reports the item state and screen-space bounds captured this frame.
/// </summary>
internal readonly record struct ControlResult(
    bool Hovered,
    bool Active,
    bool Clicked,
    bool RightClicked,
    bool DoubleClicked,
    Vector2 Min,
    Vector2 Max
)
{
    public Vector2 Size => Max - Min;

    public bool IsValid => Max.X > Min.X && Max.Y > Min.Y;

    /// <summary>True when the control was clicked this frame.</summary>
    public static implicit operator bool(ControlResult r) => r.Clicked;

    public static ControlResult FromLastItem(
        bool hovered,
        bool clicked,
        bool rightClicked = false,
        bool active = false,
        bool doubleClicked = false
    ) =>
        new(
            hovered,
            active,
            clicked,
            rightClicked,
            doubleClicked,
            ImGui.GetItemRectMin(),
            ImGui.GetItemRectMax()
        );
}