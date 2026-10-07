using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// A line at the bottom left of the main menu: how many mods run, and a mod that crashed the game if one did. The Main
/// Menu Label setting turns it off.
/// </summary>
public class MenuLabel : ScreenWidget
{
    // The game's body text style: its font is the Minecraft one of the main menu's Settings and Party.
    const string FontStyle = "/OreUI/UI/Typography/TextStyles/Style_Body_Text.Style_Body_Text_C";
    const int FontSize = 36;

    ModManager? manager;
    UTextBlock? text;

    public static MenuLabel? Show(ModManager manager)
    {
        // Loaded before anything is made: loading can let the garbage collector run.
        var styleClass = Unreal.LoadClass<UCommonTextStyle>(FontStyle);
        var label = UWidgetBlueprintLibrary.Create(manager, Unreal.ClassOf<MenuLabel>(), World.PlayerController(manager)) as MenuLabel;
        if (label == null) return null;
        label.manager = manager;
        var tree = Ui.Tree(label);
        var canvas = UGameplayStatics.SpawnObject(Unreal.ClassOf<UCanvasPanel>(), tree) as UCanvasPanel;
        label.text = Ui.Text(tree, "", FontSize);
        if (tree == null || canvas == null || label.text == null) return null;
        // In the style's font, at the label's own size and colour (the default font if the style isn't in the game).
        if (styleClass != null && UGameplayStatics.SpawnObject(styleClass, tree) is UCommonTextStyle style)
        {
            style.GetFont(out var font);
            if (font.FontObject != null)
            {
                font.Size = FontSize;
                label.text.SetFont(font);
            }
        }
        label.text.SetShadowOffset(new FVector2D { X = 1, Y = 1 });
        // The widget fills the screen and the line sits at its bottom left: placing a small widget at the screen's
        // corner (ShowAt) left it at the top left for some players, cut off by the screen's top edge.
        var slot = canvas.AddChildToCanvas(label.text);
        if (slot == null) return null;
        var corner = new FVector2D { X = 0, Y = 1 };
        slot.SetAnchors(new FAnchors { Minimum = corner, Maximum = corner });
        slot.SetAlignment(corner);
        slot.SetPosition(new FVector2D { X = 16, Y = -4 });
        slot.SetAutoSize(true);
        tree.RootWidget = canvas;
        label.SetVisibility(ESlateVisibility.HitTestInvisible);
        label.Refresh();
        label.Attach();
        // The game can rebuild its UI and take the label off the screen with it: put it back when that happens.
        Timer.Start(label, nameof(KeepShown), 0.5f, loop: true);
        return label;
    }

    /// <summary>Takes the label off the screen for good.</summary>
    public void Remove()
    {
        Timer.Stop(this, nameof(KeepShown));
        RemoveFromParent();
    }

    public void Refresh()
    {
        if (manager == null || text == null) return;
        var line = $"BetterBlueprintLoader {manager.Version}: {manager.Running()} of {manager.Mods.Count} mods running";
        if (manager.Notice != "") line = manager.Notice + "\n" + line;
        text.SetText(line);
        text.SetColorAndOpacity(Ui.SlateColor(manager.Notice != "" ? Ui.Color(1, 0.75f, 0.3f, 1) : Ui.Color(0.85f, 0.85f, 0.85f, 0.8f)));
    }

    /// <summary>Under the game's UI on the player's screen, filling it (no position set, so the engine stretches it).</summary>
    void Attach()
    {
        var player = World.PlayerController(this);
        if (player == null) return;
        SetOwningPlayer(player);
        AddToPlayerScreen(ScreenWidget.UnderGameUI);
    }

    void KeepShown()
    {
        if (!IsInViewport()) Attach();
    }
}
