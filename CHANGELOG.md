# Changelog

All notable changes to this project will be documented in this file.

## [v1.2.0] - 2026-07-17

### Added
- **Line Comparison** — a new tool (Advanced tab) to time a run segment from a fixed start point to a flagged end point using in-game time, with no LiveSplit needed. Set a start and end (each with its own radius), restore to the start, and the timer runs automatically from when you leave the start until you reach the end. Attempts are collected in a stay-on-top window with a live timer, editable names, delta-vs-best, and the fastest highlighted gold (with a subtle gold flash on a new record). Keeps the best 10 attempts and can be driven entirely by hotkeys (open, set start, set end, restore to start).
- **Golden Erdtree button** — new event trigger to burn the Erdtree, integrated from upstream ([#116](https://github.com/borgCode/TarnishedTool/pull/116)).
- **Refresh from storage on travel and boss revive rest** — travel and boss revive now re-read state from storage so restored positions and revive lists stay accurate, integrated from upstream ([#121](https://github.com/borgCode/TarnishedTool/pull/121)).

### Fixed
- **Hook registry is now cleared on detach** — prevents stale hook entries from carrying over between attach sessions, integrated from upstream ([#118](https://github.com/borgCode/TarnishedTool/pull/118)).

## [v1.1.5] - 2026-07-16

### Fixed
- **Loading screen title now properly restored on detach.** The tool saves the original hint text before replacing it with the reminder title and writes it back when detaching — previously one hint slot kept showing "Tarnished Tool - Speedrun Edition" on random loading screens until the game was restarted.
- **Fixed instant game crash when pressing a hotkey right after attaching.** During the ~2 seconds after (re)attach the tool's code cave is not allocated yet; triggering a feature then (e.g. noclip) installed a hook jumping to invalid memory. Hotkeys and hook installs are now blocked until initialization completes.
- Stale code cave address is now cleared when the game process exits, preventing the same crash after relaunching the game.


## [v1.1.4] - 2026-06-04

### Added
- Import hotkey bindings directly from the Elden Ring Practice Tool — maps compatible bindings in one click.
- **Acts Overlay hotkey** — bind a key to toggle the Acts Overlay directly from the Settings tab. Retrocompatible: existing settings files default the binding to unbound.

### Fixed
- Hotkey buttons now wrap to a second line when they overflow, keeping all controls visible without clipping.
- Camera angle is now correctly restored when loading a saved character position.


## [v1.1.3] - 2024 (hotfix)

### Fixed
- **Critical:** Detach and program close now properly reset ALL toggles from the game state.
  - Target toggles: targeting view, backstab/crit draws, freeze AI, show defenses/attacks/DPS/speffects/AI info now properly disabled.
  - Travel toggles: "Show All Graces" and "Show All Maps" now properly disabled.
  - Player toggles: HP Regen (Hot) and FP Regen now properly disabled.
  - Utility toggles: IGT overlay and Player Movement now properly disabled.
- Comprehensive cleanup on both in-world and main-menu detach states.
- Ensures complete vanilla state restoration for speedrunners to switch immediately from training to runs.


## [Unreleased] - v1.1.2

### Added
- Enhanced logging for physical damage types in the Attack Information panel (now displays raw byte value).
- Diagnostics: AttackTypeDebugger and DiagnosticsLogger tools added for dev/debug.

### Changed
- Suppress zero-damage entries in Attack Information panel (misses/jumps no longer shown).
- UI tweaks and ViewModel improvements for attack info and target tracking.
- Project configuration updates.

### Fixed
- Minor fixes around attack info offsets and general stability improvements.


## [v1.1.1] - previous release
- See repository release notes.
