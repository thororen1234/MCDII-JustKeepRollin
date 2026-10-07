using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.ProceduralMeshComponent;

namespace CustomSkins;

/// <summary>
/// The skin's second layer (the hat, jacket, sleeves and pants), built from the skin's pixels in the Java Edition layout:
/// the game's character only draws the hat, and not at Java's size. Flat, each solid pixel is a square on a box around
/// the body part, a quarter of a pixel out (the hat half a pixel), like Java. In 3D, each is a block a pixel deep standing
/// out from the body part, like the 3D Skin Layers mod. The game's own hat ends up under the new one either way.
///
/// The body parts are Java's, in the body's space in the reference pose (1 pixel = 6.25 cm, the feet at z 0, the face
/// toward +y, the character's right toward -x): the head from z 150 to 200 (see FaceParts), the body and arms from 75 to
/// 150, the legs from 0 to 75. Each part follows its bone, the way FaceParts' shapes do.
/// </summary>
public class SkinLayers : UObject
{
    public const int Off = 0;
    public const int Flat = 1;
    public const int Blocks = 2;

    const float Pixel = 6.25f;
    const float SkinSize = 64;
    // How far out each flat layer sits (Java's), and how deep the 3D blocks are, in pixels.
    const float HatOut = 0.5f;
    const float LayerOut = 0.25f;
    const float Depth = 1;
    const string HeadBones = "J_Head|J_Neck|head";
    const string BodyBones = "J_Shoulders|J_Spine|spine_03|spine_02|spine";

    // The skin's pixels (read from a render target), its width, and what the part being built is made of: fields,
    // because a List passed to a method is a copy in a Blueprint.
    List<FLinearColor> pixels = new();
    int width;
    List<FVector> vertices = new();
    List<int> triangles = new();
    List<FVector> normals = new();
    List<FVector2D> uvs = new();
    List<FVector2D> unused = new();
    List<FLinearColor> colors = new();
    List<FProcMeshTangent> tangents = new();
    // The face being built: its texture's corner in the skin, its size in pixels, and where it is (in pixels).
    int faceU;
    int faceV;
    int columns;
    int rows;
    FVector corner;
    FVector across;
    FVector down;
    FVector outward;
    // Per part built: its mesh, the body it's on, the bone it follows and the bone's place in the reference pose.
    List<UProceduralMeshComponent> parts = new();
    List<USkinnedMeshComponent> bodies = new();
    List<FName> bones = new();
    List<FTransform> rests = new();

    public static SkinLayers? Create(UObject owner) => UGameplayStatics.SpawnObject(Unreal.ClassOf<SkinLayers>(), owner) as SkinLayers;

    /// <summary>
    /// Builds the layers on a body, from a skin's pixels (a render target of it), in the body's skin material. Mode is
    /// <see cref="Flat"/> or <see cref="Blocks"/>.
    /// </summary>
    public void Build(UObject context, AActor actor, USkinnedMeshComponent body, UMaterialInterface material, UTextureRenderTarget2D skin, int mode)
    {
        if (mode == Off) return;
        if (!UKismetRenderingLibrary.ReadRenderTargetRaw(context, skin, out pixels, false)) return;
        width = skin.SizeX;
        if (width <= 0 || pixels.Count < width * width) return;
        bool blocks = mode == Blocks;

        Part(actor, body, material, Find(body, HeadBones), 32, 0, -4, 4, -4, 4, 24, 32, HatOut, blocks);
        Part(actor, body, material, Find(body, BodyBones), 16, 32, -4, 4, -2, 2, 12, 24, LayerOut, blocks);
        Part(actor, body, material, Find(body, "J_R_Arm|upperarm_r"), 40, 32, -8, -4, -2, 2, 12, 24, LayerOut, blocks);
        Part(actor, body, material, Find(body, "J_L_Arm|upperarm_l"), 48, 48, 4, 8, -2, 2, 12, 24, LayerOut, blocks);
        Part(actor, body, material, Find(body, "J_R_Leg|thigh_r"), 0, 32, -4, 0, -2, 2, 0, 12, LayerOut, blocks);
        Part(actor, body, material, Find(body, "J_L_Leg|thigh_l"), 0, 48, 0, 4, -2, 2, 0, 12, LayerOut, blocks);
    }

    /// <summary>The first of the '|' separated bone names the body has, or None.</summary>
    static FName Find(USkinnedMeshComponent body, string names)
    {
        foreach (var name in UKismetStringLibrary.ParseIntoArray(names, "|", true))
            if (body.GetBoneIndex(name) >= 0) return name;
        return FName.None;
    }

    /// <summary>
    /// A body part's layer: a box from (x0, y0, z0) to (x1, y1, z1) in pixels, its texture at (u, v) in the Java layout
    /// (the box's faces laid out around it as Java lays them), following a bone.
    /// </summary>
    void Part(AActor actor, USkinnedMeshComponent body, UMaterialInterface material, FName bone, int u, int v,
        float x0, float x1, float y0, float y1, float z0, float z1, float flatOut, bool blocks)
    {
        if (bone == FName.None) return;
        vertices.Clear();
        triangles.Clear();
        normals.Clear();
        uvs.Clear();
        // Flat, the box grows to where the layer sits; in 3D the blocks start on the body part itself.
        float grow = blocks ? 0 : flatOut;
        x0 -= grow;
        y0 -= grow;
        z0 -= grow;
        x1 += grow;
        y1 += grow;
        z1 += grow;
        int w = (int)(x1 - x0 - 2 * grow + 0.5f);
        int h = (int)(z1 - z0 - 2 * grow + 0.5f);
        int d = (int)(y1 - y0 - 2 * grow + 0.5f);

        // Front, back, the character's right and left, top and bottom: each from its texture's top left pixel, along
        // its columns and rows.
        Face(u + d, v + d, w, h, V(x0, y1, z1), V(x1 - x0, 0, 0), V(0, 0, z0 - z1), V(0, 1, 0), blocks);
        Face(u + 2 * d + w, v + d, w, h, V(x1, y0, z1), V(x0 - x1, 0, 0), V(0, 0, z0 - z1), V(0, -1, 0), blocks);
        Face(u, v + d, d, h, V(x0, y0, z1), V(0, y1 - y0, 0), V(0, 0, z0 - z1), V(-1, 0, 0), blocks);
        Face(u + d + w, v + d, d, h, V(x1, y1, z1), V(0, y0 - y1, 0), V(0, 0, z0 - z1), V(1, 0, 0), blocks);
        Face(u + d, v, w, d, V(x0, y0, z1), V(x1 - x0, 0, 0), V(0, y1 - y0, 0), V(0, 0, 1), blocks);
        Face(u + d + w, v, w, d, V(x0, y1, z0), V(x1 - x0, 0, 0), V(0, y0 - y1, 0), V(0, 0, -1), blocks);
        if (vertices.Count == 0) return;

        var identity = new FTransform { Rotation = UKismetMathLibrary.Quat_Identity(), Scale3D = new FVector { X = 1, Y = 1, Z = 1 } };
        var part = actor.AddComponentByClass(Unreal.ClassOf<UProceduralMeshComponent>(), true, identity, false) as UProceduralMeshComponent;
        if (part == null) return;
        part.K2_AttachToComponent(body, FName.None, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, false);
        part.CreateMeshSection_LinearColor(0, vertices, triangles, normals, uvs, unused, unused, unused, colors, tangents, false, false);
        part.SetMaterial(0, material);
        part.SetCastShadow(false);
        part.SetCollisionEnabled(ECollisionEnabled.NoCollision);
        // Lit like the body: the menus light their copies of the character on a lighting channel of their own.
        var lighting = body.LightingChannels;
        part.SetLightingChannels(lighting.bChannel0, lighting.bChannel1, lighting.bChannel2);
        parts.Add(part);
        bodies.Add(body);
        bones.Add(bone);
        rests.Add(RefPose(body, bone));
        Place(parts.Count - 1);
    }

    /// <summary>A point or direction in pixels.</summary>
    static FVector V(float x, float y, float z) => new FVector { X = x, Y = y, Z = z };

    /// <summary>
    /// A face of a box: its texture columns x rows from (u, v), its top left corner, its whole width and height (as
    /// directions along its columns and rows) and the way it faces, all in pixels.
    /// </summary>
    void Face(int u, int v, int faceColumns, int faceRows, FVector topLeft, FVector faceWidth, FVector faceHeight, FVector facing, bool blocks)
    {
        faceU = u;
        faceV = v;
        columns = faceColumns;
        rows = faceRows;
        corner = topLeft;
        across = UKismetMathLibrary.Divide_VectorFloat(faceWidth, faceColumns);
        down = UKismetMathLibrary.Divide_VectorFloat(faceHeight, faceRows);
        outward = facing;
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                if (!Solid(column, row)) continue;
                var uv = new FVector2D { X = (faceU + column + 0.5f) / SkinSize, Y = (faceV + row + 0.5f) / SkinSize };
                if (!blocks)
                {
                    Quad(At(column, row, 0), At(column + 1, row, 0), At(column + 1, row + 1, 0), At(column, row + 1, 0), outward, uv);
                    continue;
                }
                // The block's outside, and its sides where the pixel next to it is see-through.
                Quad(At(column, row, Depth), At(column + 1, row, Depth), At(column + 1, row + 1, Depth), At(column, row + 1, Depth), outward, uv);
                if (!Solid(column, row - 1))
                    Quad(At(column, row, 0), At(column + 1, row, 0), At(column + 1, row, Depth), At(column, row, Depth), Negate(down), uv);
                if (!Solid(column, row + 1))
                    Quad(At(column, row + 1, 0), At(column + 1, row + 1, 0), At(column + 1, row + 1, Depth), At(column, row + 1, Depth), down, uv);
                if (!Solid(column - 1, row))
                    Quad(At(column, row, 0), At(column, row + 1, 0), At(column, row + 1, Depth), At(column, row, Depth), Negate(across), uv);
                if (!Solid(column + 1, row))
                    Quad(At(column + 1, row, 0), At(column + 1, row + 1, 0), At(column + 1, row + 1, Depth), At(column + 1, row, Depth), across, uv);
            }
    }

    /// <summary>Whether a pixel of the face being built is solid (off its edges is see-through).</summary>
    bool Solid(int column, int row)
    {
        if (column < 0 || row < 0 || column >= columns || row >= rows) return false;
        float scale = width / SkinSize;
        int x = (int)((faceU + column + 0.5f) * scale);
        int y = (int)((faceV + row + 0.5f) * scale);
        int at = y * width + x;
        return at >= 0 && at < pixels.Count && pixels[at].A > 0.5f;
    }

    /// <summary>A corner of the face being built's pixels, some pixels out from it, in centimetres.</summary>
    FVector At(int column, int row, float raised)
    {
        var point = UKismetMathLibrary.Add_VectorVector(corner, UKismetMathLibrary.Multiply_VectorFloat(across, column));
        point = UKismetMathLibrary.Add_VectorVector(point, UKismetMathLibrary.Multiply_VectorFloat(down, row));
        point = UKismetMathLibrary.Add_VectorVector(point, UKismetMathLibrary.Multiply_VectorFloat(outward, raised));
        return UKismetMathLibrary.Multiply_VectorFloat(point, Pixel);
    }

    static FVector Negate(FVector vector) => UKismetMathLibrary.Multiply_VectorFloat(vector, -1);

    /// <summary>A square, coloured all over from one skin pixel, seen from both sides.</summary>
    void Quad(FVector a, FVector b, FVector c, FVector d, FVector facing, FVector2D uv)
    {
        int first = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        var normal = UKismetMathLibrary.Normal(facing, 0.0001f);
        for (int i = 0; i < 4; i++)
        {
            normals.Add(normal);
            uvs.Add(uv);
        }
        triangles.Add(first);
        triangles.Add(first + 1);
        triangles.Add(first + 2);
        triangles.Add(first);
        triangles.Add(first + 2);
        triangles.Add(first + 3);
        triangles.Add(first);
        triangles.Add(first + 2);
        triangles.Add(first + 1);
        triangles.Add(first);
        triangles.Add(first + 3);
        triangles.Add(first + 2);
    }

    /// <summary>Moves every part with its bone. Call every frame.</summary>
    public void Update()
    {
        for (int i = 0; i < parts.Count; i++) Place(i);
    }

    /// <summary>Where a part goes: where its bone takes it from the reference pose.</summary>
    void Place(int index)
    {
        var part = parts[index];
        var body = bodies[index];
        if (part == null || body == null || !UKismetSystemLibrary.IsValid(part) || !UKismetSystemLibrary.IsValid(body)) return;
        var now = body.GetSocketTransform(bones[index], ERelativeTransformSpace.RTS_Component);
        var moved = UKismetMathLibrary.ComposeTransforms(UKismetMathLibrary.InvertTransform(rests[index]), now);
        part.K2_SetRelativeTransform(moved, false, out var hit, false);
    }

    /// <summary>Removes every layer built.</summary>
    public void Clear()
    {
        foreach (var part in parts)
            if (part != null && UKismetSystemLibrary.IsValid(part)) part.K2_DestroyComponent(part);
        parts.Clear();
        bodies.Clear();
        bones.Clear();
        rests.Clear();
    }

    /// <summary>The bone's transform in the body's space, in the reference pose.</summary>
    static FTransform RefPose(USkinnedMeshComponent body, FName bone)
    {
        var transform = body.GetRefPoseTransform(body.GetBoneIndex(bone));
        var parent = body.GetParentBone(bone);
        while (parent != FName.None)
        {
            transform = UKismetMathLibrary.ComposeTransforms(transform, body.GetRefPoseTransform(body.GetBoneIndex(parent)));
            parent = body.GetParentBone(parent);
        }
        return transform;
    }
}
