using System.Collections.Generic;
using NeoRune;
using UE.Angelscript;
using UE.CommonUI;
using UE.Engine;
using UE.SlateCore;
using UE.SpicewoodUI;
using UE.UMG;

namespace DragSalvage;

/// <summary>
/// In the salvage screen, hold the mouse on an item and drag across others to select them all, instead of clicking
/// each one. On a controller, hold A on an item and move off it: every item moved onto is selected until A is let go
/// (or, if letting go can't be seen, pressed again).
/// </summary>
public class ModActor : AActor
{
    WidgetWatcher? panels;
    WidgetWatcher? slotWatcher;
    WidgetWatcher? entryWatcher;
    UAS_SalvagePanel? panel;
    bool panelShown;
    // Item buttons: equipment slots and the inventory grid's entries.
    readonly List<UCommonButtonBase> slots = [];
    // Items the current drag has been over, and the one to click next.
    readonly List<UCommonButtonBase> dragged = [];
    UCommonButtonBase? pending;
    // Controller. The game handles A itself, mostly out of the mod's sight. A going down bounces the item's focus (it
    // loses it and gets it back at once). A coming up on the item it went down on comes as the left mouse button
    // coming up on it, sometimes with a bounce too; anywhere else, only the key catcher below sees it. (The player
    // controller never sees A while a menu is open, and the items never say they're pressed.) So a drag starts when
    // the focus moves off the item A went down on before it comes up, and runs until it does.
    UCommonButtonBase? focused;
    UCommonButtonBase? lostBy;
    int lostAt = -1;
    int frame;
    // The item A went down on, and whether the focus has moved off it since (the drag is on).
    UCommonButtonBase? padStart;
    bool padDragging;
    readonly List<UCommonButtonBase> padDragged = [];
    // The item A was pressed on to stop a drag: its next bounce is A coming up, unless the focus moves first.
    UCommonButtonBase? padStopped;
    // Where and when A last came up as the mouse.
    UCommonButtonBase? padUpOn;
    double padUpAt;
    // The item the mod clicked, and when: the click bounces its focus too, but that isn't A.
    UCommonButtonBase? clicked;
    int clickedAt = -1;
    // Letting go of A. The game's key-rebinding "press any key" catcher sees keys before the menu does and reports
    // each one as it comes up, wherever the focus is, so it's on while a drag runs. While it's on it holds the
    // controller's buttons back from the menu (the stick may still move the focus).
    UPressAnyKeyInputPreProcessorWrapper? keys;
    bool keysOn;
    double keysActiveAt;
    // Set by the catcher, handled in the tick (turning the catcher off from its own callback isn't safe).
    bool padReleased;
    // The catcher heard nothing and the focus hasn't moved for a while: off for the rest of this drag, which then
    // ends with a press of A.
    bool keysGaveUp;
    const double KeysTimeout = 3;
    // What the controller drag saw, saved from the tick (saving from the game's input callbacks isn't safe).
    readonly List<string> notes = [];
    double savedAt;

    void Note(string line) => notes.Add($"{(int)(World.RealTime(this) * 1000)}ms {line}");

    static string Name(UWidget? widget) => widget != null ? UKismetSystemLibrary.GetObjectName(widget) : "nothing";

    void SaveNotes()
    {
        if (notes.Count == 0) return;
        FileLog.WriteAll(notes);
        notes.Clear();
        savedAt = World.RealTime(this);
    }

    protected override void ReceiveEndPlay(EEndPlayReason reason)
    {
        SetKeys(false);
        SaveNotes();
    }

    protected override void ReceiveBeginPlay()
    {
        // Menus can pause the game, and the salvage screen is one.
        SetTickableWhenPaused(true);
        panels = WidgetWatcher.Start(this, Unreal.ClassOf<UAS_SalvagePanel>(), 0.25f);
        if (panels != null) panels.Found += OnPanel;
        slotWatcher = WidgetWatcher.Start(this, Unreal.ClassOf<UAS_ItemSlot>(), 0.25f);
        if (slotWatcher != null) slotWatcher.Found += OnSlot;
        entryWatcher = WidgetWatcher.Start(this, Unreal.ClassOf<UAS_SpicewoodInventoryGridEntry>(), 0.25f);
        if (entryWatcher != null) entryWatcher.Found += OnSlot;
    }

    void OnPanel(UUserWidget widget) => panel = widget as UAS_SalvagePanel;

    void OnSlot(UUserWidget widget)
    {
        // Equipped gear isn't salvaged from its slots.
        if (widget is not UCommonButtonBase slot || widget is UAS_EquippedItemSlot) return;
        slots.Add(slot);
        if (panelShown) slot.SetClickMethod(EButtonClickMethod.MouseDown);
        DragCatcher.AddTo(slot, this);
        slot.OnButtonBaseFocused += OnSlotFocused;
        slot.OnButtonBaseUnfocused += OnSlotUnfocused;
    }

    void OnSlotUnfocused(UCommonButtonBase? slot)
    {
        if (slot != focused) return;
        focused = null;
        lostBy = slot;
        lostAt = frame;
    }

    void OnSlotFocused(UCommonButtonBase? slot)
    {
        var bounced = slot != null && slot == lostBy && lostAt == frame;
        focused = slot;
        lostBy = null;
        if (!panelShown || slot == null) return;
        // A mouse click bounces the focus the same way A does, and hovering moves it: only a controller counts.
        if (!UsingController())
        {
            ResetPad();
            return;
        }
        if (bounced)
        {
            if (slot == clicked && clickedAt == frame)
            {
                Note($"bounce on {Name(slot)} from the mod's click, ignored");
                return;
            }
            OnPadBounce(slot);
            return;
        }
        padStopped = null;
        if (padStart == null) return;
        // Moved off the item before A came up: A is held, and the drag is on.
        if (!padDragging) Note($"drag started from {Name(padStart)}");
        padDragging = true;
        keysActiveAt = World.RealTime(this);
        Note($"focus moved to {Name(slot)}");
        if (padDragged.Contains(slot)) return;
        padDragged.Add(slot);
        // Clicked on the next tick, once the game has seen the item focused.
        pending = slot;
    }

    /// <summary>A went down or came up on the item with the focus. Going down, the game toggles the item itself.</summary>
    void OnPadBounce(UCommonButtonBase slot)
    {
        // A coming up can bounce the focus as well as come as the mouse: that's the same release, not A going down.
        if (slot == padUpOn && World.RealTime(this) - padUpAt < 0.1)
        {
            padUpOn = null;
            return;
        }
        padUpOn = null;
        if (slot == padStopped)
        {
            Note($"A up on {Name(slot)} after stopping");
            padStopped = null;
            return;
        }
        if (padStart == null)
        {
            Note($"A down on {Name(slot)}");
            padStart = slot;
            padDragged.Add(slot);
            return;
        }
        // A came up where it went down: a plain press, or the end of a drag that came back to its first item.
        if (!padDragging || slot == padStart)
        {
            Note($"A up on {Name(slot)}{(padDragging ? ", drag ended" : "")}");
            ResetPad();
            return;
        }
        // A pressed again during a drag stops it. The drag already toggled this item, so the game's toggle is undone.
        Note($"A down on {Name(slot)}: drag stopped");
        var undo = padDragged.Contains(slot);
        ResetPad();
        padStopped = slot;
        if (undo) pending = slot;
    }

    /// <summary>Whether the player is using a controller now (the game shows controller buttons).</summary>
    bool UsingController()
    {
        var input = USubsystemBlueprintLibrary.GetLocalPlayerSubSystemFromPlayerController(World.PlayerController(this), Unreal.ClassOf<UE.CommonInput.UCommonInputSubsystem>()) as UE.CommonInput.UCommonInputSubsystem;
        return input != null && input.GetCurrentInputType() == UE.CommonInput.ECommonInputType.Gamepad;
    }

    void ResetPad()
    {
        padStart = null;
        padDragging = false;
        padDragged.Clear();
        padStopped = null;
        padReleased = false;
        keysGaveUp = false;
    }

    /// <summary>Turns the key catcher on or off.</summary>
    void SetKeys(bool on)
    {
        if (on == keysOn) return;
        if (on && keys == null)
        {
            keys = UPressAnyKeyInputPreProcessorWrapper.Create();
            if (keys == null)
            {
                Note("no key catcher: drags end with a press of A");
                keysGaveUp = true;
                return;
            }
            keys.OnKeySelected += OnPadKeyUp;
            keys.OnKeySelectionCanceled += OnPadCancelUp;
        }
        if (keys == null) return;
        keys.TogglePreprocessor(on);
        keysOn = on;
        keysActiveAt = World.RealTime(this);
        Note($"key catcher {(on ? "on" : "off")}");
    }

    /// <summary>A key came up (the catcher reports B and Escape through OnPadCancelUp instead).</summary>
    void OnPadKeyUp(UE.InputCore.FKey SelectedKey)
    {
        var key = SelectedKey.KeyName.ToString();
        keysActiveAt = World.RealTime(this);
        Note($"key up: {key}");
        if (key == "Gamepad_FaceButton_Bottom" || key == "Virtual_Accept") padReleased = true;
    }

    /// <summary>B came up: it ends the drag too, since the catcher kept the menu from seeing it.</summary>
    void OnPadCancelUp()
    {
        keysActiveAt = World.RealTime(this);
        Note("key up: cancel");
        padReleased = true;
    }

    public bool Active => panelShown;

    /// <summary>
    /// The left mouse button came up on an item. On a controller that's A coming up (the game passes it on as the
    /// mouse, often without bouncing the focus): a tap ends there, before any drag.
    /// </summary>
    public void PadMouseUp(UCommonButtonBase slot)
    {
        if (!panelShown || padStart == null || !UsingController()) return;
        Note($"A up on {Name(slot)} (mouse){(padDragging ? ", drag ended" : "")}");
        ResetPad();
        padUpOn = slot;
        padUpAt = World.RealTime(this);
    }

    /// <summary>The mouse went down on an item: a drag may start from it, and the game marks it itself.</summary>
    public void DragStarted(UCommonButtonBase slot)
    {
        dragged.Clear();
        dragged.Add(slot);
    }

    /// <summary>The mouse came onto an item with the button held.</summary>
    public void DraggedOnto(UCommonButtonBase slot)
    {
        if (!panelShown || dragged.Contains(slot)) return;
        dragged.Add(slot);
        // Clicked on the next tick, once the game has seen the item hovered.
        pending = slot;
    }

    public override void ReceiveTick(float deltaSeconds)
    {
        frame++;
        var shown = panel != null && UKismetSystemLibrary.IsValid(panel) && panel.IsVisible();
        if (shown != panelShown)
        {
            panelShown = shown;
            Note($"salvage {(shown ? "shown" : "hidden")}, {slots.Count} items, focus on {Name(focused)}");
            ResetPad();
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                if (!UKismetSystemLibrary.IsValid(slots[i])) slots.RemoveAt(i);
                else slots[i].SetClickMethod(shown ? EButtonClickMethod.MouseDown : EButtonClickMethod.DownAndUp);
            }
        }
        if (focused != null && !UKismetSystemLibrary.IsValid(focused)) focused = null;
        // The focus went somewhere other than an item (another tab, a button, a popup): A can't be tracked there.
        if (focused == null)
        {
            if (padStart != null) Note("focus left the items: drag ended");
            ResetPad();
        }
        if (padReleased)
        {
            if (padDragging) Note("A up: drag ended");
            ResetPad();
        }
        if (keysOn && World.RealTime(this) - keysActiveAt >= KeysTimeout)
        {
            Note("key catcher heard nothing: drag now ends with a press of A");
            keysGaveUp = true;
        }
        SetKeys(padDragging && !keysGaveUp && shown);
        if (World.RealTime(this) - savedAt >= 2) SaveNotes();
        if (pending == null) return;
        var slot = pending;
        pending = null;
        if (!shown || !UKismetSystemLibrary.IsValid(slot)) return;
        Note($"drag clicks {Name(slot)}");
        clicked = slot;
        clickedAt = frame;
        slot.HandleButtonClicked();
    }
}

/// <summary>
/// An invisible layer over an item button. It lets every click through to the button, and tells the mod when the
/// mouse comes onto the item with the left button held.
/// </summary>
public class DragCatcher : UUserWidget
{
    ModActor? mod;
    UCommonButtonBase? slot;

    public static void AddTo(UCommonButtonBase slot, ModActor mod)
    {
        var grid = FindGrid(slot.WidgetTree?.RootWidget);
        if (grid == null) return;
        if (UWidgetBlueprintLibrary.Create(mod, Unreal.ClassOf<DragCatcher>(), World.PlayerController(mod)) is not DragCatcher catcher || !catcher.Build()) return;
        catcher.mod = mod;
        catcher.slot = slot;
        // In the grid's first cell, spanning it all, above the item's own widgets.
        var place = grid.AddChildToGrid(catcher, 0, 0);
        place?.SetRowSpan(16);
        place?.SetColumnSpan(16);
        place?.SetLayer(1000);
        place?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
        place?.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
    }

    /// <summary>The item button's grid panel, which covers the whole item: the first one under its root.</summary>
    static UGridPanel? FindGrid(UWidget? root)
    {
        var queue = new List<UWidget>();
        if (root != null) queue.Add(root);
        for (int i = 0; i < queue.Count && i < 64; i++)
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

    bool Build()
    {
        var tree = WidgetTree;
        if (tree == null)
        {
            tree = UGameplayStatics.SpawnObject(Unreal.ClassOf<UWidgetTree>(), this) as UWidgetTree;
            WidgetTree = tree;
        }
        // A see-through border: it takes part in hit testing, so mouse events reach this widget.
        if (UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) is not UBorder border || tree == null) return false;
        border.SetBrushColor(new UE.CoreUObject.FLinearColor());
        tree.RootWidget = border;
        return true;
    }

    static bool LeftDown(FPointerEvent e) => UKismetInputLibrary.PointerEvent_IsMouseButtonDown(e, new UE.InputCore.FKey { KeyName = "LeftMouseButton" });

    public override FEventReply OnMouseButtonDown(FGeometry geometry, FPointerEvent e)
    {
        if (slot != null && mod != null && mod.Active) mod.DragStarted(slot);
        return UWidgetBlueprintLibrary.Unhandled();
    }

    public override FEventReply OnMouseButtonUp(FGeometry geometry, FPointerEvent e)
    {
        if (slot != null && mod != null && mod.Active && UKismetInputLibrary.PointerEvent_GetEffectingButton(e).KeyName.ToString() == "LeftMouseButton")
            mod.PadMouseUp(slot);
        return UWidgetBlueprintLibrary.Unhandled();
    }

    public override void OnMouseEnter(FGeometry geometry, FPointerEvent e)
    {
        if (slot != null && mod != null && LeftDown(e)) mod.DraggedOnto(slot);
    }
}
