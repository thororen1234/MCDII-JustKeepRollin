using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;
using UE.SlateCore;
using UE.SpicewoodUI;
using UE.SWSettings;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// A row of the Mods page made of one of the game's own settings rows (a header, a toggle, a slider, a dropdown, keys, or
/// a row with a button), so it looks exactly like the settings around it. The game's row only shows: it has no game
/// setting behind it, and its own controls would use one, so it never gets the mouse or the focus. This widget lies
/// over it and takes the clicks, the keys and the controller, and shows them on the game's row (its hover animation, its
/// switch, its slider, its keys).
/// </summary>
public class GameRow : UUserWidget
{
    public const string Editors = "/SpicewoodSettings/Spicewood/UI/SettingsScreen/Editors/";
    public const string Header = "W_SettingsEntry_Header";
    public const string Button = "W_SettingsEntry_SubCollection";
    public const string Toggle = "W_SettingsEntry_Bool";
    public const string Slider = "W_SettingsEntry_Scalar";
    public const string Dropdown = "W_SettingsEntry_Dropdown";
    public const string Keys = "W_SettingsListEntry_DualMappableInputs";
    const string RowBackground = "W_SettingsEntry_Background";

    ModsPage? page;
    GameLook? look;
    // The game's row, its kind (one of the class names above), and its background (the part that lights up).
    UUserWidget? entry;
    UUserWidget? background;
    string kind;
    public int Index;
    public string Action;
    bool lit;
    // A slider's range and value.
    double min;
    double max;
    double step;
    double value;
    bool percentage;
    bool dragging;
    // Keys: waiting for the key to set, and for which of the two.
    bool capturing;
    bool captureSecondary;
    FKey primaryKey;
    FKey secondaryKey;
    // What the row shows that the game's widgets set themselves when they're made (a button's label, the keys): shown
    // again once they are.
    string buttonText;
    bool hasButton;
    bool hasKeys;

    /// <summary>
    /// A row of one of the game's kinds, for a setting (index) or the page's own (-1), and what clicking it does. Null
    /// when the game's row isn't there (the page then makes its own). Keep it in a field or add it to the page at once.
    /// </summary>
    public static GameRow? Make(ModsPage page, GameLook look, string kind, int index, string action)
    {
        var row = UWidgetBlueprintLibrary.Create(page, Unreal.ClassOf<GameRow>(), World.PlayerController(page)) as GameRow;
        if (row == null) return null;
        row.page = page;
        row.look = look;
        row.kind = kind;
        row.Index = index;
        row.Action = action;
        // Headers are only text: the controller passes them by.
        row.bIsFocusable = kind != Header;
        if (!row.Build()) return null;
        return row;
    }

    bool Build()
    {
        entry = Ui.GameWidget(this, $"{Editors}{kind}.{kind}_C");
        var tree = Ui.Tree(this);
        var stack = UGameplayStatics.SpawnObject(Unreal.ClassOf<UOverlay>(), tree) as UOverlay;
        var catcher = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) as UBorder;
        if (entry == null || tree == null || stack == null || catcher == null) return false;
        // The game's row never gets the mouse; the see-through border over it does, for this widget.
        entry.SetVisibility(ESlateVisibility.HitTestInvisible);
        background = GameUI.Find(entry, "Background") as UUserWidget;
        catcher.SetBrushColor(Ui.Color(0, 0, 0, 0));
        if (kind == Header) catcher.SetVisibility(ESlateVisibility.HitTestInvisible);
        Fill(stack.AddChildToOverlay(entry));
        Fill(stack.AddChildToOverlay(catcher));
        tree.RootWidget = stack;
        return true;
    }

    static void Fill(UOverlaySlot? slot)
    {
        slot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
        slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
    }

    UTextBlock? Part(string name) => GameUI.Find(entry, name) as UTextBlock;

    /// <summary>The row's name, at its left.</summary>
    public void SetName(string text) => Part("Text_SettingName")?.SetText(text);

    /// <summary>
    /// The game's widgets in the row are made now (they set their own labels and keys while being made): shows the row's
    /// own again.
    /// </summary>
    public override void Construct() => ShowOwn();

    void ShowOwn()
    {
        if (hasButton) ShowButton();
        if (hasKeys)
        {
            ShowKey("ActionWidget_PrimaryKey", primaryKey);
            ShowKey("ActionWidget_SecondaryKey", secondaryKey);
            // The game's warning that a key isn't bound to anything: these keys belong to a mod.
            GameUI.Find(entry, "InvalidImage")?.SetVisibility(ESlateVisibility.Collapsed);
        }
    }

    /// <summary>A button row's button: its text, or no button (empty).</summary>
    public void SetButton(string text)
    {
        buttonText = text;
        hasButton = true;
        ShowButton();
    }

    void ShowButton()
    {
        var button = GameUI.Find(entry, "Button_Navigate") as UUserWidget;
        if (button == null) return;
        if (buttonText == "")
        {
            button.SetVisibility(ESlateVisibility.Hidden);
            return;
        }
        button.SetVisibility(ESlateVisibility.HitTestInvisible);
        (GameUI.FindOfClass(button, Unreal.ClassOf<UTextBlock>()) as UTextBlock)?.SetText(buttonText);
    }

    /// <summary>A toggle's state: its ON/OFF and its switch (the game's own animation, played to its end at once).</summary>
    public void SetToggle(bool on)
    {
        Part("StateText")?.SetText(on ? "ON" : "OFF");
        var switching = look?.Animation(Toggle, "ToggleOn");
        if (switching == null) return;
        if (on) PlayOn(entry, switching, true, 100);
        else PlayOn(entry, switching, false, 100);
    }

    /// <summary>A dropdown's chosen option.</summary>
    public void SetChoice(string text)
    {
        var button = GameUI.Find(entry, "ExpandButton") as UUserWidget;
        (GameUI.FindOfClass(button, Unreal.ClassOf<UTextBlock>()) as UTextBlock)?.SetText(text);
    }

    /// <summary>A slider's range and value.</summary>
    public void SetSlider(double from, double to, double by, double at, bool isPercentage)
    {
        min = from;
        max = to > from ? to : from + 1;
        step = by;
        percentage = isPercentage;
        var slider = GameUI.Find(entry, "Slider_SettingValue") as USlider;
        slider?.SetMinValue((float)min);
        slider?.SetMaxValue((float)max);
        slider?.SetStepSize(step > 0 ? (float)step : 0);
        ShowValue(at);
    }

    /// <summary>A keybind's two keys, as the game shows keys (key caps and controller buttons).</summary>
    public void SetKeys(FKey primary, FKey secondary)
    {
        primaryKey = primary;
        secondaryKey = secondary;
        hasKeys = true;
        ShowOwn();
    }

    /// <summary>A key in one of the boxes, as the game shows keys; a dash when there's none.</summary>
    void ShowKey(string name, FKey key)
    {
        if (GameUI.Find(entry, name) is not URebindableInputDisplayWidget display) return;
        var none = key.KeyName.ToString() == "" || key.KeyName.ToString() == "None";
        display.SingleInputAction?.SetKey(key);
        display.SingleInputAction?.SetVisibility(none ? ESlateVisibility.Collapsed : ESlateVisibility.HitTestInvisible);
        display.InvalidTextBlock?.SetText("-");
        display.InvalidTextBlock?.SetVisibility(none ? ESlateVisibility.HitTestInvisible : ESlateVisibility.Collapsed);
    }

    void ShowValue(double at)
    {
        value = UKismetMathLibrary.FClamp(at, min, max);
        var slider = GameUI.Find(entry, "Slider_SettingValue") as USlider;
        slider?.SetValue((float)value);
        Part("Text_SettingValue")?.SetText(percentage ? $"{UKismetMathLibrary.Round64(value * 100)}%" : $"{value:0.##}");
        // The bar's fill is drawn by its material, which the game's row sets as its value changes.
        var bar = GameUI.Find(entry, "Slider_Bar") as UImage;
        bar?.GetDynamicMaterial()?.SetScalarParameterValue("Value", (float)((value - min) / (max - min)));
    }

    static void PlayOn(UUserWidget? widget, UWidgetAnimation? animation, bool forward, float speed)
    {
        if (widget == null || animation == null) return;
        if (forward) widget.PlayAnimationForward(animation, speed, false);
        else widget.PlayAnimationReverse(animation, speed, false);
    }

    /// <summary>Lights the row like the game does while it's hovered or focused, or puts it back.</summary>
    void Light(bool on)
    {
        if (on == lit) return;
        lit = on;
        PlayOn(background, look?.Animation(RowBackground, "OnHover"), on, 1);
        PlayOn(entry, look?.Animation(kind, "OnHover"), on, 1);
    }

    void Hovered()
    {
        Light(true);
        page?.Hovered(Index, Action);
    }

    public override void OnMouseEnter(FGeometry geometry, FPointerEvent e) => Hovered();

    public override void OnMouseLeave(FPointerEvent e)
    {
        if (!HasKeyboardFocus() && !dragging) Light(false);
    }

    public override FEventReply OnFocusReceived(FGeometry geometry, FFocusEvent e)
    {
        Hovered();
        page?.Focused(this);
        return UWidgetBlueprintLibrary.Handled();
    }

    public override void OnFocusLost(FFocusEvent e)
    {
        capturing = false;
        Light(false);
    }

    public override FEventReply OnMouseButtonDown(FGeometry geometry, FPointerEvent e)
    {
        if (UKismetInputLibrary.PointerEvent_GetEffectingButton(e).KeyName.ToString() != "LeftMouseButton") return UWidgetBlueprintLibrary.Unhandled();
        var at = UKismetInputLibrary.PointerEvent_GetScreenSpacePosition(e);
        var reply = UWidgetBlueprintLibrary.Handled();
        if (kind == Slider)
        {
            dragging = true;
            SlideTo(at);
            return UWidgetBlueprintLibrary.CaptureMouse(ref reply, this);
        }
        if (kind == Keys)
        {
            var second = GameUI.Find(entry, "Button_SecondaryKey");
            StartCapture(second != null && USlateBlueprintLibrary.IsUnderLocation(second.GetCachedGeometry(), at));
            return reply;
        }
        Activate();
        return reply;
    }

    public override FEventReply OnMouseMove(FGeometry geometry, FPointerEvent e)
    {
        if (!dragging) return UWidgetBlueprintLibrary.Unhandled();
        SlideTo(UKismetInputLibrary.PointerEvent_GetScreenSpacePosition(e));
        return UWidgetBlueprintLibrary.Handled();
    }

    public override FEventReply OnMouseButtonUp(FGeometry geometry, FPointerEvent e)
    {
        if (!dragging) return UWidgetBlueprintLibrary.Unhandled();
        dragging = false;
        var reply = UWidgetBlueprintLibrary.Handled();
        return UWidgetBlueprintLibrary.ReleaseMouseCapture(ref reply);
    }

    public override FEventReply OnKeyDown(FGeometry geometry, FKeyEvent e)
    {
        var key = UKismetInputLibrary.GetKey(e);
        var name = key.KeyName.ToString();
        if (capturing)
        {
            capturing = false;
            if (name == "Escape") page?.KeyCaptureEnded();
            // From the controller, the key goes in the second box (the controller's); else in the box chosen.
            else page?.KeyPicked(Index, captureSecondary || UKismetInputLibrary.Key_IsGamepadKey(key), key);
            return UWidgetBlueprintLibrary.Handled();
        }
        if (name == "Enter" || name == "SpaceBar" || name == "Gamepad_FaceButton_Bottom" || name == "Virtual_Accept")
        {
            if (kind == Keys) StartCapture(false);
            else Activate();
            return UWidgetBlueprintLibrary.Handled();
        }
        if (kind == Slider)
        {
            int direction = 0;
            if (name == "Left" || name == "Gamepad_DPad_Left" || name == "Gamepad_LeftStick_Left") direction = -1;
            if (name == "Right" || name == "Gamepad_DPad_Right" || name == "Gamepad_LeftStick_Right") direction = 1;
            if (direction != 0)
            {
                var by = step > 0 ? step : (max - min) / 20;
                Moved(value + direction * by);
                return UWidgetBlueprintLibrary.Handled();
            }
        }
        return UWidgetBlueprintLibrary.Unhandled();
    }

    /// <summary>Clicked, or A or Enter: does the row's action (a toggle switches, a button row opens or runs).</summary>
    void Activate()
    {
        if (Action != "") page?.Clicked(Index, Action);
    }

    /// <summary>Waits for the next key for one of the two boxes (focused, so the key comes here).</summary>
    void StartCapture(bool secondary)
    {
        capturing = true;
        captureSecondary = secondary;
        SetKeyboardFocus();
        page?.KeyCaptureStarted(secondary);
    }

    void SlideTo(FVector2D at)
    {
        var slider = GameUI.Find(entry, "Slider_SettingValue");
        if (slider == null) return;
        var geometry = slider.GetCachedGeometry();
        var size = USlateBlueprintLibrary.GetLocalSize(geometry);
        if (size.X <= 0) return;
        var local = USlateBlueprintLibrary.AbsoluteToLocal(geometry, at);
        Moved(min + UKismetMathLibrary.FClamp(local.X / size.X, 0, 1) * (max - min));
    }

    void Moved(double to)
    {
        if (step > 0) to = min + UKismetMathLibrary.Round64((to - min) / step) * step;
        ShowValue(to);
        page?.SliderMoved(Index, (float)value);
    }
}
