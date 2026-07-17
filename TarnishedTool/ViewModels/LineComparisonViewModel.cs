//

using System;
using System.Numerics;
using System.Windows.Input;
using TarnishedTool.Core;
using TarnishedTool.Interfaces;
using TarnishedTool.Models;
using TarnishedTool.Utilities;

namespace TarnishedTool.ViewModels;

public class LineComparisonViewModel : BaseViewModel
{
    private readonly IGameTickService _gameTickService;
    private readonly IPlayerService _playerService;

    private enum Phase { Idle, Armed, AtStart, Running, Finished }

    private Phase _phase = Phase.Idle;
    private Position _start;
    private Position _end;
    private uint _startIgt;
    private bool _subscribed;

    public LineComparisonViewModel(IGameTickService gameTickService, IPlayerService playerService)
    {
        _gameTickService = gameTickService;
        _playerService = playerService;

        SetStartCommand = new DelegateCommand(SetStart);
        SetEndCommand = new DelegateCommand(SetEnd);
        RestoreToStartCommand = new DelegateCommand(RestoreToStart);
        ResetCommand = new DelegateCommand(Reset);
    }

    #region Commands

    public ICommand SetStartCommand { get; }
    public ICommand SetEndCommand { get; }
    public ICommand RestoreToStartCommand { get; }
    public ICommand ResetCommand { get; }

    #endregion

    #region Properties

    private float _startRadius = 0.75f;
    public float StartRadius
    {
        get => _startRadius;
        set => SetProperty(ref _startRadius, value);
    }

    private float _endRadius = 2f;
    public float EndRadius
    {
        get => _endRadius;
        set => SetProperty(ref _endRadius, value);
    }

    private string _startText = "Not set";
    public string StartText
    {
        get => _startText;
        set => SetProperty(ref _startText, value);
    }

    private string _endText = "Not set";
    public string EndText
    {
        get => _endText;
        set => SetProperty(ref _endText, value);
    }

    private string _phaseText = "Idle";
    public string PhaseText
    {
        get => _phaseText;
        set => SetProperty(ref _phaseText, value);
    }

    private string _liveTimeText = "00:00.000";
    public string LiveTimeText
    {
        get => _liveTimeText;
        set => SetProperty(ref _liveTimeText, value);
    }

    private string _lastResultText = "--:--.---";
    public string LastResultText
    {
        get => _lastResultText;
        set => SetProperty(ref _lastResultText, value);
    }

    #endregion

    public void NotifyWindowOpen()
    {
        if (_subscribed) return;
        _gameTickService.Subscribe(Tick);
        _subscribed = true;
    }

    public void NotifyWindowClosed()
    {
        if (!_subscribed) return;
        _gameTickService.Unsubscribe(Tick);
        _subscribed = false;
    }

    private void SetStart()
    {
        try
        {
            _start = _playerService.CapturePosition();
            StartText = Describe(_start);
            ReArm();
        }
        catch { }
    }

    private void SetEnd()
    {
        try
        {
            _end = _playerService.CapturePosition();
            EndText = Describe(_end);
            ReArm();
        }
        catch { }
    }

    private void RestoreToStart()
    {
        if (_start == null) return;
        try { _playerService.RestorePos(_start); } catch { }
        ReArm();
    }

    private void Reset() => ReArm();

    private void ReArm()
    {
        if (_start != null && _end != null)
        {
            _phase = Phase.Armed;
            PhaseText = "Go to start";
        }
        else
        {
            _phase = Phase.Idle;
            PhaseText = "Set start and end";
        }
        LiveTimeText = FormatMs(0);
    }

    private void Tick()
    {
        try
        {
            if (_start == null || _end == null) return;
            var current = _playerService.CapturePosition();

            switch (_phase)
            {
                case Phase.Armed:
                    // Only becomes ready once the player is actually AT the start point.
                    // This prevents the timer from starting just because the player is
                    // standing somewhere else (e.g. right after Set End).
                    if (Distance(current, _start) <= StartRadius)
                    {
                        _phase = Phase.AtStart;
                        PhaseText = "Ready";
                    }
                    break;

                case Phase.AtStart:
                    if (Distance(current, _start) > StartRadius)
                    {
                        _startIgt = _playerService.GetIgt();
                        _phase = Phase.Running;
                        PhaseText = "Running";
                    }
                    break;

                case Phase.Running:
                    var igt = _playerService.GetIgt();
                    if (igt < _startIgt)
                    {
                        // IGT went backwards (save reload) — abort the attempt.
                        ReArm();
                        break;
                    }

                    LiveTimeText = FormatMs(igt - _startIgt);

                    if (Distance(current, _end) <= EndRadius)
                    {
                        var result = igt - _startIgt;
                        _phase = Phase.Finished;
                        PhaseText = "Finished";
                        LiveTimeText = FormatMs(result);
                        LastResultText = FormatMs(result);
                    }
                    break;
            }
        }
        catch { }
    }

    private static float Distance(Position a, Position b)
    {
        byte areaA = (byte)((a.BlockId >> 24) & 0xFF);
        byte areaB = (byte)((b.BlockId >> 24) & 0xFF);

        if (PositionUtils.IsOverworld(areaA) && PositionUtils.IsOverworld(areaB) && areaA == areaB)
            return Vector3.Distance(
                PositionUtils.ToAbsolute(a.Coords, a.BlockId),
                PositionUtils.ToAbsolute(b.Coords, b.BlockId));

        if (a.BlockId == b.BlockId)
            return Vector3.Distance(a.Coords, b.Coords);

        return float.PositiveInfinity;
    }

    private static string Describe(Position p)
    {
        byte area = (byte)((p.BlockId >> 24) & 0xFF);
        byte block = (byte)((p.BlockId >> 16) & 0xFF);
        byte region = (byte)((p.BlockId >> 8) & 0xFF);
        byte size = (byte)(p.BlockId & 0xFF);
        return $"m{area:X2}_{block:X2}_{region:X2}_{size:X2}  ({p.Coords.X:F1}, {p.Coords.Y:F1}, {p.Coords.Z:F1})";
    }

    private static string FormatMs(uint ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
