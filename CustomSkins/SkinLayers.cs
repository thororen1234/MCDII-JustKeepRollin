using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.ProceduralMeshComponent;

namespace CustomSkins;

/// <summary>
/// The skin's second layer (the hat, jacket, sleeves and pants), built from the skin's pixels in the Java Edition layout:
/// the game's character only draws the hat, and not at Java's size. Flat, each solid pixel is a square on a box around
/// the body part, a quarter of a pixel out (the hat half a pixel), like Java. In 3D, each is a block standing out from the
/// body part to there, like the 3D Skin Layers mods. The game's own hat ends up under the new one either way.
///
/// The body parts are Java's with slim arms, as the game's body mesh has them, in the body's space in the reference pose
/// (1 pixel = 6.25 cm, the feet at z 0, the face toward +y, the character's right toward -x): the head from z 150 to 200
/// (see FaceParts), the body and arms from 75 to 150, the legs from 0 to 75. Each part is attached to the bone its part
/// of the mesh is skinned to (every vertex to one bone: head, shoulders, an arm or a leg).
/// </summary>
public class SkinLayers : UObject
{
    public const int Off = 0;
    public const int Flat = 1;
    public const int Blocks = 2;

    const float Pixel = 6.25f;
    const float SkinSize = 64;
    // How far out each layer sits (Java's), in pixels: flat, the layer is there; in 3D, the blocks reach out to there.
    const float HatOut = 0.5f;
    const float LayerOut = 0.25f;
    // The game's arms' width in pixels (Java's slim arms; Steve's are 4).
    const float ArmWidth = 3;
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
    // The face being built: its texture's corner in the skin, its size in pixels, and where it is (in pixels): on the
    // body part, and grown out to where the layer sits.
    int faceU;
    int faceV;
    int columns;
    int rows;
    FVector corner;
    FVector across;
    FVector down;
    FVector cornerOut;
    FVector acrossOut;
    FVector downOut;
    FVector outward;
    // The parts built, each attached to the bone it follows.
    List<UProceduralMeshComponent> parts = new();

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
        // The game's arms are slim, 3 pixels wide (its body mesh, SK_Player_Master), so the sleeves have the slim
        // layout: a 4 pixel wide sleeve stood a pixel off the arm's outside.
        Part(actor, body, material, Find(body, "J_R_Arm|upperarm_r"), 40, 32, -4 - ArmWidth, -4, -2, 2, 12, 24, LayerOut, blocks);
        Part(actor, body, material, Find(body, "J_L_Arm|upperarm_l"), 48, 48, 4, 4 + ArmWidth, -2, 2, 12, 24, LayerOut, blocks);
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
        int w = (int)(x1 - x0 + 0.5f);
        int h = (int)(z1 - z0 + 0.5f);
        int d = (int)(y1 - y0 + 0.5f);

        // Front, back, the character's right and left, top and bottom: each from its texture's top left pixel, along
        // its columns and rows.
        Face(u + d, v + d, w, h, V(x0, y1, z1), V(x1 - x0, 0, 0), V(0, 0, z0 - z1), V(0, 1, 0), flatOut, blocks);
        Face(u + 2 * d + w, v + d, w, h, V(x1, y0, z1), V(x0 - x1, 0, 0), V(0, 0, z0 - z1), V(0, -1, 0), flatOut, blocks);
        Face(u, v + d, d, h, V(x0, y0, z1), V(0, y1 - y0, 0), V(0, 0, z0 - z1), V(-1, 0, 0), flatOut, blocks);
        Face(u + d + w, v + d, d, h, V(x1, y1, z1), V(0, y0 - y1, 0), V(0, 0, z0 - z1), V(1, 0, 0), flatOut, blocks);
        Face(u + d, v, w, d, V(x0, y0, z1), V(x1 - x0, 0, 0), V(0, y1 - y0, 0), V(0, 0, 1), flatOut, blocks);
        Face(u + d + w, v, w, d, V(x0, y1, z0), V(x1 - x0, 0, 0), V(0, y0 - y1, 0), V(0, 0, -1), flatOut, blocks);
        if (vertices.Count == 0) return;
        // Into the bone's own space, the way the body's skinning puts its vertices: built in the body's reference pose,
        // so the bone's reference place undone. Done to the vertices, not as the part's place on the bone: a transform
        // can't put the bone's uneven scale (the menus' characters squash and stretch as they show) after a rotation, and
        // the layers bounced away from the body until it settled.
        var unbind = UKismetMathLibrary.InvertTransform(RefPose(body, bone));
        for (int i = 0; i < vertices.Count; i++)
        {
            vertices[i] = UKismetMathLibrary.TransformLocation(unbind, vertices[i]);
            normals[i] = UKismetMathLibrary.TransformDirection(unbind, normals[i]);
        }

        var identity = new FTransform { Rotation = UKismetMathLibrary.Quat_Identity(), Scale3D = new FVector { X = 1, Y = 1, Z = 1 } };
        var part = actor.AddComponentByClass(Unreal.ClassOf<UProceduralMeshComponent>(), true, identity, false) as UProceduralMeshComponent;
        if (part == null) return;
        parts.Add(part);
        // On the bone itself, so the engine moves it with the animation in the same frame (placing it from a tick lags a
        // frame behind).
        part.K2_AttachToComponent(body, bone, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, false);
        part.CreateMeshSection_LinearColor(0, vertices, triangles, normals, uvs, unused, unused, unused, colors, tangents, false, false);
        part.SetMaterial(0, material);
        part.SetCastShadow(false);
        part.SetCollisionEnabled(ECollisionEnabled.NoCollision);
        // Lit like the body: the menus light their copies of the character on a lighting channel of their own.
        var lighting = body.LightingChannels;
        part.SetLightingChannels(lighting.bChannel0, lighting.bChannel1, lighting.bChannel2);
    }

    /// <summary>A point or direction in pixels.</summary>
    static FVector V(float x, float y, float z) => new FVector { X = x, Y = y, Z = z };

    /// <summary>
    /// A face of a box: its texture columns x rows from (u, v), its top left corner, its whole width and height (as
    /// directions along its columns and rows) and the way it faces, all in pixels, and how far out the layer sits.
    /// Flat, each solid pixel is a square on the face grown out to there (Java's layer). In 3D, each is a block from the
    /// pixel on the body part out to the pixel on that grown face, a little wider outside than in: the blocks' outsides
    /// meet each other, and so do two faces' blocks along the box's edges, with no gap at the corners.
    /// </summary>
    void Face(int u, int v, int faceColumns, int faceRows, FVector topLeft, FVector faceWidth, FVector faceHeight, FVector facing,
        float grow, bool blocks)
    {
        faceU = u;
        faceV = v;
        columns = faceColumns;
        rows = faceRows;
        corner = topLeft;
        across = UKismetMathLibrary.Divide_VectorFloat(faceWidth, faceColumns);
        down = UKismetMathLibrary.Divide_VectorFloat(faceHeight, faceRows);
        outward = facing;
        // The face grown by the layer's distance on every side, and moved out by it.
        var acrossWay = UKismetMathLibrary.Normal(faceWidth, 0.0001f);
        var downWay = UKismetMathLibrary.Normal(faceHeight, 0.0001f);
        cornerOut = UKismetMathLibrary.Add_VectorVector(topLeft, UKismetMathLibrary.Multiply_VectorFloat(
            UKismetMathLibrary.Subtract_VectorVector(facing, UKismetMathLibrary.Add_VectorVector(acrossWay, downWay)), grow));
        acrossOut = UKismetMathLibrary.Divide_VectorFloat(UKismetMathLibrary.Add_VectorVector(faceWidth, UKismetMathLibrary.Multiply_VectorFloat(acrossWay, 2 * grow)), faceColumns);
        downOut = UKismetMathLibrary.Divide_VectorFloat(UKismetMathLibrary.Add_VectorVector(faceHeight, UKismetMathLibrary.Multiply_VectorFloat(downWay, 2 * grow)), faceRows);
        for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                if (!Solid(column, row)) continue;
                var uv = new FVector2D { X = (faceU + column + 0.5f) / SkinSize, Y = (faceV + row + 0.5f) / SkinSize };
                // The outside (all a flat layer has), and in 3D the block's sides where nothing is next to it.
                Quad(Out(column, row), Out(column + 1, row), Out(column + 1, row + 1), Out(column, row + 1), outward, uv);
                if (!blocks) continue;
                if (!Solid(column, row - 1)) Side(column, row, column + 1, row, Negate(down), uv);
                if (!Solid(column, row + 1)) Side(column, row + 1, column + 1, row + 1, down, uv);
                if (!Solid(column - 1, row)) Side(column, row, column, row + 1, Negate(across), uv);
                if (!Solid(column + 1, row)) Side(column + 1, row, column + 1, row + 1, across, uv);
            }
    }

    /// <summary>A block's side: from an edge of its pixel on the body part out to the same edge on the grown face.</summary>
    void Side(int column0, int row0, int column1, int row1, FVector facing, FVector2D uv) =>
        Quad(In(column0, row0), In(column1, row1), Out(column1, row1), Out(column0, row0), facing, uv);

    /// <summary>Whether a pixel of the face being built is solid (off its edges is see-through).</summary>
    bool Solid(int column, int row)
    {
        if (column < 0 || row < 0 || column >= columns || row >= rows) return false;
        return SolidAt(faceU + column, faceV + row);
    }

    /// <summary>Whether a pixel of the skin (in the 64x64 layout) is solid.</summary>
    bool SolidAt(int u, int v)
    {
        float scale = width / SkinSize;
        int x = (int)((u + 0.5f) * scale);
        int y = (int)((v + 0.5f) * scale);
        int at = y * width + x;
        return at >= 0 && at < pixels.Count && pixels[at].A > 0.5f;
    }

    /// <summary>A corner of the face being built's pixels on the body part, in centimetres.</summary>
    FVector In(int column, int row) => At(corner, across, down, column, row);

    /// <summary>A corner of the face being built's pixels on the face grown out to the layer, in centimetres.</summary>
    FVector Out(int column, int row) => At(cornerOut, acrossOut, downOut, column, row);

    static FVector At(FVector topLeft, FVector columnStep, FVector rowStep, int column, int row)
    {
        var point = UKismetMathLibrary.Add_VectorVector(topLeft, UKismetMathLibrary.Multiply_VectorFloat(columnStep, column));
        point = UKismetMathLibrary.Add_VectorVector(point, UKismetMathLibrary.Multiply_VectorFloat(rowStep, row));
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

    /// <summary>Removes every layer built.</summary>
    public void Clear()
    {
        foreach (var part in parts)
            if (part != null && UKismetSystemLibrary.IsValid(part)) part.K2_DestroyComponent(part);
        parts.Clear();
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
