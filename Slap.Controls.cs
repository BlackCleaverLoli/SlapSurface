using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;

namespace SlapSurface;

internal static partial class Slap
{
    // --- Checkbox ---

    /// <summary>Full-spec checkbox. Use when you need explicit key control.</summary>
    public static CheckboxResult Checkbox(CheckboxSpec spec) => CheckboxComponent.Draw(spec);

    /// <summary>Convenience checkbox: label doubles as the control key.
    /// <code>
    /// var r = Slap.Checkbox("Debug overlay", isEnabled);
    /// if (r.Changed) isEnabled = r.Checked;
    /// </code></summary>
    public static CheckboxResult Checkbox(
        string label,
        bool checkedValue,
        bool disabled = false,
        string? tooltip = null) =>
        CheckboxComponent.Draw(new CheckboxSpec(
            label, label, checkedValue,
            disabled ? ControlState.Disabled : ControlState.None,
            tooltip));

    // --- ToggleSwitch ---

    /// <summary>Full-spec toggle switch. Use when you need explicit key control or label position.</summary>
    public static ToggleSwitchResult ToggleSwitch(ToggleSwitchSpec spec) => ToggleSwitchComponent.Draw(spec);

    /// <summary>Convenience toggle switch: label doubles as the control key, defaults to left-positioned label.</summary>
    public static ToggleSwitchResult ToggleSwitch(
        string label,
        bool checkedValue,
        LabelPosition position = LabelPosition.Left,
        bool disabled = false,
        string? tooltip = null) =>
        ToggleSwitchComponent.Draw(new ToggleSwitchSpec(
            label, label, checkedValue,
            position,
            disabled ? ControlState.Disabled : ControlState.None,
            tooltip));

    /// <summary>
    /// Pre-measures the natural pixel width of a toggle switch with the given
    /// label and label position. Use this to reserve layout space (e.g. inside
    /// row scopes) before the toggle is actually drawn.
    /// </summary>
    public static float ResolveToggleSwitchWidth(
        string? label,
        LabelPosition position = LabelPosition.Left,
        FontAwesomeIcon? icon = null,
        string? tooltip = null) =>
        ToggleSwitchComponent.ResolveNaturalWidth(label, position, icon, tooltip: tooltip);

    // --- Button ---

    /// <summary>Full-spec button. Supports label, icon+label, and icon-only modes.</summary>
    public static ControlResult Button(ButtonSpec spec) => ButtonComponent.Draw(spec);

    /// <summary>
    /// Pre-measures the natural width of an auto-sized icon+text button.
    /// Use this to reserve layout space
    /// before the button is actually drawn.
    /// </summary>
    public static float ResolveButtonAutoWidth(
        string? label,
        FontAwesomeIcon? icon = null,
        uint gameIconId = 0) =>
        ButtonComponent.ResolveAutoWidth(icon, label, gameIconId);

    // --- List Kit ---

    public static void SurfaceList<T>(
        ContentZoneContext zone,
        SurfaceListSpec spec,
        SurfaceListSlots slots,
        IReadOnlyList<T> items,
        Func<T, int, SurfaceRowState> configureRow,
        Action<T, int, SurfaceRowContext> drawRow) =>
        SurfaceListComponent.Draw(zone, spec, slots, items, configureRow, drawRow);

    public static void SurfaceList<T, TRow>(
        ContentZoneContext zone,
        SurfaceListSpec spec,
        SurfaceListSlots slots,
        IReadOnlyList<T> items,
        Func<T, int, TRow> buildRow,
        Func<T, int, TRow, SurfaceRowState> configureRow,
        Action<T, int, TRow, SurfaceRowContext> drawRow) =>
        SurfaceListComponent.Draw(zone, spec, slots, items, buildRow, configureRow, drawRow);

    // --- SplitButton ---

    public static SplitButtonResult SplitButton(SplitButtonSpec spec) => SplitButtonComponent.Draw(spec);

    // --- CommandGroup ---

    public static CommandGroupResult CommandGroup(
        ContentZoneContext zone,
        CommandGroupSpec spec) =>
        CommandGroupComponent.Draw(zone, spec);

    /// <summary>
    /// Draws a command group with optional page-owned controls on the left.
    /// All controls share one responsive row measurement.
    /// </summary>
    public static CommandGroupResult CommandGroup(
        ContentZoneContext zone,
        CommandGroupSpec spec,
        Action<ResponsiveSlotGroup> collectLeft) =>
        CommandGroupComponent.Draw(zone, spec, collectLeft);

/// <summary>
/// Pre-measures the natural content width of a split button (main segment + menu segment).
/// Use this to reserve layout space (e.g. for <c>SplitAdaptive</c> rightNaturalWidth)
/// before the button is actually drawn.
/// </summary>
public static float ResolveSplitButtonNaturalWidth(SplitButtonSpec spec) =>
    SplitButtonComponent.ResolveNaturalWidth(spec);

// --- TabStrip ---

    /// <summary>Natural strip height in scaled pixels for layout pre-measurement.</summary>
    public static float ResolveTabStripHeight() => TabStripComponent.ResolveStripHeight();

    /// <summary>
    /// Estimate the total natural width of all tabs (including inter-tab gaps).
    /// Use this to calculate how much width a tab strip will consume before drawing,
    /// e.g. for responsive right-slot degradation.
    /// </summary>
    public static float EstimateTabStripWidth(IReadOnlyList<TabItemSpec> tabs) =>
        TabStripComponent.EstimateTabsWidth(tabs);

    public static TabStripResult TabStrip(TabStripSpec spec) => TabStripComponent.Draw(spec);

    /// <summary>Convenience tab strip: key + plain labels + selected index.</summary>
    public static TabStripResult TabStrip(
        ControlKey key,
        string[] tabLabels,
        int selectedIndex,
        Variant variant = Variant.Tab,
        float widthUnits = 0f)
    {
        var tabs = new TabItemSpec[tabLabels.Length];
        for (var i = 0; i < tabLabels.Length; i++)
            tabs[i] = new TabItemSpec(tabLabels[i]);
        return TabStripComponent.Draw(new TabStripSpec(
            key, tabs, selectedIndex, widthUnits, variant));
    }

    // --- Popup ---

    public static PopupPanelScope BeginPopupPanel(PopupPanelSpec spec) => PopupPanelComponent.Begin(spec);

    public static ControlResult PopupItem(PopupItemSpec spec) => PopupPanelComponent.DrawItem(spec);

    // --- Dropdown ---

    public static DropdownResult Dropdown(DropdownSpec spec) => DropdownComponent.Draw(spec);

    /// <summary>Convenience dropdown: key + plain string options + selected index.</summary>
    public static DropdownResult Dropdown(
        string key,
        string[] options,
        int selectedIndex,
        bool disabled = false,
        string? tooltip = null,
        ButtonSize size = default)
    {
        var opts = new DropdownOptionSpec[options.Length];
        for (var i = 0; i < options.Length; i++)
            opts[i] = new DropdownOptionSpec(options[i]);
        return DropdownComponent.Draw(new DropdownSpec(
            key, opts, selectedIndex,
            size,
            disabled ? ControlState.Disabled : ControlState.None,
            Tooltip: tooltip));
    }

    // --- GameIcon ---

    public static GameIconStripResult GameIconStrip(GameIconStripSpec spec) => GameIconStripComponent.Draw(spec);

    public static void GameIconStripAbsolute(
        GameIconStripSpec spec, ImDrawListPtr drawList, Vector2 origin, float width, float? height = null) =>
        GameIconStripComponent.DrawAbsolute(spec, drawList, origin, width, height);

    public static void GameIconStripAbsolute(
        GameIconStripSpec spec,
        ImDrawListPtr drawList,
        Vector2 origin,
        float width,
        ControlVisualMode visualMode,
        float? height = null) =>
        GameIconStripComponent.DrawAbsolute(spec, drawList, origin, width, height, visualMode);

    public static ControlResult GameIconButton(GameIconSpec spec) => GameIconStripComponent.DrawGameIconButton(spec);

    /// <summary>
    /// Standard game icon rendering: multi-layer embossed shadow, semi-transparent
    /// background, rounded texture image, and optional overlays. No border.
    /// The <paramref name="min"/>/<paramref name="max"/> rect is the icon slot;
    /// shadow padding is managed internally so the icon stays within bounds.
    /// </summary>
    public static void DrawGameIcon(
        uint iconId,
        Vector2 min,
        Vector2 max,
        bool highQuality = false,
        GameIconShape shape = GameIconShape.Standard,
        Vector4? tint = null,
        FontAwesomeIcon? overlayIcon = null,
        Vector4? overlayColor = null,
        bool cornerCheck = false,
        bool shadow = true) =>
        GameIconComponent.DrawInSlot(
            ImGui.GetWindowDrawList(),
            iconId,
            min,
            max,
            highQuality,
            shape,
            tint,
            overlayIcon,
            overlayColor,
            cornerCheck: cornerCheck,
            shadow: shadow
        );

    /// <summary>
    /// Draw a game icon using the interaction mode already resolved by its
    /// owning control or content slot.
    /// </summary>
    public static void DrawGameIcon(
        uint iconId,
        Vector2 min,
        Vector2 max,
        ControlVisualMode visualMode,
        bool highQuality = false,
        GameIconShape shape = GameIconShape.Standard,
        Vector4? tint = null,
        FontAwesomeIcon? overlayIcon = null,
        Vector4? overlayColor = null,
        bool cornerCheck = false,
        bool shadow = true) =>
        GameIconComponent.DrawInSlot(
            ImGui.GetWindowDrawList(),
            iconId,
            min,
            max,
            highQuality,
            shape,
            tint,
            overlayIcon,
            overlayColor,
            visualMode: visualMode,
            cornerCheck: cornerCheck,
            shadow: shadow
        );

    /// <summary>
    /// Draw a pre-resolved texture in the standard game-icon frame. The supplied
    /// rectangle is the icon slot; shadow padding is managed internally.
    /// </summary>
    public static void DrawGameIcon(
        IDalamudTextureWrap? texture,
        Vector2 min,
        Vector2 max,
        ControlVisualMode visualMode = ControlVisualMode.Base) =>
        GameIconComponent.DrawInSlot(
            ImGui.GetWindowDrawList(),
            texture,
            min,
            max,
            visualMode);

    // --- DragFloat ---

    public static DragFloatResult DragFloat(DragFloatSpec spec, float value) =>
        DragFloatComponent.Draw(spec, value);

    // --- TextInput ---

    public static TextInputResult TextInput(TextInputSpec spec, string value, int maxLength = 256) =>
        TextInputComponent.Draw(spec, value, maxLength);

    public static AutocompleteTextInputResult AutocompleteTextInput(
        AutocompleteTextInputSpec spec,
        AutocompleteTextInputState state,
        string value,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider,
        int maxLength = 256
    ) => AutocompleteTextInputComponent.Draw(
        spec,
        state,
        value,
        optionsProvider,
        maxLength
    );

    // --- MediaTile ---

    public static ControlResult MediaTile(MediaTileSpec spec) =>
        MediaTileComponent.Draw(spec);

    public static void MediaTileGrid<T>(
        MediaTileGridSpec spec,
        IReadOnlyList<T> items,
        Func<T, int, MediaTileSpec> buildTile,
        Action<T, int, ControlResult>? onResult = null
    ) => MediaTileGridComponent.Draw(spec, items, buildTile, onResult);

    // --- SettingRow ---

    /// <summary>Raw setting row: caller draws the control.</summary>
    public static void SettingRow(SettingRowSpec spec, Action<Vector2, float> drawControl) =>
        SettingRowComponent.Draw(spec, drawControl);

    /// <summary>Setting row with an embedded checkbox.</summary>
    public static CheckboxResult SettingCheckboxRow(SettingCheckboxRowSpec spec) =>
        SettingRowComponent.DrawCheckbox(spec);

    /// <summary>Setting row with an embedded dropdown combo.</summary>
    public static DropdownResult SettingComboRow(SettingComboRowSpec spec) =>
        SettingRowComponent.DrawCombo(spec);

    /// <summary>Setting row with an embedded button.</summary>
    public static ControlResult SettingActionRow(SettingActionRowSpec spec) =>
        SettingRowComponent.DrawAction(spec);

    /// <summary>Setting row with an embedded toggle switch.</summary>
    public static ToggleSwitchResult SettingToggleRow(SettingToggleRowSpec spec) =>
        SettingRowComponent.DrawToggleRow(spec);

    /// <summary>Setting row with an embedded drag-float slider.</summary>
    public static DragFloatResult SettingDragFloatRow(SettingDragFloatRowSpec spec) =>
        SettingRowComponent.DrawDragFloat(spec);

    /// <summary>Setting row with an embedded text input.</summary>
    public static TextInputResult SettingTextInputRow(SettingTextInputRowSpec spec) =>
        SettingRowComponent.DrawTextInput(spec);

    /// <summary>Setting row with an embedded autocomplete text input.</summary>
    public static AutocompleteTextInputResult SettingAutocompleteTextInputRow(
        SettingAutocompleteTextInputRowSpec spec,
        AutocompleteTextInputState state,
        Func<string, IReadOnlyList<AutocompleteOptionSpec>> optionsProvider) =>
        SettingRowComponent.DrawAutocompleteTextInput(spec, state, optionsProvider);

    // --- RowList ---

    public static void RowList<T>(
        RowListSpec spec,
        IReadOnlyList<T> items,
        float rowHeight,
        Action<T, int, RowListRowContext> drawRow) =>
        RowListComponent.Draw(spec, items, rowHeight, drawRow);

    // --- Popup Sidebar ---

    public static PopupSidebarScope BeginPopupSidebar(PopupSidebarSpec spec) => PopupSidebarComponent.Begin(spec);

    public static ModalDialogResult ModalDialog(
        ModalDialogSpec spec,
        Action drawBody,
        Action<ResponsiveSlotGroup>? collectActions = null,
        Action? drawScrollableContent = null
    ) => ModalDialogComponent.Draw(
        spec,
        drawBody,
        collectActions,
        drawScrollableContent
    );

    /// <summary>
    /// Draw a wrapped modal body paragraph in the current content zone using
    /// the modal body font and centered horizontal alignment.
    /// </summary>
    public static void ModalBodyText(string text, TextSlot semantic = TextSlot.Body) =>
        ModalDialogComponent.DrawBodyText(text, semantic);
}
