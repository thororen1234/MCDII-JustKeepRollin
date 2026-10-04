using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.UMG;

namespace CustomSkins;

/// <summary>
/// For a while, logs each kind of widget that shows on screen, the first time it does: open a menu meanwhile to see
/// what it's made of. Keeps running while menus pause the game.
/// </summary>
public class UiSpy : AActor
{
    const float ScanInterval = 0.5f;
    const float Duration = 60f;

    List<string> seen = new();
    List<string> found = new();
    PausableTimer? timer;
    double endsAt;

    public static UiSpy? Start(UObject context)
    {
        var spy = World.Spawn(context, Unreal.ClassOf<UiSpy>(), new FVector()) as UiSpy;
        if (spy == null) return null;
        spy.Begin();
        return spy;
    }

    void Begin()
    {
        endsAt = World.RealTime(this) + Duration;
        timer = PausableTimer.Start(this, ScanInterval, loop: true);
        if (timer != null) timer.Fired += Scan;
        Log.Write($"Logging the widgets on screen for {Duration} seconds: open the menu to look at now");
    }

    void Scan()
    {
        found.Clear();
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var widgets, Unreal.ClassOf<UUserWidget>(), false);
        foreach (var widget in widgets)
        {
            if (widget == null || !widget.IsVisible()) continue;
            var name = UKismetSystemLibrary.Conv_SoftClassReferenceToString(UKismetSystemLibrary.Conv_ClassToSoftClassReference(UGameplayStatics.GetObjectClass(widget)));
            if (seen.Contains(name)) continue;
            seen.Add(name);
            found.Add($"Widget {name} ({UKismetSystemLibrary.GetObjectName(widget)})");
        }
        if (found.Count > 0) Log.WriteAll(found);
        if (World.RealTime(this) < endsAt) return;
        Log.Write("Done logging widgets");
        timer?.Stop();
        K2_DestroyActor();
    }
}
