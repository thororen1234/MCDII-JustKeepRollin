using NeoRune;
using UE.Engine;
using UE.InputCore;

namespace Emoticons;

/// <summary>
/// Emoticons for MCDII: the emotes of mchorse's Emoticons mod. Hold the wheel key, point at an emote and let go to
/// play it (or tap the key and click one); walking stops it.
/// </summary>
public class ModActor : AActor
{
    const string WheelKey = "B";
    // Letting go of the key sooner than this keeps the wheel open for clicking.
    const float TapTime = 0.3f;

    EmotePlayer? player;
    EmoteWheel? wheel;
    double openedAt;

    protected override void ReceiveBeginPlay()
    {
        Log.Write($"Emoticons loaded in {World.LevelName(this)}");
        player = EmotePlayer.Create(this);
    }

    public override void ReceiveTick(float deltaSeconds)
    {
        player?.Update();
        var controller = World.PlayerController(this);
        if (controller == null) return;

        var key = new FKey { KeyName = WheelKey };
        if (controller.WasInputKeyJustPressed(key))
        {
            if (wheel != null) wheel.Close();
            else OpenWheel();
            return;
        }
        if (wheel == null) return;

        wheel.UpdateHover();
        if (controller.WasInputKeyJustPressed(new FKey { KeyName = "MouseScrollUp" })) wheel.Turn(-1);
        if (controller.WasInputKeyJustPressed(new FKey { KeyName = "MouseScrollDown" })) wheel.Turn(1);
        if (controller.WasInputKeyJustReleased(key))
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
}
