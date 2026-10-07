using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.Minimap;
using UE.SlateCore;
using UE.UMG;

namespace StatsOverlay;

/// <summary>
/// The overlay: an icon and a number for each display that's turned on, side by side or one under the other, in the
/// game's font. <see cref="Set"/> gives a display its text; an empty text hides it.
/// </summary>
public class StatsBar : ScreenWidget
{
    const string FontPath = "/Game/Spicewood/Fonts/Spicewood/FF_Spicewood.FF_Spicewood";
    const string Typeface = "Sixteen Bold";
    // Sizes at 100%.
    const float TextSize = 18;
    const float IconSize = 26;
    const float IconGap = 4;
    const float ItemGap = 14;

    // The game's assets, loaded before any widget is made (loading can let the garbage collector run).
    UObject? font;
    List<UTexture2D?> icons = new();

    UPanelWidget? root;
    List<UHorizontalBox?> items = new();
    List<UTextBlock?> texts = new();
    // What each display shows now (empty: hidden), kept to fill the widgets when they're made again.
    List<string> shown = new();
    bool column;
    float scale;

    /// <summary>An overlay not made yet: keep it in a field, then <see cref="Setup"/> it (which loads the game's assets).</summary>
    public static StatsBar? Create(UObject context) =>
        UWidgetBlueprintLibrary.Create(context, Unreal.ClassOf<StatsBar>(), World.PlayerController(context)) as StatsBar;

    /// <summary>Loads the font and an icon per display (<paramref name="iconPaths"/>, in display order), and makes the widgets.</summary>
    public void Setup(List<string> iconPaths)
    {
        scale = 1;
        font = Load(FontPath);
        foreach (var path in iconPaths)
        {
            icons.Add(Load(path) as UTexture2D);
            shown.Add("");
        }
        Build();
    }

    /// <summary>Side by side (false) or one under the other (true), and the size (1 = 100%).</summary>
    public void Style(bool asColumn, float size)
    {
        if (asColumn == column && size == scale) return;
        column = asColumn;
        scale = size;
        Build();
    }

    /// <summary>A display's text; empty hides it.</summary>
    public void Set(int display, string text)
    {
        if (display < 0 || display >= shown.Count || shown[display] == text) return;
        shown[display] = text;
        Fill(display);
    }

    void Fill(int display)
    {
        if (display >= items.Count) return;
        var item = items[display];
        var text = texts[display];
        if (item == null || text == null) return;
        var value = shown[display];
        if (value != "") text.SetText(value);
        item.SetVisibility(value == "" ? ESlateVisibility.Collapsed : ESlateVisibility.HitTestInvisible);
    }

    void Build()
    {
        var tree = Ui.Tree(this);
        if (tree == null) return;
        items.Clear();
        texts.Clear();
        if (column) root = Ui.Column(tree);
        else root = Ui.Row(tree);
        tree.RootWidget = root;
        if (root == null) return;
        for (int i = 0; i < icons.Count; i++)
        {
            var item = Ui.Row(tree);
            var text = Ui.Text(tree, "", 1);
            items.Add(item);
            texts.Add(text);
            if (item == null || text == null) continue;
            text.SetFont(Font());
            text.SetShadowOffset(new FVector2D { X = 1, Y = 1 });
            text.SetShadowColorAndOpacity(Ui.Color(0, 0, 0, 0.6f));
            var icon = UGameplayStatics.SpawnObject(Unreal.ClassOf<UImage>(), tree) as UImage;
            if (icon != null && icons[i] != null)
            {
                icon.SetBrush(UWidgetBlueprintLibrary.MakeBrushFromTexture(icons[i], 0, 0));
                icon.SetDesiredSizeOverride(new FVector2D { X = IconSize * scale, Y = IconSize * scale });
                var iconSlot = item.AddChildToHorizontalBox(icon);
                iconSlot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
                iconSlot?.SetPadding(new FMargin { Right = IconGap * scale });
            }
            item.AddChildToHorizontalBox(text)?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);

            var gap = i == 0 ? 0 : ItemGap * scale;
            if (root is UVerticalBox list) list.AddChildToVerticalBox(item)?.SetPadding(new FMargin { Top = gap / 3 });
            else if (root is UHorizontalBox line)
            {
                var slot = line.AddChildToHorizontalBox(item);
                slot?.SetPadding(new FMargin { Left = gap });
                slot?.SetVerticalAlignment(EVerticalAlignment.VAlign_Center);
            }
            Fill(i);
        }
    }

    /// <summary>The game's HUD font with an outline, or the default font if it isn't in the game.</summary>
    FSlateFontInfo Font()
    {
        var info = UMinimapHelpersLibrary.GetDefaultFont();
        if (font != null)
        {
            info.FontObject = font;
            info.TypefaceFontName = Typeface;
        }
        info.Size = TextSize * scale;
        var outline = info.OutlineSettings;
        outline.OutlineSize = 1;
        outline.OutlineColor = Ui.Color(0, 0, 0, 1);
        info.OutlineSettings = outline;
        return info;
    }

    UObject? Load(string path) =>
        UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(path)));
}
