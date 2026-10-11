using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.InputCore;
using UE.UMG;

// The shared mod info and settings types: mods find them at these asset paths and by these names, so mods made for them
// load and work with BetterBlueprintLoader. Written here from their interface, not copied.
namespace BetterBlueprintLoader;

/// <summary>A setting's kind. The Unreal names are the Blueprint editor's (NewEnumerator0...), which mods' ModInfo uses.</summary>
[Asset("/Game/Mods/BlueprintLoader/E_ModSettingType")]
public enum SettingKind : byte
{
    [UName("NewEnumerator0")] Heading,
    [UName("NewEnumerator1")] Text,
    [UName("NewEnumerator2")] Spacer,
    [UName("NewEnumerator3")] Toggle,
    [UName("NewEnumerator4")] Slider,
    [UName("NewEnumerator5")] Select,
    [UName("NewEnumerator6")] UrlButton,
    [UName("NewEnumerator7")] EventButton,
    [UName("NewEnumerator8")] Keybind,
    [UName("NewEnumerator9")] TextInput,
    [UName("NewEnumerator10")] Colour,
    [UName("NewEnumerator11")] Widget,
    // Ends a run of settings placed in columns (Side): the settings after it take the page's whole width again.
    [UName("NewEnumerator12")] SideEnd,
}

/// <summary>
/// One entry of a mod's settings page. Fields a ModInfo leaves at the struct's default aren't saved in it: read them through
/// <see cref="ModInfos.Setting"/>, which fills in those defaults (Max 1, Height 16, white).
/// </summary>
[Asset("/Game/Mods/BlueprintLoader/S_ModSetting", Guid = "26F25DAE-4128-2713-7E60-4B87835D93B1")]
public struct SettingEntry
{
    [UName("Type_2_B00CB93D43E746B0BE3E249C5B65F9D2")] public SettingKind Type;
    [UName("Id_4_59D80CF146C77FF75AC65F96B5941833")] public string Id;
    [UName("Label_6_C0961F4247FF28D6064EFA8A70420A61")] public string Label;
    [UName("Description_8_F1348120455495654CFDFA97A03FD6DE")] public string Description;
    [UName("DefaultOn_10_124D316B43295296E470F18C764EC644")] public bool DefaultOn;
    [UName("DefaultValue_12_B6229FAC4BC312D30C05C0B6BE0B4254")] public double DefaultValue;
    [UName("Min_14_2290187042A74A0D0833099EAD348328")] public double Min;
    [UName("Max_16_9FAA820945BBE75261B51E9040738BDC")] public double Max;
    [UName("Step_18_1ED4C3A742C04EE5298AF5884899ED10")] public double Step;
    [UName("ValueWidth_20_F730978746B88C346ED8658E3FCDFBE6")] public double ValueWidth;
    [UName("Options_22_1377EBB34B4849D01796E68B189A0AB2")] public List<string> Options;
    [UName("DefaultOption_24_4B08081147723E8A9D6A8795157E51A6")] public int DefaultOption;
    [UName("Url_26_F68A84A743BBB53209453BA9D2C4AFBF")] public string Url;
    [UName("ButtonText_28_3F625A794A47F1486490DDB5B94D9C5E")] public string ButtonText;
    [UName("Height_30_52E08D6A41E57747FA39F0AB511AFE6E")] public double Height;
    [UName("DefaultKey_32_9411F67C406AC500CC7C6F89B45A04E4")] public FKey DefaultKey;
    [UName("DefaultText_34_54C30B5446479E771CE3FE8A5ECDF36D")] public string DefaultText;
    [UName("DefaultColour_36_B8031080484A7124D758C29E0F455300")] public FLinearColor DefaultColour;
    [UName("WidgetClass_38_447425D74FC8BFF1B088CCBC7B7D1024")] public TSubclassOf<UUserWidget> WidgetClass;
    [UName("Percentage_40_8024A1B04F10DD1177E88BB146EE578B")] public bool Percentage;
    [UName("HexInput_42_DB63640B4B8BB8A52B0CD1864485944A")] public bool HexInput;
    [UName("Placeholder_44_615145644B6DC2DB2508D3ADF1A38DB2")] public string Placeholder;
    [UName("SecondaryKey_46_B8190CA744AEB23447C30AB5ED9A0C45")] public FKey SecondaryKey;
    // Cooked ModInfos store fields by their place in the struct: new fields only ever go at the end, in this order.
    // Shown a step in from the left, under the setting it belongs to.
    [UName("Indent_48_50980BC549AC4E9726313786BFC1057F")] public bool Indent;
    // Shown only while the setting with this id has one of these values ("true" when there are none).
    [UName("ShowIf_50_33A616AD497AF6278AEA2DA45A43F3A6")] public string ShowIf;
    [UName("ShowIfValues_52_FF9B6E2B479B78D0DF18269AA2E10F59")] public List<string> ShowIfValues;
    // A Select's options, each shown only while a setting (by id, "" for always) has one of its values (comma separated).
    [UName("OptionShowIf_54_11B48E5E413341FAD386CFABEC57AC80")] public List<string> OptionShowIf;
    [UName("OptionShowIfValues_56_8AD700EE4CF9303B2EDEB9804D40988A")] public List<string> OptionShowIfValues;
    // A column to go in ("Left" or "Right"), beside the settings in the other one, until a Side End; and the width of
    // its column (a fraction of the page, or pixels; half when 0).
    [UName("Side_58_FC50D8E74EA8F301E910D3A85B57D6BA")] public string Side;
    [UName("SideWidth_60_053658F048132438EA33D79A48371358")] public double SideWidth;
}

/// <summary>A setting's text in another language, matched to the setting by its id (by its label for one without).</summary>
[Asset("/Game/Mods/BlueprintLoader/S_ModSettingTranslation", Guid = "E8356C85-4A1B-9E71-A541-FFACC01947FF")]
public struct SettingTranslation
{
    [UName("Id_2_C8D371194F81CC1E7EF20EA6D72F7717")] public string Id;
    [UName("Label_4_E0CA9B24451E19664F70419A67ED4CB9")] public string Label;
    [UName("Description_6_40496C224D8C0FBE0E5CA2A6035A8A72")] public string Description;
    [UName("Options_8_002B7C1F4A3E5C1726F5B7B8CECD9EAC")] public List<string> Options;
    [UName("ButtonText_10_BB141FBD4490C9D958E81591FD9C43FB")] public string ButtonText;
    [UName("Placeholder_12_4C617A2746606A75B46AFD847C4CFA0C")] public string Placeholder;
}

/// <summary>
/// A mod's text in another language: its name, description and settings, shown when the game is in that language
/// ("de", "fr", "pt-BR": the game's language, or its first part). Empty fields keep the mod's own text.
/// </summary>
[Asset("/Game/Mods/BlueprintLoader/S_ModTranslation", Guid = "226907A3-4730-C1B4-09A5-A5984E12544B")]
public struct ModTranslation
{
    [UName("Language_2_36C3465A405F4F1B237A1CA2273F0717")] public string Language;
    [UName("ModName_4_A2349754415BD67001BA65A7E1866323")] public string ModName;
    [UName("Description_6_19AB83BC47978E78916E768936A8713A")] public string Description;
    [UName("DisableMessage_8_560E88C6405D58D3011F6A96048191BD")] public string DisableMessage;
    [UName("Settings_10_2C85FB5D44560EAC78B7E0AAFF48672C")] public List<SettingTranslation> Settings;
}

/// <summary>A mod's /Game/Mods/&lt;Mod&gt;/ModInfo: what the Mods tab shows about it, and its settings page.</summary>
[Asset("/Game/Mods/BlueprintLoader/BP_ModInfo")]
public class ModDetails : UPrimaryDataAsset
{
    // Cooked ModInfos store fields by their place in the class: new fields only ever go at the end, in this order.
    public string ModName;
    public string Version;
    public string Author;
    public string AuthorUrl;
    public string Description;
    public List<SettingEntry> Settings = new();
    public bool SupportsDisabling;
    public bool PreventDisabling;
    public string DisableMessage;
    // The mod's page on Nexus Mods (nexusmods.com/minecraftdungeons2/mods/<id>), for the update check, and the name of
    // its file there when the page has several (else its main file's version counts).
    public int NexusModsId;
    public string NexusFileName;
    public int LoadPriority;
    public List<ModTranslation> Translations = new();
}

/// <summary>A mod's saved values, as ModSettingsLibrary.LoadModSave gives them.</summary>
[Asset("/Game/Mods/BlueprintLoader/BP_ModSave")]
public class ModSave : USaveGame
{
    public List<string> Ids = new();
    public List<string> Values = new();
    public List<string> KeyIds = new();
    public List<FKey> PrimaryKeys = new();
    public List<FKey> SecondaryKeys = new();
}

/// <summary>
/// The settings page's events, on a mod's ModActor and on Widget settings. NeoRuneExtended mods implement it as
/// NeoRune.IModSettings, which is the same Unreal interface.
/// </summary>
[Asset("/Game/Mods/BlueprintLoader/BPI_ModSettings")]
public interface ISettingsEvents
{
    void OnButtonPressed(string Id);
    void OnKeybindChanged(string Id, FKey Key, FKey SecondaryKey);
    void OnSettingChanged(string Id, string Value);
    void OnSettingsReset();
}

/// <summary>On a Widget setting that saves its own values: shows the page's Reset button.</summary>
[Asset("/Game/Mods/BlueprintLoader/BPI_ModResettable")]
public interface IResettable
{
}
