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
/// loader first), and a page for each with what its ModInfo says about it, whether it runs, and its settings page. Like
/// the game's tabs, it has two parts: the list where the game's settings list is, and the details of the row under the
/// mouse (or the controller's focus) in the game's details frame.
/// </summary>
public class ModsPage : UUserWidget
{
    // What's shown: the list, the loader's page, or a mod's page (its index).
    const int ListPage = -2;
    const int LoaderPage = -1;
    const float ControlWidth = 420;
    // Inside the game's details frame, where its own text starts.
    const float DetailsInset = 32;

    ModManager? manager;
    GameLook? look;
    UWidgetTree? tree;
    UVerticalBox? content;
    UCanvasPanel? canvas;
    UScrollBox? listArea;
    UBorder? detailsArea;
    UTextBlock? detailsTitle;
    UTextBlock? detailsBody;
    UTextBlock? detailsMore;
    // The page's buttons, to find the one with the controller's focus, and what the details show now.
    List<PageButton> buttons = new();
    // The page's rows made of the game's settings rows (see GameRow), and the state line of a mod's page.
    List<GameRow> rows = new();
    UTextBlock? stateLine;
    // The row to give the focus after the page is made again ("first", or "index/action"), when a row had it.
    string focusWanted;
    // Set while the page changes a setting itself and shows the change in place: the page isn't made again for it.
    bool inPlace;
    int hoveredIndex;
    string hoveredAction;
    bool hoverChanged;
    string detailsShown;
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
        page.hoveredIndex = -1;
        page.hoveredAction = "";
        page.detailsShown = "";
        page.focusWanted = "";
        if (!page.Build()) return null;
        return page;
    }

    bool Build()
    {
        tree = Ui.Tree(this);
        canvas = UGameplayStatics.SpawnObject(Unreal.ClassOf<UCanvasPanel>(), tree) as UCanvasPanel;
        listArea = UGameplayStatics.SpawnObject(Unreal.ClassOf<UScrollBox>(), tree) as UScrollBox;
        content = Ui.Column(tree);
        detailsArea = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) as UBorder;
        var details = Ui.Column(tree);
        if (tree == null || canvas == null || listArea == null || content == null || detailsArea == null || details == null || look == null) return false;
        listArea.AddChild(content);
        canvas.AddChildToCanvas(listArea);
        // The details have no background of their own: they sit in the game's details frame.
        detailsArea.SetBrushColor(Ui.Color(0, 0, 0, 0));
        detailsArea.SetPadding(new FMargin { Left = DetailsInset, Top = DetailsInset, Right = DetailsInset, Bottom = DetailsInset });
        detailsTitle = look.Text(tree, "", "section");
        detailsBody = look.Text(tree, "", "body");
        detailsMore = look.Text(tree, "", "body");
        detailsBody?.SetAutoWrapText(true);
        detailsMore?.SetAutoWrapText(true);
        detailsMore?.SetOpacity(0.7f);
        details.AddChildToVerticalBox(detailsTitle);
        details.AddChildToVerticalBox(detailsBody)?.SetPadding(new FMargin { Top = 16 });
        details.AddChildToVerticalBox(detailsMore)?.SetPadding(new FMargin { Top = 24 });
        detailsArea.AddChild(details);
        canvas.AddChildToCanvas(detailsArea);
        tree.RootWidget = canvas;
        return true;
    }

    /// <summary>
    /// Puts the list and the details where the game's own are (in the page's units, from its top left): GameMenus keeps
    /// the page over the settings panel, which can move.
    /// </summary>
    public void Arrange(FVector2D listAt, FVector2D listSize, FVector2D detailsAt, FVector2D detailsSize)
    {
        Place(listArea, listAt, listSize);
        Place(detailsArea, detailsAt, detailsSize);
    }

    static void Place(UWidget? widget, FVector2D at, FVector2D size)
    {
        if (widget?.Slot is not UCanvasPanelSlot slot) return;
        slot.SetPosition(at);
        slot.SetSize(size);
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
        var hadFocus = FocusedRow(out var focusKey);
        if (focusWanted == "") focusWanted = focusKey;
        content.ClearChildren();
        rows.Clear();
        stateLine = null;
        settingWidgets.Clear();
        sliderTexts.Clear();
        buttons.Clear();
        hoveredIndex = -1;
        hoveredAction = "";
        detailsShown = "";
        manager.Note(selected == ListPage ? "Mods page: the list" : $"Mods page: {SelectedFolder()}");
        if (selected == ListPage) ShowList();
        else ShowMod();
        ShowDetails();
        // The controller (or keyboard) was on a row: it goes on the same one again, or the first of a new page.
        if (hadFocus) FocusRow(focusWanted);
        focusWanted = "";
    }

    /// <summary>Whether a row of the page has the focus, and which ("index/action").</summary>
    bool FocusedRow(out string key)
    {
        key = "";
        foreach (var row in rows)
            if (row != null && UKismetSystemLibrary.IsValid(row) && row.HasAnyUserFocus())
            {
                key = $"{row.Index}/{row.Action}";
                return true;
            }
        foreach (var button in buttons)
            if (button != null && UKismetSystemLibrary.IsValid(button) && button.HasAnyUserFocus()) return true;
        return false;
    }

    void FocusRow(string key)
    {
        GameRow? first = null;
        foreach (var row in rows)
        {
            if (row == null || !row.bIsFocusable) continue;
            if (first == null) first = row;
            if ($"{row.Index}/{row.Action}" == key)
            {
                row.SetKeyboardFocus();
                return;
            }
        }
        first?.SetKeyboardFocus();
    }

    /// <summary>A row got the focus (the controller moved onto it): the list scrolls to show it.</summary>
    public void Focused(GameRow row) => listArea?.ScrollWidgetIntoView(row, true, EDescendantScrollDestination.IntoView, 8);

    /// <summary>The Mods tab was chosen again: the list, like the game's tabs start at their top.</summary>
    public void BackToList()
    {
        if (selected == ListPage) return;
        focusWanted = $"-1/select:{selected}";
        selected = ListPage;
        refreshWanted = true;
    }

    /// <summary>A keybind row waits for a key: the details say so.</summary>
    public void KeyCaptureStarted(bool secondary)
    {
        SetDetails("Press a key", secondary ? "Press the key or controller button for the second box." : "Press a key, or a controller button for the second box.", "Esc cancels.");
        detailsShown = "";
    }

    public void KeyCaptureEnded()
    {
        detailsShown = "";
        ShowDetails();
    }

    /// <summary>One of the game's settings rows for the page, or null (plain look, or not in the game).</summary>
    GameRow? Game(string kind, int index, string action, string name)
    {
        if (look == null || look.Plain) return null;
        var row = GameRow.Make(this, look, kind, index, action);
        if (row == null) return null;
        rows.Add(row);
        row.SetName(name);
        return row;
    }

    GameRow? RowFor(int index, string action)
    {
        foreach (var row in rows)
            if (row != null && row.Index == index && row.Action == action) return row;
        return null;
    }

    /// <summary>Makes the page again on the next <see cref="Update"/>.</summary>
    public void RefreshLater() => refreshWanted = true;

    /// <summary>A setting changed: the page shows the new value if it's that mod's.</summary>
    public void SettingChanged(string folder)
    {
        if (!dragging && !inPlace && selected != ListPage && folder == SelectedFolder()) refreshWanted = true;
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
        else FollowFocus();
    }

    /// <summary>The mouse went onto a row (index and action as its button's): its details show on the next Update.</summary>
    public void Hovered(int index, string action)
    {
        hoveredIndex = index;
        hoveredAction = action;
        hoverChanged = true;
    }

    /// <summary>The controller doesn't hover: the details follow the row it has focused, else the one hovered.</summary>
    void FollowFocus()
    {
        foreach (var button in buttons)
            if (button != null && UKismetSystemLibrary.IsValid(button) && button.HasAnyUserFocus() && (button.Index != hoveredIndex || button.Action != hoveredAction))
            {
                hoveredIndex = button.Index;
                hoveredAction = button.Action;
                hoverChanged = true;
            }
        if (!hoverChanged) return;
        hoverChanged = false;
        ShowDetails();
    }

    /// <summary>
    /// The details of the row hovered or focused: a mod in the list, a setting or control on a mod's page, else the
    /// page's mod.
    /// </summary>
    void ShowDetails()
    {
        if (manager == null) return;
        var key = $"{selected}/{hoveredIndex}/{hoveredAction}";
        if (key == detailsShown) return;
        detailsShown = key;
        UKismetStringLibrary.Split(hoveredAction, ":", out var verb, out var argument, ESearchCase.CaseSensitive, ESearchDir.FromStart);
        if (selected == ListPage)
        {
            if (verb == "select") ShowModDetails(UKismetStringLibrary.Conv_StringToInt(argument));
            else SetDetails("Mods", $"{manager.Running()} of {manager.Mods.Count} mods running.", "Choose a mod to see its page: its details and settings, turning it on and off, and when it starts.");
            return;
        }
        var info = manager.InfoOf(SelectedFolder());
        if (hoveredIndex >= 0 && info != null && hoveredIndex < info.Settings.Count)
        {
            var setting = ModInfos.Setting(info, hoveredIndex);
            SetDetails(setting.Label, setting.Description, "");
            return;
        }
        switch (hoveredAction)
        {
            case "onoff":
                SetDetails("Enabled", "Turns the mod off or on right away. Remembered for next time.", "Turning a mod off stops it, but what it already changed in the game can stay until the next level.");
                return;
            case "up":
            case "down":
                SetDetails("Start order", "Mods start one after another, in the order of the Mods list. Counts from the next level, or Restart Mods.", "");
                return;
            case "reset":
                SetDetails("Reset Settings", "Puts this mod's settings back to their defaults.", "");
                return;
        }
        ShowModDetails(selected);
    }

    /// <summary>A mod's details (LoaderPage: the loader's own).</summary>
    void ShowModDetails(int index)
    {
        if (manager == null) return;
        var folder = index < 0 ? Unreal.ModName : manager.Folders[index];
        var info = manager.InfoOf(folder);
        var name = info != null && info.ModName != "" ? info.ModName : folder;
        var more = info != null && info.Version != "" ? $"Version {info.Version}" : "";
        if (index >= 0) more = Join(more, StateText(index));
        if (info != null && info.Author != "") more = Join(more, "By " + info.Author);
        SetDetails(name, info != null ? info.Description : "", more);
    }

    static string Join(string first, string second) => first == "" ? second : (second == "" ? first : first + "\n" + second);

    void SetDetails(string title, string body, string more)
    {
        detailsTitle?.SetText(title);
        detailsBody?.SetText(body);
        detailsMore?.SetText(more);
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
        var label = Text(text, "body");
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
        if (manager == null || tree == null) return;
        var heading = Game(GameRow.Header, -1, "", "Installed Mods");
        if (heading != null)
        {
            Add(heading, 0);
            if (manager.Notice != "") Add(Wrapped(manager.Notice), 8);
            if (manager.Warning != "") Add(Wrapped(manager.Warning), 8);
            ModRow(-1, OwnName(), 4);
            for (int i = 0; i < manager.Mods.Count; i++)
            {
                var info = manager.Infos[i];
                ModRow(i, info != null && info.ModName != "" ? info.ModName : manager.Folders[i], 4);
            }
            if (manager.Mods.Count == 0 && manager.Started) Add(Wrapped("No mods found in the ~mods folder"), 12);
            return;
        }
        var header = Ui.Row(tree);
        if (header != null)
        {
            header.AddChildToHorizontalBox(Text("Installed Mods", "section"))?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
            header.AddChildToHorizontalBox(Dimmed(Text($"{manager.Running()} of {manager.Mods.Count} running", "body")))?.SetVerticalAlignment(EVerticalAlignment.VAlign_Bottom);
            Add(header, 0);
        }
        if (manager.Notice != "") Add(Wrapped(manager.Notice), 8);
        if (manager.Warning != "") Add(Wrapped(manager.Warning), 8);
        AddRow(OwnName(), ModRight(-1), -1, "select:-1", 12);
        for (int i = 0; i < manager.Mods.Count; i++)
        {
            var info = manager.Infos[i];
            AddRow(info != null && info.ModName != "" ? info.ModName : manager.Folders[i], ModRight(i), -1, "select:" + i, 4);
        }
        if (manager.Mods.Count == 0 && manager.Started) Add(Wrapped("No mods found in the ~mods folder"), 12);
    }

    /// <summary>A mod in the list, as one of the game's button rows: SETTINGS, or why it isn't running.</summary>
    void ModRow(int index, string name, float top)
    {
        var row = Game(GameRow.Button, -1, "select:" + index, name);
        if (row == null || manager == null) return;
        var info = manager.InfoOf(index < 0 ? Unreal.ModName : manager.Folders[index]);
        var state = index < 0 ? "Running" : manager.States[index];
        if (state == "Crashed") row.SetButton("Crashed");
        else if (state == "Off") row.SetButton("Off");
        else if (state == "Running" || state == "Nothing to run") row.SetButton(HasSettings(info) ? "Settings" : (state == "Running" ? "" : "Game files"));
        else row.SetButton(state);
        Add(row, top);
    }

    string OwnName()
    {
        var info = manager?.InfoOf(Unreal.ModName);
        return info != null && info.ModName != "" ? info.ModName : Unreal.ModName;
    }

    /// <summary>What a mod's row has at the right: why it isn't running, if it isn't, and SETTINGS if it has any.</summary>
    UWidget? ModRight(int index)
    {
        if (manager == null || tree == null || look == null) return null;
        var right = Ui.Row(tree);
        if (right == null) return null;
        if (index >= 0 && manager.States[index] != "Running")
            right.AddChildToHorizontalBox(Text(StateText(index), "value"))?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        var info = manager.InfoOf(index < 0 ? Unreal.ModName : manager.Folders[index]);
        if (HasSettings(info))
        {
            var button = right.AddChildToHorizontalBox(look.ButtonLook(tree, "Settings"));
            button?.SetPadding(new FMargin { Left = 24 });
            button?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        }
        return right;
    }

    static bool HasSettings(ModDetails? info)
    {
        if (info == null) return false;
        for (int i = 0; i < info.Settings.Count; i++)
            if (info.Settings[i].Id != "" || info.Settings[i].Type == SettingKind.Widget) return true;
        return false;
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
        Add(SmallButton("Back to Mods", -1, "list"), 0);

        // The title, and the version at the right.
        var header = Ui.Row(tree);
        if (header != null)
        {
            var title = header.AddChildToHorizontalBox(Text(info != null && info.ModName != "" ? info.ModName : folder, "title"));
            title?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
            title?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            if (info != null && info.Version != "") header.AddChildToHorizontalBox(Dimmed(Text("Version " + info.Version, "body")))?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            Add(header, 20);
        }
        if (info != null && info.Author != "")
        {
            var author = Underlined(Text("By " + info.Author, "body"), info.AuthorUrl != "");
            Add(info.AuthorUrl != "" ? Clickable(author, -1, "website") : author, 4);
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
            var heading = Game(GameRow.Header, -1, "", "Mod");
            if (heading != null)
            {
                Add(heading, 24);
                stateLine = Wrapped(StateLine());
                Add(stateLine, 8);
                if (manager.CanRun(selected))
                {
                    var enabled = Game(GameRow.Toggle, -1, "onoff", "Enabled");
                    enabled?.SetToggle(manager.IsOn(selected));
                    Add(enabled, 8);
                }
                var earlier = Game(GameRow.Button, -1, "up", "Start Earlier");
                earlier?.SetButton("Earlier");
                Add(earlier, 4);
                var later = Game(GameRow.Button, -1, "down", "Start Later");
                later?.SetButton("Later");
                Add(later, 4);
            }
            else
            {
                Add(Text("Mod", "section"), 28);
                stateLine = Wrapped(StateLine());
                Add(Dimmed(stateLine), 8);
                if (manager.CanRun(selected)) AddRow("Enabled", look?.Switch(tree, manager.IsOn(selected)), -1, "onoff", 8);
                AddRow("Start order", StartButtons(), -1, "", 4);
            }
        }

        if (info == null) return;
        bool hasSettings = false;
        for (int i = 0; i < info.Settings.Count; i++)
        {
            if (info.Settings[i].Id != "" || info.Settings[i].Type == SettingKind.Widget) hasSettings = true;
            AddSetting(folder, i, ModInfos.Setting(info, i));
        }
        if (!hasSettings) return;
        var reset = Game(GameRow.Button, -1, "reset", "Reset Settings");
        if (reset != null)
        {
            reset.SetButton("Reset");
            Add(reset, 24);
        }
        else AddRow("Reset Settings", look?.ButtonLook(tree, "Reset"), -1, "reset", 28);
    }

    string StateLine()
    {
        if (manager == null || selected < 0) return "";
        if (manager.States[selected] == "Running") return $"Running, started in {manager.Times[selected]:0.0} ms";
        return StateText(selected);
    }

    /// <summary>Start Earlier and Start Later, side by side in the game's button look.</summary>
    UWidget? StartButtons()
    {
        if (tree == null || look == null) return null;
        var row = Ui.Row(tree);
        if (row == null) return null;
        row.AddChildToHorizontalBox(PageButtonWith(look.Text(tree, "Earlier", "button"), -1, "up", look.Button()));
        row.AddChildToHorizontalBox(PageButtonWith(look.Text(tree, "Later", "button"), -1, "down", look.Button()))?.SetPadding(new FMargin { Left = 8 });
        return row;
    }

    /// <summary>A button in the game's list button look, on its own (Back to Mods).</summary>
    UWidget? SmallButton(string text, int index, string action)
    {
        if (tree == null || look == null) return null;
        var row = Ui.Row(tree);
        if (row == null) return null;
        row.AddChildToHorizontalBox(PageButtonWith(look.Text(tree, text, "button"), index, action, look.Button()));
        return row;
    }

    /// <summary>Text with a line under it, like the game's links (or without one).</summary>
    UWidget? Underlined(UTextBlock? text, bool line)
    {
        if (tree == null || text == null || !line) return text;
        var column = Ui.Column(tree);
        var rule = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) as UBorder;
        var size = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        var fit = Ui.Row(tree);
        if (column == null || rule == null || size == null || fit == null) return text;
        rule.SetBrushColor(Ui.Color(1, 1, 1, 0.8f));
        size.SetHeightOverride(2);
        size.AddChild(rule);
        column.AddChildToVerticalBox(text);
        column.AddChildToVerticalBox(size)?.SetPadding(new FMargin { Top = 2 });
        // In a row, so the line is as wide as the text, not the page.
        fit.AddChildToHorizontalBox(column);
        return fit;
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
        // Every row is a button in the game's row look, so it lights up and shows its details while hovered or focused.
        // One without an action does nothing when clicked: its own controls take the clicks.
        Add(PageButtonWith(line, index, action, look.RowButton()), top);
    }

    /// <summary>A widget as a button with a style: the page's button around it.</summary>
    UWidget? PageButtonWith(UWidget? widget, int index, string action, FButtonStyle style)
    {
        if (widget == null || tree == null) return null;
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<PageButton>(), tree) as PageButton;
        if (button == null) return widget;
        button.Setup(this, index, action);
        buttons.Add(button);
        button.SetStyle(style);
        if (button.AddChild(widget) is UButtonSlot slot)
        {
            slot.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
            slot.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
            slot.SetPadding(new FMargin());
        }
        return button;
    }

    /// <summary>A widget as a link: the page's button around it, with only a faint highlight of its own.</summary>
    UWidget? Clickable(UWidget? widget, int index, string action)
    {
        var invisible = new FSlateBrush { DrawAs = ESlateBrushDrawType.NoDrawType };
        var hover = Look.Rounded(Ui.Color(1, 1, 1, 0.05f), Ui.Color(1, 1, 1, 0), 0, 2);
        return PageButtonWith(widget, index, action, new FButtonStyle { Normal = invisible, Hovered = hover, Pressed = hover, Disabled = invisible, NormalPadding = new FMargin(), PressedPadding = new FMargin() });
    }

    void AddSetting(string folder, int index, SettingEntry setting)
    {
        if (manager == null || tree == null || look == null) return;
        manager.GetValue(folder, setting.Id, out var value);
        if (AddGameSetting(folder, index, setting, value)) return;
        switch (setting.Type)
        {
            case SettingKind.Heading:
                Add(Text(setting.Label, "section"), 28);
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
        if (setting.Description == "" || content == null) return;
        // Under its row, like the game's notes; the details show it too.
        var description = Dimmed(Text(setting.Description, "body"));
        description?.SetAutoWrapText(true);
        if (description != null) content.AddChildToVerticalBox(description)?.SetPadding(new FMargin { Left = 24, Top = 6, Right = 24, Bottom = 6 });
    }

    /// <summary>
    /// A setting as one of the game's settings rows, with its description under it. False for kinds the game has no row
    /// for (text inputs, colours, widgets, text), or in the plain look: the page makes those itself.
    /// </summary>
    bool AddGameSetting(string folder, int index, SettingEntry setting, string value)
    {
        if (manager == null || look == null || look.Plain) return false;
        GameRow? row = null;
        float top = 4;
        switch (setting.Type)
        {
            case SettingKind.Heading:
                row = Game(GameRow.Header, index, "", setting.Label);
                top = 24;
                break;
            case SettingKind.Toggle:
                row = Game(GameRow.Toggle, index, "toggle", setting.Label);
                row?.SetToggle(value == "true");
                break;
            case SettingKind.Slider:
                row = Game(GameRow.Slider, index, "", setting.Label);
                row?.SetSlider(setting.Min, setting.Max, setting.Step, UKismetStringLibrary.Conv_StringToDouble(value), setting.Percentage);
                break;
            case SettingKind.Select:
                row = Game(GameRow.Dropdown, index, "next", setting.Label);
                row?.SetChoice(Choice(setting, value));
                break;
            case SettingKind.UrlButton:
            case SettingKind.EventButton:
                row = Game(GameRow.Button, index, setting.Type == SettingKind.UrlButton ? "url" : "event", setting.Label);
                row?.SetButton(setting.ButtonText != "" ? setting.ButtonText : (setting.Type == SettingKind.UrlButton ? "Open" : setting.Label));
                break;
            case SettingKind.Keybind:
                row = Game(GameRow.Keys, index, "", setting.Label);
                manager.GetKeybind(folder, setting.Id, out var key, out var secondary);
                row?.SetKeys(key, secondary);
                break;
            default:
                return false;
        }
        if (row == null) return false;
        Add(row, top);
        if (setting.Description != "" && content != null && setting.Type != SettingKind.Heading)
        {
            var description = Wrapped(setting.Description);
            if (description != null) content.AddChildToVerticalBox(description)?.SetPadding(new FMargin { Left = 24, Top = 8, Right = 24, Bottom = 4 });
        }
        return true;
    }

    static string Choice(SettingEntry setting, string value)
    {
        int option = UKismetStringLibrary.Conv_StringToInt(value);
        return option >= 0 && option < setting.Options.Count ? setting.Options[option] : value;
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
        var box = look != null ? look.Box() : Look.Button(false, false);
        var first = row.AddChildToHorizontalBox(Look.KeySelector(tree, this, index, false, key, box));
        first?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
        first?.SetPadding(new FMargin { Right = 6 });
        row.AddChildToHorizontalBox(Look.KeySelector(tree, this, index, true, secondary, box))?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
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
                var on = value != "true";
                var toggle = RowFor(index, "toggle");
                inPlace = toggle != null;
                manager.SetValue(folder, setting.Id, on ? "true" : "false");
                inPlace = false;
                toggle?.SetToggle(on);
                break;
            case "next":
                int count = setting.Options.Count;
                if (count == 0) break;
                var next = ((UKismetStringLibrary.Conv_StringToInt(value) + 1) % count).ToString();
                var dropdown = RowFor(index, "next");
                inPlace = dropdown != null;
                manager.SetValue(folder, setting.Id, next);
                inPlace = false;
                dropdown?.SetChoice(Choice(setting, next));
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
                focusWanted = "first";
                refreshWanted = true;
                return;
            case "list":
                focusWanted = $"-1/select:{selected}";
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
                if (selected < 0) return;
                manager.SetOn(selected, !manager.IsOn(selected));
                var enabled = RowFor(-1, "onoff");
                if (enabled == null)
                {
                    refreshWanted = true;
                    return;
                }
                enabled.SetToggle(manager.IsOn(selected));
                stateLine?.SetText(StateLine());
                return;
            case "up":
            case "down":
                if (selected < 0) return;
                int other = selected + (verb == "up" ? -1 : 1);
                if (other < 0 || other >= manager.Mods.Count) return;
                manager.Move(selected, verb == "up" ? -1 : 1);
                selected = other;
                focusWanted = $"-1/{verb}";
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
        if (secondary) second = key;
        else first = key;
        var keys = RowFor(index, "");
        inPlace = keys != null;
        manager.SetKeybind(folder, id, first, second);
        inPlace = false;
        keys?.SetKeys(first, second);
        KeyCaptureEnded();
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
    public int Index;
    public string Action;

    public void Setup(ModsPage owner, int index, string what)
    {
        page = owner;
        Index = index;
        Action = what;
        OnClicked += Clicked;
        OnHovered += Hovered;
    }

    void Clicked()
    {
        // A row without an action only shows its details.
        if (Action != "") page?.Clicked(Index, Action);
    }

    void Hovered() => page?.Hovered(Index, Action);
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
