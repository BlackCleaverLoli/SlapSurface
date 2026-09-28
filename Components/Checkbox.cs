using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Visual shape of a checkbox. Shape does not change its independent boolean behavior.
/// </summary>
internal enum CheckboxShape
{
    Square,
    Circle,
}

/// <summary>
/// Specification for a checkbox control rendered with a filled inner mark.
/// Label appears to the right of the box.
/// </summary>
internal readonly record struct CheckboxSpec(
    ControlKey Key,
    string Label,
    bool Checked,
    ControlState State = ControlState.None,
    string? Tooltip = null,
    CheckboxShape Shape = CheckboxShape.Square
);

internal readonly record struct CheckboxResult(
    bool Checked,
    bool Changed,
    bool Hovered
);

internal static class CheckboxComponent
{
    private const float BoxSizeRatio = 0.37f;
    private const float InnerBoxScale = 0.4f;
    private const float BorderScale = 1.5f;
    private const float CheckMarkBorderScale = 2.0f;
    private const float LabelGap = SlapPx.Space8;
    private const float VerticalOffset = 1.5f;

    public static CheckboxResult Draw(CheckboxSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("CheckboxSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        try
        {
            var rt = ThemeScope.Resolved;
            var disabled = spec.State.HasFlag(ControlState.Disabled);
            var textColor = SlapColor.WithDisabledAlpha(rt.Body, disabled);
            var unitH = MetricsScope.UnitHeight;
            var borderThickness = MetricsScope.BorderThickness * BorderScale;
            var checkMarkBorder = MetricsScope.BorderThickness * CheckMarkBorderScale;

            var cursor = ImGui.GetCursorScreenPos();
            var boxSize = unitH * BoxSizeRatio;
            var lineHeight = ImGui.GetTextLineHeight();
            var totalHeight = MathF.Max(lineHeight, boxSize);
            var boxMin = new Vector2(
                cursor.X,
                cursor.Y + (lineHeight - boxSize) * 0.5f + MetricsScope.ScalePadding(VerticalOffset));
            var boxMax = new Vector2(boxMin.X + boxSize, boxMin.Y + boxSize);

            Vector2 labelSize = default;
            var hasLabel = !string.IsNullOrWhiteSpace(spec.Label);
            if (hasLabel)
            {
                using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Regular))
                    labelSize = ImGui.CalcTextSize(spec.Label);
            }

            var gap = hasLabel ? MetricsScope.ScaleGap(LabelGap) : 0f;
            var totalWidth = boxSize + gap + labelSize.X;

            // Hit detection
            ImGui.SetCursorScreenPos(cursor);
            var rawClicked = ImGui.InvisibleButton("##cb", new Vector2(totalWidth, totalHeight));
            var ix = SlapInteraction.Capture(spec.State, rawClicked);

            var value = spec.Checked;
            if (ix.Clicked)
                value = !value;

            var drawList = ImGui.GetWindowDrawList();
            var circle = spec.Shape == CheckboxShape.Circle;
            var center = (boxMin + boxMax) * 0.5f;
            var radius = boxSize * 0.5f;

            // Outer border — use text color, full alpha, no hover change
            var borderColor = textColor;
            var borderU32 = ImGui.ColorConvertFloat4ToU32(borderColor);

            if (circle)
            {
                drawList.AddCircle(
                    center,
                    MathF.Max(0f, radius - borderThickness * 0.5f),
                    borderU32,
                    thickness: borderThickness);
            }
            else
            {
                drawList.AddRect(boxMin, boxMax, borderU32, 0f, ImDrawFlags.None, borderThickness);
            }

            // Check mark — fill and border are the same color
            if (value)
            {
                var checkColor = textColor;
                var checkU32 = ImGui.ColorConvertFloat4ToU32(checkColor);

                if (circle)
                {
                    var innerRadius = boxSize * InnerBoxScale * 0.5f;
                    drawList.AddCircleFilled(center, innerRadius, checkU32);
                }
                else
                {
                    var innerSize = boxSize * InnerBoxScale;
                    var offset = (boxSize - innerSize) * 0.5f;
                    var innerMin = new Vector2(boxMin.X + offset, boxMin.Y + offset);
                    var innerMax = new Vector2(boxMin.X + offset + innerSize, boxMin.Y + offset + innerSize);

                    var inset = checkMarkBorder * 0.5f;
                    var fillMin = new Vector2(innerMin.X + inset, innerMin.Y + inset);
                    var fillMax = new Vector2(innerMax.X - inset, innerMax.Y - inset);

                    drawList.AddRectFilled(fillMin, fillMax, checkU32);
                    drawList.AddRect(innerMin, innerMax, checkU32, 0f, ImDrawFlags.None, checkMarkBorder);
                }
            }

            // Hover outline — accent from text color, matching old CheckBoxHoverColor
            if (ix.Hovered && !ix.Disabled)
            {
                var outlineAccent = rt.Body;
                var outline = new SlapOutlineGroup();
                if (circle)
                    outline.CaptureCircle(
                        boxMin,
                        boxMax,
                        true,
                        selected: false,
                        accent: outlineAccent);
                else
                    outline.Capture(
                        boxMin,
                        boxMax,
                        true,
                        selected: false,
                        accent: outlineAccent,
                        rounding: 0f);
                outline.Flush(spec.Key, 1f);
            }

            // Label — text color, with leading space per old style
            if (hasLabel)
            {
                var labelColor = textColor;
                var textY = cursor.Y;
                using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Regular))
                {
                    drawList.AddText(new Vector2(boxMax.X + gap, textY),
                        ImGui.ColorConvertFloat4ToU32(labelColor), " " + spec.Label);
                }
            }

            // Tooltip
            HoverArbitration.Current.TryShowTooltip(ix.Hovered, spec.Tooltip);

            ImGui.SetCursorScreenPos(new Vector2(cursor.X, cursor.Y + unitH));

            return new CheckboxResult(value, value != spec.Checked, ix.Hovered);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    internal static float ResolveNaturalWidth(CheckboxSpec spec)
    {
        var boxSize = MetricsScope.UnitHeight * BoxSizeRatio;
        if (string.IsNullOrWhiteSpace(spec.Label))
            return boxSize;

        Vector2 labelSize;
        using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Regular))
            labelSize = ImGui.CalcTextSize(spec.Label);
        return boxSize + MetricsScope.ScaleGap(LabelGap) + labelSize.X;
    }
}
