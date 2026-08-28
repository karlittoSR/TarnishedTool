// 

using System;
using TarnishedTool.Interfaces;
using TarnishedTool.Memory;
using TarnishedTool.Services;

namespace TarnishedTool.Utilities;

public static class PatchManager
{
    // The running game's file version, e.g. "2.7.0.0". Kept so the address report
    // and the unknown-version notice can name the build that was actually found.
    public static string DetectedFileVersion { get; private set; }

    public static bool Initialize(IMemoryService memoryService)
    {
        if (memoryService.TargetProcess == null) return false;
        var module = memoryService.TargetProcess.MainModule;
        var fileVersion = module?.FileVersionInfo.FileVersion;
        var moduleBase = memoryService.BaseAddress;

        DetectedFileVersion = fileVersion;
        
        Console.WriteLine($@"Patch: {fileVersion}");

        return Offsets.Initialize(fileVersion, moduleBase);
    }
}