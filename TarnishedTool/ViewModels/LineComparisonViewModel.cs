//

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
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
    private readonly IPlayerService _playerService;
    private readonly ICharacterSnapshotService _characterSnapshotService;

    // Resets the current zone (revive bosses, reset mobs, refill flasks). Wired from
    // MainWindow to EnemyViewModel.ResetZoneInPlace so this VM stays decoupled from
    // the enemy/revive machinery. Takes the start position (a boss reset warps there
    // to safely unload the live boss) and returns true if it triggered a warp/reload
    // — the timer then waits for the reload, snaps the start position and applies
    // char once it finishes.
    private Func<Position, bool> _resetZoneAction;
    public void SetZoneResetAction(Func<Position, bool> action) => _resetZoneAction = action;

    private Action _restAction;
    public void SetRestAction(Action action) => _restAction = action;

    // A reset (which may warp/reload) is running on a background thread. The phase
    // machine is suppressed until it finishes, then the Tick re-arms cleanly (player
    // back at the start, clock 0). _reArmPending is set by the background task so the
    // re-arm runs on the game-tick thread.
    private bool _resetInProgress;
    private bool _reArmPending;
    private DateTime _resetStart;
    private static readonly TimeSpan ResetTimeout = TimeSpan.FromSeconds(20);

    private enum Phase { Idle, Armed, AtStart, Running, Finished }

    private const int MaxAttempts = 8;
    private const int MaxNameLength = 44;
    private const float MinRadius = 0.1f;

    private Phase _phase = Phase.Idle;
    private Position _start;
    private Position _end;
    private uint _startIgt;
    private int _attemptCounter;
    private readonly DispatcherTimer _comparisonTimer;
    private readonly DispatcherTimer _feedbackTimer;

    private readonly SavedLinesViewModel _savedLinesViewModel;
    private SavedLinesWindow _savedLinesWindow;
    private SavedLine _activeSavedLine;

    // Raised when a recorded attempt beats the previous best (never on the first attempt).
    public event Action NewBest;

    public LineComparisonViewModel(IPlayerService playerService,
        IStateService stateService, ICharacterSnapshotService characterSnapshotService = null)
    {
        _playerService = playerService;
        _characterSnapshotService = characterSnapshotService;

        SetStartCommand = new DelegateCommand(SetStart);
        SetEndCommand = new DelegateCommand(SetEnd);
        RestoreToStartCommand = new DelegateCommand(RestoreToStart);
        ClearResultsCommand = new DelegateCommand(ClearResults);
        RemoveSelectedCommand = new DelegateCommand(RemoveSelected);
        OpenSavedLinesCommand = new DelegateCommand(OpenSavedLines);

        _savedLinesViewModel = new SavedLinesViewModel(this, characterSnapshotService);

        Attempts.CollectionChanged += (_, _) => RecomputeDeltas();

        stateService.Subscribe(State.Loaded, () =>
        {
            CanOperate = true;
        });
        stateService.Subscribe(State.NotLoaded, () => CanOperate = false);

        // Line crossings need frame-level polling. The shared game tick runs every
        // 64 ms, which can notice both the start and finish several frames late.
        // Keep the faster poll local to this window so unrelated features retain
        // their existing memory-read frequency.
        _comparisonTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _comparisonTimer.Tick += (_, _) => Tick();

        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _feedbackTimer.Tick += (_, _) => { FeedbackText = ""; _feedbackTimer.Stop(); };
    }

    #region Commands

    public ICommand SetStartCommand { get; }
    public ICommand SetEndCommand { get; }
    public ICommand RestoreToStartCommand { get; }
    public ICommand ClearResultsCommand { get; }
    public ICommand RemoveSelectedCommand { get; }
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
                ResetResultsForChangedLine(); // trigger zone changed — old attempts no longer comparable
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
                ResetResultsForChangedLine();
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
        if (!_comparisonTimer.IsEnabled)
            _comparisonTimer.Start();
    }

    public void NotifyWindowClosed()
    {
        _comparisonTimer.Stop();
    }

    public void SetStart()
    {
        try
        {
            _start = _playerService.CapturePosition();
            StartText = Describe(_start);
            ResetResultsForChangedLine(); // line definition changed — old attempts no longer comparable
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
            ResetResultsForChangedLine();
            ReArm();
        }
        catch { }
    }

    public void RestoreToStart()
    {
        if (_start == null) return;

        // The zone reset is always attempted — no opt-in toggle. ResetZoneInPlace
        // already decides for itself whether the destination has a boss: if it does
        // it revives and warps, otherwise it does nothing and this is just a
        // teleport. A checkbox added nothing the boss lookup doesn't know, and
        // silently disabled the revive AND the rest when left off.
        //
        // The reset may warp (WarpToBlockId blocks until the fade completes), so
        // run the whole sequence on a background thread IN ORDER — reset → snap
        // exact start → apply char → rest — to avoid a racing second warp. Suppress
        // the timer meanwhile; the Tick re-arms once _reArmPending is set.
        _resetInProgress = true;
        _resetStart = DateTime.Now;
        ReArm(); // show Armed / 00:00 immediately

        var start = _start;
        var snapshot = _activeSavedLine?.Snapshot;

        _ = Task.Run(() =>
        {
            try
            {
                bool warped = _resetZoneAction?.Invoke(start) ?? false;

                // Only restore the position when the reset did NOT warp.
                //
                // A warp has already placed the player exactly: WarpToBlockId forces
                // the saved coords through its write hook and waits out both fade
                // phases. Running the position restore on top is redundant and
                // actively unsafe — it moves by a DELTA computed from the position
                // read immediately after a cross-area load, when coords and block id
                // can still be mid-transition. A bad read produces a huge delta,
                // which the long-distance path applies with gravity disabled; gravity
                // returns a second later and the player falls out of the world.
                //
                // No-boss case: nothing warped, so this is the actual teleport, and
                // it must BLOCK so the character is not applied mid-warp (the load
                // would restore HP/FP from storage). Safe to block — we are already
                // on a background task, so the UI thread is never stalled.
                if (!warped)
                    try { _playerService.RestorePosBlocking(start); } catch { }

                if (snapshot != null)
                    try { _characterSnapshotService?.Apply(snapshot); } catch { }

                // Rest LAST — after the char's stats are applied — so the flask/HP/FP
                // refill uses the final max values and leaves no gap.
                try { _restAction?.Invoke(); } catch { }
            }
            finally
            {
                _reArmPending = true;
                _resetInProgress = false;
            }
        });
    }

    private void ShowFeedback(string message)
    {
        FeedbackText = message;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    // Applies an encoded line definition (start/end + radii). Returns false if invalid.
    // Detaches from any previously active saved line before loading the new one.
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
        ResetResultsForChangedLine(); // new line definition
        ReArm();
        ShowFeedback("Line loaded");
        return true;
    }

    // Loads a library entry: applies its code, tracks it as active (so its PB
    // auto-updates), seeds the PB as "Attempt 1" (a target to beat), and shows
    // the PB in the main window.
    public bool LoadSavedLine(SavedLine line)
    {
        if (line == null || !ApplyCode(line.Code)) return false;

        _activeSavedLine = line;
        EnsurePersistentPbRow();
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
    public void SetActiveSavedLine(SavedLine line, bool ensurePersistentPbRow = false)
    {
        _activeSavedLine = line;
        if (ensurePersistentPbRow)
            EnsurePersistentPbRow();
        UpdateTargetPb();
    }

    // A saved PB is a permanent comparison baseline for this session. It always
    // stays first and is visually distinct from the fastest ordinary attempt.
    private void EnsurePersistentPbRow()
    {
        if (_activeSavedLine == null || _activeSavedLine.BestMs == 0) return;

        var pb = Attempts.FirstOrDefault(a => a.IsPersistentPb);
        if (pb == null)
        {
            pb = new LineComparisonAttempt(0, "PB", _activeSavedLine.BestMs, true);
            Attempts.Insert(0, pb);
        }
        else
        {
            pb.UpdatePersistentPb(_activeSavedLine.BestMs);
            var index = Attempts.IndexOf(pb);
            if (index > 0) Attempts.Move(index, 0);
        }

        RecomputeDeltas();
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
        // Clear only this session's runs. The saved PB is durable data and may
        // only be removed explicitly by selecting its row and removing it.
        foreach (var attempt in Attempts.Where(a => !a.IsPersistentPb).ToList())
            Attempts.Remove(attempt);
        _attemptCounter = 0;
        EnsurePersistentPbRow();
    }

    // Internal line changes must discard the visible attempt table without
    // treating that discard as a request to erase the saved line's PB. Detach
    // first so a future change to the clearing logic cannot persist an empty
    // table back to the previous save.
    private void ResetResultsForChangedLine()
    {
        DetachSavedLine();
        Attempts.Clear();
        _attemptCounter = 0;
    }

    private void RemoveSelected()
    {
        if (SelectedAttempt == null) return;

        var removed = SelectedAttempt;
        SelectedAttempt = null;
        Attempts.Remove(removed);

        // Ordinary session rows never own the durable PB. Removing the protected
        // PB row is the one explicit action that clears it from the saved line.
        if (removed.IsPersistentPb && _activeSavedLine != null)
        {
            _activeSavedLine.BestMs = 0;
            UpdateTargetPb();
            _savedLinesViewModel.Persist();
        }
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

        // Live PB: if a saved line is loaded and this attempt beats its stored
        // best, update it and persist. When this session started with a persistent
        // PB row, advance that same protected row instead of turning the ordinary
        // attempt into a separate session gold.
        if (_activeSavedLine != null && (_activeSavedLine.BestMs == 0 || result < _activeSavedLine.BestMs))
        {
            // After an explicit PB deletion there may already be session runs in
            // the table. Re-establish the PB from the fastest session run, not
            // blindly from the newest one.
            _activeSavedLine.BestMs = _activeSavedLine.BestMs == 0
                ? Attempts.Where(a => !a.IsPersistentPb).Min(a => a.ResultMs)
                : result;
            EnsurePersistentPbRow();
            UpdateTargetPb();
            _savedLinesViewModel.Persist();
        }

        // Keep the best MaxAttempts ordinary attempts. A persistent PB is a
        // baseline, not a session attempt, and must never be pruned as the worst.
        while (Attempts.Count(a => !a.IsPersistentPb) > MaxAttempts)
        {
            var worst = Attempts.Where(a => !a.IsPersistentPb)
                .OrderByDescending(a => a.ResultMs).First();
            Attempts.Remove(worst);
        }

        NextAttemptName = "";
        RecomputeDeltas();

        // Flash only on a genuine improvement — never on the first attempt.
        if (hadAttempts && result < prevBest)
            NewBest?.Invoke();
    }

    private void RecomputeDeltas()
    {
        if (Attempts.Count == 0) return;

        var persistentPb = Attempts.FirstOrDefault(a => a.IsPersistentPb);
        var bestMs = persistentPb?.ResultMs ?? Attempts.Min(a => a.ResultMs);
        foreach (var a in Attempts)
        {
            // With an all-time PB baseline, only that protected row is gold. If
            // there is no baseline, retain the original session-best behaviour.
            a.IsBest = persistentPb != null ? ReferenceEquals(a, persistentPb) : a.ResultMs == bestMs;
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
        LiveTimeText = FormatMs(0);
    }

    private void Tick()
    {
        try
        {
            if (!CanOperate) return;
            if (_start == null || _end == null) return;

            // While a reset (warp/reload) runs, don't advance the phase machine —
            // transient positions during a quitout would false-start the timer.
            // Fall back to clearing if the reset somehow never completes.
            if (_resetInProgress)
            {
                if (DateTime.Now - _resetStart > ResetTimeout)
                    _resetInProgress = false;
                return;
            }

            // The reset finished on the background thread — re-arm here (game-tick
            // thread) so the player comes back at the start with the clock at 0.
            if (_reArmPending)
            {
                _reArmPending = false;
                ReArm();
                return;
            }

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
