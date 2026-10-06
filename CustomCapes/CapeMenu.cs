using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace CustomCapes;

/// <summary>
/// The Capes section of the Custom tab in the inventory's Collectibles screen: a box for the game's cape and one per
/// PNG in the Capes folder, showing the cape's outside, in the game's slot boxes and as big as its. Clicking one wears it;
/// the one worn has the game's equipped corner. The last box, a plus, copies the Capes folder's path, to add PNGs to it
/// in File Explorer; they show once back in the game. In the game's look from its assets, or a plain one if they're missing.
/// </summary>
public class CapeRow : ScreenWidget
{
    const string Title = "Capes";
    const string SlotStyle = "/OreUI/UI/Button/SlotFrame/GearSelection/ButtonStyle_GearSlot.ButtonStyle_GearSlot_C";
    const string EquippedCorner = "/SpicewoodUI/Spicewood/UI/Widget/Inventory/PlayerInventory/Slot/T_UI_Slot_Equipped.T_UI_Slot_Equipped";
    const string TextStyles = "/OreUI/UI/Typography/TextStyles/";
    // The game's boxes fill this much of a tile of its grid; the rest is the gap between them.
    const float BoxShare = 0.83f;
    // A cape's outside is 10x16 pixels, this much of the box high.
    const float CapeShare = 0.6f;
    const float CapeAspect = 10f / 16f;
    // The equipped corner, as a share of the box.
    const float CornerShare = 0.62f;
    // The plus: its bars' length and thickness, as a share of the box.
    const float PlusLength = 0.36f;
    const float PlusThickness = 0.07f;
    // The plus box's number: it copies the folder's path.
    const int AddBox = -1;
    // Until the Custom tab has measured the game's grid.
    const float DefaultTile = 110;
    const int DefaultColumns = 6;

    CapeSwapper? capes;
    UTextBlock? title;
    // Under the boxes once the plus has copied the folder's path.
    UTextBlock? note;
    UUniformGridPanel? grid;
    List<CapeButton> buttons = new();
    List<USizeBox> boxes = new();
    List<UImage> icons = new();
    List<UImage> corners = new();
    List<UUniformGridSlot> cells = new();
    // The capes shown (0 for the game's), to see when the folder has others.
    List<int> shown = new();
    // The game's cape on its box: it changes when the game's menu picks another.
    UTexture? gameShown;
    UImage? plusBar;
    float tileWidth;
    float tileHeight;
    int columns;
    // The game's assets, loaded before any widget is made (loading can let the garbage collector run).
    UCommonButtonStyle? slotStyle;
    FSlateBrush boxNormal;
    FSlateBrush boxHovered;
    FSlateBrush boxPressed;
    UTexture2D? corner;
    List<string> styleNames = new();
    List<TSubclassOf<UCommonTextStyle>> styleClasses = new();

    /// <summary>A section not made yet: keep it in a field, then <see cref="Setup"/> it (which loads the game's assets).</summary>
    public static CapeRow? Create(UObject context) =>
        UWidgetBlueprintLibrary.Create(context, Unreal.ClassOf<CapeRow>(), World.PlayerController(context)) as CapeRow;

    public bool Setup(CapeSwapper owner)
    {
        capes = owner;
        Preload();
        if (!Build()) return false;
        Refresh();
        return true;
    }

    void Preload()
    {
        var styleClass = Unreal.LoadClass<UCommonButtonStyle>(SlotStyle);
        if (styleClass != null) slotStyle = UGameplayStatics.SpawnObject(styleClass, this) as UCommonButtonStyle;
        if (slotStyle != null)
        {
            slotStyle.GetNormalBaseBrush(out var normal);
            slotStyle.GetNormalHoveredBrush(out var hovered);
            slotStyle.GetNormalPressedBrush(out var pressed);
            boxNormal = normal;
            boxHovered = hovered;
            boxPressed = pressed;
        }
        corner = Load(EquippedCorner) as UTexture2D;
        PreloadStyle("Style_Header4_Text");
        PreloadStyle("Style_Button_Text");
        PreloadStyle("Style_Body_Text");
    }

    void PreloadStyle(string style)
    {
        var styleClass = Unreal.LoadClass<UCommonTextStyle>($"{TextStyles}{style}.{style}_C");
        if (styleClass == null) return;
        styleNames.Add(style);
        styleClasses.Add(styleClass);
    }

    /// <summary>Makes the boxes again, for PNGs added or changed since.</summary>
    public void Refresh()
    {
        if (grid == null || capes == null) return;
        // The textures first: reading them can let the garbage collector run.
        var numbers = new List<int> { 0 };
        foreach (var number in CapeSwapper.Available()) numbers.Add(number);
        var textures = new List<UTexture?>();
        foreach (var number in numbers) textures.Add(number == 0 ? capes.GameIcon() : capes.Icon(number));
        gameShown = textures[0];
        grid.ClearChildren();
        buttons.Clear();
        boxes.Clear();
        icons.Clear();
        corners.Clear();
        cells.Clear();
        shown.Clear();
        for (int i = 0; i < numbers.Count; i++)
        {
            Add(numbers[i], textures[i]);
            shown.Add(numbers[i]);
        }
        Add(AddBox, null);
        for (int i = 0; i < boxes.Count; i++) Size(i);
        Highlight();
    }

    /// <summary>
    /// Call regularly while the section shows: makes the boxes again when PNGs were added to or taken from the folder, or
    /// the game's menu picked another of its capes.
    /// </summary>
    public void Rescan()
    {
        if (capes == null) return;
        var now = CapeSwapper.Available();
        bool same = now.Count + 1 == shown.Count && capes.GameIcon() == gameShown;
        for (int i = 0; same && i < now.Count; i++) same = now[i] == shown[i + 1];
        if (!same) Refresh();
    }

    /// <summary>Sizes the boxes like the game's grid: a tile's size (gap included) and how many go across.</summary>
    public void Fit(float width, float height, int across)
    {
        if (width <= 0 || height <= 0 || across <= 0)
        {
            width = DefaultTile;
            height = DefaultTile;
            across = DefaultColumns;
        }
        if (width == tileWidth && height == tileHeight && across == columns) return;
        tileWidth = width;
        tileHeight = height;
        columns = across;
        var gapX = width * (1 - BoxShare) / 2;
        var gapY = height * (1 - BoxShare) / 2;
        grid?.SetSlotPadding(new FMargin { Left = gapX, Top = gapY, Right = gapX, Bottom = gapY });
        (title?.Slot as UVerticalBoxSlot)?.SetPadding(new FMargin { Left = gapX, Top = gapY * 2, Bottom = gapY });
        (note?.Slot as UVerticalBoxSlot)?.SetPadding(new FMargin { Left = gapX, Right = gapX });
        for (int i = 0; i < boxes.Count; i++) Size(i);
    }

    public void Pick(int number)
    {
        if (number == AddBox)
        {
            CustomTab.CopyFolder(CapeSwapper.Folder());
            note?.SetVisibility(ESlateVisibility.HitTestInvisible);
            return;
        }
        capes?.Select(number);
        Highlight();
    }

    void Highlight()
    {
        if (capes == null) return;
        for (int i = 0; i < buttons.Count; i++)
            corners[i].SetVisibility(buttons[i].Number == capes.Cape ? ESlateVisibility.HitTestInvisible : ESlateVisibility.Collapsed);
    }

    void Add(int number, UTexture? texture)
    {
        var tree = WidgetTree;
        if (tree == null || grid == null) return;
        var box = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<CapeButton>(), tree) as CapeButton;
        var layers = UGameplayStatics.SpawnObject(Unreal.ClassOf<UOverlay>(), tree) as UOverlay;
        var image = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
        var mark = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
        if (box == null || button == null || layers == null || image == null || mark == null) return;
        button.Setup(this, number);
        button.SetStyle(BoxStyle());
        box.AddChild(button);
        var content = button.AddChild(layers) as UButtonSlot;
        content?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
        content?.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
        content?.SetPadding(new FMargin());

        if (number == AddBox)
        {
            // A plus of two bars, in the game's text colour.
            var bar = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
            if (bar == null) return;
            plusBar = bar;
            foreach (var part in new List<UImage> { image, bar })
            {
                part.SetColorAndOpacity(new FLinearColor { R = 0.8f, G = 0.8f, B = 0.8f, A = 1 });
                var partSlot = layers.AddChildToOverlay(part);
                partSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
                partSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            }
        }
        else if (texture != null)
        {
            // Just the cape's outside, as seen from behind the character.
            image.SetBrushResourceObject(texture);
            var brush = image.Brush;
            brush.UVRegion = new FBox2f
            {
                Min = new FVector2f { X = CapeSwapper.OutsideStart, Y = 0 },
                Max = new FVector2f { X = CapeSwapper.OutsideEnd, Y = 1 },
                bIsValid = true,
            };
            image.SetBrush(brush);
            var imageSlot = layers.AddChildToOverlay(image);
            imageSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
            imageSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        }
        else
        {
            // The game's cape before it has been seen on the character: its name in its place.
            var label = Styled(tree, "Game", "Style_Button_Text");
            if (label != null)
            {
                var labelSlot = layers.AddChildToOverlay(label);
                labelSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
                labelSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            }
        }

        if (corner != null) mark.SetBrushFromTexture(corner, false);
        else mark.SetBrush(Rounded(Gold(1), Gold(1), 0, 2));
        var markSlot = layers.AddChildToOverlay(mark);
        markSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Right);
        markSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Top);

        var cell = grid.AddChildToUniformGrid(box, 0, 0);
        if (cell == null) return;
        buttons.Add(button);
        boxes.Add(box);
        icons.Add(image);
        corners.Add(mark);
        cells.Add(cell);
    }

    /// <summary>Sizes a box and puts it in its place in the grid.</summary>
    void Size(int i)
    {
        int across = columns > 0 ? columns : DefaultColumns;
        cells[i].SetRow(i / across);
        cells[i].SetColumn(i % across);
        var width = (tileWidth > 0 ? tileWidth : DefaultTile) * BoxShare;
        var height = (tileHeight > 0 ? tileHeight : DefaultTile) * BoxShare;
        boxes[i].SetWidthOverride(width);
        boxes[i].SetHeightOverride(height);
        if (buttons[i].Number == AddBox)
        {
            Resize(icons[i], width * PlusLength, width * PlusThickness);
            if (plusBar != null) Resize(plusBar, width * PlusThickness, width * PlusLength);
        }
        else
        {
            var capeHeight = height * CapeShare;
            Resize(icons[i], capeHeight * CapeAspect, capeHeight);
        }
        var cornerSize = width * CornerShare;
        Resize(corners[i], cornerSize, cornerSize);
    }

    /// <summary>
    /// Sizes an image by its brush. (Its desired size override lives only in the widget on screen: rebuilding that, as
    /// moving the section to a new screen does, loses it.)
    /// </summary>
    static void Resize(UImage image, float width, float height)
    {
        var brush = image.Brush;
        brush.ImageSize = new FDeprecateSlateVector2D { X = width, Y = height };
        image.SetBrush(brush);
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
        var column = UGameplayStatics.SpawnObject(Unreal.ClassOf<UVerticalBox>(), tree) as UVerticalBox;
        grid = UGameplayStatics.SpawnObject(Unreal.ClassOf<UUniformGridPanel>(), tree) as UUniformGridPanel;
        title = Styled(tree, Title, "Style_Header4_Text");
        note = Styled(tree, "Copied the Capes folder's path: paste it into File Explorer's address bar, add capes as 1.png to 20.png, and come back", "Style_Body_Text");
        if (column == null || grid == null || title == null || note == null) return false;
        title.SetJustification(ETextJustify.Left);
        note.SetJustification(ETextJustify.Left);
        note.SetAutoWrapText(true);
        note.SetVisibility(ESlateVisibility.Collapsed);
        column.AddChildToVerticalBox(title);
        column.AddChildToVerticalBox(grid)?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Left);
        column.AddChildToVerticalBox(note);
        tree.RootWidget = column;
        Fit(0, 0, 0);
        return true;
    }

    UObject? Load(string path) =>
        UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(path)));

    /// <summary>The game's item box (normal, hovered and pressed), or a plain one if it isn't in the game.</summary>
    FButtonStyle BoxStyle()
    {
        if (slotStyle == null)
        {
            var idle = Rounded(new FLinearColor { R = 0.03f, G = 0.03f, B = 0.04f, A = 0.9f }, new FLinearColor { R = 1, G = 1, B = 1, A = 0.15f }, 2, 0);
            var hover = Rounded(new FLinearColor { R = 0.05f, G = 0.05f, B = 0.06f, A = 0.95f }, new FLinearColor { R = 1, G = 1, B = 1, A = 0.6f }, 2, 0);
            return new FButtonStyle { Normal = idle, Hovered = hover, Pressed = hover, Disabled = idle, NormalPadding = new FMargin(), PressedPadding = new FMargin() };
        }
        return new FButtonStyle
        {
            Normal = boxNormal,
            Hovered = boxHovered,
            Pressed = boxPressed,
            Disabled = boxNormal,
            NormalPadding = new FMargin(),
            PressedPadding = new FMargin(),
        };
    }

    /// <summary>Text in one of the game's text styles, or a plain look if the style isn't in the game.</summary>
    UTextBlock? Styled(UObject outer, string text, string style)
    {
        int at = styleNames.IndexOf(style);
        if (at < 0) return Label(outer, text, 15, White());
        var block = UGameplayStatics.SpawnObject(Unreal.ClassOf<UCommonTextBlock>(), outer) as UCommonTextBlock;
        if (block == null) return null;
        block.SetStyle(styleClasses[at]);
        block.SetText(text);
        block.SetJustification(ETextJustify.Center);
        return block;
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

/// <summary>A box that wears one cape.</summary>
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
