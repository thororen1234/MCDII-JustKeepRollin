using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace CustomCapes;

/// <summary>
/// The Custom Capes row in the inventory's Collectibles screen, under the game's capes: a button for the game's cape
/// and one per PNG in the Capes folder, showing the cape's outside. Clicking one wears it.
/// </summary>
public class CapeRow : ScreenWidget
{
    // A cape's outside is 10x16 pixels.
    const float IconWidth = 30;
    const float IconHeight = 48;
    // About six buttons to a line.
    const float ButtonsWidth = 400;

    CapeSwapper? capes;
    UWrapBox? grid;
    List<CapeButton> buttons = new();

    public static CapeRow? Create(UObject context, CapeSwapper capes)
    {
        var row = UWidgetBlueprintLibrary.Create(context, Unreal.ClassOf<CapeRow>(), World.PlayerController(context)) as CapeRow;
        if (row == null) return null;
        row.capes = capes;
        if (!row.Build()) return null;
        row.Refresh();
        return row;
    }

    /// <summary>Makes the buttons again, for PNGs added or changed since.</summary>
    public void Refresh()
    {
        if (grid == null || capes == null) return;
        grid.ClearChildren();
        buttons.Clear();
        Add(0);
        foreach (var number in CapeSwapper.Available()) Add(number);
        Highlight();
    }

    public void Pick(int number)
    {
        capes?.Select(number);
        Highlight();
    }

    void Highlight()
    {
        if (capes == null) return;
        foreach (var button in buttons) button.SetStyle(ButtonStyle(button.Number == capes.Cape));
    }

    void Add(int number)
    {
        var tree = WidgetTree;
        if (tree == null || grid == null || capes == null) return;
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<CapeButton>(), tree) as CapeButton;
        var column = UGameplayStatics.SpawnObject(Unreal.ClassOf<UVerticalBox>(), tree) as UVerticalBox;
        var label = Label(tree, number == 0 ? "Game" : number.ToString(), 13, White());
        if (button == null || column == null || label == null) return;
        button.Setup(this, number);

        var icon = number == 0 ? null : capes.Icon(number);
        var image = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
        if (image != null)
        {
            if (icon != null)
            {
                // Just the cape's outside, as seen from behind the character.
                image.SetBrushResourceObject(icon);
                var brush = image.Brush;
                brush.UVRegion = new FBox2f
                {
                    Min = new FVector2f { X = CapeSwapper.OutsideStart, Y = 0 },
                    Max = new FVector2f { X = CapeSwapper.OutsideEnd, Y = 1 },
                    bIsValid = true,
                };
                image.SetBrush(brush);
            }
            else
                // The game's cape has no PNG, and a PNG of another size isn't a cape: a plain block in its place.
                image.SetBrush(Rounded(new FLinearColor { R = 0.2f, G = 0.2f, B = 0.24f, A = 1 }, Gold(0.4f), 1, 4));
            image.SetDesiredSizeOverride(new FVector2D { X = IconWidth, Y = IconHeight });
            column.AddChildToVerticalBox(image)?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        }
        var labelSlot = column.AddChildToVerticalBox(label);
        labelSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        labelSlot?.SetPadding(new FMargin { Top = 2 });
        button.AddChild(column);
        grid.AddChildToWrapBox(button)?.SetPadding(new FMargin { Right = 6, Bottom = 6 });
        buttons.Add(button);
    }

    bool Build()
    {
        var tree = WidgetTree;
        if (tree == null)
        {
            tree = UGameplayStatics.SpawnObject(Unreal.ClassOf<UWidgetTree>(), this) as UWidgetTree;
            WidgetTree = tree;
        }
        if (tree == null) return false;
        var panel = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) as UBorder;
        var column = UGameplayStatics.SpawnObject(Unreal.ClassOf<UVerticalBox>(), tree) as UVerticalBox;
        grid = UGameplayStatics.SpawnObject(Unreal.ClassOf<UWrapBox>(), tree) as UWrapBox;
        var title = Label(tree, "Custom Capes", 16, Gold(1));
        if (panel == null || column == null || grid == null || title == null) return false;
        grid.WrapSize = ButtonsWidth;
        grid.bExplicitWrapSize = true;
        panel.SetBrush(Rounded(new FLinearColor { R = 0.03f, G = 0.03f, B = 0.04f, A = 0.85f }, Gold(0.5f), 1, 6));
        panel.SetPadding(new FMargin { Left = 12, Top = 8, Right = 12, Bottom = 6 });
        title.SetJustification(ETextJustify.Left);
        column.AddChildToVerticalBox(title)?.SetPadding(new FMargin { Bottom = 6 });
        column.AddChildToVerticalBox(grid);
        panel.AddChild(column);
        tree.RootWidget = panel;
        return true;
    }

    static FButtonStyle ButtonStyle(bool worn)
    {
        var idle = Rounded(new FLinearColor { R = 0.07f, G = 0.07f, B = 0.09f, A = 0.92f }, new FLinearColor { R = 1, G = 1, B = 1, A = 0.18f }, 1, 6);
        var hover = Rounded(new FLinearColor { R = 0.12f, G = 0.1f, B = 0.06f, A = 0.95f }, Gold(0.8f), 2, 6);
        var picked = Rounded(new FLinearColor { R = 0.2f, G = 0.15f, B = 0.05f, A = 0.95f }, Gold(1), 2, 6);
        var padding = new FMargin { Left = 6, Top = 6, Right = 6, Bottom = 4 };
        return new FButtonStyle
        {
            Normal = worn ? picked : idle,
            Hovered = worn ? picked : hover,
            Pressed = picked,
            Disabled = idle,
            NormalPadding = padding,
            PressedPadding = padding,
        };
    }

    /// <summary>A filled rounded box.</summary>
    static FSlateBrush Rounded(FLinearColor fill, FLinearColor outline, float outlineWidth, float radius) => new FSlateBrush
    {
        DrawAs = ESlateBrushDrawType.RoundedBox,
        TintColor = Color(fill),
        OutlineSettings = new FSlateBrushOutlineSettings
        {
            CornerRadii = new FVector4 { X = radius, Y = radius, Z = radius, W = radius },
            Color = Color(outline),
            Width = outlineWidth,
            RoundingType = ESlateBrushRoundingType.FixedRadius,
        },
    };

    static FSlateColor Color(FLinearColor color) => new FSlateColor { SpecifiedColor = color, ColorUseRule = ESlateColorStylingMode.UseColor_Specified };
    static FLinearColor Gold(float alpha) => new FLinearColor { R = 0.95f, G = 0.7f, B = 0.2f, A = alpha };
    static FLinearColor White() => new FLinearColor { R = 0.95f, G = 0.95f, B = 0.95f, A = 1 };

    static UTextBlock? Label(UObject outer, string text, int size, FLinearColor color)
    {
        var label = UGameplayStatics.SpawnObject(Unreal.ClassOf<UTextBlock>(), outer) as UTextBlock;
        if (label == null) return null;
        label.SetText(text);
        label.SetJustification(ETextJustify.Center);
        // The game's font: the engine's default one isn't always cooked into the game.
        var font = UMinimapHelpersLibrary.GetDefaultFont();
        if (font.FontObject != null)
        {
            font.Size = size;
            label.SetFont(font);
        }
        label.SetColorAndOpacity(Color(color));
        return label;
    }
}

/// <summary>A button that wears one cape.</summary>
public class CapeButton : UButton
{
    int number;
    CapeRow? row;

    public int Number => number;

    public void Setup(CapeRow owner, int cape)
    {
        row = owner;
        number = cape;
        OnClicked += Clicked;
    }

    void Clicked() => row?.Pick(number);
}
