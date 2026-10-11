using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.UMG;

namespace BetterBlueprintLoader;

/// <summary>
/// The latest version of every mod on Nexus Mods, for the Mods tab's update check. A game mod can't read web pages, but it
/// can download an image: a GitHub Action (tools/versions) asks Nexus Mods every hour and packs the answer into a PNG as
/// text, one line per mod: "modId;mainVersion;fileName=version;...". Each character is a 2x2 pixel cell whose red, green
/// and blue are each one of 4 levels (6 bits: its place in <see cref="Alphabet"/>), after 4 grey cells of those levels
/// (to read them by) and up to the end mark (63). If this loader's own image can't be had, another made the same way is
/// read instead. Downloaded once per game session, kept in the loader's save for the next levels.
/// </summary>
public class ModUpdates : UObject
{
    const string OwnUrl = "https://raw.githubusercontent.com/thororen1234/MCDII-Mods/refs/heads/versions/versions.png";
    const string FallbackUrl = "https://raw.githubusercontent.com/ewanhowell5195/minecraft-dungeons-II-mod-versions/refs/heads/main/versions.png";
    const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyz .-_;=\n'()+!,&[]:/#@~*%^$";
    const int End = 63;
    const int Size = 256;
    const int Cell = 2;
    const int Cells = Size / Cell;
    const int Levels = 4;

    ModManager? manager;
    // Held so the garbage collector leaves them be while they're used.
    UAsyncTaskDownloadImage? task;
    UTexture2DDynamic? image;
    UTextureRenderTarget2D? target;
    bool usingFallback;
    // The image arrived (or didn't): read on the manager's next tick, outside the download's callback.
    bool arrived;
    bool failed;
    // "" while checking, else "done" or "failed".
    public string State;
    // The lines of the text, once read.
    public List<string> Lines = new();

    public static ModUpdates? Start(ModManager manager, string saved)
    {
        var updates = UGameplayStatics.SpawnObject(Unreal.ClassOf<ModUpdates>(), manager) as ModUpdates;
        if (updates == null) return null;
        updates.manager = manager;
        updates.State = "";
        if (saved != "")
        {
            updates.Read(saved);
            return updates;
        }
        updates.Download(OwnUrl);
        return updates;
    }

    void Download(string url)
    {
        task = UAsyncTaskDownloadImage.DownloadImage(url);
        if (task == null)
        {
            failed = true;
            return;
        }
        task.OnSuccess += Downloaded;
        task.OnFail += DownloadFailed;
    }

    void Downloaded(UTexture2DDynamic texture)
    {
        image = texture;
        arrived = true;
    }

    void DownloadFailed(UTexture2DDynamic texture) => failed = true;

    /// <summary>Reads an image that arrived, or tries the other one: call from the manager's tick. True when the text is new.</summary>
    public bool Update()
    {
        if (failed)
        {
            failed = false;
            if (!usingFallback)
            {
                usingFallback = true;
                manager?.Note("Update check: this loader's versions image couldn't be downloaded, trying the other one");
                Download(FallbackUrl);
                return false;
            }
            State = "failed";
            manager?.Note("Update check: no versions image could be downloaded");
            return false;
        }
        if (!arrived) return false;
        arrived = false;
        var text = Decode();
        if (text == "")
        {
            // Unreadable: the other image, or give up.
            failed = true;
            manager?.Note("Update check: the versions image couldn't be read");
            return false;
        }
        Read(text);
        manager?.Note($"Update check: {Lines.Count} mods on Nexus Mods{(usingFallback ? " (from the other image)" : "")}");
        return true;
    }

    /// <summary>The text, as last read: kept by the manager for the next levels.</summary>
    public string Text()
    {
        var text = "";
        foreach (var line in Lines) text = text == "" ? line : text + "\n" + line;
        return text;
    }

    void Read(string text)
    {
        Lines = UKismetStringLibrary.ParseIntoArray(text, "\n", true);
        State = "done";
    }

    /// <summary>The image's text: its cells drawn on a render target and read back. "" when it isn't such an image.</summary>
    string Decode()
    {
        if (image == null || manager == null) return "";
        if (target == null)
        {
            // Sharp pixels (the filter only takes effect when the target is made, so it's made small and resized), sRGB so
            // the levels come back as stored.
            target = UKismetRenderingLibrary.CreateRenderTarget2D(manager, 1, 1, ETextureRenderTargetFormat.RTF_RGBA8_SRGB, new FLinearColor(), false, false);
            if (target == null) return "";
            target.Filter = TextureFilter.TF_Nearest;
            UKismetRenderingLibrary.ResizeRenderTarget2D(target, Size, Size);
        }
        UKismetRenderingLibrary.ClearRenderTarget2D(manager, target, new FLinearColor());
        UKismetRenderingLibrary.BeginDrawCanvasToRenderTarget(manager, target, out var canvas, out var size, out var context);
        canvas?.K2_DrawTexture(image, new FVector2D(), new FVector2D { X = Size, Y = Size }, new FVector2D(), new FVector2D { X = 1, Y = 1 },
            new FLinearColor { R = 1, G = 1, B = 1, A = 1 }, EBlendMode.BLEND_Opaque, 0, new FVector2D());
        UKismetRenderingLibrary.EndDrawCanvasToRenderTarget(manager, context);
        if (!UKismetRenderingLibrary.ReadRenderTarget(manager, target, out var pixels, false) || pixels.Count < Size * Size) return "";

        // The 4 levels as they came back, from the grey cells at the start.
        var levels = new List<int>();
        for (int i = 0; i < Levels; i++) levels.Add(Pixel(pixels, i).R);
        if (levels[Levels - 1] <= levels[0]) return "";
        var text = "";
        for (int cell = Levels; cell < Cells * Cells; cell++)
        {
            var colour = Pixel(pixels, cell);
            int value = Level(levels, colour.R) * 16 + Level(levels, colour.G) * 4 + Level(levels, colour.B);
            if (value == End) return text;
            if (value >= UKismetStringLibrary.Len(Alphabet)) return "";
            text += UKismetStringLibrary.GetSubstring(Alphabet, value, 1);
        }
        // No end mark: not one of these images.
        return "";
    }

    /// <summary>A cell's colour, at its top left pixel (cells go left to right, then down).</summary>
    static FColor Pixel(List<FColor> pixels, int cell)
    {
        int x = cell % Cells * Cell;
        int y = cell / Cells * Cell;
        return pixels[y * Size + x];
    }

    /// <summary>Which of the levels a channel is nearest.</summary>
    static int Level(List<int> levels, byte channel)
    {
        int best = 0;
        int bestDistance = 1000;
        for (int i = 0; i < levels.Count; i++)
        {
            int distance = UKismetMathLibrary.Abs_Int(channel - levels[i]);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>
    /// The latest version of a mod on Nexus Mods: of its file with that name, if it has one, else of its main file. ""
    /// when the mod isn't there.
    /// </summary>
    public string Latest(int nexusId, string fileName)
    {
        if (nexusId <= 0) return "";
        var prefix = nexusId + ";";
        var wanted = UKismetStringLibrary.ToLower(UKismetStringLibrary.Trim(UKismetStringLibrary.TrimTrailing(fileName)));
        foreach (var line in Lines)
        {
            if (!UKismetStringLibrary.StartsWith(line, prefix, ESearchCase.CaseSensitive)) continue;
            var parts = UKismetStringLibrary.ParseIntoArray(line, ";", false);
            var main = parts.Count > 1 ? parts[1] : "";
            if (wanted == "") return main;
            for (int i = 2; i < parts.Count; i++)
            {
                if (!UKismetStringLibrary.Split(parts[i], "=", out var name, out var version, ESearchCase.CaseSensitive, ESearchDir.FromEnd)) continue;
                if (name == wanted) return version;
            }
            return main;
        }
        return "";
    }

    /// <summary>Whether a version is newer than another: their numbers compared in turn (1.10 is newer than 1.9).</summary>
    public static bool Newer(string theirs, string ours)
    {
        var a = Numbers(theirs);
        var b = Numbers(ours);
        if (a.Count == 0 || b.Count == 0) return false;
        int count = a.Count > b.Count ? a.Count : b.Count;
        for (int i = 0; i < count; i++)
        {
            int x = i < a.Count ? a[i] : 0;
            int y = i < b.Count ? b[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    /// <summary>A version's numbers: "v1.2.10" is 1, 2, 10. None when it doesn't start with one.</summary>
    static List<int> Numbers(string version)
    {
        var numbers = new List<int>();
        var text = UKismetStringLibrary.ToLower(UKismetStringLibrary.Trim(version));
        if (UKismetStringLibrary.StartsWith(text, "v", ESearchCase.CaseSensitive)) text = UKismetStringLibrary.RightChop(text, 1);
        foreach (var part in UKismetStringLibrary.ParseIntoArray(text, ".", false))
        {
            var first = UKismetStringLibrary.GetSubstring(part, 0, 1);
            if (first == "" || UKismetStringLibrary.FindSubstring("0123456789", first, false, false, 0) < 0) break;
            numbers.Add(UKismetStringLibrary.Conv_StringToInt(part));
        }
        return numbers;
    }
}
