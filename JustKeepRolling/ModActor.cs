using System.Collections.Generic;
using NeoRune;
using UE.Engine;
using UE.GameplayAbilities;
using UE.GameplayTags;
using UE.SpicewoodGAS;

namespace JustKeepRolling;

/// <summary>
/// Lets you roll in the air.
/// </summary>
public class ModActor : AActor
{
    const float CheckInterval = 2f;

    readonly HashSet<UGameplayAbility> patched = [];
    readonly HashSet<FName> rollTags = [];

    protected override void ReceiveBeginPlay()
    {
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
        foreach (var tag in roll.AbilityTags.GameplayTags) rollTags.Add(tag.TagName);

        // Allow it in the air.
        roll.ActivationRequiredTags = WithoutAirRules(roll.ActivationRequiredTags, true);
        roll.ActivationBlockedTags = WithoutAirRules(roll.ActivationBlockedTags, false);
        roll.SourceRequiredTags = WithoutAirRules(roll.SourceRequiredTags, true);
        roll.SourceBlockedTags = WithoutAirRules(roll.SourceBlockedTags, false);

        var selfRequire = roll.SelfRequireActivationTags;
        selfRequire.GameplayTagContainer = WithoutAirRules(selfRequire.GameplayTagContainer, true);
        roll.SelfRequireActivationTags = selfRequire;

        var selfBlock = roll.SelfBlockActivationTags;
        selfBlock.GameplayTagContainer = WithoutAirRules(selfBlock.GameplayTagContainer, false);
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
        if (!changed) return;
        jump.BlockAbilitiesWithTag = UBlueprintGameplayTagLibrary.MakeGameplayTagContainerFromArray(kept);
    }

    /// <summary>
    /// Removes the tags that tie the roll to the ground: "on ground" tags from a required list,
    /// and air, jump and fall tags from a blocked list.
    /// </summary>
    static FGameplayTagContainer WithoutAirRules(FGameplayTagContainer tags, bool required)
    {
        var kept = new List<FGameplayTag>();
        bool changed = false;
        foreach (var tag in tags.GameplayTags)
        {
            if (IsAirRule(tag.TagName.ToString(), required)) changed = true;
            else kept.Add(tag);
        }
        if (!changed) return tags;
        return UBlueprintGameplayTagLibrary.MakeGameplayTagContainerFromArray(kept);
    }

    static bool IsAirRule(string tag, bool required)
    {
        var name = tag.ToLower();
        if (required) return name.Contains("onground") || name.Contains("grounded");
        return name.Contains("inair") || name.Contains("airborne") || name.Contains("falling")
            || name.Contains("jump") || name.Contains("movementz");
    }
}
