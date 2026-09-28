using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Specification for a setting row: indent + label + optional description +
/// tooltip + right-aligned control. SlapSurface owns row height, spacing,
/// label alignment and tooltip; the caller draws the control via
/// <c>drawControl</c>.
/// </summary>
internal readonly record struct SettingRowSpec(
    ControlKey Key,
    string? Label = null,
    string? Tooltip = null,
    float IndentUnits = 0f,
    float ControlWidthUnits = 0f,
    bool Disabled = false,
    string? Description = null
);

/// <summary>
/// Specification for a setting row that embeds a checkbox as its control.
/// </summary>
internal readonly record struct SettingCheckboxRowSpec(
    ControlKey Key,
    string Label,
    bool Checked,
    string? Tooltip = null,
    float IndentUnits = 0f,
    bool Disabled = false,
    CheckboxShape Shape = CheckboxShape.Square
);

/// <summary>
/// Specification for a setting row that embeds a dropdown combo as its control.
/// </summary>
internal readonly record struct SettingComboRowSpec(
    ControlKey Key,
    string? Label,
    IReadOnlyList<DropdownOptionSpec> Options,
    int SelectedIndex,
    string? Tooltip = null,
	    float IndentUnits = 0f,
	    bool Disabled = false,
	    float WidthUnits = SettingRowComponent.StandardRightControlWidthUnits,
	    Func<DropdownOptionsSnapshot>? OpenOptionsProvider = null,
	    Variant Variant = Variant.Base,
	    string? Description = null
	);

/// <summary>
/// Specification for a setting row that embeds a button as its control.
/// </summary>
internal readonly record struct SettingActionRowSpec(
    ControlKey Key,
    string? Label,
    string ButtonLabel,
    string? Tooltip = null,
    float IndentUnits = 0f,
    bool Disabled = false,
    Variant Variant = Variant.Base,
    float WidthUnits = SettingRowComponent.StandardRightControlWidthUnits,
    string? Description = null
);

/// <summary>
/// Specification for a setting row that embeds a drag-float slider as its control.
/// </summary>
internal readonly record struct SettingDragFloatRowSpec(
    ControlKey Key,
    string? Label,
    float Value,
    float Min = 0f,
    float Max = 100f,
    float Speed = 1f,
    string Format = "%.0f",
    string? Tooltip = null,
    float IndentUnits = 0f,
    float ControlWidthUnits = SettingRowComponent.StandardRightControlWidthUnits,
    bool Disabled = false,
    string? Description = null
);

/// <summary>
/// Specification for a setting row that embeds a text input as its control.
/// </summary>
internal readonly record struct SettingTextInputRowSpec(
    ControlKey Key,
    string? Label,
    string Value,
    string? Placeholder = null,
    int MaxLength = 256,
    string? Tooltip = null,
    string? ClearTooltip = "",
    float IndentUnits = 0f,
    float ControlWidthUnits = SettingRowComponent.StandardRightControlWidthUnits,
    bool Disabled = false,
    string? Description = null
);

/// <summary>
/// Specification for a setting row that embeds an autocomplete text input as
/// its control.
/// </summary>
internal readonly record struct SettingAutocompleteTextInputRowSpec(
    ControlKey Key,
    string? Label,
    string Value,
    string? Placeholder = null,
    int MaxLength = 256,
    string? Tooltip = null,
    string? ClearTooltip = "",
    float IndentUnits = 0f,
    float ControlWidthUnits = SettingRowComponent.StandardRightControlWidthUnits,
    bool Disabled = false,
    string? Description = null
);

/// <summary>
/// Specification for a setting row that embeds a left-anchored toggle switch
/// with its label on the right. The switch and label form the hit target.
/// </summary>
internal readonly record struct SettingToggleRowSpec(
    ControlKey Key,
    string? Label,
    bool Checked,
    string? Tooltip = null,
    float IndentUnits = 0f,
    bool Disabled = false
);

internal static class SettingRowComponent
{
    private const float LabelControlGap = SlapPx.Space12;
    private const float ItemSpacingY = SlapPx.Space8;

    /// <summary>
    /// Standard right-side width for single-control setting rows. Combo,
    /// action and value-editor rows share this default so right-aligned
    /// controls on one page read as the same width. Multi-slot rows may pass
    /// narrower explicit widths (for example the 2u X/Y coordinate slots).
    /// </summary>
    internal const float StandardRightControlWidthUnits = 3f;

    private readonly record struct RowLayout(
        Vector2 CursorStart,
        Vector2 ControlMin,
        float ControlAvailable,
        float Width,
        float Indent,
        float RowHeight);

    /// <summary>
    /// Draw a raw setting row with a caller-provided control draw callback.
    /// The callback receives the control's min position and available width.
    /// This is the legacy raw-slot API retained for existing call sites
    /// (Angex generator); new rows should use <see cref="DrawSlotRow"/>.
    /// </summary>
    public static void Draw(SettingRowSpec spec, Action<Vector2, float> drawControl)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("SettingRowSpec.Key.Value must not be empty.", nameof(spec));

        using var _hover = HoverArbitration.Push();
        var layout = ComputeRowLayout(spec.Label, spec.IndentUnits, spec.ControlWidthUnits, spec.Disabled);
        DrawLegacyLabel(layout, spec.Label, spec.Disabled);

        if (layout.ControlAvailable > 0f)
        {
            if (spec.Disabled)
            {
                ImGui.BeginDisabled();
                try { drawControl(layout.ControlMin, layout.ControlAvailable); }
                finally { ImGui.EndDisabled(); }
            }
            else
            {
                drawControl(layout.ControlMin, layout.ControlAvailable);
            }
        }

        DrawTooltipAndAdvance(spec.Key, layout, spec.Tooltip, spec.Label);
    }

    /// <summary>
    /// Draw a setting row with an embedded checkbox.
    /// </summary>
    public static CheckboxResult DrawCheckbox(SettingCheckboxRowSpec spec)
    {
        var result = new CheckboxResult(spec.Checked, false, false);

        // Checkbox is a left-anchored control: its label is embedded in the
        // checkbox itself and the whole row area is the hit target. It stays
        // on the legacy label-less row path instead of the right-slot protocol.
        Draw(new SettingRowSpec(
            spec.Key,
            Label: null,
            Tooltip: null,
            IndentUnits: spec.IndentUnits,
            Disabled: spec.Disabled),
            (controlMin, _) =>
            {
                ImGui.SetCursorScreenPos(controlMin);
                result = CheckboxComponent.Draw(new CheckboxSpec(
                    spec.Key,
                    spec.Label,
                    spec.Checked,
                    spec.Disabled ? ControlState.Disabled : ControlState.None,
                    Tooltip: spec.Tooltip,
                    Shape: spec.Shape));
            });

        return result;
    }

    /// <summary>
    /// Draw a setting row with an embedded dropdown combo.
    /// </summary>
    public static DropdownResult DrawCombo(SettingComboRowSpec spec)
    {
        if (spec.Options.Count == 0)
            return new DropdownResult(-1, -1, false, default);

        var result = new DropdownResult(-1, -1, false, default);
        DrawSlotRow(new SettingRowSpec(
            spec.Key,
            Label: spec.Label,
            Tooltip: spec.Tooltip,
            Description: spec.Description,
            IndentUnits: spec.IndentUnits,
            Disabled: spec.Disabled),
            slots => slots.Dropdown(
	                new DropdownSpec(
	                    spec.Key,
	                    spec.Options,
	                    spec.SelectedIndex,
	                    ButtonSize.Rect(spec.WidthUnits, 1f),
	                    spec.Disabled ? ControlState.Disabled : ControlState.None,
	                    Tooltip: null,
	                    OpenOptionsProvider: spec.OpenOptionsProvider,
	                    Variant: spec.Variant),
	                r => result = r));

        return result;
    }

    /// <summary>
    /// Draw a setting row with an embedded button.
    /// </summary>
    public static ControlResult DrawAction(SettingActionRowSpec spec)
    {
        var result = default(ControlResult);
        DrawSlotRow(new SettingRowSpec(
            spec.Key,
            Label: spec.Label,
            Tooltip: spec.Tooltip,
            Description: spec.Description,
            IndentUnits: spec.IndentUnits,
            Disabled: spec.Disabled),
            slots => slots.Button(
                new ButtonSpec(
                    spec.Key,
                    spec.ButtonLabel,
                    ButtonSize.Rect(spec.WidthUnits, 1f),
                    spec.Variant,
                    spec.Disabled ? ControlState.Disabled : ControlState.None,
                    Tooltip: spec.Tooltip),
                r => result = r));

        return result;
    }

    /// <summary>
    /// Draw a setting row with a left-anchored toggle switch and its label on
    /// the right. The switch and label form the hit target and tooltip area.
    /// </summary>
    public static ToggleSwitchResult DrawToggleRow(SettingToggleRowSpec spec)
    {
        using var _hover = HoverArbitration.Push();
        var cursorStart = ImGui.GetCursorScreenPos();
        var indent = spec.IndentUnits > 0f
            ? MetricsScope.UnitWidth * spec.IndentUnits
            : 0f;
        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X - indent);
        var naturalWidth = ToggleSwitchComponent.ResolveNaturalWidth(
            spec.Label,
            LabelPosition.Right,
            tooltip: spec.Tooltip,
            labelWeight: SlapFontWeight.Regular);
        var resolvedWidth = MathF.Min(
            naturalWidth,
            MathF.Max(ToggleSwitchComponent.ResolveTrackWidth(0f), availableWidth));
        ImGui.SetCursorScreenPos(cursorStart + new Vector2(indent, 0f));

        var result = ToggleSwitchComponent.Draw(new ToggleSwitchSpec(
            spec.Key,
            spec.Label ?? string.Empty,
            spec.Checked,
            Position: LabelPosition.Right,
            State: spec.Disabled ? ControlState.Disabled : ControlState.None,
            Tooltip: spec.Tooltip,
            ResolvedWidth: resolvedWidth,
            Interactable: true,
            LabelWeight: SlapFontWeight.Regular));

        ImGui.SetCursorScreenPos(
            cursorStart + new Vector2(0f, MetricsScope.UnitHeight + MetricsScope.Scale(ItemSpacingY)));
        return result;
    }

    /// <summary>
    /// Draw a setting row with an embedded drag-float slider.
    /// </summary>
    public static DragFloatResult DrawDragFloat(SettingDragFloatRowSpec spec)
    {
        var result = new DragFloatResult(spec.Value, false, false, false);
        DrawSlotRow(new SettingRowSpec(
            spec.Key,
            Label: spec.Label,
            Tooltip: spec.Tooltip,
            Description: spec.Description,
            IndentUnits: spec.IndentUnits,
            Disabled: spec.Disabled),
            slots => slots.DragFloat(
                new DragFloatSpec(
                    spec.Key,
                    spec.Min,
                    spec.Max,
                    spec.Speed,
                    spec.Format,
                    WidthUnits: spec.ControlWidthUnits,
                    Tooltip: null,
                    State: spec.Disabled ? ControlState.Disabled : ControlState.None),
                spec.Value,
                r => result = r));

        return result;
    }

    /// <summary>
    /// Draw a setting row with an embedded text input.
    /// </summary>
    public static TextInputResult DrawTextInput(SettingTextInputRowSpec spec)
    {
        var result = new TextInputResult(
            string.Empty, false, false, false, false, false,
            false, false, false, false, default, default);
        DrawSlotRow(new SettingRowSpec(
            spec.Key,
            Label: spec.Label,
            Tooltip: spec.Tooltip,
            Description: spec.Description,
            IndentUnits: spec.IndentUnits,
            Disabled: spec.Disabled),
            slots => slots.TextInput(
                new TextInputSpec(
                    spec.Key,
                    Placeholder: spec.Placeholder ?? string.Empty,
                    WidthUnits: spec.ControlWidthUnits,
                    ClearTooltip: spec.ClearTooltip,
                    Tooltip: null),
                spec.Value,
                spec.MaxLength,
                r => result = r));

        return result;
    }

    /// <summary>
    /// Draw a setting row with an embedded autocomplete text input.
    /// </summary>
    public static AutocompleteTextInputResult DrawAutocompleteTextInput(
        SettingAutocompleteTextInputRowSpec spec,
        AutocompleteTextInputState state,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider)
    {
        var result = new AutocompleteTextInputResult(
            spec.Value, false, false, false, false, null);
        DrawSlotRow(new SettingRowSpec(
            spec.Key,
            Label: spec.Label,
            Tooltip: spec.Tooltip,
            Description: spec.Description,
            IndentUnits: spec.IndentUnits,
            Disabled: spec.Disabled),
            slots => slots.AutocompleteTextInput(
                new AutocompleteTextInputSpec(
                    new TextInputSpec(
                        spec.Key,
                        Placeholder: spec.Placeholder ?? string.Empty,
                        WidthUnits: spec.ControlWidthUnits,
                        ClearTooltip: spec.ClearTooltip,
                        Tooltip: null)),
                state,
                spec.Value,
                optionsProvider,
                spec.MaxLength,
                r => result = r));

        return result;
    }

    /// <summary>
    /// Draw a setting row whose right side is a responsive slot group.
    /// The row owns label, tooltip, indent and vertical advance; the slot
    /// group owns right-side control measurement, shrinking and hiding.
    /// </summary>
    public static void DrawSlotRow(
        SettingRowSpec spec,
        Action<ResponsiveSlotGroup> collectRightSlots,
        Action? onRowClick = null,
        bool rowSurface = false,
        Variant rowVariant = Variant.Flat)
    {
        using var _hover = HoverArbitration.Push();
        var description = ResolveDescription(spec.Description);
        var displayLabel = BuildDisplayLabel(spec.Label, spec.Tooltip);
        var layout = ComputeRowLayout(displayLabel, spec.IndentUnits, 0f, spec.Disabled);
        var textMetrics = MeasureTextBlock(displayLabel, description);
        var rowHeight = MathF.Max(MetricsScope.UnitHeight, textMetrics.Height);
        var textMaxX = layout.CursorStart.X + layout.Indent + layout.Width;

        // Preset-row style interaction: the whole row is the click target and
        // draws a hover surface; tooltip hover keeps working when disabled.
        var rowHovered = false;
        var rowButtonDrawn = rowSurface || onRowClick != null;
        if (rowButtonDrawn)
        {
            var rowMin = new Vector2(layout.CursorStart.X + layout.Indent, layout.CursorStart.Y);
            var rowMax = rowMin + new Vector2(layout.Width, rowHeight);
            ImGui.SetCursorScreenPos(rowMin);

            var rawClicked = false;
            if (spec.Disabled)
            {
                ImGui.BeginDisabled();
                try
                {
                    rawClicked = ImGui.InvisibleButton($"##row_{spec.Key.Value}", rowMax - rowMin);
                }
                finally
                {
                    ImGui.EndDisabled();
                }
            }
            else
            {
                rawClicked = ImGui.InvisibleButton($"##row_{spec.Key.Value}", rowMax - rowMin);
            }

            rowHovered = HoverArbitration.Current.CaptureHover(
                ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled));
            var rowActive = !spec.Disabled && ImGui.IsItemActive();

            if (rowSurface)
                DrawRowSurface(layout, rowHeight, spec.Disabled, rowVariant, rowHovered, rowActive);

            if (rawClicked)
                onRowClick?.Invoke();
        }

        if (layout.ControlAvailable > 0f)
        {
            var right = new ResponsiveSlotGroup();
            collectRightSlots(right);
            var entries = right.Entries;
            if (entries.Count > 0)
            {
                var ranges = new ResponsiveWidthRange[entries.Count];
                for (var i = 0; i < entries.Count; i++)
                    ranges[i] = entries[i].WidthRange;

                var gap = MetricsScope.ScaleGap(SlapPx.Space8);
                var strip = SlapMeasure.MeasureStripLayout(
                    layout.ControlAvailable,
                    0f,
                    0f,
                    0f,
                    ranges,
                    BuildPriorities(entries),
                    gap);

                var rightEdge = layout.ControlMin.X + layout.ControlAvailable;
                if (strip.RightTotalWidth > 0f)
                {
                    textMaxX = rightEdge - strip.RightTotalWidth - MetricsScope.ScaleGap(LabelControlGap);
                    textMaxX = MathF.Max(layout.CursorStart.X + layout.Indent, textMaxX);
                }

                var controlY = layout.CursorStart.Y
                    + MathF.Max(0f, (rowHeight - MetricsScope.UnitHeight) * 0.5f);
                if (spec.Disabled)
                {
                    ImGui.BeginDisabled();
                    try
                    {
                        ResponsiveSlotLayout.DrawRightStrip(
                            entries,
                            strip.RightLayouts,
                            rightEdge,
                            controlY,
                            gap);
                    }
                    finally
                    {
                        ImGui.EndDisabled();
                    }
                }
                else
                {
                    ResponsiveSlotLayout.DrawRightStrip(
                        entries,
                        strip.RightLayouts,
                        rightEdge,
                        controlY,
                        gap);
                }
            }
        }

        layout = layout with { RowHeight = rowHeight };
        DrawLabelBlock(layout, displayLabel, description, textMaxX, spec.Disabled);
        DrawTooltipAndAdvance(
            spec.Key,
            layout,
            spec.Tooltip,
            displayLabel,
            description,
            textMetrics,
            textMaxX,
            rowHovered,
            rowButtonDrawn);
    }

    private static int[] BuildPriorities(
        IReadOnlyList<ResponsiveSlotEntry> entries)
    {
        var priorities = new int[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            priorities[i] = entries[i].Priority;
        return priorities;
    }

    private static RowLayout ComputeRowLayout(
        string? label,
        float indentUnits,
        float controlWidthUnits,
        bool disabled)
    {
        var rowHeight = MetricsScope.UnitHeight;
        var indent = indentUnits > 0f
            ? MetricsScope.UnitWidth * indentUnits
            : 0f;
        var available = ImGui.GetContentRegionAvail();
        var width = MathF.Max(0f, available.X - indent);

        var hasLabel = !string.IsNullOrWhiteSpace(label);
        float labelWidth = 0f;
        if (hasLabel)
            labelWidth = ImGui.CalcTextSize(label!).X;

        var gap = MetricsScope.ScaleGap(LabelControlGap);
        var controlWidth = controlWidthUnits > 0f
            ? MetricsScope.UnitWidth * controlWidthUnits
            : 0f;
        var controlAvailable = MathF.Max(0f, width - labelWidth - (hasLabel ? gap : 0f));
        if (controlWidth > 0f)
            controlAvailable = MathF.Min(controlAvailable, controlWidth);

        var cursorStart = ImGui.GetCursorScreenPos();
        var controlX = cursorStart.X + indent + width - controlAvailable;
        return new RowLayout(
            cursorStart,
            new Vector2(controlX, cursorStart.Y),
            controlAvailable,
            width,
            indent,
            rowHeight);
    }

    private static void DrawLegacyLabel(RowLayout layout, string? label, bool disabled)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        var labelColor = ThemeScope.Resolved.Body;
        if (disabled)
            labelColor.W *= SlapColor.DisabledAlpha;
        var labelY = layout.CursorStart.Y
            + MathF.Max(0f, (layout.RowHeight - ImGui.GetTextLineHeight()) * 0.5f);
        ImGui.GetWindowDrawList().AddText(
            new Vector2(layout.CursorStart.X + layout.Indent, labelY),
            ImGui.ColorConvertFloat4ToU32(labelColor),
            label!);
    }

    private static string? ResolveDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var cleaned = description
            .Replace("\r\n", "\n")
            .Replace('\n', ' ')
            .Trim();
        while (cleaned.Contains("  "))
            cleaned = cleaned.Replace("  ", " ");
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private static string? BuildDisplayLabel(string? label, string? tooltip) =>
        ResponsiveTooltip.BuildDisplayLabel(label, tooltip);

    private static TwoLineTextMetrics MeasureTextBlock(string? label, string? description) =>
        TwoLineTextComponent.Measure(new TwoLineTextSpec(
            label ?? string.Empty,
            description,
            SlapFontSize.Regular,
            SlapFontWeight.Regular,
            SlapFontSize.Small,
            SlapFontWeight.Regular));

    private static void DrawLabelBlock(
        RowLayout layout,
        string? label,
        string? description,
        float textMaxX,
        bool disabled)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        var theme = ThemeScope.Resolved;
        var line1Color = theme.Body;
        if (disabled)
            line1Color.W *= SlapColor.DisabledAlpha;

        var line2Color = theme.Subtle;
        if (disabled)
            line2Color.W *= SlapColor.DisabledAlpha;

        var min = new Vector2(layout.CursorStart.X + layout.Indent, layout.CursorStart.Y);
        TwoLineTextComponent.DrawInRect(
            ImGui.GetWindowDrawList(),
            new TwoLineTextSpec(
                label,
                description,
                SlapFontSize.Regular,
                SlapFontWeight.Regular,
                SlapFontSize.Small,
                SlapFontWeight.Regular,
                line1Color,
                line2Color,
                RightFadeBackground: theme.Surface),
            min,
            new Vector2(MathF.Max(min.X, textMaxX), layout.CursorStart.Y + layout.RowHeight));
    }

    private static void DrawRowSurface(
        RowLayout layout,
        float rowHeight,
        bool disabled,
        Variant variant,
        bool hovered,
        bool active)
    {
        var rt = ThemeScope.Resolved;
        var palette = rt.GetButtonPalette(variant);
        var colors = SurfaceComponent.ResolveControlColors(
            palette,
            disabled ? ControlState.Disabled : ControlState.None,
            hovered: hovered && !disabled,
            active: active && !disabled,
            strength: BorderStrength.Subtle,
            variant: variant);

        var min = new Vector2(layout.CursorStart.X + layout.Indent, layout.CursorStart.Y);
        var max = min + new Vector2(layout.Width, rowHeight);
        var drawList = ImGui.GetWindowDrawList();
        var rounding = SlapCorners.ControlRadius;

        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(colors.Background), rounding);
        HoverArbitration.Current.CaptureOutline(
            min,
            max,
            hovered && !disabled,
            selected: false,
            accent: rt.ResolveOutlineAccent(variant),
            cutout: null,
            drawOutline: true,
            rounding: rounding);
    }

    private static void DrawTooltipAndAdvance(
        ControlKey key,
        RowLayout layout,
        string? tooltip,
        string? label,
        string? description = null,
        TwoLineTextMetrics? textMetrics = null,
        float textMaxX = float.PositiveInfinity,
        bool rowHovered = false,
        bool rowButtonDrawn = false)
    {
        var textAreaWidth = float.IsPositiveInfinity(textMaxX)
            ? layout.Width
            : MathF.Max(0f, textMaxX - (layout.CursorStart.X + layout.Indent));
        var line1Degraded = textMetrics.HasValue
            ? textMetrics.Value.IsLine1Clipped(textAreaWidth)
            : !string.IsNullOrWhiteSpace(label)
                && ImGui.CalcTextSize(label).X > textAreaWidth;
        var line2Degraded = textMetrics.HasValue
            && textMetrics.Value.HasLine2
            && textMetrics.Value.Line2Width > textAreaWidth;

        var composed = tooltip;
        if (line2Degraded)
            composed = ResponsiveTooltip.Compose(true, description, composed);
        if (line1Degraded)
            composed = ResponsiveTooltip.Compose(true, label, composed);
        if (!string.IsNullOrWhiteSpace(composed))
        {
            if (rowButtonDrawn)
            {
                var rowMin = new Vector2(layout.CursorStart.X + layout.Indent, layout.CursorStart.Y);
                var rowMax = rowMin + new Vector2(layout.Width, layout.RowHeight);
                HoverArbitration.Current.TryShowTooltip(rowHovered, composed, rowMin, rowMax);
            }
            else
            {
                using var _hover = HoverArbitration.Push();
                var rowMin = new Vector2(layout.CursorStart.X + layout.Indent, layout.CursorStart.Y);
                var textEndX = float.IsPositiveInfinity(textMaxX)
                    ? layout.CursorStart.X + layout.Indent + layout.Width
                    : textMaxX;
                var rowMax = new Vector2(
                    MathF.Max(rowMin.X, textEndX),
                    rowMin.Y + layout.RowHeight);
                ImGui.SetCursorScreenPos(rowMin);
                ImGui.InvisibleButton($"##tt_{key.Value}", rowMax - rowMin);
                var capturedHover = HoverArbitration.Current.CaptureHover(
                    ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled));
                HoverArbitration.Current.TryShowTooltip(capturedHover, composed);
            }
        }

        ImGui.SetCursorScreenPos(layout.CursorStart + new Vector2(0f, layout.RowHeight + MetricsScope.Scale(ItemSpacingY)));
    }
}
