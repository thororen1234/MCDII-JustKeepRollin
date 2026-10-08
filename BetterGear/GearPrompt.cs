using System.Collections.Generic;
using NeoRune;
using UE.Angelscript;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.OreUI;
using UE.Slate;
using UE.SlateCore;
using UE.SpicewoodUI;
using UE.UMG;

namespace BetterGear;

/// <summary>
/// Adds "Hide Helmet" (or "Show Helmet") and "Change Helmet Look" to the game's right-click menu of an equipped armor
/// piece or weapon in the inventory, in the look of the menu's own options. The game's menu has a fixed set of options:
/// these are buttons of the mod's own put under its list. The look is picked in a popup with the kind's found gear.
/// Its own object, not the ModActor: callbacks from the game into an actor with the mod settings interface crash it.
/// </summary>
public class GearPrompt : UObject
{
    // How long after a right-click (or the focus leaving a slot, with a controller) the game's menu counts as that
    // slot's, in seconds.
    const double ClickWindow = 1;
    const double FocusWindow = 0.25;
    // How long the inventory must stay closed before the look popup closes with it, in seconds.
    const double InventoryGrace = 0.5;

    GearLook? gear;
    bool everything;
    // The inventory's equipped gear slots, with the kind each holds.
    List<UCommonButtonBase> slots = new();
    List<int> slotKinds = new();
    // The screens the slots are on: the look popup closes when none of them is active.
    List<UCommonActivatableWidget> screens = new();
    // The slot last right-clicked, and the one the focus last left, with when (real time).
    int clickedKind;
    double clickedAt;
    int leftKind;
    double leftAt;
    // The game's right-click menus seen, whether each was open on the last tick, and the rows added to them.
    List<UAS_ContextMenu_Activatable> menus = new();
    List<bool> open = new();
    List<GearPromptRow?> rows = new();
    // The look popup, while it shows, and since when the inventory has been closed under it (real time, 0 while open).
    GearMenu? picker;
    double inventoryGoneAt;

    public static GearPrompt? Create(UObject owner, GearLook gear, bool showEverything)
    {
        var prompt = UGameplayStatics.SpawnObject(Unreal.ClassOf<GearPrompt>(), owner) as GearPrompt;
        if (prompt == null) return null;
        prompt.gear = gear;
        prompt.everything = showEverything;
        prompt.clickedKind = -1;
        prompt.leftKind = -1;
        return prompt;
    }

    /// <summary>The Show All Gear setting, for the look popup.</summary>
    public void ShowEverything(bool on) => everything = on;

    /// <summary>Call a few times a second: finds the equipped slots and the game's right-click menu as the game makes them.</summary>
    public void Scan()
    {
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var found, Unreal.ClassOf<UAS_EquippedItemSlot>(), false);
        foreach (var widget in found)
        {
            if (widget is not UAS_EquippedItemSlot slot || slots.Contains(slot)) continue;
            int kind = GearLook.KindOf(slot.EquipmentSlotTag.TagName.ToString());
            slots.Add(slot);
            slotKinds.Add(kind);
            if (UCommonUILibrary.FindParentWidgetOfType(slot, Unreal.ClassOf<UCommonActivatableWidget>()) is UCommonActivatableWidget screen
                && !screens.Contains(screen))
                screens.Add(screen);
            if (kind < 0) continue;
            slot.OnButtonBaseRightClicked_2 += RightClicked;
            slot.OnButtonBaseUnfocused += Unfocused;
        }
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var shown, Unreal.ClassOf<UAS_ContextMenu_Activatable>(), false);
        foreach (var widget in shown)
            if (widget is UAS_ContextMenu_Activatable menu && !menus.Contains(menu))
            {
                menus.Add(menu);
                open.Add(false);
                rows.Add(null);
            }
    }

    void RightClicked(UUserWidget? button)
    {
        int at = button is UCommonButtonBase slot ? slots.IndexOf(slot) : -1;
        if (at < 0) return;
        clickedKind = slotKinds[at];
        clickedAt = World.RealTime(this);
        // The game opens its menu now: look for it before the next scan.
        Scan();
    }

    void Unfocused(UCommonButtonBase? button)
    {
        int at = button != null ? slots.IndexOf(button) : -1;
        if (at < 0) return;
        leftKind = slotKinds[at];
        leftAt = World.RealTime(this);
    }

    /// <summary>
    /// Call every frame: adds the options to the game's right-click menu as it opens for an equipped slot, and closes the
    /// look popup when the inventory closes.
    /// </summary>
    public void Tick()
    {
        for (int i = 0; i < menus.Count; i++)
        {
            var menu = menus[i];
            bool now = menu != null && UKismetSystemLibrary.IsValid(menu) && menu.IsActivated();
            if (now && !open[i]) Opened(i);
            open[i] = now;
        }
        // The popup goes with the inventory (not at once: the inventory's screen can blink inactive as the menu closes).
        if (picker == null || !picker.IsInViewport() || InventoryShown()) inventoryGoneAt = 0;
        else if (inventoryGoneAt == 0) inventoryGoneAt = World.RealTime(this);
        else if (World.RealTime(this) - inventoryGoneAt > InventoryGrace)
        {
            picker.Hide();
            inventoryGoneAt = 0;
        }
    }

    /// <summary>Whether the inventory shows: the screen of one of its equipped slots is active.</summary>
    bool InventoryShown()
    {
        foreach (var screen in screens)
            if (screen != null && UKismetSystemLibrary.IsValid(screen) && screen.IsActivated()) return true;
        return false;
    }

    void Opened(int index)
    {
        var menu = menus[index];
        double now = World.RealTime(this);
        int kind = -1;
        if (clickedKind >= 0 && now - clickedAt <= ClickWindow) kind = clickedKind;
        else if (leftKind >= 0 && now - leftAt <= FocusWindow) kind = leftKind;
        clickedKind = -1;
        leftKind = -1;
        var row = rows[index];
        if (row == null && kind >= 0)
        {
            // Kept in its list before it's set up: setting it up loads the game's styles, which can let the garbage
            // collector run.
            row = GearPromptRow.Create(this);
            rows[index] = row;
            if (row != null && !row.Setup(this, menu)) row = null;
            rows[index] = row;
        }
        if (row == null || gear == null) return;
        if (kind < 0)
        {
            row.SetVisibility(ESlateVisibility.Collapsed);
            return;
        }
        var title = GearLook.KindTitle(kind);
        row.Show(kind, $"{(gear.IsHidden(kind) ? "Show" : "Hide")} {title}", $"Change {title} Look");
    }

    /// <summary>One of the added options was picked: 0 hides or shows the kind, 1 opens its look popup.</summary>
    public void Act(int kind, int option)
    {
        if (gear == null) return;
        if (option == 0) gear.ToggleHidden(kind);
        else
        {
            picker?.Hide();
            // Kept in the field before it's set up: setting it up loads the game's assets.
            picker = GearMenu.Create(this);
            if (picker != null && picker.SetupPopup(gear, everything, kind)) picker.Open();
            else picker = null;
        }
    }
}

/// <summary>
/// The options added under the game's right-click menu: the game's own text buttons, in its menu options' style, so they
/// look and act (hover, focus, sound) like the menu's own.
/// </summary>
public class GearPromptRow : UUserWidget
{
    // The game's text button, and its menu options' style.
    const string ButtonClass = "/OreUI/UI/Button/W_SpicewoodTextButton.W_SpicewoodTextButton_C";
    const string MenuStyle = "/OreUI/UI/Button/Role/Action/ButtonStyle_ContextMenu.ButtonStyle_ContextMenu_C";
    // The button's text block, and the gap between the menu's options.
    const string TextName = "TextBlockWidget";
    const float Gap = 8;

    GearPrompt? prompt;
    UAS_ContextMenu_Activatable? menu;
    UVerticalBox? list;
    List<UCommonButtonBase> buttons = new();
    List<UTextBlock> labels = new();
    int kind;

    public static GearPromptRow? Create(UObject context) =>
        UWidgetBlueprintLibrary.Create(context, Unreal.ClassOf<GearPromptRow>(), World.PlayerController(context)) as GearPromptRow;

    /// <summary>Builds the two buttons and puts them under the menu's list of options. False if the menu isn't as expected.</summary>
    public bool Setup(GearPrompt owner, UAS_ContextMenu_Activatable contextMenu)
    {
        prompt = owner;
        menu = contextMenu;
        var buttonClass = Unreal.LoadClass<UCommonButtonBase>(ButtonClass);
        var styleClass = Unreal.LoadClass<UCommonButtonStyle>(MenuStyle);
        if (buttonClass == null)
        {
            FileLog.Write("The game's text button isn't in the game: no Hide and Look options");
            return false;
        }

        var tree = WidgetTree;
        if (tree == null)
        {
            tree = UGameplayStatics.SpawnObject(Unreal.ClassOf<UWidgetTree>(), this) as UWidgetTree;
            WidgetTree = tree;
        }
        if (tree == null) return false;
        list = UGameplayStatics.SpawnObject(Unreal.ClassOf<UVerticalBox>(), tree) as UVerticalBox;
        if (list == null) return false;
        tree.RootWidget = list;
        for (int i = 0; i < 2; i++)
        {
            if (UWidgetBlueprintLibrary.Create(this, buttonClass, World.PlayerController(this)) is not UCommonButtonBase button) return false;
            buttons.Add(button);
            if (styleClass != null)
            {
                // The game's buttons pick their style by the input used: all of them are the menu options' one.
                if (button is USpicewoodButtonBase spicewood)
                {
                    spicewood.KeyboardStyle = styleClass;
                    spicewood.GamepadStyle = styleClass;
                    spicewood.TouchStyle = styleClass;
                }
                button.SetStyle(styleClass);
            }
            var label = FindText(button);
            if (label == null)
            {
                FileLog.Write("The game's text button has no text block: no Hide and Look options");
                return false;
            }
            labels.Add(label);
            button.OnButtonBaseClicked += Clicked;
            var place = list.AddChildToVerticalBox(button);
            place?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
            place?.SetPadding(new FMargin { Top = Gap });
        }

        // The menu: a border around a grid with its list of options in it. The buttons go in the grid's next row.
        var grid = FindGrid(contextMenu.ContextMenu?.WidgetTree?.RootWidget);
        if (grid == null)
        {
            FileLog.Write("The game's right-click menu has no grid to add the Hide and Look options to");
            return false;
        }
        int rowsUsed = 0;
        for (int c = 0; c < grid.GetChildrenCount(); c++)
            if (grid.GetChildAt(c)?.Slot is UGridSlot taken && taken.Row + 1 > rowsUsed) rowsUsed = taken.Row + 1;
        var cell = grid.AddChildToGrid(this, rowsUsed, 0);
        cell?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
        FileLog.Write($"Hide and Look options added to the game's right-click menu (row {rowsUsed})");
        return true;
    }

    /// <summary>Shows the options for a kind of gear, with their texts (in capitals, like the menu's own).</summary>
    public void Show(int gearKind, string hideText, string lookText)
    {
        kind = gearKind;
        SetText(0, hideText.ToUpper());
        SetText(1, lookText.ToUpper());
        SetVisibility(ESlateVisibility.Visible);
    }

    /// <summary>
    /// A button's text: as the button's own (its text block gets it again from there each time its look changes, as
    /// hovering does), and on the text block for now.
    /// </summary>
    void SetText(int index, string text)
    {
        if (buttons[index] is UOreUIButtonBase button) button.SetButtonText(text);
        labels[index].SetText(text);
    }

    void Clicked(UCommonButtonBase? button)
    {
        int option = button != null ? buttons.IndexOf(button) : -1;
        if (option < 0) return;
        // Closed the way a click beside the menu closes it, so the game knows it's closed.
        if (menu != null && UKismetSystemLibrary.IsValid(menu) && menu.InvisibleButtonScrim != null)
            menu.OnInvisibleButtonScrimClicked(menu.InvisibleButtonScrim);
        prompt?.Act(kind, option);
    }

    /// <summary>The button's text block, looking inside the widgets it's made of too.</summary>
    static UTextBlock? FindText(UUserWidget button)
    {
        var queue = new List<UWidget>();
        var root = button.WidgetTree?.RootWidget;
        if (root != null) queue.Add(root);
        for (int i = 0; i < queue.Count && i < 256; i++)
        {
            if (queue[i] is UTextBlock text && UKismetSystemLibrary.GetObjectName(text) == TextName) return text;
            if (queue[i] is UUserWidget user && user.WidgetTree?.RootWidget != null) queue.Add(user.WidgetTree.RootWidget);
            if (queue[i] is UPanelWidget panel)
                for (int c = 0; c < panel.GetChildrenCount(); c++)
                {
                    var child = panel.GetChildAt(c);
                    if (child != null) queue.Add(child);
                }
        }
        return null;
    }

    /// <summary>The first grid panel under a widget.</summary>
    static UGridPanel? FindGrid(UWidget? root)
    {
        var queue = new List<UWidget>();
        if (root != null) queue.Add(root);
        for (int i = 0; i < queue.Count && i < 256; i++)
        {
            if (queue[i] is UGridPanel grid) return grid;
            if (queue[i] is UPanelWidget panel)
                for (int c = 0; c < panel.GetChildrenCount(); c++)
                {
                    var child = panel.GetChildAt(c);
                    if (child != null) queue.Add(child);
                }
        }
        return null;
    }
}
