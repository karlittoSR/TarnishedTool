# Changelog

All notable changes to this project will be documented in this file.

## [v1.3.0] - 2026-08-16

### Added
- **Segment Timer event-flag finishes.** A segment still starts from a position, but can now finish either inside a positional radius or when a chosen event flag transitions to ON/OFF. **Scan** opens the existing Event Logger without interfering with an active timer listener.
- **Complete character snapshots for Saved Segments.** Saves can restore equipment, stats, rune level, Scadutree and Revered Spirit Ash blessing levels, flasks, physick, consumables, and the configured Ash of War for weapons that must be spawned.
- **Memorized spells in character snapshots.** The full fourteen-slot memorized-spell loadout and the selected slot are captured and restored through the game's own Memorize Spell path, so the spell list and its dependent caches stay consistent. Empty slots round-trip as empty, and restore never writes past the character's unlocked memory-slot capacity.
- **Folders for Saved Segments.** Create, rename, nest and delete folders, and drag segments or folders to move and reorder them. Folders are stored by stable id, so renaming or moving one never rewrites the segments inside it, and the single-segment export format is unchanged.
- **Boss-aware Reset to Start.** Restoring resets nearby enemies, refills HP/FP and flasks, and revives/reloads bosses using normal, large, and giant proximity tiers; long-distance restores use a loading warp.
- **Personal PB and imported Reference separation.** A sender's best is a protected blue benchmark while the recipient builds an independent gold PB. Same-library exports preserve both values exactly.
- **Versioned JSON sharing.** Export either one selected segment or the complete library. The importer accepts current envelopes, earlier JSON arrays, and legacy segment codes.

### Changed
- Renamed **Line Comparison** to **Segment Timer** and **Saved Lines** to **Saved Segments**, with a smaller and cleaner timer workflow.
- Starts default to radius `2`; finishes can be configured as a position or event flag.
- Session results now retain eight ordinary attempts plus protected PB/reference rows. Clearing runs never deletes comparison data; PB/reference deletion remains explicit.
- Saved-segment identity now includes the normalized start/finish definition, radii, finish flag, and complete character snapshot. Name and comparison times do not create duplicates.
- Saved libraries now use `%AppData%\TarnishedTool\lines.json`; existing `lines.txt` data migrates automatically.

### Fixed
- Corrected DLC-era equipment, active-slot, quick-item and Physick memory layouts; repeated segment loads no longer alternate or shift armor and talismans.
- Corrected the DLC-era inventory-entry stride so owned equipment is detected instead of being granted repeatedly; legacy consumable snapshots retain their original reader for safe restores.
- Loading, renaming, updating, importing, or clearing a saved segment no longer erases its gold time.
- **Update** now applies changed positions, radii, finish type/flag, and character state while preserving the segment's identity and comparison times.
- Restore ordering is deterministic: zone reset/warp, exact position, character snapshot, then grace refill. This avoids post-warp falls, stale equipment, incorrect resources, and Ash of War replacement errors.
- Boss resets no longer trigger for unrelated nearby practice lines, while large and giant bosses still reload reliably inside their larger arenas.
- Equivalent imports are skipped even when only their name or gold differs.
- Deleting an active saved segment now detaches it from the timer, preventing later PBs from being written to an orphaned in-memory entry.
- Event Logger hooks are reference-counted so the scanner and Segment Timer can safely run together.

## [v1.2.1] - 2026-07-18

### Added
- **Line sharing (Export / Import position).** Export a line's start and end (full precision — position, orientation and both trigger radii) to a shareable code, and import a code from anyone to train the exact same line. The results **Copy** also embeds an importable code alongside the readable positions.
- **Saved Lines library.** A **List** button opens a manager window to save named lines (bosses/segments you train often) and load them in one click (or double-click). Each saved line keeps its **personal best (gold)**, shown in the list; loading a line seeds that PB as "Attempt 1" (a target to beat) and displays it in the main window. The PB **updates automatically** whenever you beat it. Stored in `%AppData%\TarnishedTool\lines.txt` (portable, shareable).
- Brief on-screen confirmations when positions/results are copied or a line is imported.

### Fixed
- **Crash when teleporting then immediately quitting out.** Restoring a position (or warping) then force-quitting in the same instant raced the game's still-settling teleport; quit/save now briefly defer when a teleport just happened, and the delayed no-gravity reset re-resolves its pointer instead of writing a freed one.

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
