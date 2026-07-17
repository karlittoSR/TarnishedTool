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
                HotkeyActions.DisableTargetAi => "Disable AI",
                HotkeyActions.DisableAllExceptTargetAi => "Disable All Except Target AI",
                HotkeyActions.TargetNoStagger => "No Stagger",
                HotkeyActions.TargetRepeatAct => "Repeat Last Act",
                HotkeyActions.TargetTargetingView => "Targeting View",
                HotkeyActions.ShowAttackInfo => "Show Attack Info",
                HotkeyActions.ShowTargetSpEffects => "Show Active Special Effects",
                HotkeyActions.ShowActsOverlay => "Show Acts Overlay",
                HotkeyActions.TargetNoMove => "No Move",
                HotkeyActions.TargetNoAttack => "No Attack",
                HotkeyActions.AiInfo => "Show AI Info",
                HotkeyActions.EnableTargetOptions => "Target Options",
                // Player
                HotkeyActions.FasterDeath => "Faster Death",
                HotkeyActions.NoRuneLossOnDeath => "No Rune Loss",
                HotkeyActions.NoRuneArcLossOnDeath => "No Rune Arc Loss",
                HotkeyActions.NoRunesFromEnemies => "No Rune Gain",
                HotkeyActions.NoTimeChangeOnDeath => "No Time Change on Death",
                HotkeyActions.TorrentAnywhere => "Torrent Anywhere",
                HotkeyActions.FpRegen => "FP Regen",
                HotkeyActions.HealOverTime => "Heal Over Time",
                HotkeyActions.NoUpgradeCost => "No Upgrade Cost",
                HotkeyActions.AllDiscardable => "All Items Discardable",
                HotkeyActions.Level1 => "Set Level 1",
                HotkeyActions.MaxLevel => "Set Max Level",
                HotkeyActions.SetNgCycleTo7 => "Set NG+7",
                HotkeyActions.ApplySpEffect => "Apply Sp Effect",
                HotkeyActions.RemoveSpEffect => "Remove Sp Effect",
                // Position / world
                HotkeyActions.Rest => "Rest",
                HotkeyActions.RuneArc => "Use Rune Arc",
                HotkeyActions.SetMorning => "Set Morning",
                HotkeyActions.SetNoon => "Set Noon",
                HotkeyActions.SetDusk => "Set Dusk",
                HotkeyActions.SetNight => "Set Night",
                // Enemy / target actions
                HotkeyActions.AllNoDeath => "All No Death",
                HotkeyActions.AllNoDamage => "All No Damage",
                HotkeyActions.AllNoHit => "All No Hit",
                HotkeyActions.AllNoAttack => "All No Attack",
                HotkeyActions.AllNoMove => "All No Move",
                HotkeyActions.AllTargetingView => "All Targeting View",
                HotkeyActions.KillTarget => "Kill Target",
                HotkeyActions.KillAllExceptTarget => "Kill All Except Target",
                HotkeyActions.SetTargetMaxHp => "Set Target Max HP",
                HotkeyActions.ResetTargetPosition => "Reset Target Position",
                HotkeyActions.ReviveSelectedBoss => "Revive Boss",
                HotkeyActions.ReviveSelectedBossFirstEncounter => "Revive Boss (First Encounter)",
                HotkeyActions.ReviveAllBosses => "Revive All Bosses",
                HotkeyActions.ReviveAllBossesFirstEncounter => "Revive All Bosses (First Encounter)",
                HotkeyActions.ToggleResetEnemiesWithRest => "Reset Enemies on Rest",
                HotkeyActions.ForceActSequence => "Force Act Sequence",
                HotkeyActions.ForceEbActSequence => "Force EB Act Sequence",
                HotkeyActions.IncrementForceAct => "Force Act +1",
                HotkeyActions.DecrementForceAct => "Force Act -1",
                HotkeyActions.SetForceActToZero => "Reset Force Act",
                HotkeyActions.IncreaseTargetSpeed => "Target Speed +",
                HotkeyActions.DecreaseTargetSpeed => "Target Speed -",
                HotkeyActions.ToggleTargetSpeed => "Toggle Target Speed",
                // Utility / visual
                HotkeyActions.ToggleFreezeWorld => "Freeze World",
                HotkeyActions.IncreaseNoClipSpeed => "No Clip Speed +",
                HotkeyActions.DecreaseNoClipSpeed => "No Clip Speed -",
                HotkeyActions.DrawPlayerSound => "Draw Player Sound",
                HotkeyActions.DrawRagdolls => "Draw Ragdolls",
                HotkeyActions.DrawPoiseBars => "Draw Poise Bars",
                HotkeyActions.DrawStablePos => "Draw Stable Position",
                HotkeyActions.DrawLowHit => "Draw Low Hit",
                HotkeyActions.DrawHighHit => "Draw High Hit",
                HotkeyActions.OpenMapInCombat => "Map in Combat",
                HotkeyActions.WarpInDungeons => "Warp in Dungeons",
                HotkeyActions.DropRate => "Drop Rate",
                HotkeyActions.DrawMapTiles1 => "Draw Map Tiles (Layer 1)",
                HotkeyActions.DrawMapTiles2 => "Draw Map Tiles (Layer 2)",
                HotkeyActions.DrawMiniMap => "Draw Mini Map",
                HotkeyActions.DrawTilesOnWorldMap => "Draw Tiles on World Map",
                HotkeyActions.HideMap => "Hide Map",
                HotkeyActions.HideCharacters => "Hide Characters",
                HotkeyActions.ToggleNextNgCycle => "Toggle Next NG Cycle",
                // FPS
                HotkeyActions.Set20Fps => "Set 20 FPS",
                HotkeyActions.Set30Fps => "Set 30 FPS",
                HotkeyActions.Set60Fps => "Set 60 FPS",
                HotkeyActions.Set90Fps => "Set 90 FPS",
                HotkeyActions.Set120Fps => "Set 120 FPS",
                HotkeyActions.Set180Fps => "Set 180 FPS",
                HotkeyActions.Set240Fps => "Set 240 FPS",
                // Grace / warp / maps
                HotkeyActions.UnlockMainGameMaps => "Unlock Main Game Maps",
                HotkeyActions.UnlockDlcMaps => "Unlock DLC Maps",
                HotkeyActions.UnlockAllMainGameGraces => "Unlock All Main Game Graces",
                HotkeyActions.UnlockAllDlcGraces => "Unlock All DLC Graces",
                HotkeyActions.UnlockAllMainRemembrancesGraces => "Unlock Main Remembrances Graces",
                HotkeyActions.UnlockAllDlcRemembrancesGraces => "Unlock DLC Remembrances Graces",
                HotkeyActions.UnlockPresetGraces => "Unlock Preset Graces",
                HotkeyActions.ShowAllGraces => "Show All Graces",
                HotkeyActions.ShowAllMaps => "Show All Maps",
                HotkeyActions.WarpToGrace => "Warp to Grace",
                HotkeyActions.WarpToBoss => "Warp to Boss",
                HotkeyActions.WarpToCustomLocation => "Warp to Custom Location",
                HotkeyActions.RestOnWarp => "Rest on Warp",
                HotkeyActions.RestOnRevive => "Rest on Revive",
                // Status effects (inflict on target)
                HotkeyActions.TogglePoise => "Inflict Poise Break",
                HotkeyActions.ToggleSleep => "Inflict Sleep",
                HotkeyActions.TogglePoison => "Inflict Poison",
                HotkeyActions.ToggleRot => "Inflict Scarlet Rot",
                HotkeyActions.ToggleFrost => "Inflict Frostbite",
                HotkeyActions.ToggleBleed => "Inflict Bleed",
                HotkeyActions.ToggleMadness => "Inflict Madness",
                HotkeyActions.ToggleDeathblight => "Inflict Deathblight",
                // Shop / upgrade / NPC actions
                HotkeyActions.LevelUp => "Level Up",
                HotkeyActions.AllotFlasks => "Allot Flasks",
                HotkeyActions.MemorizeSpells => "Memorize Spells",
                HotkeyActions.MixPhysick => "Mix Physick",
                HotkeyActions.OpenChest => "Open Chest",
                HotkeyActions.GreatRunes => "Activate Great Rune",
                HotkeyActions.AshesOfWar => "Ashes of War",
                HotkeyActions.AlterGarments => "Alter Garments",
                HotkeyActions.Upgrade => "Upgrade",
                HotkeyActions.Sell => "Sell",
                HotkeyActions.Rebirth => "Rebirth",
                HotkeyActions.UpgradeFlask => "Upgrade Flask",
                HotkeyActions.IncreaseFlaskCharges => "Increase Flask Charges",
                HotkeyActions.OpenShopWindow => "Open Shop",
                HotkeyActions.FullShopLineup => "Full Shop Lineup",
                HotkeyActions.UnlockAffinites => "Unlock Affinities",
                HotkeyActions.UnlockGestures => "Unlock Gestures",
                HotkeyActions.UnlockMetyr => "Unlock Metyr",
                HotkeyActions.FightEldenBeast => "Fight Elden Beast",
                HotkeyActions.FightFortissax => "Fight Fortissax",
                // Events
                HotkeyActions.SetEvent => "Set Event",
                // Weather
                HotkeyActions.DefaultWeather => "Default Weather",
                HotkeyActions.RainyWeather => "Rainy Weather",
                HotkeyActions.SnowyWeather => "Snowy Weather",
                HotkeyActions.WindyRainWeather => "Windy Rain",
                HotkeyActions.FoggyWeather => "Foggy Weather",
                HotkeyActions.FlatCloudsWeather => "Flat Clouds",
                HotkeyActions.WindyPuffyClouds => "Windy Puffy Clouds",
                HotkeyActions.RainyHeavyFog => "Rainy Heavy Fog",
                HotkeyActions.ScatteredRain => "Scattered Rain",
                // Items / spawn
                HotkeyActions.SpawnItem => "Spawn Item",
                HotkeyActions.SpawnSelectedLoadout => "Spawn Loadout",
                HotkeyActions.SpawnCustomItem => "Spawn Custom Item",
                HotkeyActions.OpenLineComparison => "Line Comparison",
                HotkeyActions.SetLineStart => "Set Line Start",
                HotkeyActions.SetLineEnd => "Set Line End",
                HotkeyActions.RestoreLineStart => "Restore to Line Start",
                _ => action.ToString()
            };
        }
    }
}
