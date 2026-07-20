//

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TarnishedTool.Models;
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

    // --- Drag-and-drop reordering of saved lines ---
    // Records where a potential drag started and which item is under the cursor,
    // so PreviewMouseMove can decide when the gesture becomes a real drag.
    private Point _dragStartPoint;
    private SavedLine _draggedItem;

    private void SavedLinesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _draggedItem = null;

        if (sender is ListBox list
            && ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem item)
        {
            _draggedItem = item.DataContext as SavedLine;
        }
    }

    private void SavedLinesList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedItem == null) return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is ListBox list)
        {
            DragDrop.DoDragDrop(list, _draggedItem, DragDropEffects.Move);
            _draggedItem = null;
        }
    }

    private void SavedLinesList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(SavedLine)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void SavedLinesList_Drop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (DataContext is not SavedLinesViewModel vm) return;
        if (e.Data.GetData(typeof(SavedLine)) is not SavedLine dragged) return;

        var from = vm.Lines.IndexOf(dragged);
        if (from < 0) return;

        // Drop onto an item reorders to that item's slot; dropping past the last
        // item (into empty space) moves it to the end.
        int to = vm.Lines.Count - 1;
        if (ItemsControl.ContainerFromElement(list, e.OriginalSource as DependencyObject) is ListBoxItem targetItem
            && targetItem.DataContext is SavedLine targetLine)
        {
            to = vm.Lines.IndexOf(targetLine);
        }

        if (to < 0 || to == from) return;

        vm.Lines.Move(from, to);
        vm.SelectedLine = dragged;
        vm.Persist();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLinesViewModel vm)
        {
            ExportSelectedMenuItem.IsEnabled = vm.SelectedLine != null;
            ExportAllMenuItem.IsEnabled = vm.Lines.Count > 0;
        }

        ExportButton.ContextMenu.PlacementTarget = ExportButton;
        ExportButton.ContextMenu.IsOpen = true;
    }

    private void ExportSelectedMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLinesViewModel vm && vm.ExportSelectedCommand.CanExecute(null))
            vm.ExportSelectedCommand.Execute(null);
    }

    private void ExportAllMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLinesViewModel vm && vm.ExportAllCommand.CanExecute(null))
            vm.ExportAllCommand.Execute(null);
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
