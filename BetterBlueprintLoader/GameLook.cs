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
/// The game's settings look, copied from the settings screen on screen: its text, its rows' background, its toggle and
/// button styles and its slider style. Only looks are copied. The Mods page never makes one of the game's setting rows
/// or buttons: their own code runs when they're shown and expects a game setting behind them, and without one the game
/// crashes. Whatever wasn't on screen to copy falls back to the page's own drawing.
/// </summary>
public class GameLook : UObject
{
    // Text to copy: the details panel's title (headings), a row's label, a row's value (a toggle's ON, a slider's 50%).
    public UTextBlock? Heading;
    public UTextBlock? Label;
    public UTextBlock? Value;
    public FSlateBrush RowBrush;
    public bool HasRowBrush;
    public FButtonStyle ToggleOn;
    public FButtonStyle ToggleOff;
    public bool HasToggle;
    public FButtonStyle Button;
    public FSlateFontInfo ButtonFont;
    public FLinearColor ButtonTextColour;
    public bool HasButton;
    public FSliderStyle Slider;
    public FLinearColor SliderBar;
    public FLinearColor SliderHandle;
    public bool HasSlider;

    // Nothing copied: the page in the loader's own look (copying crashed the game once).
    bool plain;

    /// <summary>
    /// The look of a settings screen (plain: copy nothing), not copied yet: keep it in a field, then
    /// <see cref="Capture"/>. Until it's in a field, the garbage collector doesn't see it. It belongs to owner, not the
    /// screen: the page keeps using it after the screen is gone.
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

    /// <summary>
    /// Copies what's missing from the rows the settings list shows now (each tab shows other kinds). Returns what it has,
    /// for the log.
    /// </summary>
    public string Capture(UGameSettingListView? list)
    {
        if (plain) return "Settings look: plain";
        if (list == null) return "Settings look: no settings list";
        foreach (var entry in list.GetDisplayedEntryWidgets())
        {
            if (entry is UGameSettingListEntry_Setting row)
            {
                if (Label == null) Label = row.Text_SettingName;
                if (!HasRowBrush && GameUI.Find(row.Background, "Background") is UImage background)
                {
                    RowBrush = background.Brush;
                    HasRowBrush = true;
                }
            }
            if (entry is UGameSettingListEntrySetting_Bool toggle)
            {
                if (Value == null) Value = toggle.StateText;
                var style = toggle.ToggleButton?.GetStyle();
                if (!HasToggle && style != null)
                {
                    ToggleOn = ButtonStyle(style, true);
                    ToggleOff = ButtonStyle(style, false);
                    HasToggle = true;
                }
            }
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
            if (entry is UGameSettingListEntrySetting_Navigation navigation)
            {
                var style = navigation.Button_Navigate?.GetStyle();
                if (!HasButton && style != null)
                {
                    Button = ButtonStyle(style, false);
                    var text = style.GetNormalTextStyle();
                    if (text != null)
                    {
                        text.GetFont(out var font);
                        text.GetColor(out var colour);
                        ButtonFont = font;
                        ButtonTextColour = colour;
                    }
                    HasButton = true;
                }
            }
        }
        return $"Settings look: label {Label != null}, heading {Heading != null}, value {Value != null}, row {HasRowBrush}, toggle {HasToggle}, button {HasButton}, slider {HasSlider}";
    }

    /// <summary>A plain button style with a game button style's brushes (selected: its selected look, e.g. a toggle's on).</summary>
    static FButtonStyle ButtonStyle(UCommonButtonStyle style, bool selected)
    {
        FSlateBrush normal;
        FSlateBrush hovered;
        FSlateBrush pressed;
        if (selected)
        {
            style.GetSelectedBaseBrush(out normal);
            style.GetSelectedHoveredBrush(out hovered);
            style.GetSelectedPressedBrush(out pressed);
        }
        else
        {
            style.GetNormalBaseBrush(out normal);
            style.GetNormalHoveredBrush(out hovered);
            style.GetNormalPressedBrush(out pressed);
        }
        style.GetDisabledBrush(out var disabled);
        style.GetButtonPadding(out var padding);
        return new FButtonStyle { Normal = normal, Hovered = hovered, Pressed = pressed, Disabled = disabled, NormalPadding = padding, PressedPadding = padding };
    }

    /// <summary>Text in one of the settings' text looks: "heading", "label" or "value".</summary>
    public UTextBlock? Text(UObject? outer, string text, string role)
    {
        var source = role == "heading" ? Heading : (role == "value" && Value != null ? Value : Label);
        if (source == null) return Ui.Text(outer, text, role == "heading" ? 36 : 18);
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

    /// <summary>A row: the content on the game's row background.</summary>
    public UBorder? Row(UObject? outer, UWidget? content)
    {
        var row = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), outer) as UBorder;
        if (row == null) return null;
        if (HasRowBrush) row.SetBrush(RowBrush);
        else row.SetBrushColor(Ui.Color(0, 0, 0, 0.25f));
        row.SetPadding(new FMargin { Left = 24, Top = 12, Right = 24, Bottom = 12 });
        if (content != null) row.AddChild(content);
        return row;
    }

    /// <summary>
    /// A toggle's look, showing only (the row takes the click): ON/OFF and a switch like the game's, green with its knob at
    /// the right when on, grey with it at the left when off. Drawn here: the game's toggle images don't draw when copied.
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
        var fill = on ? Ui.Color(0.2f, 0.72f, 0.33f, 1) : Ui.Color(0.3f, 0.3f, 0.33f, 1);
        track.SetBrush(Look.Rounded(fill, Ui.Color(0, 0, 0, 0.5f), 2, 3));
        track.SetPadding(new FMargin { Left = 3, Top = 3, Right = 3, Bottom = 3 });
        track.SetHorizontalAlignment(on ? EHorizontalAlignment.HAlign_Right : EHorizontalAlignment.HAlign_Left);
        track.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
        knob.SetBrush(Look.Rounded(Ui.Color(0.92f, 0.92f, 0.92f, 1), Ui.Color(0, 0, 0, 0.35f), 1, 2));
        knobSize.SetWidthOverride(22);
        knobSize.AddChild(knob);
        track.AddChild(knobSize);
        trackSize.SetWidthOverride(60);
        trackSize.SetHeightOverride(30);
        trackSize.AddChild(track);
        trackSize.SetVisibility(ESlateVisibility.HitTestInvisible);
        row.AddChildToHorizontalBox(trackSize)?.SetPadding(new FMargin { Left = 16 });
        return row;
    }

    /// <summary>A button's look with text, showing only (the row takes the click).</summary>
    public UWidget? ButtonLook(UObject? outer, string text)
    {
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<UButton>(), outer) as UButton;
        var label = Text(outer, text, "label");
        if (button == null || label == null) return label;
        if (HasButton)
        {
            button.SetStyle(Button);
            if (ButtonFont.FontObject != null) label.SetFont(ButtonFont);
            label.SetColorAndOpacity(Ui.SlateColor(ButtonTextColour));
        }
        else button.SetStyle(Look.Button(false, false));
        button.AddChild(label);
        button.SetVisibility(ESlateVisibility.HitTestInvisible);
        return button;
    }

    /// <summary>Gives a slider the game's slider style.</summary>
    public void StyleSlider(USlider slider)
    {
        if (!HasSlider) return;
        slider.WidgetStyle = Slider;
        slider.SetSliderBarColor(SliderBar);
        slider.SetSliderHandleColor(SliderHandle);
    }
}
