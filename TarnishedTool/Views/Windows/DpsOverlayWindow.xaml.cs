// 

using System;
using System.Windows;
using TarnishedTool.Utilities;

namespace TarnishedTool.Views.Windows
{
    public partial class DpsOverlayWindow : Window
    {
        public DpsOverlayWindow()
        {
            InitializeComponent();
            MouseLeftButtonDown += (_, _) => DragMove();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            var left = SettingsManager.Default.DpsOverlayLeft;
            var top  = SettingsManager.Default.DpsOverlayTop;
            if (left > 0) Left = left;
            if (top  > 0) Top  = top;
            if (Application.Current.MainWindow != null)
                Application.Current.MainWindow.Closing += (_, _) => Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            SettingsManager.Default.DpsOverlayLeft = Left;
            SettingsManager.Default.DpsOverlayTop  = Top;
            SettingsManager.Default.Save();
            base.OnClosed(e);
        }
    }
}
