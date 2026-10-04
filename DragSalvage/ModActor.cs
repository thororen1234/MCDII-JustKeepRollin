using System.Collections.Generic;
using NeoRune;
using UE.Angelscript;
using UE.CommonUI;
using UE.Engine;
using UE.SlateCore;
using UE.UMG;

namespace DragSalvage;

/// <summary>
/// In the salvage screen, hold the mouse on an item and drag across others to select them all, instead of clicking
/// each one.
/// </summary>
public class ModActor : AActor
{
    WidgetWatcher? panels;
    WidgetWatcher? slotWatcher;
    WidgetWatcher? entryWatcher;
    UAS_SalvagePanel? panel;
    bool panelShown;
    // Item buttons: equipment slots and the inventory grid's entries.
    List<UCommonButtonBase> slots = new();
    // Items the current drag has been over, and the one to click next.
    List<UCommonButtonBase> dragged = new();
    UCommonButtonBase? pending;

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
    }

    public bool Active => panelShown;

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
        var shown = panel != null && UKismetSystemLibrary.IsValid(panel) && panel.IsVisible();
        if (shown != panelShown)
        {
            panelShown = shown;
            for (int i = slots.Count - 1; i >= 0; i--)
            {
                if (!UKismetSystemLibrary.IsValid(slots[i])) slots.RemoveAt(i);
                else slots[i].SetClickMethod(shown ? EButtonClickMethod.MouseDown : EButtonClickMethod.DownAndUp);
            }
        }
        if (pending == null) return;
        var slot = pending;
        pending = null;
        if (shown && UKismetSystemLibrary.IsValid(slot)) slot.HandleButtonClicked();
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
        var catcher = UWidgetBlueprintLibrary.Create(mod, Unreal.ClassOf<DragCatcher>(), World.PlayerController(mod)) as DragCatcher;
        if (catcher == null || !catcher.Build()) return;
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
        var border = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) as UBorder;
        if (tree == null || border == null) return false;
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

    public override void OnMouseEnter(FGeometry geometry, FPointerEvent e)
    {
        if (slot != null && mod != null && LeftDown(e)) mod.DraggedOnto(slot);
    }
}
