using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using SlapSurface.Metrics;
using SlapSurface.Theme;

namespace SlapSurface;

/// <summary>
/// Option for a split button dropdown.
/// </summary>
internal readonly record struct SplitButtonOption(
    ControlKey Key,
    string Label,
    string? Tooltip = null,
    FontAwesomeIcon? Icon = null,
    uint GameIconId = 0,
    bool Selected = false,
    bool Disabled = false
);

/// <summary>
/// Specification for a split button — a composite control with a main action
/// and a dropdown menu, sharing a single rounded frame.
/// </summary>
internal readonly record struct SplitButtonSpec(
    ControlKey Key,
    string MainLabel,
    ButtonSize Size = default,
    Variant Variant = Variant.Action,
    ControlState State = ControlState.None,
    string? MainTooltip = null,
    FontAwesomeIcon? MainIcon = null,
    uint MainGameIconId = 0,
    IReadOnlyList<SplitButtonOption>? Options = null,
    string? MenuTooltip = null,
    float MenuWidthRatio = 0f,
    FontAwesomeIcon? MenuIcon = FontAwesomeIcon.AngleDown,
    EdgeFadeSpec? EdgeFade = null,
    /// <summary>Optional semantic accent. When set, overrides the variant’s
    /// default text and border color with the corresponding
    /// <see cref="TextSlot"/> value.</summary>
    TextSlot? Semantic = null,
    LayeredShadowSpec Shadow = default,
    /// <summary>
    /// When &gt; 0, overrides <see cref="Size"/> width. Responsive measurement
    /// treats an explicit Rect width as a preferred baseline, while this
    /// resolved width may shrink below it to the control's structural minimum.
    /// Label clip/fade is handled in DrawMainContent.
    /// </summary>
    float ResolvedWidth = 0f,
    /// <summary>
    /// When true, a disabled split button also disables the dropdown menu
    /// segment. Defaults to false to preserve the legacy behavior where the
    /// menu stays interactive even while the main action is disabled.
    /// </summary>
    bool DisableMenuWhenDisabled = false
);

/// <summary>Result of a split button interaction.</summary>
internal readonly record struct SplitButtonResult(
    bool MainClicked,
    int MenuClickedIndex,
    bool Changed
);

internal static class SplitButtonComponent
{
    private const float TextPaddingX = SlapPx.Space16;
    private const float IconLeftPad = SlapPx.Space4;
    private const float IconGap = 0f;
    private const float MenuArrowRatio = 0.35f;

    private static float ResolveIconSlotWidth() => MetricsScope.ResolveIconSlotWidth(false);

    /// <summary>
    /// Minimum drawable width: icon slot + menu segment (or menu-only when no icon).
    /// <see cref="SplitButtonComponent.Draw"/> and <see cref="StackMeasure.ResolveSplitButtonWidth"/>
    /// both use this so that resolved-width never shrinks below the content floor.
    /// </summary>
    public static float ResolveMinWidth(SplitButtonSpec spec)
    {
        var h = MetricsScope.UnitHeight;
        var iconSlotWidth = ResolveIconSlotWidth();
        var resolvedMenuWidth = spec.MenuWidthRatio > 0f ? h * spec.MenuWidthRatio : h;
        var hasIcon = spec.MainGameIconId != 0 || spec.MainIcon.HasValue;
        return hasIcon ? MathF.Max(h, iconSlotWidth + resolvedMenuWidth) : resolvedMenuWidth;
    }

    public static SplitButtonResult Draw(SplitButtonSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Key.Value))
            throw new ArgumentException("SplitButtonSpec.Key.Value must not be empty.", nameof(spec));

        ImGui.PushID(spec.Key.Value);
        try
        {
            var h = MetricsScope.UnitHeight;
            Vector2 size;
            if (spec.ResolvedWidth > 0f)
            {
                size = new Vector2(MathF.Max(ResolveMinWidth(spec), spec.ResolvedWidth), h);
            }
            else
            {
                size = ResolveSize(spec);
            }
            var disabled = spec.State.HasFlag(ControlState.Disabled);
            var menuDisabled = disabled && spec.DisableMenuWhenDisabled;
            var menuWidth = ResolveMenuWidth(spec, size);
            var mainWidth = MathF.Max(0f, size.X - menuWidth);
            if (mainWidth <= 0f || size.Y <= 0f)
                return default;

            var start = ImGui.GetCursorScreenPos();
            var mainMin = start;
            var mainMax = mainMin + new Vector2(mainWidth, size.Y);
            var menuMin = new Vector2(mainMax.X, mainMin.Y);
            var menuMax = menuMin + new Vector2(menuWidth, size.Y);
            var fullMin = mainMin;
            var fullMax = menuMax;
            var rt = ThemeScope.Resolved;
            var rounding = SlapCorners.ControlRadius;
            var drawList = ImGui.GetWindowDrawList();
            using var _hover = HoverArbitration.Push();
            var palette = rt.GetButtonPalette(spec.Variant);

            // Main button
            ImGui.SetCursorScreenPos(mainMin);
            var mainRawClicked = ImGui.InvisibleButton("##main", mainMax - mainMin);
            var mainIx = SlapInteraction.Capture(spec.State, mainRawClicked);

            // Menu button — always interactive (matches V2 where menu is clickable even when running)
            ImGui.SetCursorScreenPos(menuMin);
            ImGui.InvisibleButton("##menu", menuMax - menuMin);
            var menuHovered = HoverArbitration.Current.CaptureHover(ImGui.IsItemHovered());
            var menuActive = !menuDisabled && menuHovered && ImGui.IsItemActive();
            var menuClicked = !menuDisabled && menuHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
            var popupId = "##splitMenuPopup";

            var buttonHovered = mainIx.Hovered || menuHovered;
            var borderStrength = SurfaceComponent.ResolveBorderStrength(spec.Variant);
            var mainColors = SurfaceComponent.ResolveControlColors(
                palette,
                spec.State,
                spec.Semantic,
                mainIx.Hovered,
                mainIx.Active,
                borderStrength,
                spec.Variant);
            // The menu segment is intentionally always interactive (matches V2,
            // where it stays clickable even while the main action is disabled).
            // Resolve its colors without the disabled flag so it does not render
            // greyed out while still accepting clicks.
            var menuState = menuDisabled ? spec.State : spec.State & ~ControlState.Disabled;
            var menuColors = SurfaceComponent.ResolveControlColors(
                palette,
                menuState,
                spec.Semantic,
                menuHovered,
                menuActive,
                borderStrength,
                spec.Variant);
            var mainBg = mainColors.Background;
            var menuBg = menuColors.Background;

            // Shadow (drawn before background so it doesn't cover the button).
            // Push a fullscreen clip so the spread isn't cut off by the
            // enclosing window/child bounds (matches Button/ContentCard).
            LayeredShadow.Draw(
                drawList,
                fullMin,
                fullMax,
                rounding,
                LayeredShadow.ResolveControlDefault(spec.Shadow, spec.Variant) with { ExpandHorizontalClip = true });

            drawList.AddRectFilled(mainMin, mainMax, ImGui.ColorConvertFloat4ToU32(mainBg),
                rounding, ImDrawFlags.RoundCornersLeft);
            drawList.AddRectFilled(menuMin, menuMax, ImGui.ColorConvertFloat4ToU32(menuBg),
                rounding, ImDrawFlags.RoundCornersRight);

            // Border — semantic tint overrides variant default
            var border = mainColors.Border;
            var borderColor = ImGui.ColorConvertFloat4ToU32(border);
            SurfaceComponent.DrawControlBorder(
                drawList,
                fullMin,
                fullMax,
                spec.Variant,
                border,
                rounding);

            // Divider line — hidden when the frame has no border
            if (spec.Variant != Variant.FlatNoBorder)
            {
                drawList.AddLine(
                    new Vector2(menuMin.X, menuMin.Y + MetricsScope.ScaleBorder(SlapPx.Space1)),
                    new Vector2(menuMin.X, menuMax.Y - MetricsScope.ScaleBorder(SlapPx.Space1)),
                    borderColor,
                    MetricsScope.BorderThickness);
            }

            // Hover outline — use semantic color if set, else variant accent
            var outlineAccent = rt.ResolveOutlineAccent(spec.Variant, spec.Semantic);
            var outline = new SlapOutlineGroup();
            if (buttonHovered)
                outline.Capture(
                    fullMin,
                    fullMax,
                    buttonHovered,
                    selected: false,
                    accent: outlineAccent,
                    rounding: rounding);

            // Text color — semantic overrides variant default
            var textColor = mainColors.Text;

            // Main content
            DrawMainContent(drawList, spec, mainMin, mainMax,
                textColor, mainBg, spec.EdgeFade, mainIx.VisualMode);

            // Menu arrow — use the menu's own text color so it stays full-alpha
            // when the main action is disabled.
            DrawMenuArrow(drawList, menuMin, menuMax,
                menuColors.Text,
                spec.MenuIcon);

            outline.Flush(spec.Key, 1f);

            // Tooltips
            var mainTooltip = ResponsiveTooltip.Compose(
                spec.ResolvedWidth > 0f && size.X + 0.5f < ResolveNaturalWidth(spec),
                spec.MainLabel,
                spec.MainTooltip);
            HoverArbitration.Current.TryShowTooltip(mainIx.Hovered, mainTooltip, mainMin, mainMax);
            HoverArbitration.Current.TryShowTooltip(menuHovered, spec.MenuTooltip, menuMin, menuMax);

            // Popup
            var menuClickedIndex = -1;
            if (menuClicked)
                ImGui.OpenPopup(popupId);

            if (!menuDisabled)
                DrawMenuPopup(popupId, spec, fullMin, fullMax, ref menuClickedIndex);

            return new SplitButtonResult(mainIx.Clicked, menuClickedIndex, menuClickedIndex >= 0);
        }
        finally
        {
            ImGui.PopID();
        }
    }

    /// <summary>
    /// Pre-measure the natural content width of a split button (main segment + menu segment).
    /// Use this to reserve layout space (e.g. for <c>SplitAdaptive</c> rightNaturalWidth)
    /// before the button is actually drawn.
    /// <para>
    /// The main segment width is derived from the spec's icon, label, padding and gap
    /// using the same constants as <see cref="DrawMainContent"/>. The menu segment width
    /// follows <see cref="ResolveMenuWidth"/> logic (UnitHeight by default, or
    /// UnitHeight × <see cref="SplitButtonSpec.MenuWidthRatio"/> when set).
    /// Label-only Auto buttons retain their existing two-unit main-segment baseline.
    /// </para>
    /// </summary>
    public static float ResolveNaturalWidth(SplitButtonSpec spec)
    {
        var measuredWidth = ResolveMeasuredContentWidth(spec);
        if (HasMainIcon(spec))
            return measuredWidth;

        var menuWidth = ResolveNaturalMenuWidth(spec);
        return MathF.Max(measuredWidth, MetricsScope.UnitWidth * 2f + menuWidth);
    }

    private static float ResolveMeasuredContentWidth(SplitButtonSpec spec)
    {
        var paddingX = MetricsScope.ScalePadding(TextPaddingX);
        var iconSlotWidth = ResolveIconSlotWidth();
        var hasGameIcon = spec.MainGameIconId != 0;
        var (mainIconText, _) = SlapIcon.Resolve(spec.MainIcon);
        var hasFaIcon = !string.IsNullOrEmpty(mainIconText);
        var hasIcon = hasGameIcon || hasFaIcon;
        var hasLabel = !string.IsNullOrEmpty(spec.MainLabel);
        var leading = IconTextComponent.ResolveLeadingLayout(
            hasGameIcon,
            hasFaIcon,
            hasLabel,
            IconLeftPad,
            IconGap,
            iconSlotWidth);

        var contentWidth = leading.TotalWidth;

        if (hasLabel)
        {
            using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold))
                contentWidth += ImGui.CalcTextSize(spec.MainLabel).X;
        }

        var mainWidth = hasIcon
            ? contentWidth + paddingX
            : contentWidth + paddingX * 2f;

        return mainWidth + ResolveNaturalMenuWidth(spec);
    }

    private static bool HasMainIcon(SplitButtonSpec spec)
    {
        var (iconText, _) = SlapIcon.Resolve(spec.MainIcon);
        return !string.IsNullOrEmpty(iconText) || spec.MainGameIconId != 0;
    }

    private static float ResolveNaturalMenuWidth(SplitButtonSpec spec)
    {
        var h = MetricsScope.UnitHeight;
        return spec.MenuWidthRatio > 0f ? h * spec.MenuWidthRatio : h;
    }

    /// <summary>
    /// Measure the preferred and structural-minimum widths for responsive layout.
    /// </summary>
    public static ResponsiveWidthRange MeasureResponsiveWidth(SplitButtonSpec spec)
    {
        var contentWidth = spec.Size.Kind == ButtonSizeKind.Rect
            ? ResolveMeasuredContentWidth(spec)
            : ResolveNaturalWidth(spec);
        var preferredBaseline = spec.Size.Kind == ButtonSizeKind.Rect
            ? MetricsScope.UnitWidth * spec.Size.WidthUnits
            : 0f;
        return ResponsiveWidthRange.Create(
            contentWidth,
            preferredBaseline,
            ResolveMinWidth(spec));
    }

    private static Vector2 ResolveSize(SplitButtonSpec spec)
    {
        if (spec.Size.Kind == ButtonSizeKind.Auto)
            return new Vector2(ResolveNaturalWidth(spec), MetricsScope.UnitHeight);

        var w = MetricsScope.UnitWidth;
        var h = MetricsScope.UnitHeight;
        return spec.Size.Kind switch
        {
            ButtonSizeKind.Rect => new Vector2(w * spec.Size.WidthUnits, h * spec.Size.HeightUnits),
            _ => new Vector2(h * spec.Size.WidthUnits, h * spec.Size.HeightUnits) // Square
        };
    }

    private static float ResolveMenuWidth(SplitButtonSpec spec, Vector2 size)
    {
        if (spec.MenuWidthRatio > 0f)
            return size.Y * spec.MenuWidthRatio;
        return MathF.Min(size.Y, size.X);
    }

    private static void DrawMainContent(
        ImDrawListPtr drawList,
        SplitButtonSpec spec,
        Vector2 min,
        Vector2 max,
        Vector4 textColor,
        Vector4 fadeBackground,
        EdgeFadeSpec? edgeFade,
        ControlVisualMode visualMode)
    {
        var paddingX = MetricsScope.ScalePadding(TextPaddingX);
        var iconSlotWidth = ResolveIconSlotWidth();
        var colorU32 = ImGui.ColorConvertFloat4ToU32(textColor);
        var hasGameIcon = spec.MainGameIconId != 0;
        var (mainIconText, mainIconFont) = SlapIcon.Resolve(spec.MainIcon);
        var hasFaIcon = !string.IsNullOrEmpty(mainIconText);
        var hasIcon = hasGameIcon || hasFaIcon;
        var hasLabel = !string.IsNullOrEmpty(spec.MainLabel);
        var leading = IconTextComponent.ResolveLeadingLayout(
            hasGameIcon,
            hasFaIcon,
            hasLabel,
            IconLeftPad,
            IconGap,
            iconSlotWidth);

        // GameIcon splits the FA alignment spacing around its slot.
        var x = hasIcon ? min.X + leading.LeftPadding
            : min.X + paddingX;

        // Icon — centered within a fixed slot (UnitHeight)
        if (hasGameIcon)
        {
            var (iconMin, iconMax) = GameIconComponent.ResolveControlIconRect(
                new Vector2(x, min.Y),
                new Vector2(x + iconSlotWidth, max.Y)
            );
            GameIconComponent.Draw(
                drawList, spec.MainGameIconId, iconMin, iconMax,
                false,
                visualMode: visualMode);
            x += leading.IconSlotWidth + leading.TextGap;
        }
        else if (hasFaIcon)
        {
            if (mainIconFont.HasValue)
                ImGui.PushFont(mainIconFont.Value);
            try
            {
                var iconSize = ImGui.CalcTextSize(mainIconText);
                var iconY = min.Y + MathF.Max(0f, ((max.Y - min.Y) - iconSize.Y) * 0.5f);
                var iconX = x + SlapIcon.ResolveHorizontalCenteringOffset(
                    mainIconFont,
                    mainIconText!,
                    iconSlotWidth);
                drawList.AddText(new Vector2(iconX, iconY), colorU32, mainIconText);
                x += leading.IconSlotWidth + leading.TextGap;
            }
            finally
            {
                if (mainIconFont.HasValue)
                    ImGui.PopFont();
            }
        }

        // Label
        if (string.IsNullOrEmpty(spec.MainLabel))
            return;

        var textMax = max.X - paddingX;
        if (x >= textMax)
            return;

        using (TypographyScope.PushCurrent(SlapFontSize.Regular, SlapFontWeight.Bold))
        {
            var textSize = ImGui.CalcTextSize(spec.MainLabel);
            var textY = min.Y + MathF.Max(0f, ((max.Y - min.Y) - textSize.Y) * 0.5f);
            drawList.PushClipRect(new Vector2(x, min.Y), new Vector2(textMax, max.Y), true);
            drawList.AddText(new Vector2(x, textY), colorU32, spec.MainLabel);
            drawList.PopClipRect();

            if (textSize.X > textMax - x)
            {
                var fadeSpec = edgeFade ?? new EdgeFadeSpec(Background: fadeBackground, VerticalInset: MetricsScope.BorderThickness);
                if (fadeSpec.IsEnabled)
                    EdgeFade.DrawRight(drawList, new Vector2(x, min.Y), textMax - x, max.Y - min.Y, fadeSpec);
            }
        }
    }

    private static void DrawMenuArrow(
        ImDrawListPtr drawList, Vector2 min, Vector2 max, Vector4 textColor,
        FontAwesomeIcon? icon = null)
    {
        var (iconText, iconFont) = SlapIcon.Resolve(icon);
        var arrow = iconText ?? "\u25BE";
        var shouldPushFont = iconFont.HasValue;
        if (shouldPushFont)
            ImGui.PushFont(iconFont!.Value);
        try
        {
        var arrowSize = ImGui.CalcTextSize(arrow);
        var pos = new Vector2(
            min.X + ((max.X - min.X) - arrowSize.X) * 0.5f,
            min.Y + ((max.Y - min.Y) - arrowSize.Y) * 0.5f);
        drawList.AddText(pos, ImGui.ColorConvertFloat4ToU32(textColor), arrow);
        }
        finally
        {
            if (shouldPushFont)
                ImGui.PopFont();
        }
    }

    private static void DrawMenuPopup(
        string popupId,
        SplitButtonSpec spec,
        Vector2 buttonMin,
        Vector2 buttonMax,
        ref int clickedIndex)
    {
        if (spec.Options == null || spec.Options.Count == 0)
            return;

        ImGui.SetNextWindowPos(new Vector2(buttonMin.X, buttonMax.Y));

	        var measureItems = spec.Options
	            .Select(option => new PopupMeasureItem(
	                option.Label ?? string.Empty,
	                HasLeadingIcon: option.GameIconId != 0 || option.Icon.HasValue,
	                HasTrailingIcon: false,
	                IsSelected: option.Selected))
	            .ToList();
	        var buttonWidth = buttonMax.X - buttonMin.X;
        var minWidthUnits = MathF.Max(buttonWidth / MetricsScope.UnitWidth, PopupPanelComponent.DefaultMinWidthUnits);
        using var popup = PopupPanelComponent.Begin(new PopupPanelSpec(
	            popupId, spec.Variant,
	            MeasureItems: measureItems,
	            MinWidthUnits: minWidthUnits));
        if (!popup.IsOpen)
            return;

        for (var i = 0; i < spec.Options.Count; i++)
        {
            var option = spec.Options[i];
            var state = ControlState.None;
            if (option.Selected)
                state |= ControlState.Selected;
            if (option.Disabled)
                state |= ControlState.Disabled;

            var result = popup.Item(new PopupItemSpec(
                $"option{i}",
                option.Label,
                Icon: option.Icon,
                spec.Variant,
                state,
                option.Tooltip,
                GameIconId: option.GameIconId));

            if (result.Clicked)
            {
                clickedIndex = i;
                ImGui.CloseCurrentPopup();
            }

            if (option.Selected)
                ImGui.SetItemDefaultFocus();
        }
    }
}
