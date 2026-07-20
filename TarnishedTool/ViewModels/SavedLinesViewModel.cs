//

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using TarnishedTool.Core;
using TarnishedTool.Interfaces;
using TarnishedTool.Models;
using TarnishedTool.Utilities;

namespace TarnishedTool.ViewModels;

public class SavedLinesViewModel : BaseViewModel
{
    private readonly LineComparisonViewModel _lineComparison;
    private readonly ICharacterSnapshotService _characterSnapshotService;

    public ObservableCollection<SavedLine> Lines { get; } = new();

    public ICommand LoadCommand { get; }
    public ICommand SaveCurrentCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ApplyCharacterCommand { get; }

    public SavedLinesViewModel(LineComparisonViewModel lineComparison,
        ICharacterSnapshotService characterSnapshotService = null)
    {
        _lineComparison = lineComparison;
        _characterSnapshotService = characterSnapshotService;

        // Mirror the load state so the buttons grey out in the main menu.
        _lineComparison.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LineComparisonViewModel.CanOperate))
                OnPropertyChanged(nameof(CanOperate));
        };

        // Ash of war cannot be read off a mounted weapon, so it is chosen here and
        // stored with the save. "Default" means the weapon's class default, which
        // is what a spawned weapon gets when nothing is chosen.
        AshOptions.Add(new AshOfWar { Id = -1, Name = "Default (weapon's own)" });
        foreach (var ash in DataLoader.GetAshOfWars().OrderBy(a => a.Name))
            AshOptions.Add(ash);

        foreach (var weapon in DataLoader.GetWeapons())
            _weaponNames[(uint)weapon.Id - ((uint)weapon.Id % 10000)] = weapon.Name;

        foreach (var line in SavedLinesStore.Load())
            Lines.Add(line);

        LoadCommand = new DelegateCommand(LoadSelected);
        SaveCurrentCommand = new DelegateCommand(SaveCurrent);
        UpdateCommand = new DelegateCommand(UpdateSelected);
        RenameCommand = new DelegateCommand(RenameSelected);
        DeleteCommand = new DelegateCommand(DeleteSelected);
        ApplyCharacterCommand = new DelegateCommand(ApplyCharacter);
    }

    private SavedLine _selectedLine;
    public SavedLine SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (!SetProperty(ref _selectedLine, value)) return;
            RefreshLineWeapons();
        }
    }

    #region Ash of war

    // A weapon inside the selected save, paired with the ash chosen for it.
    public class LineWeapon
    {
        public EquippedItem Item { get; set; }
        public string Display { get; set; }
        public override string ToString() => Display;
    }

    private static readonly string[] WeaponSlotNames =
        { "L.Hand 1", "R.Hand 1", "L.Hand 2", "R.Hand 2", "L.Hand 3", "R.Hand 3" };

    public ObservableCollection<LineWeapon> LineWeapons { get; } = new();

    // "Default" first, so clearing a choice is obvious.
    public ObservableCollection<AshOfWar> AshOptions { get; } = new();

    private LineWeapon _selectedLineWeapon;
    public LineWeapon SelectedLineWeapon
    {
        get => _selectedLineWeapon;
        set
        {
            if (!SetProperty(ref _selectedLineWeapon, value)) return;
            // Show whatever ash this weapon already carries.
            _selectedAsh = AshOptions.FirstOrDefault(a => a.Id == (value?.Item.AshOfWarId ?? -1))
                           ?? AshOptions.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedAsh));
        }
    }

    private AshOfWar _selectedAsh;
    public AshOfWar SelectedAsh
    {
        get => _selectedAsh;
        set
        {
            if (!SetProperty(ref _selectedAsh, value)) return;
            if (SelectedLineWeapon?.Item == null || value == null) return;

            SelectedLineWeapon.Item.AshOfWarId = value.Id;
            Persist();
        }
    }

    private void RefreshLineWeapons()
    {
        LineWeapons.Clear();

        var items = SelectedLine?.Snapshot?.Equipment?.Items;
        if (items != null)
        {
            foreach (var item in items.Where(i => i.Slot < WeaponSlotNames.Length).OrderBy(i => i.Slot))
            {
                var name = _weaponNames.TryGetValue(BaseWeaponId(item.ItemId), out var n) ? n : "Unknown";
                LineWeapons.Add(new LineWeapon
                {
                    Item = item,
                    Display = $"{WeaponSlotNames[item.Slot]} — {name}"
                });
            }
        }

        SelectedLineWeapon = LineWeapons.FirstOrDefault();
    }

    private static uint BaseWeaponId(uint itemId) => itemId - (itemId % 10000);

    private readonly Dictionary<uint, string> _weaponNames = new();

    #endregion

    // True only while a character is actually loaded. Everything here reads or
    // writes player memory, so acting from the main menu crashes the game — the
    // pointers it walks (PlayerGameData, ChrIns, the inventory) do not exist yet.
    public bool CanOperate => _lineComparison.CanOperate;

    // Guards every action that touches the game. Returns false (and says why)
    // when there is nothing loaded to act on.
    private bool RequireLoadedGame()
    {
        if (CanOperate) return true;
        MsgBox.Show("Load into the game first — saves can't be used from the main menu.");
        return false;
    }

    // Loading a save also puts you on its start: the line goes into the timer and
    // is then restored, so a save is one click (or double-click) from being ready
    // to run — zone reset, character state and all. LoadSavedLine sets the active
    // line first, which is what RestoreToStart reads to apply the snapshot.
    private void LoadSelected()
    {
        if (SelectedLine == null || !RequireLoadedGame()) return;
        if (!_lineComparison.LoadSavedLine(SelectedLine))
        {
            MsgBox.Show("This save's line code is invalid and could not be loaded.");
            return;
        }

        _lineComparison.RestoreToStart();
    }

    private void SaveCurrent()
    {
        if (!RequireLoadedGame()) return;

        var code = _lineComparison.ExportCurrentCode();
        if (code == null)
        {
            MsgBox.Show("Set a start and an end first, then save.");
            return;
        }

        var name = MsgBox.ShowInput("Name this line:", "", "Save Line");
        if (string.IsNullOrWhiteSpace(name)) return;

        var line = new SavedLine(name.Trim(), code, _lineComparison.GetCurrentBestMs())
        {
            // Capture the full character state (equipment + stats) alongside the
            // line so it can be restored on load.
            Snapshot = _characterSnapshotService?.Capture()
        };
        Lines.Add(line);
        Persist();

        // Track the freshly saved line so the first time you get on it updates its PB.
        _lineComparison.SetActiveSavedLine(line);
        SelectedLine = line;
    }

    // Re-captures the current line definition and character state onto the
    // selected save in place, keeping its name and PB.
    private void UpdateSelected()
    {
        if (SelectedLine == null || !RequireLoadedGame()) return;
        if (_characterSnapshotService == null)
        {
            MsgBox.Show("Character snapshots are unavailable (game not attached).");
            return;
        }

        var code = _lineComparison.ExportCurrentCode();
        if (code == null)
        {
            MsgBox.Show("Set a start and an end first, then update.");
            return;
        }

        if (!MsgBox.ShowYesNo(
                $"Update \"{SelectedLine.Name}\" with the current positions, radii and character state (gear + stats + flasks)?",
                "Update Line"))
            return;

        var refreshed = _characterSnapshotService.Capture();

        // Ash of war is authored by hand in lines.json (it cannot be read off a
        // mounted weapon), so carry it over rather than losing it on every Update.
        refreshed?.Equipment?.PreserveAshFrom(SelectedLine.Snapshot?.Equipment);

        SelectedLine.UpdateCode(code);
        SelectedLine.Snapshot = refreshed;
        Persist();

        // Track it as active so subsequent attempts keep updating its PB.
        _lineComparison.SetActiveSavedLine(SelectedLine, ensurePersistentPbRow: true);
    }

    // Applies the selected line's captured character state (stats + equipment).
    private void ApplyCharacter()
    {
        if (SelectedLine == null || !RequireLoadedGame()) return;
        if (_characterSnapshotService == null) return;
        if (SelectedLine.Snapshot == null)
        {
            MsgBox.Show("This line has no saved character state.");
            return;
        }
        var errors = _characterSnapshotService.Apply(SelectedLine.Snapshot);
        if (!string.IsNullOrWhiteSpace(errors))
        {
            try { System.Windows.Clipboard.SetDataObject(errors, true); } catch { }
            MsgBox.Show(errors + "\n(copied to clipboard)", "Apply Character");
        }
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
