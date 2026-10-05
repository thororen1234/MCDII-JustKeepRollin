using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;
using UE.Minimap;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// The Mods page's own controls, for settings the game has no row for here (keybinds, text inputs, colours). They're
/// drawn as rounded boxes, which need no textures, in the game's font: the engine's default widget images and font
/// aren't always in the game.
/// </summary>
public static class Look
{
    public static FLinearColor Gold(float alpha) => Ui.Color(0.95f, 0.7f, 0.2f, alpha);
    public static FLinearColor White() => Ui.Color(0.95f, 0.95f, 0.95f, 1);
    public static FLinearColor Grey() => Ui.Color(0.62f, 0.62f, 0.64f, 1);

    /// <summary>A filled rounded box with an outline.</summary>
    public static FSlateBrush Rounded(FLinearColor fill, FLinearColor outline, float outlineWidth, float radius) => new FSlateBrush
    {
        DrawAs = ESlateBrushDrawType.RoundedBox,
        TintColor = Ui.SlateColor(fill),
        OutlineSettings = new FSlateBrushOutlineSettings
        {
            CornerRadii = new FVector4 { X = radius, Y = radius, Z = radius, W = radius },
            Color = Ui.SlateColor(outline),
            Width = outlineWidth,
            RoundingType = ESlateBrushRoundingType.FixedRadius,
        },
    };

    /// <summary>A button's style: a list entry (wide) or a control; picked shows it's the one chosen.</summary>
    public static FButtonStyle Button(bool picked, bool wide)
    {
        var idle = Rounded(Ui.Color(0.08f, 0.08f, 0.1f, 0.9f), Ui.Color(1, 1, 1, 0.16f), 1, 5);
        var hover = Rounded(Ui.Color(0.16f, 0.12f, 0.05f, 0.95f), Gold(0.85f), 1.5f, 5);
        var chosen = Rounded(Ui.Color(0.24f, 0.17f, 0.05f, 0.95f), Gold(1), 2, 5);
        var padding = wide ? new FMargin { Left = 14, Top = 8, Right = 14, Bottom = 8 } : new FMargin { Left = 12, Top = 5, Right = 12, Bottom = 5 };
        return new FButtonStyle
        {
            Normal = picked ? chosen : idle,
            Hovered = picked ? chosen : hover,
            Pressed = chosen,
            Disabled = idle,
            NormalPadding = padding,
            PressedPadding = padding,
        };
    }

    static FTextBlockStyle TextStyle(int size)
    {
        var font = UMinimapHelpersLibrary.GetDefaultFont();
        font.Size = size;
        return new FTextBlockStyle { Font = font, ColorAndOpacity = Ui.SlateColor(White()), ShadowColorAndOpacity = Ui.Color(0, 0, 0, 0) };
    }

    /// <summary>A text box (a text input setting, or a colour's hex box).</summary>
    public static PageText? TextBox(UWidgetTree tree, ModsPage page, int index, bool hex, string value, string hint)
    {
        var box = UGameplayStatics.SpawnObject(Unreal.ClassOf<PageText>(), tree) as PageText;
        if (box == null) return null;
        box.Setup(page, index, hex);
        var idle = Rounded(Ui.Color(0.05f, 0.05f, 0.07f, 0.95f), Ui.Color(1, 1, 1, 0.2f), 1, 4);
        var focused = Rounded(Ui.Color(0.07f, 0.07f, 0.09f, 1), Gold(0.9f), 1.5f, 4);
        box.WidgetStyle = new FEditableTextBoxStyle
        {
            BackgroundImageNormal = idle,
            BackgroundImageHovered = focused,
            BackgroundImageFocused = focused,
            BackgroundImageReadOnly = idle,
            Padding = new FMargin { Left = 8, Top = 5, Right = 8, Bottom = 5 },
            TextStyle = TextStyle(15),
            ForegroundColor = Ui.SlateColor(White()),
            BackgroundColor = Ui.SlateColor(Ui.Color(1, 1, 1, 1)),
            ReadOnlyForegroundColor = Ui.SlateColor(Grey()),
            FocusedForegroundColor = Ui.SlateColor(White()),
        };
        box.SetText(value);
        box.SetHintText(hint);
        return box;
    }

    /// <summary>A keybind's key: shows it, and takes the next key pressed after a click.</summary>
    public static PageKey? KeySelector(UWidgetTree tree, ModsPage page, int index, bool secondary, FKey key, FButtonStyle style)
    {
        var selector = UGameplayStatics.SpawnObject(Unreal.ClassOf<PageKey>(), tree) as PageKey;
        if (selector == null) return null;
        selector.WidgetStyle = style;
        selector.TextStyle = TextStyle(15);
        selector.SetNoKeySpecifiedText("None");
        selector.SetKeySelectionText("Press a key...");
        selector.SetAllowGamepadKeys(true);
        selector.SetSelectedKey(new FInputChord { Key = key });
        // After the key is set: setting it doesn't count as the player picking one.
        selector.Setup(page, index, secondary);
        return selector;
    }

    /// <summary>A colour to pick: the colour itself, outlined in gold when it's the one set.</summary>
    public static PageButton? Swatch(UWidgetTree tree, ModsPage page, int index, string hex, bool picked)
    {
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<PageButton>(), tree) as PageButton;
        var size = UGameplayStatics.SpawnObject(Unreal.ClassOf<USpacer>(), tree) as USpacer;
        if (button == null || size == null) return null;
        button.Setup(page, index, "colour:" + hex);
        var colour = ModSettings.ToColour(hex);
        var outline = picked ? Gold(1) : Ui.Color(1, 1, 1, 0.25f);
        var brush = Rounded(colour, outline, picked ? 2.5f : 1, 4);
        var hover = Rounded(colour, Gold(0.8f), 2, 4);
        button.SetStyle(new FButtonStyle { Normal = brush, Hovered = hover, Pressed = hover, Disabled = brush, NormalPadding = new FMargin(), PressedPadding = new FMargin() });
        size.SetSize(new FVector2D { X = 26, Y = 20 });
        button.AddChild(size);
        return button;
    }

    /// <summary>The colours offered when a colour setting has no options of its own: Minecraft's dyes.</summary>
    public static List<string> Palette() => new List<string>
    {
        "#F9FFFE", "#9D9D97", "#474F52", "#1D1D21", "#835432", "#B02E26", "#F9801D", "#FED83D",
        "#80C71F", "#5E7C16", "#169C9C", "#3AB3DA", "#3C44AA", "#8932B8", "#C74EBD", "#F38BAA",
    };
}
