using System.Collections.Generic;
using NeoRune;
using UE.AssetRegistry;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;
using UE.SWCorePlatform;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// Starts the mods in a level and keeps track of them: the order they start in, which are turned off, which crashed
/// the game while starting, how long each took, and their options. One per level, spawned by LoaderComponent.
/// </summary>
public class ModManager : AActor
{
    public const string Version = "1.0";
    // The game this loader's R1 hook was made from, as the game gives its version ("50349472:releases/r2:<commit>"):
    // another build may have changed R1.
    const string BuiltForGame = "1.1.1.0";
    const string BuiltForBuild = "50349472:";
    const string SettingsSlot = "BetterBlueprintLoader";
    const string ModsPath = "/Game/Mods";
    const string ModActorName = "ModActor";
    const string ListKey = "F10";
    const string RestartKey = "F12";
    // Mods start just after the level does, not while the player controller is still starting.
    const float StartDelay = 0.1f;

    LoaderSettings? settings;
    // Per mod, in start order: its package, its class once loaded, its actor while running, its state and how long
    // it took to start (ms).
    public List<string> Mods = new();
    List<TSubclassOf<AActor>> classes = new();
    List<AActor> actors = new();
    public List<string> States = new();
    public List<double> Times = new();
    // Shown in the mod list and on the main menu: a mod that crashed the game, a newer game version.
    public string Notice = "";
    public string Warning = "";
    // Set when a mod is turned off while running: what it changed in the game can stay until the next level.
    public string Stopped = "";
    ModList? list;
    MenuLabel? label;

    protected override void ReceiveBeginPlay()
    {
        // The mod list works in menus, which pause the game.
        SetTickableWhenPaused(true);
        settings = UGameplayStatics.LoadGameFromSlot(SettingsSlot, 0) as LoaderSettings;
        if (settings == null) settings = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<LoaderSettings>()) as LoaderSettings;
        if (settings == null) return;

        // Still set: the game crashed while that mod was starting, the last time. It stays off until turned on.
        if (settings.Starting != "")
        {
            if (!settings.Crashed.Contains(settings.Starting)) settings.Crashed.Add(settings.Starting);
            Notice = $"{Short(settings.Starting)} crashed the game while starting, so it's turned off. Press {ListKey} to turn it back on.";
            Log.Write($"{settings.Starting} crashed the game while starting: turned off");
            settings.Starting = "";
            Save();
        }
        var game = UGameVersion.BuildVersion();
        if (game != "" && !UKismetStringLibrary.StartsWith(game, BuiltForBuild, ESearchCase.CaseSensitive))
            Warning = $"The game has updated since BetterBlueprintLoader was made (for {BuiltForGame}): if a level crashes while loading, update it.";
        Timer.Start(this, nameof(StartAll), StartDelay, loop: false);
    }

    public override void ReceiveTick(float deltaSeconds)
    {
        var controller = World.PlayerController(this);
        if (controller == null) return;
        if (controller.WasInputKeyJustPressed(new FKey { KeyName = ListKey }))
        {
            if (list != null) CloseList();
            else list = ModList.Open(this);
        }
        if (controller.WasInputKeyJustPressed(new FKey { KeyName = RestartKey })) Restart();
    }

    public void CloseList()
    {
        list?.Close();
        list = null;
    }

    /// <summary>Finds the mods and starts them, in the mod list's order.</summary>
    void StartAll()
    {
        if (settings == null) return;
        var found = Find();
        // The saved order first, then mods new since, which go at its end.
        foreach (var mod in settings.Order)
            if (found.Contains(mod)) Mods.Add(mod);
        foreach (var mod in found)
            if (!Mods.Contains(mod))
            {
                Mods.Add(mod);
                settings.Order.Add(mod);
            }
        for (int i = 0; i < Mods.Count; i++)
        {
            classes.Add(null);
            actors.Add(null);
            States.Add("");
            Times.Add(0);
        }
        for (int i = 0; i < Mods.Count; i++) Start(i);
        Save();
        Log.Write($"Started {Running()} of {Mods.Count} mods in {World.LevelName(this)} (game {UGameVersion.BuildVersion()})");
        if (UKismetStringLibrary.StartsWith(World.LevelName(this), "Menu", ESearchCase.IgnoreCase)) label = MenuLabel.Show(this);
    }

    /// <summary>The mods in the game: packages named ModActor under /Game/Mods, this loader's own left out.</summary>
    List<string> Find()
    {
        var mods = new List<string>();
        var registry = UAssetRegistryHelpers.GetAssetRegistry();
        if (registry == null) return mods;
        registry.ScanPathsSynchronous(new List<string> { ModsPath }, false, false);
        registry.GetAssetsByPath(ModsPath, out var assets, true, false);
        var own = $"{ModsPath}/{Unreal.ModName}/{ModActorName}";
        foreach (var asset in assets)
        {
            // A cooked game lists the class (ModActor_C), not the blueprint: a mod is a package named ModActor.
            var mod = asset.PackageName.ToString();
            if (mod.EndsWith("/" + ModActorName) && mod != own && !mods.Contains(mod)) mods.Add(mod);
        }
        return mods;
    }

    /// <summary>Starts a mod, unless it's off. Marked as starting meanwhile: a crash leaves the mark for next time.</summary>
    void Start(int index)
    {
        if (settings == null) return;
        var mod = Mods[index];
        if (settings.Crashed.Contains(mod))
        {
            States[index] = "Crashed";
            return;
        }
        if (settings.Disabled.Contains(mod))
        {
            States[index] = "Off";
            return;
        }
        settings.Starting = mod;
        Save();
        var started = UKismetMathLibrary.Now();
        if (classes[index] == null) classes[index] = Unreal.LoadClass<AActor>($"{mod}.{ModActorName}_C");
        var modClass = classes[index];
        if (modClass == null) States[index] = "Couldn't load";
        else
        {
            // Already running in this level: from another player controller, or another loader.
            var running = World.FindAll(this, modClass);
            if (running.Count > 0) actors[index] = running[0];
            else actors[index] = Spawn(mod, modClass);
            States[index] = actors[index] != null ? "Running" : "Couldn't start";
        }
        Times[index] = UKismetMathLibrary.GetTotalMilliseconds(UKismetMathLibrary.Subtract_DateTimeDateTime(UKismetMathLibrary.Now(), started));
        settings.Starting = "";
        Save();
    }

    /// <summary>Spawns a mod with its saved options on, so ModOptions finds them when the mod starts.</summary>
    AActor? Spawn(string mod, TSubclassOf<AActor> modClass)
    {
        var transform = UKismetMathLibrary.MakeTransform(new FVector(), new FRotator(), new FVector { X = 1, Y = 1, Z = 1 });
        var actor = UGameplayStatics.BeginDeferredActorSpawnFromClass(this, modClass, transform, ESpawnActorCollisionHandlingMethod.AlwaysSpawn, null, ESpawnActorScaleMethod.MultiplyWithRoot);
        if (actor == null || settings == null) return null;
        var tags = actor.Tags;
        for (int i = 0; i < settings.OptionKeys.Count; i++)
        {
            UKismetStringLibrary.Split(settings.OptionKeys[i], "|", out var owner, out var option, ESearchCase.CaseSensitive, ESearchDir.FromStart);
            if (owner == mod) tags.Add(UKismetStringLibrary.Conv_StringToName(ModOptions.Prefix + option + "=" + settings.OptionValues[i]));
        }
        actor.Tags = tags;
        return UGameplayStatics.FinishSpawningActor(actor, transform, ESpawnActorScaleMethod.MultiplyWithRoot);
    }

    /// <summary>
    /// Stops a mod: its ModActor, and every other actor and widget of a class from its folder (its timers, widget
    /// watchers, HUD, wheels...). What it changed in the game itself stays until the next level.
    /// </summary>
    void Stop(int index)
    {
        if (actors[index] != null && UKismetSystemLibrary.IsValid(actors[index])) actors[index].K2_DestroyActor();
        actors[index] = null;
        // /Game/Mods/Emoticons/ModActor: its classes are /Game/Mods/Emoticons/...
        var folder = UKismetStringLibrary.LeftChop(Mods[index], ModActorName.Length);
        foreach (var actor in World.FindAll(this, Unreal.ClassOf<AActor>()))
            if (actor != null && actor != this && FromFolder(actor, folder)) actor.K2_DestroyActor();
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var widgets, Unreal.ClassOf<UUserWidget>(), false);
        foreach (var widget in widgets)
            if (widget != null && FromFolder(widget, folder)) widget.RemoveFromParent();
    }

    static bool FromFolder(UObject thing, string folder)
    {
        var cls = UKismetSystemLibrary.Conv_SoftClassReferenceToString(UKismetSystemLibrary.Conv_ClassToSoftClassReference(UGameplayStatics.GetObjectClass(thing)));
        return UKismetStringLibrary.StartsWith(cls, folder, ESearchCase.IgnoreCase);
    }

    /// <summary>Stops every running mod and starts them again, in the mod list's order.</summary>
    public void Restart()
    {
        int count = 0;
        for (int i = 0; i < Mods.Count; i++)
            if (States[i] == "Running")
            {
                Stop(i);
                count++;
            }
        for (int i = 0; i < Mods.Count; i++)
            if (States[i] == "Running") Start(i);
        Log.Write($"Restarted {count} mods");
        Stopped = "";
        list?.Refresh();
    }

    /// <summary>Turns a mod on (starting it now) or off (stopping it now). Remembered.</summary>
    public void SetOn(int index, bool on)
    {
        if (settings == null || index < 0 || index >= Mods.Count) return;
        var mod = Mods[index];
        settings.Crashed.Remove(mod);
        settings.Disabled.Remove(mod);
        // The note about a mod turned off is about this one: it's on again.
        if (on && UKismetStringLibrary.StartsWith(Stopped, Short(mod) + " ", ESearchCase.CaseSensitive)) Stopped = "";
        if (on) Start(index);
        else
        {
            settings.Disabled.Add(mod);
            if (States[index] == "Running")
                Stopped = $"{Short(mod)} is off, but what it changed in the game (like controls) can stay until the next level.";
            Stop(index);
            States[index] = "Off";
        }
        Save();
        list?.Refresh();
        label?.Refresh();
    }

    /// <summary>Moves a mod up (-1) or down (1) the start order. Remembered; it counts from the next level or restart.</summary>
    public void Move(int index, int by)
    {
        int other = index + by;
        if (settings == null || index < 0 || other < 0 || index >= Mods.Count || other >= Mods.Count) return;
        // Swapped in place: a List passed to a method is a copy in a Blueprint.
        var mod = Mods[index];
        Mods[index] = Mods[other];
        Mods[other] = mod;
        var modClass = classes[index];
        classes[index] = classes[other];
        classes[other] = modClass;
        var actor = actors[index];
        actors[index] = actors[other];
        actors[other] = actor;
        var state = States[index];
        States[index] = States[other];
        States[other] = state;
        var time = Times[index];
        Times[index] = Times[other];
        Times[other] = time;
        // The saved order has every mod ever seen: the two swap places there too.
        int a = settings.Order.IndexOf(Mods[index]);
        int b = settings.Order.IndexOf(Mods[other]);
        if (a >= 0 && b >= 0)
        {
            settings.Order[a] = Mods[index];
            settings.Order[b] = Mods[other];
        }
        Save();
        list?.Refresh();
    }

    // The values of the options Options last listed ("true" or "false"): a field, because a List passed to a method is
    // a copy in a Blueprint.
    public List<string> OptionValues = new();

    /// <summary>A running mod's options' names; their values go in OptionValues.</summary>
    public List<string> Options(int index)
    {
        var names = new List<string>();
        OptionValues.Clear();
        if (index < 0 || index >= Mods.Count || actors[index] == null || !UKismetSystemLibrary.IsValid(actors[index])) return names;
        foreach (var tag in actors[index].Tags)
        {
            var text = tag.ToString();
            if (!UKismetStringLibrary.StartsWith(text, ModOptions.Prefix, ESearchCase.CaseSensitive)) continue;
            UKismetStringLibrary.Split(UKismetStringLibrary.RightChop(text, ModOptions.Prefix.Length), "=", out var name, out var value, ESearchCase.CaseSensitive, ESearchDir.FromStart);
            names.Add(name);
            OptionValues.Add(value);
        }
        return names;
    }

    /// <summary>Sets a mod's option, on the running mod and for next time.</summary>
    public void SetOption(int index, string name, string value)
    {
        if (settings == null || index < 0 || index >= Mods.Count) return;
        var actor = actors[index];
        if (actor != null && UKismetSystemLibrary.IsValid(actor)) ModOptions.Set(actor, name, value);
        var key = Mods[index] + "|" + name;
        int at = settings.OptionKeys.IndexOf(key);
        if (at >= 0) settings.OptionValues[at] = value;
        else
        {
            settings.OptionKeys.Add(key);
            settings.OptionValues.Add(value);
        }
        Save();
        list?.Refresh();
    }

    public int Running()
    {
        int count = 0;
        foreach (var state in States)
            if (state == "Running") count++;
        return count;
    }

    /// <summary>A mod's folder name, from its package: /Game/Mods/Emoticons/ModActor is Emoticons.</summary>
    public static string Short(string mod)
    {
        var path = UKismetStringLibrary.RightChop(mod, ModsPath.Length + 1);
        UKismetStringLibrary.Split(path, "/", out var name, out var rest, ESearchCase.CaseSensitive, ESearchDir.FromStart);
        return name;
    }

    void Save()
    {
        if (settings != null) UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
    }
}
