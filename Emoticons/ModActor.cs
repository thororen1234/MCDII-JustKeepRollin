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
            // Pressed again while the wheel is open: plays the emote pointed at, or else closes the wheel.
            if (wheel != null && wheel.Hovered >= 0) PlayEmote(wheel.Hovered);
            else if (wheel != null) wheel.Close();
            else OpenWheel();
            return;
        }
        if (wheel == null) return;

        // A controller points with either stick (the one pushed further), turns pages with the D-pad and stops with its
        // down: its face buttons and bumpers do things in the game while the wheel is open.
        bool pad = UsingController();
        wheel.ShowHints(pad);
        if (pad) PointWithStick(controller);
        else wheel.UpdateHover();
        if (controller.WasInputKeyJustPressed(new FKey { KeyName = "MouseScrollUp" }) || (pad && controller.WasInputKeyJustPressed(new FKey { KeyName = "Gamepad_DPad_Left" }))) wheel.Turn(-1);
        if (controller.WasInputKeyJustPressed(new FKey { KeyName = "MouseScrollDown" }) || (pad && controller.WasInputKeyJustPressed(new FKey { KeyName = "Gamepad_DPad_Right" }))) wheel.Turn(1);
        if (pad && controller.WasInputKeyJustPressed(new FKey { KeyName = "Gamepad_DPad_Down" }))
        {
            StopEmote();
            wheel.Close();
            return;
        }
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

    void PointWithStick(APlayerController controller)
    {
        var rightX = controller.GetInputAnalogKeyState(new FKey { KeyName = "Gamepad_RightX" });
        var rightY = controller.GetInputAnalogKeyState(new FKey { KeyName = "Gamepad_RightY" });
        var leftX = controller.GetInputAnalogKeyState(new FKey { KeyName = "Gamepad_LeftX" });
        var leftY = controller.GetInputAnalogKeyState(new FKey { KeyName = "Gamepad_LeftY" });
        if (rightX * rightX + rightY * rightY >= leftX * leftX + leftY * leftY) wheel?.Point(rightX, rightY);
        else wheel?.Point(leftX, leftY);
    }

    /// <summary>Whether the player is using a controller now (the game shows controller buttons).</summary>
    bool UsingController()
    {
        var input = USubsystemBlueprintLibrary.GetLocalPlayerSubSystemFromPlayerController(World.PlayerController(this), Unreal.ClassOf<UE.CommonInput.UCommonInputSubsystem>()) as UE.CommonInput.UCommonInputSubsystem;
        return input != null && input.GetCurrentInputType() == UE.CommonInput.ECommonInputType.Gamepad;
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
