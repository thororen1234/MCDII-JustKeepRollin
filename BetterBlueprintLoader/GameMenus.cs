using System.Collections.Generic;
using NeoRune;
using UE.Angelscript;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.GameplayTags;
using UE.GameSettings;
using UE.SlateCore;
using UE.SpicewoodUI;
using UE.SWSettings;
using UE.UIStateContainer;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// The loader's places in the game's own menus: a Mods tab in the settings (main menu and in game), showing the
/// <see cref="ModsPage"/> in place of the settings list, and a MODS button on the main menu that opens it.
/// </summary>
public class GameMenus : AActor
{
    const string SettingsScreenClass = "/SpicewoodSettings/Spicewood/UI/SettingsScreen/W_SettingsScreen_Activatable.W_SettingsScreen_Activatable_C";
    // The settings' tabs are named by gameplay tags, and only tags the game has work: the Mods tab uses the settings'
    // own parent tag, which none of the game's tabs uses.
    const string TabTag = "SW.UI.Settings";
    const string TabLabel = "Mods";
    const string ButtonLabel = "MODS";
    // The main menu button the MODS button goes after (CharactersButton if that one's missing).
    const string MenuButtonAfter = "DLCButton";

    ModManager? manager;
    WidgetWatcher? settingsScreens;
    WidgetWatcher? lobbies;
    PausableTimer? poll;
    // Over the game's UI: the page is on the viewport, not in the settings screen. Putting a widget anywhere inside the
    // settings screen crashes the game, so the page lies over its settings panel, which is made invisible meanwhile.
    // Above the game's whole UI on the player's screen (CommonUI's layout is there too; lower ones are covered by its
    // backgrounds).
    const int PageLayer = 5000;

    // The settings screen on screen now: its tab list, and its settings panel (list and details), which the Mods page
    // covers while its tab is chosen. The panel's settings list and the text in its details frame are hidden meanwhile
    // (the frame stays: the page's details go in it), or the whole panel when they aren't found.
    UUserWidget? settingsScreen;
    USpicewoodTabListWidget? tabs;
    USpicewoodTabListWidget? subscribedTabs;
    // The Mods tab's button: clicked while its page shows, the page goes back to the list (the tabs don't say so, as
    // the tab is already chosen).
    UCommonButtonBase? tabButton;
    bool tabClicked;
    UWidget? settingsPanel;
    UWidget? detailsView;
    ESlateVisibility panelVisibility;
    ESlateVisibility listVisibility;
    ESlateVisibility detailsVisibility;
    bool pageShown;
    ModsPage? page;
    GameLook? look;
    UGameSettingListView? settingsList;
    // The MODS button was clicked: open the settings on the next Poll, then select the tab once they're open.
    bool openSettings;
    bool openTab;
    // A tab was chosen (the Mods tab or another, named), done on the next Poll: the tabs say so from the middle of the
    // game's input code, where making the page or saving isn't safe.
    bool tabChosen;
    bool modsTabChosen;
    string otherTab = "";
    UAS_InitialLobbyScreen? lobby;
    UCommonButtonBase? menuButton;

    public static GameMenus? Start(ModManager manager)
    {
        var menus = World.Spawn(manager, Unreal.ClassOf<GameMenus>(), new FVector()) as GameMenus;
        if (menus == null) return null;
        menus.manager = manager;
        menus.Watch();
        return menus;
    }

    void Watch()
    {
        var settingsClass = Unreal.LoadClass<UUserWidget>(SettingsScreenClass);
        if (settingsClass == null) Log.Write("The game's settings screen wasn't found: no Mods tab");
        else
        {
            settingsScreens = WidgetWatcher.Start(this, settingsClass, 0.1f);
            if (settingsScreens != null) settingsScreens.Found += OnSettingsScreen;
        }
        lobbies = WidgetWatcher.Start(this, Unreal.ClassOf<UAS_InitialLobbyScreen>(), 0.25f);
        if (lobbies != null) lobbies.Found += OnLobby;
        // Menus pause the game: a timer that runs anyway keeps the tab there and labels what was added.
        poll = PausableTimer.Start(this, 0.1f, loop: true);
        if (poll != null) poll.Fired += Poll;
    }

    /// <summary>Everything the page shows may have changed: it's made again on the next Poll.</summary>
    public void Refresh() => page?.RefreshLater();

    public void SettingChanged(string folder, string id, string value) => page?.SettingChanged(folder);

    void Note(string line) => manager?.Note(line);

    void OnSettingsScreen(UUserWidget screen)
    {
        HidePage("another settings screen opened");
        settingsScreen = screen;
        look = GameLook.From(this, screen, manager != null && manager.PlainPage);
        if (look != null) Note(look.Load());
        settingsList = GameUI.Find(screen, "ListView_Settings") as UGameSettingListView;
        detailsView = GameUI.Find(screen, "Details_Settings");
        tabs = GameUI.Find(screen, "TopSettingsTabs") as USpicewoodTabListWidget;
        settingsPanel = GameUI.Find(screen, "Settings_Panel");
        page?.RemoveFromParent();
        page = null;
        pageShown = false;
        tabChosen = false;
        if (tabs == null || settingsPanel == null) Note($"No place for the Mods tab in the settings: tabs {tabs != null}, panel {settingsPanel != null}");
    }

    void Poll()
    {
        // The settings closed (or aren't on screen): the page goes too.
        if (settingsScreen != null && !UKismetSystemLibrary.IsValid(settingsScreen))
        {
            HidePage("the settings closed");
            settingsScreen = null;
            settingsList = null;
            settingsPanel = null;
            detailsView = null;
            tabs = null;
            tabChosen = false;
        }
        else if (settingsScreen != null && !settingsScreen.IsVisible()) HidePage($"the settings screen isn't visible ({settingsScreen.GetVisibility()})");
        if (tabs != null && !UKismetSystemLibrary.IsValid(tabs)) tabs = null;
        if (settingsPanel != null && !UKismetSystemLibrary.IsValid(settingsPanel)) settingsPanel = null;
        if (page != null && !UKismetSystemLibrary.IsValid(page))
        {
            Note("Mods tab: the page was destroyed");
            page = null;
            pageShown = false;
        }
        if (tabChosen)
        {
            tabChosen = false;
            if (modsTabChosen)
            {
                // Chosen again while a mod's page shows: back to the list.
                if (pageShown) page?.BackToList();
                ShowPage();
            }
            else HidePage($"tab {otherTab} chosen");
        }
        if (tabClicked)
        {
            tabClicked = false;
            if (pageShown) page?.BackToList();
        }
        if (pageShown && page != null)
        {
            // The page is never added to the screen again while it's shown: adding a widget that's already there crashed
            // the game (it can say it isn't there right after being added).
            PlacePage();
            page.Update();
            if (!checkedShown && World.RealTime(this) - shownAt > 1)
            {
                checkedShown = true;
                Note($"Mods tab: a second later, page on the viewport {page.IsInViewport()}, visible {page.IsVisible()}");
            }
        }
        if (openSettings)
        {
            openSettings = false;
            OpenSettings();
        }
        if (tabs != null) KeepTab();
        if (lobby != null) KeepMenuButton();
        // What this did is in the log now: if the game crashes next, the log says how far it got.
        manager?.Flush();
    }

    /// <summary>
    /// Adds the Mods tab once the game has registered its own tabs (again, if it rebuilds them), as a pseudo tab: a tab
    /// button with a label and no content of the game's. Then opens it, after the MODS button.
    /// </summary>
    void KeepTab()
    {
        if (tabs == null || tabs.RegisteredTabs.Count == 0) return;
        var tag = GameUI.Tag(TabTag);
        var info = tabs.GetRegisteredTabInfo(tag, out var registered);
        if (registered && info.CreatedButton != null && info.CreatedButton != tabButton)
        {
            tabButton = info.CreatedButton;
            // A chosen tab takes no clicks unless told to.
            tabButton.SetIsInteractableWhenSelected(true);
            tabButton.OnButtonBaseClicked += OnModsTabClicked;
        }
        if (!registered)
        {
            if (!tabs.RegisterPseudoTab(tag, TabLabel))
            {
                Note("Couldn't add the Mods tab to the settings");
                tabs = null;
                return;
            }
            if (subscribedTabs != tabs)
            {
                tabs.OnTabSelected += OnTabSelected;
                subscribedTabs = tabs;
            }
            return;
        }
        if (openTab)
        {
            openTab = false;
            tabs.SelectTab(tag, false);
        }
    }

    void OnModsTabClicked(UCommonButtonBase? button)
    {
        if (pageShown) tabClicked = true;
    }

    void OnTabSelected(FGameplayTag tabId)
    {
        Note("Tab chosen: " + tabId.TagName.ToString());
        tabChosen = true;
        modsTabChosen = tabId.TagName.ToString() == TabTag;
        otherTab = tabId.TagName.ToString();
    }

    /// <summary>
    /// Shows the Mods page over the settings panel (its list and details), which is made invisible and doesn't take
    /// clicks meanwhile. Nothing is added to the settings screen: the page is on the viewport, kept on the panel's place.
    /// </summary>
    void ShowPage()
    {
        if (settingsPanel == null || manager == null) return;
        manager.MakingPage();
        // The rows the last tab showed: the slider's look to copy (the game has no style asset for it).
        if (look != null) Note(look.Capture(settingsList));
        if (page == null)
        {
            if (look == null)
            {
                Note("The settings screen isn't the one expected: no Mods page");
                return;
            }
            page = ModsPage.Create(manager, look);
            if (page == null)
            {
                Note("Mods tab: the page couldn't be made");
                return;
            }
        }
        if (!pageShown)
        {
            if (SplitPanel())
            {
                listVisibility = settingsList!.GetVisibility();
                detailsVisibility = detailsView!.GetVisibility();
                Cover(settingsList);
                Cover(detailsView);
            }
            else
            {
                panelVisibility = settingsPanel.GetVisibility();
                Cover(settingsPanel);
            }
            var added = AddPage();
            pageShown = true;
            shownAt = World.RealTime(this);
            checkedShown = false;
            Note($"Mods tab: shown, on the player's screen {added}");
            PlacePage();
        }
        page.Refresh();
    }

    /// <summary>
    /// Puts the page on the player's screen, over the game's UI (which is there too, at a lower order). Adding it to the
    /// viewport doesn't work here: it never shows.
    /// </summary>
    bool AddPage()
    {
        if (page == null) return false;
        page.SetOwningPlayer(World.PlayerController(this));
        return page.AddToPlayerScreen(PageLayer);
    }

    // When the page was last shown, and whether it has been checked on since.
    double shownAt;
    bool checkedShown;

    /// <summary>Whether the page goes over the settings list and the details apart (else over the whole panel).</summary>
    bool SplitPanel() =>
        settingsList != null && detailsView != null && UKismetSystemLibrary.IsValid(settingsList) && UKismetSystemLibrary.IsValid(detailsView);

    /// <summary>Makes a part of the settings screen invisible and not take clicks, under the page.</summary>
    static void Cover(UWidget? widget)
    {
        widget?.SetRenderOpacity(0);
        widget?.SetVisibility(ESlateVisibility.HitTestInvisible);
    }

    static void Uncover(UWidget? widget, ESlateVisibility visibility)
    {
        if (widget == null || !UKismetSystemLibrary.IsValid(widget)) return;
        widget.SetRenderOpacity(1);
        widget.SetVisibility(visibility);
    }

    /// <summary>Takes the page off and shows the settings panel again.</summary>
    void HidePage(string why)
    {
        if (!pageShown) return;
        Note("Mods tab: page taken off: " + why);
        pageShown = false;
        if (page != null && UKismetSystemLibrary.IsValid(page)) page.RemoveFromParent();
        if (SplitPanel())
        {
            Uncover(settingsList, listVisibility);
            Uncover(detailsView, detailsVisibility);
        }
        else Uncover(settingsPanel, panelVisibility);
    }

    /// <summary>Keeps the page on the settings panel's place on screen (it can move: the screen animates in, resizes).</summary>
    void PlacePage()
    {
        if (page == null || settingsPanel == null || !UKismetSystemLibrary.IsValid(settingsPanel)) return;
        var geometry = settingsPanel.GetCachedGeometry();
        var size = USlateBlueprintLibrary.GetLocalSize(geometry);
        if (size.X <= 0 || size.Y <= 0) return;
        USlateBlueprintLibrary.LocalToViewport(this, geometry, new FVector2D(), out var topLeftPixels, out var topLeft);
        USlateBlueprintLibrary.LocalToViewport(this, geometry, size, out var bottomRightPixels, out var bottomRight);
        var placed = new FVector2D { X = bottomRight.X - topLeft.X, Y = bottomRight.Y - topLeft.Y };
        page.SetPositionInViewport(topLeft, false);
        page.SetDesiredSizeInViewport(placed);
        // The list where the game's list is, the details in its details frame; or the panel's left two thirds and the
        // rest, when they aren't found.
        if (SplitPanel() && Area(settingsList, topLeft, out var listAt, out var listSize) && Area(detailsView, topLeft, out var detailsAt, out var detailsSize))
            page.Arrange(listAt, listSize, detailsAt, detailsSize);
        else
        {
            var listWidth = placed.X * 2 / 3;
            page.Arrange(new FVector2D { X = 40, Y = 24 }, new FVector2D { X = listWidth - 80, Y = placed.Y - 48 },
                new FVector2D { X = listWidth, Y = 24 }, new FVector2D { X = placed.X - listWidth - 40, Y = placed.Y - 48 });
        }
        if (!placedLogged)
        {
            placedLogged = true;
            Note($"Mods tab: page at {topLeft.X:0},{topLeft.Y:0}, {placed.X:0} x {placed.Y:0} (panel {size.X:0} x {size.Y:0})");
        }
    }

    bool placedLogged;

    /// <summary>Where a widget is on screen, from a point (the page's top left), in the viewport's units.</summary>
    bool Area(UWidget? widget, FVector2D origin, out FVector2D at, out FVector2D size)
    {
        at = new FVector2D();
        size = new FVector2D();
        if (widget == null) return false;
        var geometry = widget.GetCachedGeometry();
        var local = USlateBlueprintLibrary.GetLocalSize(geometry);
        if (local.X <= 0 || local.Y <= 0) return false;
        USlateBlueprintLibrary.LocalToViewport(this, geometry, new FVector2D(), out var topLeftPixels, out var topLeft);
        USlateBlueprintLibrary.LocalToViewport(this, geometry, local, out var bottomRightPixels, out var bottomRight);
        at = new FVector2D { X = topLeft.X - origin.X, Y = topLeft.Y - origin.Y };
        size = new FVector2D { X = bottomRight.X - topLeft.X, Y = bottomRight.Y - topLeft.Y };
        return true;
    }

    void OnLobby(UUserWidget widget)
    {
        lobby = widget as UAS_InitialLobbyScreen;
        KeepMenuButton();
    }

    /// <summary>The Main Menu Mods Button setting changed: adds or removes the button.</summary>
    public void MenuButtonSettingChanged() => KeepMenuButton();

    /// <summary>
    /// Keeps exactly one MODS button in the main menu's buttons, after its DLC button, labelled: a copy of the menu's own
    /// buttons (same class, so the same look). Copies of the menu's buttons that aren't this one (left from before, or
    /// added by something else) are taken out, so the menu never fills up with unlabelled "LOBBY BUTTON"s. Runs often:
    /// the button puts its own label back whenever it's made again, and the menu can be rebuilt.
    /// </summary>
    void KeepMenuButton()
    {
        if (lobby == null || manager == null || !UKismetSystemLibrary.IsValid(lobby)) return;
        var after = GameUI.Find(lobby, MenuButtonAfter) ?? GameUI.Find(lobby, "CharactersButton");
        var column = after?.GetParent();
        if (after == null || column == null) return;
        bool wanted = manager.OwnToggle(ModManager.MenuButtonSetting);
        if (menuButton != null && (!UKismetSystemLibrary.IsValid(menuButton) || menuButton.GetParent() != column)) menuButton = null;
        foreach (var child in column.GetAllChildren())
            if (child != null && child != menuButton && IsAddedCopy(child, after)) child.RemoveFromParent();
        if (!wanted)
        {
            menuButton?.RemoveFromParent();
            menuButton = null;
            return;
        }
        if (menuButton == null)
        {
            menuButton = Ui.GameWidget(this, GameUI.ClassPath(after)) as UCommonButtonBase;
            if (menuButton == null) return;
            menuButton.OnButtonBaseClicked += OnMenuButton;
            GameUI.InsertNextTo(after, menuButton, true);
        }
        var label = GameUI.FindOfClass(menuButton, Unreal.ClassOf<UTextBlock>()) as UTextBlock;
        if (label != null && label.GetText().ToString() != ButtonLabel) label.SetText(ButtonLabel);
    }

    /// <summary>
    /// A copy of one of the menu's buttons made in code: the same class, and a generated name (W_LobbyButton_C_2147...),
    /// where the menu's own have names from its designer (PlayButton, DLCButton...).
    /// </summary>
    static bool IsAddedCopy(UWidget child, UWidget menuButton) =>
        GameUI.ClassPath(child) == GameUI.ClassPath(menuButton) && UKismetSystemLibrary.GetObjectName(child).Contains("_C_");

    void OnMenuButton(UCommonButtonBase? button)
    {
        Note("MODS button clicked");
        openSettings = true;
        openTab = true;
    }

    /// <summary>Opens the settings the way the game's UI does: an action sent to its UI store.</summary>
    void OpenSettings()
    {
        var store = UUIStoreSubsystemLibrary.Get()?.GetStore();
        var player = lobby != null && UKismetSystemLibrary.IsValid(lobby) ? lobby.GetOwningLocalPlayer() : null;
        if (store == null || player == null)
        {
            Note($"Couldn't open the settings: UI store {store != null}, player {player != null}");
            openTab = false;
            return;
        }
        store.Dispatch(USettingsActionLibrary.OpenSettingsScreen(store, player));
    }
}
