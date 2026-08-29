# Changelog

All notable changes to this project will be documented in this file.

## [v1.5.0] - 2026-08-29

Upstream Tarnished Tool v1.1.0, v1.1.1 and V1.2.0 merged into the fork — everything below comes from [borgCode/TarnishedTool](https://github.com/borgCode/TarnishedTool) unless stated otherwise. The Speedrun Edition features are unchanged.

### Added
- **Elden Ring 1.17 (`2.7.0.0`) is a supported build.** It now has a complete address table, so the tool no longer falls back to a pattern scan on it and no feature is greyed out. The scan fallback, the address report and the "new game version" notice stay for whatever patch comes next.
- **Speedy Buffing** (Player tab) — speeds the game up while buffing, with an **Allow in Combat** option. Bindable to a hotkey.
- **Damage multiplier** — separate outgoing and incoming factors, team-aware, with a hotkey reminder when it is left on.
- **FP options** — read and set current/max FP from the Player tab, alongside the existing HP controls.
- **Disable Cutscenes**, **No menu delay** and **No quitout message** (the message shown when the game did not exit cleanly), the last two guarded so they do not patch the wrong bytes on older game builds.
- **Break Nearby Objects** button (Utility tab).
- **Unlock Summoning Pools** and **Unlock All Colosseums** (Event tab).
- **Unlock Gestures is now a selection window** — pick the gestures you want instead of all of them. The hotkey still unlocks everything in one press.
- **No Godrick's Great Rune healing / FP restoration on load.**
- **Player Identity** section (Advanced tab) — PlayerGameData character type, character type and team type, for the case where nothing in the world can be interacted with.
- **Tarnished Pack support** — updated param entries and the new item IDs, with the horse skins flagged to avoid an online ban.
- Extra entries in **Activate on Launch**: Reset Enemies with Rest, Speedy Buffing, Disable Cutscenes.
- **Accept Macros**, **Hotkey Reminder** and **Show Used Hotkeys** checkboxes, folded into the fork's hotkey panel layout.

### Changed
- **The AOB scanner was rewritten upstream** into a single queued pass over the module: patterns are anchored on their rarest byte pair and matched in one sweep instead of one full scan per pattern. Missing patterns were filled in so a future game patch has more to fall back on. The fork's version-stamped, module-relative backup file is preserved on top of it — saved addresses are still dropped when the game build changes rather than replayed into unrelated code.
- The equip and memorized-spell function lookups keep their own scanner: one of them needs several candidate matches and a proof step, which the queued engine cannot express.
- Addresses are `nint` throughout instead of `long`/`IntPtr` mixes.
- The code cave grew from `0x5000` to `0x6000`: upstream's damage-multiplier and speedy-buffing regions claimed `0x4200`–`0x4900`, so the fork's equip and spell scratch space moved to `0x5000`.

### Fixed
- **Failures to attach to the game.**
- **Boss warp and revive for Adan, Fire Giant and Bayle.**
- **The error message when invading NPCs**, caused by the character-type fixes.
- **Missing whetblade events.**
- **Show Used Hotkeys** listed only the Player tab; it now lists every tab.
- Several broken hotkey bindings.

## [v1.4.2] - 2026-08-28

### Added
- **Address report on every attach.** Each attach writes the complete list of resolved game addresses to `%APPDATA%\TarnishedTool\diagnostics.log`, as module-relative offsets — the exact form the version tables store — along with the ones that came back empty. Supporting the next game patch now starts by reading that file.

### Changed
- **The Detach button is gone.** It existed back when detaching cleanly from the game looked possible; it is not, so the button and everything it implied went with it. **Launch Game** is now always available instead of being greyed out while attached, and nothing can stop the tool from closing — the "load back in before closing" popup is gone. Closing still restores the game: toggles back to vanilla, hooks uninstalled, code cave freed, loading-screen title restored, process handle released.
- **A new game version is now handled as information, not an error.** Elden Ring 1.17 (file version `2.7.0.0`) has no offset table yet, so the tool locates addresses by pattern scan. The notice saying so used to be a modal popup raised *before* that scan, blocking the very scan it announced until dismissed, naming the version as "null", and reappearing on every single attach. It now comes after the scan, names the build, lists the affected features in plain language, and is shown once per game version.
- **Features whose addresses cannot be found are greyed out** instead of staying clickable and doing nothing: Lock HP, Set FPS cap, Freeze Health, Inject AI Script, and the ChrIns window's per-entity Warp. They bind to the resolved address itself, so they come back on their own the day a version table covers the build.

### Fixed
- **The tool no longer calls addresses it could not resolve.** A few addresses have no pattern and exist only in the per-version tables; on a build with no table they stayed at zero, and the shellcode paths ran `call 0` — which crashes the game. Rest on Warp, the local-to-map coordinate conversion and the AI script injection all did this the day the game updated to 1.17.
- **Saved fallback addresses are no longer replayed across game versions.** They were kept as absolute addresses with no record of the build they came from, so after a game patch a failed pattern fell back to the previous version's address and installed a patch in the middle of unrelated code. They are now stamped with the game version, stored module-relative, and dropped when the build changes.
- **Restoring a position outside the game world no longer crashes the game.** A restore from the main menu, or during the loading screen of a quitout, fired a cross-area warp: the shellcode calls a game function that dereferences a world that is not there. Every action that moves the player — restore position, grace / boss / custom warps, segment reset, character snapshot apply — now checks that the world is loaded *and* that no loading screen is covering it, since the player pointer can already be non-null while the world is still being built.
- **An empty position slot is no longer a valid destination.** Saving a position from the main menu used to fill the slot with zeroes and mark it as saved; restoring it then warped to map `0`, which does not exist, and crashed the game. Saving outside the world is now refused, and a warp to block id `0` is rejected.

## [v1.4.1] - 2026-08-18

### Added
- **Auto Force Acts.** A new **Auto** checkbox next to the Force Acts button re-applies the configured act sequence by itself when the same boss is retried, so testing a strategy no longer means pressing the button or the hotkey before every attempt. A target is only eligible when it shares the `NpcThinkParamId` of the last manual force — act numbers are indices into that param, so a second phase with another moveset is skipped rather than fed meaningless acts — and only on a fresh fight, meaning a new instance or a boss back at full HP, so unlocking and relocking mid fight changes nothing. Checking the box never affects the target currently locked on, only the next one.

## [v1.4.0] - 2026-08-17

### Added
- **Slope Indicator.** A new Player-tab checkbox opens a small draggable overlay dot that reads the terrain under the player, so a runner can see whether a jump gains time: green uphill and blue on flat ground (jump gains), red downhill (jump loses), grey while standing still or right after a load. The reading is the slope along the direction of travel — rise over the horizontal distance actually covered — averaged across the last half second, so it is independent of walking, sprinting or riding Torrent and does not flicker on stairs, roots or single-frame physics pops. Warps and map-block changes reset the reading instead of registering as a cliff. The dot starts centred on screen until it is dragged, hovering it shows the exact gradient, its position is remembered, and the toggle is bindable to a hotkey.

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
