using System;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;

namespace SlapSurface.Theme;

internal static class ThemeScope
{
    private readonly record struct ThemeState(SlapTheme Seed, ResolvedTheme Resolved);

    private static ThemeState? _pushed;
    private static ThemeState _ambient;
    private static int _ambientFrame = -1;

    public static SlapTheme Current => (_pushed ?? Ambient).Seed;

    internal static ResolvedTheme Resolved => (_pushed ?? Ambient).Resolved;

    internal static IDisposable Push(SlapTheme theme)
    {
        var previous = _pushed;
        _pushed = new ThemeState(theme, ResolvedTheme.FromSeed(theme));
        return new Scope(previous);
    }

    private static ThemeState Ambient
    {
        get
        {
            EnsureAmbient();
            return _ambient;
        }
    }

    private static void EnsureAmbient()
    {
        var frame = ImGui.GetFrameCount();
        if (frame == _ambientFrame)
            return;

        var seed = BuildFromAmbientStyle();
        _ambient = new ThemeState(seed, ResolvedTheme.FromSeed(seed));
        _ambientFrame = frame;
    }

    private static SlapTheme BuildFromAmbientStyle()
    {
        var colors = ImGui.GetStyle().Colors;
        var text = colors[(int)ImGuiCol.Text];
        return new SlapTheme(
            SurfaceBg:     colors[(int)ImGuiCol.WindowBg],
            TitleBarBg:    colors[(int)ImGuiCol.TitleBg],

            BaseFill:      colors[(int)ImGuiCol.FrameBg],
            BaseText:      text,
            ActionFill:    colors[(int)ImGuiCol.Button],
            ActionText:    text,
            SidebarFill:  colors[(int)ImGuiCol.Header],
            SidebarText:  text,
            TabFill:      colors[(int)ImGuiCol.Tab],
            TabText:      text,

            InputFill:      colors[(int)ImGuiCol.FrameBg],
            InputText:      text,
            InputTextAlt:   text,

            Body:          text,
            Subtle:      colors[(int)ImGuiCol.TextDisabled],
            Emphasis:      text,
            Danger:        text,

            Border:             colors[(int)ImGuiCol.Border],
            CornerRadiusUnits: ResolveAmbientCornerRadius()
        );
    }

    private static float ResolveAmbientCornerRadius()
    {
        var unitHeight = MetricsScope.UnitHeight;
        if (unitHeight <= 0f)
            return SlapTheme.DefaultCornerRadiusUnits;

        var rounding = ImGui.GetStyle().FrameRounding;
        return rounding > 0f ? rounding / unitHeight : 0f;
    }

    private readonly struct Scope : IDisposable
    {
        private readonly ThemeState? _previous;

        public Scope(ThemeState? previous) => _previous = previous;

        public void Dispose() => _pushed = _previous;
    }
}
