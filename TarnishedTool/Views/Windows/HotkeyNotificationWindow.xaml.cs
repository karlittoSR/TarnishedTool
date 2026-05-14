using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TarnishedTool.Models;
using TarnishedTool.Utilities;

namespace TarnishedTool.Views.Windows
{
    public partial class HotkeyNotificationWindow : Window
    {
        private readonly Storyboard _fadeInStoryboard;
        private readonly Storyboard _fadeOutStoryboard;

        public HotkeyNotificationWindow()
        {
            InitializeComponent();

            // Create fade-in animation
            _fadeInStoryboard = new Storyboard();
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            Storyboard.SetTarget(fadeIn, this);
            Storyboard.SetTargetProperty(fadeIn, new PropertyPath(OpacityProperty));
            _fadeInStoryboard.Children.Add(fadeIn);

            // Create fade-out animation
            _fadeOutStoryboard = new Storyboard();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            Storyboard.SetTarget(fadeOut, this);
            Storyboard.SetTargetProperty(fadeOut, new PropertyPath(OpacityProperty));
            _fadeOutStoryboard.Children.Add(fadeOut);
            _fadeOutStoryboard.Completed += (s, e) => Close();

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Position window (bottom-right corner by default)
            PositionWindow();

            // Make window topmost over game
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            User32.SetTopmost(hwnd);

            // Start fade-in
            Opacity = 0;
            _fadeInStoryboard.Begin();
        }

        private void PositionWindow()
        {
            var left = SettingsManager.Default.NotificationWindowLeft;
            var top = SettingsManager.Default.NotificationWindowTop;

            if (left > 0 && top > 0)
            {
                Left = left;
                Top = top;
            }
            else
            {
                // Default to bottom-right corner with some padding
                Left = SystemParameters.PrimaryScreenWidth - Width - 20;
                Top = SystemParameters.PrimaryScreenHeight - Height - 80;
            }
        }

        public void Show(HotkeyNotification notification, int durationMs)
        {
            DataContext = notification;

            // Update border color based on state
            var color = (Color)ColorConverter.ConvertFromString(notification.StateColor);
            NotificationBorder.BorderBrush = new SolidColorBrush(color);

            Show();

            // Auto-hide after duration
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _fadeOutStoryboard.Begin();
            };
            timer.Start();
        }

        protected override void OnMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            DragMove();

            // Save new position
            SettingsManager.Default.NotificationWindowLeft = Left;
            SettingsManager.Default.NotificationWindowTop = Top;
            SettingsManager.Default.Save();
        }

        protected override void OnMouseRightButtonDown(System.Windows.Input.MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            // Right-click to dismiss immediately
            _fadeOutStoryboard.Begin();
        }
    }
}
