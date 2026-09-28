using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Interactive square media with a fixed two-line identity block beneath it.
/// A caller-provided texture takes priority over <see cref="GameIconId"/>.
/// </summary>
internal readonly record struct MediaTileSpec(
    ControlKey Key,
    string Line1,
    string? Line2 = null,
    uint GameIconId = 0,
    IDalamudTextureWrap? Texture = null,
    float MediaSizeUnits = 2f,
    ControlState State = ControlState.None,
    string? Tooltip = null,
    float ResolvedMediaSize = 0f
);

/// <summary>Responsive square-media grid sizing in Slap unit heights.</summary>
internal readonly record struct MediaTileGridSpec(
    ControlKey Key,
    float MinimumMediaSizeUnits = 2f,
    float MaximumMediaSizeUnits = 3f,
    float ColumnGap = SlapPx.Space12,
    float RowGap = SlapPx.Space12,
    /// <summary>
    /// Fixed content width for grid layout. When set, tiles are sized independently
    /// of scrollbar visibility. The host window should place its scrollbar outside
    /// this width. When null, falls back to <c>GetContentRegionAvail().X</c>.
    /// </summary>
    float? ContentWidth = null
);

internal static class MediaTileComponent
{
    private const float MediaTextGap = SlapPx.Space8;

    internal static ControlResult Draw(MediaTileSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("MediaTileSpec.Key.Value must not be empty.", nameof(spec));

        var mediaSize = spec.ResolvedMediaSize > 0f
            ? spec.ResolvedMediaSize
            : MetricsScope.UnitHeight * NormalizeUnits(spec.MediaSizeUnits, 2f);
        var textSpec = new TwoLineTextSpec(
            spec.Line1 ?? string.Empty,
            spec.Line2 ?? string.Empty,
            SlapFontSize.Regular,
            SlapFontWeight.Bold,
            SlapFontSize.Small,
            SlapFontWeight.Bold,
            ThemeScope.Resolved.Body,
            ThemeScope.Resolved.Subtle,
            RightFadeBackground: ThemeScope.Resolved.Surface
        );
        var textMetrics = TwoLineTextComponent.Measure(textSpec);
        var textDegraded = textMetrics.Line1Width > mediaSize
            || textMetrics.Line2Width > mediaSize;
        var tooltip = ResponsiveTooltip.Compose(textDegraded, spec.Line1, spec.Tooltip);
        var textHeight = ResolveTextHeight();
        var textGap = MetricsScope.ScaleGap(MediaTextGap);
        var totalSize = new Vector2(mediaSize, mediaSize + textGap + textHeight);

        ImGui.PushID(spec.Key.Value);
        using var hover = HoverArbitration.Push();
        try
        {
            var min = ImGui.GetCursorScreenPos();
            var mediaSlotMax = min + new Vector2(mediaSize);
            var (mediaMin, mediaMax) = GameIconComponent.ResolveIconRect(
                min,
                mediaSlotMax
            );

            ImGui.InvisibleButton("##mediaTile", totalSize);
            var interaction = SlapInteraction.Capture(
                spec.State,
                ImGui.IsItemClicked(ImGuiMouseButton.Left),
                captureRightClick: true
            );

            var visualMode = interaction.VisualMode;
            var drawList = ImGui.GetWindowDrawList();
            DrawMedia(drawList, spec, mediaMin, mediaMax, visualMode);

            TwoLineTextComponent.DrawInRect(
                drawList,
                textSpec,
                new Vector2(min.X, mediaSlotMax.Y + textGap),
                min + totalSize
            );

            HoverArbitration.Current.TryShowTooltip(
                interaction.Hovered,
                tooltip
            );
            return interaction.ToControlResult(min, min + totalSize);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static void DrawMedia(
        ImDrawListPtr drawList,
        MediaTileSpec spec,
        Vector2 min,
        Vector2 max,
        ControlVisualMode visualMode
    )
    {
        if (spec.Texture != null)
        {
            GameIconComponent.DrawResolved(drawList, spec.Texture, min, max, visualMode);
            return;
        }

        GameIconComponent.Draw(
            drawList,
            spec.GameIconId,
            min,
            max,
            visualMode: visualMode
        );
    }

    private static float NormalizeUnits(float value, float fallback) =>
        value > 0f && !float.IsInfinity(value) ? value : fallback;

    internal static float ResolveHeight(float mediaSize) =>
        mediaSize + MetricsScope.ScaleGap(MediaTextGap) + ResolveTextHeight();

    private static float ResolveTextHeight() =>
        TwoLineTextComponent.Measure(
            new TwoLineTextSpec(
                "Ag",
                "Ag",
                SlapFontSize.Regular,
                SlapFontWeight.Bold,
                SlapFontSize.Small,
                SlapFontWeight.Bold
            )
        ).Height;
}

internal static class MediaTileGridComponent
{
    internal static void Draw<T>(
        MediaTileGridSpec spec,
        IReadOnlyList<T> items,
        Func<T, int, MediaTileSpec> buildTile,
        Action<T, int, ControlResult>? onResult
    )
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(buildTile);
        if (items.Count == 0)
            return;

        ImGui.PushID(spec.Key.Value);
        try
        {
            var availableWidth = MathF.Max(
                0f,
                spec.ContentWidth ?? ImGui.GetContentRegionAvail().X
            );
            if (availableWidth <= 0f)
                return;

            var columnGap = MetricsScope.ScaleGap(spec.ColumnGap);
            var rowGap = MetricsScope.ScaleGap(spec.RowGap);
            var minimum = MetricsScope.UnitHeight * NormalizeUnits(
                spec.MinimumMediaSizeUnits,
                2f
            );
            var maximum = MetricsScope.UnitHeight * MathF.Max(
                NormalizeUnits(spec.MaximumMediaSizeUnits, 3f),
                minimum / MetricsScope.UnitHeight
            );
            var fittingColumns = Math.Max(
                1,
                (int)MathF.Floor((availableWidth + columnGap) / (minimum + columnGap))
            );
            var columns = Math.Min(items.Count, fittingColumns);
            var fittedMediaSize = MathF.Min(
                maximum,
                MathF.Max(
                    minimum,
                    (availableWidth - columnGap * (columns - 1)) / columns
                )
            );
            var mediaSize = MathF.Min(availableWidth, fittedMediaSize);
            var tileHeight = MediaTileComponent.ResolveHeight(mediaSize);
            var rowCount = (items.Count + columns - 1) / columns;
            var outlineBleed = MetricsScope.ScaleGap(SlapPx.Space6);
            var contentHeight = tileHeight * rowCount + rowGap * (rowCount - 1);
            var totalHeight = contentHeight + outlineBleed * 2f;
            var gridMin = ImGui.GetCursorScreenPos();
            var origin = gridMin + new Vector2(0f, outlineBleed);

            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var column = i % columns;
                var row = i / columns;
                ImGui.SetCursorScreenPos(
                    origin
                    + new Vector2(
                        column * (mediaSize + columnGap),
                        row * (tileHeight + rowGap)
                    )
                );
                var result = MediaTileComponent.Draw(
                    buildTile(item, i) with { ResolvedMediaSize = mediaSize }
                );
                onResult?.Invoke(item, i, result);
            }

            ImGui.SetCursorScreenPos(gridMin);
            ImGui.Dummy(new Vector2(availableWidth, totalHeight));
        }
        finally
        {
            ImGui.PopID();
        }
    }

    private static float NormalizeUnits(float value, float fallback) =>
        value > 0f && !float.IsInfinity(value) ? value : fallback;
}
