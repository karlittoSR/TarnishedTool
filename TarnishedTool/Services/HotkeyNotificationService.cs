using System;
using System.Collections.Generic;
using System.Windows;
using TarnishedTool.Enums;
using TarnishedTool.Interfaces;
using TarnishedTool.Models;
using TarnishedTool.Utilities;
using TarnishedTool.Views.Windows;

namespace TarnishedTool.Services
{
    public class HotkeyNotificationService : IHotkeyNotificationService
    {
        private HotkeyNotificationWindow _currentWindow;
        private const int NotificationDuration = 1500; // 1.5 seconds

        public bool IsEnabled
        {
            get => SettingsManager.Default.ShowHotkeyNotifications;
            set
            {
                SettingsManager.Default.ShowHotkeyNotifications = value;
                SettingsManager.Default.Save();
            }
        }

        public HotkeyNotificationService()
        {
            // IsEnabled is loaded from settings automatically
        }

        public void ShowNotification(HotkeyActions action, bool? isEnabled = null)
        {
            if (!IsEnabled) return;

            var notification = new HotkeyNotification
            {
                ActionName = GetFriendlyActionName(action),
                IsEnabled = isEnabled
            };

            ShowNotificationWindow(notification);
        }

        public void ShowCustomNotification(string message)
        {
            if (!IsEnabled) return;

            var notification = new HotkeyNotification
            {
                ActionName = message,
                IsEnabled = null
            };

            ShowNotificationWindow(notification);
        }

        private void ShowNotificationWindow(HotkeyNotification notification)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Close existing notification if any
                if (_currentWindow != null && _currentWindow.IsVisible)
                {
                    _currentWindow.Close();
                }

                // Create and show new notification
                _currentWindow = new HotkeyNotificationWindow();
                _currentWindow.Closed += (s, e) => _currentWindow = null;
                _currentWindow.Show(notification, NotificationDuration);
            });
        }

        private string GetFriendlyActionName(HotkeyActions action)
        {
            return action switch
            {
                HotkeyActions.NoDeath => "No Death",
                HotkeyActions.NoDamage => "No Damage",
                HotkeyActions.NoHit => "No Hit",
                HotkeyActions.NoRoll => "No Roll",
                HotkeyActions.InfiniteStamina => "Infinite Stamina",
                HotkeyActions.InfiniteConsumables => "Infinite Consumables",
                HotkeyActions.InfiniteFp => "Infinite FP",
                HotkeyActions.InfiniteArrows => "Infinite Arrows",
                HotkeyActions.InfinitePoise => "Infinite Poise",
                HotkeyActions.OneShot => "One Shot",
                HotkeyActions.TogglePlayerSpeed => "Player Speed Toggle",
                HotkeyActions.IncreasePlayerSpeed => "Increase Speed",
                HotkeyActions.DecreasePlayerSpeed => "Decrease Speed",
                HotkeyActions.ToggleSevenSpeed => "7x Speed",
                HotkeyActions.ToggleGameSpeed => "Game Speed Toggle",
                HotkeyActions.IncreaseGameSpeed => "Increase Game Speed",
                HotkeyActions.DecreaseGameSpeed => "Decrease Game Speed",
                HotkeyActions.SavePos1 => "Position 1 Saved",
                HotkeyActions.SavePos2 => "Position 2 Saved",
                HotkeyActions.RestorePos1 => "Position 1 Restored",
                HotkeyActions.RestorePos2 => "Position 2 Restored",
                HotkeyActions.Quitout => "Quitout",
                HotkeyActions.ForceSave => "Force Save",
                HotkeyActions.Noclip => "No Clip",
                HotkeyActions.DrawHitbox => "Draw Hitbox",
                HotkeyActions.ToggleFreeCam => "Free Camera",
                HotkeyActions.AllDisableAi => "Disable All AI",
                HotkeyActions.TorrentNoDeath => "Torrent No Death",
                HotkeyActions.Silent => "Silent",
                HotkeyActions.Hidden => "Hidden",
                HotkeyActions.ShowAllResistances => "Show Resistances",
                HotkeyActions.PopoutResistances => "Popout Resistances",
                HotkeyActions.ShowDefenses => "Show Defenses",
                HotkeyActions.SetTargetCustomHp => "Set Custom HP",
                HotkeyActions.FreezeTargetHp => "Freeze HP",
                HotkeyActions.LockHp => "Lock HP",
                _ => action.ToString()
            };
        }
    }
}
