using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.Slate;
using UE.SlateCore;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// The mod list (F10): every mod found, whether it runs and how long it took to start, with buttons to turn it on or
/// off, move it in the start order, and change its options.
/// </summary>
public class ModList : ScreenWidget
{
    const float Width = 1080;
    const int ViewportLayer = 1000;
    const float Height = 700;
    // A row's name and status columns: fixed, so long names don't push into the buttons.
    const float NameWidth = 320;
    const float StatusWidth = 280;

    ModManager? manager;
    UVerticalBox? rows;
    UTextBlock? notice;

    public static ModList? Open(ModManager manager)
    {
        var list = UWidgetBlueprintLibrary.Create(manager, Unreal.ClassOf<ModList>(), World.PlayerController(manager)) as ModList;
        if (list == null) return null;
        list.manager = manager;
        if (!list.Build()) return null;
        list.Refresh();
        // On the whole viewport, above everything: on the player's screen, the game's menu (where its Mods button is)
        // covers it. Centred like Emoticons' wheel: the slot gets the list's size, then the anchors and alignment again,
        // since setting the size resets them to the top left.
        var middle = new FVector2D { X = 0.5f, Y = 0.5f };
        list.AddToViewport(ViewportLayer);
        list.SetDesiredSizeInViewport(new FVector2D { X = Width, Y = Height });
        list.SetAnchorsInViewport(new FAnchors { Minimum = middle, Maximum = middle });
        list.SetAlignmentInViewport(middle);
        return list;
    }

    public void Close() => RemoveFromParent();

    /// <summary>Lists the mods again, as they are now.</summary>
    public void Refresh()
    {
        var tree = WidgetTree;
        if (manager == null || rows == null || tree == null) return;
        rows.ClearChildren();
        var lines = "";
        if (manager.Notice != "") lines += manager.Notice;
        if (manager.Warning != "") lines += (lines != "" ? "\n" : "") + manager.Warning;
        if (manager.Stopped != "") lines += (lines != "" ? "\n" : "") + manager.Stopped;
        notice?.SetText(lines);
        notice?.SetVisibility(lines != "" ? ESlateVisibility.HitTestInvisible : ESlateVisibility.Collapsed);

        for (int i = 0; i < manager.Mods.Count; i++)
        {
            var row = UGameplayStatics.SpawnObject(Unreal.ClassOf<UHorizontalBox>(), tree) as UHorizontalBox;
            if (row == null) continue;
            var state = manager.States[i];
            var running = state == "Running";
            var name = Label(tree, ModManager.Short(manager.Mods[i]), 15, White());
            var status = Label(tree, running ? $"{state} ({UKismetTextLibrary.Conv_DoubleToText(manager.Times[i], ERoundingMode.HalfToEven, false, true, 1, 324, 0, 1)} ms)" : state, 12, StateColor(state));
            AddFixed(tree, row, name, NameWidth);
            AddFixed(tree, row, status, StatusWidth);
            AddButton(tree, row, running || state == "Couldn't load" || state == "Couldn't start" ? "Turn off" : "Turn on", i, "toggle", "");
            AddButton(tree, row, "Up", i, "up", "");
            AddButton(tree, row, "Down", i, "down", "");
            rows.AddChildToVerticalBox(row)?.SetPadding(new FMargin { Top = 6 });

            // Its options, under it.
            var options = manager.Options(i);
            if (options.Count == 0) continue;
            var optionRow = UGameplayStatics.SpawnObject(Unreal.ClassOf<UWrapBox>(), tree) as UWrapBox;
            if (optionRow == null) continue;
            for (int o = 0; o < options.Count; o++)
            {
                var on = manager.OptionValues[o] == "true";
                AddButton(tree, optionRow, $"{options[o]}: {(on ? "on" : "off")}", i, on ? "option-off" : "option-on", options[o]);
            }
            rows.AddChildToVerticalBox(optionRow)?.SetPadding(new FMargin { Left = 24, Top = 2 });
        }
        if (manager.Mods.Count == 0)
        {
            var empty = Label(tree, "No mods found in Paks\\~mods", 14, Grey());
            if (empty != null) rows.AddChildToVerticalBox(empty);
        }
    }

    public void Act(int index, string action, string option)
    {
        if (manager == null) return;
        switch (action)
        {
            case "close":
                manager.CloseList();
                break;
            case "restart":
                manager.Restart();
                break;
            case "toggle":
                manager.SetOn(index, manager.States[index] != "Running" && manager.States[index] != "Couldn't load" && manager.States[index] != "Couldn't start");
                break;
            case "up":
                manager.Move(index, -1);
                break;
            case "down":
                manager.Move(index, 1);
                break;
            case "option-on":
                manager.SetOption(index, option, "true");
                break;
            case "option-off":
                manager.SetOption(index, option, "false");
                break;
        }
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
        var header = UGameplayStatics.SpawnObject(Unreal.ClassOf<UHorizontalBox>(), tree) as UHorizontalBox;
        var scroll = UGameplayStatics.SpawnObject(Unreal.ClassOf<UScrollBox>(), tree) as UScrollBox;
        rows = UGameplayStatics.SpawnObject(Unreal.ClassOf<UVerticalBox>(), tree) as UVerticalBox;
        var title = Label(tree, $"Mods - BetterBlueprintLoader {ModManager.Version}", 18, Gold(1));
        var hint = Label(tree, "Turning mods on/off might take a moment/restart to apply still.", 11, Grey());
        notice = Label(tree, "", 12, new FLinearColor { R = 1, G = 0.75f, B = 0.3f, A = 1 });
        if (panel == null || column == null || header == null || scroll == null || rows == null || title == null || hint == null || notice == null) return false;

        var titleSlot = header.AddChildToHorizontalBox(title);
        titleSlot?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
        titleSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        AddButton(tree, header, "Restart mods", -1, "restart", "");
        AddButton(tree, header, "Close", -1, "close", "");
        title.SetJustification(ETextJustify.Left);
        hint.SetJustification(ETextJustify.Left);
        notice.SetJustification(ETextJustify.Left);
        notice.SetAutoWrapText(true);
        column.AddChildToVerticalBox(header);
        column.AddChildToVerticalBox(hint)?.SetPadding(new FMargin { Top = 2 });
        column.AddChildToVerticalBox(notice)?.SetPadding(new FMargin { Top = 6 });
        scroll.AddChild(rows);
        var scrollSlot = column.AddChildToVerticalBox(scroll);
        scrollSlot?.SetSize(new FSlateChildSize { Value = 1, SizeRule = ESlateSizeRule.Fill });
        scrollSlot?.SetPadding(new FMargin { Top = 8 });

        panel.SetBrush(Rounded(new FLinearColor { R = 0.03f, G = 0.03f, B = 0.04f, A = 0.94f }, Gold(0.6f), 2, 8));
        panel.SetPadding(new FMargin { Left = 18, Top = 14, Right = 18, Bottom = 14 });
        panel.AddChild(column);
        var size = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        if (size == null) return false;
        size.SetWidthOverride(Width);
        size.SetHeightOverride(Height);
        size.AddChild(panel);
        tree.RootWidget = size;
        return true;
    }

    /// <summary>Adds text to a row in a column of its own width, cut off if it's longer.</summary>
    static void AddFixed(UWidgetTree tree, UHorizontalBox row, UTextBlock? text, float width)
    {
        var box = UGameplayStatics.SpawnObject(Unreal.ClassOf<USizeBox>(), tree) as USizeBox;
        if (box == null || text == null) return;
        box.SetWidthOverride(width);
        box.SetClipping(EWidgetClipping.ClipToBounds);
        text.SetJustification(ETextJustify.Left);
        box.AddChild(text);
        row.AddChildToHorizontalBox(box)?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
    }

    void AddButton(UWidgetTree tree, UPanelWidget parent, string text, int index, string action, string option)
    {
        var button = UGameplayStatics.SpawnObject(Unreal.ClassOf<ModButton>(), tree) as ModButton;
        var label = Label(tree, text, 12, White());
        if (button == null || label == null) return;
        button.Setup(this, index, action, option);
        button.SetStyle(new FButtonStyle
        {
            Normal = Rounded(new FLinearColor { R = 0.1f, G = 0.1f, B = 0.12f, A = 1 }, new FLinearColor { R = 1, G = 1, B = 1, A = 0.15f }, 1, 4),
            Hovered = Rounded(new FLinearColor { R = 0.2f, G = 0.16f, B = 0.06f, A = 1 }, Gold(0.9f), 1, 4),
            Pressed = Rounded(new FLinearColor { R = 0.3f, G = 0.22f, B = 0.06f, A = 1 }, Gold(1), 1, 4),
            NormalPadding = new FMargin { Left = 8, Top = 2, Right = 8, Bottom = 2 },
            PressedPadding = new FMargin { Left = 8, Top = 3, Right = 8, Bottom = 1 },
        });
        button.AddChild(label);
        if (parent is UHorizontalBox row)
        {
            var slot = row.AddChildToHorizontalBox(button);
            slot?.SetPadding(new FMargin { Left = 6 });
            slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
        }
        else if (parent is UWrapBox wrap) wrap.AddChildToWrapBox(button)?.SetPadding(new FMargin { Right = 6, Bottom = 4 });
    }

    static FLinearColor StateColor(string state)
    {
        if (state == "Running") return new FLinearColor { R = 0.5f, G = 0.9f, B = 0.5f, A = 1 };
        if (state == "Off") return Grey();
        return new FLinearColor { R = 1, G = 0.45f, B = 0.35f, A = 1 };
    }

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
    static FLinearColor Grey() => new FLinearColor { R = 0.6f, G = 0.6f, B = 0.6f, A = 1 };

    static UTextBlock? Label(UObject outer, string text, int size, FLinearColor color)
    {
        var label = UGameplayStatics.SpawnObject(Unreal.ClassOf<UTextBlock>(), outer) as UTextBlock;
        if (label == null) return null;
        label.SetText(text);
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

/// <summary>A button of the mod list: what it does, to which mod.</summary>
public class ModButton : UButton
{
    ModList? list;
    int index;
    string action;
    string option;

    public void Setup(ModList owner, int mod, string what, string optionName)
    {
        list = owner;
        index = mod;
        action = what;
        option = optionName;
        OnClicked += Clicked;
    }

    void Clicked() => list?.Act(index, action, option);
}
