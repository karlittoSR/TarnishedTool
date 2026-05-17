// 

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using TarnishedTool.Core;
using TarnishedTool.Models;

namespace TarnishedTool.ViewModels;

public class AttackInfoViewModel : BaseViewModel
{
    private const int MaxVisibleEntries = 500;
    private readonly List<AttackInfoEntry> _fullHistory = new();
    public ObservableCollection<AttackInfoEntry> VisibleLog { get; } = new();

    // DPS tracking
    private DateTime? _fightStartTime;
    private bool _fightFinished;
    private int _totalDamageDealt;
    private nint _sessionChrIns;

    private double _fightDps;
    public double FightDps
    {
        get => _fightDps;
        private set => SetProperty(ref _fightDps, value);
    }

    private int _fightTotalDamage;
    public int FightTotalDamage
    {
        get => _fightTotalDamage;
        private set => SetProperty(ref _fightTotalDamage, value);
    }

    private string _fightDuration = "---";
    public string FightDuration
    {
        get => _fightDuration;
        private set => SetProperty(ref _fightDuration, value);
    }

    private string _fightDpsText = "---";
    public string FightDpsText
    {
        get => _fightDpsText;
        private set => SetProperty(ref _fightDpsText, value);
    }

    private string _fightTotalDamageText = "---";
    public string FightTotalDamageText
    {
        get => _fightTotalDamageText;
        private set => SetProperty(ref _fightTotalDamageText, value);
    }

    private bool _isFightActive;
    public bool IsFightActive
    {
        get => _isFightActive;
        private set => SetProperty(ref _isFightActive, value);
    }

    public AttackInfoViewModel()
    {
        ClearCommand = new DelegateCommand(Clear);
        ExportCommand = new DelegateCommand(Export);
    }

    #region Commands

    public ICommand ClearCommand { get; }
    public ICommand ExportCommand { get; }

    #endregion


    #region Public Methods

    public void AddAttacks(List<AttackInfo> attacks)
    {
        foreach (var attack in attacks)
        {
            AddAttack(attack);
        }
    }

    public void NotifyTarget(int currentHp)
    {
        if (_fightStartTime == null || _fightFinished) return;
        if (currentHp > 0) return;

        RefreshDpsDisplay();
        _fightFinished = true;
        IsFightActive = false;
    }

    // Fallback for when the attack hook doesn't fire (e.g. AI disabled):
    // accumulate damage from raw HP drops between ticks.
    private int _lastKnownHp = -1;

    public void NotifyHpDelta(int currentHp, int maxHp)
    {
        if (_fightFinished || maxHp <= 0) return;

        if (_lastKnownHp < 0)
        {
            _lastKnownHp = currentHp;
            return;
        }

        int delta = _lastKnownHp - currentHp;
        _lastKnownHp = currentHp;

        if (delta <= 0) return;

        // Start session on first HP drop
        if (_fightStartTime == null)
        {
            _fightStartTime = DateTime.UtcNow;
            _totalDamageDealt = 0;
            IsFightActive = true;
        }

        _totalDamageDealt += delta;
        RefreshDpsDisplay();

        // Freeze on kill
        if (currentHp <= 0)
        {
            RefreshDpsDisplay();
            _fightFinished = true;
            IsFightActive = false;
            _lastKnownHp = -1;
        }
    }

    // Called every tick with the current locked-on chrIns.
    // Returns true if this chrIns is the active session target (callers can skip damage calls otherwise).
    public bool NotifyTargetChanged(nint chrIns)
    {
        if (_fightFinished || _fightStartTime == null)
        {
            if (_sessionChrIns != chrIns)
            {
                // Different target seen after session ended — clear and lock to the new one
                ResetSession();
                _sessionChrIns = chrIns;
            }
            return true;
        }

        // Active session — only allow the original session target
        return chrIns == _sessionChrIns;
    }

    #endregion

    #region Private Methods

    private void AddAttack(AttackInfo attack)
    {
        // Skip attacks with 0 damage
        if (attack.TotalDamage <= 0)
            return;

        // Start a new fight session on the first hit only if no session is active
        if (_fightStartTime == null && !_fightFinished && _sessionChrIns != 0)
        {
            _fightStartTime = DateTime.UtcNow;
            _totalDamageDealt = 0;
            IsFightActive = true;
        }

        if (!_fightFinished)
        {
            _totalDamageDealt += attack.TotalDamage;
            RefreshDpsDisplay();
        }

        var entry = new AttackInfoEntry(attack);
        _fullHistory.Add(entry);

        Application.Current.Dispatcher.Invoke(() =>
        {
            VisibleLog.Insert(0, entry);

            if (VisibleLog.Count > MaxVisibleEntries)
                VisibleLog.RemoveAt(VisibleLog.Count - 1);
        });
    }

    public void RefreshDpsDisplay()
    {
        if (_fightStartTime == null || _fightFinished) return;

        double elapsed = (DateTime.UtcNow - _fightStartTime.Value).TotalSeconds;
        if (elapsed <= 0) return;

        double dps = _totalDamageDealt / elapsed;
        FightDps = dps;
        FightTotalDamage = _totalDamageDealt;
        FightDuration = $"{elapsed:F1}s";
        FightDpsText = $"{dps:F0}";
        FightTotalDamageText = $"{_totalDamageDealt}";
    }

    private void Clear()
    {
        _fullHistory.Clear();
        VisibleLog.Clear();
        ResetSession();
    }

    public void ResetSession()
    {
        _fightStartTime = null;
        _fightFinished = false;
        _totalDamageDealt = 0;
        _lastKnownHp = -1;
        _sessionChrIns = 0;
        IsFightActive = false;
        FightDps = 0;
        FightTotalDamage = 0;
        FightDuration = "---";
        FightDpsText = "---";
        FightTotalDamageText = "---";
    }
    
    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"AttackLog_{DateTime.Now:yyyyMMdd_HHmmss}",
            DefaultExt = ".csv",
            Filter = "CSV files (*.csv)|*.csv"
        };

        if (dialog.ShowDialog() != true) return;

        var sb = new StringBuilder();
        sb.AppendLine("Id,EnemyId,AttackType,TotalDamage,Physical,Fire,Magic,Lightning,Holy,RawPhysical,RawFire,RawMagic,RawLightning,RawHoly,PoiseDamage,RawSplit,FinalSplit");

        foreach (var e in _fullHistory)
        {
            sb.AppendLine($"{e.Id},{e.EnemyId},{e.AttackType},{e.TotalDamage},{e.PhysicalDamage},{e.FireDamage},{e.MagicDamage},{e.LightningDamage},{e.HolyDamage},{e.RawPhysicalDamage:F2},{e.RawFireDamage:F2},{e.RawMagicDamage:F2},{e.RawLightningDamage:F2},{e.RawHolyDamage:F2},{e.PoiseDamage:F2},{e.RawSplitText},{e.FinalSplitText}");
        }

        File.WriteAllText(dialog.FileName, sb.ToString());
    }

    #endregion
}