using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Input;
using TarnishedTool.Core;
using TarnishedTool.Enums;
using TarnishedTool.Interfaces;
using TarnishedTool.Models;
using TarnishedTool.Utilities;
using TarnishedTool.Views.Windows;

namespace TarnishedTool.ViewModels;

public class AdvancedViewModel : BaseViewModel
{
    private readonly IItemService _itemService;

    private readonly ParamEditorViewModel _paramEditorViewModel;

    private readonly ISpEffectService _spEffectService;
    private readonly SpEffectViewModel _spEffectViewModel = new();
    private SpEffectsWindow _spEffectsWindow;

    private readonly HotkeyManager _hotkeyManager;
    private readonly IGameTickService _gameTickService;
    private readonly IReminderService _reminderService;
    private readonly IAiService _aiService;

    private readonly IUtilityService _utilityService;
    private readonly ChrInsWindowViewModel _chrInsWindowViewModel;
    private ChrInsWindow _chrInsWindow;

    private readonly LineComparisonViewModel _lineComparisonViewModel;
    private LineComparisonWindow _lineComparisonWindow;

    // Exposed so MainWindow can wire the in-place zone reset (EnemyViewModel).
    public LineComparisonViewModel LineComparison => _lineComparisonViewModel;

    private ParamEditorWindow _paramEditorWindow;

    private readonly IPlayerService _playerService;
    private readonly IHotkeyNotificationService _notificationService;
    private readonly IEquipService _equipService;
    private readonly ICharacterSnapshotService _characterSnapshotService;

    private bool _hasNotifiedInitialOpen;

    public AdvancedViewModel(IItemService itemService, IStateService stateService,
        IParamService paramService, IParamRepository paramRepository, ISpEffectService spEffectService,
        IPlayerService playerService, HotkeyManager hotkeyManager, IGameTickService gameTickService,
        IReminderService reminderService, IAiService aiService, IUtilityService utilityService,
        IChrInsService chrInsService, IAiWindowService aiWindowService, IHotkeyNotificationService notificationService = null,
        IEquipService equipService = null, ICharacterSnapshotService characterSnapshotService = null)
    {
        _itemService = itemService;
        _equipService = equipService;
        _characterSnapshotService = characterSnapshotService;
        _spEffectService = spEffectService;
        _playerService = playerService;
        _hotkeyManager = hotkeyManager;
        _gameTickService = gameTickService;
        _reminderService = reminderService;
        _aiService = aiService;
        _utilityService = utilityService;
        _hotkeyManager = hotkeyManager;
        _notificationService = notificationService;

        RegisterHotkeys();

        stateService.Subscribe(State.Loaded, OnGameLoaded);
        stateService.Subscribe(State.NotLoaded, OnGameNotLoaded);

        SpawnWithEquipIdCommand = new DelegateCommand(SpawnWithEquipId);
        SpawnAndEquipCommand = new DelegateCommand(SpawnAndEquip);
        ScanEquippedIdCommand = new DelegateCommand(ScanEquippedId);
        CaptureEquipmentCommand = new DelegateCommand(CaptureEquipment);
        SaveEquipmentCommand = new DelegateCommand(SaveEquipment);
        RestoreEquipmentCommand = new DelegateCommand(RestoreEquipment);
        SaveCharacterCommand = new DelegateCommand(SaveCharacter);
        RestoreCharacterCommand = new DelegateCommand(RestoreCharacter);
        OpenParamEditorCommand = new DelegateCommand(OpenParamEditor);
        ApplySpEffectCommand = new DelegateCommand(ApplySpEffect);
        RemoveSpEffectCommand = new DelegateCommand(RemoveSpEffect);
        AboutSpEffectsCommand = new DelegateCommand(ShowAboutSpEffects);
        OpenAiWindowCommand = new DelegateCommand(OpenAiWindow);
        InjectScriptCommand = new DelegateCommand(InjectScript);
        OpenLineComparisonCommand = new DelegateCommand(OpenLineComparison);

        SelectedEquipType = EquipTypes[0].Value;

        _paramEditorViewModel = new ParamEditorViewModel(paramRepository, paramService, reminderService);
        _chrInsWindowViewModel = new ChrInsWindowViewModel(stateService, gameTickService, playerService, chrInsService, aiWindowService);
        _lineComparisonViewModel = new LineComparisonViewModel(gameTickService, playerService, stateService, characterSnapshotService);
    }

    
    #region Commands

    public ICommand SpawnWithEquipIdCommand { get; set; }
    public ICommand SpawnAndEquipCommand { get; set; }
    public ICommand ScanEquippedIdCommand { get; set; }
    public ICommand CaptureEquipmentCommand { get; set; }
    public ICommand SaveEquipmentCommand { get; set; }
    public ICommand RestoreEquipmentCommand { get; set; }
    public ICommand SaveCharacterCommand { get; set; }
    public ICommand RestoreCharacterCommand { get; set; }
    public ICommand OpenParamEditorCommand { get; set; }
    public ICommand ApplySpEffectCommand { get; set; }
    public ICommand RemoveSpEffectCommand { get; set; }
    public ICommand AboutSpEffectsCommand { get; set; }
    public ICommand OpenAiWindowCommand { get; set; }
    public ICommand InjectScriptCommand { get; set; }
    public ICommand OpenLineComparisonCommand { get; set; }

    #endregion

    #region Properties

    public ObservableCollection<KeyValuePair<string, uint>> EquipTypes { get; } = new()
    {
        new("Accessory", 0x20000000),
        new("Gem", 0x80000000),
        new("Goods", 0x40000000),
        new("Protector", 0x10000000),
        new("Weapon", 0x00000000)
    };

    private uint _selectedEquipType;

    public uint SelectedEquipType
    {
        get => _selectedEquipType;
        set => SetProperty(ref _selectedEquipType, value);
    }

    private string _equipId;

    public string EquipId
    {
        get => _equipId;
        set => SetProperty(ref _equipId, value);
    }

    // Equip POC: target ChrAsm slot (0=L-hand1, 1=R-hand1, 6=arrow1, 7=bolt1,
    // 12=head, 13=chest, 14=legs, 15=hands, 17-20=talismans).
    private string _equipSlot = "1";

    public string EquipSlot
    {
        get => _equipSlot;
        set => SetProperty(ref _equipSlot, value);
    }

    private bool _areOptionsEnabled;

    public bool AreOptionsEnabled
    {
        get => _areOptionsEnabled;
        set => SetProperty(ref _areOptionsEnabled, value);
    }
    
    private string _applySpEffectId;

    public string ApplySpEffectId
    {
        get => _applySpEffectId;
        set => SetProperty(ref _applySpEffectId, value);
    }

    private string _removeSpEffectId;

    public string RemoveSpEffectId
    {
        get => _removeSpEffectId;
        set => SetProperty(ref _removeSpEffectId, value);
    }
    
    private bool _isSpEffectWindowOpen;
    
    public bool IsSpEffectWindowOpen
    {
        get => _isSpEffectWindowOpen;
        set
        {
            if (SetProperty(ref _isSpEffectWindowOpen, value))
            {
                if (_isSpEffectWindowOpen)
                {
                    OpenSpEffectsWindow();
                    _gameTickService.Subscribe(SpEffectsTick);
                }
                else
                {
                    _gameTickService.Unsubscribe(SpEffectsTick);
                }
            }
        }
    }

    #endregion

    #region Private Methods

    private void OnGameLoaded()
    {
        AreOptionsEnabled = true;
        if (IsSpEffectWindowOpen) _gameTickService.Subscribe(SpEffectsTick);
    }

    private void RegisterHotkeys()
    {
        _hotkeyManager.RegisterAction(HotkeyActions.ApplySpEffect, () => { SafeExecute(ApplySpEffect); _notificationService?.ShowNotification(HotkeyActions.ApplySpEffect); });
        _hotkeyManager.RegisterAction(HotkeyActions.RemoveSpEffect, () => { SafeExecute(RemoveSpEffect); _notificationService?.ShowNotification(HotkeyActions.RemoveSpEffect); });
        _hotkeyManager.RegisterAction(HotkeyActions.SpawnCustomItem,() => { SafeExecute(SpawnWithEquipId); _notificationService?.ShowNotification(HotkeyActions.SpawnCustomItem); });
        _hotkeyManager.RegisterAction(HotkeyActions.OpenParamPatcher, () => { SafeExecute(OpenParamEditor); _notificationService?.ShowNotification(HotkeyActions.OpenParamPatcher); });
        _hotkeyManager.RegisterAction(HotkeyActions.OpenCharactersList, () => { SafeExecute(OpenAiWindow); _notificationService?.ShowNotification(HotkeyActions.OpenCharactersList); });
        _hotkeyManager.RegisterAction(HotkeyActions.InjectAiScript, () => { SafeExecute(InjectScript); _notificationService?.ShowNotification(HotkeyActions.InjectAiScript); });

        _hotkeyManager.RegisterAction(HotkeyActions.OpenLineComparison, () => { SafeExecute(OpenLineComparison); _notificationService?.ShowNotification(HotkeyActions.OpenLineComparison); });
        _hotkeyManager.RegisterAction(HotkeyActions.SetLineStart, () => { SafeExecute(() => _lineComparisonViewModel.SetStart()); _notificationService?.ShowNotification(HotkeyActions.SetLineStart); });
        _hotkeyManager.RegisterAction(HotkeyActions.SetLineEnd, () => { SafeExecute(() => _lineComparisonViewModel.SetEnd()); _notificationService?.ShowNotification(HotkeyActions.SetLineEnd); });
        _hotkeyManager.RegisterAction(HotkeyActions.RestoreLineStart, () => { SafeExecute(() => _lineComparisonViewModel.RestoreToStart()); _notificationService?.ShowNotification(HotkeyActions.RestoreLineStart); });
    }

    private void SafeExecute(Action action)
    {
        if (!AreOptionsEnabled) return;
        action();
    }

    private void OnGameNotLoaded()
    {
        AreOptionsEnabled = false;
        if (IsSpEffectWindowOpen) _gameTickService.Unsubscribe(SpEffectsTick);
    }

    private void SpawnWithEquipId()
    {
        if (!uint.TryParse(EquipId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint equipId))
        {
            MsgBox.Show("Invalid Equip ID");
            return;
        }

        uint itemId = equipId + SelectedEquipType;
        _itemService.SpawnItem((int)itemId, 1, -1, false, 1);
    }

    // Equip proof-of-concept: spawn the item (proven path), then equip it into
    // the chosen slot via the ported equip mechanism.
    private void SpawnAndEquip()
    {
        if (_equipService == null || !_equipService.IsAvailable)
        {
            var info = _equipService != null ? "\n\n" + _equipService.ResolutionInfo : "";
            MsgBox.Show("Equip functions were not found for this game version (AOB scan failed)." + info);
            return;
        }

        if (!uint.TryParse(EquipId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint equipId))
        {
            MsgBox.Show("Invalid Equip ID");
            return;
        }

        if (!int.TryParse(EquipSlot.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot))
        {
            MsgBox.Show("Invalid slot");
            return;
        }

        uint itemId = equipId + SelectedEquipType;
        _itemService.SpawnItem((int)itemId, 1, -1, false, 1);
        _equipService.Equip(itemId, slot);
    }

    // Discovery helper for finding the ChrAsm equipped-item array offset: equip a
    // known item first, then scan PlayerGameData for its id. Run once per slot and
    // diff the offsets — the one that moves by the array stride is the equip array.
    private void ScanEquippedId()
    {
        if (_equipService == null)
        {
            MsgBox.Show("Equip service unavailable.");
            return;
        }

        if (!uint.TryParse(EquipId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint equipId))
        {
            MsgBox.Show("Invalid Equip ID");
            return;
        }

        uint fullId = equipId + SelectedEquipType;
        const int range = 0x2000;

        var fullHits = _equipService.FindValueOffsets(fullId, range);
        var bareHits = _equipService.FindValueOffsets(equipId, range);

        string Format(IReadOnlyList<int> hits) =>
            hits.Count == 0 ? "none" : string.Join(", ", hits.Select(o => $"+0x{o:X}"));

        MsgBox.Show(
            $"Scanned PlayerGameData first 0x{range:X} bytes.\n\n" +
            $"Full id 0x{fullId:X} ({fullId}):\n{Format(fullHits)}\n\n" +
            $"Bare id 0x{equipId:X} ({equipId}):\n{Format(bareHits)}",
            "Equipped-ID offsets");
    }

    private static readonly string[] EquipSlotNames =
    {
        "L.Weapon 1", "R.Weapon 1", "L.Weapon 2", "R.Weapon 2", "L.Weapon 3", "R.Weapon 3",
        "Arrow 1", "Bolt 1", "Arrow 2", "Bolt 2", "Arrow 3", "Bolt 3",
        "Head", "Chest", "Arms", "Legs", "Hair",
        "Talisman 1", "Talisman 2", "Talisman 3", "Talisman 4", "Talisman 5"
    };

    // Reads and displays the currently-equipped items (validates capture).
    private void CaptureEquipment()
    {
        if (_equipService == null)
        {
            MsgBox.Show("Equip service unavailable.");
            return;
        }

        var equipped = _equipService.CaptureEquipment();
        if (equipped.Items.Count == 0)
        {
            MsgBox.Show("No equipped items read (all slots empty, or offset mismatch on this version).");
            return;
        }

        var lines = equipped.Items.Select(e =>
        {
            var name = e.Slot >= 0 && e.Slot < EquipSlotNames.Length ? EquipSlotNames[e.Slot] : $"Slot {e.Slot}";
            return $"{name} (slot {e.Slot}): {e.ItemId}";
        }).ToList();
        lines.Add($"\nArmStyle: {equipped.ArmStyle}   Sel: [{string.Join(",", equipped.WeaponSlotSelections)}]   Pouches: {equipped.TalismanPouchCount}");

        MsgBox.Show(string.Join("\n", lines), "Currently equipped");
    }

    // Equipment round-trip POC: snapshot the current equipment in memory, then
    // re-apply it (spawn + equip each slot) to validate the full save/restore path.
    private Models.EquipmentSnapshot _savedEquipment;

    private void SaveEquipment()
    {
        if (_equipService == null) return;
        _savedEquipment = _equipService.CaptureEquipment();
        MsgBox.Show($"Saved {_savedEquipment.Items.Count} equipped item(s).", "Save Equipment");
    }

    private void RestoreEquipment()
    {
        if (_equipService == null) return;
        if (_savedEquipment == null || _savedEquipment.Items.Count == 0)
        {
            MsgBox.Show("Nothing saved yet. Click Save Equipment first.");
            return;
        }
        _equipService.ApplyEquipment(_savedEquipment);
    }

    // Character round-trip POC: equipment + stats + rune level.
    private Models.CharacterSnapshot _savedCharacter;

    private void SaveCharacter()
    {
        if (_characterSnapshotService == null) return;
        _savedCharacter = _characterSnapshotService.Capture();
        var s = _savedCharacter.Stats;
        MsgBox.Show(
            $"Saved character.\nRL {_savedCharacter.RuneLevel}  " +
            $"VIG {s.Vigor} MND {s.Mind} END {s.Endurance} STR {s.Strength} " +
            $"DEX {s.Dexterity} INT {s.Intelligence} FTH {s.Faith} ARC {s.Arcane}\n" +
            $"{_savedCharacter.Equipment.Items.Count} equipped item(s).",
            "Save Character");
    }

    private void RestoreCharacter()
    {
        if (_characterSnapshotService == null) return;
        if (_savedCharacter == null)
        {
            MsgBox.Show("Nothing saved yet. Click Save Character first.");
            return;
        }
        _characterSnapshotService.Apply(_savedCharacter);
    }

    private void OpenParamEditor()
    {
        if (_paramEditorWindow != null && _paramEditorWindow.IsVisible)
        {
            _paramEditorWindow.Activate();
            return;
        }

        _paramEditorWindow = new ParamEditorWindow
        {
            Title = "Param Editor",
            DataContext = _paramEditorViewModel
        };

        _paramEditorWindow.Closed += (_, _) => _paramEditorWindow = null;
        _paramEditorWindow.Show();
        if (!_hasNotifiedInitialOpen)
        {
            _paramEditorViewModel.NotifyInitialWindowOpened();
            _hasNotifiedInitialOpen = true;
        }
    }
    
    private void OpenSpEffectsWindow()
    {
        if (_spEffectsWindow != null && _spEffectsWindow.IsVisible)
        {
            _spEffectsWindow.Activate();
            return;
        }
        
        
        _spEffectsWindow = new SpEffectsWindow
        {
            DataContext = _spEffectViewModel,
            Title = "Player Active Special Effects"
        };
        _spEffectsWindow.Closed += (s, e) =>
        {
            _spEffectsWindow = null;
            IsSpEffectWindowOpen = false;
        };
        _spEffectsWindow.Show();
    }

    
    private void ShowAboutSpEffects()
    {
        MsgBox.Show(
            "To put it simply Special Effects are effects that get applied to every entity in the game in order to achieve a specific goal in mind, that goal can quite literally be anything the devs have in mind. For example you can lock the player in a certain area, activate the effect of a talisman after the player equips it, apply a buff to the player. You can can also force a boss to follow up a specific move after an attack or trigger an entire phase through it. spEffects also control the hp and damage scaling of enemies and many more things that it's hard to explain in a small info box. If you want to learn about this I would recommend you check out Smithbox by Vawser and slowly get a grasp on how things work as most things are annotated thanks to the community effort so it will be a little easier to navigate.",
            "About Special Effects");
    }

    private void SpEffectsTick()
    {
        var spEffects = _spEffectService.GetActiveSpEffectList(_playerService.GetPlayerIns());
        _spEffectViewModel.RefreshEffects(spEffects);
    }

    private void RemoveSpEffect()
    {
        if (!uint.TryParse(RemoveSpEffectId, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out uint spEffectId)) return;
        var playerIns = _playerService.GetPlayerIns();
        _spEffectService.RemoveSpEffect(playerIns, spEffectId);
    }

    private void ApplySpEffect()
    {
        if (!uint.TryParse(ApplySpEffectId, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out uint spEffectId)) return;
        var playerIns = _playerService.GetPlayerIns();
        _spEffectService.ApplySpEffect(playerIns, spEffectId);
    }
    
    private void OpenAiWindow()
    {
        if (_chrInsWindow != null && _chrInsWindow.IsVisible)
        {
            _chrInsWindow.Activate();
            return;
        }
        
        _chrInsWindow = new ChrInsWindow
        {
            DataContext = _chrInsWindowViewModel,
            Title = "AI"
        };
        _chrInsWindow.Closed += (s, e) =>
        {
            _chrInsWindow = null;
            _chrInsWindowViewModel.NotifyWindowClosed();
        };

        _utilityService.PatchDebugFont();
        _reminderService.TrySetReminder();
        _chrInsWindow.Show();
        _chrInsWindowViewModel.NotifyWindowOpen();
        
        _chrInsWindow.Activate();
        _chrInsWindow.Focus();
    }
    
    private void OpenLineComparison()
    {
        if (_lineComparisonWindow != null && _lineComparisonWindow.IsVisible)
        {
            _lineComparisonWindow.Activate();
            return;
        }

        _lineComparisonWindow = new LineComparisonWindow
        {
            DataContext = _lineComparisonViewModel
        };
        _lineComparisonWindow.Closed += (s, e) =>
        {
            _lineComparisonWindow = null;
            _lineComparisonViewModel.NotifyWindowClosed();
        };

        _lineComparisonWindow.Show();
        _lineComparisonViewModel.NotifyWindowOpen();

        _lineComparisonWindow.Activate();
        _lineComparisonWindow.Focus();
    }

    private void InjectScript()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Lua files (*.lua)|*.lua",
            Title = "Inject AI Script"
        };
        
        if (dialog.ShowDialog() != true) return;
        
        if (!HasRegisterTableGoal(File.ReadLines(dialog.FileName).Take(5)))
        {
            MsgBox.Show("Did not find \"RegisterTableGoal\", please include that in the loaded file", "Invalid file");
            return;
        }
        
        var content = File.ReadAllText(dialog.FileName).Replace("\r\n", "\n");
        var scriptWithNullTermination = Encoding.UTF8.GetBytes(content + '\0');

        _reminderService.TrySetReminder();
        _aiService.InjectAiScript(scriptWithNullTermination);
        
    }
    
    private bool HasRegisterTableGoal(IEnumerable<string> firstLines) => 
        firstLines.Any(line => line.Contains("RegisterTableGoal"));

    #endregion
}