using System.Collections.Generic;
using NeoRune;
using UE.CoreUObject;
using UE.Engine;

namespace Emoticons;

/// <summary>
/// Plays the baked Emoticons animations on the player's character.
///
/// Only this game shows the emote: the server and other players don't know about it.
/// </summary>
public class EmotePlayer : UObject
{
    // Bone slots, in EmoteData's order: Steve's anchor, body, low_body, head, then left arm, left forearm, right arm,
    // right forearm, left thigh, left calf, right thigh, right calf.
    const int SlotCount = 12;
    const int Pelvis = 0;
    const int Chest = 2;
    const int Head = 3;
    const int LeftArm = 4;
    const int RightArm = 6;
    const int FirstLimb = 4;

    const float TicksPerSecond = 20f;
    // Steve's pelvis to the base of his head, in blocks: sets how far a block is on this skeleton.
    const float StevePelvisToHead = 0.75f;
    // Walking this far (cm) from where the emote started stops it, like Emoticons' "stop on move".
    const float StopDistance = 15f;

    AActor? owner;
    ACharacter? character;
    USkeletalMeshComponent? mesh;
    UPoseableMeshComponent? pose;
    List<USkinnedMeshComponent> followers = new();
    // Followers that didn't follow anything before the emote.
    List<USkinnedMeshComponent> borrowed = new();
    List<USceneComponent> attached = new();
    List<FName> attachedSockets = new();
    List<FTransform> attachedPlaces = new();
    // Child actors holding meshes that follow the pose.
    List<UChildActorComponent> pinned = new();

    List<FName> bones = new();
    // Bones hanging off the root beside the pelvis (the scabbard's): they don't follow the hips, so what hangs on
    // them is hidden during an emote.
    List<FName> rootBones = new();
    List<USceneComponent> hidden = new();
    // Per slot: the bone's component-space rotation in Steve's rest pose.
    List<FQuat> rest = new();
    // Unreal's axes (X forward, Y right, Z up) to the mesh's component space.
    FQuat toMesh;
    FVector pelvisRest;
    float blockSize;
    bool ready;

    bool playing;
    int emote;
    double startTime;
    FVector startLocation;
    // The game's montage playing when the emote started (or null): a new one (an attack) stops the emote.
    UAnimMontage? startMontage;
    // Per slot: its index among the emote's animated bones, or -1 when the emote leaves it at rest.
    List<int> slotData = new();
    int animatedCount;
    // Every emote's frames, unpacked the first time it plays: where each emote's start, or -1 before that.
    List<FQuat> frames = new();
    List<FVector> offsets = new();
    List<int> frameStarts = new();
    List<int> offsetStarts = new();
    int frameStart;
    int offsetStart;
    bool moves;

    public static EmotePlayer? Create(AActor owner)
    {
        var player = UGameplayStatics.SpawnObject(Unreal.ClassOf<EmotePlayer>(), owner) as EmotePlayer;
        if (player == null) return null;
        player.owner = owner;
        for (int i = 0; i < EmoteData.Count; i++)
        {
            player.frameStarts.Add(-1);
            player.offsetStarts.Add(-1);
        }
        return player;
    }

    public bool Playing => playing;

    public void Play(int index)
    {
        if (owner == null || index < 0 || index >= EmoteData.Count) return;
        if (!Prepare()) return;
        // While an emote plays the body shows the posed mesh, so its own pose is only worth copying before.
        if (Playing) Release();
        else CopyCurrentPose();

        Decode(index);
        emote = index;
        playing = true;
        startTime = UGameplayStatics.GetTimeSeconds(owner);
        startLocation = character!.K2_GetActorLocation();
        startMontage = mesh!.GetAnimInstance()?.GetCurrentActiveMontage();
        // A previous emote may have moved the pelvis.
        if (bones[Pelvis] != FName.None) pose!.SetBoneLocationByName(bones[Pelvis], pelvisRest, EBoneSpaces.ComponentSpace);
        Apply(0);
        Follow();
    }

    public void Stop()
    {
        if (!Playing) return;
        Release();
        playing = false;
    }

    /// <summary>Call every frame.</summary>
    public void Update()
    {
        if (!Playing || owner == null) return;
        if (character == null || mesh == null || pose == null || World.Player(owner) != character)
        {
            playing = false;
            return;
        }
        var moved = UKismetMathLibrary.VSize(UKismetMathLibrary.Subtract_VectorVector(character.K2_GetActorLocation(), startLocation));
        if (moved > StopDistance)
        {
            Stop();
            return;
        }
        // Attacking (or drinking a potion, or any of the game's own animations) stops it too.
        var montage = mesh.GetAnimInstance()?.GetCurrentActiveMontage();
        if (montage == null) startMontage = null;
        else if (montage != startMontage)
        {
            Stop();
            return;
        }

        var ticks = (UGameplayStatics.GetTimeSeconds(owner) - startTime) * TicksPerSecond;
        if (EmoteData.Looping(emote))
        {
            var length = EmoteData.Length(emote);
            if (length > 0) ticks -= UKismetMathLibrary.FFloor(ticks / length) * length;
        }
        else if (ticks >= EmoteData.Duration(emote))
        {
            Stop();
            return;
        }
        Apply(ticks);
        KeepFollowing();
    }

    /// <summary>
    /// Meshes set to follow the body during the emote follow the posed mesh instead. Other mods set theirs to follow the
    /// body when they like (Custom Skin Loader's eyes and mouth, on a timer), and the body follows the posed mesh while
    /// an emote plays: a mesh following it would keep its pose from before the emote (the eyes floating off the head).
    /// </summary>
    void KeepFollowing()
    {
        if (character == null || mesh == null || pose == null) return;
        // The game shows its weapons again when it likes (turning to a click): they stay hidden until the emote ends.
        foreach (var component in hidden)
            if (component != null && component.IsVisible()) component.SetVisibility(false, true);
        foreach (var component in character.K2_GetComponentsByClass(Unreal.ClassOf<USkinnedMeshComponent>()))
        {
            if (component is not USkinnedMeshComponent skinned || skinned == mesh || skinned == pose) continue;
            if (skinned.LeaderPoseComponent.Get() != mesh) continue;
            skinned.SetLeaderPoseComponent(pose, true, false);
            // Given back to the body when the emote ends.
            if (!followers.Contains(skinned)) followers.Add(skinned);
        }
    }

    /// <summary>Finds the character, its bones and the poseable mesh: again whenever the character changes.</summary>
    bool Prepare()
    {
        var current = World.Player(owner!) as ACharacter;
        if (current == null || current.Mesh == null) return false;
        if (ready && current == character && current.Mesh == mesh) return true;

        character = current;
        mesh = current.Mesh;
        ready = false;
        if (!FindBones() || !Calibrate()) return false;

        pose = character.AddComponentByClass(Unreal.ClassOf<UPoseableMeshComponent>(), true, new FTransform
        {
            Rotation = UKismetMathLibrary.Quat_Identity(),
            Scale3D = new FVector { X = 1, Y = 1, Z = 1 },
        }, false) as UPoseableMeshComponent;
        if (pose == null)
        {
            Log.Write("Couldn't add a poseable mesh to the character");
            return false;
        }
        pose.K2_AttachToComponent(mesh, FName.None, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, false);
        pose.SetSkinnedAssetAndUpdate(mesh.GetSkinnedAsset(), false);
        // Hidden, but still working out its bones so the real mesh can follow it.
        pose.VisibilityBasedAnimTickOption = EVisibilityBasedAnimTickOption.AlwaysTickPoseAndRefreshBones;
        pose.SetVisibility(false, false);
        ready = true;
        return true;
    }

    bool FindBones()
    {
        bones.Clear();
        bones.Add(Find("J_Hip|pelvis|hips|hip|root_pelvis|Bip001 Pelvis|Bip01 Pelvis|mixamorig:Hips"));
        bones.Add(Find("J_Spine|spine_01|spine01|spine1|spine|spine_lower|lower_spine|waist|Bip001 Spine|Bip01 Spine|mixamorig:Spine"));
        bones.Add(Find("J_Shoulders|spine_03|spine03|spine3|spine_02|spine02|spine2|chest|upperchest|upper_chest|spine_upper|upper_spine|torso|body|Bip001 Spine2|Bip001 Spine1|Bip01 Spine1|mixamorig:Spine2|mixamorig:Spine1"));
        bones.Add(Find("J_Neck|head|head_01|Bip001 Head|Bip01 Head|mixamorig:Head"));
        bones.Add(Find("J_L_Arm|upperarm_l|l_upperarm|upper_arm_l|upper_arm.l|arm_upper_l|upperarm_left|arm_l|l_arm|arm_left|left_arm|leftarm|Bip001 L UpperArm|Bip01 L UpperArm|mixamorig:LeftArm"));
        bones.Add(Find("lowerarm_l|l_lowerarm|forearm_l|l_forearm|forearm.l|lower_arm_l|arm_lower_l|forearm_left|lowerarm_left|elbow_l|leftforearm|Bip001 L Forearm|Bip01 L Forearm|mixamorig:LeftForeArm"));
        bones.Add(Find("J_R_Arm|upperarm_r|r_upperarm|upper_arm_r|upper_arm.r|arm_upper_r|upperarm_right|arm_r|r_arm|arm_right|right_arm|rightarm|Bip001 R UpperArm|Bip01 R UpperArm|mixamorig:RightArm"));
        bones.Add(Find("lowerarm_r|r_lowerarm|forearm_r|r_forearm|forearm.r|lower_arm_r|arm_lower_r|forearm_right|lowerarm_right|elbow_r|rightforearm|Bip001 R Forearm|Bip01 R Forearm|mixamorig:RightForeArm"));
        bones.Add(Find("J_L_Leg|thigh_l|l_thigh|thigh.l|upperleg_l|upper_leg_l|leg_upper_l|thigh_left|upleg_l|leftupleg|leg_l|l_leg|leg_left|left_leg|Bip001 L Thigh|Bip01 L Thigh|mixamorig:LeftUpLeg"));
        bones.Add(Find("calf_l|l_calf|shin_l|shin.l|lowerleg_l|lower_leg_l|leg_lower_l|calf_left|knee_l|leftleg|Bip001 L Calf|Bip01 L Calf|mixamorig:LeftLeg"));
        bones.Add(Find("J_R_Leg|thigh_r|r_thigh|thigh.r|upperleg_r|upper_leg_r|leg_upper_r|thigh_right|upleg_r|rightupleg|leg_r|r_leg|leg_right|right_leg|Bip001 R Thigh|Bip01 R Thigh|mixamorig:RightUpLeg"));
        bones.Add(Find("calf_r|r_calf|shin_r|shin.r|lowerleg_r|lower_leg_r|leg_lower_r|calf_right|knee_r|rightleg|Bip001 R Calf|Bip01 R Calf|mixamorig:RightLeg"));

        if (bones[Head] == FName.None || bones[LeftArm] == FName.None || bones[RightArm] == FName.None
            || (bones[Pelvis] == FName.None && bones[Pelvis + 1] == FName.None))
        {
            Log.Write("Couldn't find the head, arms and pelvis in this skeleton: emotes are off");
            return false;
        }
        return true;
    }

    /// <summary>The first of the '|' separated names that's a bone of the mesh (names ignore case), or None.</summary>
    FName Find(string names)
    {
        foreach (var name in UKismetStringLibrary.ParseIntoArray(names, "|", true))
            if (mesh!.GetBoneIndex(name) >= 0) return name;
        return FName.None;
    }

    /// <summary>The bone's component-space transform in the skeleton's reference pose.</summary>
    FTransform RefPose(FName bone)
    {
        var transform = mesh!.GetRefPoseTransform(mesh.GetBoneIndex(bone));
        var parent = mesh.GetParentBone(bone);
        while (parent != FName.None)
        {
            transform = UKismetMathLibrary.ComposeTransforms(transform, mesh.GetRefPoseTransform(mesh.GetBoneIndex(parent)));
            parent = mesh.GetParentBone(parent);
        }
        return transform;
    }

    FVector RefPosition(FName bone) => RefPose(bone).Translation;

    /// <summary>Works out the mesh's axes and size, and each bone's rotation in Steve's rest pose.</summary>
    bool Calibrate()
    {
        var pelvis = bones[Pelvis] != FName.None ? bones[Pelvis] : bones[Pelvis + 1];
        pelvisRest = RefPosition(pelvis);
        var head = RefPosition(bones[Head]);
        var up = UKismetMathLibrary.Normal(UKismetMathLibrary.Subtract_VectorVector(head, pelvisRest), 0.0001f);
        var right = UKismetMathLibrary.Subtract_VectorVector(RefPosition(bones[RightArm]), RefPosition(bones[LeftArm]));
        right = UKismetMathLibrary.Normal(UKismetMathLibrary.Subtract_VectorVector(right,
            UKismetMathLibrary.Multiply_VectorFloat(up, UKismetMathLibrary.Dot_VectorVector(right, up))), 0.0001f);
        var forward = UKismetMathLibrary.Cross_VectorVector(right, up);
        toMesh = UKismetMathLibrary.Conv_RotatorToQuaternion(UKismetMathLibrary.MakeRotationFromAxes(forward, right, up));
        blockSize = (float)(UKismetMathLibrary.VSize(UKismetMathLibrary.Subtract_VectorVector(head, pelvisRest)) / StevePelvisToHead);
        var down = UKismetMathLibrary.Multiply_VectorFloat(up, -1);

        rest.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            if (bones[i] == FName.None)
            {
                rest.Add(UKismetMathLibrary.Quat_Identity());
                continue;
            }
            var rotation = RefPose(bones[i]).Rotation;
            // Limbs hang straight down in Steve's rest pose: turn them there from however the skeleton holds them.
            var end = i >= FirstLimb ? LimbEnd(i) : FName.None;
            if (end != FName.None)
            {
                var direction = UKismetMathLibrary.Normal(UKismetMathLibrary.Subtract_VectorVector(RefPosition(end), RefPosition(bones[i])), 0.0001f);
                rotation = UKismetMathLibrary.Multiply_QuatQuat(UKismetMathLibrary.Quat_FindBetweenNormals(direction, down), rotation);
            }
            rest.Add(rotation);
        }

        return blockSize > 1;
    }

    /// <summary>The bone a limb bone points at: the next limb bone down, or the hand or foot.</summary>
    FName LimbEnd(int slot)
    {
        bool upper = slot % 2 == 0;
        if (upper && bones[slot + 1] != FName.None) return bones[slot + 1];
        bool arm = slot < 8;
        bool left = slot == 4 || slot == 5 || slot == 8 || slot == 9;
        if (arm && left) return Find("J_L_Hand|hand_l|l_hand|hand.l|hand_left|wrist_l|lefthand|Bip001 L Hand|Bip01 L Hand|mixamorig:LeftHand");
        if (arm) return Find("J_R_Hand|hand_r|r_hand|hand.r|hand_right|wrist_r|righthand|Bip001 R Hand|Bip01 R Hand|mixamorig:RightHand");
        if (left) return Find("J_L_Foot|foot_l|l_foot|foot.l|foot_left|ankle_l|leftfoot|Bip001 L Foot|Bip01 L Foot|mixamorig:LeftFoot");
        return Find("J_R_Foot|foot_r|r_foot|foot.r|foot_right|ankle_r|rightfoot|Bip001 R Foot|Bip01 R Foot|mixamorig:RightFoot");
    }

    /// <summary>Unpacks an emote's frames the first time it plays (see tools/convert_emotes.py for the format).</summary>
    void Decode(int index)
    {
        var mask = EmoteData.Bones(index);
        moves = EmoteData.Moves(index);
        slotData.Clear();
        animatedCount = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                slotData.Add(animatedCount);
                animatedCount++;
            }
            else slotData.Add(-1);
        }

        if (frameStarts[index] >= 0)
        {
            frameStart = frameStarts[index];
            offsetStart = offsetStarts[index];
            return;
        }
        frameStart = frames.Count;
        offsetStart = offsets.Count;
        frameStarts[index] = frameStart;
        offsetStarts[index] = offsetStart;
        var data = EmoteData.Animation(index);
        int at = 0;
        for (int frame = 0; frame < EmoteData.Frames(index); frame++)
        {
            for (int i = 0; i < animatedCount; i++)
            {
                var x = Value(data, at, 1);
                var y = Value(data, at + 2, 1);
                var z = Value(data, at + 4, 1);
                var w = UKismetMathLibrary.sqrt(UKismetMathLibrary.FMax(0, 1 - x * x - y * y - z * z));
                frames.Add(new FQuat { X = x, Y = y, Z = z, W = w });
                at += 6;
            }
            if (moves)
            {
                offsets.Add(new FVector { X = Value(data, at, EmoteData.MaxOffset), Y = Value(data, at + 2, EmoteData.MaxOffset), Z = Value(data, at + 4, EmoteData.MaxOffset) });
                at += 6;
            }
        }
    }

    static double Value(string data, int at, float limit)
    {
        int bits = (UKismetStringLibrary.GetCharacterAsNumber(data, at) - 48) * 64 + UKismetStringLibrary.GetCharacterAsNumber(data, at + 1) - 48;
        return (bits / 4095.0 * 2 - 1) * limit;
    }

    /// <summary>Poses the hidden mesh at a time in ticks.</summary>
    void Apply(double ticks)
    {
        if (pose == null) return;
        int last = EmoteData.Frames(emote) - 1;
        int frame = UKismetMathLibrary.FFloor(ticks);
        if (frame > last - 1) frame = last - 1;
        if (frame < 0) frame = 0;
        var alpha = (float)UKismetMathLibrary.FClamp(ticks - frame, 0, 1);
        int next = frame + 1 > last ? last : frame + 1;

        if (moves && bones[Pelvis] != FName.None)
        {
            var offset = UKismetMathLibrary.Multiply_VectorFloat(UKismetMathLibrary.VLerp(offsets[offsetStart + frame], offsets[offsetStart + next], alpha), blockSize);
            pose.SetBoneLocationByName(bones[Pelvis], UKismetMathLibrary.Add_VectorVector(pelvisRest, UKismetMathLibrary.Quat_RotateVector(toMesh, offset)), EBoneSpaces.ComponentSpace);
        }

        var fromMesh = UKismetMathLibrary.Quat_Inversed(toMesh);
        // Parents before children: setting a bone in component space carries its children along.
        for (int i = 0; i < SlotCount; i++)
        {
            if (bones[i] == FName.None) continue;
            var rotation = rest[i];
            var k = slotData[i];
            if (k >= 0)
            {
                var delta = UKismetMathLibrary.Quat_Slerp(frames[frameStart + frame * animatedCount + k], frames[frameStart + next * animatedCount + k], alpha);
                delta = UKismetMathLibrary.Multiply_QuatQuat(UKismetMathLibrary.Multiply_QuatQuat(toMesh, delta), fromMesh);
                rotation = UKismetMathLibrary.Multiply_QuatQuat(delta, rotation);
            }
            pose.SetBoneRotationByName(bones[i], UKismetMathLibrary.Quat_Rotator(rotation), EBoneSpaces.ComponentSpace);
        }
    }

    /// <summary>
    /// Makes the real mesh, and every mesh that follows its pose, follow the hidden posed mesh. MCDII builds armor and
    /// weapons as child actors, so their meshes are looked for there too.
    /// </summary>
    void Follow()
    {
        followers.Clear();
        borrowed.Clear();
        if (character == null || mesh == null || pose == null) return;
        pinned.Clear();
        AddFollowers(character);
        foreach (var component in character.K2_GetComponentsByClass(Unreal.ClassOf<UChildActorComponent>()))
        {
            if (component is not UChildActorComponent child || child.ChildActor == null) continue;
            var before = borrowed.Count;
            AddFollowers(child.ChildActor);
            if (borrowed.Count > before) pinned.Add(child);
        }
        foreach (var skinned in followers) skinned.SetLeaderPoseComponent(pose, true, false);

        // Sockets follow the mesh's own animation, so weapons, the cape and the rest move to the same sockets on the
        // posed mesh until the emote ends.
        attached.Clear();
        attachedSockets.Clear();
        attachedPlaces.Clear();
        mesh.GetChildrenComponents(false, out var children);
        foreach (var child in children)
        {
            if (child == null || child == pose) continue;
            attached.Add(child);
            attachedSockets.Add(child.GetAttachSocketName());
            attachedPlaces.Add(child.GetRelativeTransform());
            var socket = child.GetAttachSocketName();
            bool hide = false;
            if (socket != FName.None)
            {
                var bone = mesh.GetSocketBoneName(socket).ToString().ToLower();
                if (rootBones.Contains(mesh.GetSocketBoneName(socket)) || bone.Contains("hand")) hide = true;
            }
            if (child is UChildActorComponent cac && !pinned.Contains(cac)) hide = true;

            if (hide && child.IsVisible())
            {
                hidden.Add(child);
                child.SetVisibility(false, true);
                if (child is UChildActorComponent hideCac && hideCac.ChildActor != null)
                    hideCac.ChildActor.SetActorHiddenInGame(true);
            }
        }
        for (int i = 0; i < attached.Count; i++)
        {
            // A mesh following the pose already gets the body's movement from it: on a socket that moves too, it would
            // move twice (the armor drifting off the hips).
            if (attached[i] is UChildActorComponent child && pinned.Contains(child))
                attached[i].K2_AttachToComponent(pose, FName.None, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, EAttachmentRule.SnapToTarget, false);
            else
                attached[i].K2_AttachToComponent(pose, attachedSockets[i], EAttachmentRule.KeepRelative, EAttachmentRule.KeepRelative, EAttachmentRule.KeepRelative, false);
        }
    }

    void AddFollowers(AActor actor)
    {
        foreach (var component in actor.K2_GetComponentsByClass(Unreal.ClassOf<USkinnedMeshComponent>()))
        {
            if (component is not USkinnedMeshComponent skinned || skinned == pose) continue;
            if (skinned == mesh || skinned.LeaderPoseComponent.Get() == mesh) followers.Add(skinned);
            // The face and the armor are meshes of their own that copy the body's animation: they share the body's
            // bones, unlike the cape. The game leaves old face meshes behind, hidden.
            else if (skinned.LeaderPoseComponent.Get() == null && skinned.IsVisible() && SharesBones(skinned))
            {
                followers.Add(skinned);
                borrowed.Add(skinned);
            }
        }
    }

    bool SharesBones(USkinnedMeshComponent skinned)
    {
        for (int i = 0; i < FirstLimb; i++)
            if (bones[i] != FName.None && skinned.GetBoneIndex(bones[i]) >= 0) return true;
        return false;
    }

    /// <summary>
    /// Starts the posed mesh from the body's current pose, each bone relative to its parent so it stays on the posed body.
    /// The emote's own bones take the skeleton's rest pose (a landing squashes and bends them), and the bones below the
    /// limbs (hands, feet) its rest bend (a jump bends the knees and feet): the emote only turns its own bones, so the game's
    /// pose would stay in the others. Their size stays the game's, which shows and hides weapons by shrinking their bones.
    /// </summary>
    void CopyCurrentPose()
    {
        if (mesh == null || pose == null) return;
        FindRootBones();
        // Bones come parents first, so each parent is already in place.
        for (int i = 0; i < mesh.GetNumBones(); i++)
        {
            var name = mesh.GetBoneName(i);
            var parent = mesh.GetParentBone(name);
            var current = mesh.GetBoneTransform(name, ERelativeTransformSpace.RTS_Component);
            if (parent != FName.None)
            {
                var local = UKismetMathLibrary.MakeRelativeTransform(current, mesh.GetBoneTransform(parent, ERelativeTransformSpace.RTS_Component));
                if (bones.Contains(name)) local = mesh.GetRefPoseTransform(i);
                else if (BelowLimb(name))
                {
                    var rest = mesh.GetRefPoseTransform(i);
                    local = new FTransform { Rotation = rest.Rotation, Translation = rest.Translation, Scale3D = local.Scale3D };
                }
                current = UKismetMathLibrary.ComposeTransforms(local, pose.GetBoneTransformByName(parent, EBoneSpaces.ComponentSpace));
            }
            pose.SetBoneTransformByName(name, current, EBoneSpaces.ComponentSpace);
        }
    }

    /// <summary>Whether a bone hangs below one of the limb bones (a hand or foot), not a limb bone itself.</summary>
    bool BelowLimb(FName bone)
    {
        for (var parent = mesh!.GetParentBone(bone); parent != FName.None; parent = mesh.GetParentBone(parent))
            for (int i = FirstLimb; i < SlotCount; i++)
                if (bones[i] == parent) return true;
        return false;
    }

    /// <summary>The bones hanging off the root beside the pelvis, and everything below them.</summary>
    void FindRootBones()
    {
        rootBones.Clear();
        var pelvis = bones[Pelvis];
        if (pelvis == FName.None) return;
        var root = mesh!.GetParentBone(pelvis);
        if (root == FName.None) return;
        for (int i = 0; i < mesh.GetNumBones(); i++)
        {
            var name = mesh.GetBoneName(i);
            if (name != pelvis && mesh.GetParentBone(name) == root) rootBones.Add(name);
        }
        // Their children too (the sheathed sword's bone hangs off the scabbard's).
        for (int i = 0; i < mesh.GetNumBones(); i++)
        {
            var name = mesh.GetBoneName(i);
            if (!rootBones.Contains(name) && rootBones.Contains(mesh.GetParentBone(name))) rootBones.Add(name);
        }
    }

    /// <summary>Gives the meshes back to the game's animation.</summary>
    void Release()
    {
        foreach (var skinned in followers)
            if (skinned != null) skinned.SetLeaderPoseComponent(skinned == mesh || borrowed.Contains(skinned) ? null : mesh, true, false);
        followers.Clear();
        borrowed.Clear();
        for (int i = 0; i < attached.Count; i++)
        {
            if (attached[i] == null || mesh == null) continue;
            attached[i].K2_AttachToComponent(mesh, attachedSockets[i], EAttachmentRule.KeepRelative, EAttachmentRule.KeepRelative, EAttachmentRule.KeepRelative, false);
            attached[i].K2_SetRelativeTransform(attachedPlaces[i], false, out var hit, false);
        }
        attached.Clear();
        attachedSockets.Clear();
        attachedPlaces.Clear();
        pinned.Clear();
        foreach (var component in hidden)
        {
            if (component != null)
            {
                component.SetVisibility(true, true);
                if (component is UChildActorComponent cac && cac.ChildActor != null)
                    cac.ChildActor.SetActorHiddenInGame(false);
            }
        }
        hidden.Clear();
    }
}
