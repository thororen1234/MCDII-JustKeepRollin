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
    // The save slots NeoRune's Log kept each mod's log in: NeoRune_<Mod>.
    const string OldLogPrefix = "NeoRune_";
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
    public const string OpenKeySetting = "open_menu_key";
    public const string OpenToSetting = "open_menu_mod";
    public const string UpdateCheckSetting = "update_check";
    // A mod's page on Nexus Mods, by its id, and this loader's id there (as in its nexus.json).
    public const string NexusPage = "https://www.nexusmods.com/minecraftdungeons2/mods/";
    const int OwnNexusId = 95;

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
    ModUpdates? updates;
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
        // NeoRuneExtended.Sdk 0.4.3 puts it in the ModInfo from nexus.json; built with an older one, it's said here.
        if (OwnInfo != null && OwnInfo.NexusModsId <= 0) OwnInfo.NexusModsId = OwnNexusId;

        // Still set: the game crashed while that mod was starting, the last time. It stays off until turned on.
        if (settings.Starting != "")
        {
            if (!settings.Crashed.Contains(settings.Starting)) settings.Crashed.Add(settings.Starting);
            if (settings.GettingSettings)
            {
                Notice = $"{Short(settings.Starting)} crashed the game while getting its saved settings, so it's turned off. Reset its settings in Settings > Mods, then turn it back on.";
                FileLog.Write($"{settings.Starting} crashed the game while getting its saved settings: turned off");
            }
            else
            {
                Notice = $"{Short(settings.Starting)} crashed the game while starting, so it's turned off. Turn it back on in Settings > Mods.";
                FileLog.Write($"{settings.Starting} crashed the game while starting: turned off");
            }
            settings.Starting = "";
            settings.GettingSettings = false;
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
            var folder = settings.MakingPageOf;
            settings.MakingPage = false;
            settings.MakingPageOf = "";
            if (folder == "")
            {
                settings.PlainPage = true;
                FileLog.Write("The game crashed while the Mods page was being made in the game's look: it's made plain from now on");
            }
            else
            {
                if (!settings.PlainPages.Contains(folder)) settings.PlainPages.Add(folder);
                var line = $"{folder} crashed the game while its settings page was being made, so that page is made plain now. Retry Game Look in Settings > Mods tries again.";
                Notice = Notice == "" ? line : Notice + "\n" + line;
                FileLog.Write($"The game crashed while {folder}'s settings page was being made in the game's look: it's made plain from now on");
            }
            Save();
        }
        menus = GameMenus.Start(this);
        StartUpdateCheck();
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
        CheckUpdates();
        Flush();
        var controller = World.PlayerController(this);
        if (controller == null) return;
        // The Restart Mods Key setting: F12 unless changed. A key left empty is never pressed.
        if (GetKeybind(Unreal.ModName, RestartKeySetting, out var key, out var secondary)
            && (controller.WasInputKeyJustPressed(key) || controller.WasInputKeyJustPressed(secondary))) Restart();
        // The Open Mods Key setting: the Mods tab, on the mod chosen in Open To (the list when none).
        if (GetKeybind(Unreal.ModName, OpenKeySetting, out var open, out var openSecondary)
            && (controller.WasInputKeyJustPressed(open) || controller.WasInputKeyJustPressed(openSecondary)))
        {
            GetValue(Unreal.ModName, OpenToSetting, out var folder);
            menus?.OpenMods(folder);
        }
    }

    /// <summary>
    /// Starts the update check, unless it's turned off: from the loader's save when this game session already downloaded
    /// the versions (each level has its own loader), else from Nexus Mods (see ModUpdates).
    /// </summary>
    void StartUpdateCheck()
    {
        if (settings == null || !OwnToggle(UpdateCheckSetting)) return;
        double frame = UKismetSystemLibrary.GetFrameCount();
        var saved = settings.Versions != "" && frame >= settings.VersionsFrame ? settings.Versions : "";
        updates = ModUpdates.Start(this, saved);
    }

    void CheckUpdates()
    {
        if (updates == null || !updates.Update() || settings == null) return;
        settings.Versions = updates.Text();
        settings.VersionsFrame = UKismetSystemLibrary.GetFrameCount();
        Save();
        RecountUpdates();
        menus?.Refresh();
        label?.Refresh();
    }

    /// <summary>"" while checking (or turned off), else "done" or "failed".</summary>
    public string UpdateState => updates != null ? updates.State : "";

    /// <summary>
    /// The newer version of a mod on Nexus Mods (by folder; the loader's own too), or "" when it's up to date, isn't on
    /// Nexus Mods (its ModInfo has no NexusModsId) or the versions aren't known.
    /// </summary>
    public string UpdateOf(string folder)
    {
        var info = InfoOf(folder);
        if (updates == null || updates.State != "done" || info == null || info.NexusModsId <= 0 || !OwnToggle(UpdateCheckSetting)) return "";
        var latest = updates.Latest(info.NexusModsId, info.NexusFileName);
        return latest != "" && ModUpdates.Newer(latest, info.Version) ? latest : "";
    }

    /// <summary>Whether a mod's ModInfo says where it is on Nexus Mods, so its updates can be checked.</summary>
    public bool OnNexus(string folder)
    {
        var info = InfoOf(folder);
        return info != null && info.NexusModsId > 0;
    }

    /// <summary>How many mods (the loader too) have a newer version on Nexus Mods, as last counted (the main menu shows it every frame).</summary>
    public int UpdatesFound;

    /// <summary>Counts the mods with a newer version again: when the versions arrive, the mods are found, or the check is turned on or off.</summary>
    void RecountUpdates()
    {
        int count = UpdateOf(Unreal.ModName) != "" ? 1 : 0;
        foreach (var folder in Folders)
            if (UpdateOf(folder) != "") count++;
        if (count != UpdatesFound) Note($"Update check: {count} mods have updates");
        UpdatesFound = count;
    }

    /// <summary>
    /// Whether a setting of a mod's page shows: one with a ShowIf shows while that setting has one of its ShowIfValues
    /// ("true" when it lists none), and while that setting shows itself.
    /// </summary>
    public bool Shown(string folder, int index)
    {
        var info = InfoOf(folder);
        // Each step follows a setting this one depends on; a loop of them stops after a few.
        for (int depth = 0; depth < 8 && info != null && index >= 0 && index < info.Settings.Count; depth++)
        {
            var setting = info.Settings[index];
            if (setting.ShowIf == "") return true;
            GetValue(folder, setting.ShowIf, out var value);
            if (!ModInfos.OneOf(value, setting.ShowIfValues)) return false;
            index = SettingIndex(folder, setting.ShowIf);
            if (index < 0) return true;
        }
        return true;
    }

    /// <summary>
    /// Whether an option of a Select shows: one with an OptionShowIf (a setting's id) shows while that setting has one of
    /// its OptionShowIfValues (comma separated; "true" when empty).
    /// </summary>
    public bool OptionShown(string folder, SettingEntry setting, int option)
    {
        if (option < 0 || option >= setting.OptionShowIf.Count || setting.OptionShowIf[option] == "") return true;
        GetValue(folder, setting.OptionShowIf[option], out var value);
        var values = option < setting.OptionShowIfValues.Count ? UKismetStringLibrary.ParseIntoArray(setting.OptionShowIfValues[option], ",", true) : new List<string>();
        return ModInfos.OneOf(value, values);
    }

    /// <summary>Which settings of a mod's page show now, as text ("1101..."): when it changes, the page is made again.</summary>
    public string ShownKey(string folder)
    {
        var info = InfoOf(folder);
        var key = "";
        if (info == null) return key;
        for (int i = 0; i < info.Settings.Count; i++) key += Shown(folder, i) ? "1" : "0";
        return key;
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
        DeleteOldLogs();
        for (int i = 0; i < Mods.Count; i++) Start(i);
        Save();
        Started = true;
        FileLog.Write($"Started {Running()} of {Mods.Count} mods in {World.LevelName(this)} (game {UGameVersion.BuildVersion()})");
        RecountUpdates();
        if (InMenu()) ShowMenuLabel();
        menus?.Refresh();
    }

    /// <summary>
    /// Deletes the logs mods built with NeoRune kept in save slots (NeoRune_&lt;Mod&gt;), this loader's own too: the Xbox app
    /// version syncs every save slot to the cloud, and those logs only ever grew. Mods log to files of their own now, and
    /// a mod that doesn't log at all would never delete its old one itself. Settings slots are left alone.
    /// </summary>
    void DeleteOldLogs()
    {
        var slots = new List<string>();
        slots.Add(OldLogPrefix + Unreal.ModName);
        foreach (var folder in Folders) slots.Add(OldLogPrefix + folder);
        foreach (var slot in slots)
            if (UGameplayStatics.DoesSaveGameExist(slot, 0)) UGameplayStatics.DeleteGameInSlot(slot, 0);
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
        // The mod gets each setting that isn't on its default, once it has started. It's still marked as starting
        // meanwhile, and as getting them: a crash on a saved value leaves both marks for next time.
        if (actors[index] != null && HasSaved(Folders[index]))
        {
            settings.GettingSettings = true;
            SaveNow();
            SendSaved(Folders[index], actors[index]);
            settings.GettingSettings = false;
        }
        settings.Starting = "";
        SaveNow();
    }

    /// <summary>Whether a mod has saved values or keybinds: SendSaved may send it something.</summary>
    bool HasSaved(string folder)
    {
        if (settings == null) return false;
        var prefix = folder + "|";
        foreach (var key in settings.ValueKeys)
            if (UKismetStringLibrary.StartsWith(key, prefix, ESearchCase.CaseSensitive)) return true;
        foreach (var key in settings.KeyIds)
            if (UKismetStringLibrary.StartsWith(key, prefix, ESearchCase.CaseSensitive)) return true;
        return false;
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
        FileLog.Write($"Restarted {count} mods");
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
        FileLog.WriteAll(lines);
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
        if (folder == Unreal.ModName && id == UpdateCheckSetting)
        {
            if (updates == null) StartUpdateCheck();
            RecountUpdates();
            label?.Refresh();
        }
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

    public void RetryPlainPage()
    {
        if (settings == null) return;
        // Not PageFixes: setting it back made the next start clear the mark of a crash while making the page, so it
        // took a second crash to make the page plain again.
        settings.PlainPage = false;
        settings.PlainPages.Clear();
        Save();
    }

    /// <summary>Whether a mod's own settings page is made plain (making it in the game's look crashed the game once).</summary>
    public bool PlainPageOf(string folder) => settings != null && settings.PlainPages.Contains(folder);

    /// <summary>Whether any page is made plain: the Mods list then offers to retry the game's look.</summary>
    public bool AnyPlainPage => settings != null && (settings.PlainPage || settings.PlainPages.Count > 0);

    // When the Mods page was last made: it counts as made once it has been on screen for a moment.
    double pageMadeAt;

    /// <summary>
    /// Marks the Mods page as being made, saved at once: if the game crashes before the page has been on screen for a
    /// second (making or drawing it), the mark is still there when the game starts again. Which page: a mod's folder,
    /// or empty for the list (and the tab itself).
    /// </summary>
    public void MakingPage(string folder)
    {
        pageMadeAt = World.RealTime(this);
        if (settings == null || (settings.MakingPage && settings.MakingPageOf == folder)) return;
        settings.MakingPage = true;
        settings.MakingPageOf = folder;
        SaveNow();
    }

    void PageSettled()
    {
        if (settings == null || !settings.MakingPage || World.RealTime(this) - pageMadeAt < 1) return;
        settings.MakingPage = false;
        settings.MakingPageOf = "";
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
                label?.Remove();
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
