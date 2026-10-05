using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.SlateCore;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// A line at the bottom left of the main menu: how many mods run, and a mod that crashed the game if one did. The Main
/// Menu Label setting turns it off.
/// </summary>
public class MenuLabel : ScreenWidget
{
    ModManager? manager;
    UTextBlock? text;

    public static MenuLabel? Show(ModManager manager)
    {
        var label = UWidgetBlueprintLibrary.Create(manager, Unreal.ClassOf<MenuLabel>(), World.PlayerController(manager)) as MenuLabel;
        if (label == null) return null;
        label.manager = manager;
        var tree = Ui.Tree(label);
        label.text = Ui.Text(tree, "", 13);
        if (tree == null || label.text == null) return null;
        label.text.SetShadowOffset(new FVector2D { X = 1, Y = 1 });
        tree.RootWidget = label.text;
        label.SetVisibility(ESlateVisibility.HitTestInvisible);
        label.Refresh();
        label.ShowAt(new FVector2D { X = 16, Y = -4 }, new FVector2D { X = 0, Y = 1 }, ScreenWidget.UnderGameUI);
        return label;
    }

    public void Refresh()
    {
        if (manager == null || text == null) return;
        var line = $"BetterBlueprintLoader {manager.Version}: {manager.Running()} of {manager.Mods.Count} mods running";
        if (manager.Notice != "") line = manager.Notice + "\n" + line;
        text.SetText(line);
        text.SetColorAndOpacity(Ui.SlateColor(manager.Notice != "" ? Ui.Color(1, 0.75f, 0.3f, 1) : Ui.Color(0.85f, 0.85f, 0.85f, 0.8f)));
    }
}
