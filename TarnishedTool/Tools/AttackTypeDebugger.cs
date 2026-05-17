using System;
using System.Collections.Generic;
using TarnishedTool.Interfaces;
using TarnishedTool.Models;

namespace TarnishedTool.Tools;

/// <summary>
/// Temporary debug tool to diagnose attack type offset issues.
/// Usage: Perform attacks with different weapon types and check the raw byte values.
/// </summary>
public class AttackTypeDebugger
{
    private readonly IAttackInfoService _attackInfoService;
    private readonly List<(int Id, byte RawByte, string DisplayType)> _log = new();

    public AttackTypeDebugger(IAttackInfoService attackInfoService)
    {
        _attackInfoService = attackInfoService;
    }

    /// <summary>
    /// Call this periodically to log attack info.
    /// Returns a summary of recent attacks with their raw byte values vs displayed type.
    /// </summary>
    public string LogAttackInfo()
    {
        var attacks = _attackInfoService.PollAttackInfo();

        foreach (var attack in attacks)
        {
            var rawByte = (byte)attack.PhysicalAttackType;
            var displayType = attack.PhysicalAttackType.ToString();
            _log.Add((attack.MyId, rawByte, displayType));
        }

        if (_log.Count == 0)
            return "No attacks recorded yet.";

        var result = "=== ATTACK TYPE DEBUG LOG ===\n";
        result += "ID\t| Raw Byte | Enum Value\n";
        result += "----+----------+----------\n";

        foreach (var entry in _log)
        {
            result += $"{entry.Id}\t| 0x{entry.RawByte:X2}\t  | {entry.DisplayType}\n";
        }

        return result;
    }

    public void ClearLog()
    {
        _log.Clear();
    }
}
