//

using System.Collections.ObjectModel;
using System.Windows.Input;
using TarnishedTool.Core;
using TarnishedTool.Models;
using TarnishedTool.Utilities;

namespace TarnishedTool.ViewModels;

public class SavedLinesViewModel : BaseViewModel
{
    private readonly LineComparisonViewModel _lineComparison;

    public ObservableCollection<SavedLine> Lines { get; } = new();

    public ICommand LoadCommand { get; }
    public ICommand SaveCurrentCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }

    public SavedLinesViewModel(LineComparisonViewModel lineComparison)
    {
        _lineComparison = lineComparison;

        foreach (var line in SavedLinesStore.Load())
            Lines.Add(line);

        LoadCommand = new DelegateCommand(LoadSelected);
        SaveCurrentCommand = new DelegateCommand(SaveCurrent);
        RenameCommand = new DelegateCommand(RenameSelected);
        DeleteCommand = new DelegateCommand(DeleteSelected);
    }

    private SavedLine _selectedLine;
    public SavedLine SelectedLine
    {
        get => _selectedLine;
        set => SetProperty(ref _selectedLine, value);
    }

    private void LoadSelected()
    {
        if (SelectedLine == null) return;
        if (!_lineComparison.LoadSavedLine(SelectedLine))
            MsgBox.Show("This saved line's code is invalid and could not be loaded.");
    }

    private void SaveCurrent()
    {
        var code = _lineComparison.ExportCurrentCode();
        if (code == null)
        {
            MsgBox.Show("Set a start and an end first, then save.");
            return;
        }

        var name = MsgBox.ShowInput("Name this line:", "", "Save Line");
        if (string.IsNullOrWhiteSpace(name)) return;

        var line = new SavedLine(name.Trim(), code, _lineComparison.GetCurrentBestMs());
        Lines.Add(line);
        Persist();

        // Track the freshly saved line so the first time you get on it updates its PB.
        _lineComparison.SetActiveSavedLine(line);
        SelectedLine = line;
    }

    private void RenameSelected()
    {
        if (SelectedLine == null) return;

        var name = MsgBox.ShowInput("New name:", SelectedLine.Name, "Rename Line");
        if (string.IsNullOrWhiteSpace(name)) return;

        SelectedLine.Name = name.Trim();
        Persist();
    }

    private void DeleteSelected()
    {
        if (SelectedLine == null) return;
        if (!MsgBox.ShowYesNo($"Delete \"{SelectedLine.Name}\"?", "Delete Line")) return;

        Lines.Remove(SelectedLine);
        Persist();
    }

    public void Persist() => SavedLinesStore.Save(Lines);
}
