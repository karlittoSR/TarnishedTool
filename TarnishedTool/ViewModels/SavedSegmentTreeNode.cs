//

using System.Collections.ObjectModel;
using System.ComponentModel;
using TarnishedTool.Models;

namespace TarnishedTool.ViewModels;

public enum SavedSegmentDropPlacement
{
    Before,
    After,
    Inside,
    RootEnd,
}

// UI-only node. The persisted folder/entry models stay independent from WPF,
// while this wrapper supplies expansion, selection and a shared child collection.
public sealed class SavedSegmentTreeNode : BaseViewModel
{
    public SavedSegmentTreeNode(SavedSegmentFolder folder)
    {
        Folder = folder;
    }

    public SavedSegmentTreeNode(SavedSegmentLibraryEntry entry)
    {
        Entry = entry;
        if (entry?.Segment != null)
            entry.Segment.PropertyChanged += Segment_PropertyChanged;
    }

    public SavedSegmentFolder Folder { get; }
    public SavedSegmentLibraryEntry Entry { get; }
    public SavedLine Segment => Entry?.Segment;
    public bool IsFolder => Folder != null;
    public string Name => IsFolder ? Folder.Name : Segment?.Name ?? string.Empty;
    public string Icon => IsFolder ? "📁" : string.Empty;
    public string ReferenceText => IsFolder ? string.Empty : Segment?.ReferenceText ?? string.Empty;
    public string BestText => IsFolder ? string.Empty : Segment?.BestText ?? string.Empty;
    public int Order => IsFolder ? Folder.Order : Entry?.Order ?? 0;

    public SavedSegmentTreeNode Parent { get; set; }
    public ObservableCollection<SavedSegmentTreeNode> Children { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private bool _isDropBefore;
    public bool IsDropBefore
    {
        get => _isDropBefore;
        set => SetProperty(ref _isDropBefore, value);
    }

    private bool _isDropAfter;
    public bool IsDropAfter
    {
        get => _isDropAfter;
        set => SetProperty(ref _isDropAfter, value);
    }

    private bool _isDropInside;
    public bool IsDropInside
    {
        get => _isDropInside;
        set => SetProperty(ref _isDropInside, value);
    }

    public void SetDropHint(SavedSegmentDropPlacement? placement)
    {
        IsDropBefore = placement == SavedSegmentDropPlacement.Before;
        IsDropAfter = placement == SavedSegmentDropPlacement.After;
        IsDropInside = placement == SavedSegmentDropPlacement.Inside;
    }

    public void RefreshName() => OnPropertyChanged(nameof(Name));

    private void Segment_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SavedLine.Name)) OnPropertyChanged(nameof(Name));
        if (e.PropertyName == nameof(SavedLine.ReferenceText)) OnPropertyChanged(nameof(ReferenceText));
        if (e.PropertyName == nameof(SavedLine.BestText)) OnPropertyChanged(nameof(BestText));
    }
}
