using NeoRune;
using UE.Engine;
using UE.GameplayAbilities;
using UE.InputCore;
using UE.SpicewoodGAS;

namespace CurrencyLimits;

/// <summary>
/// Currency Limits: raises how many Emeralds and Echo Shards you can carry, to the amounts set with the sliders in
/// BetterBlueprintLoader's Mods tab.
/// </summary>
[ModSetting.Text("How much of each currency you can carry. All the way left is the game's own cap. "
    + "Lowering a cap never takes currency away: it stays at least what you carry.")]
[ModSetting.Slider(EmeraldsSetting, "Emerald Cap", Default = MaxEmeralds, Min = GameEmeralds, Max = MaxEmeralds,
    Step = EmeraldsStep, Description = "The game's cap is 99,000.")]
[ModSetting.Slider(EchoShardsSetting, "Echo Shard Cap", Default = MaxEchoShards, Min = GameEchoShards, Max = MaxEchoShards,
    Step = EchoShardsStep, Description = "The game's cap is 100.")]
public class ModActor : AActor, ISettingsEvents
{
    const string EmeraldsSetting = "emeralds_cap";
    const string EchoShardsSetting = "echo_shards_cap";
    // The game's own caps.
    const double GameEmeralds = 99000;
    const double GameEchoShards = 100;
    // Currency amounts are 32-bit floats, exact for whole numbers up to 16,777,216 (2^24): the highest step at or under it.
    const double EmeraldsStep = 1000;
    const double EchoShardsStep = 100;
    const double MaxEmeralds = 16777000;
    const double MaxEchoShards = 16777200;
    // The attribute set comes back with the game's caps when the character spawns again: this often, they're set again.
    const float CheckInterval = 2f;

    // The sliders; BetterBlueprintLoader only sends a setting when it isn't on its default.
    double emeraldsCap = MaxEmeralds;
    double echoShardsCap = MaxEchoShards;
    double nextCheck;

    public override void ReceiveTick(float deltaSeconds)
    {
        var now = World.RealTime(this);
        if (now < nextCheck) return;
        nextCheck = now + CheckInterval;
        Apply();
    }

    void Apply()
    {
        var asc = UAbilitySystemBlueprintLibrary.GetAbilitySystemComponent(World.Player(this));
        if (asc == null) return; // menus have no player character

        foreach (var set in asc.SpawnedAttributes)
            if (set is UATR_Currency currency)
            {
                var emeralds = Capped(currency.EmeraldsMax, emeraldsCap, currency.Emeralds.CurrentValue);
                if (emeralds.BaseValue != currency.EmeraldsMax.BaseValue || emeralds.CurrentValue != currency.EmeraldsMax.CurrentValue)
                    currency.EmeraldsMax = emeralds;

                var echoShards = Capped(currency.SpringStoneMax, echoShardsCap, currency.SpringStone.CurrentValue);
                if (echoShards.BaseValue != currency.SpringStoneMax.BaseValue || echoShards.CurrentValue != currency.SpringStoneMax.CurrentValue)
                    currency.SpringStoneMax = echoShards;
            }
    }

    /// <summary>The cap at <paramref name="cap"/>, but never under what's carried (lowering it would take currency away).</summary>
    static FGameplayAttributeData Capped(FGameplayAttributeData max, double cap, float carried)
    {
        var value = (float)(cap > carried ? cap : carried);
        max.BaseValue = value;
        max.CurrentValue = value;
        return max;
    }

    public void OnSettingChanged(string id, string value)
    {
        if (id == EmeraldsSetting) emeraldsCap = UKismetMathLibrary.FClamp(ModSettings.ToNumber(value), GameEmeralds, MaxEmeralds);
        else if (id == EchoShardsSetting) echoShardsCap = UKismetMathLibrary.FClamp(ModSettings.ToNumber(value), GameEchoShards, MaxEchoShards);
        else return;
        nextCheck = 0; // set the caps on the next tick
    }

    public void OnSettingsReset()
    {
        emeraldsCap = MaxEmeralds;
        echoShardsCap = MaxEchoShards;
        nextCheck = 0;
    }

    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey) { }
    public void OnButtonPressed(string id) { }
}

[Asset("/Game/Mods/BlueprintLoader/BPI_ModSettings")]
public interface ISettingsEvents
{
    void OnButtonPressed(string Id);
    void OnKeybindChanged(string Id, FKey Key, FKey SecondaryKey);
    void OnSettingChanged(string Id, string Value);
    void OnSettingsReset();
}
