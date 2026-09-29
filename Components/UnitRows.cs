using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// 响应式单元行里的一个单元：自成一件行式控件的图标 + 两行文本内容。
/// </summary>
internal readonly record struct UnitRowItem(ControlKey Key, RowIconTwoLineSpec Content);

/// <summary>
/// 响应式单元行规格：单元按行铺排。每个单元都是独立控件——自带底色、边框、
/// hover/active 反馈与命中区，可点范围与视觉范围完全一致；行只承担排列职责，
/// 不绘制容器，因此不存在"看着像整行可点、实际只有单元可点"的落差。
/// <para>
/// <see cref="MaxUnitsPerRow"/> 给出每行单元数量的上限（0 表示不限），实际数量
/// 只受"单元最小宽度（仅图标）"限制：空间不足时单元先收窄而不是换到下一行，
/// 行内可用宽度按该行单元数量均分。
/// </para>
/// <para>
/// 单元内容沿用行内容自身的响应式表现：文本按内容区裁剪并右侧淡出，宽度归零时
/// 只剩图标，tooltip 按行内容的既有规则给出被裁剪的名称。
/// </para>
/// </summary>
internal readonly record struct UnitRowsSpec(
    ControlKey Key,
    IReadOnlyList<UnitRowItem> Items,
    float HeightUnits = 1.5f,
    int MaxUnitsPerRow = 2,
    float UnitGap = SlapPx.Space4,
    float RowGap = SlapPx.Space8,
    float OutlineBleedPadding = 2f
);

internal static class UnitRowsComponent
{
    // 单元内边距对齐 SurfaceList 的行内边距，让单元与列表行的图标距左沿一致。
    private const float UnitPaddingX = SlapPx.Space8;

    /// <summary>
    /// 绘制单元行。<paramref name="drawUnit"/> 对每个单元每帧调用一次，
    /// 调用方在其中处理该单元的点击、右键菜单等副作用——与
    /// <see cref="SurfaceListSpec"/> 的行回调同一协议，因此需要每帧重新提交的
    /// 弹窗（如条目右键菜单）不会因为只在触发帧调用而失效。
    /// </summary>
    public static void Draw(UnitRowsSpec spec, Action<int, SurfaceRowContext> drawUnit)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("UnitRowsSpec.Key.Value must not be empty.", nameof(spec));

        if (spec.Items.Count == 0)
            return;

        ImGui.PushID(spec.Key.Value);
        try
        {
            using var _hover = HoverArbitration.Push();
            var drawList = ImGui.GetWindowDrawList();
            var height = MetricsScope.UnitHeight * Normalize(spec.HeightUnits, 1.5f);
            var unitGap = MetricsScope.ScaleGap(spec.UnitGap);
            var rowGap = MetricsScope.ScaleGap(spec.RowGap);
            var unitPaddingX = MetricsScope.ScalePadding(UnitPaddingX);
            var bleed = MetricsScope.Scale(spec.OutlineBleedPadding);
            var origin = ImGui.GetCursorScreenPos();
            var width = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            var innerWidth = MathF.Max(0f, width - bleed * 2f);

            var unitsPerRow = ResolveUnitsPerRow(
                innerWidth,
                height * SlapPx.RowIconFillRatio + unitPaddingX * 2f,
                unitGap,
                spec.MaxUnitsPerRow,
                spec.Items.Count);
            var unitWidth = MathF.Max(
                0f,
                (innerWidth - unitGap * (unitsPerRow - 1)) / unitsPerRow);

            var rt = ThemeScope.Resolved;
            var palette = rt.GetButtonPalette(Variant.Flat);
            var accent = rt.ResolveOutlineAccent(Variant.Flat);
            var rounding = SlapCorners.ControlRadius;
            var stepY = height + rowGap;
            var unitOriginX = origin.X + bleed;

            // 描边辉光只在窗口纵向上裁剪，横向保持全屏，避免窗口边缘切掉光晕。
            var windowPos = ImGui.GetWindowPos();
            var windowSize = ImGui.GetWindowSize();
            HoverArbitration.Current.SetOutlineClipRect(
                new Vector2(0f, windowPos.Y),
                new Vector2(ImGui.GetIO().DisplaySize.X, windowPos.Y + windowSize.Y));

            for (var i = 0; i < spec.Items.Count; i++)
            {
                var item = spec.Items[i];
                var unitMin = new Vector2(
                    unitOriginX + i % unitsPerRow * (unitWidth + unitGap),
                    origin.Y + i / unitsPerRow * stepY);
                var unitMax = unitMin + new Vector2(unitWidth, height);

                ImGui.SetCursorScreenPos(unitMin);
                var rawClicked = ImGui.InvisibleButton(
                    $"##{item.Key.Value}",
                    new Vector2(unitWidth, height));
                var state = SlapInteraction.Capture(
                    ControlState.None,
                    rawClicked,
                    captureRightClick: true);

                var colors = SurfaceComponent.ResolveControlColors(
                    palette,
                    ControlState.None,
                    hovered: state.Hovered,
                    active: state.Active,
                    strength: BorderStrength.Subtle,
                    variant: Variant.Flat);
                drawList.AddRectFilled(
                    unitMin,
                    unitMax,
                    ImGui.ColorConvertFloat4ToU32(colors.Background),
                    rounding);
                var border = SurfaceComponent.ResolveListRowBorder(palette, state.Disabled);
                LabeledOutlineDrawing.DrawBorderWithCutout(
                    drawList,
                    unitMin,
                    unitMax,
                    null,
                    border,
                    rounding,
                    MetricsScope.BorderThickness);
                HoverArbitration.Current.CaptureOutline(
                    unitMin,
                    unitMax,
                    state.Hovered,
                    state.Selected,
                    accent,
                    null,
                    true,
                    rounding);

                var context = new SurfaceRowContext(
                    unitMin,
                    unitMax,
                    new Vector2(unitMin.X + unitPaddingX, unitMin.Y),
                    new Vector2(unitMax.X - unitPaddingX, unitMax.Y),
                    state.Hovered,
                    state.Hovered || state.Active,
                    state.Active,
                    state.Selected,
                    null,
                    state.Disabled,
                    state.Clicked,
                    state.RightClicked,
                    state.DoubleClicked,
                    colors.Background,
                    border,
                    null,
                    i);

                drawList.PushClipRect(unitMin, unitMax, true);
                try
                {
                    RowIconTwoLineComponent.Draw(context, item.Content);
                    drawUnit(i, context);
                }
                finally
                {
                    drawList.PopClipRect();
                }
            }

            HoverArbitration.Current.FlushOutline(spec.Key, 1f);

            var rows = (spec.Items.Count + unitsPerRow - 1) / unitsPerRow;
            ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + rows * stepY - rowGap));
            ImGui.Dummy(Vector2.Zero);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static int ResolveUnitsPerRow(
        float innerWidth,
        float minUnitWidth,
        float gap,
        int maxUnitsPerRow,
        int itemCount)
    {
        var capacity = (int)MathF.Floor((innerWidth + gap) / (minUnitWidth + gap));
        if (maxUnitsPerRow > 0)
            capacity = Math.Min(capacity, maxUnitsPerRow);

        return Math.Clamp(capacity, 1, Math.Max(1, itemCount));
    }

    private static float Normalize(float value, float fallback) =>
        value > 0f && !float.IsInfinity(value) ? value : fallback;
}
