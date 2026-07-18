//

using System.Diagnostics;

namespace TarnishedTool.Utilities;

// Tracks the time since the last player teleport.
//
// Teleporting and then immediately quitting out (which triggers a save + world
// teardown on the next frame) races the game's still-settling teleport and
// crashes. Quitout/ForceSave briefly defer when a teleport just happened.
public static class TeleportGuard
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static long _lastTeleportMs = long.MinValue / 2;

    // How long the game needs to settle a teleport before a save/quit is safe.
    public const int CooldownMs = 250;

    public static void MarkTeleport() => _lastTeleportMs = Clock.ElapsedMilliseconds;

    public static long MsSinceTeleport() => Clock.ElapsedMilliseconds - _lastTeleportMs;
}
