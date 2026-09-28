using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace SlapSurface;

/// <summary>
/// Saves the current screen-space cursor on construction and restores it on dispose.
/// <para>
/// Use this immediately after a layout item (<c>InvisibleButton</c>, <c>Dummy</c>)
/// to isolate cursor movement from custom drawing callbacks. The layout item has
/// already advanced the cursor by <c>size + ItemSpacing</c>; this scope preserves
/// that advancement so the next item in the parent layout starts at the correct
/// position.
/// </para>
/// </summary>
internal readonly struct CursorScope : IDisposable
{
    private readonly Vector2 _pos;

    public CursorScope()
    {
        _pos = ImGui.GetCursorScreenPos();
    }

    public readonly void Dispose()
    {
        ImGui.SetCursorScreenPos(_pos);
    }
}
