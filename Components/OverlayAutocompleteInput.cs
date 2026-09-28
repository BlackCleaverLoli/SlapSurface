using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Specification for a full-host overlay autocomplete search: a dim mask
/// shared with modal dialogs, an input box centered horizontally near the top
/// of the content area, and the autocomplete popup protocol. Must be drawn at
/// the top-level host window scope (see <see cref="OverlayMaskComponent"/>).
/// </summary>
internal readonly record struct OverlayAutocompleteInputSpec(
    AutocompleteTextInputSpec Autocomplete,
    float OverlayAlpha = 0.33f,
    float TopOffsetUnits = 1f,
    float MaxInputWidthUnits = 3f
);

internal static class OverlayAutocompleteInputComponent
{
    private static readonly Dictionary<string, int> FocusFramesByKey = new();

    internal static void Reset()
    {
        FocusFramesByKey.Clear();
    }

    internal static AutocompleteTextInputResult Draw(
        OverlayAutocompleteInputSpec spec,
        AutocompleteTextInputState state,
        string value,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider
    )
    {
        if (string.IsNullOrWhiteSpace(spec.Autocomplete.Input.Key.Value))
        {
            throw new ArgumentException(
                "OverlayAutocompleteInputSpec.Autocomplete.Input.Key.Value must not be empty.",
                nameof(spec)
            );
        }

        if (optionsProvider is null)
            throw new ArgumentNullException(nameof(optionsProvider));

        var key = spec.Autocomplete.Input.Key.Value;
        var windowPos = ImGui.GetWindowPos();
        var windowMax = windowPos + ImGui.GetWindowSize();

        // Close when the user clicks outside the host window, even if the click
        // lands on the game UI or another ImGui window.
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var mouse = ImGui.GetIO().MousePos;
            var insideWindow =
                mouse.X >= windowPos.X && mouse.X <= windowMax.X
                && mouse.Y >= windowPos.Y && mouse.Y <= windowMax.Y;
            if (!insideWindow)
            {
                FocusFramesByKey.Remove(key);
                return new AutocompleteTextInputResult(
                    value,
                    false,
                    false,
                    false,
                    true,
                    null
                );
            }
        }

        OverlayMaskScope? mask = null;
        var inputStylePushed = false;
        var inputBegun = false;
        var idPushed = false;
        try
        {
            ImGui.PushID($"OverlayAutocomplete_{key}");
            idPushed = true;
            mask = OverlayMaskComponent.Begin(
                new OverlayMaskSpec(
                    key,
                    ShowOverlay: true,
                    spec.OverlayAlpha,
                    NoMove: true
                )
            );
            if (!mask.IsOpen)
            {
                return new AutocompleteTextInputResult(
                    value,
                    false,
                    false,
                    false,
                    false,
                    null
                );
            }

            var inputWidth = MathF.Min(
                mask.WindowSize.X,
                MetricsScope.UnitWidth * MathF.Max(spec.MaxInputWidthUnits, 1f)
            );
            var inputMin = new Vector2(
                mask.WindowMin.X + (mask.WindowSize.X - inputWidth) * 0.5f,
                mask.HostMin.Y + MetricsScope.UnitHeight * MathF.Max(spec.TopOffsetUnits, 0f)
            );
            var inputMax = new Vector2(
                inputMin.X + inputWidth,
                inputMin.Y + MetricsScope.UnitHeight
            );

            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            inputStylePushed = true;
            ImGui.SetCursorScreenPos(inputMin);
            inputBegun = ImGui.BeginChild(
                "##input",
                inputMax - inputMin,
                false,
                ImGuiWindowFlags.NoScrollbar
                    | ImGuiWindowFlags.NoScrollWithMouse
                    | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoBackground
            );
            if (!inputBegun)
            {
                ImGui.EndChild();
                return new AutocompleteTextInputResult(
                    value,
                    false,
                    false,
                    false,
                    false,
                    null
                );
            }

            SurfaceComponent.DrawFrostedPanelBackground(
                ImGui.GetWindowDrawList(),
                inputMin,
                inputMax,
                SlapCorners.ControlRadius
            );

            var mouse = ImGui.GetIO().MousePos;
            var insideInput =
                mouse.X >= inputMin.X && mouse.X <= inputMax.X
                && mouse.Y >= inputMin.Y && mouse.Y <= inputMax.Y;
            var leftClicked = ImGui.IsMouseClicked(ImGuiMouseButton.Left);

            var focusFrames = FocusFramesByKey.TryGetValue(key, out var cachedFrames)
                ? cachedFrames
                : 2;
            if (focusFrames > 0)
            {
                ImGui.SetKeyboardFocusHere();
                focusFrames--;
            }
            FocusFramesByKey[key] = focusFrames;

            var result = AutocompleteTextInputComponent.Draw(
                spec.Autocomplete with
                {
                    Input = spec.Autocomplete.Input with { ResolvedWidth = inputWidth },
                },
                state,
                value,
                optionsProvider
            );

            if (result.SelectedOption != null)
            {
                FocusFramesByKey.Remove(key);
                return result;
            }

            var outsideInputAndPopup =
                !insideInput && !IsPointInAutocompletePopup(key, mouse);
            var shouldClose = (leftClicked || result.Deactivated) && outsideInputAndPopup;
            if (shouldClose)
            {
                FocusFramesByKey.Remove(key);
                return result with { Deactivated = true };
            }

            // A focus loss caused by clicking inside the input or the
            // autocomplete popup must not close the overlay; closing is
            // decided solely by shouldClose.
            return result with { Deactivated = false };
        }
        finally
        {
            if (inputBegun)
                ImGui.EndChild();
            if (inputStylePushed)
                ImGui.PopStyleVar();
            mask?.Dispose();
            if (idPushed)
                ImGui.PopID();
        }
    }

    private static bool IsPointInAutocompletePopup(string key, Vector2 point)
    {
        var popupId = $"{key}##autocompletePopup";
        if (!ImGui.IsPopupOpen(popupId))
            return false;

        var popupWindow = ImGuiP.FindWindowByName($"##Popup_{ImGui.GetID(popupId):x8}");
        if (popupWindow.IsNull)
            return false;

        var popupMin = popupWindow.Pos;
        var popupMax = popupMin + popupWindow.Size;
        return point.X >= popupMin.X && point.X <= popupMax.X
            && point.Y >= popupMin.Y && point.Y <= popupMax.Y;
    }
}

internal static partial class Slap
{
    /// <summary>
    /// Draws a full-host overlay with an autocomplete input centered near the
    /// top of the content area, sharing the modal mask protocol. The input
    /// receives keyboard focus on open; outside-window clicks and input
    /// deactivation close the overlay, while clicks inside the autocomplete
    /// popup are exempt. Escape is not intercepted. Must be drawn at the
    /// top-level host window scope.
    /// </summary>
    public static AutocompleteTextInputResult OverlayAutocompleteInput(
        OverlayAutocompleteInputSpec spec,
        AutocompleteTextInputState state,
        string value,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider
    ) => OverlayAutocompleteInputComponent.Draw(spec, state, value, optionsProvider);

    /// <summary>
    /// Clears the static focus-transition state of overlay autocomplete
    /// inputs. Host windows should call this when they hide, so an input left
    /// open at hide time cannot re-enter its focus or close transition on the
    /// next open.
    /// </summary>
    public static void ResetOverlayAutocompleteInputState() =>
        OverlayAutocompleteInputComponent.Reset();
}
