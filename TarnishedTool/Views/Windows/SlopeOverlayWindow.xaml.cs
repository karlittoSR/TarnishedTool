//

using System;
using System.Windows;
using TarnishedTool.Utilities;

namespace TarnishedTool.Views.Windows
{
    public partial class SlopeOverlayWindow : Window
    {
        public SlopeOverlayWindow()
        {
            InitializeComponent();
            MouseLeftButtonDown += (_, _) => DragMove();
            Loaded += OnLoaded;
        }

        // Fallback footprint (dot plus its margin) in case layout has not measured the
        // window yet when Loaded fires.
        private const double DefaultSize = 32d;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var left = SettingsManager.Default.SlopeOverlayLeft;
            var top  = SettingsManager.Default.SlopeOverlayTop;

            if (left > 0 || top > 0)
            {
                if (left > 0) Left = left;
                if (top  > 0) Top  = top;
            }
            else
            {
                CenterOnPrimaryScreen();
            }

            if (Application.Current.MainWindow != null)
                Application.Current.MainWindow.Closing += (_, _) => Close();
        }

        // Never dragged, so start in the middle of the screen the game runs on. Closing
        // always writes the current position back, so this only applies on first use.
        private void CenterOnPrimaryScreen()
        {
            var width  = ActualWidth  > 0 ? ActualWidth  : DefaultSize;
            var height = ActualHeight > 0 ? ActualHeight : DefaultSize;

            Left = (SystemParameters.PrimaryScreenWidth  - width)  / 2;
            Top  = (SystemParameters.PrimaryScreenHeight - height) / 2;
        }

        protected override void OnClosed(EventArgs e)
        {
            SettingsManager.Default.SlopeOverlayLeft = Left;
            SettingsManager.Default.SlopeOverlayTop  = Top;
            SettingsManager.Default.Save();
            base.OnClosed(e);
        }
    }
}
