using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;

namespace BetterBlueprintLoader;

/// <summary>Reading mods' ModInfo, and their settings' values as text, as mods get them.</summary>
public static class ModInfos
{
    /// <summary>A mod's /Game/Mods/&lt;folder&gt;/ModInfo, or null when it has none.</summary>
    public static ModDetails? Load(string folder)
    {
        var path = UKismetSystemLibrary.MakeSoftObjectPath($"/Game/Mods/{folder}/ModInfo.ModInfo");
        return UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(path)) as ModDetails;
    }

    /// <summary>
    /// A setting with the struct's defaults filled in. A ModInfo leaves out fields that are on SettingEntry's default (Max 1,
    /// Height 16, a white colour), and this mod's copy of the struct starts at zero.
    /// </summary>
    public static SettingEntry Setting(ModDetails info, int index)
    {
        var setting = info.Settings[index];
        if (setting.Min == 0 && setting.Max == 0) setting.Max = 1;
        if (setting.Height == 0) setting.Height = 16;
        var colour = setting.DefaultColour;
        if (colour.R == 0 && colour.G == 0 && colour.B == 0 && colour.A == 0) setting.DefaultColour = new FLinearColor { R = 1, G = 1, B = 1, A = 1 };
        // Its text in the game's language, when the mod has it.
        int language = Translation(info);
        if (language < 0) return setting;
        // Settings without an id (headings, text) are matched by their own label.
        var key = setting.Id != "" ? setting.Id : setting.Label;
        foreach (var translated in info.Translations[language].Settings)
        {
            if (translated.Id != key) continue;
            if (translated.Label != "") setting.Label = translated.Label;
            if (translated.Description != "") setting.Description = translated.Description;
            if (translated.ButtonText != "") setting.ButtonText = translated.ButtonText;
            if (translated.Placeholder != "") setting.Placeholder = translated.Placeholder;
            // Only as many as the mod's own: a choice is saved as its option's place in the list.
            if (translated.Options.Count == setting.Options.Count) setting.Options = translated.Options;
            break;
        }
        return setting;
    }

    /// <summary>
    /// The mod's translation for the game's language: its name exactly ("pt-BR"), else the same language ("pt", or
    /// another "pt-..."). -1 when it has none.
    /// </summary>
    public static int Translation(ModDetails? info)
    {
        if (info == null || info.Translations.Count == 0) return -1;
        var language = UKismetStringLibrary.ToLower(UKismetInternationalizationLibrary.GetCurrentLanguage());
        language = UKismetStringLibrary.Replace(language, "_", "-", ESearchCase.CaseSensitive);
        var main = Main(language);
        if (main == "") return -1;
        int near = -1;
        for (int i = 0; i < info.Translations.Count; i++)
        {
            var theirs = UKismetStringLibrary.ToLower(UKismetStringLibrary.Trim(info.Translations[i].Language));
            theirs = UKismetStringLibrary.Replace(theirs, "_", "-", ESearchCase.CaseSensitive);
            if (theirs == language) return i;
            if (near < 0 && Main(theirs) == main) near = i;
        }
        return near;
    }

    /// <summary>A language's first part: "pt" of "pt-br".</summary>
    static string Main(string language)
    {
        language = UKismetStringLibrary.Replace(language, "_", "-", ESearchCase.CaseSensitive);
        if (UKismetStringLibrary.Split(language, "-", out var main, out var rest, ESearchCase.CaseSensitive, ESearchDir.FromStart)) return main;
        return language;
    }

    /// <summary>A mod's name as shown, in the game's language when it has it (its folder when it has no name).</summary>
    public static string Name(ModDetails? info, string folder)
    {
        if (info == null) return folder;
        int language = Translation(info);
        if (language >= 0 && info.Translations[language].ModName != "") return info.Translations[language].ModName;
        return info.ModName != "" ? info.ModName : folder;
    }

    /// <summary>A mod's description as shown, in the game's language when it has it.</summary>
    public static string Description(ModDetails? info)
    {
        if (info == null) return "";
        int language = Translation(info);
        if (language >= 0 && info.Translations[language].Description != "") return info.Translations[language].Description;
        return info.Description;
    }

    /// <summary>Whether a value is one of a list (ignoring case and spaces at the ends); "true" stands for an empty list.</summary>
    public static bool OneOf(string value, List<string> values)
    {
        value = UKismetStringLibrary.Trim(UKismetStringLibrary.TrimTrailing(value));
        if (values.Count == 0) return UKismetStringLibrary.EqualEqual_StriStri(value, "true");
        foreach (var wanted in values)
            if (UKismetStringLibrary.EqualEqual_StriStri(value, UKismetStringLibrary.Trim(UKismetStringLibrary.TrimTrailing(wanted)))) return true;
        return false;
    }

    /// <summary>Whether a setting has a value sent to the mod (as opposed to headings, text, buttons and keybinds).</summary>
    public static bool HasValue(SettingEntry setting) =>
        setting.Type == SettingKind.Toggle || setting.Type == SettingKind.Slider || setting.Type == SettingKind.Select
        || setting.Type == SettingKind.TextInput || setting.Type == SettingKind.Colour;

    /// <summary>A setting's default value as the text mods get: "true", "0.75", "2", the text, "#FF8800".</summary>
    public static string DefaultValue(SettingEntry setting)
    {
        switch (setting.Type)
        {
            case SettingKind.Toggle:
                return setting.DefaultOn ? "true" : "false";
            case SettingKind.Slider:
                return Number(setting.DefaultValue);
            case SettingKind.Select:
                return setting.DefaultOption.ToString();
            case SettingKind.TextInput:
                return setting.DefaultText;
            case SettingKind.Colour:
                return Hex(setting.DefaultColour);
        }
        return "";
    }

    /// <summary>A number as text without trailing zeros: 0.75, 1, -2.5.</summary>
    public static string Number(double value) => UKismetStringLibrary.Conv_DoubleToString(value);

    /// <summary>A colour as "#RRGGBB" (sRGB, as a colour picker shows it).</summary>
    public static string Hex(FLinearColor colour)
    {
        var srgb = UKismetMathLibrary.Conv_LinearColorToColor(colour, true);
        return "#" + HexByte(srgb.R) + HexByte(srgb.G) + HexByte(srgb.B);
    }

    static string HexByte(byte value)
    {
        const string digits = "0123456789ABCDEF";
        int v = value;
        return UKismetStringLibrary.GetSubstring(digits, v / 16, 1) + UKismetStringLibrary.GetSubstring(digits, v % 16, 1);
    }

    /// <summary>A key's name for the page, or "None".</summary>
    public static string KeyName(FKey key)
    {
        var name = key.KeyName.ToString();
        return name == "" || name == "None" ? "None" : UKismetInputLibrary.Key_GetDisplayName(key, false).ToString();
    }
}
