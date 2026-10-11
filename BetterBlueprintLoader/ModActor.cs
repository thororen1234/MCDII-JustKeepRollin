using NeoRune;
using UE.Engine;
using UE.InputCore;

namespace BetterBlueprintLoader;

/// <summary>
/// NeoRuneExtended packs a mod only with a ModActor. The loader is LoaderComponent, and it doesn't start itself: this is
/// here for its settings page in the Mods tab, which ModManager reads directly.
/// </summary>
[ModSetting.Heading("Main menu")]
[ModSetting.Toggle(ModManager.MenuLabelSetting, "Main Menu Label", Default = true,
    Description = "Shows how many mods are running, and a mod that crashed the game, at the bottom left of the main menu.")]
[ModSetting.Toggle(ModManager.MenuButtonSetting, "Main Menu Mods Button", Default = true,
    Description = "Adds a MODS button to the main menu, which opens this tab.")]
[ModSetting.Heading("Mods")]
[ModSetting.EventButton(ModManager.RestartSetting, "Restart Mods", ButtonText = "Restart",
    Description = "Stops every running mod and starts them again, in their order. Handy when making mods.")]
[ModSetting.Keybind(ModManager.RestartKeySetting, "Restart Mods Key", Default = "F12",
    Description = "Restarts the mods from anywhere, like the Restart button.")]
[ModSetting.Keybind(ModManager.OpenKeySetting, "Open Mods Key", Default = "F10",
    Description = "Opens this tab from anywhere: the main menu, or while playing.")]
[ModSetting.Select(ModManager.OpenToSetting, "Open To", "Mods List",
    Description = "Where the Open Mods Key opens the tab: the list of mods, or straight to a mod's page.")]
[ModSetting.Heading("Updates")]
[ModSetting.Toggle(ModManager.UpdateCheckSetting, "Check For Updates", Default = true,
    Description = "Checks Nexus Mods for newer versions of your mods (once each time the game starts) and shows them in this tab and on the main menu's MODS button. Only mods that say where they are on Nexus Mods can be checked.")]
[ModSetting.Text("If the game crashes while a mod is starting or getting its saved settings, BetterBlueprintLoader turns that mod off and tells you on the main menu. Turn it back on here when the mod is updated. If a mod's settings page crashes the game, that page is shown in a plain look from then on.")]
#pragma warning disable NR0001
public class ModActor : AActor, ISettingsEvents
#pragma warning restore NR0001
{
    public void OnButtonPressed(string id) { }
    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey) { }
    public void OnSettingChanged(string id, string value) { }
    public void OnSettingsReset() { }
}
