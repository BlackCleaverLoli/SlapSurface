using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlapSurface;

internal sealed class SlapTitleBarNavigationHandler
{
    private Vector2 lastMin;
    private Vector2 lastMax;
    private bool hasLastRect;

    public bool ShouldDisableMove =>
        hasLastRect && IsInRect(ImGui.GetIO().MousePos, lastMin, lastMax);

    internal void SetBounds(bool visible, Vector2 min, Vector2 max)
    {
        hasLastRect = visible && max.X > min.X && max.Y > min.Y;
        lastMin = min;
        lastMax = max;
    }

    private static bool IsInRect(Vector2 position, Vector2 min, Vector2 max) =>
        position.X >= min.X
        && position.X <= max.X
        && position.Y >= min.Y
        && position.Y <= max.Y;
}
