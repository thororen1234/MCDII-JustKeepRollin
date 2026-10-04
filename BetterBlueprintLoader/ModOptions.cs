using UE.CoreUObject;
using UE.Engine;

namespace BetterBlueprintLoader;

/// <summary>
/// On/off options for a mod, shown under it in BetterBlueprintLoader's mod list (F10), where players change them; the
/// loader remembers them. Copy this file into your mod and ask for an option when you need its value:
/// <code>
/// if (ModOptions.Toggle(this, "Show hint", true)) ...   // this: your ModActor
/// </code>
/// It works without BetterBlueprintLoader too, always giving the default. The options are tags on the ModActor,
/// "Option:Name=true": the loader puts the saved ones on before the mod starts, and changes them while it runs.
/// </summary>
public static class ModOptions
{
    public const string Prefix = "Option:";

    /// <summary>An on/off option's value: what the player set in the mod list, or the default.</summary>
    public static bool Toggle(AActor mod, string name, bool defaultValue)
    {
        var value = Get(mod, name);
        if (value != "") return value == "true";
        Set(mod, name, defaultValue ? "true" : "false");
        return defaultValue;
    }

    /// <summary>An option's value as text, or "" when the mod hasn't asked for it yet.</summary>
    public static string Get(AActor mod, string name)
    {
        var start = Prefix + name + "=";
        foreach (var tag in mod.Tags)
        {
            var text = tag.ToString();
            if (UKismetStringLibrary.StartsWith(text, start, ESearchCase.CaseSensitive)) return UKismetStringLibrary.RightChop(text, start.Length);
        }
        return "";
    }

    public static void Set(AActor mod, string name, string value)
    {
        var start = Prefix + name + "=";
        var tags = mod.Tags;
        for (int i = tags.Count - 1; i >= 0; i--)
            if (UKismetStringLibrary.StartsWith(tags[i].ToString(), start, ESearchCase.CaseSensitive)) tags.RemoveAt(i);
        tags.Add(UKismetStringLibrary.Conv_StringToName(start + value));
        mod.Tags = tags;
    }
}
