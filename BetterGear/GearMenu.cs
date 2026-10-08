using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace BetterGear;

/// <summary>
/// The Armor &amp; Weapons section of the Custom tab in the inventory's Collectibles screen: a row per kind of gear
/// (helmet, chestplate, leggings, boots, melee and ranged weapon), each with a box for the item worn (the game's look),
/// one that hides it, and one per item found that fits its slot, in the game's slot boxes and as big as its. Clicking
/// one wears that look; the one worn has the game's equipped corner.
/// </summary>
public class GearMenu : ScreenWidget
{
    const string Title = "Armor & Weapons";
    const string SlotStyle = "/OreUI/UI/Button/SlotFrame/GearSelection/ButtonStyle_GearSlot.ButtonStyle_GearSlot_C";
    const string EquippedCorner = "/SpicewoodUI/Spicewood/UI/Widget/Inventory/PlayerInventory/Slot/T_UI_Slot_Equipped.T_UI_Slot_Equipped";
    const string TextStyles = "/OreUI/UI/Typography/TextStyles/";
    // The game's boxes fill this much of a tile of its grid; the rest is the gap between them.
    const float BoxShare = 0.83f;
    // An item's icon, as a share of the box.
    const float IconShare = 0.8f;
    // The equipped corner, as a share of the box.
    const float CornerShare = 0.62f;
    // Until the Custom tab has measured the game's grid.
    const float DefaultTile = 110;
    const int DefaultColumns = 6;
    // The popup's boxes, how many go across, and how tall its list gets before it scrolls.
    const float PopupTile = 110;
    const int PopupColumns = 8;
    const float PopupListHeight = 560;
    // The Close button's kind number.
    const int CloseBox = -1;
    const int FocusFrames = 3;

    GearLook? gear;
    bool everything;
    // As a popup over the inventory: one kind only, in a scrolling panel with a Close button.
    bool popup;
    int only;
    UVerticalBox? column;
    // Where the rows go: the column itself, or in a popup the list in its scroll box.
    UVerticalBox? rowsIn;
    UScrollBox? scroller;
    UTextBlock? title;
    // Per kind shown: its heading and its grid.
    List<UTextBlock> headings = new();
    List<UUniformGridPanel> grids = new();
    List<GearButton> buttons = new();
    List<USizeBox> boxes = new();
    List<UImage> icons = new();
    List<UImage> corners = new();
    List<UUniformGridSlot> cells = new();
    // Each box's place in its grid.
    List<int> places = new();
    // Whether each box is lit as having the controller's focus.
    List<bool> lit = new();
    GearButton? closeButton;
    bool closeLit;
    // Frames until the popup puts the focus on the look worn (0: done).
    int focusIn;
    // What the boxes show, to see when the gear worn or found changes.
    string shown;
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
    // The boxes' items and icons, read before the boxes are made.
    List<int> kinds = new();
    List<string> looks = new();
    List<UTexture2D?> textures = new();
    List<string> tips = new();

    /// <summary>A section not made yet: keep it in a field, then <see cref="Setup"/> it (which loads the game's assets).</summary>
    public static GearMenu? Create(UObject context) =>
        UWidgetBlueprintLibrary.Create(context, Unreal.ClassOf<GearMenu>(), World.PlayerController(context)) as GearMenu;

    public bool Setup(GearLook owner, bool showEverything)
    {
        gear = owner;
        everything = showEverything;
        only = -1;
        Preload();
        if (!Build()) return false;
        Refresh();
        return true;
    }

    /// <summary>
    /// Sets it up as a popup picking one kind's look (from its right-click menu in the inventory): show it with
    /// <see cref="Open"/>; picking a look or Close takes it off the screen.
    /// </summary>
    public bool SetupPopup(GearLook owner, bool showEverything, int kind)
    {
        gear = owner;
        everything = showEverything;
        only = kind;
        popup = true;
        Preload();
        if (!Build()) return false;
        Refresh();
        return true;
    }

    /// <summary>Shows the popup in the middle of the screen, over the game's menus, with the focus on the look worn.</summary>
    public void Open()
    {
        Fit(PopupTile, PopupTile, PopupColumns);
        ShowAt(new FVector2D(), new FVector2D { X = 0.5f, Y = 0.5f }, AboveGameUI);
        // The focus goes on the look worn a few frames on: the game puts it back on its slot as its menu closes.
        focusIn = FocusFrames;
    }

    void FocusWorn()
    {
        if (gear == null) return;
        for (int i = 0; i < buttons.Count; i++)
            if (buttons[i].Look == gear.Picked(buttons[i].Kind)) buttons[i].SetUserFocus(World.PlayerController(this));
    }

    /// <summary>The kind the popup picks for, or -1 for the Custom tab's section.</summary>
    public int Kind => only;

    /// <summary>The Show All Gear setting: every item that fits a slot, found or not.</summary>
    public void ShowEverything(bool on)
    {
        if (everything == on) return;
        everything = on;
        Refresh();
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
        corner = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(EquippedCorner))) as UTexture2D;
        PreloadStyle("Style_Header4_Text");
        PreloadStyle("Style_Button_Text");
    }

    void PreloadStyle(string style)
    {
        var styleClass = Unreal.LoadClass<UCommonTextStyle>($"{TextStyles}{style}.{style}_C");
        if (styleClass == null) return;
        styleNames.Add(style);
        styleClasses.Add(styleClass);
    }

    /// <summary>Makes the rows again, for the gear worn and found now.</summary>
    public void Refresh()
    {
        if (gear == null || column == null || title == null) return;
        // The icons first: loading them can let the garbage collector run.
        kinds.Clear();
        looks.Clear();
        textures.Clear();
        tips.Clear();
        for (int kind = 0; kind < GearLook.KindCount; kind++)
        {
            if (!gear.HasSlot(kind) || (only >= 0 && kind != only)) continue;
            var worn = gear.Worn(kind);
            AddChoice(kind, "", worn != "" ? gear.Icon(worn) : null, worn != "" ? gear.Title(worn) : "The game's look");
            AddChoice(kind, GearLook.Hidden, null, $"No {GearLook.KindTitle(kind).ToLower()}");
            foreach (var item in gear.Choices(kind, everything)) AddChoice(kind, item, gear.Icon(item), gear.Title(item));
        }
        shown = gear.Signature();

        if (rowsIn == null) return;
        rowsIn.ClearChildren();
        headings.Clear();
        grids.Clear();
        buttons.Clear();
        boxes.Clear();
        icons.Clear();
        corners.Clear();
        cells.Clear();
        places.Clear();
        lit.Clear();
        if (!popup) column.AddChildToVerticalBox(title);
        int last = -1;
        int place = 0;
        for (int i = 0; i < kinds.Count; i++)
        {
            if (kinds[i] != last)
            {
                last = kinds[i];
                place = 0;
                if (!AddRow(kinds[i])) return;
            }
            Add(kinds[i], looks[i], textures[i], tips[i], place);
            place++;
        }
        // The popup's way out, under its list (made once: the list is what's made again).
        if (popup && closeButton == null && column != null && WidgetTree != null)
        {
            var close = UGameplayStatics.SpawnObject(Unreal.ClassOf<GearButton>(), WidgetTree) as GearButton;
            var label = Styled(WidgetTree, "Close", "Style_Button_Text");
            if (close != null && label != null)
            {
                closeButton = close;
                close.Setup(this, CloseBox, "");
                close.SetStyle(BoxStyle(false));
                (close.AddChild(label) as UButtonSlot)?.SetPadding(new FMargin { Left = 24, Top = 8, Right = 24, Bottom = 8 });
                var closeSlot = column.AddChildToVerticalBox(close);
                closeSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
                closeSlot?.SetPadding(new FMargin { Top = 12 });
            }
        }
        Fit(tileWidth, tileHeight, columns);
        for (int i = 0; i < boxes.Count; i++) Size(i);
        Highlight();
    }

    void AddChoice(int kind, string look, UTexture2D? texture, string tip)
    {
        kinds.Add(kind);
        looks.Add(look);
        textures.Add(texture);
        tips.Add(tip);
    }

    /// <summary>Call regularly while the section shows: makes the rows again when the gear worn or found changed.</summary>
    public void Rescan()
    {
        if (gear != null && gear.Signature() != shown) Refresh();
        else Highlight();
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
        bool same = width == tileWidth && height == tileHeight && across == columns;
        tileWidth = width;
        tileHeight = height;
        columns = across;
        var gapX = width * (1 - BoxShare) / 2;
        var gapY = height * (1 - BoxShare) / 2;
        foreach (var grid in grids) grid.SetSlotPadding(new FMargin { Left = gapX, Top = gapY, Right = gapX, Bottom = gapY });
        (title?.Slot as UVerticalBoxSlot)?.SetPadding(new FMargin { Left = gapX, Top = gapY * 2, Bottom = gapY });
        foreach (var heading in headings) (heading.Slot as UVerticalBoxSlot)?.SetPadding(new FMargin { Left = gapX, Top = gapY });
        if (same) return;
        for (int i = 0; i < boxes.Count; i++) Size(i);
    }

    public void Pick(int kind, string look)
    {
        if (kind != CloseBox) gear?.Select(kind, look);
        Highlight();
        if (popup) Hide();
    }

    /// <summary>
    /// With a controller: lights the box with the focus and scrolls it into view. The D-pad moves between boxes (from
    /// the sections above) and A picks one.
    /// </summary>
    public override void Tick(FGeometry geometry, float deltaTime)
    {
        if (buttons.Count == 0 || !IsVisible()) return;
        if (focusIn > 0)
        {
            focusIn--;
            if (focusIn == 0) FocusWorn();
        }
        var page = popup ? scroller : GetParent() as UScrollBox;
        for (int i = 0; i < buttons.Count; i++)
        {
            bool focused = buttons[i].HasKeyboardFocus();
            if (focused == lit[i]) continue;
            lit[i] = focused;
            buttons[i].SetStyle(BoxStyle(focused));
            if (focused) page?.ScrollWidgetIntoView(boxes[i], true, EDescendantScrollDestination.IntoView, 0);
        }
        if (closeButton != null && closeButton.HasKeyboardFocus() != closeLit)
        {
            closeLit = !closeLit;
            closeButton.SetStyle(BoxStyle(closeLit));
        }
    }

    void Highlight()
    {
        if (gear == null) return;
        for (int i = 0; i < buttons.Count; i++)
            corners[i].SetVisibility(gear.Picked(buttons[i].Kind) == buttons[i].Look ? ESlateVisibility.HitTestInvisible : ESlateVisibility.Collapsed);
    }

    bool AddRow(int kind)
    {
        var tree = WidgetTree;
        if (tree == null || rowsIn == null) return false;
        var heading = Styled(tree, GearLook.KindTitle(kind), "Style_Button_Text");
        var grid = UGameplayStatics.SpawnObject(Unreal.ClassOf<UUniformGridPanel>(), tree) as UUniformGridPanel;
        if (heading == null || grid == null) return false;
        heading.SetJustification(ETextJustify.Left);
        headings.Add(heading);
        grids.Add(grid);
        // A popup's title already names its one kind.
        if (!popup) rowsIn.AddChildToVerticalBox(heading);
        rowsIn.AddChildToVerticalBox(grid)?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Left);
        return true;
    }

    void Add(int kind, string look, UTexture2D? texture, string tip, int place)
    {
        var tree = WidgetTree;
        if (tree == null || grids.Count == 0) return;
        var grid = grids[grids.Count - 1];
        var box = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<GearButton>(), tree) as GearButton;
        var layers = UGameplayStatics.SpawnObject(Unreal.ClassOf<UOverlay>(), tree) as UOverlay;
        var image = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
        var mark = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
        if (box == null || button == null || layers == null || image == null || mark == null) return;
        button.Setup(this, kind, look);
        button.SetStyle(BoxStyle(false));
        button.SetToolTipText(tip);
        box.AddChild(button);
        var content = button.AddChild(layers) as UButtonSlot;
        content?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Fill);
        content?.SetVerticalAlignment(EVerticalAlignment.VAlign_Fill);
        content?.SetPadding(new FMargin());

        if (texture != null)
        {
            image.SetBrushFromTexture(texture, false);
            var imageSlot = layers.AddChildToOverlay(image);
            imageSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
            imageSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        }
        else
        {
            // No icon: the box says what it is.
            var label = Styled(tree, look == GearLook.Hidden ? "None" : "Game", "Style_Button_Text");
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
        places.Add(place);
        lit.Add(false);
    }

    /// <summary>Sizes a box and puts it in its place in its grid.</summary>
    void Size(int i)
    {
        int across = columns > 0 ? columns : DefaultColumns;
        cells[i].SetRow(places[i] / across);
        cells[i].SetColumn(places[i] % across);
        var width = (tileWidth > 0 ? tileWidth : DefaultTile) * BoxShare;
        var height = (tileHeight > 0 ? tileHeight : DefaultTile) * BoxShare;
        boxes[i].SetWidthOverride(width);
        boxes[i].SetHeightOverride(height);
        var side = (width < height ? width : height) * IconShare;
        Resize(icons[i], side, side);
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
        column = UGameplayStatics.SpawnObject(Unreal.ClassOf<UVerticalBox>(), tree) as UVerticalBox;
        title = Styled(tree, popup ? $"{GearLook.KindTitle(only)} Look" : Title, "Style_Header4_Text");
        if (column == null || title == null) return false;
        title.SetJustification(ETextJustify.Left);
        column.AddChildToVerticalBox(title);
        if (!popup)
        {
            rowsIn = column;
            tree.RootWidget = column;
            return true;
        }
        // The popup: a dark panel, its title, then its list in a scroll box no taller than the screen allows.
        var panel = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) as UBorder;
        var limit = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        scroller = UGameplayStatics.SpawnObject(Unreal.ClassOf<UScrollBox>(), tree) as UScrollBox;
        rowsIn = UGameplayStatics.SpawnObject(Unreal.ClassOf<UVerticalBox>(), tree) as UVerticalBox;
        if (panel == null || limit == null || scroller == null || rowsIn == null) return false;
        panel.SetBrush(Rounded(new FLinearColor { R = 0.02f, G = 0.02f, B = 0.03f, A = 0.96f }, new FLinearColor { R = 1, G = 1, B = 1, A = 0.2f }, 2, 6));
        panel.SetPadding(new FMargin { Left = 24, Top = 16, Right = 24, Bottom = 16 });
        panel.SetContent(column);
        limit.SetMaxDesiredHeight(PopupListHeight);
        limit.AddChild(scroller);
        scroller.AddChild(rowsIn);
        column.AddChildToVerticalBox(limit);
        tree.RootWidget = panel;
        return true;
    }

    /// <summary>
    /// The game's item box (normal, hovered and pressed), or a plain one if it isn't in the game. Focused, it looks hovered
    /// all the time: the controller's focus has no look of its own.
    /// </summary>
    FButtonStyle BoxStyle(bool focused)
    {
        if (slotStyle == null)
        {
            var idle = Rounded(new FLinearColor { R = 0.03f, G = 0.03f, B = 0.04f, A = 0.9f }, new FLinearColor { R = 1, G = 1, B = 1, A = 0.15f }, 2, 0);
            var hover = Rounded(new FLinearColor { R = 0.05f, G = 0.05f, B = 0.06f, A = 0.95f }, new FLinearColor { R = 1, G = 1, B = 1, A = 0.6f }, 2, 0);
            return new FButtonStyle { Normal = focused ? hover : idle, Hovered = hover, Pressed = hover, Disabled = idle, NormalPadding = new FMargin(), PressedPadding = new FMargin() };
        }
        return new FButtonStyle
        {
            Normal = focused ? boxHovered : boxNormal,
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

/// <summary>A box that wears one look for one kind of gear.</summary>
public class GearButton : UButton
{
    int kind;
    string look;
    GearMenu? menu;

    public int Kind => kind;
    public string Look => look;

    public void Setup(GearMenu owner, int gearKind, string gearLook)
    {
        menu = owner;
        kind = gearKind;
        look = gearLook;
        OnClicked += Clicked;
    }

    void Clicked() => menu?.Pick(kind, look);
}
