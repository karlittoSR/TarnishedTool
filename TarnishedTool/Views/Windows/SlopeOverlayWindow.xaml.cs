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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var left = SettingsManager.Default.SlopeOverlayLeft;
            var top  = SettingsManager.Default.SlopeOverlayTop;
            if (left > 0) Left = left;
            if (top  > 0) Top  = top;
            if (Application.Current.MainWindow != null)
                Application.Current.MainWindow.Closing += (_, _) => Close();
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
