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
[ModSetting.Text("If the game crashes while a mod is starting, BetterBlueprintLoader turns that mod off and tells you on the main menu. Turn it back on here when the mod is updated.")]
#pragma warning disable NR0001
public class ModActor : AActor, ISettingsEvents
#pragma warning restore NR0001
{
    public void OnButtonPressed(string id) { }
    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey) { }
    public void OnSettingChanged(string id, string value) { }
    public void OnSettingsReset() { }
}
