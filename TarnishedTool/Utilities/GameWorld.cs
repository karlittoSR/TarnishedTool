//

using TarnishedTool.Interfaces;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.Utilities;

// Anything that moves the player — a coordinate restore or a warp — needs a world
// that is actually there. Two distinct states must be rejected, and the obvious
// null-pointer check only covers the first:
//   * main menu: WorldChrMan.PlayerIns is null;
//   * loading screen (typically right after a quitout): the player pointer can
//     already be non-null while the world is still being built, so a restore
//     writes into a half-initialised struct and the warp shellcode calls game
//     functions that dereference it. That is the crash.
// The fade-screen bit is the game's own "a load is covering the screen" flag.
public static class GameWorld
{
    public static bool IsReady(IMemoryService memoryService)
    {
        var worldChrMan = memoryService.Read<nint>(WorldChrMan.Base);
        if (worldChrMan == 0) return false;

        if (memoryService.Read<nint>(worldChrMan + WorldChrMan.PlayerIns) == 0) return false;

        var menuMan = memoryService.Read<nint>(MenuMan.Base);
        if (menuMan == 0) return false;

        return !memoryService.IsBitSet(menuMan + MenuMan.IsFading, (int)MenuMan.FadeBitFlags.IsFadeScreen);
    }
}
