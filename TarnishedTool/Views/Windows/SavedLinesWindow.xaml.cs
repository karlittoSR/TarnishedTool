//

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TarnishedTool.Utilities;
using TarnishedTool.ViewModels;

namespace TarnishedTool.Views.Windows;

public partial class SavedLinesWindow : TopmostWindow
{
    private SavedSegmentTreeNode _contextNode;
    private Point _dragStartPoint;
    private SavedSegmentTreeNode _draggedNode;
    private SavedSegmentTreeNode _pressedFolder;
    private SavedSegmentTreeNode _dropTarget;
    private SavedSegmentDropPlacement? _dropPlacement;

    private void SavedSegmentsTree_SelectedItemChanged(
        object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is SavedLinesViewModel vm)
            vm.SelectTreeNode(e.NewValue as SavedSegmentTreeNode);

        // Mouse selection already has a realized container, while F7/F8 changes
        // the view model first. Defer one UI pass so expanded parent folders and
        // the selected child container exist before asking the ScrollViewer to
        // reveal it. BringIntoView only scrolls when the row is outside the view.
        if (e.NewValue is SavedSegmentTreeNode selected)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                FindContainer(SavedSegmentsTree, selected)?.BringIntoView()));
    }

    // A simple click anywhere on a folder header opens/closes it. Clicking the
    // standard disclosure arrow is left to WPF so it is never toggled twice.
    private void SavedSegmentsTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(SavedSegmentsTree);
        _draggedNode = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)
            ?.DataContext as SavedSegmentTreeNode;
        _pressedFolder = null;
        if (e.ClickCount != 1) return;
        var source = e.OriginalSource as DependencyObject;
        var item = FindAncestor<TreeViewItem>(source);
        if (item == null)
        {
            if (DataContext is SavedLinesViewModel vm) vm.SelectTreeNode(null);
            return;
        }
        if (FindAncestor<ToggleButton>(source) != null) return;
        if (item.DataContext is SavedSegmentTreeNode { IsFolder: true } node)
            _pressedFolder = node;
    }

    private void SavedSegmentsTree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (_pressedFolder != null && item?.DataContext == _pressedFolder)
            _pressedFolder.IsExpanded = !_pressedFolder.IsExpanded;
        _pressedFolder = null;
    }

    private void SavedSegmentsTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedNode == null) return;
        var current = e.GetPosition(SavedSegmentsTree);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var dragged = _draggedNode;
        _pressedFolder = null;
        try
        {
            DragDrop.DoDragDrop(SavedSegmentsTree, dragged, DragDropEffects.Move);
        }
        finally
        {
            ClearDropHint();
            _draggedNode = null;
        }
    }

    private void SavedSegmentsTree_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(SavedSegmentTreeNode)) is not SavedSegmentTreeNode dragged
            || DataContext is not SavedLinesViewModel vm)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        var target = item?.DataContext as SavedSegmentTreeNode;
        var placement = DropPlacement(item, target, e);
        bool allowed = vm.CanMoveTreeNode(dragged, target, placement);
        ShowDropHint(allowed ? target : null, allowed ? placement : (SavedSegmentDropPlacement?)null);
        e.Effects = allowed ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void SavedSegmentsTree_DragLeave(object sender, DragEventArgs e)
    {
        // IsMouseOver can briefly become false while WPF transfers the drag
        // between two item containers. Only clear after the pointer has really
        // crossed the outer bounds of the tree.
        var point = e.GetPosition(SavedSegmentsTree);
        if (point.X < 0 || point.Y < 0
            || point.X > SavedSegmentsTree.ActualWidth
            || point.Y > SavedSegmentsTree.ActualHeight)
            ClearDropHint();
    }

    private void SavedSegmentsTree_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(SavedSegmentTreeNode)) is SavedSegmentTreeNode dragged
            && DataContext is SavedLinesViewModel vm
            && _dropPlacement is SavedSegmentDropPlacement placement)
            vm.MoveTreeNode(dragged, _dropTarget, placement);

        ClearDropHint();
        e.Handled = true;
    }

    private SavedSegmentDropPlacement DropPlacement(TreeViewItem item,
        SavedSegmentTreeNode target, DragEventArgs e)
    {
        if (item == null || target == null) return SavedSegmentDropPlacement.RootEnd;
        var header = item.Template.FindName("HeaderBorder", item) as FrameworkElement ?? item;
        double height = Math.Max(1, header.ActualHeight);
        double y = e.GetPosition(header).Y;

        if (target.IsFolder)
        {
            if (y < height * 0.25) return SavedSegmentDropPlacement.Before;
            if (y > height * 0.75) return SavedSegmentDropPlacement.After;
            return SavedSegmentDropPlacement.Inside;
        }
        return y < height * 0.5
            ? SavedSegmentDropPlacement.Before
            : SavedSegmentDropPlacement.After;
    }

    private void ShowDropHint(SavedSegmentTreeNode target, SavedSegmentDropPlacement? placement)
    {
        // At a row boundary WPF can alternate rapidly between "after A" and
        // "before B". They are the exact same insertion point, so retain the
        // already-rendered indicator instead of hiding and recreating it.
        if (IsSameBoundary(_dropTarget, _dropPlacement, target, placement)) return;

        if (_dropTarget != target) _dropTarget?.SetDropHint(null);
        _dropTarget = target;
        _dropPlacement = placement;
        target?.SetDropHint(placement);
        RootDropIndicator.Visibility = placement == SavedSegmentDropPlacement.RootEnd
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsSameBoundary(SavedSegmentTreeNode oldTarget,
        SavedSegmentDropPlacement? oldPlacement, SavedSegmentTreeNode newTarget,
        SavedSegmentDropPlacement? newPlacement)
    {
        if (oldTarget == null || newTarget == null || oldTarget == newTarget) return false;

        if (oldPlacement == SavedSegmentDropPlacement.After
            && newPlacement == SavedSegmentDropPlacement.Before)
            return AreConsecutiveSiblings(oldTarget, newTarget);

        if (oldPlacement == SavedSegmentDropPlacement.Before
            && newPlacement == SavedSegmentDropPlacement.After)
            return AreConsecutiveSiblings(newTarget, oldTarget);

        return false;
    }

    private bool AreConsecutiveSiblings(SavedSegmentTreeNode first, SavedSegmentTreeNode second)
    {
        if (first.Parent != second.Parent) return false;
        var siblings = first.Parent?.Children
                       ?? (DataContext as SavedLinesViewModel)?.RootNodes;
        if (siblings == null) return false;
        int firstIndex = siblings.IndexOf(first);
        return firstIndex >= 0 && firstIndex + 1 < siblings.Count
                               && siblings[firstIndex + 1] == second;
    }

    private void ClearDropHint()
    {
        _dropTarget?.SetDropHint(null);
        _dropTarget = null;
        _dropPlacement = null;
        RootDropIndicator.Visibility = Visibility.Collapsed;
    }

    private void SavedSegmentsTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        _contextNode = item?.DataContext as SavedSegmentTreeNode;
        if (item != null)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void SavedSegmentsTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        bool folder = _contextNode?.IsFolder == true;
        AddFolderMenuItem.Header = folder ? "📁 Add Subfolder" : "📁 Add Folder";
        FolderActionsSeparator.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
        RenameFolderMenuItem.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
        DeleteFolderMenuItem.Visibility = folder ? Visibility.Visible : Visibility.Collapsed;
    }

    // Double-clicking a segment loads it. Folders only expand/collapse.
    private void SavedSegmentsTree_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext
                is not SavedSegmentTreeNode node)
            return;

        // The first click already toggled a folder. Suppress TreeViewItem's own
        // double-click expansion so a double click cannot immediately undo it.
        if (node.IsFolder)
        {
            e.Handled = true;
            return;
        }

        if (DataContext is SavedLinesViewModel vm && vm.LoadCommand.CanExecute(null))
        {
            vm.SelectTreeNode(node);
            vm.LoadCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void AddFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLinesViewModel vm) vm.AddFolder(_contextNode);
    }

    private void RenameFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLinesViewModel vm) vm.RenameFolder(_contextNode);
    }

    private void DeleteFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLinesViewModel vm) vm.DeleteFolder(_contextNode);
    }

    private static T FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        for (var current = source; current != null; current = ParentOf(current))
            if (current is T result) return result;
        return null;
    }

    private static TreeViewItem FindContainer(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
            return direct;

        foreach (var childItem in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(childItem) is not TreeViewItem child)
                continue;
            var nested = FindContainer(child, item);
            if (nested != null) return nested;
        }
        return null;
    }

    private static DependencyObject ParentOf(DependencyObject source)
    {
        if (source is Visual || source is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(source);
        return LogicalTreeHelper.GetParent(source);
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
