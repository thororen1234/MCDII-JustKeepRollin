using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// The Mods tab of the game's settings, in the settings' own look (see <see cref="GameLook"/>): the list of mods (the
/// loader first), and a page for each with what its ModInfo says about it, whether it runs, and its settings page.
/// </summary>
public class ModsPage : UUserWidget
{
    // What's shown: the list, the loader's page, or a mod's page (its index).
    const int ListPage = -2;
    const int LoaderPage = -1;
    const float ControlWidth = 420;

    ModManager? manager;
    GameLook? look;
    UWidgetTree? tree;
    UVerticalBox? content;
    int selected;
    // Widget settings' widgets, made again each time the page is.
    List<UUserWidget> settingWidgets = new();
    // Slider settings' value texts, by setting index: dragging updates the text, not the whole page.
    Dictionary<int, UTextBlock> sliderTexts = new();
    bool dragging;

    // What the player did, done on the next Update: the page's controls report it from the middle of the game's input
    // code, where making the page again or saving isn't safe (both can let the garbage collector run, and the clicked
    // button itself would be destroyed while its click still runs). Per entry: the setting (-1 for the page's own), what
    // to do, its text, its key and whether that's the second key.
    List<int> queuedIndex = new();
    List<string> queuedAction = new();
    List<string> queuedText = new();
    List<FKey> queuedKey = new();
    List<bool> queuedSecondary = new();
    // The last slider move (whether there was one, the setting, where to), and whether the page is to be made again.
    bool sliderMoved;
    int movedSlider;
    float movedTo;
    bool refreshWanted;

    /// <summary>
    /// Makes the page, empty: keep it in a field, then <see cref="Refresh"/> it. Until it's in a field, only this code's
    /// locals hold it, and the garbage collector doesn't see those: anything that can let it run (like Log.Write, which
    /// saves) would destroy the page while it's being made.
    /// </summary>
    public static ModsPage? Create(ModManager manager, GameLook look)
    {
        var page = UWidgetBlueprintLibrary.Create(manager, Unreal.ClassOf<ModsPage>(), World.PlayerController(manager)) as ModsPage;
        if (page == null) return null;
        page.manager = manager;
        page.look = look;
        page.selected = ListPage;
        if (!page.Build()) return null;
        return page;
    }

    bool Build()
    {
        tree = Ui.Tree(this);
        var scroll = UGameplayStatics.SpawnObject(Unreal.ClassOf<UScrollBox>(), tree) as UScrollBox;
        content = Ui.Column(tree);
        if (tree == null || scroll == null || content == null) return false;
        scroll.AddChild(content);
        // Where the game's settings list is: the left two thirds (the rest is its details panel).
        var columns = Ui.Row(tree);
        var rest = UGameplayStatics.SpawnObject(Unreal.ClassOf<USpacer>(), tree) as USpacer;
        if (columns == null || rest == null) return false;
        columns.AddChildToHorizontalBox(scroll)?.SetSize(new FSlateChildSize { Value = 2, SizeRule = ESlateSizeRule.Fill });
        columns.AddChildToHorizontalBox(rest)?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
        var padded = Ui.Panel(tree, columns, Ui.Color(0, 0, 0, 0), 0);
        padded?.SetPadding(new FMargin { Left = 40, Top = 24, Right = 40, Bottom = 24 });
        tree.RootWidget = padded;
        return true;
    }

    /// <summary>What the page has, for the log: what's missing makes Refresh do nothing.</summary>
    public string Ready() => $"manager {manager != null}, look {look != null}, tree {tree != null}, content {content != null}";

    /// <summary>Shows everything again, as it is now.</summary>
    public void Refresh()
    {
        if (manager == null || content == null || tree == null || look == null)
        {
            manager?.Note("Mods page: not refreshed: " + Ready());
            return;
        }
        refreshWanted = false;
        manager.MakingPage();
        content.ClearChildren();
        settingWidgets.Clear();
        sliderTexts.Clear();
        manager.Note(selected == ListPage ? "Mods page: the list" : $"Mods page: {SelectedFolder()}");
        if (selected == ListPage) ShowList();
        else ShowMod();
    }

    /// <summary>Makes the page again on the next <see cref="Update"/>.</summary>
    public void RefreshLater() => refreshWanted = true;

    /// <summary>A setting changed: the page shows the new value if it's that mod's.</summary>
    public void SettingChanged(string folder)
    {
        if (!dragging && selected != ListPage && folder == SelectedFolder()) refreshWanted = true;
    }

    /// <summary>
    /// Does what the player did since the last call, then makes the page again if that changed it. Called by the loader's
    /// own timer, outside the game's input code.
    /// </summary>
    public void Update()
    {
        if (queuedIndex.Count > 0)
        {
            var indices = queuedIndex;
            var actions = queuedAction;
            var texts = queuedText;
            var keys = queuedKey;
            var secondaries = queuedSecondary;
            queuedIndex.Clear();
            queuedAction.Clear();
            queuedText.Clear();
            queuedKey.Clear();
            queuedSecondary.Clear();
            for (int i = 0; i < indices.Count; i++)
            {
                if (actions[i] == "key") KeyPickedNow(indices[i], secondaries[i], keys[i]);
                else if (actions[i] == "text" || actions[i] == "hex") TextEnteredNow(indices[i], actions[i] == "hex", texts[i]);
                else Act(indices[i], actions[i]);
            }
        }
        if (sliderMoved)
        {
            var index = movedSlider;
            sliderMoved = false;
            SliderMovedNow(index, movedTo);
        }
        if (refreshWanted) Refresh();
    }

    void Queue(int index, string action, string text, FKey key, bool secondary)
    {
        queuedIndex.Add(index);
        queuedAction.Add(action);
        queuedText.Add(text);
        queuedKey.Add(key);
        queuedSecondary.Add(secondary);
    }

    string SelectedFolder() => selected < 0 || manager == null ? Unreal.ModName : manager.Folders[selected];

    UTextBlock? Text(string text, string role) => look?.Text(tree, text, role);

    UTextBlock? Wrapped(string text)
    {
        var label = Text(text, "label");
        label?.SetAutoWrapText(true);
        return label;
    }

    /// <summary>Text made fainter: what explains something, under it.</summary>
    static UTextBlock? Dimmed(UTextBlock? text)
    {
        text?.SetOpacity(0.65f);
        return text;
    }

    void ShowList()
    {
        if (manager == null) return;
        Add(Text("Mods", "heading"), 0);
        Add(Text($"{manager.Running()} of {manager.Mods.Count} mods running", "label"), 8);
        if (manager.Notice != "") Add(Wrapped(manager.Notice), 6);
        if (manager.Warning != "") Add(Wrapped(manager.Warning), 6);
        AddRow("BetterBlueprintLoader", Text(manager.Version, "value"), -1, "select:-1", 18);
        for (int i = 0; i < manager.Mods.Count; i++)
        {
            var info = manager.Infos[i];
            var name = info != null && info.ModName != "" ? info.ModName : manager.Folders[i];
            var version = info != null && info.Version != "" ? info.Version + "  ·  " : "";
            AddRow(name, Text(version + StateText(i), "value"), -1, "select:" + i, 4);
        }
        if (manager.Mods.Count == 0 && manager.Started) Add(Text("No mods found in Paks\\~mods", "label"), 12);
    }

    string StateText(int index)
    {
        if (manager == null) return "";
        var state = manager.States[index];
        if (state == "Running") return "On";
        if (state == "Crashed") return "Off: crashed the game while starting";
        if (state == "Nothing to run") return "Changes game files";
        return state;
    }

    void ShowMod()
    {
        if (manager == null || tree == null) return;
        var folder = SelectedFolder();
        var info = manager.InfoOf(folder);
        AddRow("Back to Mods", null, -1, "list", 0);

        // The title, and the version at the right.
        var header = Ui.Row(tree);
        if (header != null)
        {
            var title = header.AddChildToHorizontalBox(Text(info != null && info.ModName != "" ? info.ModName : folder, "heading"));
            title?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
            if (info != null && info.Version != "") header.AddChildToHorizontalBox(Text("Version " + info.Version, "label"))?.SetVerticalAlignment(EVerticalAlignment.VAlign_Top);
            Add(header, 18);
        }
        if (info != null && info.Author != "")
        {
            var author = Text("By " + info.Author, "label");
            Add(info.AuthorUrl != "" ? Clickable(author, -1, "website") : author, 6);
        }
        if (info != null && info.Description != "") Add(Wrapped(info.Description), 16);

        if (selected == LoaderPage)
        {
            if (manager.Notice != "") Add(Wrapped(manager.Notice), 12);
            if (manager.Warning != "") Add(Wrapped(manager.Warning), 6);
            if (manager.Stopped != "") Add(Wrapped(manager.Stopped), 6);
        }
        else
        {
            var state = StateText(selected);
            if (manager.States[selected] == "Running") state = $"Running, started in {manager.Times[selected]:0.0} ms";
            Add(Wrapped(state), 12);
            if (manager.CanRun(selected)) AddRow("Enabled", look?.Switch(tree, manager.IsOn(selected)), -1, "onoff", 12);
            AddRow("Start Earlier", null, -1, "up", 4);
            AddRow("Start Later", null, -1, "down", 4);
        }

        if (info == null) return;
        bool hasSettings = false;
        for (int i = 0; i < info.Settings.Count; i++)
        {
            if (info.Settings[i].Id != "" || info.Settings[i].Type == SettingKind.Widget) hasSettings = true;
            AddSetting(folder, i, ModInfos.Setting(info, i));
        }
        if (hasSettings) AddRow("Reset Settings", null, -1, "reset", 24);
    }

    void Add(UWidget? widget, float top)
    {
        if (widget == null || content == null) return;
        content.AddChildToVerticalBox(widget)?.SetPadding(new FMargin { Top = top });
    }

    /// <summary>
    /// A settings row: the label at the left, something at the right, on the game's row background. With an action, the
    /// whole row is a button.
    /// </summary>
    void AddRow(string label, UWidget? right, int index, string action, float top)
    {
        if (look == null || tree == null) return;
        var line = Ui.Row(tree);
        if (line == null) return;
        var text = line.AddChildToHorizontalBox(Text(label, "label"));
        text?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
        text?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        if (right != null)
        {
            var slot = line.AddChildToHorizontalBox(right);
            slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            slot?.SetPadding(new FMargin { Left = 32 });
        }
        var row = look.Row(tree, line);
        Add(action != "" ? Clickable(row, index, action) : row, top);
    }

    /// <summary>A widget as a button: the page's button around it, with only a faint highlight of its own.</summary>
    UWidget? Clickable(UWidget? widget, int index, string action)
    {
        if (widget == null || tree == null) return null;
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<PageButton>(), tree) as PageButton;
        if (button == null) return widget;
        button.Setup(this, index, action);
        var invisible = new FSlateBrush { DrawAs = ESlateBrushDrawType.NoDrawType };
        var hover = Look.Rounded(Ui.Color(1, 1, 1, 0.05f), Ui.Color(1, 1, 1, 0), 0, 2);
        button.SetStyle(new FButtonStyle { Normal = invisible, Hovered = hover, Pressed = hover, Disabled = invisible, NormalPadding = new FMargin(), PressedPadding = new FMargin() });
        if (button.AddChild(widget) is UButtonSlot slot)
        {
            slot.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
            slot.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
            slot.SetPadding(new FMargin());
        }
        return button;
    }

    void AddSetting(string folder, int index, SettingEntry setting)
    {
        if (manager == null || tree == null || look == null) return;
        manager.GetValue(folder, setting.Id, out var value);
        switch (setting.Type)
        {
            case SettingKind.Heading:
                Add(Text(setting.Label, "heading"), 24);
                return;
            case SettingKind.Text:
                Add(Wrapped(setting.Label), 8);
                return;
            case SettingKind.Spacer:
                var spacer = UGameplayStatics.SpawnObject(Unreal.ClassOf<USpacer>(), tree) as USpacer;
                spacer?.SetSize(new FVector2D { X = 1, Y = setting.Height });
                Add(spacer, 0);
                return;
            case SettingKind.Widget:
                AddWidgetSetting(folder, setting);
                return;
            case SettingKind.Toggle:
                AddRow(setting.Label, look.Switch(tree, value == "true"), index, "toggle", 4);
                break;
            case SettingKind.Slider:
                AddSlider(index, setting, UKismetStringLibrary.Conv_StringToDouble(value));
                break;
            case SettingKind.Select:
                int option = UKismetStringLibrary.Conv_StringToInt(value);
                var shown = option >= 0 && option < setting.Options.Count ? setting.Options[option] : value;
                AddRow(setting.Label, look.ButtonLook(tree, shown), index, "next", 4);
                break;
            case SettingKind.UrlButton:
            case SettingKind.EventButton:
                var text = setting.ButtonText != "" ? setting.ButtonText : (setting.Type == SettingKind.UrlButton ? "Open" : setting.Label);
                AddRow(setting.Label, look.ButtonLook(tree, text), index, setting.Type == SettingKind.UrlButton ? "url" : "event", 4);
                break;
            case SettingKind.Keybind:
                AddRow(setting.Label, Fixed(Keys(folder, index, setting)), index, "", 4);
                break;
            case SettingKind.TextInput:
                AddRow(setting.Label, Fixed(Look.TextBox(tree, this, index, false, value, setting.Placeholder)), index, "", 4);
                break;
            case SettingKind.Colour:
                AddRow(setting.Label, Fixed(Colours(index, setting, value)), index, "", 4);
                break;
        }
        if (setting.Description != "") Add(Dimmed(Wrapped(setting.Description)), 4);
    }

    /// <summary>A control at a fixed width, for the right of a row.</summary>
    UWidget? Fixed(UWidget? control)
    {
        var box = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        if (box == null || control == null) return control;
        box.SetWidthOverride(ControlWidth);
        box.AddChild(control);
        return box;
    }

    static string SliderText(SettingEntry setting, double value)
    {
        if (setting.Percentage) return $"{UKismetMathLibrary.Round64(value * 100)}%";
        return $"{value:0.##}";
    }

    void AddSlider(int index, SettingEntry setting, double value)
    {
        if (look == null || tree == null) return;
        var slider = UGameplayStatics.SpawnObject(Unreal.ClassOf<PageSlider>(), tree) as PageSlider;
        var right = Ui.Row(tree);
        var text = Text(SliderText(setting, value), "value");
        if (slider == null || right == null || text == null) return;
        look.StyleSlider(slider);
        slider.SetMinValue((float)setting.Min);
        slider.SetMaxValue((float)setting.Max);
        slider.SetStepSize(setting.Step > 0 ? (float)setting.Step : 0);
        slider.SetValue((float)value);
        // After the value is set: setting it isn't the player moving it.
        slider.Setup(this, index);
        sliderTexts.Add(index, text);
        right.AddChildToHorizontalBox(text)?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        var box = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        if (box != null)
        {
            box.SetWidthOverride(ControlWidth - 80);
            box.AddChild(slider);
            var slot = right.AddChildToHorizontalBox(box);
            slot?.SetPadding(new FMargin { Left = 16 });
            slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        }
        AddRow(setting.Label, right, index, "", 4);
    }

    UWidget? Keys(string folder, int index, SettingEntry setting)
    {
        if (manager == null || tree == null) return null;
        manager.GetKeybind(folder, setting.Id, out var key, out var secondary);
        var row = Ui.Row(tree);
        if (row == null) return null;
        var first = row.AddChildToHorizontalBox(Look.KeySelector(tree, this, index, false, key));
        first?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
        first?.SetPadding(new FMargin { Right = 6 });
        row.AddChildToHorizontalBox(Look.KeySelector(tree, this, index, true, secondary))?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
        return row;
    }

    UWidget? Colours(int index, SettingEntry setting, string value)
    {
        if (tree == null) return null;
        var column = Ui.Column(tree);
        var swatches = UGameplayStatics.SpawnObject(Unreal.ClassOf<UWrapBox>(), tree) as UWrapBox;
        if (column == null || swatches == null) return null;
        var options = setting.Options.Count > 0 ? setting.Options : Look.Palette();
        foreach (var hex in options)
        {
            var swatch = Look.Swatch(tree, this, index, hex, UKismetStringLibrary.ToUpper(hex) == UKismetStringLibrary.ToUpper(value));
            if (swatch != null) swatches.AddChildToWrapBox(swatch)?.SetPadding(new FMargin { Right = 4, Bottom = 4 });
        }
        column.AddChildToVerticalBox(swatches);
        if (setting.HexInput) column.AddChildToVerticalBox(Look.TextBox(tree, this, index, true, value, "#RRGGBB"))?.SetPadding(new FMargin { Top = 4 });
        return column;
    }

    void AddWidgetSetting(string folder, SettingEntry setting)
    {
        if (manager == null || setting.WidgetClass == null) return;
        // Made again every time the page is: it gets OnWidgetAdded and the saved settings.
        var widget = Ui.Create(this, setting.WidgetClass);
        if (widget == null) return;
        settingWidgets.Add(widget);
        Add(widget, 10);
        if (widget is ISettingsEvents target) target.OnWidgetAdded(setting.Id);
        manager.SendSaved(folder, widget);
    }

    /// <summary>A row or button was clicked: index is the setting (-1 for the page's own). Done on the next Update.</summary>
    public void Clicked(int index, string action) => Queue(index, action, "", new FKey(), false);

    void Act(int index, string action)
    {
        if (manager == null) return;
        var folder = SelectedFolder();
        UKismetStringLibrary.Split(action, ":", out var verb, out var argument, ESearchCase.CaseSensitive, ESearchDir.FromStart);
        if (verb == "") verb = action;
        if (index < 0)
        {
            ActOnPage(verb, argument);
            return;
        }
        var info = manager.InfoOf(folder);
        if (info == null || index >= info.Settings.Count) return;
        var setting = ModInfos.Setting(info, index);
        manager.GetValue(folder, setting.Id, out var value);
        switch (verb)
        {
            case "toggle":
                manager.SetValue(folder, setting.Id, value == "true" ? "false" : "true");
                break;
            case "next":
                int count = setting.Options.Count;
                if (count == 0) break;
                manager.SetValue(folder, setting.Id, ((UKismetStringLibrary.Conv_StringToInt(value) + 1) % count).ToString());
                break;
            case "url":
                UKismetSystemLibrary.LaunchURL(setting.Url);
                break;
            case "event":
                manager.PressButton(folder, setting.Id);
                break;
            case "colour":
                manager.SetValue(folder, setting.Id, UKismetStringLibrary.ToUpper(argument));
                break;
        }
    }

    void ActOnPage(string verb, string argument)
    {
        if (manager == null) return;
        var folder = SelectedFolder();
        switch (verb)
        {
            case "select":
                selected = UKismetStringLibrary.Conv_StringToInt(argument);
                refreshWanted = true;
                return;
            case "list":
                selected = ListPage;
                refreshWanted = true;
                return;
            case "website":
                var info = manager.InfoOf(folder);
                if (info != null) UKismetSystemLibrary.LaunchURL(info.AuthorUrl);
                return;
            case "reset":
                manager.ResetSettings(folder);
                return;
            case "onoff":
                if (selected >= 0) manager.SetOn(selected, !manager.IsOn(selected));
                refreshWanted = true;
                return;
            case "up":
            case "down":
                if (selected < 0) return;
                int other = selected + (verb == "up" ? -1 : 1);
                if (other < 0 || other >= manager.Mods.Count) return;
                manager.Move(selected, verb == "up" ? -1 : 1);
                selected = other;
                refreshWanted = true;
                return;
        }
    }

    /// <summary>A slider moved: done on the next Update (only the last move counts).</summary>
    public void SliderMoved(int index, float position)
    {
        sliderMoved = true;
        movedSlider = index;
        movedTo = position;
    }

    /// <summary>Saves a slider's value (on the setting's step) and shows it, without making the page again.</summary>
    void SliderMovedNow(int index, float position)
    {
        if (manager == null) return;
        var folder = SelectedFolder();
        var info = manager.InfoOf(folder);
        if (info == null || index < 0 || index >= info.Settings.Count) return;
        var setting = ModInfos.Setting(info, index);
        double value = position;
        if (setting.Step > 0) value = setting.Min + UKismetMathLibrary.Round64((value - setting.Min) / setting.Step) * setting.Step;
        value = UKismetMathLibrary.FClamp(value, setting.Min, setting.Max);
        dragging = true;
        manager.SetValue(folder, setting.Id, ModInfos.Number(value));
        dragging = false;
        if (sliderTexts.TryGetValue(index, out var text)) text.SetText(SliderText(setting, value));
    }

    /// <summary>A key was picked for a keybind setting: done on the next Update.</summary>
    public void KeyPicked(int index, bool secondary, FKey key) => Queue(index, "key", "", key, secondary);

    void KeyPickedNow(int index, bool secondary, FKey key)
    {
        if (manager == null) return;
        var folder = SelectedFolder();
        var info = manager.InfoOf(folder);
        if (info == null || index < 0 || index >= info.Settings.Count) return;
        var id = info.Settings[index].Id;
        manager.GetKeybind(folder, id, out var first, out var second);
        if (secondary) manager.SetKeybind(folder, id, first, key);
        else manager.SetKeybind(folder, id, key, second);
    }

    /// <summary>Text was entered in a text input (or a colour's hex box): done on the next Update.</summary>
    public void TextEntered(int index, bool hex, string text) => Queue(index, hex ? "hex" : "text", text, new FKey(), false);

    void TextEnteredNow(int index, bool hex, string text)
    {
        if (manager == null) return;
        var folder = SelectedFolder();
        var info = manager.InfoOf(folder);
        if (info == null || index < 0 || index >= info.Settings.Count) return;
        var value = text;
        if (hex)
        {
            value = UKismetStringLibrary.ToUpper(UKismetStringLibrary.Trim(text));
            if (!UKismetStringLibrary.StartsWith(value, "#", ESearchCase.CaseSensitive)) value = "#" + value;
            if (UKismetStringLibrary.Len(value) != 7) return;
        }
        manager.SetValue(folder, info.Settings[index].Id, value);
    }
}

/// <summary>A button of the Mods page: tells the page which setting (or -1) and what to do.</summary>
public class PageButton : UButton
{
    ModsPage? page;
    int setting;
    string action;

    public void Setup(ModsPage owner, int index, string what)
    {
        page = owner;
        setting = index;
        action = what;
        OnClicked += Clicked;
    }

    void Clicked() => page?.Clicked(setting, action);
}

/// <summary>A slider setting's slider, in the game's slider style.</summary>
public class PageSlider : USlider
{
    ModsPage? page;
    int setting;

    public void Setup(ModsPage owner, int index)
    {
        page = owner;
        setting = index;
        OnValueChanged += Moved;
    }

    void Moved(float value) => page?.SliderMoved(setting, value);
}

/// <summary>A keybind setting's key: click it, then press the key.</summary>
public class PageKey : UInputKeySelector
{
    ModsPage? page;
    int setting;
    bool secondary;

    public void Setup(ModsPage owner, int index, bool isSecondary)
    {
        page = owner;
        setting = index;
        secondary = isSecondary;
        OnKeySelected_2 += Picked;
    }

    void Picked(FInputChord chord) => page?.KeyPicked(setting, secondary, chord.Key);
}

/// <summary>A text input setting's box (or a colour's hex box): saved when the text is committed.</summary>
public class PageText : UEditableTextBox
{
    ModsPage? page;
    int setting;
    bool hex;

    public void Setup(ModsPage owner, int index, bool isHex)
    {
        page = owner;
        setting = index;
        hex = isHex;
        OnTextCommitted += Committed;
    }

    void Committed(FText text, ETextCommit method) => page?.TextEntered(setting, hex, text.ToString());
}
