//

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TarnishedTool.Utilities;
using TarnishedTool.ViewModels;

namespace TarnishedTool.Views.Windows;

public partial class SavedLinesWindow : TopmostWindow
{
    // Double-clicking an item loads it (same as the Load button).
    private void SavedLinesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBox list
            && ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem
            && DataContext is SavedLinesViewModel vm
            && vm.LoadCommand.CanExecute(null))
        {
            vm.LoadCommand.Execute(null);
        }
    }

    public SavedLinesWindow()
    {
        InitializeComponent();

        if (Application.Current.MainWindow != null)
            Application.Current.MainWindow.Closing += (_, _) => Close();

        Loaded += (_, _) =>
        {
            if (SettingsManager.Default.SavedLinesWindowLeft > 0)
                Left = SettingsManager.Default.SavedLinesWindowLeft;

            if (SettingsManager.Default.SavedLinesWindowTop > 0)
                Top = SettingsManager.Default.SavedLinesWindowTop;

            AlwaysOnTopCheckBox.IsChecked = SettingsManager.Default.SavedLinesAlwaysOnTop;
        };
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        SettingsManager.Default.SavedLinesWindowLeft = Left;
        SettingsManager.Default.SavedLinesWindowTop = Top;
        SettingsManager.Default.SavedLinesAlwaysOnTop = AlwaysOnTopCheckBox.IsChecked ?? false;
        SettingsManager.Default.Save();
    }
}
