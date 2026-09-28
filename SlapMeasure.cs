using System;
using System.Collections.Generic;
using SlapSurface.Metrics;

namespace SlapSurface;

/// <summary>Preferred and structural-minimum widths for a responsive item.</summary>
internal readonly record struct ResponsiveWidthRange(
    float PreferredWidth,
    float MinimumWidth
)
{
    /// <summary>
    /// Combines measured content, a caller-requested preferred baseline, and
    /// the control's structural minimum into a responsive width range.
    /// </summary>
    public static ResponsiveWidthRange Create(
        float contentWidth,
        float preferredBaseline,
        float structuralMinimum)
    {
        var minimum = MathF.Max(0f, structuralMinimum);
        var preferred = MathF.Max(
            MathF.Max(0f, contentWidth),
            MathF.Max(0f, preferredBaseline));
        return new ResponsiveWidthRange(MathF.Max(preferred, minimum), minimum);
    }
}

/// <summary>Allocated width and visibility for a responsive item.</summary>
internal readonly record struct ResponsiveItemLayout(
    float Width,
    bool Visible
);

/// <summary>Immediate-mode measurement primitives for responsive layouts.</summary>
internal static class SlapMeasure
{
    private const int RightGroup = 0;
    private const int LeftGroup = 1;
    private const int StripLeftGroup = 2;

    private readonly record struct SlotStep(
        int Priority,
        int Rank,
        int Group,
        int Index
    );

    /// <summary>
    /// Resolved layout for a left content area and a right edge group.
    /// </summary>
    internal readonly record struct StripLayoutResult(
        float LeftWidth,
        float RightTotalWidth,
        ResponsiveItemLayout[] RightLayouts
    );

    /// <summary>Resolved layout for left and right edge groups.</summary>
    internal readonly record struct TwoSidedLayoutResult(
        ResponsiveItemLayout[] LeftLayouts,
        ResponsiveItemLayout[] RightLayouts
    );

    /// <summary>
    /// Measure a strip whose right-side items are ordered from the outer edge
    /// inward. Right items shrink first. When the right minimums and protected
    /// left width no longer fit, inner right items are hidden before the outer
    /// edge item. The left area may finally shrink below its protected width to
    /// keep the result within the total budget.
    /// <para>
    /// Higher <paramref name="rightPriorities"/> degrade later: lower-priority
    /// slots shrink and hide first so a protected slot (e.g. an expanded input)
    /// keeps its width while other slots still have room. Within the same
    /// priority the legacy edge-inward order is preserved.
    /// </para>
    /// </summary>
    internal static StripLayoutResult MeasureStripLayout(
        float totalWidth,
        float leftPreferredWidth,
        float leftMinimumWidth,
        float leftRightGap,
        IReadOnlyList<ResponsiveWidthRange> rightItems,
        IReadOnlyList<int> rightPriorities,
        float rightItemGap)
    {
        var budget = MathF.Max(0f, totalWidth);
        var leftPreferred = MathF.Max(0f, leftPreferredWidth);
        var leftMinimum = Math.Clamp(leftMinimumWidth, 0f, leftPreferred);
        var outerGap = MathF.Max(0f, leftRightGap);
        var itemGap = MathF.Max(0f, rightItemGap);
        var ranges = NormalizeRanges(rightItems);

        if (ranges.Length == 0)
        {
            return new StripLayoutResult(
                MathF.Min(leftPreferred, budget),
                0f,
                Array.Empty<ResponsiveItemLayout>());
        }

        var visible = CreateVisibility(ranges.Length);
        var effectiveLeftMinimum = leftMinimum;
        var hideSteps = BuildStripHideSteps(ranges.Length, rightPriorities);
        for (var s = 0;
             s < hideSteps.Length
             && MeasureStripMinimumTotal(
                 ranges,
                 visible,
                 effectiveLeftMinimum,
                 outerGap,
                 itemGap) > budget;
             s++)
        {
            var step = hideSteps[s];
            if (step.Group == StripLeftGroup)
                effectiveLeftMinimum = 0f;
            else
                visible[step.Index] = false;
        }

        var rightWidths = ResolvePreferredWidths(ranges, visible);
        var visibleRightCount = CountVisible(visible);
        var effectiveOuterGap = visibleRightCount > 0 ? outerGap : 0f;
        var preferredTotal = leftPreferred
            + effectiveOuterGap
            + MeasureWidthsTotal(rightWidths, visible, itemGap);
        var remainingReduction = MathF.Max(0f, preferredTotal - budget);

        var leftWidth = leftPreferred;
        var shrinkSteps = BuildStripShrinkSteps(ranges.Length, rightPriorities);
        for (var s = 0; s < shrinkSteps.Length && remainingReduction > 0f; s++)
        {
            var step = shrinkSteps[s];
            if (step.Group == StripLeftGroup)
                ReduceWidth(ref leftWidth, 0f, ref remainingReduction);
            else if (visible[step.Index])
                ReduceWidth(
                    ref rightWidths[step.Index],
                    ranges[step.Index].MinimumWidth,
                    ref remainingReduction);
        }

        var rightLayouts = BuildLayouts(rightWidths, visible);
        var rightTotalWidth = MeasureLayoutsTotal(rightLayouts, itemGap);
        return new StripLayoutResult(leftWidth, rightTotalWidth, rightLayouts);
    }

    /// <summary>
    /// Measure two edge groups. Items in each group are ordered from the outer
    /// edge inward. The right group shrinks first, followed by the left group.
    /// If all minimums still exceed the budget, inner left items are hidden,
    /// then inner right items. The right outer item is the final survivor.
    /// <para>
    /// Higher priorities degrade later: lower-priority slots shrink and hide
    /// first so a protected slot (e.g. an expanded input) keeps its width while
    /// other slots still have room. Within the same priority the legacy side
    /// and edge-inward order is preserved.
    /// </para>
    /// </summary>
    internal static TwoSidedLayoutResult MeasureTwoSidedLayout(
        float totalWidth,
        float groupGap,
        IReadOnlyList<ResponsiveWidthRange> leftItems,
        IReadOnlyList<int> leftPriorities,
        IReadOnlyList<ResponsiveWidthRange> rightItems,
        IReadOnlyList<int> rightPriorities,
        float itemGap)
    {
        var budget = MathF.Max(0f, totalWidth);
        var normalizedGroupGap = MathF.Max(0f, groupGap);
        var normalizedItemGap = MathF.Max(0f, itemGap);
        var leftRanges = NormalizeRanges(leftItems);
        var rightRanges = NormalizeRanges(rightItems);
        var leftVisible = CreateVisibility(leftRanges.Length);
        var rightVisible = CreateVisibility(rightRanges.Length);

        var hideSteps = BuildTwoSidedHideSteps(
            leftRanges.Length,
            rightRanges.Length,
            leftPriorities,
            rightPriorities);
        for (var s = 0;
             s < hideSteps.Length
             && MeasureTwoSidedMinimumTotal(
                 leftRanges,
                 leftVisible,
                 rightRanges,
                 rightVisible,
                 normalizedGroupGap,
                 normalizedItemGap) > budget;
             s++)
        {
            var step = hideSteps[s];
            if (step.Group == LeftGroup)
                leftVisible[step.Index] = false;
            else
                rightVisible[step.Index] = false;
        }

        var leftWidths = ResolvePreferredWidths(leftRanges, leftVisible);
        var rightWidths = ResolvePreferredWidths(rightRanges, rightVisible);
        var preferredTotal = MeasureTwoSidedWidthsTotal(
            leftWidths,
            leftVisible,
            rightWidths,
            rightVisible,
            normalizedGroupGap,
            normalizedItemGap);
        var remainingReduction = MathF.Max(0f, preferredTotal - budget);

        var shrinkSteps = BuildTwoSidedShrinkSteps(
            leftRanges.Length,
            rightRanges.Length,
            leftPriorities,
            rightPriorities);
        for (var s = 0; s < shrinkSteps.Length && remainingReduction > 0f; s++)
        {
            var step = shrinkSteps[s];
            if (step.Group == LeftGroup)
            {
                if (!leftVisible[step.Index])
                    continue;
                ReduceWidth(
                    ref leftWidths[step.Index],
                    leftRanges[step.Index].MinimumWidth,
                    ref remainingReduction);
            }
            else
            {
                if (!rightVisible[step.Index])
                    continue;
                ReduceWidth(
                    ref rightWidths[step.Index],
                    rightRanges[step.Index].MinimumWidth,
                    ref remainingReduction);
            }
        }

        return new TwoSidedLayoutResult(
            BuildLayouts(leftWidths, leftVisible),
            BuildLayouts(rightWidths, rightVisible));
    }

    /// <summary>
    /// Clamp a resolved width to the 1:1 square minimum used by controls that
    /// support an icon-only compact form.
    /// </summary>
    internal static float ClampResolvedWidth(float width) =>
        MathF.Max(MetricsScope.UnitHeight, width);

    /// <summary>Resolved layout for a two-item adaptive split.</summary>
    internal readonly record struct AdaptiveSplitResult(
        float LeftWidth,
        float RightWidth,
        float Gap
    );

    /// <summary>
    /// Measure a two-item split where the right item receives its natural
    /// width and the left item fills the remainder. An optional protected
    /// width can reserve space for the left item.
    /// </summary>
    internal static AdaptiveSplitResult MeasureAdaptiveSplit(
        float totalWidth,
        float gap,
        float rightNaturalWidth,
        float leftProtectedWidth
    )
    {
        var rightWidth = MathF.Min(rightNaturalWidth, totalWidth);
        var effectiveGap = rightWidth > 0f ? gap : 0f;
        var leftWidth = MathF.Max(0f, totalWidth - rightWidth - effectiveGap);

        if (leftProtectedWidth > 0f && leftWidth > 0f && leftWidth < leftProtectedWidth)
        {
            leftWidth = MathF.Min(totalWidth, leftProtectedWidth);
            effectiveGap = leftWidth > 0f && leftWidth < totalWidth ? gap : 0f;
            rightWidth = MathF.Max(0f, totalWidth - leftWidth - effectiveGap);
        }

        if (leftWidth <= 0f || rightWidth <= 0f)
            effectiveGap = 0f;

        return new AdaptiveSplitResult(leftWidth, rightWidth, effectiveGap);
    }

    private static ResponsiveWidthRange[] NormalizeRanges(
        IReadOnlyList<ResponsiveWidthRange> items)
    {
        var ranges = new ResponsiveWidthRange[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var preferred = MathF.Max(0f, items[i].PreferredWidth);
            var minimum = Math.Clamp(items[i].MinimumWidth, 0f, preferred);
            ranges[i] = new ResponsiveWidthRange(preferred, minimum);
        }
        return ranges;
    }

    private static bool[] CreateVisibility(int count)
    {
        var visible = new bool[count];
        Array.Fill(visible, true);
        return visible;
    }

    private static float[] ResolvePreferredWidths(
        ResponsiveWidthRange[] ranges,
        bool[] visible)
    {
        var widths = new float[ranges.Length];
        for (var i = 0; i < ranges.Length; i++)
        {
            if (visible[i])
                widths[i] = ranges[i].PreferredWidth;
        }
        return widths;
    }

    /// <summary>
    /// Strip hide order: inner right slots, then the left area, then the right
    /// outer slot. Lower priorities move earlier; equal priorities keep this
    /// legacy order.
    /// </summary>
    private static SlotStep[] BuildStripHideSteps(
        int rightCount,
        IReadOnlyList<int> rightPriorities)
    {
        var steps = new SlotStep[rightCount + 1];
        var n = 0;
        for (var i = rightCount - 1; i >= 1; i--)
            steps[n++] = new SlotStep(
                rightPriorities[i],
                n - 1,
                RightGroup,
                i);
        steps[n++] = new SlotStep(0, n - 1, StripLeftGroup, -1);
        if (rightCount > 0)
            steps[n++] = new SlotStep(
                rightPriorities[0],
                n - 1,
                RightGroup,
                0);
        SortSteps(steps);
        return steps;
    }

    /// <summary>
    /// Strip shrink order: all right slots from the inner edge outward, then
    /// the left area. Lower priorities move earlier; equal priorities keep this
    /// legacy order.
    /// </summary>
    private static SlotStep[] BuildStripShrinkSteps(
        int rightCount,
        IReadOnlyList<int> rightPriorities)
    {
        var steps = new SlotStep[rightCount + 1];
        var n = 0;
        for (var i = rightCount - 1; i >= 0; i--)
            steps[n++] = new SlotStep(
                rightPriorities[i],
                n - 1,
                RightGroup,
                i);
        steps[n++] = new SlotStep(0, n - 1, StripLeftGroup, -1);
        SortSteps(steps);
        return steps;
    }

    /// <summary>
    /// Two-sided hide order: left inner slots, right inner slots, left outer
    /// slot, right outer slot. Lower priorities move earlier; equal priorities
    /// keep this legacy order.
    /// </summary>
    private static SlotStep[] BuildTwoSidedHideSteps(
        int leftCount,
        int rightCount,
        IReadOnlyList<int> leftPriorities,
        IReadOnlyList<int> rightPriorities)
    {
        var steps = new SlotStep[leftCount + rightCount];
        var n = 0;
        for (var i = leftCount - 1; i >= 1; i--)
            steps[n++] = new SlotStep(
                leftPriorities[i],
                n - 1,
                LeftGroup,
                i);
        for (var i = rightCount - 1; i >= 1; i--)
            steps[n++] = new SlotStep(
                rightPriorities[i],
                n - 1,
                RightGroup,
                i);
        if (leftCount > 0)
            steps[n++] = new SlotStep(
                leftPriorities[0],
                n - 1,
                LeftGroup,
                0);
        if (rightCount > 0)
            steps[n++] = new SlotStep(
                rightPriorities[0],
                n - 1,
                RightGroup,
                0);
        SortSteps(steps);
        return steps;
    }

    /// <summary>
    /// Two-sided shrink order: right slots from the inner edge outward, then
    /// left slots from the inner edge outward. Lower priorities move earlier;
    /// equal priorities keep this legacy order.
    /// </summary>
    private static SlotStep[] BuildTwoSidedShrinkSteps(
        int leftCount,
        int rightCount,
        IReadOnlyList<int> leftPriorities,
        IReadOnlyList<int> rightPriorities)
    {
        var steps = new SlotStep[leftCount + rightCount];
        var n = 0;
        for (var i = rightCount - 1; i >= 0; i--)
            steps[n++] = new SlotStep(
                rightPriorities[i],
                n - 1,
                RightGroup,
                i);
        for (var i = leftCount - 1; i >= 0; i--)
            steps[n++] = new SlotStep(
                leftPriorities[i],
                n - 1,
                LeftGroup,
                i);
        SortSteps(steps);
        return steps;
    }

    private static void SortSteps(SlotStep[] steps) =>
        Array.Sort(
            steps,
            static (a, b) => a.Priority != b.Priority
                ? a.Priority.CompareTo(b.Priority)
                : a.Rank.CompareTo(b.Rank));

    private static void ReduceWidth(
        ref float width,
        float minimum,
        ref float remainingReduction)
    {
        var reduction = MathF.Min(MathF.Max(0f, width - minimum), remainingReduction);
        width -= reduction;
        remainingReduction -= reduction;
    }

    private static float MeasureStripMinimumTotal(
        ResponsiveWidthRange[] ranges,
        bool[] visible,
        float leftMinimum,
        float outerGap,
        float itemGap)
    {
        var rightTotal = MeasureMinimumTotal(ranges, visible, itemGap);
        return leftMinimum + rightTotal + (rightTotal > 0f ? outerGap : 0f);
    }

    private static float MeasureTwoSidedMinimumTotal(
        ResponsiveWidthRange[] leftRanges,
        bool[] leftVisible,
        ResponsiveWidthRange[] rightRanges,
        bool[] rightVisible,
        float groupGap,
        float itemGap)
    {
        var leftTotal = MeasureMinimumTotal(leftRanges, leftVisible, itemGap);
        var rightTotal = MeasureMinimumTotal(rightRanges, rightVisible, itemGap);
        return leftTotal + rightTotal + (leftTotal > 0f && rightTotal > 0f ? groupGap : 0f);
    }

    private static float MeasureTwoSidedWidthsTotal(
        float[] leftWidths,
        bool[] leftVisible,
        float[] rightWidths,
        bool[] rightVisible,
        float groupGap,
        float itemGap)
    {
        var leftTotal = MeasureWidthsTotal(leftWidths, leftVisible, itemGap);
        var rightTotal = MeasureWidthsTotal(rightWidths, rightVisible, itemGap);
        return leftTotal + rightTotal + (leftTotal > 0f && rightTotal > 0f ? groupGap : 0f);
    }

    private static float MeasureMinimumTotal(
        ResponsiveWidthRange[] ranges,
        bool[] visible,
        float gap)
    {
        var total = 0f;
        var visibleCount = 0;
        for (var i = 0; i < ranges.Length; i++)
        {
            if (!visible[i])
                continue;

            total += ranges[i].MinimumWidth;
            visibleCount++;
        }
        return total + gap * Math.Max(0, visibleCount - 1);
    }

    private static float MeasureWidthsTotal(float[] widths, bool[] visible, float gap)
    {
        var total = 0f;
        var visibleCount = 0;
        for (var i = 0; i < widths.Length; i++)
        {
            if (!visible[i])
                continue;

            total += widths[i];
            visibleCount++;
        }
        return total + gap * Math.Max(0, visibleCount - 1);
    }

    private static float MeasureLayoutsTotal(ResponsiveItemLayout[] layouts, float gap)
    {
        var total = 0f;
        var visibleCount = 0;
        for (var i = 0; i < layouts.Length; i++)
        {
            if (!layouts[i].Visible)
                continue;

            total += layouts[i].Width;
            visibleCount++;
        }
        return total + gap * Math.Max(0, visibleCount - 1);
    }

    private static ResponsiveItemLayout[] BuildLayouts(float[] widths, bool[] visible)
    {
        var layouts = new ResponsiveItemLayout[widths.Length];
        for (var i = 0; i < widths.Length; i++)
            layouts[i] = new ResponsiveItemLayout(widths[i], visible[i]);
        return layouts;
    }

    private static int CountVisible(bool[] visible)
    {
        var count = 0;
        for (var i = 0; i < visible.Length; i++)
        {
            if (visible[i])
                count++;
        }
        return count;
    }
}
