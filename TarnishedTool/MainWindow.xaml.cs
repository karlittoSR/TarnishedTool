using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TarnishedTool.Enums;
using TarnishedTool.Interfaces;
using TarnishedTool.Memory;
using TarnishedTool.Services;
using TarnishedTool.Utilities;
using TarnishedTool.ViewModels;
using TarnishedTool.Views.Tabs;
using static TarnishedTool.Memory.Offsets;
using UtilityTab = TarnishedTool.Views.Tabs.UtilityTab;

namespace TarnishedTool
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private readonly IMemoryService _memoryService;
        private readonly IStateService _stateService;
        private readonly IDlcService _dlcService;
        private readonly AoBScanner _aobScanner;
        private HookManager _hookManager;
        private HotkeyManager _hotkeyManager;
        private IReminderService _reminderService;

        private PlayerViewModel _playerViewModel;
        private EnemyViewModel _enemyViewModel;
        private UtilityViewModel _utilityViewModel;
        private TargetViewModel _targetViewModel;
        private TravelViewModel _travelViewModel;

        private readonly DispatcherTimer _gameLoadedTimer;

        public MainWindow()
        {
            _memoryService = new MemoryService();
            _memoryService.StartAutoAttach();
            InitializeComponent();
            
            var savedLeft = SettingsManager.Default.WindowLeft;
            var savedTop = SettingsManager.Default.WindowTop;
            if ((savedLeft != 0 || savedTop != 0) && IsOnVisibleScreen(savedLeft, savedTop))
            {
                Left = savedLeft;
                Top = savedTop;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            _aobScanner = new AoBScanner(_memoryService);
            _stateService = new StateService(_memoryService);

            _hookManager = new HookManager(_memoryService, _stateService);
            _hotkeyManager = new HotkeyManager(_memoryService);
            var hotkeyManager = _hotkeyManager;

            IActionRequestService actionRequestService = new ActionRequestService(_memoryService, _hookManager);
            IParamService paramService = new ParamService(_memoryService);
            IReminderService reminderService = new ReminderService(_memoryService, _hookManager, _stateService);
            _reminderService = reminderService;
            IChrInsService chrInsService = new ChrInsService(_memoryService);
            ITravelService travelService = new TravelService(_memoryService, _hookManager);
            IPlayerService playerService =
                new PlayerService(_memoryService, _hookManager, travelService, paramService, chrInsService, actionRequestService);
            IUtilityService utilityService = new UtilityService(_memoryService, _hookManager, playerService, actionRequestService);
            IEventService eventService = new EventService(_memoryService, _hookManager, reminderService);
            IAttackInfoService attackInfoService = new AttackInfoService(_memoryService, _hookManager);
            ITargetService targetService =
                new TargetService(_memoryService, _hookManager, playerService, reminderService, chrInsService);
            IEnemyService enemyService = new EnemyService(_memoryService, _hookManager, reminderService);
            ISettingsService settingsService = new SettingsService(_memoryService);
            IEzStateService ezStateService = new EzStateService(_memoryService);
            IItemService itemService = new ItemService(_memoryService);
            IParamRepository paramRepository = new ParamRepository();
            IEquipService equipService =
                new EquipService(_memoryService, itemService, paramService, paramRepository);
            IFlaskService flaskService = new FlaskService(ezStateService, _memoryService);
            IInventoryService inventoryService =
                new InventoryService(_memoryService, paramService, paramRepository, ezStateService, itemService);
            ICharacterSnapshotService characterSnapshotService =
                new CharacterSnapshotService(equipService, playerService, flaskService, inventoryService);
            ISpEffectService spEffectService = new SpEffectService(_memoryService, reminderService);
            IEmevdService emevdService = new EmevdService(_memoryService);
            IEventLogReader eventLogReader = new EventLogReader(_memoryService);
            IGameTickService gameTickService = new GameTickService(_stateService);
            IAiService aiService = new AiService(_memoryService);
            IAiWindowService aiWindowService = new AiWindowService(aiService, gameTickService, spEffectService);

            _dlcService = new DlcService(_memoryService);

            // Create notification service
            IHotkeyNotificationService hotkeyNotificationService = new HotkeyNotificationService();

            PlayerViewModel playerViewModel = new PlayerViewModel(
                playerService, _stateService, hotkeyManager,
                eventService, spEffectService, emevdService,
                _dlcService, ezStateService, gameTickService, paramService,
                hotkeyNotificationService
            );
            _playerViewModel = playerViewModel;

            TravelViewModel travelViewModel = new TravelViewModel(
                travelService, eventService, _stateService,
                _dlcService, emevdService, playerService,
                gameTickService, hotkeyManager, hotkeyNotificationService
            );
            _travelViewModel = travelViewModel;

            EnemyViewModel enemyViewModel = new EnemyViewModel(
                enemyService, _stateService, hotkeyManager, emevdService,
                _dlcService, spEffectService, paramService, playerService,
                eventService, reminderService, travelService, chrInsService,
                hotkeyNotificationService
            );
            _enemyViewModel = enemyViewModel;

            TargetViewModel targetViewModel = new TargetViewModel(
                targetService, _stateService, enemyService,
                attackInfoService, hotkeyManager, spEffectService,
                emevdService, gameTickService, aiWindowService,
                hotkeyNotificationService
            );
            _targetViewModel = targetViewModel;

            EventViewModel eventViewModel = new EventViewModel(
                eventService, _stateService, itemService, _dlcService,
                ezStateService, emevdService, hotkeyManager,
                utilityService, eventLogReader, hotkeyNotificationService
            );

            UtilityViewModel utilityViewModel = new UtilityViewModel(
                utilityService, _stateService, ezStateService,
                playerService, hotkeyManager, playerViewModel,
                _dlcService, spEffectService, flaskService, paramService,
                hotkeyNotificationService, _memoryService
            );
            _utilityViewModel = utilityViewModel;
            utilityViewModel.SetTargetViewModel(targetViewModel);
            targetViewModel.SetUtilityViewModel(utilityViewModel);

            ItemViewModel itemViewModel = new ItemViewModel(
                itemService, _dlcService, _stateService, eventService, hotkeyManager, hotkeyNotificationService
            );

            AdvancedViewModel advancedViewModel = new AdvancedViewModel(
                itemService, _stateService,
                paramService, paramRepository, spEffectService, playerService,
                hotkeyManager, gameTickService, reminderService, aiService,
                utilityService, chrInsService, aiWindowService, hotkeyNotificationService,
                characterSnapshotService
            );

            // Wire the Line Comparison "Reset zone on Restore to Start" toggle to the
            // proven in-place boss-revive + area-reload + rest logic.
            advancedViewModel.LineComparison.SetZoneResetAction(enemyViewModel.ResetZoneInPlace);
            advancedViewModel.LineComparison.SetRestAction(enemyViewModel.RestAndRefresh);

            var activateOnLaunchManager = new ActivateOnLaunchManager();
            
            ActivateOnLaunchViewModel activateOnLaunchViewModel = new ActivateOnLaunchViewModel(
                playerViewModel,enemyViewModel,utilityViewModel, travelViewModel, eventViewModel, itemViewModel,activateOnLaunchManager,_stateService
            );

            SettingsViewModel settingsViewModel = new SettingsViewModel(
                settingsService, hotkeyManager, _stateService, activateOnLaunchViewModel
            );

            var playerTab = new PlayerTab(playerViewModel);
            var travelTab = new TravelTab(travelViewModel);
            var enemyTab = new EnemyTab(enemyViewModel);
            var targetTab = new TargetTab(targetViewModel);
            var utilityTab = new UtilityTab(utilityViewModel);
            var itemTab = new ItemTab(itemViewModel);
            var eventTab = new EventTab(eventViewModel);
            var advancedTab = new AdvancedTab(advancedViewModel);
            var settingsTab = new SettingsTab(settingsViewModel);


            MainTabControl.Items.Add(new TabItem { Header = "Player", Content = playerTab });
            MainTabControl.Items.Add(new TabItem { Header = "Travel", Content = travelTab });
            MainTabControl.Items.Add(new TabItem { Header = "Enemies", Content = enemyTab });
            MainTabControl.Items.Add(new TabItem { Header = "Target", Content = targetTab });
            MainTabControl.Items.Add(new TabItem { Header = "Utility", Content = utilityTab });
            MainTabControl.Items.Add(new TabItem { Header = "Event", Content = eventTab });
            MainTabControl.Items.Add(new TabItem { Header = "Items", Content = itemTab });
            MainTabControl.Items.Add(new TabItem { Header = "Advanced", Content = advancedTab });
            MainTabControl.Items.Add(new TabItem { Header = "Settings", Content = settingsTab });

            MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;

            _stateService.Publish(State.AppStart);

            Closing += MainWindow_Closing;

            _gameLoadedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(25)
            };
            _gameLoadedTimer.Tick += Timer_Tick;
            _gameLoadedTimer.Start();

            VersionChecker.UpdateVersionText(AppVersion);

            if (SettingsManager.Default.EnableUpdateChecks)
            {
                VersionChecker.CheckForUpdates(this);
            }
        }

        private bool _loaded;
        private bool _hasAllocatedMemory;
        private bool _appliedOneTimeFeatures;
        private bool _hasPublishedLoaded;
        private bool _hasPublishedFadedIn;
        private bool _hasCheckedPatch;
        private DateTime? _attachedTime;

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_memoryService.IsAttached)
            {
                IsAttachedText.Text = "Attached to game";
                IsAttachedText.Foreground = (SolidColorBrush)Application.Current.Resources["AttachedBrush"];

                LaunchGameButton.IsEnabled = false;
                DetachButton.Visibility = Visibility.Visible;
                // Only enable Detach when the player is actually in-world (WorldChrMan.PlayerIns != 0).
                // Prevents writing to null pointers on the main menu after a quitout.
                DetachButton.IsEnabled = _loaded;

                if (!_attachedTime.HasValue)
                {
                    _attachedTime = DateTime.Now;
                    return;
                }

                if ((DateTime.Now - _attachedTime.Value).TotalSeconds < 2)
                    return;

                if (!_hasCheckedPatch)
                {
                    if (!PatchManager.Initialize(_memoryService))
                    {
                        _aobScanner.DoFallbackScan();
                    }

                    // Equip POC: AOB-scanned on every version (the version offset
                    // tables don't carry these functions), so it stays version-proof.
                    _aobScanner.ScanEquipFunctions();

#if DEBUG
                    Console.WriteLine($@"Base: 0x{(long)_memoryService.BaseAddress:X}");
#endif
                    _hasCheckedPatch = true;
                }

                
                if (!_hasAllocatedMemory)
                {
                    _memoryService.AllocCodeCave();
#if DEBUG
                    Console.WriteLine($@"Code cave: 0x{CodeCaveOffsets.Base.ToInt64():X}");
#endif
                    _stateService.Publish(State.Attached);
                    _hasAllocatedMemory = true;
                }

                if (_stateService.IsLoaded())
                {
                    if (!_hasPublishedFadedIn && _hasPublishedLoaded && IsFadedIn())
                    {
                        _stateService.Publish(State.FadedIn);
                        _hasPublishedFadedIn = true;
                    }

                    if (_loaded) return;
                    _loaded = true;
                    _dlcService.CheckDlc();
                    _stateService.Publish(State.Loaded);
                    _hasPublishedLoaded = true;
                    CheckIfGameStart();
                    if (_appliedOneTimeFeatures) return;
                    _stateService.Publish(State.FirstLoaded);
                    _appliedOneTimeFeatures = true;
                }
                else if (_loaded)
                {
                    _stateService.Publish(State.NotLoaded);
                    _loaded = false;
                    _hasPublishedLoaded = false;
                    _hasPublishedFadedIn = false;
                }
            }
            else
            {
                _hasCheckedPatch = false;
                _loaded = false;
                _attachedTime = null;
                _hasAllocatedMemory = false;
                _appliedOneTimeFeatures = false;
                _hasPublishedLoaded = false;
                _hasPublishedFadedIn = false;
                // The game process is gone, so the code cave address is no longer valid.
                // Clear it so nothing (hotkeys, toggles) can use the stale address after
                // the game is relaunched but before a new cave is allocated.
                CodeCaveOffsets.Base = IntPtr.Zero;
                _stateService.Publish(State.Detached);
                IsAttachedText.Text = "Not attached";
                IsAttachedText.Foreground = (SolidColorBrush)Application.Current.Resources["NotAttachedBrush"];
                LaunchGameButton.IsEnabled = true;
                DetachButton.Visibility = Visibility.Collapsed;
                DetachButton.IsEnabled = false;
            }
        }

        private bool IsFadedIn() =>
            _memoryService.Read<byte>(_memoryService.Read<nint>(MenuMan.Base) + MenuMan.IsFading) == 0;

        private void CheckIfGameStart()
        {
            var igt = _memoryService.Read<uint>(_memoryService.Read<nint>(GameDataMan.Base) + GameDataMan.Igt);
            if (igt < 5000) _stateService.Publish(State.OnNewGameStart);
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_memoryService.IsAttached)
            {
                if (_loaded)
                {
                    // Stop the timer and keyboard hook before resetting so no pending
                    // BeginInvoke actions can re-enable features after our reset completes.
                    _gameLoadedTimer.Stop();
                    _hotkeyManager.Stop();
                    DetachFromGame();
                }
                else
                {
                    // On the main menu after a quitout, memory pointers are null so patches
                    // cannot be reversed. Block close and ask the user to go back in-game first.
                    e.Cancel = true;
                    MsgBox.Show(
                        "The game is currently on the main menu.\n\n" +
                        "Active patches cannot be reversed from the main menu. " +
                        "Please load back into the game and use the Detach button to restore a clean vanilla state before closing.",
                        "Detach Required Before Closing");
                    return;
                }
            }

            var bounds = WindowState == WindowState.Normal
                ? new Rect(Left, Top, ActualWidth, ActualHeight)
                : RestoreBounds;
            SettingsManager.Default.WindowLeft = bounds.Left;
            SettingsManager.Default.WindowTop = bounds.Top;
            SettingsManager.Default.Save();
        }

        private static bool IsOnVisibleScreen(double left, double top)
        {
            const double minVisibleX = 100;
            const double minVisibleY = 30;
            var vLeft = SystemParameters.VirtualScreenLeft;
            var vTop = SystemParameters.VirtualScreenTop;
            var vRight = vLeft + SystemParameters.VirtualScreenWidth;
            var vBottom = vTop + SystemParameters.VirtualScreenHeight;
            return left + minVisibleX > vLeft
                   && left < vRight - minVisibleX
                   && top + minVisibleY > vTop
                   && top < vBottom - minVisibleY;
        }

        private void LaunchGame_Click(object sender, RoutedEventArgs e)
        {
            _memoryService.EnableAutoAttach();
            Task.Run(ExeManager.LaunchGame);
        }

        private void Detach_Click(object sender, RoutedEventArgs e)
        {
            if (!_memoryService.IsAttached) return;
            DetachFromGame();
        }

        private void DetachFromGame()
        {
            // Reset each ViewModel individually so a failure in one doesn't prevent the others.
            // Only write vanilla values back to memory when the game world is loaded (player pointer valid).
            // On the main menu after a quitout the pointers are null and writing would crash.
            if (_loaded)
            {
                TryReset(_playerViewModel.ResetToggles);
                TryReset(_enemyViewModel.ResetToggles);
                TryReset(_utilityViewModel.ResetToggles);
                TryReset(_targetViewModel.ResetToggles);
                TryReset(_travelViewModel.ResetToggles);
            }

            // Restore the loading-screen title FMG entry before the handle is closed,
            // otherwise the reminder text keeps appearing on random loading screens
            // until the game is restarted.
            try { _reminderService.RestoreReminder(); } catch { }

            try { _hookManager.UninstallAllHooks(); } catch { }

            try
            {
                if (CodeCaveOffsets.Base != IntPtr.Zero)
                {
                    _memoryService.FreeMem(CodeCaveOffsets.Base);
                    CodeCaveOffsets.Base = IntPtr.Zero;
                }
            }
            catch { }

            _memoryService.ManualDetach();

            // Always reset UI toggles so checkboxes reflect vanilla state,
            // even when memory writes were skipped (e.g. main menu after quitout).
            if (!_loaded)
            {
                TryReset(_playerViewModel.ResetToggles);
                TryReset(_enemyViewModel.ResetToggles);
                TryReset(_utilityViewModel.ResetToggles);
                TryReset(_targetViewModel.ResetToggles);
                TryReset(_travelViewModel.ResetToggles);
            }

            _hasAllocatedMemory = false;
            _appliedOneTimeFeatures = false;
            _hasPublishedLoaded = false;
            _hasPublishedFadedIn = false;
            _hasCheckedPatch = false;
            _loaded = false;
            _attachedTime = null;
        }

        private static void TryReset(Action reset)
        {
            try { reset(); }
            catch { }
        }

        private void CheckUpdate_Click(object sender, RoutedEventArgs e) =>
            VersionChecker.CheckForUpdates(this, true);

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl && MainTabControl.SelectedItem is TabItem selectedTab)
            {
                if (selectedTab.Header.ToString() == "Event")
                {
                    _stateService.Publish(State.EventTabActivated);
                }
            }
        }
    }
}