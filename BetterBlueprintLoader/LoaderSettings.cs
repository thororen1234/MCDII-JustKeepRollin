using System.Collections.Generic;
using UE.Engine;

namespace BetterBlueprintLoader;

/// <summary>The mod list's choices, kept between game sessions: SaveGames/BetterBlueprintLoader.sav.</summary>
public class LoaderSettings : USaveGame
{
    // Mods in the order they start (packages like /Game/Mods/Emoticons/ModActor), new ones added at the end.
    public List<string> Order = new();
    // Mods turned off in the mod list.
    public List<string> Disabled = new();
    // Mods turned off because the game crashed while they were starting.
    public List<string> Crashed = new();
    // The mod starting right now: still set when the game starts again, that mod crashed it.
    public string Starting;
    // Mod options set in the mod list, as "package|option" and its value.
    public List<string> OptionKeys = new();
    public List<string> OptionValues = new();
}
