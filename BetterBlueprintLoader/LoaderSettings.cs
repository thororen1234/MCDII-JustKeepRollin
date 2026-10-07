using System.Collections.Generic;
using UE.Engine;
using UE.InputCore;

namespace BetterBlueprintLoader;

/// <summary>What the loader keeps between game sessions: SaveGames/BetterBlueprintLoader.sav.</summary>
public class LoaderSettings : USaveGame
{
    // Mods in the order they start (packages like /Game/Mods/Emoticons/ModActor), new ones added at the end.
    public List<string> Order = new();
    // Mods turned off in the Mods tab.
    public List<string> Disabled = new();
    // Mods turned off because the game crashed while they were starting.
    public List<string> Crashed = new();
    // The mod starting right now: still set when the game starts again, that mod crashed it.
    public string Starting;
    // Set while that mod is getting its saved settings, the last part of starting: a mod can crash on a saved value.
    public bool GettingSettings;
    // Set while the Mods page is being made in the game's look: still set when the game starts again, that crashed it,
    // and the page is made in the loader's own look from then on: only that mod's settings page (MakingPageOf, its
    // folder), or the whole tab when it was the list (empty).
    public bool MakingPage;
    public string MakingPageOf;
    public bool PlainPage;
    public List<string> PlainPages = new();
    // How many times PlainPage was cleared because the page's crashes were found to be something else (the page destroyed
    // while made; added to the screen twice; a variable named "none").
    public int PageFixes;
    // Mods' saved values, as "folder|id" and the value: their settings pages' values (only those off their default) and
    // what mods save themselves (ModSettingsLibrary).
    public List<string> ValueKeys = new();
    public List<string> Values = new();
    // Mods' saved keybinds, as "folder|id" and the two keys.
    public List<string> KeyIds = new();
    public List<FKey> PrimaryKeys = new();
    public List<FKey> SecondaryKeys = new();
}
