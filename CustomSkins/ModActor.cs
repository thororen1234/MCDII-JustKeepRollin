using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace CustomSkins;

/// <summary>
/// Custom Skins: wear your own skin PNGs, switched live from the inventory's Collectibles screen or with F6. Only you
/// see them: everyone else sees the skin picked in the game's own menu, so that one is the fallback. The keys are
/// settings in BetterBlueprintLoader's Mods tab.
/// </summary>
[ModSetting.Heading("Keys")]
[ModSetting.Keybind(NextSetting, "Next Skin", Default = DefaultNextKey,
    Description = "Wears the next skin in the Skins folder; after the last one, the game's skin again.")]
[ModSetting.Keybind(ReloadSetting, "Reload Skins", Default = DefaultReloadKey,
    Description = "Reads the skin files again, to see changes you just saved in your image editor.")]
[ModSetting.Keybind(ExportSetting, "Save Game Skin", Default = DefaultExportKey,
    Description = "Saves the skin picked in the game's menu as a PNG in the Skins folder's _game folder, to start your own from.")]
[ModSetting.Keybind(InfoSetting, "Show Info", Default = DefaultInfoKey,
    Description = "Writes what the mod knows about your character to the log and shows it. If something doesn't work, press it in game, press Copy and send the log.")]
public class ModActor : AActor, IModSettings
{
    const string NextSetting = "next_key";
    const string ReloadSetting = "reload_key";
    const string ExportSetting = "export_key";
    const string InfoSetting = "info_key";
    const string DefaultNextKey = "F6";
    const string DefaultReloadKey = "F7";
    const string DefaultExportKey = "F8";
    const string DefaultInfoKey = "F9";
    // The game puts its skin back when the character or gear changes: this often, the custom one goes back on.
    const float CheckInterval = 1f;
    const float MenuInterval = 0.25f;
    // The inventory's Collectibles screen (capes and pets), and the screen around it, which is active while it shows.
    const string CollectiblesClass = "W_Collectibles.W_Collectibles_C";
    const string CollectiblesScreenClass = "W_Collectibles_Activatable.W_Collectibles_Activatable_C";
    // The Collectibles item grid: the skin row goes in the list it's in.
    const string ItemGridName = "ItemGrid";
    // Where the row sits when the item grid has no list to go in: just past the scrollbar, with its bottom level with
    // the scrollbar's.
    const float RowLeft = 16;
    const float RowBottom = 196;

    SkinSwapper? skins;
    PausableTimer? checker;
    PausableTimer? menuWatcher;
    SkinRow? row;
    UUserWidget? rowIn;
    // The Collectibles item grid: a floating row sits over its bottom.
    UWidget? grid;
    // Without a list to go in, the row shows over the screen while the Collectibles screen is open.
    bool floating;
    bool menuShown;

    protected override void ReceiveBeginPlay()
    {
        // Menus can pause the game, and the inventory, which shows the character, is one.
        SetTickableWhenPaused(true);
        skins = SkinSwapper.Create(this);
        checker = PausableTimer.Start(this, CheckInterval, loop: true);
        if (checker != null) checker.Fired += Check;
        menuWatcher = PausableTimer.Start(this, MenuInterval, loop: true);
        if (menuWatcher != null) menuWatcher.Fired += WatchMenu;
    }

    // The keys, each setting's two (the second empty unless set in the Mods tab).
    FKey nextKey = new FKey { KeyName = DefaultNextKey };
    FKey nextKey2 = new FKey();
    FKey reloadKey = new FKey { KeyName = DefaultReloadKey };
    FKey reloadKey2 = new FKey();
    FKey exportKey = new FKey { KeyName = DefaultExportKey };
    FKey exportKey2 = new FKey();
    FKey infoKey = new FKey { KeyName = DefaultInfoKey };
    FKey infoKey2 = new FKey();

    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey)
    {
        if (id == NextSetting)
        {
            nextKey = key;
            nextKey2 = secondaryKey;
        }
        else if (id == ReloadSetting)
        {
            reloadKey = key;
            reloadKey2 = secondaryKey;
        }
        else if (id == ExportSetting)
        {
            exportKey = key;
            exportKey2 = secondaryKey;
        }
        else if (id == InfoSetting)
        {
            infoKey = key;
            infoKey2 = secondaryKey;
        }
    }

    public void OnSettingsReset()
    {
        nextKey = new FKey { KeyName = DefaultNextKey };
        reloadKey = new FKey { KeyName = DefaultReloadKey };
        exportKey = new FKey { KeyName = DefaultExportKey };
        infoKey = new FKey { KeyName = DefaultInfoKey };
        nextKey2 = new FKey();
        reloadKey2 = new FKey();
        exportKey2 = new FKey();
        infoKey2 = new FKey();
    }

    static bool Pressed(APlayerController controller, FKey key, FKey secondary) =>
        controller.WasInputKeyJustPressed(key) || controller.WasInputKeyJustPressed(secondary);

    public override void ReceiveTick(float deltaSeconds)
    {
        skins?.UpdateFace();
        var controller = World.PlayerController(this);
        if (controller == null || skins == null) return;
        if (Pressed(controller, nextKey, nextKey2))
        {
            skins.Next();
            row?.Refresh();
        }
        if (Pressed(controller, reloadKey, reloadKey2))
        {
            skins.Reload();
            row?.Refresh();
        }
        if (Pressed(controller, exportKey, exportKey2)) skins.ExportGameSkin();
        if (Pressed(controller, infoKey, infoKey2))
        {
            skins.LogInfo();
            Log.Show(this);
        }
    }

    void Check() => skins?.Check();

    /// <summary>Puts the skin row in the Collectibles screen when it opens, with the skins in the folder now.</summary>
    void WatchMenu()
    {
        if (skins == null) return;
        UUserWidget? collectibles = null;
        bool shown = false;
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var widgets, Unreal.ClassOf<UUserWidget>(), false);
        foreach (var widget in widgets)
        {
            if (widget == null) continue;
            var cls = UKismetSystemLibrary.Conv_SoftClassReferenceToString(UKismetSystemLibrary.Conv_ClassToSoftClassReference(UGameplayStatics.GetObjectClass(widget)));
            if (cls.Contains(CollectiblesClass)) collectibles = widget;
            else if (cls.Contains(CollectiblesScreenClass) && widget is UCommonActivatableWidget screen && screen.IsActivated()) shown = true;
        }

        if (shown && !menuShown)
        {
            if (row == null) row = SkinRow.Create(this, skins);
            else row.Refresh();
        }
        menuShown = shown;
        if (row == null) return;
        if (collectibles != null && collectibles != rowIn) PlaceRow(collectibles);
        if (!floating) return;
        row.SetVisibility(shown ? ESlateVisibility.Visible : ESlateVisibility.Collapsed);
        if (shown) FollowGrid();
    }

    /// <summary>Keeps the floating row across the bottom of the item grid, as wide as it.</summary>
    void FollowGrid()
    {
        if (row == null || grid == null || !UKismetSystemLibrary.IsValid(grid)) return;
        var geometry = grid.GetCachedGeometry();
        var size = USlateBlueprintLibrary.GetLocalSize(geometry);
        if (size.X <= 0) return;
        var height = row.GetDesiredSize().Y;
        if (height <= 0) height = 130;
        USlateBlueprintLibrary.LocalToViewport(this, geometry, new FVector2D { X = 0, Y = size.Y }, out var pixel, out var corner);
        row.SetDesiredSizeInViewport(new FVector2D { X = size.X, Y = height });
        row.SetPositionInViewport(new FVector2D { X = corner.X, Y = corner.Y - height }, false);
    }

    void PlaceRow(UUserWidget collectibles)
    {
        if (row == null) return;
        rowIn = collectibles;
        row.RemoveFromParent();
        grid = Find(collectibles.WidgetTree?.RootWidget, ItemGridName);
        // The capes scroll in a list inside the item grid: the row goes at its end, under the last cape.
        if (grid is UUserWidget gridWidget)
        {
            if (FindScrollBox(gridWidget.WidgetTree?.RootWidget) is UScrollBox scroll)
            {
                floating = false;
                (scroll.AddChild(row) as UScrollBoxSlot)?.SetPadding(new FMargin { Top = 8 });
                Log.Write($"Skin row added to the Collectibles screen, at the end of {UKismetSystemLibrary.GetObjectName(scroll)}");
                return;
            }
        }
        // Or beside the item grid, past its scrollbar: in the next cell, at the bottom, level with the scrollbar's end.
        if (grid?.GetParent() is UGridPanel cells && grid.Slot is UGridSlot gridCell)
        {
            floating = false;
            var place = cells.AddChildToGrid(row, gridCell.Row, gridCell.Column + gridCell.ColumnSpan);
            place?.SetRowSpan(gridCell.RowSpan);
            place?.SetLayer(gridCell.Layer + 10);
            place?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Left);
            place?.SetVerticalAlignment(EVerticalAlignment.VAlign_Bottom);
            place?.SetPadding(new FMargin { Left = RowLeft, Bottom = RowBottom });
            Log.Write($"Skin row added to the Collectibles screen, beside the item grid's scrollbar");
            return;
        }
        // Or the nearest list the item grid is in: the row goes at its end.
        var parent = grid?.GetParent();
        while (parent != null && parent is not UVerticalBox) parent = parent.GetParent();
        if (parent is UVerticalBox list)
        {
            floating = false;
            list.AddChildToVerticalBox(row)?.SetPadding(new FMargin { Top = 8 });
            Log.Write($"Skin row added to the Collectibles screen, in {UKismetSystemLibrary.GetObjectName(list)}");
            return;
        }
        floating = true;
        row.ShowAt(new FVector2D(), new FVector2D(), ScreenWidget.AboveGameUI);
        FollowGrid();
        Log.Write($"Skin row shown over the Collectibles screen (item grid {(grid == null ? "not found" : "not in a list")})");
    }

    /// <summary>The first scroll box under a widget, or null.</summary>
    static UScrollBox? FindScrollBox(UWidget? root)
    {
        var queue = new List<UWidget>();
        if (root != null) queue.Add(root);
        for (int i = 0; i < queue.Count && i < 512; i++)
        {
            if (queue[i] is UScrollBox scroll) return scroll;
            if (queue[i] is UPanelWidget panel)
                for (int c = 0; c < panel.GetChildrenCount(); c++)
                {
                    var child = panel.GetChildAt(c);
                    if (child != null) queue.Add(child);
                }
        }
        return null;
    }

    /// <summary>The first widget with a name under a widget, or null.</summary>
    static UWidget? Find(UWidget? root, string name)
    {
        var queue = new List<UWidget>();
        if (root != null) queue.Add(root);
        for (int i = 0; i < queue.Count && i < 512; i++)
        {
            if (UKismetSystemLibrary.GetObjectName(queue[i]) == name) return queue[i];
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
