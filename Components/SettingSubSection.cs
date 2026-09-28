using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Scope returned by <see cref="Slap.BeginSettingSubSection"/>.
/// Manages indent depth and exposes SettingRow leaf methods that
/// automatically apply the correct indentation for the current level.
/// Nested sub-sections are created via <see cref="BeginSubSection"/>.
/// </summary>
internal sealed class SettingSubSectionScope : DisposableScope
{
    private readonly int _depth;
    private readonly float _rowIndentUnits;

    internal SettingSubSectionScope(int depth)
    {
        _depth = depth;
        _rowIndentUnits = SettingIndent.Level1 * (depth + 1);
    }

    // ── Leaf controls ──────────────────────────────────

    public CheckboxResult CheckboxRow(SettingCheckboxRowSpec spec)
        => SettingRowComponent.DrawCheckbox(spec with { IndentUnits = _rowIndentUnits + spec.IndentUnits });

    public DropdownResult ComboRow(SettingComboRowSpec spec)
        => SettingRowComponent.DrawCombo(spec with { IndentUnits = _rowIndentUnits + spec.IndentUnits });

    public ControlResult ActionRow(SettingActionRowSpec spec)
        => SettingRowComponent.DrawAction(spec with { IndentUnits = _rowIndentUnits + spec.IndentUnits });

    public ToggleSwitchResult ToggleRow(SettingToggleRowSpec spec)
        => SettingRowComponent.DrawToggleRow(spec with { IndentUnits = _rowIndentUnits + spec.IndentUnits });

    public void Row(SettingRowSpec spec, Action<ResponsiveSlotGroup> collectRightSlots)
        => SettingRowComponent.DrawSlotRow(spec with { IndentUnits = _rowIndentUnits + spec.IndentUnits }, collectRightSlots);

    public DragFloatResult DragFloatRow(SettingDragFloatRowSpec spec)
        => SettingRowComponent.DrawDragFloat(spec with { IndentUnits = _rowIndentUnits + spec.IndentUnits });

    public TextInputResult TextInputRow(SettingTextInputRowSpec spec)
        => SettingRowComponent.DrawTextInput(spec with { IndentUnits = _rowIndentUnits + spec.IndentUnits });

    // ── Nesting ────────────────────────────────────────

    public SettingSubSectionScope BeginSubSection(string title)
    {
        SettingSubSectionComponent.DrawSubTitle(title, _depth + 1);
        return new SettingSubSectionScope(_depth + 1);
    }
}

internal static class SettingSubSectionComponent
{
    private const float SubTitleGapTop = SlapPx.Space2;
    private const float SubTitleGapBottom = SlapPx.Space8;

    internal static void DrawSubTitle(string title, int level)
    {
        var indent = MetricsScope.UnitWidth * SettingIndent.Level1 * level;
        var color = ThemeScope.Resolved.Body;

        ImGui.Dummy(new Vector2(0f, MetricsScope.Scale(SubTitleGapTop)));
        ImGui.Dummy(new Vector2(indent, 0f));
        ImGui.SameLine(0, 0);
        using (Slap.PushFont(SlapFontSize.Regular, SlapFontWeight.Bold))
        {
            ImGui.TextColored(color, title);
        }
        ImGui.Dummy(new Vector2(0f, MetricsScope.Scale(SubTitleGapBottom)));
    }

    public static SettingSubSectionScope Begin(string title)
    {
        DrawSubTitle(title, 0);
        return new SettingSubSectionScope(0);
    }
}
