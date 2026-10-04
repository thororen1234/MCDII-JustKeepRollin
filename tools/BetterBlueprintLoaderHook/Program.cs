using System.Diagnostics;
using NeoRune.Assets;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;

// BetterBlueprintLoader's R1 hook. The game loads the game feature data /R1/R1 with every level; this writes the same
// data plus one more action, which has the game add the loader's component to the player controller, into the mod's
// staging folder, then packs the mod again the way NeoRune does.
//
// Usage: BetterBlueprintLoaderHook <NeoRune out dir> <mod name> <NeoRune tools dir>

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: BetterBlueprintLoaderHook <NeoRune out dir> <mod name> <NeoRune tools dir>");
    return 1;
}
var outDir = args[0];
var modName = args[1];
var toolsDir = args[2];
var staging = Path.Combine(outDir, "Temp", "Staging");
var header = Path.Combine(outDir, "Temp", "Header");
if (!Directory.Exists(staging) || !Directory.Exists(header))
{
    Console.Error.WriteLine($"No NeoRune staging folder in {outDir}: build the mod first");
    return 1;
}

var r1 = R1.Write(Path.Combine(staging, "Dungeons", "Plugins", "GameFeatures", "R1", "Content"), modName);
R1.Check(r1);

var stem = Path.Combine(outDir, "Pak", modName + "_P");
foreach (var extension in new[] { ".pak", ".utoc", ".ucas" })
    if (File.Exists(stem + extension)) File.Delete(stem + extension);
Run(Path.Combine(toolsDir, "retoc.exe"), "to-zen", "--version", "UE5_6", staging, stem + ".utoc");
if (File.Exists(stem + ".pak")) File.Delete(stem + ".pak");
Run(Path.Combine(toolsDir, "repak.exe"), "pack", "-q", "--version", "V11", header, stem + ".pak");
Console.WriteLine($"BetterBlueprintLoader: added the R1 hook to {stem}.utoc");
return 0;

static void Run(string tool, params string[] arguments)
{
    var start = new ProcessStartInfo(tool) { UseShellExecute = false };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException($"Couldn't start {tool}");
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(tool)} failed with exit code {process.ExitCode}");
}

static class R1
{
    const EObjectFlags AssetFlags = EObjectFlags.RF_Public | EObjectFlags.RF_Standalone | EObjectFlags.RF_Transactional;
    const EObjectFlags ActionFlags = EObjectFlags.RF_Public | EObjectFlags.RF_Transactional;

    /// <summary>
    /// The game's /R1/R1 (read from the game with retoc), with the loader's action added after its two:
    /// Actions: R1_GameFeatureAction, GameFeatureAction_AddWorldPartitionContent (the Overworld's R1 data layer), and ours.
    /// PrimaryAssetTypesToScan: the SWTypeSystemPrimaryDataAsset class, with /R1/SWTypeSystemPrimaryDataAssetR1 as its
    /// only asset (the rules are the defaults: priority -1, chunk -1, recursive).
    ///
    /// Written as the game cooks it, with unversioned properties: the game reads tagged ones too, but skips a value whose
    /// type name it doesn't agree with, and a skipped component entry adds nothing.
    /// </summary>
    public static string Write(string directory, string modName)
    {
        var p = new PackageBuilder("/R1/R1");
        var dataClass = p.ImportClass("/Script/GameFeatures.GameFeatureData");
        var dataDefaults = p.ImportDefaultObject("/Script/GameFeatures.GameFeatureData");
        var r1ActionClass = p.ImportClass("/Script/R1.R1_GameFeatureAction");
        var r1ActionDefaults = p.ImportDefaultObject("/Script/R1.R1_GameFeatureAction");
        var worldClass = p.ImportClass("/Script/GameFeatures.GameFeatureAction_AddWorldPartitionContent");
        var worldDefaults = p.ImportDefaultObject("/Script/GameFeatures.GameFeatureAction_AddWorldPartitionContent");
        var componentsClass = p.ImportClass("/Script/GameFeatures.GameFeatureAction_AddComponents");
        var componentsDefaults = p.ImportDefaultObject("/Script/GameFeatures.GameFeatureAction_AddComponents");
        var dataLayer = p.ImportObject("/R1/Spicewood/Maps/Overworld/R1.R1", "/Script/Engine.ExternalDataLayerAsset");

        var data = Export(p, "R1", dataClass, dataDefaults, FPackageIndex.FromRawIndex(0), AssetFlags);
        data.bIsAsset = true;
        var dataIndex = p.AddExport(data);
        var r1Action = Export(p, "R1_GameFeatureAction_0", r1ActionClass, r1ActionDefaults, dataIndex, ActionFlags);
        var r1ActionIndex = p.AddExport(r1Action);
        var world = Export(p, "GameFeatureAction_AddWorldPartitionContent_0", worldClass, worldDefaults, dataIndex, ActionFlags);
        var worldIndex = p.AddExport(world);
        var components = Export(p, "GameFeatureAction_AddComponents_0", componentsClass, componentsDefaults, dataIndex, ActionFlags);
        var componentsIndex = p.AddExport(components);

        // The actions are made after the data they're in, and the data loads after its actions.
        data.CreateBeforeSerializationDependencies = new List<FPackageIndex> { r1ActionIndex, worldIndex, componentsIndex };
        world.CreateBeforeSerializationDependencies = new List<FPackageIndex> { dataLayer };

        // UGameFeatureData: Actions, PrimaryAssetTypesToScan.
        var r1 = new Unversioned(p);
        r1.Fragment(0, 2, zeroes: false, last: true);
        r1.Int(3);
        r1.Int(r1ActionIndex.Index);
        r1.Int(worldIndex.Index);
        r1.Int(componentsIndex.Index);
        r1.Int(1);
        // FPrimaryAssetTypeInfo: PrimaryAssetType (None: zero), AssetBaseClass, then AssetBaseClassLoaded (transient)
        // skipped, bHasBlueprintClasses and bIsEditorOnly (false: zero), Directories, SpecificAssets, Rules.
        r1.Fragment(0, 2, zeroes: true, last: false);
        r1.Fragment(1, 5, zeroes: true, last: true);
        r1.Byte(0b0000_1101);
        r1.SoftPath("/Script/SWCoreGameplay.SWTypeSystemPrimaryDataAsset");
        r1.Int(0);
        r1.Int(1);
        r1.SoftPath("/R1/SWTypeSystemPrimaryDataAssetR1.SWTypeSystemPrimaryDataAssetR1");
        // FPrimaryAssetRules: Priority -1, ChunkId -1, bApplyRecursively, CookRule (Unknown: zero).
        r1.Fragment(0, 4, zeroes: true, last: true);
        r1.Byte(0b0000_1000);
        r1.Int(-1);
        r1.Int(-1);
        r1.Byte(1);
        r1.Int(0);

        // UR1_GameFeatureAction: nothing.
        var action = new Unversioned(p);
        action.Fragment(0, 0, zeroes: false, last: true);
        action.Int(0);

        // UGameFeatureAction_AddWorldPartitionContent: ExternalDataLayerAsset.
        var layer = new Unversioned(p);
        layer.Fragment(0, 1, zeroes: false, last: true);
        layer.Int(dataLayer.Index);
        layer.Int(0);

        // UGameFeatureAction_AddComponents: ComponentList, one FGameFeatureComponentEntry: ActorClass, ComponentClass,
        // bClientComponent, bServerComponent, AdditionFlags (zero).
        var list = new Unversioned(p);
        list.Fragment(0, 1, zeroes: false, last: true);
        list.Int(1);
        list.Fragment(0, 5, zeroes: true, last: true);
        list.Byte(0b0001_0000);
        list.SoftPath("/Script/Engine.PlayerController");
        list.SoftPath($"/Game/Mods/{modName}/LoaderComponent.LoaderComponent_C");
        list.Byte(1);
        list.Byte(1);
        list.Int(0);

        data.Data = r1.Bytes();
        r1Action.Data = action.Bytes();
        world.Data = layer.Bytes();
        components.Data = list.Bytes();

        p.Asset.PackageFlags |= EPackageFlags.PKG_UnversionedProperties;
        var path = p.Write(directory, "R1");
        // As the game's: no script serialization offsets.
        foreach (var export in p.Asset.Exports)
        {
            export.ScriptSerializationStartOffset = 0;
            export.ScriptSerializationEndOffset = 0;
        }
        p.Asset.Write(path);
        return path;
    }

    static RawExport Export(PackageBuilder p, string name, FPackageIndex cls, FPackageIndex template, FPackageIndex outer, EObjectFlags flags)
    {
        return new RawExport
        {
            ObjectName = p.Name(name),
            ClassIndex = cls,
            SuperIndex = FPackageIndex.FromRawIndex(0),
            TemplateIndex = template,
            OuterIndex = outer,
            ObjectFlags = flags,
            Data = Array.Empty<byte>(),
            Extras = Array.Empty<byte>(),
            SerializationBeforeSerializationDependencies = new List<FPackageIndex>(),
            CreateBeforeSerializationDependencies = new List<FPackageIndex>(),
            // Its class and the class's defaults, as the game's own R1 has it.
            SerializationBeforeCreateDependencies = new List<FPackageIndex> { cls, template },
            CreateBeforeCreateDependencies = outer.Index != 0 ? new List<FPackageIndex> { outer } : new List<FPackageIndex>(),
        };
    }

    /// <summary>
    /// Unreal's unversioned property data: fragments saying which of a struct's properties follow (in declaration
    /// order), a mask of the ones that are zero and so not written, then the values; after an object's properties, its
    /// GUID flag.
    /// </summary>
    sealed class Unversioned
    {
        readonly PackageBuilder package;
        readonly MemoryStream stream = new();
        readonly BinaryWriter writer;

        public Unversioned(PackageBuilder package)
        {
            this.package = package;
            writer = new BinaryWriter(stream);
        }

        /// <summary>Skips some properties, then has some values follow.</summary>
        public void Fragment(int skip, int values, bool zeroes, bool last) =>
            writer.Write((ushort)(skip | (zeroes ? 1 << 7 : 0) | (last ? 1 << 8 : 0) | (values << 9)));

        public void Byte(byte value) => writer.Write(value);

        public void Int(int value) => writer.Write(value);

        void Name(string value)
        {
            package.Name(value);
            writer.Write(package.Asset.SearchNameReference(new FString(value)));
            writer.Write(0);
        }

        /// <summary>An FSoftObjectPath: package and asset names, and an empty sub path.</summary>
        public void SoftPath(string path)
        {
            var (packageName, assetName) = PackageBuilder.Split(path);
            Name(packageName);
            Name(assetName);
            writer.Write(0);
        }

        public byte[] Bytes()
        {
            writer.Flush();
            return stream.ToArray();
        }
    }

    /// <summary>Reads the written R1 back and prints its exports, so a build log shows what went into the pak.</summary>
    public static void Check(string path)
    {
        var asset = new UAsset(path, EngineVersion.VER_UE5_6);
        foreach (var export in asset.Exports)
        {
            var cls = export.ClassIndex.IsImport() ? export.ClassIndex.ToImport(asset).ObjectName.ToString() : "?";
            var bytes = export is RawExport raw ? BitConverter.ToString(raw.Data) : export.GetType().Name;
            Console.WriteLine($"  {export.ObjectName} ({cls}): {bytes}");
        }
    }
}
