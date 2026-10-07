using NeoRune;
using UE.Engine;
using UE.InputCore;

namespace Emoticons;

/// <summary>
/// Emoticons for MCDII: the emotes of mchorse's Emoticons mod. Hold the wheel key, point at an emote and let go to
/// play it (or tap the key and click one); walking stops it. The key is a setting in BetterBlueprintLoader's Mods tab.
/// </summary>
[ModSetting.Keybind(WheelKeySetting, "Emote Wheel Key", Default = DefaultWheelKey,
    Description = "Hold it to open the emote wheel, point at an emote and let go to play it. Tap it to keep the wheel open and click an emote.")]
public class ModActor : AActor, ISettingsEvents
{
    const string WheelKeySetting = "wheel_key";
    const string DefaultWheelKey = "B";
    // Letting go of the key sooner than this keeps the wheel open for clicking.
    const float TapTime = 0.3f;

    EmotePlayer? player;
    EmoteWheel? wheel;
    double openedAt;
    bool started;
    // The wheel's keys (the setting's two), and the one that opened it: letting go of that one plays the emote.
    FKey wheelKey = new FKey { KeyName = DefaultWheelKey };
    FKey secondWheelKey = new FKey();
    FKey heldKey = new FKey();

    public void OnKeybindChanged(string id, FKey key, FKey secondaryKey)
    {
        if (id != WheelKeySetting) return;
        wheelKey = key;
        secondWheelKey = secondaryKey;
    }

    public void OnSettingsReset()
    {
        wheelKey = new FKey { KeyName = DefaultWheelKey };
        secondWheelKey = new FKey();
    }

    public void OnSettingChanged(string id, string value) { }
    public void OnButtonPressed(string id) { }

    protected override void ReceiveBeginPlay()
    {
        Log.Write($"Emoticons loaded in {World.LevelName(this)}");
    }

    public override void ReceiveTick(float deltaSeconds)
    {
        if (!started) { started = true; player = EmotePlayer.Create(this); }
        player?.Update();
        var controller = World.PlayerController(this);
        if (controller == null) return;

        bool first = controller.WasInputKeyJustPressed(wheelKey);
        if (first || controller.WasInputKeyJustPressed(secondWheelKey))
        {
            heldKey = first ? wheelKey : secondWheelKey;
            if (wheel != null) wheel.Close();
            else OpenWheel();
            return;
        }
        if (wheel == null) return;

        wheel.UpdateHover();
        if (controller.WasInputKeyJustPressed(new FKey { KeyName = "MouseScrollUp" })) wheel.Turn(-1);
        if (controller.WasInputKeyJustPressed(new FKey { KeyName = "MouseScrollDown" })) wheel.Turn(1);
        if (controller.WasInputKeyJustReleased(heldKey))
        {
            if (wheel.Hovered >= 0) PlayEmote(wheel.Hovered);
            else if (wheel.StopHovered)
            {
                StopEmote();
                wheel.Close();
            }
            else if (World.RealTime(this) - openedAt > TapTime) wheel.Close();
        }
    }

    public void PlayEmote(int index)
    {
        wheel?.Close();
        player?.Play(index);
    }

    public void StopEmote() => player?.Stop();

    public void OpenWheel()
    {
        if (wheel != null || World.Player(this) == null) return;
        wheel = EmoteWheel.Open(this);
        openedAt = World.RealTime(this);
    }

    public void WheelClosed() => wheel = null;

    /// <summary>
    /// Holds the wheel being made, before it's built: building it loads the game's assets, which can let the garbage
    /// collector run, and it would destroy a wheel held only by the code making it.
    /// </summary>
        public void Holding(EmoteWheel made) => wheel = made;
}

[Asset("/Game/Mods/BlueprintLoader/BPI_ModSettings")]
public interface ISettingsEvents
{
    void OnButtonPressed(string Id);
    void OnKeybindChanged(string Id, FKey Key, FKey SecondaryKey);
    void OnSettingChanged(string Id, string Value);
    void OnSettingsReset();
}
