# Changelog

All notable changes to this project will be documented in this file.

## [v1.1.5] - 2026-06-04

### Added
- **Acts Overlay hotkey** — bind a key to toggle the Acts Overlay directly from the Settings tab, without touching the Target tab. Fully retrocompatible: existing settings files default the binding to unbound.

## [v1.1.4] - 2026-06-04

### Added
- Import hotkey bindings directly from the Elden Ring Practice Tool — maps compatible bindings in one click.

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
