using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.SWCoreGameplay;
using UE.UMG;

namespace CustomSkins;

/// <summary>Remembers the picked skin between levels and game sessions: SaveGames/CustomSkins.sav.</summary>
public class CustomSkinsSettings : USaveGame
{
    // 0 is the game's own skin, otherwise the number of the PNG in the Skins folder.
    public int Skin;
}

/// <summary>
/// Puts a skin PNG on the player's character, in place of the texture of the game's skin material. Only this game
/// sees it: the server and other players still have the skin picked in the game's menu.
/// </summary>
public class SkinSwapper : UObject
{
    const string SettingsSlot = "CustomSkins";
    const int MaxSkins = 20;
    // Skins are 64x64 in the game's format, but bigger (HD) ones are fine: past this, a texture is not a skin.
    const int MaxSkinSize = 1024;
    // The skin material's texture of how each pixel takes the light: red metallic, green roughness, blue glow, alpha
    // subsurface. Each skin has its own, so a custom skin gets <number>_MRES.png, or else a plain one.
    const string MresParameter = "MRES";
    // The body mesh of the player's character, and of its copies in menus.
    const string PlayerBody = "/Game/Spicewood/Art/Characters/Player/Master/SK_Player_Master.SK_Player_Master";
    // The inventory's picture of the character.
    const string PreviewWidget = "CharacterRender";
    // The game's skins are mostly this rough, with no metal, glow or subsurface.
    const float DefaultRoughness = 0.84f;
    // Converted skins keep the face with its eyes and mouth in this 8x8 block (unused in Java skins), and the head's
    // front has it without them.
    const int FaceBlockX = 56;
    const int FaceBlockY = 20;
    const int HeadFrontX = 8;
    const int HeadFrontY = 8;
    const int FaceSize = 8;
    // The second layer (the hat) over the head's front.
    const int HatFrontX = 40;
    const int HatFrontY = 8;
    // The game's skin materials take their skin from this.
    const string GameParameter = "BaseColour";
    // Texture parameters a skin material might take its skin from, the game's first (names ignore case).
    const string ParameterNames = GameParameter + "|Skin|SkinTexture|Skin_Texture|SkinTex|T_Skin|PlayerSkin|CharacterSkin|SkinMap|"
        + "BaseColor|BaseColorTexture|BaseColorMap|Base Color|Base_Color|Albedo|AlbedoTexture|Diffuse|DiffuseTexture|"
        + "DiffuseMap|Texture|Tex|MainTexture|MainTex|Color|ColorTexture|Avatar|AvatarTexture|CharacterTexture|Atlas";

    UObject? owner;
    CustomSkinsSettings? settings;
    // The PNGs as read, and per PNG the texture worn: the PNG with its face on, drawn again each time it's put on.
    Dictionary<int, UTexture2D> files = new();
    Dictionary<int, UTextureRenderTarget2D> withFaces = new();
    // Every texture this mod made, to tell them from the game's: a copy of the character the game makes while a custom
    // skin is on starts with it, and putting that one back when the skin changes would bring the last skin back.
    List<UTexture> ours = new();
    // The eyes and mouth that move with the face, for skins with them (see FaceParts), and which skins have them.
    FaceParts? face;
    Dictionary<int, bool> movingFaces = new();
    // The second layer (see SkinLayers): Off, Flat or 3D, the game's skin it was built from and the character it's on
    // (while the game's skin is worn), and render targets of the game's skins to read their pixels from.
    SkinLayers? layers;
    int layerMode;
    UTexture? layersFrom;
    ACharacter? layersOn;
    Dictionary<UTexture, UTextureRenderTarget2D> readable = new();
    // The game's skin as found on the character, put back on copies that only ever had a custom one.
    UTexture? gameSkin;
    UTexture? gameMres;
    Dictionary<int, UTexture2D> mresTextures = new();
    // The game's skin materials seen on the character, and their skins (read through an instance of each).
    Dictionary<UMaterialInterface, UTexture> materialSkins = new();
    // While dressing a copy of the character in a menu: the main menu's party has other skins, which aren't the game's
    // skin to remember (unless none is known yet).
    bool dressingCopy;
    UTexture? defaultMres;
    // LogInfo's lines: a field, because a List passed to a method is a copy in a Blueprint.
    List<string> info = new();

    // The skin materials changed, with what they had before: the game's own material instances (shared by the body,
    // the face and the second layer slots) are changed in place, other materials get an instance of ours.
    ACharacter? character;
    UTexture? worn;
    // The player's character and the menu's copies of it, once they wear the skin.
    List<AActor> dressed = new();
    // The menu characters already logged: the game puts its skin back on them often, and they get it again.
    List<AActor> announced = new();
    // Per menu character, its body's material when WatchPreviews last saw it dressed.
    Dictionary<AActor, UMaterialInterface> previewMaterials = new();
    // While the game's skin is worn: per copy of the character, the skin its second layer was built from.
    Dictionary<AActor, UTexture> layeredSkins = new();
    List<UMaterialInstanceDynamic> instances = new();
    List<FName> parameters = new();
    List<UTexture?> oldSkins = new();
    List<UTexture?> oldMres = new();
    // The material slots given an instance of ours, with the material they had.
    List<UMeshComponent> components = new();
    List<int> slots = new();
    List<UMaterialInterface> originals = new();
    List<UMaterialInstanceDynamic> added = new();
    // The character already reported as having no skin material, so Check doesn't log it every second.
    ACharacter? reported;

    public static SkinSwapper? Create(UObject owner)
    {
        var swapper = UGameplayStatics.SpawnObject(Unreal.ClassOf<SkinSwapper>(), owner) as SkinSwapper;
        if (swapper == null) return null;
        swapper.owner = owner;
        swapper.face = FaceParts.Create(swapper);
        swapper.layers = SkinLayers.Create(swapper);
        swapper.settings = UGameplayStatics.LoadGameFromSlot(SettingsSlot, 0) as CustomSkinsSettings;
        if (swapper.settings == null)
            swapper.settings = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<CustomSkinsSettings>()) as CustomSkinsSettings;
        return swapper;
    }

    /// <summary>The skin worn: 0 for the game's, otherwise the number of its PNG.</summary>
    public int Skin => settings != null ? settings.Skin : 0;

    /// <summary>Wears the next PNG in the Skins folder, then the game's skin again after the last one.</summary>
    public void Next()
    {
        int next = 0;
        foreach (var number in Available())
            if (number > Skin)
            {
                next = number;
                break;
            }
        Select(next);
    }

    /// <summary>Wears a skin: 0 for the game's, otherwise the number of its PNG. Remembered for next time.</summary>
    public void Select(int number)
    {
        if (settings == null) return;
        settings.Skin = number;
        UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
        FileLog.Write(number == 0 ? "Wearing the game's skin" : $"Wearing {number}.png ({Available().Count} skins in {Folder()})");
        Apply();
    }

    /// <summary>Shows the skin's second layer: <see cref="SkinLayers.Off"/>, Flat or Blocks (3D).</summary>
    public void SetLayers(int mode)
    {
        if (mode == layerMode) return;
        layerMode = mode;
        Apply();
    }

    /// <summary>Reads the PNG again, to see changes made to it while playing.</summary>
    public void Reload()
    {
        files.Clear();
        withFaces.Clear();
        mresTextures.Clear();
        Apply();
    }

    /// <summary>Call regularly: puts the skin back on a new character, or after the game put its own back.</summary>
    public void Check()
    {
        if (owner == null) return;
        var current = World.Player(owner) as ACharacter;
        if (Skin == 0)
        {
            // The game's skin's layers, on a new character or after the game's skin changed.
            if (layerMode != SkinLayers.Off && current != null && (current != layersOn || GameSkin() != layersFrom)) Apply();
            return;
        }
        if (current != null && current == reported) return;
        if (current != character || worn == null || (current != null && instances.Count == 0))
        {
            Apply();
            return;
        }
        for (int i = 0; i < instances.Count; i++)
            if (!UKismetSystemLibrary.IsValid(instances[i]) || instances[i].K2_GetTextureParameterValue(parameters[i]) != worn)
            {
                Apply();
                return;
            }
        for (int i = 0; i < added.Count; i++)
            if (!UKismetSystemLibrary.IsValid(components[i]) || components[i].GetMaterial(slots[i]) != added[i])
            {
                Apply();
                return;
            }
        // A new material instance on the body: the game changed skin.
        if (current != null && current.Mesh != null && !Changed(current.Mesh.GetMaterial(0)))
        {
            Apply();
            return;
        }
        // The main menu has no character of the player's, only the one by the campfire.
        if (current == null || PreviewShown()) DressPreviews();
    }

    bool Changed(UMaterialInterface? material) => material is UMaterialInstanceDynamic instance && instances.Contains(instance);

    void Apply()
    {
        Restore();
        reported = null;
        if (owner == null) return;
        if (Skin == 0)
        {
            GameSkinLayers();
            return;
        }
        character = World.Player(owner) as ACharacter;
        worn = Load(Skin);
        if (worn == null)
        {
            FileLog.Write($"Couldn't read {Skin}.png in {Folder()}");
            reported = character;
            return;
        }
        if (character != null)
        {
            Dress(character);
            if (instances.Count == 0)
            {
                FileLog.Write("Found no skin material on the character: press the Show Info key (F9 unless changed) in game and send the log");
                reported = character;
                return;
            }
        }
        if (character == null || PreviewShown()) DressPreviews();
    }

    /// <summary>
    /// Whether a menu shows the character: the inventory draws a copy of it, which wears its own copy of the skin.
    /// </summary>
    bool PreviewShown()
    {
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(owner, out var widgets, Unreal.ClassOf<UUserWidget>(), false);
        foreach (var widget in widgets)
            if (widget != null && UKismetSystemLibrary.GetObjectName(widget) == PreviewWidget && widget.IsVisible()) return true;
        return false;
    }

    /// <summary>
    /// Puts the skin on the copies of the character in menus (the inventory's picture, the main menu's campfire): actors
    /// with the player's body that aren't players.
    /// </summary>
    void DressPreviews()
    {
        foreach (var actor in World.FindAll(owner!, Unreal.ClassOf<AActor>()))
        {
            if (actor == null || actor == character || dressed.Contains(actor)) continue;
            // Other players: they keep their own skins.
            if (actor is APawn pawn && pawn.PlayerState != null) continue;
            if (actor.GetComponentByClass(Unreal.ClassOf<USkeletalMeshComponent>()) is not USkeletalMeshComponent mesh) continue;
            if (UKismetSystemLibrary.GetPathName(mesh.GetSkinnedAsset()) != PlayerBody) continue;
            var before = instances.Count;
            Dress(actor);
            if (announced.Contains(actor)) continue;
            announced.Add(actor);
            FileLog.Write($"Dressed the menu's character {UKismetSystemLibrary.GetPathName(actor)} ({instances.Count - before} materials)");
        }
    }

    /// <summary>
    /// Puts the skin and its second layer on the copies of the character (the menus': the inventory's, the lobby's, the
    /// main menu's; and the cutscenes', which are the inventory's kind too) the frame they show or the game puts its own
    /// skin back on them, rather than on the next check. Call every frame.
    /// </summary>
    public void WatchPreviews()
    {
        if (owner == null) return;
        if (Skin == 0 ? layerMode == SkinLayers.Off : worn == null) return;
        WatchPreviews(Unreal.ClassOf<UE.InventorySystem.ACharacterPreviewActor>());
        WatchPreviews(Unreal.ClassOf<UE.MainMenu.APartyPreviewActor>());
    }

    void WatchPreviews(TSubclassOf<AActor> previewClass)
    {
        foreach (var actor in World.FindAll(owner!, previewClass))
        {
            if (actor == null || actor.GetComponentByClass(Unreal.ClassOf<USkeletalMeshComponent>()) is not USkeletalMeshComponent body) continue;
            // Nothing changed since it was last looked at: the usual case, checked cheaply.
            var material = body.GetMaterial(0);
            if (material == null || (previewMaterials.ContainsKey(actor) && previewMaterials[actor] == material)) continue;
            if (Skin == 0)
            {
                // The game's skin: just its second layer, from the skin this copy wears (a cutscene's copy gets its skin
                // after it shows, and the main menu's party has other skins).
                previewMaterials[actor] = material;
                if (layers == null || UKismetSystemLibrary.GetPathName(body.GetSkinnedAsset()) != PlayerBody) continue;
                var skin = SkinOf(actor);
                if (skin == null || (layeredSkins.ContainsKey(actor) && layeredSkins[actor] == skin)) continue;
                layers.ClearOn(actor);
                Layers(actor, skin);
                layeredSkins[actor] = skin;
                continue;
            }
            if (material is UMaterialInstanceDynamic own && instances.Contains(own))
            {
                int at = instances.IndexOf(own);
                if (own.K2_GetTextureParameterValue(parameters[at]) != worn) own.SetTextureParameterValue(parameters[at], worn);
                // A copy wearing the character's own material (a cutscene's) still needs its second layer and face.
                if (!dressed.Contains(actor)) Dress(actor);
            }
            else if (UKismetSystemLibrary.GetPathName(body.GetSkinnedAsset()) == PlayerBody)
            {
                if (dressed.Contains(actor)) DressMaterials(actor);
                else
                {
                    Dress(actor);
                    if (!announced.Contains(actor))
                    {
                        announced.Add(actor);
                        FileLog.Write($"Dressed the menu's character {UKismetSystemLibrary.GetPathName(actor)} as it showed");
                    }
                }
            }
            var now = body.GetMaterial(0);
            if (now != null) previewMaterials[actor] = now;
        }
    }

    /// <summary>Puts the skin on an actor's skin materials, its second layer and its moving face.</summary>
    void Dress(AActor actor)
    {
        dressed.Add(actor);
        DressMaterials(actor);
        if (worn != null) Layers(actor, worn);
        // Eyes and mouth that move with the face, on the body, unless another mod gives it its own.
        if (face == null || !movingFaces.ContainsKey(Skin) || !movingFaces[Skin] || FaceParts.HasOthers(actor)) return;
        if (actor.GetComponentByClass(Unreal.ClassOf<USkeletalMeshComponent>()) is not USkeletalMeshComponent body) return;
        if (UKismetSystemLibrary.GetPathName(body.GetSkinnedAsset()) != PlayerBody) return;
        var skin = body.GetMaterial(0);
        if (skin != null) face.Build(actor, body, skin);
    }

    /// <summary>Puts the skin on an actor's skin materials (only the ones that don't have it).</summary>
    void DressMaterials(AActor actor)
    {
        dressingCopy = actor != character;
        // Armor and the cape are child actors: only the actor's own meshes (body, face) wear the skin.
        foreach (var component in actor.K2_GetComponentsByClass(Unreal.ClassOf<USkinnedMeshComponent>()))
        {
            if (component is not USkinnedMeshComponent mesh) continue;
            for (int i = 0; i < mesh.GetNumMaterials(); i++)
            {
                var material = mesh.GetMaterial(i);
                if (material == null || Changed(material)) continue;
                if (material is UMaterialInstanceDynamic own)
                {
                    // An instance made from a dynamic instance doesn't see its textures: change the game's own.
                    Wear(own);
                    continue;
                }
                var instance = UKismetMaterialLibrary.CreateDynamicMaterialInstance(owner, material, FName.None, EMIDCreationFlags.Transient);
                if (instance == null || !Wear(instance)) continue;
                mesh.SetMaterial(i, instance);
                components.Add(mesh);
                slots.Add(i);
                originals.Add(material);
                added.Add(instance);
            }
        }
    }

    /// <summary>Builds the second layer of a skin on an actor's body, in the body's skin material, if it's on.</summary>
    void Layers(AActor actor, UTexture skin)
    {
        if (layers == null || layerMode == SkinLayers.Off) return;
        if (actor.GetComponentByClass(Unreal.ClassOf<USkeletalMeshComponent>()) is not USkeletalMeshComponent body) return;
        if (UKismetSystemLibrary.GetPathName(body.GetSkinnedAsset()) != PlayerBody) return;
        var material = body.GetMaterial(0);
        var pixels = Readable(skin);
        if (material != null && pixels != null) layers.Build(owner!, actor, body, material, pixels, layerMode);
    }

    /// <summary>The game's skin's second layer on the character (only the hat shows unless it has more).</summary>
    void GameSkinLayers()
    {
        if (layerMode == SkinLayers.Off) return;
        var player = World.Player(owner!) as ACharacter;
        var skin = GameSkin();
        if (player == null || skin == null) return;
        layersOn = player;
        layersFrom = skin;
        Layers(player, skin);
    }

    /// <summary>A render target of a skin, to read its pixels from: the skin itself if it is one.</summary>
    UTextureRenderTarget2D? Readable(UTexture skin)
    {
        if (skin is UTextureRenderTarget2D target) return target;
        if (readable.ContainsKey(skin)) return readable[skin];
        if (skin is not UTexture2D texture) return null;
        var copy = Copy(texture, texture.Blueprint_GetSizeX(), texture.Blueprint_GetSizeY(), false);
        if (copy == null) return null;
        readable[skin] = copy;
        ours.Add(copy);
        return copy;
    }

    /// <summary>Puts the skin on a material if it's a skin material.</summary>
    bool Wear(UMaterialInstanceDynamic instance)
    {
        var parameter = SkinParameter(instance);
        if (parameter == FName.None) return false;
        instances.Add(instance);
        parameters.Add(parameter);
        // What to put back when the skin changes: the game's, never a skin of ours.
        var skin = instance.K2_GetTextureParameterValue(parameter);
        if (skin != null && ours.Contains(skin)) skin = gameSkin;
        else if (skin != null && (!dressingCopy || gameSkin == null)) gameSkin = skin;
        oldSkins.Add(skin);
        var mres = instance.K2_GetTextureParameterValue(MresParameter);
        var oldMresTexture = mres;
        if (mres != null && ours.Contains(mres)) oldMresTexture = gameMres;
        else if (mres != null && (!dressingCopy || gameMres == null)) gameMres = mres;
        oldMres.Add(oldMresTexture);
        instance.SetTextureParameterValue(parameter, worn);
        if (mres != null) instance.SetTextureParameterValue(MresParameter, Mres(Skin));
        return true;
    }

    /// <summary>Gives the character the game's skin back.</summary>
    void Restore()
    {
        face?.Clear();
        layers?.Clear();
        layersOn = null;
        layersFrom = null;
        for (int i = 0; i < instances.Count; i++)
        {
            if (!UKismetSystemLibrary.IsValid(instances[i]) || instances[i].K2_GetTextureParameterValue(parameters[i]) != worn) continue;
            if (oldSkins[i] != null) instances[i].SetTextureParameterValue(parameters[i], oldSkins[i]);
            if (oldMres[i] != null) instances[i].SetTextureParameterValue(MresParameter, oldMres[i]);
        }
        for (int i = 0; i < added.Count; i++)
            if (UKismetSystemLibrary.IsValid(components[i]) && components[i].GetMaterial(slots[i]) == added[i])
                components[i].SetMaterial(slots[i], originals[i]);
        dressed.Clear();
        previewMaterials.Clear();
        layeredSkins.Clear();
        instances.Clear();
        parameters.Clear();
        oldSkins.Clear();
        oldMres.Clear();
        components.Clear();
        slots.Clear();
        originals.Clear();
        added.Clear();
    }

    /// <summary>The material's texture parameter that holds a skin, or None.</summary>
    static FName SkinParameter(UMaterialInstanceDynamic material)
    {
        foreach (var name in UKismetStringLibrary.ParseIntoArray(ParameterNames, "|", true))
            if (IsSkin(material.K2_GetTextureParameterValue(name))) return name;
        return FName.None;
    }

    static bool IsSkin(UTexture? texture)
    {
        if (texture is not UTexture2D texture2D) return false;
        var size = texture2D.Blueprint_GetSizeX();
        return size > 0 && size <= MaxSkinSize && texture2D.Blueprint_GetSizeY() == size;
    }

    /// <summary>The texture to wear for a PNG: with its face on, or the PNG as it is if that fails. Null for a missing PNG.</summary>
    UTexture? Load(int number)
    {
        var file = File(number);
        if (file == null) return null;
        int width = file.Blueprint_GetSizeX();
        int height = file.Blueprint_GetSizeY();
        if (!withFaces.ContainsKey(number))
        {
            var created = NewTarget(width, height);
            if (created == null) return file;
            withFaces[number] = created;
            ours.Add(created);
        }
        // Drawn every time: a render target can lose what was drawn on it, and the face with it.
        var target = withFaces[number];
        float pixel = width / 64f;
        // A skin with eyes and mouth that move with the face (see FaceParts) has them, rather than the face block painted on.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            UKismetRenderingLibrary.ClearRenderTarget2D(owner, target, new FLinearColor());
            Draw(target, file, width, height, false);
            if (!Solid(target, (int)((HeadFrontX + FaceSize / 2) * pixel), (int)((HeadFrontY + FaceSize / 2) * pixel))) continue;
            movingFaces[number] = MovingFace(target, pixel);
            if (movingFaces[number]) return target;
            break;
        }
        // Drawn again if it didn't take. The head's front is never see-through: if the copy has it so, the drawing
        // was lost (or the copy lost its alpha, and the character would be invisible in it).
        for (int attempt = 0; attempt < 3; attempt++)
        {
            UKismetRenderingLibrary.ClearRenderTarget2D(owner, target, new FLinearColor());
            Draw(target, file, width, height, true);
            if (Solid(target, (int)((HeadFrontX + FaceSize / 2) * pixel), (int)((HeadFrontY + FaceSize / 2) * pixel)) && FaceOn(target, pixel))
                return target;
        }
        // The PNG as it is still works, without the face.
        FileLog.Write($"Couldn't put the face on {number}.png: wearing it as it is");
        return file;
    }

    /// <summary>Moves the eyes and mouth with the face, and hides the second layer under armor. Call every frame.</summary>
    public void UpdateFace()
    {
        face?.Update();
        if (owner != null) layers?.UpdateCover(owner);
    }

    /// <summary>Whether a skin colours any of the moving eyes' and mouth's shapes: their pupils, and the mouths.</summary>
    bool MovingFace(UTextureRenderTarget2D target, float pixel) =>
        Any(target, pixel, 24, 6) || Any(target, pixel, 30, 6) || Any(target, pixel, 30, 7) || Any(target, pixel, 24, 0)
        || Any(target, pixel, 24, 4) || Any(target, pixel, 24, 2) || Any(target, pixel, 24, 7) || Any(target, pixel, 26, 7);

    bool Any(UTextureRenderTarget2D target, float pixel, int x, int y) => Solid(target, (int)((x + 0.5f) * pixel), (int)((y + 0.5f) * pixel));

    UTexture2D? File(int number)
    {
        if (files.ContainsKey(number)) return files[number];
        var path = Folder() + number + ".png";
        if (!UBlueprintPathsLibrary.FileExists(path)) return null;
        var file = UKismetRenderingLibrary.ImportFileAsTexture2D(owner, path);
        if (file != null)
        {
            files[number] = file;
            ours.Add(file);
        }
        return file;
    }

    /// <summary>
    /// The texture drawn on a new render target. With withFace, the face block of a converted skin is drawn over the
    /// head's front: the game only draws its eyes and mouth for its own skins, and the converter keeps the skin's face
    /// with its eyes and mouth there. Drawn alpha-composited onto the see-through target, so each pixel keeps its alpha:
    /// translucent and masked drawing leave the target's alpha at 0, and the character invisible.
    /// </summary>
    UTextureRenderTarget2D? Copy(UTexture texture, int width, int height, bool withFace)
    {
        var target = NewTarget(width, height);
        if (target != null) Draw(target, texture, width, height, withFace);
        return target;
    }

    void Draw(UTextureRenderTarget2D target, UTexture texture, int width, int height, bool withFace)
    {
        UKismetRenderingLibrary.BeginDrawCanvasToRenderTarget(owner, target, out var canvas, out var size, out var context);
        if (canvas != null)
        {
            var white = new FLinearColor { R = 1, G = 1, B = 1, A = 1 };
            canvas.K2_DrawTexture(texture, new FVector2D(), new FVector2D { X = width, Y = height }, new FVector2D(),
                new FVector2D { X = 1, Y = 1 }, white, EBlendMode.BLEND_AlphaComposite, 0, new FVector2D());
            if (withFace)
            {
                // The layout is 64 pixels wide whatever the size: HD skins scale it.
                float pixel = width / 64f;
                canvas.K2_DrawTexture(texture, new FVector2D { X = HeadFrontX * pixel, Y = HeadFrontY * pixel },
                    new FVector2D { X = FaceSize * pixel, Y = FaceSize * pixel },
                    new FVector2D { X = FaceBlockX / 64f, Y = FaceBlockY / 64f }, new FVector2D { X = FaceSize / 64f, Y = FaceSize / 64f },
                    white, EBlendMode.BLEND_AlphaComposite, 0, new FVector2D());
            }
        }
        UKismetRenderingLibrary.EndDrawCanvasToRenderTarget(owner, context);
    }

    /// <summary>
    /// The front of a skin's head, face (moving eyes and mouth too) and second layer included, for the skin's button: 0 for the game's skin last seen
    /// on the character. Null for a missing PNG, or a game's skin not seen yet.
    /// </summary>
    public UTexture? Icon(int number)
    {
        var skin = number == 0 ? GameSkin() : Load(number);
        if (skin == null) return null;
        var target = NewTarget(FaceSize, FaceSize);
        if (target == null) return null;
        UKismetRenderingLibrary.BeginDrawCanvasToRenderTarget(owner, target, out var canvas, out var size, out var context);
        if (canvas != null)
        {
            canvas.K2_DrawTexture(skin, new FVector2D(), new FVector2D { X = FaceSize, Y = FaceSize },
                new FVector2D { X = HeadFrontX / 64f, Y = HeadFrontY / 64f }, new FVector2D { X = FaceSize / 64f, Y = FaceSize / 64f },
                new FLinearColor { R = 1, G = 1, B = 1, A = 1 }, EBlendMode.BLEND_AlphaComposite, 0, new FVector2D());
            // The game's skins keep their face, eyes and mouth painted on, in the face block (their eyes and mouth
            // that move are the game's own).
            if (number == 0)
                canvas.K2_DrawTexture(skin, new FVector2D(), new FVector2D { X = FaceSize, Y = FaceSize },
                    new FVector2D { X = FaceBlockX / 64f, Y = FaceBlockY / 64f }, new FVector2D { X = FaceSize / 64f, Y = FaceSize / 64f },
                    new FLinearColor { R = 1, G = 1, B = 1, A = 1 }, EBlendMode.BLEND_AlphaComposite, 0, new FVector2D());
            FaceShapes(canvas, skin);
            canvas.K2_DrawTexture(skin, new FVector2D(), new FVector2D { X = FaceSize, Y = FaceSize },
                new FVector2D { X = HatFrontX / 64f, Y = HatFrontY / 64f }, new FVector2D { X = FaceSize / 64f, Y = FaceSize / 64f },
                new FLinearColor { R = 1, G = 1, B = 1, A = 1 }, EBlendMode.BLEND_AlphaComposite, 0, new FVector2D());
        }
        UKismetRenderingLibrary.EndDrawCanvasToRenderTarget(owner, context);
        return Solid(target, FaceSize / 2, FaceSize / 2) ? target : null;
    }

    /// <summary>
    /// The eyes and mouth that move with the face (see FaceParts, whose shapes these are), drawn flat on the face: the
    /// game's skins and converted skins keep them apart from the head's front. A skin leaves the shapes it doesn't use
    /// see-through, and a skin with them painted on has none.
    /// </summary>
    static void FaceShapes(UCanvas canvas, UTexture skin)
    {
        FaceEye(canvas, skin, 1, 2, 26, 32, 24, 30);
        FaceEye(canvas, skin, 5, 5, 28, 34, 25, 31);
        FacePixel(canvas, skin, 3, 8, 24, 7);
        FacePixel(canvas, skin, 4, 8, 25, 7);
        for (int i = 0; i < 4; i++) FacePixel(canvas, skin, 2 + i, 7, 26 + i, 7);
    }

    /// <summary>An eye in every shape, whites then pupil (FaceParts.Eyes).</summary>
    static void FaceEye(UCanvas canvas, UTexture skin, int column, int pupilColumn, int u, int u2, int pupilU, int pupilU2)
    {
        FaceShape(canvas, skin, column, 2, u, 6, 5, 5);
        FaceShape(canvas, skin, column, 2, u2, 6, 6, 6);
        FaceShape(canvas, skin, column, 2, u2, 7, 7, 7);
        FaceShape(canvas, skin, column, 2, u, 0, 4, 5);
        FaceShape(canvas, skin, column, 2, u, 4, 5, 6);
        FaceShape(canvas, skin, column, 2, u, 2, 6, 7);
        FaceShape(canvas, skin, pupilColumn, 1, pupilU, 6, 5, 5);
        FaceShape(canvas, skin, pupilColumn, 1, pupilU2, 6, 6, 6);
        FaceShape(canvas, skin, pupilColumn, 1, pupilU2, 7, 7, 7);
        FaceShape(canvas, skin, pupilColumn, 1, pupilU, 0, 4, 5);
        FaceShape(canvas, skin, pupilColumn, 1, pupilU, 4, 5, 6);
        FaceShape(canvas, skin, pupilColumn, 1, pupilU, 2, 6, 7);
    }

    /// <summary>A block of face pixels, some columns wide from rows first to last (1-8), from the skin from (u, v).</summary>
    static void FaceShape(UCanvas canvas, UTexture skin, int column, int width, int u, int v, int first, int last)
    {
        for (int row = first; row <= last; row++)
            for (int i = 0; i < width; i++) FacePixel(canvas, skin, column + i, row, u + i, v + row - first);
    }

    /// <summary>A face pixel at a column (0-7) and row (1-8), from one skin pixel.</summary>
    static void FacePixel(UCanvas canvas, UTexture skin, int column, int row, int u, int v) =>
        canvas.K2_DrawTexture(skin, new FVector2D { X = column, Y = row - 1 }, new FVector2D { X = 1, Y = 1 },
            new FVector2D { X = u / 64f, Y = v / 64f }, new FVector2D { X = 1 / 64f, Y = 1 / 64f },
            new FLinearColor { R = 1, G = 1, B = 1, A = 1 }, EBlendMode.BLEND_AlphaComposite, 0, new FVector2D());

    /// <summary>
    /// Whether the face block is on the head's front: some of their pixels compared (eyes, mouth). A skin without a face
    /// block has nothing to put on.
    /// </summary>
    bool FaceOn(UTextureRenderTarget2D target, float pixel) =>
        Same(target, pixel, 2, 4) && Same(target, pixel, 5, 5) && Same(target, pixel, 3, 7) && Same(target, pixel, 1, 2);

    /// <summary>Whether a pixel of the head's front is the face block's, or the face block has nothing there.</summary>
    bool Same(UTextureRenderTarget2D target, float pixel, int x, int y)
    {
        var face = UKismetRenderingLibrary.ReadRenderTargetPixel(owner, target, (int)((FaceBlockX + x) * pixel), (int)((FaceBlockY + y) * pixel));
        if (face.A == 0) return true;
        var head = UKismetRenderingLibrary.ReadRenderTargetPixel(owner, target, (int)((HeadFrontX + x) * pixel), (int)((HeadFrontY + y) * pixel));
        return head.R == face.R && head.G == face.G && head.B == face.B;
    }

    /// <summary>Whether a pixel of a render target is solid (reading it waits for the drawing to finish).</summary>
    bool Solid(UTextureRenderTarget2D target, int x, int y) => UKismetRenderingLibrary.ReadRenderTargetPixel(owner, target, x, y).A > 0;

    /// <summary>
    /// An empty render target: sRGB, so colours stay as stored, see-through, and drawn with sharp pixels. The filter
    /// only takes effect when the target is made, so it's made small and then resized.
    /// </summary>
    UTextureRenderTarget2D? NewTarget(int width, int height)
    {
        var target = UKismetRenderingLibrary.CreateRenderTarget2D(owner, 1, 1, ETextureRenderTargetFormat.RTF_RGBA8_SRGB,
            new FLinearColor(), false, false);
        if (target == null) return null;
        target.Filter = TextureFilter.TF_Nearest;
        UKismetRenderingLibrary.ResizeRenderTarget2D(target, width, height);
        // The resize happens on the render thread: reading a pixel waits for it, so the first drawing isn't lost.
        UKismetRenderingLibrary.ReadRenderTargetPixel(owner, target, 0, 0);
        return target;
    }

    /// <summary>
    /// Saves the skin picked in the game's menu to the Skins\_game folder as PNGs, its MRES too: to start a skin from,
    /// or to see which pixels the game uses.
    /// </summary>
    public void ExportGameSkin()
    {
        if (owner == null) return;
        var player = World.Player(owner) as ACharacter;
        if (player == null || player.Mesh == null || player.Mesh.GetMaterial(0) is not UMaterialInstanceDynamic body)
        {
            FileLog.Write("No character to save the skin of");
            return;
        }
        var skin = body.K2_GetTextureParameterValue(GameParameter);
        var mres = body.K2_GetTextureParameterValue(MresParameter);
        // While a custom skin is worn, the game's is the one it replaced.
        for (int i = 0; i < instances.Count; i++)
            if (instances[i] == body)
            {
                skin = oldSkins[i];
                mres = oldMres[i];
            }
        if (skin == null)
        {
            FileLog.Write("The character's material has no skin texture");
            return;
        }
        var name = UKismetSystemLibrary.GetObjectName(skin);
        Export(skin, name);
        if (mres != null) Export(mres, name + "_MRES");
        FileLog.Write($"Saved {name}.png to {Folder()}_game/");
        // And the custom skin as worn, face included, to check what the character really has on.
        if (Skin != 0 && worn != null)
        {
            Export(worn, $"worn_{Skin}");
            FileLog.Write($"Saved worn_{Skin}.png ({UKismetSystemLibrary.GetObjectName(worn)}) to {Folder()}_game/");
        }
    }

    void Export(UTexture texture, string name)
    {
        int width = 64;
        int height = 64;
        if (texture is UTexture2D texture2D)
        {
            width = texture2D.Blueprint_GetSizeX();
            height = texture2D.Blueprint_GetSizeY();
        }
        var target = Copy(texture, width, height, false);
        if (target != null) UKismetRenderingLibrary.ExportRenderTarget(owner, target, Folder() + "_game/", name + ".png");
    }

    /// <summary>
    /// The skin the character has from the game's menu: the one it would wear without a custom skin, which changes when the
    /// game's menu picks another. Else the one last seen, or null.
    /// </summary>
    public UTexture? GameSkin()
    {
        var player = owner != null ? World.Player(owner) : null;
        if (player == null) return gameSkin;
        var skin = SkinOf(player);
        if (skin != null) gameSkin = skin;
        return gameSkin;
    }

    /// <summary>The game's skin an actor with the player's body wears (the one a custom skin replaced), or null.</summary>
    UTexture? SkinOf(AActor actor)
    {
        foreach (var component in actor.K2_GetComponentsByClass(Unreal.ClassOf<USkinnedMeshComponent>()))
        {
            if (component is not USkinnedMeshComponent mesh || UKismetSystemLibrary.GetPathName(mesh.GetSkinnedAsset()) != PlayerBody) continue;
            for (int i = 0; i < mesh.GetNumMaterials(); i++)
            {
                var material = mesh.GetMaterial(i);
                if (material == null) continue;
                UTexture? skin = null;
                if (material is UMaterialInstanceDynamic dynamic)
                {
                    // Under a custom skin: the game's is the one it replaced.
                    int at = instances.IndexOf(dynamic);
                    if (at >= 0) skin = oldSkins[at];
                    else
                    {
                        var parameter = SkinParameter(dynamic);
                        if (parameter != FName.None) skin = dynamic.K2_GetTextureParameterValue(parameter);
                    }
                }
                else if (materialSkins.ContainsKey(material)) skin = materialSkins[material];
                else
                {
                    var reader = UKismetMaterialLibrary.CreateDynamicMaterialInstance(owner, material, FName.None, EMIDCreationFlags.Transient);
                    var parameter = reader != null ? SkinParameter(reader) : FName.None;
                    if (reader != null && parameter != FName.None) skin = reader.K2_GetTextureParameterValue(parameter);
                    if (skin != null) materialSkins[material] = skin;
                }
                if (skin == null || ours.Contains(skin)) continue;
                return skin;
            }
        }
        return null;
    }

    /// <summary>
    /// The skin's &lt;number&gt;_MRES.png, or a plain MRES. (Not the game's skin's: it lights a custom skin badly.)
    /// </summary>
    UTexture? Mres(int number)
    {
        if (mresTextures.ContainsKey(number)) return mresTextures[number];
        var path = Folder() + number + "_MRES.png";
        if (UBlueprintPathsLibrary.FileExists(path))
        {
            var texture = UKismetRenderingLibrary.ImportFileAsTexture2D(owner, path);
            if (texture != null)
            {
                mresTextures[number] = texture;
                ours.Add(texture);
                return texture;
            }
        }
        if (defaultMres == null)
        {
            defaultMres = UKismetRenderingLibrary.CreateRenderTarget2D(owner, 4, 4, ETextureRenderTargetFormat.RTF_RGBA8,
                new FLinearColor { R = 0, G = DefaultRoughness, B = 0, A = 0 }, false, false);
            if (defaultMres != null) ours.Add(defaultMres);
        }
        return defaultMres;
    }

    /// <summary>The numbers of the PNGs in the Skins folder (1.png to 20.png), in order.</summary>
    public static List<int> Available()
    {
        var numbers = new List<int>();
        var folder = Folder();
        for (int i = 1; i <= MaxSkins; i++)
            if (UBlueprintPathsLibrary.FileExists(folder + i + ".png")) numbers.Add(i);
        return numbers;
    }

    /// <summary>The Skins folder, with a slash at the end.</summary>
    public static string Folder() =>
        UBlueprintPathsLibrary.ConvertRelativePathToFull(UBlueprintPathsLibrary.ProjectContentDir() + "Paks/~mods/CustomSkins/Skins/", "");

    /// <summary>
    /// Logs what the next steps need to know about the character: its meshes and materials with their skin textures,
    /// its bones in the reference pose (for second layers) and its cosmetics slots (for capes).
    /// </summary>
    public void LogInfo()
    {
        if (owner == null) return;
        info.Clear();
        info.Add($"--- Custom Skins info, level {World.LevelName(owner)}");
        info.Add($"Skins folder: {Folder()} ({Available().Count} skins), wearing {(Skin == 0 ? "the game's skin" : Skin + ".png")}");
        var player = World.Player(owner) as ACharacter;
        if (player == null)
        {
            info.Add("No character");
            FileLog.WriteAll(info);
            return;
        }
        info.Add($"Character: {UKismetSystemLibrary.GetPathName(player)}");
        LogMeshes(player, "");
        foreach (var component in player.K2_GetComponentsByClass(Unreal.ClassOf<UChildActorComponent>()))
            if (component is UChildActorComponent child && child.ChildActor != null)
            {
                info.Add($"Child actor {UKismetSystemLibrary.GetObjectName(child)}: {UKismetSystemLibrary.GetPathName(child.ChildActor)}");
                LogMeshes(child.ChildActor, "  ");
            }
        player.GetAttachedActors(out var attached, true, false);
        foreach (var actor in attached)
        {
            if (actor == null) continue;
            info.Add($"Attached actor {UKismetSystemLibrary.GetPathName(actor)}");
            LogMeshes(actor, "  ");
        }

        var paperdoll = player.GetComponentByClass(Unreal.ClassOf<USWPaperdollComponent>()) as USWPaperdollComponent;
        if (paperdoll == null) info.Add("No paperdoll on the character");
        else foreach (var entry in paperdoll.SlotMap)
            info.Add($"Cosmetic slot {entry.Key.TagName} = {entry.Value.TypeTag.TagName}");

        FileLog.WriteAll(info);
    }

    void LogMeshes(AActor actor, string indent)
    {
        foreach (var component in actor.K2_GetComponentsByClass(Unreal.ClassOf<UMeshComponent>()))
        {
            // Hidden ones are gear not worn.
            if (component is not UMeshComponent mesh || !mesh.IsVisible()) continue;
            var asset = "";
            if (mesh is USkinnedMeshComponent skinned) asset = UKismetSystemLibrary.GetPathName(skinned.GetSkinnedAsset());
            if (mesh is UStaticMeshComponent staticMesh) asset = UKismetSystemLibrary.GetPathName(staticMesh.StaticMesh);
            info.Add($"{indent}Mesh {UKismetSystemLibrary.GetObjectName(mesh)} ({UKismetSystemLibrary.GetClassDisplayName(UGameplayStatics.GetObjectClass(mesh))}) {asset}, on {UKismetSystemLibrary.GetObjectName(mesh.GetAttachParent())} {mesh.GetAttachSocketName()}");
            var overlay = mesh.GetOverlayMaterial();
            if (overlay != null) info.Add($"{indent}  overlay: {UKismetSystemLibrary.GetPathName(overlay)}");
            var slotNames = mesh.GetMaterialSlotNames();
            for (int i = 0; i < mesh.GetNumMaterials(); i++)
            {
                var material = mesh.GetMaterial(i);
                var slot = i < slotNames.Count ? slotNames[i].ToString() : "";
                info.Add($"{indent}  [{i}] {slot}: {UKismetSystemLibrary.GetPathName(material)}");
                if (material == null) continue;
                // An instance made from a dynamic instance doesn't see its textures: read those from it.
                var probe = material as UMaterialInstanceDynamic;
                if (probe == null) probe = UKismetMaterialLibrary.CreateDynamicMaterialInstance(actor, material, FName.None, EMIDCreationFlags.Transient);
                if (probe == null) continue;
                foreach (var name in UKismetStringLibrary.ParseIntoArray(ParameterNames + "|" + MresParameter, "|", true))
                {
                    var texture = probe.K2_GetTextureParameterValue(name);
                    if (texture == null) continue;
                    var size = texture is UTexture2D texture2D ? $"{texture2D.Blueprint_GetSizeX()}x{texture2D.Blueprint_GetSizeY()}" : "?";
                    info.Add($"{indent}      {name} = {UKismetSystemLibrary.GetPathName(texture)} ({size})");
                }
            }
        }
    }
}
