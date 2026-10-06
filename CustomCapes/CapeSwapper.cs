using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.UMG;

namespace CustomCapes;

/// <summary>Remembers the picked cape between levels and game sessions: SaveGames/CustomCapes.sav.</summary>
public class CustomCapesSettings : USaveGame
{
    // 0 is the game's own cape, otherwise the number of the PNG in the Capes folder.
    public int Cape;
    // Whether capes take lighting properly (linear render target) instead of being washed out.
    public bool CharacterLighting;
    public string GameCapePath;
    public string GameMresPath;
}

/// <summary>
/// Puts a cape PNG on the player's character, in place of the texture of the cape the game put on it, or on a cape of
/// the mod's own when it wears none. Only this game sees it: the server and other players still see the cape picked in
/// the game's menu.
///
/// The game's cape textures are 32x16. Columns 0-21 are a Java cape's rows 1-16 (left edge, outside, right edge,
/// inside), and rows 0-9 of columns 22 and 23 are its bottom and top edges, stood up. Java capes (64x32, or just the
/// 22x17 cape, and HD sizes of either) are moved into that layout; 32x16 PNGs are taken as the game's layout already.
/// </summary>
public class CapeSwapper : UObject
{
    const string SettingsSlot = "CustomCapes";
    const int MaxCapes = 20;
    // The mesh of every one of the game's capes, and the body of the player's character and of its copies in menus.
    const string CapeMesh = "/Game/Spicewood/Art/Characters/Player/Capes/SK_Cape.SK_Cape";
    const string PlayerBody = "/Game/Spicewood/Art/Characters/Player/Master/SK_Player_Master.SK_Player_Master";
    // The inventory's picture of the character.
    const string PreviewWidget = "CharacterRender";
    // Where and how the game's capes sit on the body.
    const string CapeSocket = "J_BackBlingSocket";
    const float CapeYaw = 180;
    const float CapeRoll = -110;
    // The cape material's texture, and its texture of how each pixel takes the light: red metallic, green roughness,
    // blue glow, alpha subsurface. A cape gets its own <number>_MRES.png, or else a plain one.
    const string TextureParameter = "BaseColour";
    const string MresParameter = "MRES";
    // The game's capes are about this rough, with no metal or glow.
    const float DefaultRoughness = 0.9f;
    // The game's layout, and in it the cape's outside (shown on its button) and the edges' columns.
    const int GameWidth = 32;
    const int GameHeight = 16;
    const int OutsideLeft = 1;
    const int OutsideWidth = 10;
    const int BottomColumn = 22;
    const int TopColumn = 23;
    // A Java cape: 64x32 with the cape in its top left 22x17, or a PNG of just those 22x17.
    const int JavaWidth = 64;
    const int JavaCapeWidth = 22;
    const int JavaCapeHeight = 17;

    /// <summary>The cape's outside in a worn texture, for its button: left and right, as a fraction of its width.</summary>
    public const float OutsideStart = (float)OutsideLeft / GameWidth;
    public const float OutsideEnd = (float)(OutsideLeft + OutsideWidth) / GameWidth;

    UObject? owner;
    CustomCapesSettings? settings;
    // The PNGs as read (by name: "3", "3_MRES"), and per cape the textures worn, in the game's layout.
    Dictionary<string, UTexture2D> files = new();
    Dictionary<int, UTextureRenderTarget2D> capes = new();
    Dictionary<int, UTextureRenderTarget2D> mresTextures = new();
    UTexture? defaultMres;
    // Every texture this mod made, to tell them from the game's: a copy of the character the game makes while a custom
    // cape is on starts with it, and putting that one back when the cape changes would bring the last cape back.
    List<UTexture> ours = new();
    // The game's cape as found, put back on copies that only ever had a custom one.
    UTexture? gameCape;
    UTexture? gameMres;

    ACharacter? character;
    UTexture? worn;
    // Set when the picked PNG couldn't be read, so Check doesn't try (and log) again every second.
    bool missing;
    // The menu characters already logged: the game puts its cape back on them often, and they get it again.
    List<AActor> announced = new();
    // The cape materials changed, with what they had before: the game's own material instances are changed in place,
    // other materials get an instance of ours.
    List<UMaterialInstanceDynamic> instances = new();
    List<UTexture?> oldCapes = new();
    List<UTexture?> oldMres = new();
    // The material slots given an instance of ours, with the material they had.
    List<UMeshComponent> components = new();
    List<int> slots = new();
    List<UMaterialInterface> originals = new();
    List<UMaterialInstanceDynamic> added = new();
    // Capes of the mod's own, on characters that wear none of the game's, and the ones kept when some go.
    List<USkeletalMeshComponent> ownCapes = new();
    List<USkeletalMeshComponent> kept = new();
    USkeletalMesh? capeMesh;

    public static CapeSwapper? Create(UObject owner)
    {
        var swapper = UGameplayStatics.SpawnObject(Unreal.ClassOf<CapeSwapper>(), owner) as CapeSwapper;
        if (swapper == null) return null;
        swapper.owner = owner;
        swapper.settings = UGameplayStatics.LoadGameFromSlot(SettingsSlot, 0) as CustomCapesSettings;
        if (swapper.settings == null)
            swapper.settings = UGameplayStatics.CreateSaveGameObject(Unreal.ClassOf<CustomCapesSettings>()) as CustomCapesSettings;
        return swapper;
    }

    /// <summary>The cape worn: 0 for the game's, otherwise the number of its PNG.</summary>
    public int Cape => settings != null ? settings.Cape : 0;

    /// <summary>Wears the next PNG in the Capes folder, then the game's cape again after the last one.</summary>
    public void Next()
    {
        int next = 0;
        foreach (var number in Available())
            if (number > Cape)
            {
                next = number;
                break;
            }
        Select(next);
    }

    /// <summary>Wears a cape: 0 for the game's, otherwise the number of its PNG. Remembered for next time.</summary>
    public void Select(int number)
    {
        if (settings == null) return;
        settings.Cape = number;
        UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
        Apply();
        if (!missing) Log.Write(number == 0 ? "Wearing the game's cape" : $"Wearing {number}.png ({Available().Count} capes in {Folder()})");
    }

    public void SetCharacterLighting(bool lit)
    {
        if (settings == null || settings.CharacterLighting == lit) return;
        settings.CharacterLighting = lit;
        UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
        // We must drop all cached targets since their format is wrong now
        capes.Clear();
        mresTextures.Clear();
        defaultMres = null;
        ours.Clear();
        Apply();
    }

    /// <summary>Reads the PNGs again, to see changes made to them while playing.</summary>
    public void Reload()
    {
        files.Clear();
        Apply();
    }

    /// <summary>Call regularly: puts the cape back on a new character, or after the game changed or put back its own.</summary>
    public void Check()
    {
        if (owner == null) return;
        var current = World.Player(owner) as ACharacter;
        bool shouldApply = current != character || 
                           (worn == null && !missing && Cape != 0) || 
                           (worn == null && Cape == 0 && settings != null && !string.IsNullOrEmpty(settings.GameCapePath));
        if (shouldApply)
        {
            Apply();
            return;
        }
        if (worn == null) return;
        if (current != null && Cape != 0) Dress(current);
        // The main menu has no character of the player's, only the one by the campfire.
        if ((current == null || PreviewShown()) && worn != null) DressPreviews();
    }

    void Apply()
    {
        Restore();
        missing = false;
        if (owner == null) return;
        character = World.Player(owner) as ACharacter;
        if (Cape == 0)
        {
            if (settings != null && gameCape == null && !string.IsNullOrEmpty(settings.GameCapePath))
                gameCape = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(settings.GameCapePath))) as UTexture;
            if (settings != null && gameMres == null && !string.IsNullOrEmpty(settings.GameMresPath))
                gameMres = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(settings.GameMresPath))) as UTexture;
            worn = gameCape;
        }
        else worn = Load(Cape);

        if (worn == null)
        {
            if (Cape != 0)
            {
                missing = true;
                Log.Write($"Couldn't read {Cape}.png in {Folder()}: a cape is 64x32 or 22x17 (Java), or 32x16 (the game's layout)");
            }
            return;
        }
        if (character != null && Cape != 0) Dress(character);
        if (character == null || PreviewShown()) DressPreviews();
    }

    /// <summary>
    /// Whether a menu shows the character: the inventory draws a copy of it, which wears its own copy of the cape.
    /// </summary>
    bool PreviewShown()
    {
        UWidgetBlueprintLibrary.GetAllWidgetsOfClass(owner, out var widgets, Unreal.ClassOf<UUserWidget>(), false);
        foreach (var widget in widgets)
            if (widget != null && UKismetSystemLibrary.GetObjectName(widget) == PreviewWidget && widget.IsVisible()) return true;
        return false;
    }

    /// <summary>
    /// Puts the cape on the copies of the character in menus (the inventory's picture, the main menu's campfire): actors
    /// with the player's body that aren't players.
    /// </summary>
    void DressPreviews()
    {
        foreach (var actor in World.FindAll(owner!, Unreal.ClassOf<AActor>()))
        {
            if (actor == null || actor == character) continue;
            // Other players: they keep their own capes.
            if (actor is APawn pawn && pawn.PlayerState != null) continue;
            if (Body(actor) == null) continue;
            Dress(actor);
            if (announced.Contains(actor)) continue;
            announced.Add(actor);
            Log.Write($"Dressed the menu's character {UKismetSystemLibrary.GetPathName(actor)}");
        }
    }

    /// <summary>
    /// Puts the cape on an actor: on the game's capes it wears, or else on a cape of the mod's own. Safe to call again:
    /// it only changes what the game changed back since.
    /// </summary>
    void Dress(AActor actor)
    {
        bool wearsOne = false;
        foreach (var component in actor.K2_GetComponentsByClass(Unreal.ClassOf<USkeletalMeshComponent>()))
        {
            if (component is not USkeletalMeshComponent mesh || ownCapes.Contains(mesh)) continue;
            // Hidden ones are gear not worn.
            if (!mesh.IsVisible() || UKismetSystemLibrary.GetPathName(mesh.GetSkinnedAsset()) != CapeMesh) continue;
            wearsOne = true;
            for (int i = 0; i < mesh.GetNumMaterials(); i++) Cover(mesh, i);
        }
        if (wearsOne) RemoveOwnCape(actor);
        else if (OwnCape(actor) == null) AddOwnCape(actor);
    }

    /// <summary>Puts the cape on a material slot of a cape, unless it has it already.</summary>
    void Cover(UMeshComponent mesh, int slot)
    {
        var material = mesh.GetMaterial(slot);
        if (material == null) return;
        if (material is UMaterialInstanceDynamic own)
        {
            if (!instances.Contains(own)) Wear(own);
            // The game put its texture back on its own instance.
            else if (own.K2_GetTextureParameterValue(TextureParameter) != worn) Rewear(own);
            return;
        }
        var instance = UKismetMaterialLibrary.CreateDynamicMaterialInstance(owner, material, FName.None, EMIDCreationFlags.Transient);
        if (instance == null || !Wear(instance)) return;
        mesh.SetMaterial(slot, instance);
        components.Add(mesh);
        slots.Add(slot);
        originals.Add(material);
        added.Add(instance);
    }

    /// <summary>Puts the cape on a material if it's a cape material.</summary>
    bool Wear(UMaterialInstanceDynamic instance)
    {
        var cape = instance.K2_GetTextureParameterValue(TextureParameter);
        if (cape == null) return false;
        instances.Add(instance);
        // What to put back when the cape changes: the game's, never a cape of ours.
        if (ours.Contains(cape)) cape = gameCape;
        else 
        {
            gameCape = cape;
            if (cape != null)
            {
                var path = UKismetSystemLibrary.GetPathName(cape);
                if (settings != null && settings.GameCapePath != path)
                {
                    settings.GameCapePath = path;
                    UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
                }
            }
        }
        oldCapes.Add(cape);
        var mres = instance.K2_GetTextureParameterValue(MresParameter);
        if (mres != null && ours.Contains(mres)) mres = gameMres;
        else if (mres != null) 
        {
            gameMres = mres;
            var path = UKismetSystemLibrary.GetPathName(mres);
            if (settings != null && settings.GameMresPath != path)
            {
                settings.GameMresPath = path;
                UGameplayStatics.SaveGameToSlot(settings, SettingsSlot, 0);
            }
        }
        oldMres.Add(mres);
        Rewear(instance);
        return true;
    }

    void Rewear(UMaterialInstanceDynamic instance)
    {
        instance.SetTextureParameterValue(TextureParameter, worn);
        if (instance.K2_GetTextureParameterValue(MresParameter) != null) 
        {
            var mres = (Cape == 0 && File("0_MRES") == null) ? gameMres : Mres(Cape);
            instance.SetTextureParameterValue(MresParameter, mres);
        }
    }

    /// <summary>
    /// Gives an actor a cape of the mod's own: the game's cape mesh where the game puts its capes. It hangs as modelled,
    /// without the game's capes' swing.
    /// </summary>
    void AddOwnCape(AActor actor)
    {
        var body = Body(actor);
        if (body == null || !body.DoesSocketExist(CapeSocket)) return;
        if (capeMesh == null)
            capeMesh = UKismetSystemLibrary.LoadAsset_Blocking(UKismetSystemLibrary.Conv_SoftObjPathToSoftObjRef(UKismetSystemLibrary.MakeSoftObjectPath(CapeMesh))) as USkeletalMesh;
        if (capeMesh == null) return;
        var identity = new FTransform { Rotation = UKismetMathLibrary.Quat_Identity(), Scale3D = new FVector { X = 1, Y = 1, Z = 1 } };
        var cape = actor.AddComponentByClass(Unreal.ClassOf<USkeletalMeshComponent>(), true, identity, false) as USkeletalMeshComponent;
        if (cape == null) return;
        ownCapes.Add(cape);
        cape.SetSkeletalMeshAsset(capeMesh);
        cape.K2_AttachToComponent(body, CapeSocket, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, false);
        cape.K2_SetRelativeRotation(UKismetMathLibrary.MakeRotator(CapeRoll, 0, CapeYaw), false, out var hit, false);
        cape.SetCollisionEnabled(ECollisionEnabled.NoCollision);
        // Lit like the body: the menus light their copies of the character on a lighting channel of their own.
        var lighting = body.LightingChannels;
        cape.SetLightingChannels(lighting.bChannel0, lighting.bChannel1, lighting.bChannel2);
        for (int i = 0; i < cape.GetNumMaterials(); i++) Cover(cape, i);
    }

    USkeletalMeshComponent? OwnCape(AActor actor)
    {
        foreach (var cape in ownCapes)
            if (cape != null && UKismetSystemLibrary.IsValid(cape) && cape.GetOwner() == actor) return cape;
        return null;
    }

    /// <summary>Takes the mod's own capes off an actor (all of them, with no actor), dropping any already gone.</summary>
    void RemoveOwnCape(AActor? actor)
    {
        if (ownCapes.Count == 0) return;
        kept.Clear();
        foreach (var cape in ownCapes)
        {
            if (cape == null || !UKismetSystemLibrary.IsValid(cape)) continue;
            if (actor == null || cape.GetOwner() == actor) cape.K2_DestroyComponent(cape);
            else kept.Add(cape);
        }
        ownCapes.Clear();
        foreach (var cape in kept) ownCapes.Add(cape);
        kept.Clear();
    }

    /// <summary>The player's body on an actor, or null.</summary>
    static USkeletalMeshComponent? Body(AActor actor)
    {
        foreach (var component in actor.K2_GetComponentsByClass(Unreal.ClassOf<USkeletalMeshComponent>()))
            if (component is USkeletalMeshComponent mesh && UKismetSystemLibrary.GetPathName(mesh.GetSkinnedAsset()) == PlayerBody) return mesh;
        return null;
    }

    /// <summary>Gives the character the game's cape back.</summary>
    void Restore()
    {
        for (int i = 0; i < instances.Count; i++)
        {
            if (!UKismetSystemLibrary.IsValid(instances[i]) || instances[i].K2_GetTextureParameterValue(TextureParameter) != worn) continue;
            if (oldCapes[i] != null) instances[i].SetTextureParameterValue(TextureParameter, oldCapes[i]);
            if (oldMres[i] != null) instances[i].SetTextureParameterValue(MresParameter, oldMres[i]);
        }
        for (int i = 0; i < added.Count; i++)
            if (UKismetSystemLibrary.IsValid(components[i]) && components[i].GetMaterial(slots[i]) == added[i])
                components[i].SetMaterial(slots[i], originals[i]);
        RemoveOwnCape(null);
        worn = null;
        instances.Clear();
        oldCapes.Clear();
        oldMres.Clear();
        components.Clear();
        slots.Clear();
        originals.Clear();
        added.Clear();
    }

    /// <summary>The texture to wear for a cape PNG, in the game's layout: null for a missing PNG or one of another size.</summary>
    UTexture? Load(int number)
    {
        var file = File(number.ToString());
        if (file == null) return null;
        var target = capes.ContainsKey(number) ? capes[number] : null;
        target = Convert(file, target);
        if (target != null) capes[number] = target;
        return target;
    }

    /// <summary>The cape's texture, for its button (its outside is from OutsideStart to OutsideEnd): null for a missing PNG.</summary>
    public UTexture? Icon(int number) => Load(number);

    /// <summary>
    /// A cape PNG drawn in the game's layout, on the render target given if it's the right size, or on a new one. Null
    /// for a PNG that isn't a cape's size.
    /// </summary>
    UTextureRenderTarget2D? Convert(UTexture2D file, UTextureRenderTarget2D? target)
    {
        int width = file.Blueprint_GetSizeX();
        int height = file.Blueprint_GetSizeY();
        // The PNG's layout, and how many of its pixels make one of the layout's.
        bool java = true;
        float layoutWidth = JavaWidth;
        float layoutHeight = JavaWidth / 2;
        float unit = 1;
        if (width == GameWidth && height == GameHeight)
        {
            java = false;
            layoutWidth = GameWidth;
            layoutHeight = GameHeight;
        }
        else if (width == height * 2 && width % JavaWidth == 0) unit = width / JavaWidth;
        else if (width * JavaCapeHeight == height * JavaCapeWidth && width % JavaCapeWidth == 0)
        {
            layoutWidth = JavaCapeWidth;
            layoutHeight = JavaCapeHeight;
            unit = width / JavaCapeWidth;
        }
        else return null;

        int targetWidth = (int)(GameWidth * unit);
        int targetHeight = (int)(GameHeight * unit);
        if (target == null || target.SizeX != targetWidth || target.SizeY != targetHeight)
        {
            target = NewTarget(targetWidth, targetHeight);
            if (target == null) return null;
            ours.Add(target);
        }
        // Drawn every time: a render target can lose what was drawn on it. Drawn again if it didn't take: the middle of
        // the outside is solid on every cape.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            UKismetRenderingLibrary.ClearRenderTarget2D(owner, target, new FLinearColor());
            Draw(target, file, java, unit, layoutWidth, layoutHeight);
            if (Solid(target, (int)((OutsideLeft + OutsideWidth / 2) * unit), (int)(GameHeight / 2 * unit))) break;
        }
        return target;
    }

    /// <summary>
    /// Draws a cape PNG on a render target in the game's layout. Drawn alpha-composited onto the see-through target, so
    /// each pixel keeps its alpha.
    /// </summary>
    void Draw(UTextureRenderTarget2D target, UTexture2D file, bool java, float unit, float layoutWidth, float layoutHeight)
    {
        UKismetRenderingLibrary.BeginDrawCanvasToRenderTarget(owner, target, out var canvas, out var size, out var context);
        if (canvas != null)
        {
            if (!java) Block(canvas, file, 0, 0, GameWidth, GameHeight, 0, 0, unit, layoutWidth, layoutHeight);
            else
            {
                // Rows 1-16: left edge, outside, right edge, inside.
                Block(canvas, file, 0, 1, JavaCapeWidth, GameHeight, 0, 0, unit, layoutWidth, layoutHeight);
                // Row 0 has the top edge over the outside and the bottom edge after it, a pixel each per column of the
                // outside: stood up, one under the other.
                for (int i = 0; i < OutsideWidth; i++)
                {
                    Block(canvas, file, OutsideLeft + i, 0, 1, 1, TopColumn, i, unit, layoutWidth, layoutHeight);
                    Block(canvas, file, OutsideLeft + OutsideWidth + i, 0, 1, 1, BottomColumn, i, unit, layoutWidth, layoutHeight);
                }
            }
        }
        UKismetRenderingLibrary.EndDrawCanvasToRenderTarget(owner, context);
    }

    /// <summary>Draws a block of a PNG (in its layout's pixels) at a place in the game's layout.</summary>
    static void Block(UCanvas canvas, UTexture file, int x, int y, int width, int height, int toX, int toY, float unit,
        float layoutWidth, float layoutHeight)
    {
        canvas.K2_DrawTexture(file, new FVector2D { X = toX * unit, Y = toY * unit }, new FVector2D { X = width * unit, Y = height * unit },
            new FVector2D { X = x / layoutWidth, Y = y / layoutHeight }, new FVector2D { X = width / layoutWidth, Y = height / layoutHeight },
            new FLinearColor { R = 1, G = 1, B = 1, A = 1 }, EBlendMode.BLEND_AlphaComposite, 0, new FVector2D());
    }

    /// <summary>Whether a pixel of a render target is solid (reading it waits for the drawing to finish).</summary>
    bool Solid(UTextureRenderTarget2D target, int x, int y) => UKismetRenderingLibrary.ReadRenderTargetPixel(owner, target, x, y).A > 0;

    /// <summary>
    /// An empty render target: sRGB, so colours stay as stored, see-through, and drawn with sharp pixels. The filter
    /// only takes effect when the target is made, so it's made small and then resized.
    /// </summary>
    UTextureRenderTarget2D? NewTarget(int width, int height)
    {
        var format = (settings != null && settings.CharacterLighting) ? ETextureRenderTargetFormat.RTF_RGBA8 : ETextureRenderTargetFormat.RTF_RGBA8_SRGB;
        var target = UKismetRenderingLibrary.CreateRenderTarget2D(owner, 1, 1, format,
            new FLinearColor(), false, false);
        if (target == null) return null;
        target.Filter = TextureFilter.TF_Nearest;
        UKismetRenderingLibrary.ResizeRenderTarget2D(target, width, height);
        // The resize happens on the render thread: reading a pixel waits for it, so the first drawing isn't lost.
        UKismetRenderingLibrary.ReadRenderTargetPixel(owner, target, 0, 0);
        return target;
    }

    UTexture2D? File(string name)
    {
        if (files.ContainsKey(name)) return files[name];
        var path = Folder() + name + ".png";
        if (!UBlueprintPathsLibrary.FileExists(path)) return null;
        var file = UKismetRenderingLibrary.ImportFileAsTexture2D(owner, path);
        if (file != null)
        {
            files[name] = file;
            ours.Add(file);
        }
        return file;
    }

    /// <summary>The cape's &lt;number&gt;_MRES.png (in the same layout as the cape), or a plain MRES.</summary>
    UTexture? Mres(int number)
    {
        var file = File($"{number}_MRES");
        if (file != null)
        {
            var target = Convert(file, mresTextures.ContainsKey(number) ? mresTextures[number] : null);
            if (target != null)
            {
                mresTextures[number] = target;
                return target;
            }
        }
        if (defaultMres == null)
        {
            defaultMres = UKismetRenderingLibrary.CreateRenderTarget2D(owner, 4, 4, ETextureRenderTargetFormat.RTF_RGBA8,
                new FLinearColor { R = 0, G = DefaultRoughness, B = 0, A = 1 }, false, false);
            if (defaultMres != null) ours.Add(defaultMres);
        }
        return defaultMres;
    }

    /// <summary>
    /// Saves the texture of the game's cape the character wears to the Capes\_game folder as a PNG (32x16, the game's
    /// layout), its MRES too: to start a cape from.
    /// </summary>
    public void ExportGameCape()
    {
        if (owner == null) return;
        var player = World.Player(owner) as ACharacter;
        UMaterialInstanceDynamic? material = null;
        if (player != null)
            foreach (var component in player.K2_GetComponentsByClass(Unreal.ClassOf<USkeletalMeshComponent>()))
                if (component is USkeletalMeshComponent mesh && !ownCapes.Contains(mesh) && mesh.IsVisible()
                    && UKismetSystemLibrary.GetPathName(mesh.GetSkinnedAsset()) == CapeMesh)
                {
                    material = mesh.GetMaterial(0) as UMaterialInstanceDynamic;
                    if (material == null)
                    {
                        var source = mesh.GetMaterial(0);
                        if (source != null) material = UKismetMaterialLibrary.CreateDynamicMaterialInstance(owner, source, FName.None, EMIDCreationFlags.Transient);
                    }
                }
        if (material == null)
        {
            Log.Write("The character wears none of the game's capes to save");
            return;
        }
        var cape = material.K2_GetTextureParameterValue(TextureParameter);
        var mres = material.K2_GetTextureParameterValue(MresParameter);
        // While a custom cape is worn, the game's is the one it replaced.
        for (int i = 0; i < instances.Count; i++)
            if (instances[i] == material)
            {
                cape = oldCapes[i];
                mres = oldMres[i];
            }
        if (cape == null)
        {
            Log.Write("The cape's material has no texture");
            return;
        }
        var name = UKismetSystemLibrary.GetObjectName(cape);
        Export(cape, name);
        if (mres != null) Export(mres, name + "_MRES");
        Log.Write($"Saved {name}.png to {Folder()}_game/");
    }

    void Export(UTexture texture, string name)
    {
        int width = GameWidth;
        int height = GameHeight;
        if (texture is UTexture2D texture2D)
        {
            width = texture2D.Blueprint_GetSizeX();
            height = texture2D.Blueprint_GetSizeY();
        }
        var target = NewTarget(width, height);
        if (target == null) return;
        ours.Add(target);
        UKismetRenderingLibrary.BeginDrawCanvasToRenderTarget(owner, target, out var canvas, out var size, out var context);
        canvas?.K2_DrawTexture(texture, new FVector2D(), new FVector2D { X = width, Y = height }, new FVector2D(),
            new FVector2D { X = 1, Y = 1 }, new FLinearColor { R = 1, G = 1, B = 1, A = 1 }, EBlendMode.BLEND_AlphaComposite, 0, new FVector2D());
        UKismetRenderingLibrary.EndDrawCanvasToRenderTarget(owner, context);
        UKismetRenderingLibrary.ExportRenderTarget(owner, target, Folder() + "_game/", name + ".png");
    }

    /// <summary>The numbers of the PNGs in the Capes folder (1.png to 20.png), in order.</summary>
    public static List<int> Available()
    {
        var numbers = new List<int>();
        var folder = Folder();
        for (int i = 1; i <= MaxCapes; i++)
            if (UBlueprintPathsLibrary.FileExists(folder + i + ".png")) numbers.Add(i);
        return numbers;
    }

    static string Folder() =>
        UBlueprintPathsLibrary.ConvertRelativePathToFull(UBlueprintPathsLibrary.ProjectContentDir() + "Paks/~mods/CustomCapes/Capes/", "");
}
