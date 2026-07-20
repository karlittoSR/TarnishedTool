//

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TarnishedTool.Models;
using TarnishedTool.Utilities;
using TarnishedTool.ViewModels;

namespace TarnishedTool.Views.Windows;

public partial class LineComparisonWindow : TopmostWindow
{
    private LineComparisonViewModel _vm;

    public LineComparisonWindow()
    {
        InitializeComponent();

        if (Application.Current.MainWindow != null)
            Application.Current.MainWindow.Closing += (_, _) => Close();

        DataContextChanged += OnDataContextChanged;

        Loaded += (_, _) =>
        {
            if (SettingsManager.Default.LineComparisonWindowLeft > 0)
                Left = SettingsManager.Default.LineComparisonWindowLeft;

            if (SettingsManager.Default.LineComparisonWindowTop > 0)
                Top = SettingsManager.Default.LineComparisonWindowTop;

            AlwaysOnTopCheckBox.IsChecked = SettingsManager.Default.LineComparisonAlwaysOnTop;
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null) _vm.NewBest -= FlashGold;
        _vm = e.NewValue as LineComparisonViewModel;
        if (_vm != null) _vm.NewBest += FlashGold;
    }

    private void FlashGold()
    {
        var anim = new DoubleAnimation
        {
            From = 0.3,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(450),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        GoldFlash.BeginAnimation(OpacityProperty, anim);
    }

    private void AttemptsGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is LineComparisonAttempt { IsPersistentPb: true })
            e.Cancel = true;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_vm != null) _vm.NewBest -= FlashGold;

        SettingsManager.Default.LineComparisonWindowLeft = Left;
        SettingsManager.Default.LineComparisonWindowTop = Top;
        SettingsManager.Default.LineComparisonAlwaysOnTop = AlwaysOnTopCheckBox.IsChecked ?? false;
        SettingsManager.Default.Save();
    }
}
