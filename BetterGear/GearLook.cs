using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.GameplayTags;
using UE.InventorySystem;
using UE.LootProgression;
using UE.MainMenu;
using UE.SWCoreGameplay;
using UE.UMG;

namespace BetterGear;

/// <summary>Remembers the looks and the gear found, between levels and game sessions: SaveGames/BetterGear.sav.</summary>
public class BetterGearSettings : USaveGame
{
    // Per gear kind (GearLook's kind numbers): "" for the game's look, or the tag of the item worn as a look.
    public List<string> Looks = new();
    // Every armor piece and weapon seen in the inventory, by tag: they stay pickable after being salvaged.
    public List<string> Found = new();
    // The item worn per kind when last seen, for the main menu's character (which has no inventory).
    public List<string> Worn = new();
    // The Hide Armor key's switch: all four armor kinds hidden, whatever their look.
    public bool ArmorHidden;
    // Per gear kind: hidden on its own (from its right-click menu, or the None box), whatever its look.
    public List<bool> Hidden = new();
}

/// <summary>A part of the character's gear the mod changed, with what it had before.</summary>
public class GearChange : UObject
{
    public UMeshComponent? Part;
    public UObject? Mesh;
    public TSubclassOf<UObject>? Anim;
    public FName Socket;
    public FVector Location;
    public FRotator Rotation;
    public FVector Scale;
    public bool Hidden;
    // Hidden by the mod now: kept hidden every frame (the game shows its gear again, e.g. after an attack).
    public bool Hiding;
    public List<UMaterialInterface?> Materials = new();
    // Still wanted in this check: the ones that aren't get their old look back.
    public bool Kept;
}

/// <summary>A part of a look that the worn item has nothing in the place of: one of the mod's own.</summary>
public class GearExtra : UObject
{
    public AActor? Wearer;
    public UMeshComponent? Part;
    // The look's part it copies.
    public UMeshComponent? Source;
    public bool Kept;
}

/// <summary>
/// Hides the character's armor and weapons, or shows another armor piece or weapon in their place: one found before,
/// salvaged ones too. Only this game sees it: the server and other players still see the gear worn.
///
/// The game puts each worn item's parts on the character's body as its own components, copied from the item's gear actor
/// and attached to the body's sockets (gear not worn keeps its parts, hidden). A look is a copy of its item's gear actor,
/// spawned hidden under the world: each part of the worn item gets the mesh and materials of the look's part on the same
/// socket (or else the next one left), so the game's own showing and hiding of them (a weapon in the hand or on the
/// back, a bow's frames) still works. Parts left over are hidden, and look parts left over are added.
/// </summary>
public class GearLook : UObject
{
    const string SettingsSlot = "BetterGear";
    public const string Hidden = "hidden";
    public const int KindCount = 6;
    public const int Helmet = 0;
    public const int Chest = 1;
    public const int Legs = 2;
    public const int Boots = 3;
    public const int Melee = 4;
    public const int Ranged = 5;
    // Every item's type asset: SW.Item.ScavengerChest is SW_Item_ScavengerChest, with its gear actor's class in it.
    const string TypeAssets = "/Game/Spicewood/GameplayLogic/SWTypeSystemPrimaryDataAsset/_Generated_/";
    // The body of the player's character and of its copies in menus.
    const string PlayerBody = "/Game/Spicewood/Art/Characters/Player/Master/SK_Player_Master.SK_Player_Master";
    // Where the looks' gear actors wait, hidden.
    const float TemplateDepth = -20000;

    UObject? owner;
    BetterGearSettings? settings;
    // Per kind: the slot's tag, and the item worn in it, as last read from the inventory.
    List<FGameplayTag> slots = new();
    List<bool> hasSlot = new();
    List<string> worn = new();
    // The items' gear actors, spawned hidden, by tag; tags with none.
    Dictionary<string, AActor> templates = new();
    List<string> failed = new();
    // Item tags already sorted into kinds (or none), so each is looked up once.
    List<string> sorted = new();
    // The game's own list of the loot collected.
    List<string> collected = new();
    Dictionary<string, UTexture2D> icons = new();
    Dictionary<string, string> titles = new();
    List<GearChange> changes = new();
    List<GearExtra> extras = new();
    // Kept between checks so they aren't made again each time.
    List<UMeshComponent> parts = new();
    List<UMeshComponent> mine = new();
    List<UMeshComponent> lookParts = new();
    List<bool> used = new();
    // Test logging: the looks already reported, per wearer.
    List<string> reported = new();
    bool loggedSlots;

    public static GearLook? Create(UObject owner)
    {
        var look = UGameplayStatics.SpawnObject(Unreal.ClassOf<GearLook>(), owner) as GearLook;
        if (look == null) return null;
        look.owner = owner;
        look.settings = UGameplayStatics.LoadGameFromSlot(SettingsSlot, 0) as BetterGearSettings;
        if (look.settings == null)
            look.settings = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<BetterGearSettings>()) as BetterGearSettings;
        if (look.settings != null)
        {
            while (look.settings.Looks.Count < KindCount) look.settings.Looks.Add("");
            while (look.settings.Worn.Count < KindCount) look.settings.Worn.Add("");
            while (look.settings.Hidden.Count < KindCount) look.settings.Hidden.Add(false);
            // Saved by version 1.0.0, which kept hidden as a look.
            for (int i = 0; i < KindCount; i++)
                if (look.settings.Looks[i] == Hidden)
                {
                    look.settings.Looks[i] = "";
                    look.settings.Hidden[i] = true;
                }
        }
        for (int i = 0; i < KindCount; i++)
        {
            look.slots.Add(new FGameplayTag());
            look.hasSlot.Add(false);
            look.worn.Add(look.settings != null ? look.settings.Worn[i] : "");
        }
        return look;
    }

    /// <summary>A kind's name, for the menu.</summary>
    public static string KindTitle(int kind)
    {
        if (kind == Helmet) return "Helmet";
        if (kind == Chest) return "Chestplate";
        if (kind == Legs) return "Leggings";
        if (kind == Boots) return "Boots";
        if (kind == Melee) return "Melee Weapon";
        return "Ranged Weapon";
    }

    /// <summary>The kind of gear an equipment slot holds, from its tag's name, or -1 for other slots.</summary>
    public static int KindOf(string slot)
    {
        var name = slot.ToLower();
        if (name.Contains("artifact") || name.Contains("potion")) return -1;
        if (name.Contains("helmet") || name.Contains("head")) return Helmet;
        if (name.Contains("chest")) return Chest;
        if (name.Contains("legging") || name.Contains("legs")) return Legs;
        if (name.Contains("boot") || name.Contains("feet")) return Boots;
        if (name.Contains("melee")) return Melee;
        if (name.Contains("ranged")) return Ranged;
        return -1;
    }

    /// <summary>The look of a kind: "" for the game's, <see cref="Hidden"/>, or the tag of the item worn as a look.</summary>
    public string Look(int kind)
    {
        if (settings == null || kind < 0 || kind >= KindCount) return "";
        if ((settings.ArmorHidden && kind <= Boots) || settings.Hidden[kind]) return Hidden;
        return settings.Looks[kind];
    }

    /// <summary>The look picked for a kind (<see cref="Hidden"/> when hidden on its own), not counting the Hide Armor key.</summary>
    public string Picked(int kind)
    {
        if (settings == null || kind < 0 || kind >= KindCount) return "";
        return settings.Hidden[kind] ? Hidden : settings.Looks[kind];
    }

    public bool ArmorHidden => settings != null && settings.ArmorHidden;

    /// <summary>Whether a kind is hidden on its own (not counting the Hide Armor key).</summary>
    public bool IsHidden(int kind) => settings != null && kind >= 0 && kind < KindCount && settings.Hidden[kind];

    /// <summary>
    /// Wears a look: "" for the game's, or an item's tag; <see cref="Hidden"/> hides the kind and keeps its look for when
    /// it's shown again. Remembered for next time.
    /// </summary>
    public void Select(int kind, string look)
    {
        if (settings == null || kind < 0 || kind >= KindCount) return;
        if (look == Hidden) settings.Hidden[kind] = true;
        else
        {
            settings.Looks[kind] = look;
            settings.Hidden[kind] = false;
        }
        UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
        Check();
        Log.Write($"{KindTitle(kind)}: {(look == "" ? "the game's look" : look)}");
    }

    /// <summary>Hides a kind on its own, or shows it again with its look. Remembered for next time.</summary>
    public void ToggleHidden(int kind)
    {
        if (settings == null || kind < 0 || kind >= KindCount) return;
        settings.Hidden[kind] = !settings.Hidden[kind];
        UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
        Check();
        Log.Write($"{KindTitle(kind)} {(settings.Hidden[kind] ? "hidden" : "shown")}");
    }

    /// <summary>Hides all the armor, whatever its look, or shows it again.</summary>
    public void ToggleArmor()
    {
        if (settings == null) return;
        settings.ArmorHidden = !settings.ArmorHidden;
        UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
        Check();
    }

    /// <summary>Whether a kind has a slot in the inventory (read on the last check).</summary>
    public bool HasSlot(int kind) => kind >= 0 && kind < KindCount && hasSlot[kind];

    /// <summary>The tag of the item worn in a kind's slot, or "".</summary>
    public string Worn(int kind) => kind >= 0 && kind < KindCount ? worn[kind] : "";

    /// <summary>What the menu shows: changes when gear is worn, found, or the looks change.</summary>
    public string Signature()
    {
        var text = "";
        for (int i = 0; i < KindCount; i++) text += worn[i] + "|" + Look(i) + "|";
        return text + (settings != null ? settings.Found.Count : 0) + "|" + collected.Count;
    }

    /// <summary>
    /// The items that can be worn as a kind's look: the ones that fit its slot that were found (with everything, all of
    /// them), not counting the one worn.
    /// </summary>
    public List<string> Choices(int kind, bool everything)
    {
        var choices = new List<string>();
        var player = owner != null ? World.Player(owner) : null;
        if (player == null || settings == null || !HasSlot(kind)) return choices;
        foreach (var item in UInventoryHelperLibrary.GetAllPossibleItemsForSlot(player, slots[kind]))
        {
            var tag = item.TagName.ToString();
            if (tag == "None" || tag == worn[kind] || choices.Contains(tag)) continue;
            if (everything || settings.Found.Contains(tag) || collected.Contains(tag)) choices.Add(tag);
        }
        return choices;
    }

    /// <summary>An item's icon, or null.</summary>
    public UTexture2D? Icon(string tag)
    {
        if (icons.ContainsKey(tag)) return icons[tag];
        if (owner == null) return null;
        var soft = UInventoryHelperLibrary.GetTypeIcon(owner, new FGameplayTag { TagName = tag }, 0);
        var path = UKismetSystemLibrary.Conv_SoftObjectReferenceToString(soft);
        if (string.IsNullOrEmpty(path)) return null;
        var icon = Load(path) as UTexture2D;
        if (icon != null) icons[tag] = icon;
        return icon;
    }

    /// <summary>An item's name in the game's language.</summary>
    public string Title(string tag)
    {
        if (titles.ContainsKey(tag)) return titles[tag];
        if (owner == null) return tag;
        var title = UKismetTextLibrary.Conv_TextToString(UInventoryHelperLibrary.GetTypeTitle(owner, new FGameplayTag { TagName = tag }, false));
        if (string.IsNullOrEmpty(title)) title = tag;
        titles[tag] = title;
        return title;
    }

    /// <summary>
    /// Call regularly: reads the gear worn and found, and puts the looks on the character and on its copies in menus,
    /// after the game changed or put back its own.
    /// </summary>
    public void Check()
    {
        if (owner == null || settings == null) return;
        var player = World.Player(owner);
        if (player != null) ReadInventory(player);
        foreach (var change in changes)
        {
            change.Kept = false;
            change.Hiding = false;
        }
        foreach (var extra in extras) extra.Kept = false;

        if (player is ACharacter character && character.Mesh != null) Dress(character, character.Mesh);
        foreach (var preview in World.FindAll(owner, Unreal.ClassOf<ACharacterPreviewActor>()))
            if (preview != null) DressCopy(preview);
        // The main menu's character by the campfire, which wears what was worn when last played.
        if (player == null)
            foreach (var preview in World.FindAll(owner, Unreal.ClassOf<APartyPreviewActor>()))
                if (preview != null) DressCopy(preview);
        Release();
    }

    void DressCopy(AActor actor)
    {
        var body = Body(actor);
        if (body != null) Dress(actor, body);
    }

    /// <summary>Reads the slots, the gear worn in them, and the gear in the inventory (to remember it as found).</summary>
    void ReadInventory(AActor player)
    {
        if (settings == null) return;
        var inventory = UInventoryManagerComponent.GetComponent(player);
        if (inventory == null) return;
        var names = "";
        foreach (var slot in inventory.GetInventory())
        {
            var name = slot.TypeTag.TagName.ToString();
            names += name + " ";
            int kind = KindOf(name);
            if (kind < 0) continue;
            slots[kind] = slot.TypeTag;
            hasSlot[kind] = true;
        }
        bool changed = false;
        for (int i = 0; i < KindCount; i++)
        {
            var now = "";
            foreach (var entry in inventory.GetEquippedItems())
                if (KindOf(entry.EquippedSlot.TagName.ToString()) == i) now = entry.ItemData.TypeTag.TagName.ToString();
            worn[i] = now;
            if (now != "" && settings.Worn[i] != now)
            {
                settings.Worn[i] = now;
                changed = true;
            }
        }
        foreach (var entry in inventory.GetAllItems())
        {
            var tag = entry.ItemData.TypeTag.TagName.ToString();
            if (sorted.Contains(tag)) continue;
            sorted.Add(tag);
            if (KindOf(UInventoryHelperLibrary.GetItemSlotTag(player, entry.ItemData.TypeTag).TagName.ToString()) < 0) continue;
            if (settings.Found.Contains(tag)) continue;
            settings.Found.Add(tag);
            changed = true;
        }
        ReadCollected(player);
        if (changed) UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
        if (!loggedSlots)
        {
            loggedSlots = true;
            var kinds = "";
            for (int i = 0; i < KindCount; i++) kinds += $"{KindTitle(i)}={(hasSlot[i] ? slots[i].TagName.ToString() : "-")} ({worn[i]}), ";
            Log.Write($"Inventory slots: {names}");
            Log.Write($"Gear: {kinds}{settings.Found.Count} found, {collected.Count} collected");
        }
    }

    /// <summary>The game's list of the loot collected, on the character, its controller or its player state.</summary>
    void ReadCollected(AActor player)
    {
        var progress = player.GetComponentByClass(Unreal.ClassOf<ULootProgressionComponent>()) as ULootProgressionComponent;
        var controller = World.PlayerController(player);
        if (progress == null && controller != null)
            progress = controller.GetComponentByClass(Unreal.ClassOf<ULootProgressionComponent>()) as ULootProgressionComponent;
        if (progress == null && controller?.PlayerState != null)
            progress = controller.PlayerState.GetComponentByClass(Unreal.ClassOf<ULootProgressionComponent>()) as ULootProgressionComponent;
        if (progress == null || progress.LootItemsCollected.Count == collected.Count) return;
        collected.Clear();
        foreach (var tag in progress.LootItemsCollected) collected.Add(tag.TagName.ToString());
    }

    /// <summary>
    /// Puts the looks on an actor wearing the player's gear (attached to its body). Safe to call again: it only changes
    /// what the game changed back since.
    /// </summary>
    void Dress(AActor wearer, USkeletalMeshComponent body)
    {
        // The parts of gear on the body: the game's, not the mod's own.
        parts.Clear();
        foreach (var component in wearer.K2_GetComponentsByClass(Unreal.ClassOf<UMeshComponent>()))
            if (component is UMeshComponent part && IsPart(part) && part.GetAttachParent() == body && Extra(part) == null)
                parts.Add(part);

        for (int kind = 0; kind < KindCount; kind++)
        {
            var look = Look(kind);
            var item = worn[kind];
            if (look == "" || item == "" || look == item) continue;
            var itemTemplate = Template(item);
            if (itemTemplate == null) continue;
            // The worn item's parts: on the body, on the sockets and with the meshes its gear actor has them.
            mine.Clear();
            foreach (var source in itemTemplate.K2_GetComponentsByClass(Unreal.ClassOf<UMeshComponent>()))
            {
                if (source is not UMeshComponent sourcePart || !IsPart(sourcePart)) continue;
                var socket = sourcePart.GetAttachSocketName().ToString();
                var mesh = MeshOf(sourcePart);
                foreach (var part in parts)
                    if (!mine.Contains(part) && OriginalSocket(part) == socket && OriginalMesh(part) == mesh) mine.Add(part);
            }
            if (mine.Count == 0) continue;

            if (look == Hidden)
            {
                foreach (var part in mine) Hide(part);
                continue;
            }
            var lookTemplate = Template(look);
            if (lookTemplate == null) continue;
            lookParts.Clear();
            foreach (var source in lookTemplate.K2_GetComponentsByClass(Unreal.ClassOf<UMeshComponent>()))
                if (source is UMeshComponent sourcePart && IsPart(sourcePart)) lookParts.Add(sourcePart);
            used.Clear();
            foreach (var source in lookParts) used.Add(false);

            // Each worn part takes the look's part on its socket, then the next one left of the same sort (moved to its
            // socket); worn parts left over are hidden.
            var unpaired = new List<UMeshComponent>();
            foreach (var part in mine)
            {
                int at = -1;
                for (int i = 0; i < lookParts.Count && at < 0; i++)
                    if (!used[i] && SameSort(part, lookParts[i]) && lookParts[i].GetAttachSocketName().ToString() == OriginalSocket(part)) at = i;
                if (at < 0)
                {
                    unpaired.Add(part);
                    continue;
                }
                used[at] = true;
                Wear(part, lookParts[at], body);
            }
            foreach (var part in unpaired)
            {
                int at = -1;
                for (int i = 0; i < lookParts.Count && at < 0; i++)
                    if (!used[i] && SameSort(part, lookParts[i])) at = i;
                if (at < 0)
                {
                    Hide(part);
                    continue;
                }
                used[at] = true;
                Wear(part, lookParts[at], body);
            }
            // The look's parts left over are the mod's own, shown while the worn item is.
            bool shown = false;
            foreach (var part in mine) shown = shown || Shown(part);
            int added = 0;
            for (int i = 0; i < lookParts.Count; i++)
                if (!used[i])
                {
                    AddExtra(wearer, body, lookParts[i], shown);
                    added++;
                }
            var report = $"{UKismetSystemLibrary.GetObjectName(wearer)}: {item} as {look}";
            if (!reported.Contains(report))
            {
                reported.Add(report);
                Log.Write($"{report}: {mine.Count} worn parts, {lookParts.Count} look parts, {unpaired.Count} not on the same socket, {added} added");
            }
        }
    }

    /// <summary>A mesh of a gear actor on one of the body's sockets: not its storage box, not its paperdoll.</summary>
    static bool IsPart(UMeshComponent mesh)
    {
        if (mesh is UInstancedStaticMeshComponent || mesh is UPaperdollComponent) return false;
        if (mesh is not UStaticMeshComponent && mesh is not USkeletalMeshComponent) return false;
        return mesh.GetAttachSocketName().ToString() != "None";
    }

    static bool SameSort(UMeshComponent a, UMeshComponent b) => (a is UStaticMeshComponent) == (b is UStaticMeshComponent);

    static UObject? MeshOf(UMeshComponent mesh)
    {
        if (mesh is UStaticMeshComponent staticMesh) return staticMesh.StaticMesh;
        if (mesh is USkinnedMeshComponent skinned) return skinned.GetSkinnedAsset();
        return null;
    }

    GearChange? Change(UMeshComponent part)
    {
        foreach (var change in changes)
            if (change.Part == part) return change;
        return null;
    }

    GearExtra? Extra(UMeshComponent part)
    {
        foreach (var extra in extras)
            if (extra.Part == part) return extra;
        return null;
    }

    /// <summary>A part's mesh before the mod changed it.</summary>
    UObject? OriginalMesh(UMeshComponent part)
    {
        var change = Change(part);
        return change != null ? change.Mesh : MeshOf(part);
    }

    string OriginalSocket(UMeshComponent part)
    {
        var change = Change(part);
        return (change != null ? change.Socket : part.GetAttachSocketName()).ToString();
    }

    /// <summary>Whether the game shows a part (not counting the mod hiding it).</summary>
    bool Shown(UMeshComponent part)
    {
        var change = Change(part);
        return part.bVisible && !(change != null ? change.Hidden : part.bHiddenInGame);
    }

    /// <summary>A part's change, made (with what it has now) if it has none.</summary>
    GearChange? Remember(UMeshComponent part)
    {
        var change = Change(part);
        if (change != null)
        {
            change.Kept = true;
            return change;
        }
        change = UGameplayStatics.SpawnObject(Unreal.ClassOf<GearChange>(), this) as GearChange;
        if (change == null) return null;
        change.Part = part;
        change.Mesh = MeshOf(part);
        if (part is USkeletalMeshComponent skeletal) change.Anim = skeletal.GetAnimClass();
        change.Socket = part.GetAttachSocketName();
        change.Location = part.RelativeLocation;
        change.Rotation = part.RelativeRotation;
        change.Scale = part.RelativeScale3D;
        change.Hidden = part.bHiddenInGame;
        for (int i = 0; i < part.GetNumMaterials(); i++) change.Materials.Add(part.GetMaterial(i));
        change.Kept = true;
        changes.Add(change);
        return change;
    }

    void Hide(UMeshComponent part)
    {
        var change = Remember(part);
        if (change == null) return;
        change.Hiding = true;
        if (!part.bHiddenInGame) part.SetHiddenInGame(true, false);
    }

    /// <summary>
    /// Call every frame: hides again at once the parts the game showed since (between checks, which find new parts).
    /// </summary>
    public void KeepHidden()
    {
        foreach (var change in changes)
            if (change.Hiding && change.Part != null && UKismetSystemLibrary.IsValid(change.Part) && !change.Part.bHiddenInGame)
                change.Part.SetHiddenInGame(true, false);
    }

    /// <summary>Gives a worn part a look's part: its mesh, materials, socket and place on it.</summary>
    void Wear(UMeshComponent part, UMeshComponent source, USkeletalMeshComponent body)
    {
        var change = Remember(part);
        if (change == null) return;
        change.Hiding = false;
        if (part.bHiddenInGame != change.Hidden) part.SetHiddenInGame(change.Hidden, false);
        if (MeshOf(part) == MeshOf(source)) return;
        Copy(source, part);
        var socket = source.GetAttachSocketName();
        if (part.GetAttachSocketName().ToString() != socket.ToString())
            part.K2_AttachToComponent(body, socket, EAttachmentRule.KeepRelative, EAttachmentRule.KeepRelative, EAttachmentRule.KeepRelative, false);
        part.K2_SetRelativeLocationAndRotation(source.RelativeLocation, source.RelativeRotation, false, out var hit, false);
        part.SetRelativeScale3D(source.RelativeScale3D);
    }

    /// <summary>Copies a part's mesh, materials and animation onto another of the same sort.</summary>
    static void Copy(UMeshComponent source, UMeshComponent part)
    {
        if (part is UStaticMeshComponent staticPart && source is UStaticMeshComponent staticSource)
            staticPart.SetStaticMesh(staticSource.StaticMesh);
        else if (part is USkeletalMeshComponent skeletalPart && source is USkeletalMeshComponent skeletalSource)
        {
            skeletalPart.SetSkeletalMeshAsset(skeletalSource.GetSkinnedAsset() as USkeletalMesh);
            var anim = skeletalSource.GetAnimClass();
            if (anim != null) skeletalPart.SetAnimInstanceClass(anim);
        }
        for (int i = 0; i < source.GetNumMaterials(); i++) part.SetMaterial(i, source.GetMaterial(i));
    }

    /// <summary>Adds (or keeps) the mod's own copy of a look's part on an actor's body.</summary>
    void AddExtra(AActor wearer, USkeletalMeshComponent body, UMeshComponent source, bool shown)
    {
        foreach (var extra in extras)
            if (extra.Wearer == wearer && extra.Source == source && extra.Part != null && UKismetSystemLibrary.IsValid(extra.Part))
            {
                extra.Kept = true;
                if (extra.Part.bVisible != shown) extra.Part.SetVisibility(shown, false);
                return;
            }
        var identity = new FTransform { Rotation = UKismetMathLibrary.Quat_Identity(), Scale3D = new FVector { X = 1, Y = 1, Z = 1 } };
        var made = UGameplayStatics.SpawnObject(Unreal.ClassOf<GearExtra>(), this) as GearExtra;
        if (made == null) return;
        extras.Add(made);
        made.Wearer = wearer;
        made.Source = source;
        made.Kept = true;
        UMeshComponent? part;
        if (source is UStaticMeshComponent)
            part = wearer.AddComponentByClass(Unreal.ClassOf<UStaticMeshComponent>(), true, identity, false) as UMeshComponent;
        else part = wearer.AddComponentByClass(Unreal.ClassOf<USkeletalMeshComponent>(), true, identity, false) as UMeshComponent;
        made.Part = part;
        if (part == null) return;
        Copy(source, part);
        part.K2_AttachToComponent(body, source.GetAttachSocketName(), EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, false);
        part.K2_SetRelativeLocationAndRotation(source.RelativeLocation, source.RelativeRotation, false, out var hit, false);
        part.SetRelativeScale3D(source.RelativeScale3D);
        part.SetCollisionEnabled(ECollisionEnabled.NoCollision);
        // Lit like the body: the menus light their copies of the character on a lighting channel of their own.
        var lighting = body.LightingChannels;
        part.SetLightingChannels(lighting.bChannel0, lighting.bChannel1, lighting.bChannel2);
        part.SetVisibility(shown, false);
    }

    /// <summary>Gives the parts no longer wanted their old look back, and takes off the mod's own ones no longer wanted.</summary>
    void Release()
    {
        var keptChanges = new List<GearChange>();
        foreach (var change in changes)
        {
            if (change.Kept)
            {
                keptChanges.Add(change);
                continue;
            }
            var part = change.Part;
            if (part == null || !UKismetSystemLibrary.IsValid(part)) continue;
            if (MeshOf(part) != change.Mesh)
            {
                if (part is UStaticMeshComponent staticPart) staticPart.SetStaticMesh(change.Mesh as UStaticMesh);
                else if (part is USkeletalMeshComponent skeletalPart)
                {
                    skeletalPart.SetSkeletalMeshAsset(change.Mesh as USkeletalMesh);
                    if (change.Anim != null) skeletalPart.SetAnimInstanceClass(change.Anim);
                }
                for (int i = 0; i < change.Materials.Count; i++) part.SetMaterial(i, change.Materials[i]);
                if (part.GetAttachSocketName().ToString() != change.Socket.ToString() && part.GetAttachParent() != null)
                    part.K2_AttachToComponent(part.GetAttachParent(), change.Socket, EAttachmentRule.KeepRelative, EAttachmentRule.KeepRelative, EAttachmentRule.KeepRelative, false);
                part.K2_SetRelativeLocationAndRotation(change.Location, change.Rotation, false, out var hit, false);
                part.SetRelativeScale3D(change.Scale);
            }
            if (part.bHiddenInGame != change.Hidden) part.SetHiddenInGame(change.Hidden, false);
        }
        changes.Clear();
        foreach (var change in keptChanges) changes.Add(change);

        var keptExtras = new List<GearExtra>();
        foreach (var extra in extras)
        {
            if (extra.Kept)
            {
                keptExtras.Add(extra);
                continue;
            }
            if (extra.Part != null && UKismetSystemLibrary.IsValid(extra.Part)) extra.Part.K2_DestroyComponent(extra.Part);
        }
        extras.Clear();
        foreach (var extra in keptExtras) extras.Add(extra);
    }

    /// <summary>
    /// An item's gear actor, spawned hidden under the world to copy its parts from (once per level), or null if the item
    /// has none.
    /// </summary>
    AActor? Template(string tag)
    {
        if (templates.ContainsKey(tag) && UKismetSystemLibrary.IsValid(templates[tag])) return templates[tag];
        if (failed.Contains(tag) || owner == null) return null;
        var gearClass = GearClass(tag);
        var actor = gearClass != null ? World.Spawn(owner, gearClass, new FVector { Z = TemplateDepth }) : null;
        if (actor == null)
        {
            failed.Add(tag);
            Log.Write($"No gear actor found for {tag}");
            return null;
        }
        templates[tag] = actor;
        actor.SetActorHiddenInGame(true);
        actor.SetActorEnableCollision(false);
        actor.SetActorTickEnabled(false);
        var names = "";
        foreach (var component in actor.K2_GetComponentsByClass(Unreal.ClassOf<UMeshComponent>()))
            if (component is UMeshComponent part && IsPart(part))
                names += $"{UKismetSystemLibrary.GetObjectName(part)} on {part.GetAttachSocketName()} ({UKismetSystemLibrary.GetObjectName(MeshOf(part))}), ";
        Log.Write($"Gear actor for {tag}: {UKismetSystemLibrary.GetPathName(actor)}: {names}");
        return actor;
    }

    /// <summary>
    /// An item's gear actor class: from the item's type asset, which holds it, or else from the game's type system.
    /// </summary>
    TSubclassOf<AGearActor>? GearClass(string tag)
    {
        var name = tag.Replace(".", "_");
        if (Load($"{TypeAssets}{name}.{name}") is USWTypeSystemTypeDataAsset type)
            foreach (var reference in type.AssetReferences)
                if (reference != null && UKismetSystemLibrary.Conv_ObjectToClass(reference, Unreal.ClassOf<AGearActor>()) != null)
                    return Unreal.LoadClass<AGearActor>(UKismetSystemLibrary.GetPathName(reference));
        var types = owner != null ? USWTypeSystem.GetSWTypeSystemFromUObject(owner) : null;
        if (types == null) return null;
        var path = UKismetSystemLibrary.Conv_SoftObjectReferenceToString(types.GetAssetSoftPtr(new FGameplayTag { TagName = tag }, "ActorReference"));
        if (string.IsNullOrEmpty(path)) return null;
        return Unreal.LoadClass<AGearActor>(path);
    }

    /// <summary>The player's body on an actor, or null.</summary>
    static USkeletalMeshComponent? Body(AActor actor)
    {
        foreach (var component in actor.K2_GetComponentsByClass(Unreal.ClassOf<USkeletalMeshComponent>()))
            if (component is USkeletalMeshComponent mesh && UKismetSystemLibrary.GetPathName(mesh.GetSkinnedAsset()) == PlayerBody) return mesh;
        return null;
    }

    static UObject? Load(string path) =>
        UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(path)));
}
