//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using TarnishedTool.Memory;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.Utilities;

// A handful of addresses have no AOB pattern: they live only in the per-version
// tables in Offsets.cs, so on a game build the tool has no table for yet they
// cannot be found at all. The features behind them cannot run -- these flags let
// their controls grey out instead of staying clickable and doing nothing.
//
// Everything is a live read of the resolved address, and the addresses are only
// known once the version table or the fallback pattern scan has run: call
// Refresh() then so the bindings pick the new values up.
public static class FeatureSupport
{
    public static event EventHandler<PropertyChangedEventArgs> StaticPropertyChanged;

    public static bool IsFpsCapSupported => Patches.FpsCap != IntPtr.Zero;

    public static bool IsLockHpSupported => Hooks.PlayerLockHp != 0;

    public static bool IsFreezeHealthSupported => Hooks.NoHeal != 0;

    public static bool IsAiScriptSupported =>
        Functions.LuaDoString != 0 && WorldAiManagerImp.Base != IntPtr.Zero;

    // The entry's map coordinates are produced by a game function, so without it
    // there is no destination to warp to.
    public static bool IsChrInsWarpSupported => Functions.LocalToMapCoords != 0;

    // Not a feature of its own: it is the refill step inside the Rest actions, so
    // nothing gets greyed out for it. Worth naming when listing what is missing.
    public static bool IsRefreshFromStorageSupported => Functions.RefreshFromStorage != 0;

    // Human names for what cannot run on the current game build, for the notice
    // the user actually reads -- raw address symbols mean nothing to a runner.
    public static IReadOnlyList<string> UnavailableFeatures()
    {
        var unavailable = new List<string>();

        if (!IsFpsCapSupported) unavailable.Add("Set FPS cap");
        if (!IsLockHpSupported) unavailable.Add("Lock HP");
        if (!IsFreezeHealthSupported) unavailable.Add("Freeze Health");
        if (!IsAiScriptSupported) unavailable.Add("Inject AI Script");
        if (!IsChrInsWarpSupported) unavailable.Add("Warp to an entity, in the ChrIns window");
        if (!IsRefreshFromStorageSupported) unavailable.Add("The refill step of the Rest actions");

        return unavailable;
    }

    public static void Refresh()
    {
        Raise(nameof(IsFpsCapSupported));
        Raise(nameof(IsLockHpSupported));
        Raise(nameof(IsFreezeHealthSupported));
        Raise(nameof(IsAiScriptSupported));
        Raise(nameof(IsChrInsWarpSupported));
    }

    private static void Raise(string propertyName) =>
        StaticPropertyChanged?.Invoke(null, new PropertyChangedEventArgs(propertyName));
}
