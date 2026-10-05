using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;

namespace BetterBlueprintLoader;

/// <summary>
/// The functions mods call to save and read values (the shared mod interface's names and parameters), kept in
/// BetterBlueprintLoader's own save. "Mod" is the mod's actor or widget: its folder is where its class is. Saving under a
/// setting's id changes that setting (the mod gets OnSettingChanged).
/// </summary>
[Asset("/Game/Mods/BlueprintLoader/BFL_ModSettings")]
public class ModSettingsLibrary : UBlueprintFunctionLibrary
{
    public static void ModFolder(UObject Mod, UObject __WorldContext, out string Folder) => Folder = ModManager.FolderOf(Mod);

    public static void SetFolderSetting(string Folder, string Id, string Value, UObject __WorldContext) =>
        ModManager.Of(__WorldContext)?.SetValue(Folder, Id, Value);

    public static void GetFolderSetting(string Folder, string Id, string Default, UObject __WorldContext, out string Value, out bool Found)
    {
        Value = Default;
        Found = false;
        var manager = ModManager.Of(__WorldContext);
        if (manager != null && manager.GetValue(Folder, Id, out var saved))
        {
            Value = saved;
            Found = true;
        }
    }

    public static void SetFolderKeybind(string Folder, string Id, FKey Key, FKey SecondaryKey, UObject __WorldContext) =>
        ModManager.Of(__WorldContext)?.SetKeybind(Folder, Id, Key, SecondaryKey);

    public static void GetFolderKeybind(string Folder, string Id, UObject __WorldContext, out FKey Key, out FKey SecondaryKey, out bool Found)
    {
        Key = new FKey();
        SecondaryKey = new FKey();
        Found = false;
        var manager = ModManager.Of(__WorldContext);
        if (manager != null && manager.GetKeybind(Folder, Id, out var key, out var secondary))
        {
            Key = key;
            SecondaryKey = secondary;
            Found = true;
        }
    }

    public static void ClearFolderSettings(string Folder, UObject __WorldContext, ref List<string> Ids) =>
        ModManager.Of(__WorldContext)?.Clear(Folder, Ids);

    public static void SendSavedSettings(string Folder, UObject Target, UObject __WorldContext) =>
        ModManager.Of(__WorldContext)?.SendSaved(Folder, Target);

    public static void LoadModSave(string Folder, UObject __WorldContext, out ModSave Save)
    {
        Save = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<ModSave>()) as ModSave;
        if (Save != null) ModManager.Of(__WorldContext)?.FillSave(Folder, Save);
    }

    public static void SetModSetting(UObject Mod, string Id, string Value, UObject __WorldContext) =>
        ModManager.Of(__WorldContext)?.SetValue(ModManager.FolderOf(Mod), Id, Value);

    public static void SetModSettingBool(UObject Mod, string Id, bool Value, UObject __WorldContext) =>
        ModManager.Of(__WorldContext)?.SetValue(ModManager.FolderOf(Mod), Id, Value ? "true" : "false");

    public static void SetModSettingNumber(UObject Mod, string Id, double Value, UObject __WorldContext) =>
        ModManager.Of(__WorldContext)?.SetValue(ModManager.FolderOf(Mod), Id, ModInfos.Number(Value));

    public static void SetModSettingInt(UObject Mod, string Id, int Value, UObject __WorldContext) =>
        ModManager.Of(__WorldContext)?.SetValue(ModManager.FolderOf(Mod), Id, Value.ToString());

    public static void SetModKeybind(UObject Mod, string Id, FKey Key, FKey SecondaryKey, UObject __WorldContext) =>
        ModManager.Of(__WorldContext)?.SetKeybind(ModManager.FolderOf(Mod), Id, Key, SecondaryKey);

    public static void GetModSetting(UObject Mod, string Id, string Default, UObject __WorldContext, out string Value, out bool Found)
    {
        Found = ModSaves.Get(__WorldContext, Mod, Id, out var saved);
        Value = Found ? saved : Default;
    }

    public static void GetModSettingBool(UObject Mod, string Id, bool Default, UObject __WorldContext, out bool Value, out bool Found)
    {
        Found = ModSaves.Get(__WorldContext, Mod, Id, out var saved);
        Value = Found ? saved == "true" : Default;
    }

    public static void GetModSettingNumber(UObject Mod, string Id, double Default, UObject __WorldContext, out double Value, out bool Found)
    {
        Found = ModSaves.Get(__WorldContext, Mod, Id, out var saved);
        Value = Found ? UKismetStringLibrary.Conv_StringToDouble(saved) : Default;
    }

    public static void GetModSettingInt(UObject Mod, string Id, int Default, UObject __WorldContext, out int Value, out bool Found)
    {
        Found = ModSaves.Get(__WorldContext, Mod, Id, out var saved);
        Value = Found ? UKismetStringLibrary.Conv_StringToInt(saved) : Default;
    }

    public static void GetModKeybind(UObject Mod, string Id, UObject __WorldContext, out FKey Key, out FKey SecondaryKey, out bool Found)
    {
        Key = new FKey();
        SecondaryKey = new FKey();
        Found = false;
        var manager = ModManager.Of(__WorldContext);
        if (manager != null && manager.GetKeybind(ModManager.FolderOf(Mod), Id, out var key, out var secondary))
        {
            Key = key;
            SecondaryKey = secondary;
            Found = true;
        }
    }
}

/// <summary>What ModSettingsLibrary's functions share (they can't call each other).</summary>
static class ModSaves
{
    public static bool Get(UObject context, UObject mod, string id, out string value)
    {
        value = "";
        var manager = ModManager.Of(context);
        return manager != null && manager.GetValue(ModManager.FolderOf(mod), id, out value);
    }
}
