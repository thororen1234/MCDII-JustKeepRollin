using System.Collections.Generic;
using NeoRune;
using UE.Angelscript;
using UE.CommonUI;
using UE.CoreUObject;
using UE.Engine;
using UE.GameplayAbilities;
using UE.GameplayTags;
using UE.InputCore;
using UE.SlateCore;
using UE.SpicewoodGAS;
using UE.SpicewoodUI;
using UE.UMG;

namespace StatsOverlay;

/// <summary>
/// Stats Overlay: your Emeralds, Echo Shards, Enchantment Points, the Soul Storm going on and more, just right of the
/// hotbar, fading in and out with it. Each display is turned on or off in BetterBlueprintLoader's Mods tab.
/// </summary>
[ModSetting.Heading("Displays")]
[ModSetting.Toggle(EmeraldsSetting, "Emeralds", Default = true)]
[ModSetting.Toggle(EchoShardsSetting, "Echo Shards", Default = true)]
[ModSetting.Toggle(EnchantmentSetting, "Enchantment Points", Default = true,
    Description = "Points you can spend on enchantments.")]
[ModSetting.Toggle(SoulStormSetting, "Soul Storm", Default = true,
    Description = "Where a Soul Storm is going on and how long it has left. Hidden while there's none.")]
[ModSetting.Toggle(XPSetting, "Experience", Default = false,
    Description = "XP towards the next level.")]
[ModSetting.Toggle(HealthSetting, "Health", Default = false,
    Description = "Your health as numbers.")]
[ModSetting.Heading("Look")]
[ModSetting.Select(LayoutSetting, "Layout", "Side by side", "One under the other")]
[ModSetting.Slider(SizeSetting, "Size", Default = 1, Min = 0.5, Max = 2, Step = 0.05, Percentage = true)]
public class ModActor : AActor, ISettingsEvents
{
    const string EmeraldsSetting = "show_emeralds";
    const string EchoShardsSetting = "show_echo_shards";
    const string EnchantmentSetting = "show_enchantment_points";
    const string SoulStormSetting = "show_soul_storm";
    const string XPSetting = "show_xp";
    const string HealthSetting = "show_health";
    const string LayoutSetting = "layout";
    const string SizeSetting = "size";

    // The displays, in the order they're shown.
    const int Emeralds = 0;
    const int EchoShards = 1;
    const int Enchantment = 2;
    const int SoulStorm = 3;
    const int XP = 4;
    const int Health = 5;

    const string HotbarClass = "/Game/Spicewood/UI/HUD/Player/Hotbar/Solo/W_SinglePlayerHotbar.W_SinglePlayerHotbar_C";
    // The hotbar's lower bar (potion, XP bar, dodge): the overlay sits just past its right end.
    const string LowerBarClass = "/Game/Spicewood/UI/HUD/Player/Hotbar/Solo/W_Hotbar_Lower.W_Hotbar_Lower_C";
    // Space between the lower bar and the overlay.
    const float Gap = 12;
    // How often the hotbar is looked for while it isn't found, and the numbers read.
    const float FindInterval = 1f;
    const float UpdateInterval = 0.25f;
    // How often the Soul Storm is looked up (it looks through the game's widgets).
    const float StormInterval = 1f;
    // A Soul Storm countdown longer than this isn't one.
    const double LongestStorm = 6 * 3600;

    readonly List<bool> shownDisplays = new() { true, true, true, true, false, false };
    bool column;
    float size = 1;

    StatsBar? bar;
    UUserWidget? hotbar;
    UWidget? anchor;
    TSubclassOf<UUserWidget>? hotbarClass;
    TSubclassOf<UWidget>? lowerBarClass;
    bool loaded;
    double nextFind;
    double nextUpdate;
    double nextStorm;
    string stormText = "";
    // What the log last said about the Soul Storm, to log only changes.
    string stormLogged = "";

    protected override void ReceiveBeginPlay()
    {
        // Menus can pause the game: the overlay still has to hide with the hotbar.
        SetTickableWhenPaused(true);
    }

    public override void ReceiveTick(float deltaSeconds)
    {
        var now = World.RealTime(this);
        if (now >= nextFind)
        {
            nextFind = now + FindInterval;
            FindHotbar();
        }
        if (bar == null) return;
        if (now >= nextStorm)
        {
            nextStorm = now + StormInterval;
            stormText = shownDisplays[SoulStorm] ? SoulStormText() : "";
        }
        if (now >= nextUpdate)
        {
            nextUpdate = now + UpdateInterval;
            Update();
        }
        Follow();
    }

    void FindHotbar()
    {
        if (hotbar != null && UKismetSystemLibrary.IsValid(hotbar) && anchor != null && UKismetSystemLibrary.IsValid(anchor)) return;
        if (!loaded)
        {
            loaded = true;
            hotbarClass = Unreal.LoadClass<UUserWidget>(HotbarClass);
            lowerBarClass = Unreal.LoadClass<UWidget>(LowerBarClass);
        }
        hotbar = null;
        anchor = null;
        if (hotbarClass == null) return;
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var found, hotbarClass, false);
        foreach (var widget in found)
            if (widget != null && widget.IsVisible())
            {
                hotbar = widget;
                break;
            }
        if (hotbar == null) return;
        if (lowerBarClass != null) anchor = GameUI.FindOfClass(hotbar, lowerBarClass);
        if (anchor == null) anchor = hotbar;
        if (bar != null) return;
        bar = StatsBar.Create(this);
        if (bar == null) return;
        bar.Setup(new List<string>
        {
            "/OreUI/UI/Icons/Currency/T_Emerald_UI.T_Emerald_UI",
            "/OreUI/UI/Icons/Currency/T_UI_SpringStone.T_UI_SpringStone",
            "/OreUI/UI/Icons/Currency/T_UI_EnchantmentPoints.T_UI_EnchantmentPoints",
            "/OreUI/UI/Widget/Tooltip/Modules/T_UI_Icon_SoulStormPip.T_UI_Icon_SoulStormPip",
            "/OreUI/UI/Icons/Effects/T_UI_Icon_Effect_ExperienceIncrease.T_UI_Icon_Effect_ExperienceIncrease",
            "/OreUI/UI/Icons/Effects/T_UI_Icon_Effect_HealthBoost.T_UI_Icon_Effect_HealthBoost",
        });
        bar.Style(column, size);
        bar.ShowAt(new FVector2D(), new FVector2D(), ScreenWidget.UnderGameUI);
        Log.Write($"Overlay made next to {UKismetSystemLibrary.GetObjectName(anchor)} in {World.LevelName(this)}");
    }

    /// <summary>Keeps the overlay just right of the lower bar, centred on it, and as faded as the hotbar.</summary>
    void Follow()
    {
        if (bar == null) return;
        float opacity = 0;
        var geometry = new FGeometry();
        var size = new FVector2D();
        hiddenBy = "the lower bar is gone";
        if (anchor != null && UKismetSystemLibrary.IsValid(anchor))
        {
            opacity = ShownOpacity(anchor);
            geometry = anchor.GetCachedGeometry();
            size = USlateBlueprintLibrary.GetLocalSize(geometry);
            if (opacity > 0.01f && size.X <= 0) hiddenBy = "the lower bar has no size";
        }
        if (opacity <= 0.01f || size.X <= 0)
        {
            bar.SetVisibility(ESlateVisibility.Collapsed);
            Note($"hidden: {hiddenBy}");
            return;
        }
        bar.SetVisibility(ESlateVisibility.HitTestInvisible);
        bar.SetRenderOpacity(opacity);
        USlateBlueprintLibrary.LocalToViewport(this, geometry, new FVector2D { X = size.X, Y = size.Y / 2 }, out var pixel, out var position);
        bar.SetAlignmentInViewport(new FVector2D { X = 0, Y = 0.5 });
        bar.SetPositionInViewport(new FVector2D { X = position.X + Gap, Y = position.Y }, false);
        Note($"shown at {position.X:0},{position.Y:0} (pixel {pixel.X:0},{pixel.Y:0}), bar {size.X:0}x{size.Y:0}, overlay {bar.GetDesiredSize().X:0}x{bar.GetDesiredSize().Y:0}, in viewport {bar.IsInViewport()}");
    }

    // Why the overlay was last hidden, and what the log last said about it (to log only changes, and not forever).
    string hiddenBy = "";
    string noted = "";
    int notes;

    void Note(string state)
    {
        // Positions change while the hotbar animates: compare without them.
        var kind = UKismetStringLibrary.Contains(state, "shown", false, false) ? "shown" : state;
        if (kind == noted || notes >= 40) return;
        noted = kind;
        notes++;
        Log.Write($"Overlay {state}");
    }

    /// <summary>
    /// How visible a widget is on screen: 0 when it or anything it's in is hidden, else its opacity times theirs (the
    /// hotbar fades in and out).
    /// </summary>
    float ShownOpacity(UWidget start)
    {
        float opacity = 1;
        UObject? at = start;
        for (int i = 0; i < 64 && at != null; i++)
        {
            if (at is UWidget widget)
            {
                if (!widget.IsVisible())
                {
                    hiddenBy = $"{UKismetSystemLibrary.GetObjectName(widget)} isn't visible";
                    return 0;
                }
                // Not whether a screen is active: the hotbar's never is, even on screen.
                opacity *= widget.GetRenderOpacity();
                UObject? parent = widget.GetParent();
                if (parent == null) parent = UKismetSystemLibrary.GetOuterObject(widget);
                at = parent;
            }
            // A user widget's root is in its widget tree, which is in the user widget.
            else if (at is UWidgetTree) at = UKismetSystemLibrary.GetOuterObject(at);
            else break;
        }
        if (opacity <= 0.01f) hiddenBy = "faded out";
        return opacity;
    }

    void Update()
    {
        if (bar == null) return;
        UATR_Currency? currency = null;
        UATR_XP? xp = null;
        UATR_Health? health = null;
        var asc = UAbilitySystemBlueprintLibrary.GetAbilitySystemComponent(World.Player(this));
        if (asc != null)
            foreach (var set in asc.SpawnedAttributes)
            {
                if (set is UATR_Currency currencySet) currency = currencySet;
                else if (set is UATR_XP xpSet) xp = xpSet;
                else if (set is UATR_Health healthSet) health = healthSet;
            }

        bar.Set(Emeralds, shownDisplays[Emeralds] && currency != null ? Number(currency.Emeralds.CurrentValue) : "");
        bar.Set(EchoShards, shownDisplays[EchoShards] && currency != null ? Number(currency.SpringStone.CurrentValue) : "");
        bar.Set(Enchantment, shownDisplays[Enchantment] && xp != null ? Number(xp.EnchantmentPoints.CurrentValue) : "");
        bar.Set(SoulStorm, stormText);
        bar.Set(XP, shownDisplays[XP] && xp != null
            ? $"{Number(xp.XP.CurrentValue)} / {Number(xp.XPForNextLevel.CurrentValue)}" : "");
        bar.Set(Health, shownDisplays[Health] && health != null
            ? $"{Number(UKismetMathLibrary.FCeil(health.Health.CurrentValue))} / {Number(health.HealthMax.CurrentValue)}" : "");
    }

    /// <summary>The area with a Soul Storm and the time it has left, or empty while there's none.</summary>
    string SoulStormText()
    {
        var areas = new List<FGameplayTag>();
        // An actor is in its level, which is in the world.
        var world = UKismetSystemLibrary.GetOuterObject(UKismetSystemLibrary.GetOuterObject(this)) as UWorld;
        if (world != null)
        {
            USWSoulStormUtilLibrary.GetAllAreasWithOngoingSoulStorm(world, out var ongoing);
            areas = ongoing;
        }
        var left = StormTimeLeft();
        var area = areas.Count > 0 ? areas[0].TagName.ToString() : "";
        var state = $"areas {areas.Count} {area}, countdown {(left > 0 ? Clock(left) : "none")}";
        if (state != stormLogged)
        {
            stormLogged = state;
            Log.Write($"Soul Storm: {state}");
        }
        if (areas.Count == 0 && left <= 0) return "";
        var name = area != "" ? AreaName(area) : "Soul Storm";
        return left > 0 ? $"{name} {Clock(left)}" : name;
    }

    /// <summary>
    /// Seconds left in the Soul Storm, from the game's own trackers (the HUD's, or the map's), which count down to its end;
    /// 0 when none counts down.
    /// </summary>
    double StormTimeLeft()
    {
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var trackers, Unreal.ClassOf<UAS_SoulStormTracker>(), false);
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var mapTrackers, Unreal.ClassOf<UAS_SoulStormMapTracker>(), false);
        foreach (var widget in mapTrackers) trackers.Add(widget);
        foreach (var widget in trackers)
        {
            USpicewoodDateTimeTextBlock? timer = null;
            if (widget is UAS_SoulStormTracker tracker) timer = tracker.TimerTextBlock;
            else if (widget is UAS_SoulStormMapTracker mapTracker) timer = mapTracker.TimerTextBlock;
            if (timer == null || !timer.IsCountingDown()) continue;
            var end = timer.GetDateTime();
            var left = UKismetMathLibrary.GetTotalSeconds(UKismetMathLibrary.Subtract_DateTimeDateTime(end, UKismetMathLibrary.UtcNow()));
            if (left > 0 && left < LongestStorm) return left;
            // In case it counts in local time.
            left = UKismetMathLibrary.GetTotalSeconds(UKismetMathLibrary.Subtract_DateTimeDateTime(end, UKismetMathLibrary.Now()));
            if (left > 0 && left < LongestStorm) return left;
        }
        return 0;
    }

    /// <summary>An area's name from its tag: its last part, with spaces between words ("SW.Area.FrostedPeaks" is "Frosted Peaks").</summary>
    static string AreaName(string tag)
    {
        var dot = UKismetStringLibrary.FindSubstring(tag, ".", false, true, -1);
        var last = dot >= 0 ? UKismetStringLibrary.RightChop(tag, dot + 1) : tag;
        var name = "";
        var lowerBefore = false;
        for (int i = 0; i < UKismetStringLibrary.Len(last); i++)
        {
            var letter = UKismetStringLibrary.GetSubstring(last, i, 1);
            if (letter == "_")
            {
                name += " ";
                lowerBefore = false;
                continue;
            }
            var upper = letter != UKismetStringLibrary.ToLower(letter);
            if (upper && lowerBefore) name += " ";
            name += letter;
            lowerBefore = !upper;
        }
        return name;
    }

    /// <summary>A whole number with thousands separators, e.g. 43,282.</summary>
    static string Number(double value) =>
        UKismetTextLibrary.Conv_TextToString(UKismetTextLibrary.Conv_Int64ToText(UKismetMathLibrary.Round64(value), false, true, 1, 324));

    /// <summary>Seconds as m:ss, or h:mm:ss from an hour.</summary>
    static string Clock(double seconds)
    {
        var total = UKismetMathLibrary.FFloor(seconds);
        var hours = total / 3600;
        var minutes = total / 60 % 60;
        var secs = total % 60;
        var tail = $"{(secs < 10 ? "0" : "")}{secs}";
        if (hours > 0) return $"{hours}:{(minutes < 10 ? "0" : "")}{minutes}:{tail}";
        return $"{minutes}:{tail}";
    }

    public void OnSettingChanged(string id, string value)
    {
        if (id == EmeraldsSetting) shownDisplays[Emeralds] = ModSettings.ToBool(value);
        else if (id == EchoShardsSetting) shownDisplays[EchoShards] = ModSettings.ToBool(value);
        else if (id == EnchantmentSetting) shownDisplays[Enchantment] = ModSettings.ToBool(value);
        else if (id == SoulStormSetting) shownDisplays[SoulStorm] = ModSettings.ToBool(value);
        else if (id == XPSetting) shownDisplays[XP] = ModSettings.ToBool(value);
        else if (id == HealthSetting) shownDisplays[Health] = ModSettings.ToBool(value);
        else if (id == LayoutSetting) column = ModSettings.ToInt(value) == 1;
        else if (id == SizeSetting) size = (float)UKismetMathLibrary.FClamp(ModSettings.ToNumber(value), 0.5, 2);
        else return;
        Restyle();
    }

    public void OnSettingsReset()
    {
        for (int i = 0; i < shownDisplays.Count; i++) shownDisplays[i] = i < XP;
        column = false;
        size = 1;
        Restyle();
    }

    /// <summary>Shows a settings change on the next tick.</summary>
    void Restyle()
    {
        bar?.Style(column, size);
        nextUpdate = 0;
        nextStorm = 0;
    }

    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey) { }
    public void OnButtonPressed(string id) { }
}

[Asset("/Game/Mods/BlueprintLoader/BPI_ModSettings")]
public interface ISettingsEvents
{
    void OnButtonPressed(string Id);
    void OnKeybindChanged(string Id, FKey Key, FKey SecondaryKey);
    void OnSettingChanged(string Id, string Value);
    void OnSettingsReset();
}
