using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;
using UE.ProceduralMeshComponent;

namespace CustomSkins;

/// <summary>
/// Eyes and mouth that move with the face (looking around, blinking, talking): a square per pixel, coloured from the
/// skin. The shapes and the skin pixels they take their colours from are the converter's
/// (https://ewanhowell.com/tools/dungeons2skinconverter): eyes 1 pixel tall on face row 5, 6 or 7, or 2 pixels tall on
/// rows 4-5, 5-6 or 6-7, a pupil each, and a mouth 2 pixels wide on row 8 or 4 on row 7. A skin colours the shapes it
/// uses and leaves the rest see-through, which hides them. (The game draws its own shape, 1 pixel eyes on row 5 and a
/// 2 pixel mouth on row 7, itself.)
///
/// The face is the head's front: 8x8 pixels of 6.25 cm, from x -25 (the face's left as you look at it) to 25, z 200
/// (the top) down to 150, at y 25. The bones sit on it: an eye bone at the bottom of row 5 between the eye's columns,
/// a pupil bone on its inner column, the mouth bone at the bottom of row 7.
///
/// Each shape follows its bone, but the game's blinks and squints scale an eye toward the bottom of row 5, where its
/// own eyes are: each shape is scaled toward its own bottom edge instead, or the lower eyes would jump about.
/// </summary>
public class FaceParts : UObject
{
    const float Pixel = 6.25f;
    const float FaceLeft = -25;
    const float FaceTop = 200;
    const float FaceFront = 25;
    // In front of the face, the pupils in front of the eyes. The game's own face has layers up to 0.4 cm out (its eyes at
    // 0.06, planes of the face at 0.25 and 0.4, from its mesh): shapes at the same distance as one flickered as one or
    // the other was drawn. The eye shapes overlap each other (a 1 pixel eye on row 5 and the 2 pixel ones on rows 4-5
    // and 5-6), so each is a step further out than the one before, for the same reason.
    const float EyeDepth = 0.6f;
    const float PupilDepth = 1.0f;
    const float ShapeStep = 0.05f;
    const float SkinSize = 64;

    // The squares of the shape being built: fields, because a List passed to a method is a copy in a Blueprint.
    List<FVector> vertices = new();
    List<int> triangles = new();
    List<FVector> normals = new();
    List<FVector2D> uvs = new();
    List<FVector2D> unused = new();
    List<FLinearColor> colors = new();
    List<FProcMeshTangent> tangents = new();
    float depth;
    // Per shape built: its squares, the body they're on, the bone they follow, its place in the reference pose, and
    // the point the shape is scaled toward (in the body's space).
    List<UProceduralMeshComponent> parts = new();
    List<USkinnedMeshComponent> bodies = new();
    List<FName> bones = new();
    List<FTransform> rests = new();
    List<FVector> pivots = new();

    public static FaceParts? Create(UObject owner) => UGameplayStatics.SpawnObject(Unreal.ClassOf<FaceParts>(), owner) as FaceParts;

    /// <summary>Whether an actor has another mod's eyes and mouth already (Custom Skin Loader's face mesh).</summary>
    public static bool HasOthers(AActor actor)
    {
        foreach (var component in actor.K2_GetComponentsByClass(Unreal.ClassOf<USkinnedMeshComponent>()))
            if (component is USkinnedMeshComponent skinned && UKismetSystemLibrary.GetPathName(skinned.GetSkinnedAsset()).Contains("FaceExtras")) return true;
        return false;
    }

    /// <summary>Builds the eyes and mouth on a body, in the skin material.</summary>
    public void Build(AActor actor, USkinnedMeshComponent body, UMaterialInterface material)
    {
        // The eye on the face's left as you look at it is the character's right eye: columns 1-2, pupil on 2.
        Eyes(actor, body, material, "J_R_EyeIris", "J_R_EyePupil", 1, 2, 26, 32, 24, 30);
        Eyes(actor, body, material, "J_L_EyeIris", "J_L_EyePupil", 5, 5, 28, 34, 25, 31);
        // 2 pixels on row 8, 4 on row 7.
        Begin(EyeDepth);
        Square(3, 8, 24, 7);
        Square(4, 8, 25, 7);
        Part(actor, body, material, "J_Mouth", 8);
        Begin(EyeDepth);
        for (int i = 0; i < 4; i++) Square(2 + i, 7, 26 + i, 7);
        Part(actor, body, material, "J_Mouth", 7);
    }

    /// <summary>
    /// An eye in every shape: whites 2 pixels wide from a column, a pupil on another. The 1 pixel shapes take their
    /// colours from (u, 6) on row 5 and (u2, 6), (u2, 7) on rows 6 and 7; the 2 pixel ones from (u, 0-1), (u, 4-5) and
    /// (u, 2-3). The pupil's are in the same rows, at its own u.
    /// </summary>
    void Eyes(AActor actor, USkinnedMeshComponent body, UMaterialInterface material, string eye, string pupil,
        int column, int pupilColumn, int u, int u2, int pupilU, int pupilU2)
    {
        Shape(actor, body, material, eye, column, 2, u, 6, 5, 5, EyeDepth);
        Shape(actor, body, material, eye, column, 2, u2, 6, 6, 6, EyeDepth + ShapeStep);
        Shape(actor, body, material, eye, column, 2, u2, 7, 7, 7, EyeDepth + 2 * ShapeStep);
        Shape(actor, body, material, eye, column, 2, u, 0, 4, 5, EyeDepth + 3 * ShapeStep);
        Shape(actor, body, material, eye, column, 2, u, 4, 5, 6, EyeDepth + 4 * ShapeStep);
        Shape(actor, body, material, eye, column, 2, u, 2, 6, 7, EyeDepth + 5 * ShapeStep);
        Shape(actor, body, material, pupil, pupilColumn, 1, pupilU, 6, 5, 5, PupilDepth);
        Shape(actor, body, material, pupil, pupilColumn, 1, pupilU2, 6, 6, 6, PupilDepth + ShapeStep);
        Shape(actor, body, material, pupil, pupilColumn, 1, pupilU2, 7, 7, 7, PupilDepth + 2 * ShapeStep);
        Shape(actor, body, material, pupil, pupilColumn, 1, pupilU, 0, 4, 5, PupilDepth + 3 * ShapeStep);
        Shape(actor, body, material, pupil, pupilColumn, 1, pupilU, 4, 5, 6, PupilDepth + 4 * ShapeStep);
        Shape(actor, body, material, pupil, pupilColumn, 1, pupilU, 2, 6, 7, PupilDepth + 5 * ShapeStep);
    }

    /// <summary>A block of squares, some columns wide from rows first to last, coloured from the skin from (u, v).</summary>
    void Shape(AActor actor, USkinnedMeshComponent body, UMaterialInterface material, string bone, int column, int width,
        int u, int v, int first, int last, float frontBy)
    {
        Begin(frontBy);
        for (int row = first; row <= last; row++)
            for (int i = 0; i < width; i++) Square(column + i, row, u + i, v + row - first);
        Part(actor, body, material, bone, last);
    }

    void Begin(float frontBy)
    {
        vertices.Clear();
        triangles.Clear();
        normals.Clear();
        uvs.Clear();
        depth = frontBy;
    }

    /// <summary>A square on the face at a column (0-7) and row (1-8), coloured all over from one skin pixel.</summary>
    void Square(int column, int row, int u, int v)
    {
        float left = FaceLeft + column * Pixel;
        float top = FaceTop - (row - 1) * Pixel;
        float y = FaceFront + depth;
        int first = vertices.Count;
        vertices.Add(new FVector { X = left, Y = y, Z = top });
        vertices.Add(new FVector { X = left + Pixel, Y = y, Z = top });
        vertices.Add(new FVector { X = left + Pixel, Y = y, Z = top - Pixel });
        vertices.Add(new FVector { X = left, Y = y, Z = top - Pixel });
        // The middle of the pixel, so its neighbours don't bleed in.
        var uv = new FVector2D { X = (u + 0.5f) / SkinSize, Y = (v + 0.5f) / SkinSize };
        for (int i = 0; i < 4; i++)
        {
            normals.Add(new FVector { Y = 1 });
            uvs.Add(uv);
        }
        // Both sides, whichever way round the game draws them.
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

    /// <summary>
    /// Puts the squares built on the body, following a bone, scaled toward the bottom of a row (under the bone, in the
    /// middle of its x). The squares are in the body's space, as is the part: Update moves it with the bone.
    /// </summary>
    void Part(AActor actor, USkinnedMeshComponent body, UMaterialInterface material, string bone, int bottomRow)
    {
        if (body.GetBoneIndex(bone) < 0) return;
        var identity = new FTransform { Rotation = UKismetMathLibrary.Quat_Identity(), Scale3D = new FVector { X = 1, Y = 1, Z = 1 } };
        var part = actor.AddComponentByClass(Unreal.ClassOf<UProceduralMeshComponent>(), true, identity, false) as UProceduralMeshComponent;
        if (part == null) return;
        part.K2_AttachToComponent(body, FName.None, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, false);
        part.CreateMeshSection_LinearColor(0, vertices, triangles, normals, uvs, unused, unused, unused, colors, tangents, false, false);
        part.SetMaterial(0, material);
        part.SetCastShadow(false);
        part.SetCollisionEnabled(ECollisionEnabled.NoCollision);
        // Lit like the face: the menus light their copies of the character on a lighting channel of their own.
        var lighting = body.LightingChannels;
        part.SetLightingChannels(lighting.bChannel0, lighting.bChannel1, lighting.bChannel2);
        var rest = RefPose(body, bone);
        parts.Add(part);
        bodies.Add(body);
        bones.Add(bone);
        rests.Add(rest);
        pivots.Add(new FVector { X = rest.Translation.X, Y = FaceFront, Z = FaceTop - bottomRow * Pixel });
        Place(parts.Count - 1);
    }

    /// <summary>Moves every shape with its bone. Call every frame.</summary>
    public void Update()
    {
        for (int i = 0; i < parts.Count; i++) Place(i);
    }

    /// <summary>
    /// Where a shape goes: where its bone takes the face from the reference pose, but with the bone's scale (blinks,
    /// squints) around the shape's pivot rather than the bone, so the pivot moves only as the bone moves and turns.
    /// </summary>
    void Place(int index)
    {
        var part = parts[index];
        var body = bodies[index];
        if (part == null || body == null || !UKismetSystemLibrary.IsValid(part) || !UKismetSystemLibrary.IsValid(body)) return;
        var rest = rests[index];
        var now = body.GetSocketTransform(bones[index], ERelativeTransformSpace.RTS_Component);
        var fromRest = UKismetMathLibrary.InvertTransform(rest);
        // The face from the reference pose to now, scale included...
        var moved = UKismetMathLibrary.ComposeTransforms(fromRest, now);
        // ...and without the bone's change of scale.
        var unscaled = UKismetMathLibrary.ComposeTransforms(fromRest,
            UKismetMathLibrary.MakeTransform(now.Translation, UKismetMathLibrary.Quat_Rotator(now.Rotation), rest.Scale3D));
        var pivot = pivots[index];
        var shift = UKismetMathLibrary.Subtract_VectorVector(UKismetMathLibrary.TransformLocation(unscaled, pivot), UKismetMathLibrary.TransformLocation(moved, pivot));
        moved.Translation = UKismetMathLibrary.Add_VectorVector(moved.Translation, shift);
        part.K2_SetRelativeTransform(moved, false, out var hit, false);
    }

    /// <summary>Removes every eye and mouth built.</summary>
    public void Clear()
    {
        foreach (var part in parts)
            if (part != null && UKismetSystemLibrary.IsValid(part)) part.K2_DestroyComponent(part);
        parts.Clear();
        bodies.Clear();
        bones.Clear();
        rests.Clear();
        pivots.Clear();
    }

    /// <summary>The bone's transform in the body's space, in the reference pose.</summary>
    static FTransform RefPose(USkinnedMeshComponent body, string bone)
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
