using System.Collections.Generic;
using NeoRune;
using UE.Engine;
using UE.GameplayAbilities;
using UE.GameplayTags;
using UE.SpicewoodGAS;
using UE.SWCoreGameplay;

namespace JustKeepRollin;

/// <summary>
/// Makes rolling go the way the character is facing instead of towards the mouse cursor,
/// and lets you roll in the air.
/// </summary>
public class ModActor : AActor
{
    const float CheckInterval = 2f;

    HashSet<UGameplayAbility> patched = new();
    HashSet<FName> rollTags = new();

    protected override void ReceiveBeginPlay()
    {
        Log.Write($"JustKeepRollin loaded in {World.LevelName(this)}");
        // Abilities are granted after the character spawns, and can be granted again later
        // (respawn, gear changes), so keep checking.
        Timer.Start(this, nameof(PatchAbilities), CheckInterval, loop: true);
    }

    void PatchAbilities()
    {
        var asc = UAbilitySystemBlueprintLibrary.GetAbilitySystemComponent(World.Player(this));
        if (asc == null) return; // menus have no player character

        var abilities = new List<UGameplayAbility>();
        foreach (var spec in asc.ActivatableAbilities.Items)
        {
            if (spec.Ability != null) abilities.Add(spec.Ability);
            foreach (var instance in spec.NonReplicatedInstances) abilities.Add(instance);
            foreach (var instance in spec.ReplicatedInstances) abilities.Add(instance);
        }

        // Rolls first, so their tags are known when the jump abilities are patched.
        foreach (var ability in abilities)
            if (ability is UGA_Roll roll && !patched.Contains(roll))
            {
                patched.Add(roll);
                PatchRoll(roll);
            }
        foreach (var ability in abilities)
            if (!patched.Contains(ability) && UKismetSystemLibrary.GetObjectName(ability).Contains("Jump"))
            {
                patched.Add(ability);
                PatchJump(ability);
            }
    }

    void PatchRoll(UGA_Roll roll)
    {
        Log.Write($"Found {UKismetSystemLibrary.GetObjectName(roll)} tags={Describe(roll.AbilityTags)}");
        foreach (var tag in roll.AbilityTags.GameplayTags) rollTags.Add(tag.TagName);

        // Roll forward instead of towards the cursor.
        var directional = roll.IsDirectional;
        if (directional.Type != EKeyValueType.Bool || directional.@bool)
        {
            Log.Write($"IsDirectional type={directional.Type} bool={directional.@bool} " +
                      $"key={UBlueprintGameplayTagLibrary.GetDebugStringFromGameplayTag(directional.TypeTag)} -> forward");
            directional.Type = EKeyValueType.Bool;
            directional.@bool = false;
            roll.IsDirectional = directional;
        }

        // Allow it in the air.
        roll.ActivationRequiredTags = WithoutAirRules(roll.ActivationRequiredTags, true, "ActivationRequiredTags");
        roll.ActivationBlockedTags = WithoutAirRules(roll.ActivationBlockedTags, false, "ActivationBlockedTags");
        roll.SourceRequiredTags = WithoutAirRules(roll.SourceRequiredTags, true, "SourceRequiredTags");
        roll.SourceBlockedTags = WithoutAirRules(roll.SourceBlockedTags, false, "SourceBlockedTags");

        var selfRequire = roll.SelfRequireActivationTags;
        selfRequire.GameplayTagContainer = WithoutAirRules(selfRequire.GameplayTagContainer, true, "SelfRequireActivationTags");
        roll.SelfRequireActivationTags = selfRequire;

        var selfBlock = roll.SelfBlockActivationTags;
        selfBlock.GameplayTagContainer = WithoutAirRules(selfBlock.GameplayTagContainer, false, "SelfBlockActivationTags");
        roll.SelfBlockActivationTags = selfBlock;
    }

    /// <summary>Stops a jump ability from blocking the roll while it's active.</summary>
    void PatchJump(UGameplayAbility jump)
    {
        var kept = new List<FGameplayTag>();
        bool changed = false;
        foreach (var tag in jump.BlockAbilitiesWithTag.GameplayTags)
        {
            if (rollTags.Contains(tag.TagName)) changed = true;
            else kept.Add(tag);
        }
        Log.Write($"Found {UKismetSystemLibrary.GetObjectName(jump)} blocks={Describe(jump.BlockAbilitiesWithTag)}");
        if (!changed) return;
        jump.BlockAbilitiesWithTag = UBlueprintGameplayTagLibrary.MakeGameplayTagContainerFromArray(kept);
        Log.Write($"-> no longer blocks the roll");
    }

    /// <summary>
    /// Removes the tags that tie the roll to the ground: "on ground" tags from a required list,
    /// and air, jump and fall tags from a blocked list.
    /// </summary>
    FGameplayTagContainer WithoutAirRules(FGameplayTagContainer tags, bool required, string what)
    {
        var kept = new List<FGameplayTag>();
        var removed = "";
        foreach (var tag in tags.GameplayTags)
        {
            if (IsAirRule(tag.TagName.ToString(), required)) removed += $"{tag.TagName}";
            else kept.Add(tag);
        }
        if (removed == "") return tags;
        Log.Write($"{what}: removed{removed}");
        return UBlueprintGameplayTagLibrary.MakeGameplayTagContainerFromArray(kept);
    }

    static bool IsAirRule(string tag, bool required)
    {
        var name = tag.ToLower();
        if (required) return name.Contains("onground") || name.Contains("grounded");
        return name.Contains("inair") || name.Contains("airborne") || name.Contains("falling")
            || name.Contains("jump") || name.Contains("movementz");
    }

    static string Describe(FGameplayTagContainer tags) =>
        UBlueprintGameplayTagLibrary.GetDebugStringFromGameplayTagContainer(tags);
}
