using System;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using TarnishedTool.Core;
using TarnishedTool.Enums;
using TarnishedTool.GameIds;
using TarnishedTool.Interfaces;
using TarnishedTool.Models;
using TarnishedTool.Utilities;
using TarnishedTool.Views.Windows;
using static TarnishedTool.Memory.Offsets;

namespace TarnishedTool.ViewModels
{
    public class PlayerViewModel : BaseViewModel
    {
        private int _currentRuneLevel;
        private bool _customHpHasBeenSet = !string.IsNullOrWhiteSpace(SettingsManager.Default.SaveCustomHp);

        private float _playerDesiredSpeed = -1f;
        private const float DefaultSpeed = 1f;
        private const float Epsilon = 0.0001f;

        private bool _pauseUpdates = true;

        private readonly IPlayerService _playerService;
        private readonly IParamService _paramService;

        private readonly CharacterState _saveState1 = new();
        private readonly CharacterState _saveState2 = new();

        private readonly SlopeTracker _slopeTracker = new();
        private SlopeOverlayWindow _slopeOverlayWindow;

        private static readonly Brush SlopeUphillBrush = Brushes.LimeGreen;
        private static readonly Brush SlopeFlatBrush = Brushes.DodgerBlue;
        private static readonly Brush SlopeDownhillBrush = Brushes.OrangeRed;
        private static readonly Brush SlopeUnknownBrush = Brushes.DimGray;

        private readonly HotkeyManager _hotkeyManager;
        private readonly IEventService _eventService;
        private readonly ISpEffectService _spEffectService;
        private readonly IEmevdService _emevdService;
        private readonly IDlcService _dlcService;
        private readonly IEzStateService _ezStateService;
        private readonly IGameTickService _gameTickService;
        private readonly IHotkeyNotificationService _notificationService;

        public static readonly long[] NewGameEventIds = new long[] { 50, 51, 52, 53, 54, 55, 56, 57 };

        // Faster Death Stuff
        private const uint MenuCommonParamRowId = 0;
        private const int DeathTimeOffset0 = 0x0;
        private const int DeathTimeOffset4 = 0x4;
        private const float OriginalDeathTime0x0 = 3.8f;
        private const float OriginalDeathTime0x4 = 3.3f;

        // No Death Stuff (Miquella's Grab)
        private const uint NoMiquellaCharmSpEffectRowId = 19681;
        private const int SpEffectDurationOffset = 0x8;
        private const int SpEffectVfxOffset = 0x170;
        private const float OriginalSpEffectDuration = -1f;
        private const int OriginalSpEffectVfx = 20050560;

        // Torrent Anywhere Stuff (Abyssal Woods)
        private const uint NoForcedDismountSpEffectRowId = 19995;
        private const int ForcedDismountDurationOffset = 0x8;
        private const int ForcedDismountStateInfoOffset = 0x156;
        private const float ForcedDismountDuration = -1f;
        private const int ForcedDismountStateInfo = 433;

        public PlayerViewModel(IPlayerService playerService, IStateService stateService, HotkeyManager hotkeyManager,
            IEventService eventService, ISpEffectService spEffectService, IEmevdService emevdService,
            IDlcService dlcService, IEzStateService ezStateService, IGameTickService gameTickService,
            IParamService paramService, IHotkeyNotificationService notificationService = null)
        {
            _playerService = playerService;
            _hotkeyManager = hotkeyManager;
            _eventService = eventService;
            _spEffectService = spEffectService;
            _emevdService = emevdService;
            _dlcService = dlcService;
            _ezStateService = ezStateService;
            _gameTickService = gameTickService;
            _paramService = paramService;
            _notificationService = notificationService;

            RegisterHotkeys();

            stateService.Subscribe(State.Loaded, OnGameLoaded);
            stateService.Subscribe(State.FirstLoaded, OnGameFirstLoaded);
            stateService.Subscribe(State.NotLoaded, OnGameNotLoaded);
            stateService.Subscribe(State.OnNewGameStart, OnNewGameStart);
            stateService.Subscribe(State.FadedIn, OnFadedIn);

            SetRfbsCommand = new DelegateCommand(SetRfbs);
            SetMaxHpCommand = new DelegateCommand(SetMaxHp);
            SetCustomHpCommand = new DelegateCommand(SetCustomHp);
            DieCommand = new DelegateCommand(Die);

            SavePositionCommand = new DelegateCommand(SavePosition);
            RestorePositionCommand = new DelegateCommand(RestorePosition);

            ChangeRunesCommand = new DelegateCommand(ChangeRunes);
            ApplyRuneArcCommand = new DelegateCommand(ApplyRuneArc);
            RestCommand = new DelegateCommand(Rest);

            SetMaxLevelCommand = new DelegateCommand(SetMaxLevel);
            SetRuneLevelOneCommand = new DelegateCommand(SetRuneLevelOne);

            ApplyPrefs();
        }

        #region Commands

        public ICommand SetRfbsCommand { get; set; }
        public ICommand SetMaxHpCommand { get; set; }
        public ICommand SetCustomHpCommand { get; set; }
        public ICommand DieCommand { get; set; }

        public ICommand SavePositionCommand { get; set; }
        public ICommand RestorePositionCommand { get; set; }

        public ICommand ChangeRunesCommand { get; set; }
        public ICommand ApplyRuneArcCommand { get; set; }
        public ICommand RestCommand { get; set; }

        public ICommand SetMaxLevelCommand { get; set; }
        public ICommand SetRuneLevelOneCommand { get; set; }

        #endregion

        #region Properties

        private bool _areOptionsEnabled;

        public bool AreOptionsEnabled
        {
            get => _areOptionsEnabled;
            set => SetProperty(ref _areOptionsEnabled, value);
        }

        private bool _isDlcAvailable;

        public bool IsDlcAvailable
        {
            get => _isDlcAvailable;
            set => SetProperty(ref _isDlcAvailable, value);
        }

        private int _currentHp;

        public int CurrentHp
        {
            get => _currentHp;
            set => SetProperty(ref _currentHp, value);
        }

        private int _currentMaxHp;

        public int CurrentMaxHp
        {
            get => _currentMaxHp;
            set => SetProperty(ref _currentMaxHp, value);
        }

        private string _customHp = SettingsManager.Default.SaveCustomHp;

        public string CustomHp
        {
            get => _customHp;
            set
            {
                if (SetProperty(ref _customHp, value))
                {
                    _customHpHasBeenSet = true;
                }
            }
        }

        private bool _isHotEnabled;

        public bool IsHotEnabled
        {
            get => _isHotEnabled;
            set => SetProperty(ref _isHotEnabled, value);
        }

        private bool _isFpRegenEnabled;

        public bool IsFpRegenEnabled
        {
            get => _isFpRegenEnabled;
            set => SetProperty(ref _isFpRegenEnabled, value);
        }

        private bool _isHpLocked;

        public bool IsHpLocked
        {
            get => _isHpLocked;
            set
            {
                if (SetProperty(ref _isHpLocked, value))
                {
                    _playerService.ToggleLockHp(_isHpLocked);
                    _playerService.ToggleNoDamage(_isHpLocked);
                    if (!_isHpLocked && !IsNoDamageEnabled)
                    {
                        _playerService.ToggleNoDamage(false);
                    }
                }
            }
        }

        private bool _isNoRollEnabled;

        public bool IsNoRollEnabled
        {
            get => _isNoRollEnabled;
            set
            {
                if (SetProperty(ref _isNoRollEnabled, value))
                {
                    _playerService.ToggleNoRoll(_isNoRollEnabled);
                }
            }
        }

        private bool _isSetRfbsOnLoadEnabled;

        public bool IsSetRfbsOnLoadEnabled
        {
            get => _isSetRfbsOnLoadEnabled;
            set => SetProperty(ref _isSetRfbsOnLoadEnabled, value);
        }

        private bool _isPos1Saved;

        public bool IsPos1Saved
        {
            get => _isPos1Saved;
            set => SetProperty(ref _isPos1Saved, value);
        }

        private bool _isPos2Saved;

        public bool IsPos2Saved
        {
            get => _isPos2Saved;
            set => SetProperty(ref _isPos2Saved, value);
        }

        private bool _isStateIncluded;

        public bool IsStateIncluded
        {
            get => _isStateIncluded;
            set => SetProperty(ref _isStateIncluded, value);
        }

        private float _posX;

        public float PosX
        {
            get => _posX;
            set => SetProperty(ref _posX, value);
        }

        private float _posY;

        public float PosY
        {
            get => _posY;
            set => SetProperty(ref _posY, value);
        }

        private float _posZ;

        public float PosZ
        {
            get => _posZ;
            set => SetProperty(ref _posZ, value);
        }

        private bool _isNoDeathEnabled;

        public bool IsNoDeathEnabled
        {
            get => _isNoDeathEnabled;
            set
            {
                if (SetProperty(ref _isNoDeathEnabled, value))
                {
                    _playerService.ToggleDebugFlag(ChrDbgFlags.PlayerNoDeath, _isNoDeathEnabled);
                    ApplyNoMiquellaCharm(_isNoDeathEnabled);
                }
            }
        }

        private bool _isNoDamageEnabled;

        public bool IsNoDamageEnabled
        {
            get => _isNoDamageEnabled;
            set
            {
                if (SetProperty(ref _isNoDamageEnabled, value))
                {
                    _playerService.ToggleNoDamage(_isNoDamageEnabled);
                }
            }
        }

        private bool _isNoHitEnabled;

        public bool IsNoHitEnabled
        {
            get => _isNoHitEnabled;
            set
            {
                if (SetProperty(ref _isNoHitEnabled, value))
                {
                    _playerService.ToggleNoHit(_isNoHitEnabled);
                }
            }
        }

        private bool _isInfiniteStaminaEnabled;

        public bool IsInfiniteStaminaEnabled
        {
            get => _isInfiniteStaminaEnabled;
            set
            {
                if (SetProperty(ref _isInfiniteStaminaEnabled, value))
                {
                    _playerService.ToggleDebugFlag(ChrDbgFlags.InfiniteStam, _isInfiniteStaminaEnabled);
                }
            }
        }

        private bool _isInfiniteConsumablesEnabled;

        public bool IsInfiniteConsumablesEnabled
        {
            get => _isInfiniteConsumablesEnabled;
            set
            {
                if (SetProperty(ref _isInfiniteConsumablesEnabled, value))
                {
                    _playerService.ToggleDebugFlag(ChrDbgFlags.InfiniteGoods, _isInfiniteConsumablesEnabled);
                }
            }
        }

        private bool _isInfiniteArrowsEnabled;

        public bool IsInfiniteArrowsEnabled
        {
            get => _isInfiniteArrowsEnabled;
            set
            {
                if (SetProperty(ref _isInfiniteArrowsEnabled, value))
                {
                    _playerService.ToggleDebugFlag(ChrDbgFlags.InfiniteArrows, _isInfiniteArrowsEnabled);
                }
            }
        }

        private bool _isInfiniteFpEnabled;

        public bool IsInfiniteFpEnabled
        {
            get => _isInfiniteFpEnabled;
            set
            {
                if (SetProperty(ref _isInfiniteFpEnabled, value))
                {
                    _playerService.ToggleDebugFlag(ChrDbgFlags.InfiniteFp, _isInfiniteFpEnabled);
                }
            }
        }

        private bool _isOneShotEnabled;

        public bool IsOneShotEnabled
        {
            get => _isOneShotEnabled;
            set
            {
                if (SetProperty(ref _isOneShotEnabled, value))
                {
                    _playerService.ToggleDebugFlag(ChrDbgFlags.OneShot, _isOneShotEnabled);
                }
            }
        }

        private bool _isInfinitePoiseEnabled;

        public bool IsInfinitePoiseEnabled
        {
            get => _isInfinitePoiseEnabled;
            set
            {
                if (SetProperty(ref _isInfinitePoiseEnabled, value))
                {
                    _playerService.ToggleInfinitePoise(_isInfinitePoiseEnabled);
                }
            }
        }

        private bool _isSilentEnabled;

        public bool IsSilentEnabled
        {
            get => _isSilentEnabled;
            set
            {
                if (SetProperty(ref _isSilentEnabled, value))
                {
                    _playerService.ToggleDebugFlag(ChrDbgFlags.Silent, _isSilentEnabled, true);
                }
            }
        }

        private bool _isHiddenEnabled;

        public bool IsHiddenEnabled
        {
            get => _isHiddenEnabled;
            set
            {
                if (SetProperty(ref _isHiddenEnabled, value))
                {
                    _playerService.ToggleDebugFlag(ChrDbgFlags.Hidden, _isHiddenEnabled, true);
                }
            }
        }

        private bool _isTorrentNoDeathEnabled;

        public bool IsTorrentNoDeathEnabled
        {
            get => _isTorrentNoDeathEnabled;
            set
            {
                if (SetProperty(ref _isTorrentNoDeathEnabled, value))
                {
                    _playerService.ToggleTorrentNoDeath(_isTorrentNoDeathEnabled);
                }
            }
        }

        private bool _isTorrentAnywhereEnabled;

        public bool IsTorrentAnywhereEnabled
        {
            get => _isTorrentAnywhereEnabled;
            set
            {
                if (SetProperty(ref _isTorrentAnywhereEnabled, value))
                {
                    _playerService.ToggleTorrentAnywhere(_isTorrentAnywhereEnabled);
                    ApplyTorrentAnywhere(_isTorrentAnywhereEnabled);
                }
            }
        }

        private bool _isNoRuneLossEnabled;

        public bool IsNoRuneLossEnabled
        {
            get => _isNoRuneLossEnabled;
            set
            {
                if (SetProperty(ref _isNoRuneLossEnabled, value))
                {
                    _playerService.ToggleNoRuneLoss(_isNoRuneLossEnabled);
                }
            }
        }

        private bool _isNoRuneArcLossEnabled;

        public bool IsNoRuneArcLossEnabled
        {
            get => _isNoRuneArcLossEnabled;
            set
            {
                if (SetProperty(ref _isNoRuneArcLossEnabled, value))
                {
                    _playerService.ToggleNoRuneArcLoss(_isNoRuneArcLossEnabled);
                }
            }
        }

        private bool _isNoRuneGainEnabled;

        public bool IsNoRuneGainEnabled
        {
            get => _isNoRuneGainEnabled;
            set
            {
                if (SetProperty(ref _isNoRuneGainEnabled, value))
                {
                    _playerService.ToggleNoRuneGain(_isNoRuneGainEnabled);
                }
            }
        }

        private bool _isNoTimePassOnDeathEnabled;

        public bool IsNoTimePassOnDeathEnabled
        {
            get => _isNoTimePassOnDeathEnabled;
            set
            {
                if (SetProperty(ref _isNoTimePassOnDeathEnabled, value))
                {
                    _playerService.ToggleNoTimePassOnDeath(_isNoTimePassOnDeathEnabled);
                }
            }
        }

        private int _runeLevel;

        public int RuneLevel
        {
            get => _runeLevel;
            private set => SetProperty(ref _runeLevel, value);
        }

        private int _vigor;

        public int Vigor
        {
            get => _vigor;
            set => SetProperty(ref _vigor, value);
        }

        private int _mind;

        public int Mind
        {
            get => _mind;
            set => SetProperty(ref _mind, value);
        }

        private int _endurance;

        public int Endurance
        {
            get => _endurance;
            set => SetProperty(ref _endurance, value);
        }

        private int _strength;

        public int Strength
        {
            get => _strength;
            set => SetProperty(ref _strength, value);
        }

        private int _dexterity;

        public int Dexterity
        {
            get => _dexterity;
            set => SetProperty(ref _dexterity, value);
        }

        private int _intelligence;

        public int Intelligence
        {
            get => _intelligence;
            set => SetProperty(ref _intelligence, value);
        }

        private int _faith;

        public int Faith
        {
            get => _faith;
            set => SetProperty(ref _faith, value);
        }

        private int _arcane;

        public int Arcane
        {
            get => _arcane;
            set => SetProperty(ref _arcane, value);
        }

        private int _scadu;

        public int Scadu
        {
            get => _scadu;
            set => SetProperty(ref _scadu, value);
        }

        private int _spiritAsh;

        public int SpiritAsh
        {
            get => _spiritAsh;
            set => SetProperty(ref _spiritAsh, value);
        }

        private int _runes = 10000;

        public int Runes
        {
            get => _runes;
            set => SetProperty(ref _runes, value);
        }

        private int _newGame;

        public int NewGame
        {
            get => _newGame;
            set
            {
                if (SetProperty(ref _newGame, value))
                {
                    SetNewGame(value);
                }
            }
        }

        private int _currentAnimation;

        public int CurrentAnimation
        {
            get => _currentAnimation;
            set => SetProperty(ref _currentAnimation, value);
        }

        private float _playerSpeed;

        public float PlayerSpeed
        {
            get => _playerSpeed;
            set
            {
                if (SetProperty(ref _playerSpeed, value))
                {
                    _playerService.SetSpeed(value);
                    // Only save speed if it's a meaningful increase (> 1.0), never save 0 or 1.0
                    if (IsRememberSpeedEnabled && value > DefaultSpeed + Epsilon)
                    {
                        SettingsManager.Default.PlayerSpeed = value;
                        SettingsManager.Default.Save();
                    }
                }
            }
        }

        private bool _isRememberSpeedEnabled;

        public bool IsRememberSpeedEnabled
        {
            get => _isRememberSpeedEnabled;
            set
            {
                if (SetProperty(ref _isRememberSpeedEnabled, value))
                {
                    SettingsManager.Default.RememberPlayerSpeed = _isRememberSpeedEnabled;

                    if (_isRememberSpeedEnabled)
                    {
                        if (Math.Abs(PlayerSpeed - DefaultSpeed) > Epsilon)
                            SettingsManager.Default.PlayerSpeed = PlayerSpeed;
                    }
                    else
                    {
                        SettingsManager.Default.PlayerSpeed = DefaultSpeed;
                    }

                    SettingsManager.Default.Save();
                }
            }
        }

        private bool _isAutoSetNewGameSevenEnabled;

        public bool IsAutoSetNewGameSevenEnabled
        {
            get => _isAutoSetNewGameSevenEnabled;
            set => SetProperty(ref _isAutoSetNewGameSevenEnabled, value);
        }

        private bool _isResetWorldIncluded;

        public bool IsResetWorldIncluded
        {
            get => _isResetWorldIncluded;
            set => SetProperty(ref _isResetWorldIncluded, value);
        }

        private MapLocation _mapLocation;

        public MapLocation MapLocation
        {
            get => _mapLocation;
            set => SetProperty(ref _mapLocation, value);
        }

        private bool _showPlayerLocation;

        public bool ShowPlayerLocation
        {
            get => _showPlayerLocation;
            set => SetProperty(ref _showPlayerLocation, value);
        }

        private bool _isSlopeIndicatorEnabled;

        public bool IsSlopeIndicatorEnabled
        {
            get => _isSlopeIndicatorEnabled;
            set
            {
                if (!SetProperty(ref _isSlopeIndicatorEnabled, value)) return;

                _slopeTracker.Reset();
                UpdateSlopeIndicator();

                if (_isSlopeIndicatorEnabled) OpenSlopeOverlay();
                else CloseSlopeOverlay();

                SettingsManager.Default.ShowSlopeOverlay = value;
                SettingsManager.Default.Save();
            }
        }

        private Brush _slopeIndicatorBrush = SlopeUnknownBrush;

        public Brush SlopeIndicatorBrush
        {
            get => _slopeIndicatorBrush;
            private set => SetProperty(ref _slopeIndicatorBrush, value);
        }

        private string _slopeIndicatorTooltip = "Slope: --";

        public string SlopeIndicatorTooltip
        {
            get => _slopeIndicatorTooltip;
            private set => SetProperty(ref _slopeIndicatorTooltip, value);
        }

        private bool _isFasterDeathEnabled;

        public bool IsFasterDeathEnabled
        {
            get => _isFasterDeathEnabled;
            set
            {
                if (!SetProperty(ref _isFasterDeathEnabled, value)) return;
                ApplyFasterDeath(value);
            }
        }

        #endregion

        #region Public Methods

        public void PauseUpdates() => _pauseUpdates = true;
        public void ResumeUpdates() => _pauseUpdates = false;
        public void SetHp(int hp) => _playerService.SetHp(hp);

        public void SetStat(string statName, int value)
        {
            if (Enum.TryParse<GameDataMan.PlayerGameDataOffsets>(statName, out var offset))
            {
                _playerService.SetStat((int)offset, value);
            }
        }

        public void SetScadu(int value) => _playerService.SetScadu(value);
        public void SetSpiritAsh(int value) => _playerService.SetSpiritAsh(value);
        public void SetSpeed(float value) => PlayerSpeed = value;

        public void ResetToggles()
        {
            IsNoDeathEnabled = false;
            IsNoDamageEnabled = false;
            IsNoHitEnabled = false;
            IsInfiniteStaminaEnabled = false;
            IsInfiniteConsumablesEnabled = false;
            IsInfiniteArrowsEnabled = false;
            IsInfiniteFpEnabled = false;
            IsOneShotEnabled = false;
            IsInfinitePoiseEnabled = false;
            IsSilentEnabled = false;
            IsHiddenEnabled = false;
            IsTorrentNoDeathEnabled = false;
            IsTorrentAnywhereEnabled = false;
            IsNoRuneLossEnabled = false;
            IsNoRuneArcLossEnabled = false;
            IsNoRuneGainEnabled = false;
            IsNoTimePassOnDeathEnabled = false;
            IsFasterDeathEnabled = false;
            IsHpLocked = false;
            IsNoRollEnabled = false;
            IsSlopeIndicatorEnabled = false;
            IsFpRegenEnabled = false;
            IsHotEnabled = false;
            IsSetRfbsOnLoadEnabled = false;
            PlayerSpeed = DefaultSpeed;
        }

        #endregion

        #region Private Methods

        private void OnGameLoaded()
        {
            AreOptionsEnabled = true;

            LoadStats();
            _gameTickService.Subscribe(PlayerTick);
            _pauseUpdates = false;
            IsDlcAvailable = _dlcService.IsDlcAvailable;

            _slopeTracker.Reset();
            UpdateSlopeIndicator();

            if (SettingsManager.Default.ShowSlopeOverlay && !_isSlopeIndicatorEnabled)
            {
                _isSlopeIndicatorEnabled = true;
                OnPropertyChanged(nameof(IsSlopeIndicatorEnabled));
                OpenSlopeOverlay();
            }
        }

        private void OnFadedIn()
        {
            if (IsSetRfbsOnLoadEnabled) SetRfbs();
            if (IsTorrentAnywhereEnabled)
            {
                _playerService.ToggleTorrentAnywhere(true);
                ApplyTorrentAnywhere(_isTorrentAnywhereEnabled);
            }

            if (IsTorrentNoDeathEnabled) _playerService.ToggleTorrentNoDeath(true);
            if (IsNoDamageEnabled) _playerService.ToggleNoDamage(true);
            if (IsNoHitEnabled) _playerService.ToggleNoHit(true);
            if (IsNoRollEnabled) _playerService.ToggleNoRoll(true);
            
        }

        private void OnGameFirstLoaded()
        {
            if (IsNoDeathEnabled)
            {
                _playerService.ToggleDebugFlag(ChrDbgFlags.PlayerNoDeath, true);
                ApplyNoMiquellaCharm(true);
            }

            if (IsInfiniteStaminaEnabled) _playerService.ToggleDebugFlag(ChrDbgFlags.InfiniteStam, true);
            if (IsInfiniteConsumablesEnabled) _playerService.ToggleDebugFlag(ChrDbgFlags.InfiniteGoods, true);
            if (IsInfiniteArrowsEnabled) _playerService.ToggleDebugFlag(ChrDbgFlags.InfiniteArrows, true);
            if (IsInfiniteFpEnabled) _playerService.ToggleDebugFlag(ChrDbgFlags.InfiniteFp, true);
            if (IsOneShotEnabled) _playerService.ToggleDebugFlag(ChrDbgFlags.OneShot, true);
            if (IsInfinitePoiseEnabled) _playerService.ToggleInfinitePoise(true);
            if (IsSilentEnabled) _playerService.ToggleDebugFlag(ChrDbgFlags.Silent, true);
            if (IsHiddenEnabled) _playerService.ToggleDebugFlag(ChrDbgFlags.Hidden, true);
            if (IsNoRuneGainEnabled) _playerService.ToggleNoRuneGain(true);
            if (IsNoRuneArcLossEnabled) _playerService.ToggleNoRuneArcLoss(true);
            if (IsNoRuneLossEnabled) _playerService.ToggleNoRuneLoss(true);
            if (IsNoTimePassOnDeathEnabled) _playerService.ToggleNoTimePassOnDeath(true);
            if (IsFasterDeathEnabled) ApplyFasterDeath(true);
            _pauseUpdates = false;
        }

        private void OnGameNotLoaded()
        {
            AreOptionsEnabled = false;
            _gameTickService.Unsubscribe(PlayerTick);
            _slopeTracker.Reset();
            UpdateSlopeIndicator();
        }

        private void OnNewGameStart()
        {
            if (!IsAutoSetNewGameSevenEnabled) return;
            SetNewGame(7);
            NewGame = _playerService.GetNewGame();
        }

        private void RegisterHotkeys()
        {
            _hotkeyManager.RegisterAction(HotkeyActions.SetRfbs, SetRfbs);
            _hotkeyManager.RegisterAction(HotkeyActions.SetMaxHp, SetMaxHp);
            _hotkeyManager.RegisterAction(HotkeyActions.SavePos1, () => { SavePosition(0); _notificationService?.ShowNotification(HotkeyActions.SavePos1); });
            _hotkeyManager.RegisterAction(HotkeyActions.SavePos2, () => { SavePosition(1); _notificationService?.ShowNotification(HotkeyActions.SavePos2); });
            _hotkeyManager.RegisterAction(HotkeyActions.RestorePos1, () => { RestorePosition(0); _notificationService?.ShowNotification(HotkeyActions.RestorePos1); });
            _hotkeyManager.RegisterAction(HotkeyActions.RestorePos2, () => { RestorePosition(1); _notificationService?.ShowNotification(HotkeyActions.RestorePos2); });
            _hotkeyManager.RegisterAction(HotkeyActions.NoDeath,
                () => { IsNoDeathEnabled = !IsNoDeathEnabled; _notificationService?.ShowNotification(HotkeyActions.NoDeath, IsNoDeathEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.NoDamage,
                () => { IsNoDamageEnabled = !IsNoDamageEnabled; _notificationService?.ShowNotification(HotkeyActions.NoDamage, IsNoDamageEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.InfiniteStamina,
                () => { IsInfiniteStaminaEnabled = !IsInfiniteStaminaEnabled; _notificationService?.ShowNotification(HotkeyActions.InfiniteStamina, IsInfiniteStaminaEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.InfiniteConsumables,
                () => { IsInfiniteConsumablesEnabled = !IsInfiniteConsumablesEnabled; _notificationService?.ShowNotification(HotkeyActions.InfiniteConsumables, IsInfiniteConsumablesEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.InfiniteArrows,
                () => { IsInfiniteArrowsEnabled = !IsInfiniteArrowsEnabled; _notificationService?.ShowNotification(HotkeyActions.InfiniteArrows, IsInfiniteArrowsEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.InfiniteFp,
                () => { IsInfiniteFpEnabled = !IsInfiniteFpEnabled; _notificationService?.ShowNotification(HotkeyActions.InfiniteFp, IsInfiniteFpEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.OneShot, () => { IsOneShotEnabled = !IsOneShotEnabled; _notificationService?.ShowNotification(HotkeyActions.OneShot, IsOneShotEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.InfinitePoise,
                () => { IsInfinitePoiseEnabled = !IsInfinitePoiseEnabled; _notificationService?.ShowNotification(HotkeyActions.InfinitePoise, IsInfinitePoiseEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.Silent, () => { IsSilentEnabled = !IsSilentEnabled; _notificationService?.ShowNotification(HotkeyActions.Silent, IsSilentEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.Hidden, () => { IsHiddenEnabled = !IsHiddenEnabled; _notificationService?.ShowNotification(HotkeyActions.Hidden, IsHiddenEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.TogglePlayerSpeed, ToggleSpeed);
            _hotkeyManager.RegisterAction(HotkeyActions.IncreasePlayerSpeed,
                () => SetSpeed(Math.Min(10, PlayerSpeed + 0.25f)));
            _hotkeyManager.RegisterAction(HotkeyActions.DecreasePlayerSpeed,
                () => SetSpeed(Math.Max(0, PlayerSpeed - 0.25f)));
            _hotkeyManager.RegisterAction(HotkeyActions.RuneArc, () => SafeExecute(ApplyRuneArc));
            _hotkeyManager.RegisterAction(HotkeyActions.Rest, () => SafeExecute(Rest));
            _hotkeyManager.RegisterAction(HotkeyActions.PlayerSetCustomHp, SetCustomHp);
            _hotkeyManager.RegisterAction(HotkeyActions.NoHit, () => { IsNoHitEnabled = !IsNoHitEnabled; _notificationService?.ShowNotification(HotkeyActions.NoHit, IsNoHitEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.FasterDeath,
                () => { IsFasterDeathEnabled = !IsFasterDeathEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.HealOverTime, () => { IsHotEnabled = !IsHotEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.TorrentNoDeath,
                () => { IsTorrentNoDeathEnabled = !IsTorrentNoDeathEnabled; _notificationService?.ShowNotification(HotkeyActions.TorrentNoDeath, IsTorrentNoDeathEnabled); });
            _hotkeyManager.RegisterAction(HotkeyActions.TorrentAnywhere,
                () => { IsTorrentAnywhereEnabled = !IsTorrentAnywhereEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.RfbsOnLoad,
                () => { IsSetRfbsOnLoadEnabled = !IsSetRfbsOnLoadEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.NoRunesFromEnemies,
                () => { IsNoRuneGainEnabled = !IsNoRuneGainEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.NoRuneArcLossOnDeath,
                () => { IsNoRuneArcLossEnabled = !IsNoRuneArcLossEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.NoRuneLossOnDeath,
                () => { IsNoRuneLossEnabled = !IsNoRuneLossEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.NoTimeChangeOnDeath,
                () => { IsNoTimePassOnDeathEnabled = !IsNoTimePassOnDeathEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.ToggleResetEnemiesWithRest,
                () => { IsResetWorldIncluded = !IsResetWorldIncluded; });
            _hotkeyManager.RegisterAction(HotkeyActions.Level1, SetRuneLevelOne);
            _hotkeyManager.RegisterAction(HotkeyActions.MaxLevel, SetMaxLevel);
            _hotkeyManager.RegisterAction(HotkeyActions.SetNgCycleTo7, () => { SetNewGame(7); });
            _hotkeyManager.RegisterAction(HotkeyActions.FpRegen, () => { IsFpRegenEnabled = !IsFpRegenEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.LockHp, () => { IsHpLocked = !IsHpLocked; });
            _hotkeyManager.RegisterAction(HotkeyActions.NoRoll, () => { IsNoRollEnabled = !IsNoRollEnabled; });
            _hotkeyManager.RegisterAction(HotkeyActions.SlopeIndicator,
                () => { IsSlopeIndicatorEnabled = !IsSlopeIndicatorEnabled; _notificationService?.ShowNotification(HotkeyActions.SlopeIndicator, IsSlopeIndicatorEnabled); });
        }

        private void SafeExecute(Action action)
        {
            if (!AreOptionsEnabled) return;
            action();
        }

        private void PlayerTick()
        {
            if (_pauseUpdates) return;

            if (IsHotEnabled) TryApplyHot();

            if (IsFpRegenEnabled) TryApplyFpRegen();

            CurrentHp = _playerService.GetCurrentHp();
            CurrentMaxHp = _playerService.GetMaxHp();
            PlayerSpeed = _playerService.GetSpeed();
            int newRuneLevel = _playerService.GetRuneLevel();
            Scadu = _playerService.GetScadu();
            SpiritAsh = _playerService.GetSpiritAsh();
            CurrentAnimation = _playerService.GetCurrentAnimation();
            if (ShowPlayerLocation) MapLocation = _playerService.GetMapLocation();
            if (IsSlopeIndicatorEnabled) TrackSlope();

            if (_currentRuneLevel == newRuneLevel) return;
            RuneLevel = newRuneLevel;
            _currentRuneLevel = newRuneLevel;
            LoadStats();
        }

        private void TrackSlope()
        {
            var position = _playerService.CapturePosition();
            // Absolute coordinates so crossing an overworld grid boundary, which shifts the
            // map coordinates by a whole 256-unit cell, does not read as a cliff.
            _slopeTracker.Add(PositionUtils.ToAbsolute(position.Coords, position.BlockId));
            UpdateSlopeIndicator();
        }

        private void UpdateSlopeIndicator()
        {
            SlopeIndicatorBrush = _slopeTracker.State switch
            {
                SlopeState.Uphill => SlopeUphillBrush,
                SlopeState.Flat => SlopeFlatBrush,
                SlopeState.Downhill => SlopeDownhillBrush,
                _ => SlopeUnknownBrush
            };

            SlopeIndicatorTooltip = _slopeTracker.State switch
            {
                SlopeState.Uphill => $"Uphill — jump gains time ({_slopeTracker.Gradient:P0})",
                SlopeState.Flat => $"Flat — jump gains time ({_slopeTracker.Gradient:P0})",
                SlopeState.Downhill => $"Downhill — jump loses time ({_slopeTracker.Gradient:P0})",
                _ => "Slope: -- (keep moving to read the ground)"
            };
        }

        private void OpenSlopeOverlay()
        {
            if (_slopeOverlayWindow != null) return;
            _slopeOverlayWindow = new SlopeOverlayWindow
            {
                DataContext = this
            };
            _slopeOverlayWindow.Closed += (s, e) =>
            {
                _slopeOverlayWindow = null;
                IsSlopeIndicatorEnabled = false;
            };
            _slopeOverlayWindow.Show();
        }

        private void CloseSlopeOverlay()
        {
            if (_slopeOverlayWindow == null || !_slopeOverlayWindow.IsVisible) return;
            _slopeOverlayWindow.Close();
            _slopeOverlayWindow = null;
        }

        private void TryApplyHot()
        {
            int currentHp = _playerService.GetCurrentHp();
            int maxHp = _playerService.GetMaxHp();

            if (currentHp >= maxHp) return;
            int hpToSet = Math.Min(currentHp + (int)(maxHp * 0.033), maxHp);
            _playerService.SetHp(hpToSet);
        }

        private void TryApplyFpRegen()
        {
            int currentFp = _playerService.GetCurrentFp();
            int maxFp = _playerService.GetMaxFp();

            if (currentFp >= maxFp) return;
            int fpToSet = Math.Min(currentFp + (int)(maxFp * 0.033), maxFp);
            _playerService.SetFp(fpToSet);
        }


        private void LoadStats()
        {
            Stats stats = _playerService.GetStats();
            Vigor = stats.Vigor;
            Mind = stats.Mind;
            Endurance = stats.Endurance;
            Strength = stats.Strength;
            Dexterity = stats.Dexterity;
            Intelligence = stats.Intelligence;
            Faith = stats.Faith;
            Arcane = stats.Arcane;
            RuneLevel = _playerService.GetRuneLevel();
            NewGame = _playerService.GetNewGame();
        }

        private void SetRfbs() => _playerService.SetRfbs();
        private void SetMaxHp() => _playerService.SetFullHp();
        private void Die() => _playerService.SetHp(0);

        private void SetCustomHp()
        {
            if (!_customHpHasBeenSet) return;
            var (customHp, error) = ParseCustomHp();
            if (customHp == null)
            {
                MsgBox.Show(error, "Invalid Input");
                return;
            }

            if (customHp > CurrentMaxHp)
                customHp = CurrentMaxHp;

            _playerService.SetHp(customHp.Value);
            SettingsManager.Default.SaveCustomHp = CustomHp;
            SettingsManager.Default.Save();
        }

        private (int? value, string error) ParseCustomHp()
        {
            var input = CustomHp?.Trim();
            if (string.IsNullOrEmpty(input))
                return (null, "Please enter a value");

            if (input.EndsWith("%"))
            {
                if (double.TryParse(input.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var percent))
                    return ((int)(percent / 100.0 * CurrentMaxHp), null);
                return (null, "Invalid percentage format");
            }

            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var absolute))
                return (absolute, null);

            return (null, "Enter a number or percentage (e.g. 545 or 40%)");
        }

        private void SavePosition(object parameter)
        {
            int index = Convert.ToInt32(parameter);
            var state = index == 0 ? _saveState1 : _saveState2;
            if (index == 0) IsPos1Saved = true;
            else IsPos2Saved = true;

            state.IncludesState = IsStateIncluded;
            if (IsStateIncluded)
            {
                state.Hp = CurrentHp;
                state.Fp = _playerService.GetCurrentFp();
                state.Sp = _playerService.GetCurrentSp();
            }

            _playerService.SavePos(index);
        }

        private void RestorePosition(object parameter)
        {
            int index = Convert.ToInt32(parameter);

            if (index == 0 && !IsPos1Saved) return;
            if (index == 1 && !IsPos2Saved) return;
            _playerService.RestorePos(index);
            if (!IsStateIncluded) return;

            var state = index == 0 ? _saveState1 : _saveState2;
            if (IsStateIncluded && state.IncludesState)
            {
                _playerService.SetHp(state.Hp);
                _playerService.SetFp(state.Fp);
                _playerService.SetSp(state.Sp);
            }
        }

        private void ApplyRuneArc()
        {
            var playerIns = _playerService.GetPlayerIns();
            _spEffectService.ApplySpEffect(playerIns, SpEffect.RuneArc);
        }

        private void ChangeRunes() => _playerService.ChangeRunes(Runes);

        private void ToggleSpeed()
        {
            if (!AreOptionsEnabled) return;

            // Speed stuck at 0: recover directly to the remembered/saved speed
            if (IsApproximately(PlayerSpeed, 0f))
            {
                float savedSpeed = SettingsManager.Default.PlayerSpeed;
                float target = savedSpeed > 0f && !IsApproximately(savedSpeed, 0f) ? savedSpeed : DefaultSpeed;
                _playerDesiredSpeed = target;
                SetSpeed(target);
                return;
            }

            if (!IsApproximately(PlayerSpeed, DefaultSpeed))
            {
                _playerDesiredSpeed = PlayerSpeed;
                SetSpeed(DefaultSpeed);
            }
            else
            {
                // Guard against invalid speeds (0, 1.0, or uninitialized) being used as desired speed
                if (_playerDesiredSpeed <= DefaultSpeed + Epsilon)
                {
                    float savedSpeed = SettingsManager.Default.PlayerSpeed;
                    System.Diagnostics.Debug.WriteLine($"[ToggleSpeed] _playerDesiredSpeed was {_playerDesiredSpeed}, checking savedSpeed from settings: {savedSpeed}");
                    // Only use saved speed if it's a meaningful increase (> 1.0), otherwise default to 2.0
                    _playerDesiredSpeed = savedSpeed > DefaultSpeed + Epsilon ? savedSpeed : 2f;
                    System.Diagnostics.Debug.WriteLine($"[ToggleSpeed] Condition: {savedSpeed} > {DefaultSpeed} = {savedSpeed > DefaultSpeed + Epsilon}, using: {_playerDesiredSpeed}");
                }
                SetSpeed(_playerDesiredSpeed);
            }
        }

        private bool IsApproximately(float a, float b)
        {
            return Math.Abs(a - b) < Epsilon;
        }

        private void Rest()
        {
            if (IsResetWorldIncluded) _ezStateService.ExecuteTalkCommand(EzState.TalkCommands.FadeOutAndPassTime(true));
            else _emevdService.ExecuteEmevdCommand(Emevd.EmevdCommands.Rest);

            _playerService.RefreshFromStorage();
        }


        private void ApplyPrefs()
        {
            _isRememberSpeedEnabled = SettingsManager.Default.RememberPlayerSpeed;
            OnPropertyChanged(nameof(IsRememberSpeedEnabled));
            System.Diagnostics.Debug.WriteLine($"[PlayerViewModel.ApplyPrefs] RememberPlayerSpeed={_isRememberSpeedEnabled}, SettingsManager.PlayerSpeed={SettingsManager.Default.PlayerSpeed}");
            if (_isRememberSpeedEnabled)
            {
                _playerDesiredSpeed = SettingsManager.Default.PlayerSpeed;
                System.Diagnostics.Debug.WriteLine($"[PlayerViewModel.ApplyPrefs] Initialized _playerDesiredSpeed from settings: {_playerDesiredSpeed}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[PlayerViewModel.ApplyPrefs] RememberPlayerSpeed is false, _playerDesiredSpeed remains: {_playerDesiredSpeed}");
            }
        }

        private void SetNewGame(int value)
        {
            _playerService.SetNewGame(value);
            NewGame = value;
            var activeIndex = Math.Min(_newGame, NewGameEventIds.Length - 1);
            for (var i = 0; i < NewGameEventIds.Length; i++)
            {
                _eventService.SetEvent(NewGameEventIds[i], i == activeIndex);
            }
        }

        private void SetRuneLevelOne()
        {
            foreach (var statOffset in EnumUtil.GetValues<GameDataMan.PlayerGameDataOffsets>()
                         .Where(o => o >= GameDataMan.PlayerGameDataOffsets.Vigor &&
                                     o <= GameDataMan.PlayerGameDataOffsets.Arcane))
            {
                _playerService.SetStat((int)statOffset, 10);
            }
        }

        private void SetMaxLevel()
        {
            foreach (var statOffset in EnumUtil.GetValues<GameDataMan.PlayerGameDataOffsets>()
                         .Where(o => o >= GameDataMan.PlayerGameDataOffsets.Vigor &&
                                     o <= GameDataMan.PlayerGameDataOffsets.Arcane))
            {
                _playerService.SetStat((int)statOffset, 99);
            }
        }

        private void ApplyFasterDeath(bool enabled)
        {
            var (tableIndex, slotIndex) = ParamIndices.All["MenuCommonParam"];

            IntPtr row = _paramService.GetParamRow(tableIndex, slotIndex, MenuCommonParamRowId);
            if (row == IntPtr.Zero) return;

            float val0 = enabled ? 0f : OriginalDeathTime0x0;
            float val4 = enabled ? 0f : OriginalDeathTime0x4;

            _paramService.Write(row, DeathTimeOffset0, val0);
            _paramService.Write(row, DeathTimeOffset4, val4);
        }

        private void ApplyNoMiquellaCharm(bool enabled)
        {
            var (tableIndex, slotIndex) = ParamIndices.All["SpEffectParam"];

            IntPtr row = _paramService.GetParamRow(tableIndex, slotIndex, NoMiquellaCharmSpEffectRowId);
            if (row == IntPtr.Zero) return;

            float duration = enabled ? 0f : OriginalSpEffectDuration;
            int vfx = enabled ? -1 : OriginalSpEffectVfx;
            _paramService.Write(row, SpEffectDurationOffset, duration);
            _paramService.Write(row, SpEffectVfxOffset, vfx);
        }

        private void ApplyTorrentAnywhere(bool enabled)
        {
            // needed if player enables it while in Abyssal woods
            var playerIns = _playerService.GetPlayerIns();
            if (playerIns == IntPtr.Zero) return;
            _spEffectService.RemoveSpEffect(playerIns, SpEffect.ForcedDismount);

            // needed for later area reloads
            var (tableIndex, slotIndex) = ParamIndices.All["SpEffectParam"];

            IntPtr row = _paramService.GetParamRow(tableIndex, slotIndex, NoForcedDismountSpEffectRowId);
            if (row == IntPtr.Zero) return;

            float duration = enabled ? 0f : ForcedDismountDuration;
            int stateinfo = enabled ? 0 : ForcedDismountStateInfo;
            _paramService.Write(row, ForcedDismountDurationOffset, duration);
            _paramService.Write(row, ForcedDismountStateInfoOffset, stateinfo);
        }

        #endregion
    }
}