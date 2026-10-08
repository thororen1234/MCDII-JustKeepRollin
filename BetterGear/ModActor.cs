using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;
using UE.SlateCore;
using UE.UMG;

namespace BetterGear;

/// <summary>
/// Better Armor &amp; Weapons: hide your armor and weapons, or wear any armor piece or weapon you've found (salvaged ones
/// too) as a look over what you have on, picked in the inventory's Collectibles screen, Custom tab. Only you see it:
/// everyone else sees the gear you wear. The settings are in BetterBlueprintLoader's Mods tab.
/// </summary>
[ModSetting.Heading("Looks")]
[ModSetting.Toggle(EverythingSetting, "Show All Gear", Default = false,
    Description = "Lists every armor piece and weapon in the game as a look, not only the ones you've found.")]
[ModSetting.Heading("Keys")]
[ModSetting.Keybind(ArmorSetting, "Hide Armor", Default = DefaultArmorKey,
    Description = "Hides all your armor, or shows it again (with its looks).")]
public class ModActor : AActor, ISettingsEvents
{
    const string EverythingSetting = "show_all";
    const string ArmorSetting = "armor_key";
    const string DefaultArmorKey = "F2";
    // The game puts its gear back when the character or gear changes: this often, the looks go back on (hidden parts
    // are kept hidden every frame).
    const float CheckInterval = 0.25f;
    const float MenuInterval = 0.25f;
    // The inventory's Collectibles screen (capes and pets), and the screen around it, which is active while it shows.
    const string CollectiblesClass = "W_Collectibles.W_Collectibles_C";
    const string CollectiblesScreenClass = "W_Collectibles_Activatable.W_Collectibles_Activatable_C";
    // The Collectibles item grid: the section goes in the Custom tab laid over it.
    const string ItemGridName = "ItemGrid";

    GearLook? gear;
    // The Hide and Look options in the inventory's right-click menu of equipped gear.
    GearPrompt? prompt;
    // When Check and WatchMenu run next (real time): from the tick, which keeps running while menus pause the game.
    // Not PausableTimers: calling a ModActor's methods from one crashed the game (CustomSkins, 2026-10-05).
    double nextCheck;
    double nextMenuWatch;
    GearMenu? menu;
    UUserWidget? menuIn;
    bool menuShown;
    bool started;
    CustomTab? tab;
    bool tabbed;
    // The Show All Gear setting; BetterBlueprintLoader only sends it when it isn't on its default.
    bool everything;

    FKey armorKey = new FKey { KeyName = DefaultArmorKey };
    FKey armorKey2 = new FKey();

    protected override void ReceiveBeginPlay()
    {
        // Menus can pause the game, and the inventory, which shows the character, is one.
        SetTickableWhenPaused(true);
    }

    /// <summary>
    /// Whether another copy of the mod runs in this level (a second mod loader spawns one too). Two copies would each
    /// change the same parts and take the other's changes for the game's: the newcomer leaves.
    /// </summary>
    bool HasTwin()
    {
        foreach (var other in World.FindAll(this, Unreal.ClassOf<ModActor>()))
            if (other != null && other != this && UKismetSystemLibrary.IsValid(other)) return true;
        return false;
    }

    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey)
    {
        if (id != ArmorSetting) return;
        armorKey = key;
        armorKey2 = secondaryKey;
    }

    public void OnSettingsReset()
    {
        armorKey = new FKey { KeyName = DefaultArmorKey };
        armorKey2 = new FKey();
        SetEverything(false);
    }

    public void OnSettingChanged(string id, string value)
    {
        if (id == EverythingSetting) SetEverything(value == "true");
    }

    public void OnButtonPressed(string id) { }

    void SetEverything(bool on)
    {
        everything = on;
        menu?.ShowEverything(on);
        prompt?.ShowEverything(on);
    }

    static bool Pressed(APlayerController controller, FKey key, FKey secondary) =>
        controller.WasInputKeyJustPressed(key) || controller.WasInputKeyJustPressed(secondary);

    public override void ReceiveTick(float deltaSeconds)
    {
        if (!started)
        {
            if (HasTwin())
            {
                FileLog.Write("Another copy of Better Armor & Weapons is running in this level (is a second mod loader installed?): this one stopped");
                K2_DestroyActor();
                return;
            }
            started = true;
            gear = GearLook.Create(this);
            if (gear != null) prompt = GearPrompt.Create(this, gear, everything);
        }
        prompt?.Tick();
        gear?.KeepHidden();
        var now = World.RealTime(this);
        if (now >= nextCheck)
        {
            nextCheck = now + CheckInterval;
            gear?.Check();
        }
        if (now >= nextMenuWatch)
        {
            nextMenuWatch = now + MenuInterval;
            WatchMenu();
            prompt?.Scan();
        }
        var controller = World.PlayerController(this);
        if (controller == null || gear == null) return;
        if (Pressed(controller, armorKey, armorKey2))
        {
            gear.ToggleArmor();
            menu?.Rescan();
        }
    }

    /// <summary>Puts the section in the Collectibles screen's Custom tab when it opens, with the gear worn and found now.</summary>
    void WatchMenu()
    {
        if (gear == null) return;
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
            if (menu == null)
            {
                // Kept in the field before it's set up: setting it up loads the game's assets, which can let the
                // garbage collector run.
                menu = GearMenu.Create(this);
                if (menu != null && !menu.Setup(gear, everything)) menu = null;
            }
            else menu.Refresh();
        }
        menuShown = shown;
        if (menu == null) return;
        if (collectibles != null && collectibles != menuIn) PlaceMenu(collectibles);
        if (shown && tabbed && tab != null)
        {
            tab.Keep();
            menu.Fit(tab.TileWidth, tab.TileHeight, tab.Columns);
            if (tab.Chosen) menu.Rescan();
        }
    }

    void PlaceMenu(UUserWidget collectibles)
    {
        if (menu == null) return;
        menuIn = collectibles;
        menu.RemoveFromParent();
        menu.SetVisibility(ESlateVisibility.Visible);
        tabbed = false;
        var grid = Find(collectibles.WidgetTree?.RootWidget, ItemGridName);

        // The Custom tab's page, laid over the item grid: after CustomSkins' and CustomCapes' sections.
        if (grid?.GetParent() is UGridPanel)
        {
            if (tab == null) tab = CustomTab.Create(this);
            if (tab != null && tab.Attach(grid, menu, false))
            {
                tabbed = true;
                FileLog.Write("Armor & Weapons section added to the Collectibles screen's Custom tab");
                return;
            }
        }
        // Or the nearest list the item grid is in: the section goes at its end.
        var parent = grid?.GetParent();
        while (parent != null && parent is not UVerticalBox) parent = parent.GetParent();
        if (parent is UVerticalBox list)
        {
            list.AddChildToVerticalBox(menu)?.SetPadding(new FMargin { Top = 8 });
            FileLog.Write($"Armor & Weapons section added to the Collectibles screen, in {UKismetSystemLibrary.GetObjectName(list)}");
            return;
        }
        FileLog.Write($"No place for the Armor & Weapons section in the Collectibles screen (item grid {(grid == null ? "not found" : "not in a list")})");
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

[Asset("/Game/Mods/BlueprintLoader/BPI_ModSettings")]
public interface ISettingsEvents
{
    void OnButtonPressed(string Id);
    void OnKeybindChanged(string Id, FKey Key, FKey SecondaryKey);
    void OnSettingChanged(string Id, string Value);
    void OnSettingsReset();
}
