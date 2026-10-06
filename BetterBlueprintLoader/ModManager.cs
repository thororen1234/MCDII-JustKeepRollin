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
/// Starts the mods in a level and keeps track of them: the order they start in, which are turned off, which crashed the
/// game while starting, how long each took. It also keeps mods' settings and sends them to the mods (ISettingsEvents). One per level, spawned by LoaderComponent.
/// </summary>
public class ModManager : AActor
{
    // The game this loader's R1 hook was made from, as the game gives its version ("50349472:releases/r2:<commit>"):
    // another build may have changed R1.
    const string BuiltForGame = "1.1.1.0";
    const string BuiltForBuild = "50349472:";
    const string SettingsSlot = "BetterBlueprintLoader";
    const string ModsPath = "/Game/Mods";
    const string ModActorName = "ModActor";
    const string ModInfoName = "ModInfo";
    // The shared mod interface lives in this folder, which BetterBlueprintLoader provides: it's not a mod.
    const string InterfacesFolder = "BlueprintLoader";
    // Mods start once the player's character is there and set up: many do their work once, when they start, on the
    // character (its stats, its abilities). Levels without one (the main menu) start them after the longest wait.
    const float WaitInterval = 0.25f;
    const double SetUpTime = 0.5;
    const double LongestWait = 10;

    // The loader's own settings, on its page in the Mods tab (see ModActor).
    public const string MenuLabelSetting = "menu_label";
    public const string MenuButtonSetting = "menu_button";
    public const string RestartSetting = "restart";
    public const string RestartKeySetting = "restart_key";

    LoaderSettings? settings;
    // Per mod, in start order: its package (/Game/Mods/X/ModActor, also for mods without one), its folder, its ModInfo,
    // whether it has a ModActor, its class once loaded, its actor while running, its state and how long it took to
    // start (ms).
    public List<string> Mods = new();
    public List<string> Folders = new();
    public List<ModDetails> Infos = new();
    List<bool> runnable = new();
    List<TSubclassOf<AActor>> classes = new();
    List<AActor> actors = new();
    public List<string> States = new();
    public List<double> Times = new();
    // The loader's own ModInfo, for its page in the Mods tab.
    public ModDetails? OwnInfo;
    // Shown on the main menu and in the Mods tab: a mod that crashed the game, a newer game version.
    public string Notice = "";
    public string Warning = "";
    // Set when a mod is turned off while running: what it changed in the game can stay until the next level.
    public string Stopped = "";
    public bool Started;
    MenuLabel? label;
    GameMenus? menus;
    double waitStarted;
    double characterSince;

    public string Version => OwnInfo != null && OwnInfo.Version != "" ? OwnInfo.Version : "2.0.0";

    /// <summary>The loader in the level of an object, or null.</summary>
    public static ModManager? Of(UObject? context)
    {
        if (context == null) return null;
        return UGameplayStatics.GetActorOfClass(context, Unreal.ClassOf<ModManager>()) as ModManager;
    }

    protected override void ReceiveBeginPlay()
    {
        // The Mods tab and the main menu work while menus pause the game.
        SetTickableWhenPaused(true);
        settings = UGameplayStatics.LoadGameFromSlot(SettingsSlot, 0) as LoaderSettings;
        if (settings == null) settings = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<LoaderSettings>()) as LoaderSettings;
        if (settings == null) return;
        OwnInfo = ModInfos.Load(Unreal.ModName);

        // Still set: the game crashed while that mod was starting, the last time. It stays off until turned on.
        if (settings.Starting != "")
        {
            if (!settings.Crashed.Contains(settings.Starting)) settings.Crashed.Add(settings.Starting);
            Notice = $"{Short(settings.Starting)} crashed the game while starting, so it's turned off. Turn it back on in Settings > Mods.";
            Log.Write($"{settings.Starting} crashed the game while starting: turned off");
            settings.Starting = "";
            Save();
        }

        if (settings.PageFixes < 5)
        {
            settings.PageFixes = 5;
            settings.PlainPage = false;
            settings.MakingPage = false;
            Save();
        }
        if (settings.MakingPage)
        {
            settings.MakingPage = false;
            settings.PlainPage = true;
            Log.Write("The game crashed while the Mods page was being made in the game's look: it's made plain from now on");
            Save();
        }
        menus = GameMenus.Start(this);
        waitStarted = World.RealTime(this);
        characterSince = -1;
        Timer.Start(this, nameof(WaitForCharacter), WaitInterval, loop: true);
    }

    void WaitForCharacter()
    {
        var now = World.RealTime(this);
        if (World.Player(this) is ACharacter)
        {
            if (characterSince < 0) characterSince = now;
            if (now - characterSince < SetUpTime) return;
        }
        // Menus have no character to wait for.
        else if (now - waitStarted < LongestWait && !InMenu()) return;
        Timer.Stop(this, nameof(WaitForCharacter));
        StartAll();
    }

    public bool InMenu() => UKismetStringLibrary.StartsWith(World.LevelName(this), "Menu", ESearchCase.IgnoreCase);

    public override void ReceiveTick(float deltaSeconds)
    {
        PageSettled();
        Flush();
        var controller = World.PlayerController(this);
        if (controller == null) return;
        // The Restart Mods Key setting: F12 unless changed. A key left empty is never pressed.
        if (!GetKeybind(Unreal.ModName, RestartKeySetting, out var key, out var secondary)) return;
        if (controller.WasInputKeyJustPressed(key) || controller.WasInputKeyJustPressed(secondary)) Restart();
    }

    /// <summary>Finds the mods and starts them, in the saved order.</summary>
    void StartAll()
    {
        if (settings == null) return;
        var found = Find();
        Mods.Clear();
        settings.Order.Clear();
        foreach (var mod in found)
        {
            Mods.Add(mod);
            settings.Order.Add(mod);
        }
        for (int i = 0; i < Mods.Count; i++)
        {
            var folder = Short(Mods[i]);
            Folders.Add(folder);
            Infos.Add(ModInfos.Load(folder));
            runnable.Add(withActor.Contains(Mods[i]));
            classes.Add(null);
            actors.Add(null);
            States.Add("");
            Times.Add(0);
        }
        for (int i = 0; i < Mods.Count; i++) Start(i);
        Save();
        Started = true;
        Log.Write($"Started {Running()} of {Mods.Count} mods in {World.LevelName(this)} (game {UGameVersion.BuildVersion()})");
        if (InMenu()) ShowMenuLabel();
        menus?.Refresh();
    }

    // Found by Find: mods with a ModActor (the others only have a ModInfo).
    List<string> withActor = new();

    /// <summary>The mods in the game: folders under /Game/Mods with a ModActor or a ModInfo, this loader's own left out.</summary>
    List<string> Find()
    {
        var mods = new List<string>();
        withActor.Clear();
        var registry = UAssetRegistryHelpers.GetAssetRegistry();
        if (registry == null) return mods;
        registry.ScanPathsSynchronous(new List<string> { ModsPath }, false, false);
        registry.GetAssetsByPath(ModsPath, out var assets, true, false);
        foreach (var asset in assets)
        {
            // A cooked game lists the class (ModActor_C), not the blueprint: a mod is a package named ModActor or ModInfo.
            var package = asset.PackageName.ToString();
            var isActor = package.EndsWith("/" + ModActorName);
            if (!isActor && !package.EndsWith("/" + ModInfoName)) continue;
            var folder = Short(package);
            if (folder == Unreal.ModName || folder == InterfacesFolder) continue;
            var mod = $"{ModsPath}/{folder}/{ModActorName}";
            if (!mods.Contains(mod)) mods.Add(mod);
            if (isActor && !withActor.Contains(mod)) withActor.Add(mod);
        }
        for (int i = 0; i < mods.Count - 1; i++)
        {
            for (int j = i + 1; j < mods.Count; j++)
            {
                if (IsGreater(mods[i], mods[j]))
                {
                    var temp = mods[i];
                    mods[i] = mods[j];
                    mods[j] = temp;
                }
            }
        }
        return mods;
    }

    static bool IsGreater(string a, string b)
    {
        string alphabet = "0123456789AaBbCcDdEeFfGgHhIiJjKkLlMmNnOoPpQqRrSsTtUuVvWwXxYyZz_ -";
        int lenA = UKismetStringLibrary.Len(a);
        int lenB = UKismetStringLibrary.Len(b);
        int min = lenA < lenB ? lenA : lenB;
        for (int i = 0; i < min; i++)
        {
            string charA = UKismetStringLibrary.GetSubstring(a, i, 1);
            string charB = UKismetStringLibrary.GetSubstring(b, i, 1);
            if (!UKismetStringLibrary.EqualEqual_StrStr(charA, charB))
            {
                int idxA = UKismetStringLibrary.FindSubstring(alphabet, charA, false, false, 0);
                int idxB = UKismetStringLibrary.FindSubstring(alphabet, charB, false, false, 0);
                return idxA > idxB;
            }
        }
        return lenA > lenB;
    }

    /// <summary>Starts a mod, unless it's off. Marked as starting meanwhile: a crash leaves the mark for next time.</summary>
    void Start(int index)
    {
        if (settings == null) return;
        var mod = Mods[index];
        if (!runnable[index])
        {
            States[index] = "Nothing to run";
            return;
        }
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
        SaveNow();
        var started = UKismetMathLibrary.Now();
        if (classes[index] == null) classes[index] = Unreal.LoadClass<AActor>($"{mod}.{ModActorName}_C");
        var modClass = classes[index];
        if (modClass == null) States[index] = "Couldn't load";
        else
        {
            // Already running in this level: from another player controller, or another loader.
            var running = World.FindAll(this, modClass);
            if (running.Count > 0) actors[index] = running[0];
            else actors[index] = World.Spawn(this, modClass, new FVector());
            States[index] = actors[index] != null ? "Running" : "Couldn't start";
        }
        Times[index] = UKismetMathLibrary.GetTotalMilliseconds(UKismetMathLibrary.Subtract_DateTimeDateTime(UKismetMathLibrary.Now(), started));
        settings.Starting = "";
        SaveNow();
        // The mod gets each setting that isn't on its default, once it has started.
        if (actors[index] != null) SendSaved(Folders[index], actors[index]);
    }

    /// <summary>
    /// Stops a mod: its ModActor, and every other actor and widget of a class from its folder (its timers, widget
    /// watchers, HUD, wheels...). What it changed in the game itself stays until the next level.
    /// </summary>
    void Stop(int index)
    {
        if (actors[index] != null && UKismetSystemLibrary.IsValid(actors[index])) actors[index].K2_DestroyActor();
        actors[index] = null;
        var folder = $"{ModsPath}/{Folders[index]}/";
        foreach (var actor in World.FindAll(this, Unreal.ClassOf<AActor>()))
            if (actor != null && actor != this && FromFolder(actor, folder)) actor.K2_DestroyActor();
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(this, out var widgets, Unreal.ClassOf<UUserWidget>(), false);
        foreach (var widget in widgets)
            if (widget != null && FromFolder(widget, folder)) widget.RemoveFromParent();
    }

    static bool FromFolder(UObject thing, string folder) => UKismetStringLibrary.StartsWith(GameUI.ClassPath(thing), folder, ESearchCase.IgnoreCase);

    /// <summary>Stops every running mod and starts them again, in the saved order.</summary>
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
        menus?.Refresh();
    }

    /// <summary>Turns a mod on (starting it now) or off (stopping it now). Remembered.</summary>
    public void SetOn(int index, bool on)
    {
        if (settings == null || index < 0 || index >= Mods.Count || !runnable[index]) return;
        var mod = Mods[index];
        settings.Crashed.Remove(mod);
        settings.Disabled.Remove(mod);
        // The notes about this mod are out of date now.
        if (UKismetStringLibrary.StartsWith(Stopped, Folders[index] + " ", ESearchCase.CaseSensitive)) Stopped = "";
        if (UKismetStringLibrary.StartsWith(Notice, Folders[index] + " ", ESearchCase.CaseSensitive)) Notice = "";
        if (on) Start(index);
        else
        {
            settings.Disabled.Add(mod);
            if (States[index] == "Running")
                Stopped = $"{Folders[index]} is off, but what it changed in the game (like controls) can stay until the next level.";
            Stop(index);
            States[index] = "Off";
        }
        Save();
        menus?.Refresh();
        label?.Refresh();
    }

    public bool IsOn(int index) => States[index] == "Running" || States[index] == "Couldn't load" || States[index] == "Couldn't start";

    public bool CanRun(int index) => runnable[index];

    /// <summary>Moves a mod up (-1) or down (1) the start order. Remembered; it counts from the next level or restart.</summary>
    public void Move(int index, int by)
    {
        int other = index + by;
        if (settings == null || index < 0 || other < 0 || index >= Mods.Count || other >= Mods.Count) return;
        // Swapped in place: a List passed to a method is a copy in a Blueprint.
        var mod = Mods[index];
        Mods[index] = Mods[other];
        Mods[other] = mod;
        var folder = Folders[index];
        Folders[index] = Folders[other];
        Folders[other] = folder;
        var info = Infos[index];
        Infos[index] = Infos[other];
        Infos[other] = info;
        var canRun = runnable[index];
        runnable[index] = runnable[other];
        runnable[other] = canRun;
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
        menus?.Refresh();
    }

    public int Running()
    {
        int count = 0;
        foreach (var state in States)
            if (state == "Running") count++;
        return count;
    }

    /// <summary>A mod's folder name, from a package: /Game/Mods/Emoticons/ModActor is Emoticons.</summary>
    public static string Short(string package)
    {
        var path = UKismetStringLibrary.RightChop(package, ModsPath.Length + 1);
        UKismetStringLibrary.Split(path, "/", out var name, out var rest, ESearchCase.CaseSensitive, ESearchDir.FromStart);
        return name;
    }

    /// <summary>The folder of the mod an object belongs to (its class is in /Game/Mods/&lt;folder&gt;), or "".</summary>
    public static string FolderOf(UObject? thing)
    {
        if (thing == null) return "";
        var path = GameUI.ClassPath(thing);
        if (!UKismetStringLibrary.StartsWith(path, ModsPath + "/", ESearchCase.IgnoreCase)) return "";
        return Short(path);
    }

    // Saving (a setting, a log line) can let the garbage collector run, which isn't safe in the middle of the game's own
    // code, like a button's click: settings and log lines changed there are saved on the next tick.
    bool saveWanted;
    List<string> notes = new();

    void Save() => saveWanted = true;

    /// <summary>Saves at once: the marks that say what crashed the game must be saved before what might crash it.</summary>
    void SaveNow()
    {
        saveWanted = false;
        if (settings != null) UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
    }

    /// <summary>A line for the log, written on the next tick.</summary>
    public void Note(string line) => notes.Add(line);

    /// <summary>Saves what changed and writes the log lines waiting, now: only from the loader's own tick or timers.</summary>
    public void Flush()
    {
        if (saveWanted) SaveNow();
        if (notes.Count == 0) return;
        var lines = notes;
        notes.Clear();
        Log.WriteAll(lines);
    }

    protected override void ReceiveEndPlay(EEndPlayReason reason) => Flush();

    /// <summary>A mod's ModInfo by folder: the loader's own, a mod's, or null.</summary>
    public ModDetails? InfoOf(string folder)
    {
        if (folder == Unreal.ModName) return OwnInfo;
        int index = Folders.IndexOf(folder);
        return index >= 0 ? Infos[index] : null;
    }

    /// <summary>The running ModActor of a mod, or null.</summary>
    public AActor? ActorOf(string folder)
    {
        int index = Folders.IndexOf(folder);
        if (index < 0 || actors[index] == null || !UKismetSystemLibrary.IsValid(actors[index])) return null;
        return actors[index];
    }

    /// <summary>The index of a setting with an id in a mod's ModInfo, or -1.</summary>
    public int SettingIndex(string folder, string id)
    {
        var info = InfoOf(folder);
        if (info == null || id == "") return -1;
        for (int i = 0; i < info.Settings.Count; i++)
            if (info.Settings[i].Id == id) return i;
        return -1;
    }

    /// <summary>A saved value: a setting's (its default when not changed), or one the mod saved itself.</summary>
    public bool GetValue(string folder, string id, out string value)
    {
        value = "";
        if (settings == null) return false;
        int at = settings.ValueKeys.IndexOf(folder + "|" + id);
        if (at >= 0)
        {
            value = settings.Values[at];
            return true;
        }
        var info = InfoOf(folder);
        int setting = SettingIndex(folder, id);
        if (info == null || setting < 0 || !ModInfos.HasValue(ModInfos.Setting(info, setting))) return false;
        value = ModInfos.DefaultValue(ModInfos.Setting(info, setting));
        return true;
    }

    /// <summary>
    /// Saves a value. Saving a setting's id changes that setting: the mod gets OnSettingChanged
    /// and the settings page shows it. A setting on its default isn't kept, so the mod isn't sent it when it loads.
    /// </summary>
    public void SetValue(string folder, string id, string value)
    {
        if (settings == null) return;
        var key = folder + "|" + id;
        int at = settings.ValueKeys.IndexOf(key);
        var info = InfoOf(folder);
        int setting = SettingIndex(folder, id);
        bool isSetting = info != null && setting >= 0 && ModInfos.HasValue(ModInfos.Setting(info, setting));
        if (isSetting && value == ModInfos.DefaultValue(ModInfos.Setting(info, setting)))
        {
            if (at >= 0)
            {
                settings.ValueKeys.RemoveAt(at);
                settings.Values.RemoveAt(at);
            }
        }
        else if (at >= 0) settings.Values[at] = value;
        else
        {
            settings.ValueKeys.Add(key);
            settings.Values.Add(value);
        }
        Save();
        if (!isSetting) return;
        SendSetting(ActorOf(folder), id, value);
        menus?.SettingChanged(folder, id, value);
        if (folder == Unreal.ModName) ApplyOwnSetting(id);
    }

    /// <summary>A saved keybind: a setting's (its defaults when not changed), or one the mod saved itself.</summary>
    public bool GetKeybind(string folder, string id, out FKey key, out FKey secondary)
    {
        key = new FKey();
        secondary = new FKey();
        if (settings == null) return false;
        int at = settings.KeyIds.IndexOf(folder + "|" + id);
        if (at >= 0)
        {
            key = settings.PrimaryKeys[at];
            secondary = settings.SecondaryKeys[at];
            return true;
        }
        var info = InfoOf(folder);
        int setting = SettingIndex(folder, id);
        if (info == null || setting < 0 || info.Settings[setting].Type != SettingKind.Keybind) return false;
        key = info.Settings[setting].DefaultKey;
        secondary = info.Settings[setting].SecondaryKey;
        return true;
    }

    /// <summary>Saves a keybind; a keybind setting's id changes that setting (the mod gets OnKeybindChanged).</summary>
    public void SetKeybind(string folder, string id, FKey key, FKey secondary)
    {
        if (settings == null) return;
        var name = folder + "|" + id;
        int at = settings.KeyIds.IndexOf(name);
        var info = InfoOf(folder);
        int setting = SettingIndex(folder, id);
        bool isSetting = info != null && setting >= 0 && info.Settings[setting].Type == SettingKind.Keybind;
        if (isSetting && key.KeyName == info.Settings[setting].DefaultKey.KeyName && secondary.KeyName == info.Settings[setting].SecondaryKey.KeyName)
        {
            if (at >= 0)
            {
                settings.KeyIds.RemoveAt(at);
                settings.PrimaryKeys.RemoveAt(at);
                settings.SecondaryKeys.RemoveAt(at);
            }
        }
        else if (at >= 0)
        {
            settings.PrimaryKeys[at] = key;
            settings.SecondaryKeys[at] = secondary;
        }
        else
        {
            settings.KeyIds.Add(name);
            settings.PrimaryKeys.Add(key);
            settings.SecondaryKeys.Add(secondary);
        }
        Save();
        if (!isSetting) return;
        var actor = ActorOf(folder);
        if (actor is ISettingsEvents target) target.OnKeybindChanged(id, key, secondary);
        menus?.SettingChanged(folder, id, "");
    }

    /// <summary>
    /// Puts a mod's settings back to their defaults and tells it (OnSettingsReset). What the mod
    /// saved under other ids stays.
    /// </summary>
    public void ResetSettings(string folder)
    {
        var info = InfoOf(folder);
        if (settings == null || info == null) return;
        foreach (var setting in info.Settings)
        {
            if (setting.Id == "") continue;
            int at = settings.ValueKeys.IndexOf(folder + "|" + setting.Id);
            if (at >= 0)
            {
                settings.ValueKeys.RemoveAt(at);
                settings.Values.RemoveAt(at);
            }
            int key = settings.KeyIds.IndexOf(folder + "|" + setting.Id);
            if (key >= 0)
            {
                settings.KeyIds.RemoveAt(key);
                settings.PrimaryKeys.RemoveAt(key);
                settings.SecondaryKeys.RemoveAt(key);
            }
        }
        Save();
        var actor = ActorOf(folder);
        if (actor is ISettingsEvents target) target.OnSettingsReset();
        if (folder == Unreal.ModName) ApplyOwnSetting("");
        menus?.Refresh();
    }

    /// <summary>Removes saved values (ModSettingsLibrary.ClearFolderSettings).</summary>
    public void Clear(string folder, List<string> ids)
    {
        if (settings == null) return;
        foreach (var id in ids)
        {
            int at = settings.ValueKeys.IndexOf(folder + "|" + id);
            if (at >= 0)
            {
                settings.ValueKeys.RemoveAt(at);
                settings.Values.RemoveAt(at);
            }
            int key = settings.KeyIds.IndexOf(folder + "|" + id);
            if (key >= 0)
            {
                settings.KeyIds.RemoveAt(key);
                settings.PrimaryKeys.RemoveAt(key);
                settings.SecondaryKeys.RemoveAt(key);
            }
        }
        Save();
    }

    /// <summary>Copies a mod's saved values and keybinds into a ModSave (ModSettingsLibrary.LoadModSave).</summary>
    public void FillSave(string folder, ModSave save)
    {
        if (settings == null) return;
        var prefix = folder + "|";
        for (int i = 0; i < settings.ValueKeys.Count; i++)
            if (UKismetStringLibrary.StartsWith(settings.ValueKeys[i], prefix, ESearchCase.CaseSensitive))
            {
                save.Ids.Add(UKismetStringLibrary.RightChop(settings.ValueKeys[i], prefix.Length));
                save.Values.Add(settings.Values[i]);
            }
        for (int i = 0; i < settings.KeyIds.Count; i++)
            if (UKismetStringLibrary.StartsWith(settings.KeyIds[i], prefix, ESearchCase.CaseSensitive))
            {
                save.KeyIds.Add(UKismetStringLibrary.RightChop(settings.KeyIds[i], prefix.Length));
                save.PrimaryKeys.Add(settings.PrimaryKeys[i]);
                save.SecondaryKeys.Add(settings.SecondaryKeys[i]);
            }
    }

    /// <summary>Sends a mod (its ModActor or a Widget setting) each of its settings that isn't on its default.</summary>
    public void SendSaved(string folder, UObject? target)
    {
        var info = InfoOf(folder);
        if (settings == null || info == null || target == null || !(target is ISettingsEvents)) return;
        for (int i = 0; i < info.Settings.Count; i++)
        {
            var setting = info.Settings[i];
            if (setting.Id == "") continue;
            if (setting.Type == SettingKind.Keybind)
            {
                int key = settings.KeyIds.IndexOf(folder + "|" + setting.Id);
                if (key >= 0 && target is ISettingsEvents keys) keys.OnKeybindChanged(setting.Id, settings.PrimaryKeys[key], settings.SecondaryKeys[key]);
                continue;
            }
            if (!ModInfos.HasValue(ModInfos.Setting(info, i))) continue;
            int at = settings.ValueKeys.IndexOf(folder + "|" + setting.Id);
            if (at >= 0) SendSetting(target, setting.Id, settings.Values[at]);
        }
    }

    /// <summary>An event button was clicked: the mod gets OnButtonPressed; the loader's own buttons act here.</summary>
    public void PressButton(string folder, string id)
    {
        if (folder == Unreal.ModName)
        {
            if (id == RestartSetting) Restart();
            return;
        }
        if (ActorOf(folder) is ISettingsEvents target) target.OnButtonPressed(id);
    }

    static void SendSetting(UObject? target, string id, string value)
    {
        if (target is ISettingsEvents settingsTarget) settingsTarget.OnSettingChanged(id, value);
    }

    /// <summary>Whether the Mods page is made plain (making it in the game's look crashed the game once).</summary>
    public bool PlainPage => settings != null && settings.PlainPage;

    // When the Mods page was last made: it counts as made once it has been on screen for a moment.
    double pageMadeAt;

    /// <summary>
    /// Marks the Mods page as being made, saved at once: if the game crashes before the page has been on screen for a
    /// second (making or drawing it), the mark is still there when the game starts again.
    /// </summary>
    public void MakingPage()
    {
        pageMadeAt = World.RealTime(this);
        if (settings == null || settings.MakingPage) return;
        settings.MakingPage = true;
        SaveNow();
    }

    void PageSettled()
    {
        if (settings == null || !settings.MakingPage || World.RealTime(this) - pageMadeAt < 1) return;
        settings.MakingPage = false;
        Save();
    }

    /// <summary>Whether one of the loader's own toggles is on (they default to on).</summary>
    public bool OwnToggle(string id) => !GetValue(Unreal.ModName, id, out var value) || value != "false";

    void ApplyOwnSetting(string id)
    {
        if (!InMenu()) return;
        if (id == "" || id == MenuLabelSetting)
        {
            if (OwnToggle(MenuLabelSetting)) ShowMenuLabel();
            else
            {
                label?.RemoveFromParent();
                label = null;
            }
        }
        if (id == "" || id == MenuButtonSetting) menus?.MenuButtonSettingChanged();
    }

    void ShowMenuLabel()
    {
        if (label != null || !OwnToggle(MenuLabelSetting)) return;
        label = MenuLabel.Show(this);
    }
}
