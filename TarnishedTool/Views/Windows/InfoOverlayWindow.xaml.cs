//

using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TarnishedTool.Interfaces;
using TarnishedTool.Memory;
using TarnishedTool.Utilities;
using TarnishedTool.ViewModels;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.Views.Windows
{
    public partial class InfoOverlayWindow : Window
    {
        private readonly IMemoryService _memoryService;
        private readonly DispatcherTimer _timer;

        private TargetViewModel _targetVm;

        // Version of the last Force Acts press we processed.
        // When ForceSeqVersion changes, a new sequence started and we clear the consumed flag.
        private int _knownForceSeqVersion = -1;
        // True once a sequence has been fully consumed — stay in normal mode until a new sequence starts.
        private bool _forceSeqConsumed;
        private bool _sawRunningForceSeq;

        private static readonly SolidColorBrush BrushNormal    = new(Color.FromRgb(0xEA, 0xEA, 0xEA));
        private static readonly SolidColorBrush BrushPast      = new(Color.FromRgb(0xCC, 0xCC, 0xCC));
        private static readonly SolidColorBrush BrushCurrent   = new(Color.FromRgb(0x39, 0xFF, 0x14));
        private static readonly SolidColorBrush BrushFuture    = Brushes.White;
        private static readonly SolidColorBrush BrushSeparator = new(Color.FromRgb(0x80, 0x80, 0x80));

        public InfoOverlayWindow(IMemoryService memoryService)
        {
            _memoryService = memoryService;
            InitializeComponent();
            MouseLeftButtonDown += (_, _) => DragMove();
            Loaded += OnLoaded;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public void SetActsSource(TargetViewModel vm)
        {
            _targetVm = vm;
            ActsText.Visibility = vm != null ? Visibility.Visible : Visibility.Collapsed;
            _knownForceSeqVersion = -1;
            _forceSeqConsumed = false;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var left = SettingsManager.Default.IgtOverlayLeft;
            var top  = SettingsManager.Default.IgtOverlayTop;
            if (left > 0) Left = left;
            if (top  > 0) Top  = top;
            if (Application.Current.MainWindow != null)
                Application.Current.MainWindow.Closing += (_, _) => Close();
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                var gdmBase = _memoryService.Read<IntPtr>(GameDataMan.Base);
                if (gdmBase == IntPtr.Zero) return;
                var ms = _memoryService.Read<uint>(gdmBase + GameDataMan.Igt);
                var ts = TimeSpan.FromMilliseconds(ms);
                IgtText.Text = string.Format("IGT {0:D2}:{1:D2}:{2:D2}.{3:D2}",
                    (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);

                if (_targetVm != null)
                    UpdateActsDisplay();
            }
            catch { }
        }

        private void UpdateActsDisplay()
        {
            if (CodeCaveOffsets.Base == IntPtr.Zero) return;

            // Detect when a new sequence is launched — clear consumed flag
            int seqVer = _targetVm.ForceSeqVersion;
            if (seqVer != _knownForceSeqVersion)
            {
                _knownForceSeqVersion = seqVer;
                _forceSeqConsumed = seqVer == 0;
                _sawRunningForceSeq = false;
            }

            ActsText.Inlines.Clear();

            if (_targetVm.IsForceActSequenceActive)
            {
                ShowManagedForceActSequence();
                return;
            }

            byte shouldRun = _memoryService.Read<byte>(CodeCaveOffsets.Base + CodeCaveOffsets.ShouldRun);
            if (shouldRun != 0) _sawRunningForceSeq = true;

            if (shouldRun == 0 && !_forceSeqConsumed && seqVer > 0 && _sawRunningForceSeq)
            {
                _forceSeqConsumed = true;
                _targetVm.UnhookForceActSequence();
            }

            if (shouldRun != 0 && !_forceSeqConsumed)
            {
                int currentIdx = _memoryService.Read<int>(CodeCaveOffsets.Base + CodeCaveOffsets.CurrentIdx);
                var acts = new int[10];
                int seqLen = 0;
                for (int i = 0; i < 10; i++)
                {
                    acts[i] = _memoryService.Read<int>(CodeCaveOffsets.Base + CodeCaveOffsets.ActArray + 0x4 * i);
                    if (acts[i] != 0) seqLen = i + 1;
                }
                if (seqLen == 0) seqLen = 1;

                if (currentIdx >= seqLen)
                {
                    // The hook clears ShouldRun itself at the real sequence length.
                    // Wait for that flag before uninstalling so we do not patch code mid-execution.
                    for (int i = 0; i < seqLen; i++)
                    {
                        if (i > 0) ActsText.Inlines.Add(new Run(" - ") { Foreground = BrushSeparator });
                        ActsText.Inlines.Add(new Run(acts[i].ToString()) { Foreground = BrushPast });
                    }
                    return;
                }

                // Sequence in progress: past=light grey, current=green, future=white
                for (int i = 0; i < seqLen; i++)
                {
                    if (i > 0) ActsText.Inlines.Add(new Run(" - ") { Foreground = BrushSeparator });
                    SolidColorBrush color = i < currentIdx ? BrushPast
                                          : i == currentIdx ? BrushCurrent
                                          : BrushFuture;
                    ActsText.Inlines.Add(new Run(acts[i].ToString()) { Foreground = color });
                }
                return;
            }

            ShowNormalMode();
        }

        private void ShowManagedForceActSequence()
        {
            int[] acts = _targetVm.ForceActSequenceActs;
            int nextForcedIdx = _targetVm.ForceActSequenceIndex;
            int currentObservedIdx = -1;

            for (int i = Math.Min(nextForcedIdx, acts.Length) - 1; i >= 0; i--)
            {
                if (acts[i] == _targetVm.LastAct)
                {
                    currentObservedIdx = i;
                    break;
                }
            }

            for (int i = 0; i < acts.Length; i++)
            {
                if (i > 0) ActsText.Inlines.Add(new Run(" - ") { Foreground = BrushSeparator });
                SolidColorBrush color;
                if (i == currentObservedIdx)
                    color = BrushCurrent;
                else if (currentObservedIdx >= 0)
                    color = i < currentObservedIdx ? BrushPast : BrushFuture;
                else
                    color = i < nextForcedIdx ? BrushPast : BrushFuture;
                ActsText.Inlines.Add(new Run(acts[i].ToString()) { Foreground = color });
            }
        }

        private void ShowNormalMode()
        {
            ActsText.Inlines.Clear();
            ActsText.Inlines.Add(new Run($"{_targetVm.ActHistPrev0}") { Foreground = BrushPast });
            ActsText.Inlines.Add(new Run(" - ") { Foreground = BrushSeparator });
            ActsText.Inlines.Add(new Run($"{_targetVm.ActHistPrev1}") { Foreground = BrushPast });
            ActsText.Inlines.Add(new Run(" - ") { Foreground = BrushSeparator });
            ActsText.Inlines.Add(new Run($"{_targetVm.LastAct}") { Foreground = BrushCurrent });
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            SettingsManager.Default.IgtOverlayLeft = Left;
            SettingsManager.Default.IgtOverlayTop  = Top;
            SettingsManager.Default.Save();
            base.OnClosed(e);
        }
    }
}
