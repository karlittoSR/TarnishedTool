//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
    private readonly IEventService _eventService;
    private readonly IEventLogReader _eventLogReader;

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

    private Action _openEventLoggerAction;
    public void SetOpenEventLoggerAction(Action action) => _openEventLoggerAction = action;

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
    private const float DefaultStartRadius = 2f;

    private Phase _phase = Phase.Idle;
    private Position _start;
    private Position _end;
    private uint _startIgt;
    private bool _endFlagIsExpected;
    private bool _flagFinishPending;
    private int _attemptCounter;
    private readonly DispatcherTimer _comparisonTimer;
    private readonly DispatcherTimer _feedbackTimer;

    private readonly SavedLinesViewModel _savedLinesViewModel;
    private SavedLinesWindow _savedLinesWindow;
    private SavedLine _activeSavedLine;
    private bool _isWindowOpen;
    private bool _flagMonitorActive;

    // Raised when a recorded attempt beats the previous best (never on the first attempt).
    public event Action NewBest;

    public LineComparisonViewModel(IPlayerService playerService,
        IStateService stateService, IEventService eventService, IEventLogReader eventLogReader,
        ICharacterSnapshotService characterSnapshotService = null)
    {
        _playerService = playerService;
        _characterSnapshotService = characterSnapshotService;
        _eventService = eventService;
        _eventLogReader = eventLogReader;
        _eventLogReader.EntriesReceived += OnEventLogEntriesReceived;

        SetStartCommand = new DelegateCommand(SetStart);
        SetEndCommand = new DelegateCommand(SetEnd);
        RestoreToStartCommand = new DelegateCommand(RestoreToStart);
        ClearResultsCommand = new DelegateCommand(ClearResults);
        RemoveSelectedCommand = new DelegateCommand(RemoveSelected);
        OpenSavedLinesCommand = new DelegateCommand(OpenSavedLines);
        ScanEventFlagsCommand = new DelegateCommand(() => _openEventLoggerAction?.Invoke());

        _savedLinesViewModel = new SavedLinesViewModel(this, characterSnapshotService);

        Attempts.CollectionChanged += (_, _) => RecomputeDeltas();

        stateService.Subscribe(State.Loaded, () =>
        {
            CanOperate = true;
        });
        stateService.Subscribe(State.NotLoaded, () => CanOperate = false);
        stateService.Subscribe(State.Detached, () => CanOperate = false);

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
    public ICommand ScanEventFlagsCommand { get; }

    #endregion

    public ObservableCollection<LineComparisonAttempt> Attempts { get; } = new();

    #region Properties

    private bool _canOperate;
    public bool CanOperate
    {
        get => _canOperate;
        set
        {
            if (SetProperty(ref _canOperate, value))
                UpdateFlagMonitor();
        }
    }

    public IReadOnlyList<string> FinishTypeOptions { get; } = new[] { "Position", "Event Flag" };
    public IReadOnlyList<string> FlagValueOptions { get; } = new[] { "ON", "OFF" };

    private int _selectedFinishTypeIndex;
    public int SelectedFinishTypeIndex
    {
        get => _selectedFinishTypeIndex;
        set
        {
            int normalized = value == 1 ? 1 : 0;
            if (!SetProperty(ref _selectedFinishTypeIndex, normalized)) return;
            OnPropertyChanged(nameof(IsPositionFinish));
            OnPropertyChanged(nameof(IsEventFlagFinish));
            ResetResultsForChangedLine();
            UpdateFlagMonitor();
            ReArm();
        }
    }

    public bool IsPositionFinish => _selectedFinishTypeIndex == 0;
    public bool IsEventFlagFinish => _selectedFinishTypeIndex == 1;

    private string _endFlagIdText = "";
    public string EndFlagIdText
    {
        get => _endFlagIdText;
        set
        {
            if (!SetProperty(ref _endFlagIdText, value)) return;
            ResetResultsForChangedLine();
            ReArm();
        }
    }

    private int _selectedFlagValueIndex;
    public int SelectedFlagValueIndex
    {
        get => _selectedFlagValueIndex;
        set
        {
            int normalized = value == 1 ? 1 : 0;
            if (!SetProperty(ref _selectedFlagValueIndex, normalized)) return;
            ResetResultsForChangedLine();
            ReArm();
        }
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

    private string _targetReferenceText = "";
    public string TargetReferenceText
    {
        get => _targetReferenceText;
        set => SetProperty(ref _targetReferenceText, value);
    }

    private float _startRadius = DefaultStartRadius;
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
        _isWindowOpen = true;
        UpdateFlagMonitor();
        if (!_comparisonTimer.IsEnabled)
            _comparisonTimer.Start();
    }

    public void NotifyWindowClosed()
    {
        _isWindowOpen = false;
        UpdateFlagMonitor();
        _comparisonTimer.Stop();
    }

    public void SetStart()
    {
        try
        {
            _start = _playerService.CapturePosition();
            StartText = Describe(_start);
            SetProperty(ref _startRadius, DefaultStartRadius, nameof(StartRadius));
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

    // Applies an encoded segment definition. Returns false if invalid.
    // Detaches from any previously active saved line before loading the new one.
    public bool ApplyCode(string code)
    {
        if (!LineShareCodec.TryDecode(code, out var definition))
            return false;

        _start = definition.Start;
        _end = definition.EndPosition;
        StartText = Describe(_start);
        EndText = _end != null ? Describe(_end) : "Not set";

        // Set fields directly so their editing setters do not repeatedly clear
        // attempts while one complete definition is being loaded.
        SetProperty(ref _startRadius,
            definition.StartRadius < MinRadius ? MinRadius : definition.StartRadius, nameof(StartRadius));
        SetProperty(ref _endRadius,
            definition.EndRadius < MinRadius ? MinRadius : definition.EndRadius, nameof(EndRadius));
        SetProperty(ref _selectedFinishTypeIndex,
            definition.FinishType == SegmentFinishType.EventFlag ? 1 : 0, nameof(SelectedFinishTypeIndex));
        SetProperty(ref _endFlagIdText,
            definition.FinishType == SegmentFinishType.EventFlag
                ? definition.EndFlagId.ToString(CultureInfo.InvariantCulture)
                : "",
            nameof(EndFlagIdText));
        SetProperty(ref _selectedFlagValueIndex,
            definition.FinishType != SegmentFinishType.EventFlag || definition.EndFlagValue ? 0 : 1,
            nameof(SelectedFlagValueIndex));
        OnPropertyChanged(nameof(IsPositionFinish));
        OnPropertyChanged(nameof(IsEventFlagFinish));
        ResetResultsForChangedLine(); // new line definition
        UpdateFlagMonitor();
        ReArm();
        ShowFeedback("Segment loaded");
        return true;
    }

    // Loads a library entry and restores its protected PB/reference comparison
    // rows. Only the local PB is advanced by completed attempts.
    public bool LoadSavedLine(SavedLine line)
    {
        if (line == null || !ApplyCode(line.Code)) return false;

        _activeSavedLine = line;
        EnsureComparisonRows();
        UpdateTargetTimes();
        return true;
    }

    // The current typed definition as the token persisted inside SavedLine JSON.
    public string ExportCurrentCode() =>
        TryBuildDefinition(out var definition) ? LineShareCodec.Encode(definition) : null;

    // Best local time currently in the table, excluding a shared reference.
    public uint GetCurrentBestMs()
    {
        var localTimes = Attempts.Where(a => !a.IsReference).Select(a => a.ResultMs).ToList();
        return localTimes.Count == 0 ? 0 : localTimes.Min();
    }

    // Marks a saved line as active so subsequent attempts update its PB live.
    // Used right after Save current, so a freshly saved line starts tracking
    // immediately (the first boss time attaches to it).
    public void SetActiveSavedLine(SavedLine line, bool ensurePersistentPbRow = false)
    {
        _activeSavedLine = line;
        if (ensurePersistentPbRow)
            EnsureComparisonRows();
        UpdateTargetTimes();
    }

    // Protected comparison rows are durable data, not session attempts. PB stays
    // first; an imported reference follows it and is never changed by a run.
    private void EnsureComparisonRows()
    {
        if (_activeSavedLine == null) return;

        var pb = Attempts.FirstOrDefault(a => a.IsPersistentPb);
        if (_activeSavedLine.BestMs > 0)
        {
            if (pb == null)
            {
                pb = new LineComparisonAttempt(0, "PB", _activeSavedLine.BestMs, isPersistentPb: true);
                Attempts.Insert(0, pb);
            }
            else
            {
                pb.UpdatePersistentPb(_activeSavedLine.BestMs);
                var index = Attempts.IndexOf(pb);
                if (index > 0) Attempts.Move(index, 0);
            }
        }
        else if (pb != null)
        {
            Attempts.Remove(pb);
            pb = null;
        }

        var reference = Attempts.FirstOrDefault(a => a.IsReference);
        if (_activeSavedLine.ReferenceMs > 0)
        {
            if (reference == null)
            {
                reference = new LineComparisonAttempt(0, "Reference",
                    _activeSavedLine.ReferenceMs, isReference: true);
                Attempts.Insert(pb == null ? 0 : 1, reference);
            }
            else
            {
                reference.UpdatePersistentPb(_activeSavedLine.ReferenceMs);
                var targetIndex = pb == null ? 0 : 1;
                var index = Attempts.IndexOf(reference);
                if (index != targetIndex) Attempts.Move(index, targetIndex);
            }
        }
        else if (reference != null)
        {
            Attempts.Remove(reference);
        }

        RecomputeDeltas();
    }

    // Keep the positional/flag definition usable after deletion, but sever its
    // ownership link so future runs cannot update an object absent from Lines.
    public void DetachDeletedSavedLine(SavedLine line)
    {
        if (!ReferenceEquals(_activeSavedLine, line)) return;

        _activeSavedLine = null;
        foreach (var protectedAttempt in Attempts.Where(a => a.IsProtected).ToList())
            Attempts.Remove(protectedAttempt);

        UpdateTargetTimes();
        RecomputeDeltas();
        ShowFeedback("Saved segment deleted; timer is now unsaved");
    }

    private void DetachSavedLine()
    {
        _activeSavedLine = null;
        UpdateTargetTimes();
    }

    private void UpdateTargetTimes()
    {
        TargetPbText = _activeSavedLine != null && _activeSavedLine.BestMs > 0
            ? $"PB: {FormatMs(_activeSavedLine.BestMs)}"
            : "";
        TargetReferenceText = _activeSavedLine != null && _activeSavedLine.ReferenceMs > 0
            ? $"Ref: {FormatMs(_activeSavedLine.ReferenceMs)}"
            : "";
    }

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
        // Clear only session runs. PB and reference are durable and can only be
        // removed explicitly by selecting their protected row.
        foreach (var attempt in Attempts.Where(a => !a.IsProtected).ToList())
            Attempts.Remove(attempt);
        _attemptCounter = 0;
        EnsureComparisonRows();
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

    private bool TryBuildDefinition(out SegmentDefinition definition)
    {
        definition = null;
        if (_start == null) return false;

        if (IsEventFlagFinish)
        {
            if (!TryGetEndFlagId(out uint eventId)) return false;
            definition = SegmentDefinition.EventFlagFinish(
                _start, StartRadius, eventId, _selectedFlagValueIndex == 0);
            return true;
        }

        if (_end == null) return false;
        definition = SegmentDefinition.PositionFinish(_start, StartRadius, _end, EndRadius);
        return true;
    }

    private bool TryGetEndFlagId(out uint eventId) =>
        uint.TryParse(_endFlagIdText?.Trim(), NumberStyles.None,
            CultureInfo.InvariantCulture, out eventId) && eventId > 0;

    private void UpdateFlagMonitor()
    {
        bool shouldBeActive = _isWindowOpen && CanOperate && IsEventFlagFinish;
        if (shouldBeActive == _flagMonitorActive) return;

        if (shouldBeActive)
        {
            _eventService.AcquireEventLogger();
            _eventLogReader.Start();
        }
        else
        {
            _eventLogReader.Stop();
            _eventService.ReleaseEventLogger();
        }

        _flagMonitorActive = shouldBeActive;
    }

    private void OnEventLogEntriesReceived(List<EventLogEntry> entries)
    {
        if (_phase != Phase.Running || !IsEventFlagFinish
            || !TryGetEndFlagId(out uint eventId))
            return;

        bool expectedValue = _selectedFlagValueIndex == 0;
        foreach (var entry in entries)
        {
            if (entry.EventId != eventId) continue;

            bool wasExpected = _endFlagIsExpected;
            _endFlagIsExpected = entry.Value == expectedValue;
            if (!wasExpected && _endFlagIsExpected)
            {
                // Preserve a detected one-shot transition if IGT happens to be
                // transiently unreadable on this exact frame. Tick retries it.
                _flagFinishPending = true;
                TryFinishPendingFlag();
                return;
            }
        }
    }

    private void TryFinishPendingFlag()
    {
        if (!_flagFinishPending || _phase != Phase.Running) return;

        uint igt = _playerService.GetIgt();
        if (igt == 0 || igt < _startIgt) return;

        _flagFinishPending = false;
        FinishAttempt(igt - _startIgt);
    }

    private void FinishAttempt(uint result)
    {
        _phase = Phase.Finished;
        PhaseText = "Finished";
        LiveTimeText = FormatMs(result);
        RecordAttempt(result);
    }

    private void RemoveSelected()
    {
        if (SelectedAttempt == null) return;

        var removed = SelectedAttempt;
        SelectedAttempt = null;
        Attempts.Remove(removed);

        // Protected rows are the only explicit way to delete their corresponding
        // durable value. Ordinary session-row removal never changes saved data.
        if (removed.IsPersistentPb && _activeSavedLine != null)
        {
            _activeSavedLine.BestMs = 0;
            UpdateTargetTimes();
            _savedLinesViewModel.Persist();
        }
        else if (removed.IsReference && _activeSavedLine != null)
        {
            _activeSavedLine.ReferenceMs = 0;
            UpdateTargetTimes();
            _savedLinesViewModel.Persist();
        }

        RecomputeDeltas();
    }

    private void RecordAttempt(uint result)
    {
        // Defensive ownership check: only an object still present in the saved
        // library may receive a durable PB update.
        if (_activeSavedLine != null && !_savedLinesViewModel.Contains(_activeSavedLine))
            DetachDeletedSavedLine(_activeSavedLine);

        // A shared reference must not suppress a genuine local PB flash. Capture
        // the local comparison before adding/updating any protected rows.
        uint previousSavedPb = _activeSavedLine?.BestMs ?? 0;
        var priorSessionTimes = Attempts.Where(a => !a.IsProtected).Select(a => a.ResultMs).ToList();
        bool hadLocalComparison = _activeSavedLine != null
            ? previousSavedPb > 0
            : priorSessionTimes.Count > 0;
        uint previousLocalBest = _activeSavedLine != null
            ? previousSavedPb
            : (priorSessionTimes.Count > 0 ? priorSessionTimes.Min() : uint.MaxValue);

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
                ? Attempts.Where(a => !a.IsProtected).Min(a => a.ResultMs)
                : result;
            EnsureComparisonRows();
            UpdateTargetTimes();
            _savedLinesViewModel.Persist();
        }

        // Keep the best MaxAttempts ordinary attempts. Protected PB/reference
        // baselines are not session attempts and must never be pruned.
        while (Attempts.Count(a => !a.IsProtected) > MaxAttempts)
        {
            var worst = Attempts.Where(a => !a.IsProtected)
                .OrderByDescending(a => a.ResultMs).First();
            Attempts.Remove(worst);
        }

        NextAttemptName = "";
        RecomputeDeltas();

        // Flash only on a genuine improvement — never on the first attempt.
        if (hadLocalComparison && result < previousLocalBest)
            NewBest?.Invoke();
    }

    private void RecomputeDeltas()
    {
        if (Attempts.Count == 0) return;

        var persistentPb = Attempts.FirstOrDefault(a => a.IsPersistentPb);
        var reference = Attempts.FirstOrDefault(a => a.IsReference);
        var protectedBaseline = persistentPb ?? reference;
        var bestMs = protectedBaseline?.ResultMs ?? Attempts.Min(a => a.ResultMs);
        foreach (var a in Attempts)
        {
            // A personal PB remains the sole gold row. Reference-only saves use
            // their reference for deltas without presenting it as a personal PB.
            a.IsBest = persistentPb != null
                ? ReferenceEquals(a, persistentPb)
                : reference == null && a.ResultMs == bestMs;
            a.DeltaText = ReferenceEquals(a, protectedBaseline) || a.IsBest
                ? "—"
                : TimeFormatter.SignedDelta((long)a.ResultMs - bestMs);
        }
    }

    private void ReArm()
    {
        _flagFinishPending = false;
        if (TryBuildDefinition(out _))
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
            if (!TryBuildDefinition(out _)) return;

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
                        _flagFinishPending = false;
                        if (IsEventFlagFinish && TryGetEndFlagId(out uint eventId))
                        {
                            bool expectedValue = _selectedFlagValueIndex == 0;
                            _endFlagIsExpected = _eventService.GetEvent(eventId) == expectedValue;
                        }
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

                    if (IsEventFlagFinish)
                    {
                        TryFinishPendingFlag();
                    }
                    else if (Distance(current, _end) <= EndRadius)
                    {
                        FinishAttempt(igt - _startIgt);
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
