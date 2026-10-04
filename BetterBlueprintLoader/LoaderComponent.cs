using NeoRune;
using UE.Engine;

namespace BetterBlueprintLoader;

/// <summary>
/// What the game adds to the player controller when a level loads (see the R1 hook in tools/BetterBlueprintLoaderHook):
/// it puts a ModManager in the level, which starts the mods.
/// </summary>
public class LoaderComponent : UActorComponent
{
    public override void ReceiveBeginPlay()
    {
        // The game adds this to every player controller, and a host has one per player: mods start once, for the
        // player on this computer.
        if (GetOwner() is not APlayerController controller || !controller.IsLocalPlayerController()) return;
        if (World.FindAll(controller, Unreal.ClassOf<ModManager>()).Count > 0) return;
        World.Spawn(controller, Unreal.ClassOf<ModManager>(), new UE.CoreUObject.FVector());
    }
}
