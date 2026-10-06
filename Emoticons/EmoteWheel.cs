using System.Collections.Generic;
using NeoRune;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace Emoticons;

/// <summary>
/// The emote wheel: a page of emotes in a ring around the middle of the screen. Point the mouse at one to pick it;
/// the mouse wheel turns the page. Drawn like the game's own wheel (its ring, with the names on its marks, and its
/// text styles), from the game's assets; in a plain look of its own if they aren't in the game.
/// </summary>
public class EmoteWheel : ScreenWidget
{
    const string RingMaterial = "/SpicewoodUI/Spicewood/UI/Widget/RadialMenu/MI_UI_RadialMenu_Background.MI_UI_RadialMenu_Background";
    const string TextStyles = "/OreUI/UI/Typography/TextStyles/";
    const string ButtonArt = "/OreUI/UI/Button/Role/ListEntry/";
    const int PerPage = 8;
    const float Size = 1000;
    const float Radius = 350;
    const float Disc = 900;
    const float Middle = 230;
    // Smallest pill; longer names make it wider.
    const float SlotWidth = 120;
    const float SlotHeight = 30;
    // Widest a name gets: longer ones wrap, and a word too long to wrap (CALCULATED) shrinks. The ones at the sides stay
    // between the ring's arrow and its edge even when pointed at (1.2 times as big): Radius + 1.2 * LabelWidth / 2 < Disc / 2.
    const float LabelWidth = 140;
    // How far the mouse must be from the middle (in UI units) to point at an emote.
    const float DeadZone = Middle / 2;

    ModActor? mod;
    int page;
    // Set to -1 by Layout, which runs before the wheel is shown.
    int hovered;
    int litSlot;
    List<EmoteButton> buttons = new();
    List<UTextBlock> labels = new();
    List<UCanvasPanelSlot> slots = new();
    UTextBlock? title;
    UTextBlock? pageText;
    UButton? stop;
    // The game's ring: its material draws the panel and the marks the names sit on.
    UMaterialInstanceDynamic? ring;
    bool native;
    // The game's assets, loaded before any of the wheel's widgets is made (loading can let the garbage collector run,
    // which would destroy widgets made but not yet in the wheel).
    UMaterialInterface? ringMaterial;
    UMaterialInterface? buttonNormal;
    UMaterialInterface? buttonHovered;
    UMaterialInterface? buttonPressed;
    List<string> styleNames = new();
    List<TSubclassOf<UCommonTextStyle>> styleClasses = new();

    void Preload()
    {
        ringMaterial = Load(RingMaterial) as UMaterialInterface;
        buttonNormal = Load($"{ButtonArt}MI_ListEntry_Background-Normal-Base.MI_ListEntry_Background-Normal-Base") as UMaterialInterface;
        buttonHovered = Load($"{ButtonArt}MI_ListEntry_Background-Normal-Hovered.MI_ListEntry_Background-Normal-Hovered") as UMaterialInterface;
        buttonPressed = Load($"{ButtonArt}MI_ListEntry_Background-Normal-Pressed.MI_ListEntry_Background-Normal-Pressed") as UMaterialInterface;
        PreloadStyle("Style_Button_Text");
        PreloadStyle("Style_SectionHeader1_Text");
        PreloadStyle("Style_Body_Text");
    }

    void PreloadStyle(string style)
    {
        var styleClass = Unreal.LoadClass<UCommonTextStyle>($"{TextStyles}{style}.{style}_C");
        if (styleClass == null) return;
        styleNames.Add(style);
        styleClasses.Add(styleClass);
    }

    /// <summary>The emote the mouse points at, or -1.</summary>
    public int Hovered => hovered;

    /// <summary>Whether the mouse is on the Stop button.</summary>
    public bool StopHovered => stop != null && stop.IsHovered();

    public static EmoteWheel? Open(ModActor mod)
    {
        var wheel = UWidgetBlueprintLibrary.Create(mod, Unreal.ClassOf<EmoteWheel>(), World.PlayerController(mod)) as EmoteWheel;
        if (wheel == null) return null;
        mod.Holding(wheel);
        wheel.mod = mod;
        if (!wheel.Build())
        {
            mod.WheelClosed();
            return null;
        }
        wheel.Layout();
        var middle = new FVector2D { X = 0.5f, Y = 0.5f };
        wheel.ShowAt(new FVector2D(), middle, ScreenWidget.AboveGameUI);
        // Without a size the screen slot is a point, so centring it moves nothing. Setting the size resets the
        // anchors to the top left, so they're set again after it.
        wheel.SetDesiredSizeInViewport(new FVector2D { X = Size, Y = Size });
        wheel.SetAnchorsInViewport(new FAnchors { Minimum = middle, Maximum = middle });
        wheel.SetAlignmentInViewport(middle);
        return wheel;
    }

    public void Close()
    {
        // Not RemoveFromParent: ShowAt's check would put the closed wheel back on the screen.
        Hide();
        mod?.WheelClosed();
    }

    public void Turn(int by)
    {
        page = (page + by + Pages()) % Pages();
        Layout();
    }

    static int Pages() => (EmoteData.Count + PerPage - 1) / PerPage;

    int OnPage()
    {
        var left = EmoteData.Count - page * PerPage;
        return left < PerPage ? left : PerPage;
    }

    /// <summary>Highlights the emote in the direction of the mouse from the middle of the screen. Call every frame.</summary>
    public void UpdateHover()
    {
        var scale = UWidgetLayoutLibrary.GetViewportScale(this);
        if (scale <= 0) return;
        var size = UWidgetLayoutLibrary.GetViewportSize(this);
        var mouse = UWidgetLayoutLibrary.GetMousePositionOnViewport(this);
        var dx = mouse.X - size.X / scale / 2;
        var dy = mouse.Y - size.Y / scale / 2;

        int slot = -1;
        int count = OnPage();
        if (dx * dx + dy * dy > DeadZone * DeadZone && count > 0)
        {
            // Slot 0 is at the top, then clockwise; each slot's slice is centred on it.
            var angle = UKismetMathLibrary.DegAtan2(dy, dx) + 90 + 180.0 / PerPage;
            slot = UKismetMathLibrary.Percent_IntInt(UKismetMathLibrary.FFloor(angle / (360.0 / PerPage)) + PerPage, PerPage);
            if (slot >= count) slot = -1;
        }
        var next = slot >= 0 ? page * PerPage + slot : -1;
        if (next == hovered) return;
        hovered = next;
        Light(slot);
        title?.SetText(hovered >= 0 ? EmoteData.Title(hovered) : "Emotes");
    }

    void Light(int slot)
    {
        if (litSlot >= 0 && litSlot < buttons.Count) Style(litSlot, false);
        litSlot = slot;
        if (slot >= 0) Style(slot, true);
    }

    void Style(int slot, bool lit)
    {
        // The game's ring lights a quarter (it's made for four choices), so on it the name itself shows what's pointed at.
        if (native)
        {
            labels[slot].SetRenderOpacity(lit ? 1 : 0.6f);
            buttons[slot].SetRenderScale(lit ? new FVector2D { X = 1.2f, Y = 1.2f } : new FVector2D { X = 1, Y = 1 });
            return;
        }
        buttons[slot].SetStyle(ButtonStyle(lit));
        labels[slot].SetColorAndOpacity(Color(lit ? Dark() : White()));
    }

    void StopEmote() => mod?.StopEmote();

    void Layout()
    {
        int count = OnPage();
        for (int i = 0; i < PerPage; i++)
        {
            if (i >= count)
            {
                buttons[i].SetVisibility(ESlateVisibility.Collapsed);
                continue;
            }
            // On the ring's marks: its arrows (top, right, bottom, left) and its corners.
            var angle = -90 + i * 360.0 / PerPage;
            slots[i].SetPosition(new FVector2D
            {
                X = Size / 2 + Radius * UKismetMathLibrary.DegCos(angle),
                Y = Size / 2 + Radius * UKismetMathLibrary.DegSin(angle),
            });
            buttons[i].SetIndex(page * PerPage + i);
            buttons[i].SetVisibility(ESlateVisibility.Visible);
            labels[i].SetText(EmoteData.Title(page * PerPage + i));
            Style(i, false);
        }
        hovered = -1;
        litSlot = -1;
        ring?.SetScalarParameterValue("Divisions", PerPage);
        ring?.SetScalarParameterValue("Selected", 0);
        title?.SetText("Emotes");
        pageText?.SetText(Pages() > 1 ? $"Page {page + 1} of {Pages()}" : "");
    }

    bool Build()
    {
        Preload();
        var tree = WidgetTree;
        if (tree == null)
        {
            tree = UGameplayStatics.SpawnObject(Unreal.ClassOf<UWidgetTree>(), this) as UWidgetTree;
            WidgetTree = tree;
        }
        var box = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        var canvas = UGameplayStatics.SpawnObject(Unreal.ClassOf<UCanvasPanel>(), tree) as UCanvasPanel;
        var disc = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) as UBorder;
        var middle = UGameplayStatics.SpawnObject(Unreal.ClassOf<UBorder>(), tree) as UBorder;
        var column = UGameplayStatics.SpawnObject(Unreal.ClassOf<UVerticalBox>(), tree) as UVerticalBox;
        if (tree == null || box == null || canvas == null || disc == null || middle == null || column == null) return false;
        box.SetWidthOverride(Size);
        box.SetHeightOverride(Size);
        box.AddChild(canvas);

        var ringImage = Ring(tree);
        native = ringImage != null;
        if (ringImage != null)
        {
            if (!Place(canvas, ringImage, Disc, Disc)) return false;
        }
        else
        {
            disc.SetBrush(Rounded(new FLinearColor { R = 0, G = 0, B = 0, A = 0.45f }, Gold(0.5f), 2, Disc / 2));
            if (!Place(canvas, disc, Disc, Disc)) return false;
        }

        for (int i = 0; i < PerPage; i++)
        {
            var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<EmoteButton>(), tree) as EmoteButton;
            var label = native ? Styled(tree, "", "Style_Button_Text") : Label(tree, "", 15, White());
            var fit = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
            var shrink = UGameplayStatics.SpawnObject(Unreal.ClassOf<UScaleBox>(), tree) as UScaleBox;
            if (button == null || label == null || fit == null || shrink == null) return false;
            button.Setup(mod);
            // On the game's ring, the names are text only: the ring shows which is pointed at.
            if (native) button.SetStyle(Invisible());
            fit.SetMinDesiredWidth(SlotWidth);
            fit.SetMinDesiredHeight(SlotHeight);
            // Long names go on two lines, so the ones at the sides stay on the ring. Wrapping at a set width (not
            // AutoWrapText) makes a single word too long to wrap measure wider than the box, so the scale box shrinks it.
            fit.SetMaxDesiredWidth(LabelWidth);
            label.WrapTextAt = LabelWidth;
            shrink.SetStretch(EStretch.ScaleToFit);
            shrink.SetStretchDirection(EStretchDirection.DownOnly);
            shrink.AddChild(label);
            var labelSlot = fit.AddChild(shrink) as USizeBoxSlot;
            labelSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
            labelSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            var fitSlot = button.AddChild(fit) as UButtonSlot;
            fitSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
            fitSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            var slot = canvas.AddChildToCanvas(button);
            if (slot == null) return false;
            // Sized to the name, centred on its spot on the ring.
            slot.SetAutoSize(true);
            slot.SetAlignment(new FVector2D { X = 0.5f, Y = 0.5f });
            buttons.Add(button);
            labels.Add(label);
            slots.Add(slot);
        }

        title = native ? Styled(tree, "Emotes", "Style_SectionHeader1_Text") : Label(tree, "Emotes", 20, Gold(1));
        pageText = native ? Styled(tree, "", "Style_Body_Text") : Label(tree, "", 13, White());
        var hint = native ? Styled(tree, "Scroll for more", "Style_Body_Text") : Label(tree, "Scroll for more", 11, new FLinearColor { R = 0.6f, G = 0.6f, B = 0.6f, A = 1 });
        stop = UGameplayStatics.SpawnObject(Unreal.ClassOf<UButton>(), tree) as UButton;
        var stopLabel = native ? Styled(tree, "Stop", "Style_Button_Text") : Label(tree, "Stop", 13, White());
        if (title == null || pageText == null || hint == null || stop == null || stopLabel == null) return false;
        hint.SetRenderOpacity(0.7f);
        var red = new FLinearColor { R = 0.45f, G = 0.1f, B = 0.1f, A = 0.95f };
        if (!native || !GameButton(stop))
            stop.SetStyle(new FButtonStyle
            {
                Normal = Rounded(red, new FLinearColor { R = 1, G = 1, B = 1, A = 0.15f }, 1, -1),
                Hovered = Rounded(new FLinearColor { R = 0.65f, G = 0.15f, B = 0.15f, A = 1 }, Gold(0.8f), 1, -1),
                Pressed = Rounded(red, Gold(1), 1, -1),
                NormalPadding = new FMargin { Left = 18, Top = 3, Right = 18, Bottom = 3 },
                PressedPadding = new FMargin { Left = 18, Top = 4, Right = 18, Bottom = 2 },
            });
        stop.AddChild(stopLabel);
        stop.OnClicked += StopEmote;
        column.AddChildToVerticalBox(title)?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        column.AddChildToVerticalBox(pageText)?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        column.AddChildToVerticalBox(hint)?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        var stopSlot = column.AddChildToVerticalBox(stop);
        stopSlot?.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        stopSlot?.SetPadding(new FMargin { Top = 10 });
        // The game's ring has its own middle: the names go on it without a disc of their own.
        if (native) middle.SetBrushColor(new FLinearColor());
        else middle.SetBrush(Rounded(new FLinearColor { R = 0.03f, G = 0.03f, B = 0.04f, A = 0.85f }, Gold(0.7f), 2, Middle / 2));
        middle.SetHorizontalAlignment(EHorizontalAlignment.HAlign_Center);
        middle.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        middle.AddChild(column);
        if (!Place(canvas, middle, Middle, Middle)) return false;

        tree.RootWidget = box;
        return true;
    }

    UObject? Load(string path) =>
        UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(path)));

    /// <summary>The game's wheel ring, as an image whose material can light a part; null if it isn't in the game.</summary>
    UImage? Ring(UWidgetTree tree)
    {
        var material = ringMaterial;
        if (material == null) return null;
        var image = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
        if (image == null) return null;
        image.SetBrush(UWidgetBlueprintLibrary.MakeBrushFromMaterial(material, (int)Disc, (int)Disc));
        ring = image.GetDynamicMaterial();
        // The arrow at the edge pointing at the part lit.
        ring?.SetScalarParameterValue("ArrowScale", 1);
        return image;
    }

    /// <summary>Text in one of the game's text styles, or the wheel's own look if the style isn't in the game.</summary>
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

    /// <summary>Gives a button the game's list button look (false if it isn't in the game).</summary>
    bool GameButton(UButton button)
    {
        var normal = buttonNormal;
        var hovered = buttonHovered;
        var pressed = buttonPressed;
        if (normal == null || hovered == null || pressed == null) return false;
        // Roomy: the art's light rim is made for wide list rows, and with little space inside it reads as a second button.
        var padding = new FMargin { Left = 36, Top = 12, Right = 36, Bottom = 12 };
        button.SetStyle(new FButtonStyle
        {
            Normal = UWidgetBlueprintLibrary.MakeBrushFromMaterial(normal, 32, 32),
            Hovered = UWidgetBlueprintLibrary.MakeBrushFromMaterial(hovered, 32, 32),
            Pressed = UWidgetBlueprintLibrary.MakeBrushFromMaterial(pressed, 32, 32),
            Disabled = UWidgetBlueprintLibrary.MakeBrushFromMaterial(normal, 32, 32),
            NormalPadding = padding,
            PressedPadding = padding,
        });
        return true;
    }

    static FButtonStyle Invisible()
    {
        // Not named "none": Unreal names ignore case, and a variable named None breaks the compiled code.
        var blank = new FSlateBrush { DrawAs = ESlateBrushDrawType.NoDrawType };
        return new FButtonStyle { Normal = blank, Hovered = blank, Pressed = blank, Disabled = blank, NormalPadding = new FMargin(), PressedPadding = new FMargin() };
    }

    /// <summary>Puts a widget of a size in the middle of the canvas.</summary>
    static bool Place(UCanvasPanel canvas, UWidget widget, float width, float height)
    {
        var slot = canvas.AddChildToCanvas(widget);
        if (slot == null) return false;
        slot.SetSize(new FVector2D { X = width, Y = height });
        slot.SetPosition(new FVector2D { X = Size / 2, Y = Size / 2 });
        slot.SetAlignment(new FVector2D { X = 0.5f, Y = 0.5f });
        return true;
    }

    static FButtonStyle ButtonStyle(bool lit)
    {
        var idle = Rounded(new FLinearColor { R = 0.07f, G = 0.07f, B = 0.09f, A = 0.92f }, new FLinearColor { R = 1, G = 1, B = 1, A = 0.18f }, 1, -1);
        var gold = Rounded(Gold(1), new FLinearColor { R = 1, G = 0.95f, B = 0.8f, A = 1 }, 2, -1);
        // The highlighted pill grows a little.
        var padding = lit ? new FMargin { Left = 18, Top = 5, Right = 18, Bottom = 5 } : new FMargin { Left = 12, Top = 2, Right = 12, Bottom = 2 };
        return new FButtonStyle
        {
            Normal = lit ? gold : idle,
            Hovered = gold,
            Pressed = gold,
            Disabled = idle,
            NormalPadding = padding,
            PressedPadding = padding,
        };
    }

    /// <summary>A filled rounded box; a negative radius rounds the ends fully (a pill).</summary>
    static FSlateBrush Rounded(FLinearColor fill, FLinearColor outline, float outlineWidth, float radius) => new FSlateBrush
    {
        DrawAs = ESlateBrushDrawType.RoundedBox,
        TintColor = Color(fill),
        OutlineSettings = new FSlateBrushOutlineSettings
        {
            CornerRadii = new FVector4 { X = radius, Y = radius, Z = radius, W = radius },
            Color = Color(outline),
            Width = outlineWidth,
            RoundingType = radius < 0 ? ESlateBrushRoundingType.HalfHeightRadius : ESlateBrushRoundingType.FixedRadius,
        },
    };

    static FSlateColor Color(FLinearColor color) => new FSlateColor { SpecifiedColor = color, ColorUseRule = ESlateColorStylingMode.UseColor_Specified };
    static FLinearColor Gold(float alpha) => new FLinearColor { R = 0.95f, G = 0.7f, B = 0.2f, A = alpha };
    static FLinearColor White() => new FLinearColor { R = 0.95f, G = 0.95f, B = 0.95f, A = 1 };
    static FLinearColor Dark() => new FLinearColor { R = 0.08f, G = 0.06f, B = 0.03f, A = 1 };

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

/// <summary>A button that plays one emote.</summary>
public class EmoteButton : UButton
{
    int index;
    ModActor? mod;

    public void Setup(ModActor? owner)
    {
        mod = owner;
        OnClicked += Clicked;
    }

    public void SetIndex(int emote) => index = emote;

    void Clicked() => mod?.PlayEmote(index);
}
