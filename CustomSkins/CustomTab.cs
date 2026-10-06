using System.Collections.Generic;
using NeoRune;
using UE.Angelscript;
using UE.CoreUObject;
using UE.Engine;
using UE.GameplayTags;
using UE.MainMenu;
using UE.SlateCore;
using UE.SpicewoodUI;
using UE.UMG;

namespace CustomSkins;

/// <summary>
/// The Custom tab in the Collectibles screen's tab bar, shared with CustomCapes (whichever mod gets there first adds
/// it, with the game's chestplate icon). Its page is one scrolling panel, also shared, laid over the game's grid of
/// items, with a section from each mod in it; while the tab is chosen the game's items are hidden and the panel shows.
/// Its own object, not the ModActor: callbacks from the game into an actor with the mod settings interface crash it.
/// </summary>
public class CustomTab : UObject
{
    public const string Tag = "SW.UI.Collectibles.CustomCosmetics";
    const string Label = "Custom";
    // The item grid's header while the tab is chosen (the game would name one of its own categories).
    const string Header = "Custom";
    // The item grid's list of items, under its header and tab bar.
    const string ListName = "ItemGridListContainer";
    // The shared panel's class name: CustomCapes' class has the same name.
    const string PanelClass = "CustomCosmeticsPanel";
    // Where the panel goes until the game's grid has been measured: under the item grid's header and tab bar.
    const float DefaultTop = 150;
    // The tab's icon: the game's own, for the chest armour category.
    const string IconTexture = "/OreUI/UI/Icons/Categories/T_UI_Icon_Category_Chest.T_UI_Icon_Category_Chest";

    USpicewoodTabListWidget? tabs;
    USpicewoodTabListWidget? subscribed;
    UWidget? grid;
    UWidget? list;
    ESlateVisibility listVisibility;
    // The game's grid of items: the panel goes where it is, with boxes its size.
    UTileView? tiles;
    UScrollBox? panel;
    UTexture2D? icon;
    USpicewoodButtonBase? iconOn;
    bool chosen;
    bool reported;
    float tileWidth;
    float tileHeight;
    int columns;

    /// <summary>Whether the Custom tab is the one shown.</summary>
    public bool Chosen => chosen;
    /// <summary>The size of a box in the game's grid, gap included (0 until it has been measured).</summary>
    public float TileWidth => tileWidth;
    public float TileHeight => tileHeight;
    /// <summary>How many boxes go across the game's grid (0 until it has been measured).</summary>
    public int Columns => columns;

    public static CustomTab? Create(UObject owner) =>
        UGameplayStatics.SpawnObject(Unreal.ClassOf<CustomTab>(), owner) as CustomTab;

    /// <summary>
    /// Puts a mod's section in the shared panel over the item grid (a new grid each time the screen is made): first, or
    /// after the other mod's. False if the item grid isn't in a grid panel.
    /// </summary>
    public bool Attach(UWidget itemGrid, UWidget section, bool first)
    {
        if (itemGrid.GetParent() is not UGridPanel cells || itemGrid.Slot is not UGridSlot cell) return false;
        grid = itemGrid;
        panel = null;
        for (int c = 0; c < cells.GetChildrenCount(); c++)
        {
            var child = cells.GetChildAt(c);
            if (child is UScrollBox box && ClassName(child).Contains(PanelClass))
            {
                panel = box;
                break;
            }
        }
        if (panel == null)
        {
            panel = UGameplayStatics.SpawnObject(Unreal.ClassOf<CustomCosmeticsPanel>(), cells) as CustomCosmeticsPanel;
            if (panel == null) return false;
            var place = cells.AddChildToGrid(panel, cell.Row, cell.Column);
            place?.SetRowSpan(cell.RowSpan);
            place?.SetColumnSpan(cell.ColumnSpan);
            place?.SetLayer(cell.Layer + 10);
            place?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
            place?.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
            place?.SetPadding(new FMargin { Top = DefaultTop });
        }
        section.RemoveFromParent();
        if (first)
        {
            var others = new List<UWidget>();
            for (int c = 0; c < panel.GetChildrenCount(); c++)
            {
                var child = panel.GetChildAt(c);
                if (child != null) others.Add(child);
            }
            panel.ClearChildren();
            panel.AddChild(section);
            foreach (var other in others) panel.AddChild(other);
        }
        else panel.AddChild(section);

        tabs = FindTabs(itemGrid);
        // Without the list, the whole grid goes (the tab bar with it, but the tab keys still work).
        list = Find(itemGrid, ListName);
        if (list == null) list = itemGrid;
        listVisibility = list.GetVisibility();
        tiles = FindTiles(list);
        iconOn = null;
        chosen = false;
        Show();
        if (tabs == null) Log.Write("No tab bar in the Collectibles item grid: the Custom tab can't be added");
        return true;
    }

    /// <summary>
    /// Call regularly while the screen shows: adds the tab once the game has added its own (again if it rebuilt them),
    /// gives it its icon, and keeps the panel over the game's grid.
    /// </summary>
    public void Keep()
    {
        Measure();
        if (tabs == null || !UKismetSystemLibrary.IsValid(tabs) || tabs.RegisteredTabs.Count == 0) return;
        var tag = new FGameplayTag { TagName = Tag };
        tabs.GetRegisteredTabInfo(tag, out var registered);
        if (!registered && !tabs.RegisterPseudoTab(tag, Label))
        {
            Log.Write("Couldn't add the Custom tab to the Collectibles screen");
            tabs = null;
            return;
        }
        if (subscribed != tabs)
        {
            tabs.OnTabSelected += Selected;
            subscribed = tabs;
        }
        GiveIcon(tag);
        // The game may show its list again, or name its own category, on its own.
        if (chosen) Show();
    }

    void Selected(FGameplayTag tab)
    {
        chosen = tab.TagName.ToString() == Tag;
        Show();
    }

    void Show()
    {
        if (list != null && UKismetSystemLibrary.IsValid(list)) list.SetVisibility(chosen ? ESlateVisibility.Hidden : listVisibility);
        if (panel != null && UKismetSystemLibrary.IsValid(panel)) panel.SetVisibility(chosen ? ESlateVisibility.Visible : ESlateVisibility.Collapsed);
        if (chosen && tabs is UAS_CategorySelector selector) selector.CategoryHeaderText?.SetText(Header);
    }

    /// <summary>
    /// Puts the panel where the game's grid of items is and takes its box size. The grid keeps its place while hidden,
    /// so this works on the Custom tab too.
    /// </summary>
    void Measure()
    {
        if (tiles == null || grid == null || panel == null || !UKismetSystemLibrary.IsValid(tiles) || !UKismetSystemLibrary.IsValid(grid)) return;
        var tileGeometry = tiles.GetCachedGeometry();
        var size = USlateBlueprintLibrary.GetLocalSize(tileGeometry);
        var gridGeometry = grid.GetCachedGeometry();
        var gridSize = USlateBlueprintLibrary.GetLocalSize(gridGeometry);
        if (size.X <= 0 || size.Y <= 0 || gridSize.X <= 0 || gridSize.Y <= 0) return;
        var topLeft = USlateBlueprintLibrary.AbsoluteToLocal(gridGeometry, USlateBlueprintLibrary.LocalToAbsolute(tileGeometry, new FVector2D()));
        var bottomRight = USlateBlueprintLibrary.AbsoluteToLocal(gridGeometry, USlateBlueprintLibrary.LocalToAbsolute(tileGeometry, size));
        (panel.Slot as UGridSlot)?.SetPadding(new FMargin
        {
            Left = (float)topLeft.X,
            Top = (float)topLeft.Y,
            Right = (float)(gridSize.X - bottomRight.X),
            Bottom = (float)(gridSize.Y - bottomRight.Y),
        });
        var width = tiles.GetEntryWidth();
        var height = tiles.GetEntryHeight();
        if (width <= 0 || height <= 0) return;
        tileWidth = width;
        tileHeight = height;
        columns = (int)(size.X / width);
        if (columns < 1) columns = 1;
    }

    /// <summary>Gives the tab's button the game's chestplate icon where the game's tabs have their category's icon.</summary>
    void GiveIcon(FGameplayTag tag)
    {
        if (tabs == null) return;
        var info = tabs.GetRegisteredTabInfo(tag, out var registered);
        var button = info.CreatedButton;
        if (!registered || button == null || button == iconOn) return;
        if (button is not UAS_SpicewoodButtonTabActionMinimal minimal || minimal.CategoryIcon == null)
        {
            if (!reported) Log.Write($"The Custom tab's button is a {ClassName(button)}: no icon put on it");
            reported = true;
            return;
        }
        if (icon == null)
            icon = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(IconTexture))) as UTexture2D;
        if (icon == null) return;
        minimal.CategoryIcon.SetBrushResourceObject(icon);
        iconOn = button;
    }

    /// <summary>
    /// Copies a folder's path, to paste into File Explorer and add files to (the game can't open Explorer: its links all
    /// open in the browser). The Xbox app version's game folder is reported under WindowsApps, where Explorer can't add
    /// files: the same folder in the game's XboxGames install, on any drive, is copied instead.
    /// </summary>
    public static void CopyFolder(string folder)
    {
        var path = folder;
        int apps = folder.IndexOf("/WindowsApps/");
        if (apps >= 0)
        {
            // Past the package's folder: Dungeons/Content/Paks/~mods/...
            var rest = folder.Substring(apps + 13);
            int slash = rest.IndexOf("/");
            if (slash >= 0)
            {
                rest = rest.Substring(slash + 1);
                var drives = new List<string> { "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z" };
                foreach (var drive in drives)
                {
                    var candidate = drive + ":/XboxGames/Minecraft Dungeons 2/Content/" + rest;
                    if (path == folder && UBlueprintPathsLibrary.DirectoryExists(candidate)) path = candidate;
                }
            }
        }
        UMainMenuFunctionLibrary.CopyToClipboard(path.Replace("/", "\\"));
    }

    /// <summary>An object's class, as its path.</summary>
    static string ClassName(UObject widget) =>
        UKismetSystemLibrary.Conv_SoftClassReferenceToString(UKismetSystemLibrary.Conv_ClassToSoftClassReference(UGameplayStatics.GetObjectClass(widget)));

    /// <summary>The first tab bar under a widget, looking inside the widgets it's made of too.</summary>
    static USpicewoodTabListWidget? FindTabs(UWidget root)
    {
        var all = Under(root);
        foreach (var widget in all)
            if (widget is USpicewoodTabListWidget found) return found;
        return null;
    }

    /// <summary>The first grid of tiles under a widget, looking inside the widgets it's made of too.</summary>
    static UTileView? FindTiles(UWidget root)
    {
        var all = Under(root);
        foreach (var widget in all)
            if (widget is UTileView found) return found;
        return null;
    }

    /// <summary>The first widget with a name under a widget, looking inside the widgets it's made of too.</summary>
    static UWidget? Find(UWidget root, string name)
    {
        var all = Under(root);
        foreach (var widget in all)
            if (UKismetSystemLibrary.GetObjectName(widget) == name) return widget;
        return null;
    }

    /// <summary>A widget and the widgets under it, nearest first. (A list passed to a method is a copy: built here.)</summary>
    static List<UWidget> Under(UWidget root)
    {
        var queue = new List<UWidget> { root };
        for (int i = 0; i < queue.Count && i < 1024; i++)
        {
            if (queue[i] is UUserWidget user)
            {
                var inner = user.WidgetTree?.RootWidget;
                if (inner != null) queue.Add(inner);
            }
            if (queue[i] is UPanelWidget panel)
                for (int c = 0; c < panel.GetChildrenCount(); c++)
                {
                    var child = panel.GetChildAt(c);
                    if (child != null) queue.Add(child);
                }
        }
        return queue;
    }
}

/// <summary>The Custom tab's page: a scrolling list of the mods' sections. Found by its class name.</summary>
public class CustomCosmeticsPanel : UScrollBox
{
}
