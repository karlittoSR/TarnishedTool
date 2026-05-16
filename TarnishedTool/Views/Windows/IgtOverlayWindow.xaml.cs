// 

using System;
using System.Windows;
using System.Windows.Threading;
using TarnishedTool.Interfaces;
using TarnishedTool.Utilities;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.Views.Windows
{
    public partial class IgtOverlayWindow : Window
    {
        private readonly IMemoryService _memoryService;
        private readonly DispatcherTimer _timer;

        public IgtOverlayWindow(IMemoryService memoryService)
        {
            _memoryService = memoryService;
            InitializeComponent();
            MouseLeftButtonDown += (_, _) => DragMove();
            Loaded += OnLoaded;
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnTick;
            _timer.Start();
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
            }
            catch { }
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
