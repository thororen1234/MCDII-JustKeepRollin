using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>A line on the main menu: how many mods run, and a mod that crashed the game if one did.</summary>
public class MenuLabel : ScreenWidget
{
    ModManager? manager;
    UTextBlock? text;

    public static MenuLabel? Show(ModManager manager)
    {
        var label = UWidgetBlueprintLibrary.Create(manager, Unreal.ClassOf<MenuLabel>(), World.PlayerController(manager)) as MenuLabel;
        if (label == null) return null;
        label.manager = manager;
        var tree = label.WidgetTree;
        if (tree == null)
        {
            tree = UGameplayStatics.SpawnObject(Unreal.ClassOf<UWidgetTree>(), label) as UWidgetTree;
            label.WidgetTree = tree;
        }
        if (tree == null) return null;
        label.text = UGameplayStatics.SpawnObject(Unreal.ClassOf<UTextBlock>(), tree) as UTextBlock;
        if (label.text == null) return null;
        var font = UMinimapHelpersLibrary.GetDefaultFont();
        if (font.FontObject != null)
        {
            font.Size = 13;
            label.text.SetFont(font);
        }
        label.text.SetShadowOffset(new FVector2D { X = 1, Y = 1 });
        tree.RootWidget = label.text;
        // Clicks go through it to the menu.
        label.SetVisibility(ESlateVisibility.HitTestInvisible);
        label.Refresh();
        label.ShowAt(new FVector2D { X = 16, Y = -4 }, new FVector2D { X = 0, Y = 1 }, ScreenWidget.UnderGameUI);
        return label;
    }

    public void Refresh()
    {
        if (manager == null || text == null) return;
        var line = $"BetterBlueprintLoader {ModManager.Version}: {manager.Running()} of {manager.Mods.Count} mods running (F10)";
        if (manager.Notice != "") line += "\n" + manager.Notice;
        text.SetText(line);
        // The game update warning is in the mod list only.
        var warn = manager.Notice != "";
        text.SetColorAndOpacity(new FSlateColor
        {
            SpecifiedColor = warn ? new FLinearColor { R = 1, G = 0.75f, B = 0.3f, A = 1 } : new FLinearColor { R = 0.85f, G = 0.85f, B = 0.85f, A = 0.8f },
            ColorUseRule = ESlateColorStylingMode.UseColor_Specified,
        });
    }
}
