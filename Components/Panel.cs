using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;

namespace SlapSurface;

/// <remarks>
/// <see cref="Variant"/> and <see cref="ControlState"/> are reserved for future use
/// (no-op in skeleton). Panel currently provides a borderless child
/// region/container with no scrolling. Layout behaviour is controlled by <see cref="Size"/>.
/// </remarks>
internal readonly record struct PanelSpec(
    ControlKey Key,
    PanelSize Size = default,
    Variant Variant = Variant.Base,
    ControlState State = ControlState.None
);

internal static class PanelComponent
{
    public static IDisposable Begin(PanelSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("PanelSpec.Key.Value must not be empty.", nameof(spec));

        var panelSize = ResolvePanelSize(spec.Size);
        var flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

        ImGui.PushID(spec.Key.Value);
        ImGui.BeginChild("##Content", panelSize, false, flags);

        return new PanelScope(spec.Key.Value);
    }

    private static Vector2 ResolvePanelSize(PanelSize size)
    {
        var unit = MetricsScope.UnitHeight;
        return size.Kind switch
        {
            PanelSizeKind.FillAvailable => Vector2.Zero,
            PanelSizeKind.FillWidth => new Vector2(0f, unit * size.HeightUnits),
            _ => Vector2.Zero
        };
    }

    private readonly struct PanelScope : IDisposable
    {
        private readonly string _key;
        public PanelScope(string key) => _key = key;

        public void Dispose()
        {
            ImGui.EndChild();
            ImGui.PopID();
        }
    }
}
