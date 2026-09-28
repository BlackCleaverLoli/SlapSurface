using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;

namespace SlapSurface;

/// <summary>
/// Optional bottom card within the shell content column. The card takes its
/// natural content height plus card padding; the main slot receives the
/// remaining height above the gap.
/// </summary>
internal readonly record struct ShellBottomCardSpec(
    ContentCardSpec Card,
    float ContentHeightUnits = 1f,
    float Gap = SlapPx.Space4,
    ImGuiWindowFlags MainChildFlags = ImGuiWindowFlags.None
);

internal readonly record struct ShellSlotBounds(Vector2 Min, Vector2 Max)
{
    public Vector2 Size => Max - Min;
}

internal readonly record struct ShellBottomCardBounds(
    ShellSlotBounds Main,
    ShellSlotBounds Card
);

internal static class ShellBottomCardComponent
{
    public static ShellBottomCardBounds Draw(
        ShellBottomCardSpec spec,
        Action<ShellSlotBounds> drawMain,
        Action<ContentZoneContext> drawCard
    )
    {
        ArgumentNullException.ThrowIfNull(drawMain);
        ArgumentNullException.ThrowIfNull(drawCard);
        if (string.IsNullOrWhiteSpace(spec.Card.Key.Value))
            throw new ArgumentException(
                "ShellBottomCardSpec.Card.Key.Value must not be empty.",
                nameof(spec)
            );

        var origin = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail();
        var width = MathF.Max(0f, available.X);
        var height = MathF.Max(0f, available.Y);
        var contentHeightUnits = spec.ContentHeightUnits > 0f
            && !float.IsInfinity(spec.ContentHeightUnits)
                ? spec.ContentHeightUnits
                : 1f;
        var requestedCardHeight = ContentCardComponent.ResolveNaturalHeight(
            spec.Card,
            MetricsScope.UnitHeight * contentHeightUnits
        );
        var cardHeight = MathF.Min(height, requestedCardHeight);
        var availableAboveCard = MathF.Max(0f, height - cardHeight);
        var requestedGap = spec.Gap >= 0f && !float.IsInfinity(spec.Gap)
            ? MetricsScope.ScaleGap(spec.Gap)
            : 0f;
        var gap = MathF.Min(availableAboveCard, requestedGap);
        var mainHeight = MathF.Max(0f, availableAboveCard - gap);

        var main = new ShellSlotBounds(
            origin,
            origin + new Vector2(width, mainHeight)
        );
        var cardMin = origin + new Vector2(0f, mainHeight + gap);
        var card = new ShellSlotBounds(
            cardMin,
            cardMin + new Vector2(width, cardHeight)
        );
        var bounds = new ShellBottomCardBounds(main, card);

        if (mainHeight > 0f && width > 0f)
        {
            ImGui.SetCursorScreenPos(main.Min);
            ImGui.BeginChild(
                $"{spec.Card.Key.Value}_main",
                main.Size,
                false,
                spec.MainChildFlags
            );
            try
            {
                drawMain(main);
            }
            finally
            {
                ImGui.EndChild();
            }
        }

        if (cardHeight > 0f && width > 0f)
        {
            ImGui.SetCursorScreenPos(card.Min);
            using var cardScope = ContentCardComponent.Begin(spec.Card);
            cardScope.Zone(cardScope.RemainingHeight, drawCard);
        }

        ImGui.SetCursorScreenPos(origin + new Vector2(0f, height));
        return bounds;
    }
}
