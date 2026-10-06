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
}

/// <summary>A mod's /Game/Mods/&lt;Mod&gt;/ModInfo: what the Mods tab shows about it, and its settings page.</summary>
[Asset("/Game/Mods/BlueprintLoader/BP_ModInfo")]
public class ModDetails : UPrimaryDataAsset
{
    public string ModName;
    public string Version;
    public string Author;
    public string AuthorUrl;
    public string Description;
    public List<SettingEntry> Settings = new();
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
