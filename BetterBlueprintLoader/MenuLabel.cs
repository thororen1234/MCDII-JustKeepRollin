using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// A line on the main menu: how many mods run, a Mods button that opens the mod list, and a mod that crashed the game
/// if one did.
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
        var tree = label.WidgetTree;
        if (tree == null)
        {
            tree = UGameplayStatics.SpawnObject(Unreal.ClassOf<UWidgetTree>(), label) as UWidgetTree;
            label.WidgetTree = tree;
        }
        if (tree == null) return null;
        var row = UGameplayStatics.SpawnObject(Unreal.ClassOf<UHorizontalBox>(), tree) as UHorizontalBox;
        label.text = UGameplayStatics.SpawnObject(Unreal.ClassOf<UTextBlock>(), tree) as UTextBlock;
        var button = ModsButton.Make(tree, manager, "Mods", 13);
        if (row == null || label.text == null || button == null) return null;
        var font = UMinimapHelpersLibrary.GetDefaultFont();
        if (font.FontObject != null)
        {
            font.Size = 13;
            label.text.SetFont(font);
        }
        label.text.SetShadowOffset(new FVector2D { X = 1, Y = 1 });
        row.AddChildToHorizontalBox(label.text)?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        var buttonSlot = row.AddChildToHorizontalBox(button);
        buttonSlot?.SetPadding(new FMargin { Left = 8 });
        buttonSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        tree.RootWidget = row;
        // Only the button takes clicks; the rest goes through to the menu.
        label.SetVisibility(ESlateVisibility.SelfHitTestInvisible);
        row.SetVisibility(ESlateVisibility.SelfHitTestInvisible);
        label.text.SetVisibility(ESlateVisibility.HitTestInvisible);
        label.Refresh();
        // Above the menu: under it, the menu's full-screen layer takes the button's clicks.
        label.ShowAt(new FVector2D { X = 16, Y = -4 }, new FVector2D { X = 0, Y = 1 }, ScreenWidget.AboveGameUI);
        return label;
    }

    public void Refresh()
    {
        if (manager == null || text == null) return;
        var line = $"BetterBlueprintLoader {ModManager.Version}: {manager.Running()} of {manager.Mods.Count} mods running";
        if (manager.Notice != "") line = manager.Notice + "\n" + line;
        text.SetText(line);
        text.SetColorAndOpacity(new FSlateColor
        {
            SpecifiedColor = manager.Notice != "" ? new FLinearColor { R = 1, G = 0.75f, B = 0.3f, A = 1 } : new FLinearColor { R = 0.85f, G = 0.85f, B = 0.85f, A = 0.8f },
            ColorUseRule = ESlateColorStylingMode.UseColor_Specified,
        });
    }
}

/// <summary>A button that opens or closes the mod list: on the main menu, and in the game's menu bar.</summary>
public class ModsButton : UButton
{
    ModManager? manager;

    public static ModsButton? Make(UWidgetTree tree, ModManager manager, string text, int size)
    {
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<ModsButton>(), tree) as ModsButton;
        var label = UGameplayStatics.SpawnObject(Unreal.ClassOf<UTextBlock>(), tree) as UTextBlock;
        if (button == null || label == null) return null;
        button.Setup(manager);
        label.SetText(text);
        var font = UMinimapHelpersLibrary.GetDefaultFont();
        if (font.FontObject != null)
        {
            font.Size = size;
            label.SetFont(font);
        }
        label.SetColorAndOpacity(Color(new FLinearColor { R = 0.9f, G = 0.9f, B = 0.9f, A = 1 }));
        button.SetStyle(new FButtonStyle
        {
            Normal = Box(new FLinearColor { R = 0.08f, G = 0.08f, B = 0.1f, A = 0.85f }, new FLinearColor { R = 1, G = 1, B = 1, A = 0.2f }),
            Hovered = Box(new FLinearColor { R = 0.25f, G = 0.17f, B = 0.05f, A = 0.95f }, new FLinearColor { R = 0.95f, G = 0.6f, B = 0.15f, A = 1 }),
            Pressed = Box(new FLinearColor { R = 0.35f, G = 0.22f, B = 0.05f, A = 1 }, new FLinearColor { R = 0.95f, G = 0.6f, B = 0.15f, A = 1 }),
            NormalPadding = new FMargin { Left = 10, Top = 2, Right = 10, Bottom = 2 },
            PressedPadding = new FMargin { Left = 10, Top = 3, Right = 10, Bottom = 1 },
        });
        button.AddChild(label);
        return button;
    }

    void Setup(ModManager owner)
    {
        manager = owner;
        OnClicked += Clicked;
    }

    void Clicked() => manager?.ToggleList();

    static FSlateBrush Box(FLinearColor fill, FLinearColor outline) => new FSlateBrush
    {
        DrawAs = ESlateBrushDrawType.RoundedBox,
        TintColor = Color(fill),
        OutlineSettings = new FSlateBrushOutlineSettings
        {
            CornerRadii = new FVector4 { X = 3, Y = 3, Z = 3, W = 3 },
            Color = Color(outline),
            Width = 1,
            RoundingType = ESlateBrushRoundingType.FixedRadius,
        },
    };

    static FSlateColor Color(FLinearColor color) => new FSlateColor { SpecifiedColor = color, ColorUseRule = ESlateColorStylingMode.UseColor_Specified };
}
