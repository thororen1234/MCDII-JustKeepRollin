using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.InterchangeCore;

/// <summary>A throwaway object to make the log's file objects in (making one needs no world).</summary>
public class FileLogAnchor : USaveGame
{
}

/// <summary>
/// A mod's log, in a file of its own on this PC: &lt;Saved&gt;\Mods\&lt;Mod&gt;.log, Saved being the game's own folder for its logs
/// (%LOCALAPPDATA%\Dungeons2\Saved). Never in a save slot: the Xbox app version syncs every save slot to the cloud. The
/// game can't write text files, so the file is the engine's Interchange node container, with one node whose attribute
/// names are the numbered lines: tools\readlog.py prints it. A log starts again with each game session (the last one is
/// kept as &lt;Mod&gt;.previous.log), and after <see cref="MaxLines"/> lines.
/// Linked into every mod by Directory.Build.targets; mods call it in place of NeoRune's Log, which writes to a save slot.
/// </summary>
public static class FileLog
{
    const int MaxLines = 5000;
    const string NodeId = "log";
    const string CountKey = "count";
    const string FrameKey = "frame";

    public static void Write(string line)
    {
        var lines = new List<string>();
        lines.Add(line);
        WriteAll(lines);
    }

    /// <summary>Appends several lines with one save of the file.</summary>
    public static void WriteAll(List<string> lines)
    {
        var anchor = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<FileLogAnchor>());
        if (anchor == null) return;
        var container = UGameplayStatics.SpawnObject(Unreal.ClassOf<UInterchangeBaseNodeContainer>(), anchor) as UInterchangeBaseNodeContainer;
        if (container == null) return;
        var file = Path("");
        // The engine's frame count starts at 0 with each game session: a smaller one than the log's last means a new one.
        double frame = UKismetSystemLibrary.GetFrameCount();
        UInterchangeBaseNode? node = null;
        if (UBlueprintPathsLibrary.FileExists(file))
        {
            container.LoadFromFile(file);
            node = container.GetNode(NodeId);
        }
        int count = 0;
        if (node != null)
        {
            node.GetInt32Attribute(CountKey, out count);
            node.GetDoubleAttribute(FrameKey, out var last);
            if (frame < last || count >= MaxLines)
            {
                container.SaveToFile(Path(".previous"));
                container.Reset();
                node = null;
                count = 0;
            }
        }
        if (node == null)
        {
            // The log NeoRune kept in a save slot before, which synced to the cloud: gone with the save API, so the cloud
            // forgets it too.
            var oldSlot = "NeoRune_" + Unreal.ModName;
            if (UGameplayStatics.DoesSaveGameExist(oldSlot, 0)) UGameplayStatics.DeleteGameInSlot(oldSlot, 0);
            node = UGameplayStatics.SpawnObject(Unreal.ClassOf<UInterchangeBaseNode>(), container) as UInterchangeBaseNode;
            if (node == null) return;
            node.InitializeNode(NodeId, Unreal.ModName, EInterchangeNodeContainerType.None);
            container.AddNode(node);
        }
        foreach (var line in lines)
        {
            count++;
            // The line is the attribute's name, after its number (the file keeps attributes in no order).
            var number = count.ToString();
            while (number.Length < 6) number = "0" + number;
            node.AddBooleanAttribute(number + "|" + line, true);
        }
        node.AddInt32Attribute(CountKey, count);
        node.AddDoubleAttribute(FrameKey, frame);
        container.SaveToFile(file);
    }

    /// <summary>The log file's path: &lt;Saved&gt;/Mods/&lt;Mod&gt;&lt;suffix&gt;.log.</summary>
    static string Path(string suffix) => UBlueprintPathsLibrary.ProjectSavedDir() + "Mods/" + Unreal.ModName + suffix + ".log";
}
