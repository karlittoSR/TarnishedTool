//

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TarnishedTool.Core;
using TarnishedTool.Enums;
using TarnishedTool.Interfaces;
using TarnishedTool.Models;
using TarnishedTool.Utilities;
using TarnishedTool.Views.Windows;

namespace TarnishedTool.ViewModels;

public class LineComparisonViewModel : BaseViewModel
{
    private readonly IGameTickService _gameTickService;
    private readonly IPlayerService _playerService;

    private enum Phase { Idle, Armed, AtStart, Running, Finished }

    private const int MaxAttempts = 10;
    private const int MaxNameLength = 44;
    private const float MinRadius = 0.1f;

    private Phase _phase = Phase.Idle;
    private Position _start;
    private Position _end;
    private uint _startIgt;
    private bool _subscribed;
    private int _attemptCounter;
    private readonly DispatcherTimer _feedbackTimer;

    private readonly SavedLinesViewModel _savedLinesViewModel;
    private SavedLinesWindow _savedLinesWindow;
    private SavedLine _activeSavedLine;

    // Raised when a recorded attempt beats the previous best (never on the first attempt).
    public event Action NewBest;

    public LineComparisonViewModel(IGameTickService gameTickService, IPlayerService playerService,
        IStateService stateService)
    {
        _gameTickService = gameTickService;
        _playerService = playerService;

        SetStartCommand = new DelegateCommand(SetStart);
        SetEndCommand = new DelegateCommand(SetEnd);
        RestoreToStartCommand = new DelegateCommand(RestoreToStart);
        ClearResultsCommand = new DelegateCommand(ClearResults);
        CopyResultsCommand = new DelegateCommand(CopyResults);
        RemoveSelectedCommand = new DelegateCommand(RemoveSelected);
        ExportPositionsCommand = new DelegateCommand(ExportPositions);
        ImportPositionsCommand = new DelegateCommand(ImportPositions);
        OpenSavedLinesCommand = new DelegateCommand(OpenSavedLines);

        _savedLinesViewModel = new SavedLinesViewModel(this);

        Attempts.CollectionChanged += (_, _) => RecomputeDeltas();

        stateService.Subscribe(State.Loaded, () => CanOperate = true);
        stateService.Subscribe(State.NotLoaded, () => CanOperate = false);

        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _feedbackTimer.Tick += (_, _) => { FeedbackText = ""; _feedbackTimer.Stop(); };
    }

    #region Commands

    public ICommand SetStartCommand { get; }
    public ICommand SetEndCommand { get; }
    public ICommand RestoreToStartCommand { get; }
    public ICommand ClearResultsCommand { get; }
    public ICommand CopyResultsCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
    public ICommand ExportPositionsCommand { get; }
    public ICommand ImportPositionsCommand { get; }
    public ICommand OpenSavedLinesCommand { get; }

    #endregion

    public ObservableCollection<LineComparisonAttempt> Attempts { get; } = new();

    #region Properties

    private bool _canOperate;
    public bool CanOperate
    {
        get => _canOperate;
        set => SetProperty(ref _canOperate, value);
    }

    private bool _canExport;
    public bool CanExport
    {
        get => _canExport;
        set => SetProperty(ref _canExport, value);
    }

    private string _feedbackText = "";
    public string FeedbackText
    {
        get => _feedbackText;
        set => SetProperty(ref _feedbackText, value);
    }

    private string _targetPbText = "";
    public string TargetPbText
    {
        get => _targetPbText;
        set => SetProperty(ref _targetPbText, value);
    }

    private float _startRadius = 1f;
    public float StartRadius
    {
        get => _startRadius;
        set
        {
            if (SetProperty(ref _startRadius, value < MinRadius ? MinRadius : value))
            {
                ClearResults(); // trigger zone changed — old attempts no longer comparable
                DetachSavedLine();
            }
        }
    }

    private float _endRadius = 3f;
    public float EndRadius
    {
        get => _endRadius;
        set
        {
            if (SetProperty(ref _endRadius, value < MinRadius ? MinRadius : value))
            {
                ClearResults();
                DetachSavedLine();
            }
        }
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

    private string _nextAttemptName = "";
    public string NextAttemptName
    {
        get => _nextAttemptName;
        set => SetProperty(ref _nextAttemptName, value);
    }

    private LineComparisonAttempt _selectedAttempt;
    public LineComparisonAttempt SelectedAttempt
    {
        get => _selectedAttempt;
        set => SetProperty(ref _selectedAttempt, value);
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

    public void SetStart()
    {
        try
        {
            _start = _playerService.CapturePosition();
            StartText = Describe(_start);
            ClearResults(); // line definition changed — old attempts no longer comparable
            DetachSavedLine();
            ReArm();
        }
        catch { }
    }

    public void SetEnd()
    {
        try
        {
            _end = _playerService.CapturePosition();
            EndText = Describe(_end);
            ClearResults();
            DetachSavedLine();
            ReArm();
        }
        catch { }
    }

    public void RestoreToStart()
    {
        if (_start == null) return;
        try { _playerService.RestorePos(_start); } catch { }
        ReArm();
    }

    private void ExportPositions()
    {
        if (_start == null || _end == null) return;
        var code = LineShareCodec.Encode(_start, StartRadius, _end, EndRadius);
        try
        {
            Clipboard.SetText(code);
            ShowFeedback("Positions copied to clipboard");
        }
        catch { }
    }

    private void ShowFeedback(string message)
    {
        FeedbackText = message;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private void ImportPositions()
    {
        string text;
        try { text = Clipboard.GetText(); } catch { text = null; }

        if (!ApplyCode(text))
            MsgBox.Show("Invalid line code. Paste a code produced by the \"Export position\" button.");
    }

    // Applies a shared line code (start/end + radii). Returns false if invalid.
    // Detaches from any active saved line (a raw/clipboard code isn't a library entry).
    public bool ApplyCode(string code)
    {
        if (!LineShareCodec.TryDecode(code, out var start, out var startRadius, out var end, out var endRadius))
            return false;

        _start = start;
        _end = end;
        StartText = Describe(_start);
        EndText = Describe(_end);
        // Set fields directly so the radius setters don't clear twice.
        SetProperty(ref _startRadius, startRadius < MinRadius ? MinRadius : startRadius, nameof(StartRadius));
        SetProperty(ref _endRadius, endRadius < MinRadius ? MinRadius : endRadius, nameof(EndRadius));
        ClearResults(); // new line definition
        DetachSavedLine();
        ReArm();
        ShowFeedback("Line imported");
        return true;
    }

    // Loads a library entry: applies its code, tracks it as active (so its PB
    // auto-updates), seeds the PB as "Attempt 1" (a target to beat), and shows
    // the PB in the main window.
    public bool LoadSavedLine(SavedLine line)
    {
        if (line == null || !ApplyCode(line.Code)) return false;

        _activeSavedLine = line;
        if (line.BestMs > 0)
        {
            _attemptCounter = 1;
            Attempts.Add(new LineComparisonAttempt(1, "PB", line.BestMs));
        }
        UpdateTargetPb();
        return true;
    }

    // The current start/end as a shareable code, or null if not both set.
    public string ExportCurrentCode() =>
        _start != null && _end != null ? LineShareCodec.Encode(_start, StartRadius, _end, EndRadius) : null;

    // Best (gold) time currently in the table, or 0 if none.
    public uint GetCurrentBestMs() => Attempts.Count == 0 ? 0 : Attempts.Min(a => a.ResultMs);

    // Marks a saved line as active so subsequent attempts update its PB live.
    // Used right after Save current, so a freshly saved line starts tracking
    // immediately (the first boss time attaches to it).
    public void SetActiveSavedLine(SavedLine line)
    {
        _activeSavedLine = line;
        UpdateTargetPb();
    }

    private void DetachSavedLine()
    {
        _activeSavedLine = null;
        UpdateTargetPb();
    }

    private void UpdateTargetPb() =>
        TargetPbText = _activeSavedLine != null && _activeSavedLine.BestMs > 0
            ? $"PB: {FormatMs(_activeSavedLine.BestMs)}"
            : "";

    private void OpenSavedLines()
    {
        if (_savedLinesWindow != null && _savedLinesWindow.IsVisible)
        {
            _savedLinesWindow.Activate();
            return;
        }

        _savedLinesWindow = new SavedLinesWindow { DataContext = _savedLinesViewModel };
        _savedLinesWindow.Closed += (_, _) => _savedLinesWindow = null;
        _savedLinesWindow.Show();
        _savedLinesWindow.Activate();
        _savedLinesWindow.Focus();
    }

    private void ClearResults()
    {
        Attempts.Clear();
        _attemptCounter = 0;
    }

    private void RemoveSelected()
    {
        if (SelectedAttempt != null)
            Attempts.Remove(SelectedAttempt);
    }

    private void CopyResults()
    {
        if (Attempts.Count == 0 && _start == null && _end == null) return;

        var sb = new StringBuilder();

        // Positions this table belongs to, captured at copy time.
        sb.AppendLine($"Start: {(_start != null ? Describe(_start) : "Not set")}");
        sb.AppendLine($"End:   {(_end != null ? Describe(_end) : "Not set")}");
        if (_start != null && _end != null)
            sb.AppendLine($"Code:  {LineShareCodec.Encode(_start, StartRadius, _end, EndRadius)}");
        sb.AppendLine();

        sb.AppendLine("#\tName\tResult\tDelta");
        foreach (var a in Attempts)
        {
            // Strip the leading '+' so spreadsheets don't read the delta as a formula.
            var delta = a.DeltaText.TrimStart('+');
            sb.AppendLine($"{a.Number}\t{a.Name}\t{a.ResultText}\t{delta}");
        }
        try
        {
            Clipboard.SetText(sb.ToString());
            ShowFeedback("Results copied to clipboard");
        }
        catch { }
    }

    private void RecordAttempt(uint result)
    {
        // Capture the previous best before adding, to detect a new record.
        var hadAttempts = Attempts.Count > 0;
        var prevBest = hadAttempts ? Attempts.Min(a => a.ResultMs) : uint.MaxValue;

        var number = ++_attemptCounter;
        var name = string.IsNullOrWhiteSpace(NextAttemptName) ? $"Attempt {number}" : NextAttemptName.Trim();
        if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength);

        Attempts.Add(new LineComparisonAttempt(number, name, result));

        // Keep only the best MaxAttempts rows: when full, drop the slowest.
        while (Attempts.Count > MaxAttempts)
        {
            var worst = Attempts.OrderByDescending(a => a.ResultMs).First();
            Attempts.Remove(worst);
        }

        NextAttemptName = "";
        // Deltas/best recomputed via Attempts.CollectionChanged.

        // Flash only on a genuine improvement — never on the first attempt.
        if (hadAttempts && result < prevBest)
            NewBest?.Invoke();

        // Live PB: if a saved line is loaded and this attempt beats its stored
        // best, update it and persist (keeps the library's gold up to date).
        if (_activeSavedLine != null && (_activeSavedLine.BestMs == 0 || result < _activeSavedLine.BestMs))
        {
            _activeSavedLine.BestMs = result;
            UpdateTargetPb();
            _savedLinesViewModel.Persist();
        }
    }

    private void RecomputeDeltas()
    {
        if (Attempts.Count == 0) return;
        var bestMs = Attempts.Min(a => a.ResultMs);
        foreach (var a in Attempts)
        {
            a.IsBest = a.ResultMs == bestMs;
            a.DeltaText = a.IsBest ? "—" : TimeFormatter.SignedDelta((long)a.ResultMs - bestMs);
        }
    }

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
        CanExport = _start != null && _end != null;
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
                        var startIgt = _playerService.GetIgt();
                        if (startIgt == 0) break; // transient read — don't start on a bad baseline
                        _startIgt = startIgt;
                        _phase = Phase.Running;
                        PhaseText = "Running";
                    }
                    break;

                case Phase.Running:
                    var igt = _playerService.GetIgt();
                    if (igt == 0) break; // transient read — skip this tick
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
                        RecordAttempt(result);
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

    private static string FormatMs(uint ms) => TimeFormatter.Mmssmmm(ms);
}
