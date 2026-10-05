using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.GameSettings;
using UE.SlateCore;
using UE.SWSettings;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// The game's settings look, from the game's own assets: the text styles and the materials its settings rows, toggles,
/// buttons and boxes are drawn with (the settings screen's widgets use the same ones). Only looks are used. The Mods
/// page never makes one of the game's setting rows or buttons: their own code runs when they're shown and expects a game
/// setting behind them, and without one the game crashes. What isn't in the game (after an update) is copied from the
/// settings screen on screen instead, or else drawn by the page itself.
/// </summary>
public class GameLook : UObject
{
    const string TextStyles = "/OreUI/UI/Typography/TextStyles/";
    const string SettingsArt = "/OreUI/UI/Button/Role/Settings/";
    const string ListEntryArt = "/OreUI/UI/Button/Role/ListEntry/";

    // The game's text styles, by role: "title" (a mod's name on its page), "section" (INSTALLED MODS, a settings
    // heading, the details panel's title), "label" (a row's name), "value" (a row's ON, 50%), "body" (descriptions),
    // "button" (a button's text).
    List<string> styleRoles = new();
    List<TSubclassOf<UCommonTextStyle>> styles = new();
    // Loaded materials: held here, so the garbage collector keeps them while the page uses them.
    List<UObject> loaded = new();
    // The game's settings rows' animations (hover, a toggle switching), by "widget:animation", loaded when first used.
    List<string> animationNames = new();
    List<UWidgetAnimation> animations = new();
    public FSlateBrush RowBrush;
    public FSlateBrush RowHighlight;
    public bool HasRowArt;
    public FSlateBrush BoxBrush;
    public bool HasBoxArt;
    public FSlateBrush ToggleOnBrush;
    public FSlateBrush ToggleOffBrush;
    public FSlateBrush ThumbBrush;
    public bool HasToggleArt;
    public FSlateBrush ButtonNormal;
    public FSlateBrush ButtonHovered;
    public FSlateBrush ButtonPressed;
    public bool HasButtonArt;

    // Copied from the settings screen on screen: text for when the styles aren't there, and the slider (the game's has
    // no plain style asset).
    public UTextBlock? Heading;
    public UTextBlock? Label;
    public UTextBlock? Value;
    public FSliderStyle Slider;
    public FLinearColor SliderBar;
    public FLinearColor SliderHandle;
    public bool HasSlider;

    // Nothing from the game: the page in the loader's own look (making it in the game's look crashed the game once).
    bool plain;

    /// <summary>
    /// The look of a settings screen (plain: nothing from the game): keep it in a field. Until it's in a field, the
    /// garbage collector doesn't see it, nor what it loads. It belongs to owner, not the screen: the page keeps using it
    /// after the screen is gone.
    /// </summary>
    public static GameLook? From(UObject owner, UUserWidget screen, bool plain)
    {
        var look = UGameplayStatics.SpawnObject(Unreal.ClassOf<GameLook>(), owner) as GameLook;
        if (look == null) return null;
        look.plain = plain;
        if (plain) return look;
        var details = GameUI.Find(screen, "Details_Settings") as UUserWidget;
        look.Heading = GameUI.Find(details, "Text_SettingName") as UTextBlock;
        return look;
    }

    /// <summary>Loads the game's styles and art. Returns what it found, for the log.</summary>
    public string Load()
    {
        if (plain) return "Settings look: plain";
        AddStyle("title", "Style_Header3_Text");
        AddStyle("section", "Style_SectionHeader1_Text");
        AddStyle("label", "Style_NavBarHeader_Text");
        AddStyle("value", "Style_Header5_Text");
        AddStyle("body", "Style_Body_Text");
        AddStyle("button", "Style_Button_Text");
        var row = Brush(SettingsArt, "MI_UI_Settings_EntryBG", out RowBrush);
        var highlight = Brush(SettingsArt, "MI_UI_Settings_EntryBG_Highlighted", out RowHighlight);
        HasRowArt = row && highlight;
        HasBoxArt = Brush(SettingsArt, "MI_UI_Settings_EntryBG_Internal", out BoxBrush);
        var on = Brush(SettingsArt, "MI_UI_Settings_On", out ToggleOnBrush);
        var off = Brush(SettingsArt, "MI_UI_Settings_Off", out ToggleOffBrush);
        var thumb = Brush(SettingsArt, "MI_UI_SliderThumb", out ThumbBrush);
        HasToggleArt = on && off && thumb;
        var normal = Brush(ListEntryArt, "MI_ListEntry_Background-Normal-Base", out ButtonNormal);
        var hovered = Brush(ListEntryArt, "MI_ListEntry_Background-Normal-Hovered", out ButtonHovered);
        var pressed = Brush(ListEntryArt, "MI_ListEntry_Background-Normal-Pressed", out ButtonPressed);
        HasButtonArt = normal && hovered && pressed;
        return $"Settings look: {styles.Count} of 6 text styles, row {HasRowArt}, box {HasBoxArt}, toggle {HasToggleArt}, button {HasButtonArt}";
    }

    void AddStyle(string role, string name)
    {
        var style = Unreal.LoadClass<UCommonTextStyle>($"{TextStyles}{name}.{name}_C");
        if (style == null) return;
        styleRoles.Add(role);
        styles.Add(style);
    }

    /// <summary>
    /// One of a game settings row's animations (OnHover, ToggleOn), played on that row to show it like the game does: they
    /// belong to the row's class, so any row of the class can play them. Null when it isn't in the game.
    /// </summary>
    public UWidgetAnimation? Animation(string widget, string name)
    {
        var key = widget + ":" + name;
        int at = animationNames.IndexOf(key);
        if (at >= 0) return animations[at];
        var path = UKismetSystemLibrary.MakeSoftObjectPath($"{GameRow.Editors}{widget}.{widget}_C:{name}_INST");
        var animation = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(path)) as UWidgetAnimation;
        if (animation == null) return null;
        animationNames.Add(key);
        animations.Add(animation);
        return animation;
    }

    /// <summary>Whether the page is drawn without anything from the game.</summary>
    public bool Plain => plain;

    /// <summary>A brush drawing one of the game's UI materials, if it's in the game.</summary>
    bool Brush(string folder, string name, out FSlateBrush brush)
    {
        brush = new FSlateBrush();
        var path = UKismetSystemLibrary.MakeSoftObjectPath($"{folder}{name}.{name}");
        var asset = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(path));
        if (asset is not UMaterialInterface material) return false;
        loaded.Add(material);
        brush = UWidgetBlueprintLibrary.MakeBrushFromMaterial(material, 32, 32);
        return true;
    }

    /// <summary>
    /// Copies the slider, and text for styles that are missing, from the rows the settings list shows now (each tab shows
    /// other kinds). Returns what it has, for the log.
    /// </summary>
    public string Capture(UGameSettingListView? list)
    {
        if (plain || list == null) return "";
        foreach (var entry in list.GetDisplayedEntryWidgets())
        {
            if (entry is UGameSettingListEntry_Setting row && Label == null) Label = row.Text_SettingName;
            if (entry is UGameSettingListEntrySetting_Bool toggle && Value == null) Value = toggle.StateText;
            if (entry is UGameSettingListEntrySetting_Scalar scalar)
            {
                if (Value == null) Value = scalar.Text_SettingValue;
                var slider = scalar.Slider_SettingValue;
                if (!HasSlider && slider != null)
                {
                    Slider = slider.WidgetStyle;
                    SliderBar = slider.SliderBarColor;
                    SliderHandle = slider.SliderHandleColor;
                    HasSlider = true;
                }
            }
        }
        return $"Settings look copied: label {Label != null}, value {Value != null}, slider {HasSlider}";
    }

    /// <summary>Text in one of the settings' text looks (see the roles above).</summary>
    public UTextBlock? Text(UObject? outer, string text, string role)
    {
        int at = styleRoles.IndexOf(role);
        if (at >= 0)
        {
            var styled = UGameplayStatics.SpawnObject(Unreal.ClassOf<UCommonTextBlock>(), outer) as UCommonTextBlock;
            if (styled != null)
            {
                styled.SetStyle(styles[at]);
                styled.SetText(text);
                return styled;
            }
        }
        var source = role == "title" || role == "section" ? Heading : (role == "value" && Value != null ? Value : Label);
        if (source == null) return Ui.Text(outer, text, role == "title" ? 40 : (role == "section" ? 28 : 18));
        var label = UGameplayStatics.SpawnObject(Unreal.ClassOf<UTextBlock>(), outer) as UTextBlock;
        if (label == null) return null;
        label.SetText(text);
        label.SetFont(source.Font);
        label.SetColorAndOpacity(source.ColorAndOpacity);
        label.SetShadowOffset(source.ShadowOffset);
        label.SetShadowColorAndOpacity(source.ShadowColorAndOpacity);
        label.SetTextTransformPolicy(source.TextTransformPolicy);
        return label;
    }

    static FMargin RowPadding() => new FMargin { Left = 24, Top = 14, Right = 24, Bottom = 14 };

    /// <summary>A row that isn't a button: the content on the game's row background.</summary>
    public UBorder? Row(UObject? outer, UWidget? content)
    {
        var row = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), outer) as UBorder;
        if (row == null) return null;
        if (HasRowArt) row.SetBrush(RowBrush);
        else row.SetBrushColor(Ui.Color(0, 0, 0, 0.25f));
        row.SetPadding(RowPadding());
        if (content != null) row.AddChild(content);
        return row;
    }

    /// <summary>A row that is a button: the game's row background, framed in white while hovered or focused.</summary>
    public FButtonStyle RowButton()
    {
        var normal = HasRowArt ? RowBrush : Look.Rounded(Ui.Color(0, 0, 0, 0.25f), Ui.Color(1, 1, 1, 0), 0, 2);
        var lit = HasRowArt ? RowHighlight : Look.Rounded(Ui.Color(1, 1, 1, 0.06f), Ui.Color(1, 1, 1, 0.9f), 2, 2);
        return new FButtonStyle { Normal = normal, Hovered = lit, Pressed = lit, Disabled = normal, NormalPadding = RowPadding(), PressedPadding = RowPadding() };
    }

    /// <summary>A box inside a row, like the game's dropdowns and slider: around a key, a text input.</summary>
    public FButtonStyle Box()
    {
        if (!HasBoxArt) return Look.Button(false, false);
        var padding = new FMargin { Left = 12, Top = 6, Right = 12, Bottom = 6 };
        var lit = HasRowArt ? RowHighlight : BoxBrush;
        return new FButtonStyle { Normal = BoxBrush, Hovered = lit, Pressed = lit, Disabled = BoxBrush, NormalPadding = padding, PressedPadding = padding };
    }

    /// <summary>
    /// A toggle's look, showing only (the row takes the click): ON/OFF and the game's switch. Drawn by the page when the
    /// game's switch art isn't there: green with its knob at the right when on, grey with it at the left when off.
    /// </summary>
    public UWidget? Switch(UObject? outer, bool on)
    {
        var row = Ui.Row(outer);
        var state = Text(outer, on ? "ON" : "OFF", "value");
        var track = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), outer) as UBorder;
        var knob = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), outer) as UBorder;
        var trackSize = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), outer) as USizeBox;
        var knobSize = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), outer) as USizeBox;
        if (row == null || track == null || knob == null || trackSize == null || knobSize == null) return state;
        row.AddChildToHorizontalBox(state)?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        if (HasToggleArt)
        {
            track.SetBrush(on ? ToggleOnBrush : ToggleOffBrush);
            knob.SetBrush(ThumbBrush);
        }
        else
        {
            track.SetBrush(Look.Rounded(on ? Ui.Color(0.2f, 0.72f, 0.33f, 1) : Ui.Color(0.3f, 0.3f, 0.33f, 1), Ui.Color(0, 0, 0, 0.5f), 2, 3));
            knob.SetBrush(Look.Rounded(Ui.Color(0.92f, 0.92f, 0.92f, 1), Ui.Color(0, 0, 0, 0.35f), 1, 2));
        }
        track.SetPadding(new FMargin { Left = 3, Top = 3, Right = 3, Bottom = 3 });
        track.SetHorizontalAlignment(on ? EHorizontalAlignment.HAlign_Right : EHorizontalAlignment.HAlign_Left);
        track.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
        knobSize.SetWidthOverride(HasToggleArt ? 14 : 22);
        knobSize.AddChild(knob);
        track.AddChild(knobSize);
        trackSize.SetWidthOverride(60);
        trackSize.SetHeightOverride(32);
        trackSize.AddChild(track);
        trackSize.SetVisibility(ESlateVisibility.HitTestInvisible);
        row.AddChildToHorizontalBox(trackSize)?.SetPadding(new FMargin { Left = 16 });
        return row;
    }

    /// <summary>A button's look with text, showing only (the row takes the click), like the game's list buttons.</summary>
    public UWidget? ButtonLook(UObject? outer, string text)
    {
        var box = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), outer) as UBorder;
        var label = Text(outer, text, "button");
        if (box == null || label == null) return label;
        if (HasButtonArt) box.SetBrush(ButtonNormal);
        else box.SetBrush(Look.Button(false, false).Normal);
        box.SetPadding(new FMargin { Left = 24, Top = 8, Right = 24, Bottom = 8 });
        box.AddChild(label);
        box.SetVisibility(ESlateVisibility.HitTestInvisible);
        return box;
    }

    /// <summary>A button of its own (not a whole row): the game's list button look.</summary>
    public FButtonStyle Button()
    {
        if (!HasButtonArt) return Look.Button(false, false);
        var padding = new FMargin { Left = 24, Top = 8, Right = 24, Bottom = 8 };
        return new FButtonStyle { Normal = ButtonNormal, Hovered = ButtonHovered, Pressed = ButtonPressed, Disabled = ButtonNormal, NormalPadding = padding, PressedPadding = padding };
    }

    /// <summary>Gives a slider the game's slider style, once it has been copied from the settings screen.</summary>
    public void StyleSlider(USlider slider)
    {
        if (!HasSlider) return;
        slider.WidgetStyle = Slider;
        slider.SetSliderBarColor(SliderBar);
        slider.SetSliderHandleColor(SliderHandle);
    }
}
