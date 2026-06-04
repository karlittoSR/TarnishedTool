<div align="center">
  <img height="130" alt="Icon" src="TarnishedTool/Assets/icon.ico">
  <h1 style="display: inline; vertical-align: middle; margin-left: 10px;">TarnishedTool — Speedrun Edition</h1>
</div>

<p>A fork of <a href="https://github.com/borgCode/TarnishedTool">Tarnished Tool</a> by borgCode, tailored for speedrun session workflow. Adds clean session management (Detach), richer in-game hotkey feedback, and quality-of-life improvements for runners.</p>

[![Latest Release](https://img.shields.io/github/v/release/karlittoSR/TarnishedTool.svg)](https://github.com/karlittoSR/TarnishedTool/releases/latest)

**This tool is strictly for offline use only, it directly manipulates game memory which violates the Terms of Service and will most likely lead to a ban if you use this online.**

*When attached, the loading screen title in game changes to "TarnishedTool - Speedrun Edition".*

---

## Speedrun Edition — Features

This fork focuses on reliability and workflow improvements for speedrunners: safer session cleanup, predictable hotkey behavior, and small utilities that make practice and runs easier.

### Clean Session Management (Detach)
* **Detach button** — resets all active toggles, uninstalls all hooks, frees the code cave and releases the game process in one click, leaving the game in a vanilla state ready for a run.
* The Detach button is only enabled when the player is **in-world** (prevents crashes from null pointers on the main menu).
* **Closing the tool** (red X / Alt+F4) only saves window settings — it does **not** reset game state. Always use the Detach button before closing if you want the game left in a clean vanilla state.

### Hotkey Notifications
* Every hotkey now shows an **ON / OFF toast notification** (green / red) instead of a generic one-shot popup.
* All notification labels use human-readable names (no raw enum names).
* Only one toast is on screen at a time — the latest one always replaces the previous.
* Notifications are **ON by default**.

### IGT Overlay
* Toggle **Show IGT** in the Utility tab to open a draggable transparent overlay showing current In-Game Time.
* Updates every 16 ms (1-frame precision at 60 fps).
* Overlay position is saved between sessions — drag it wherever you want it.

### Acts Overlay
* Toggle **Show Acts Overlay** in the Target tab to show the locked-on target's recent act history in a draggable transparent overlay.
* During Force Acts sequences, the overlay shows the configured act chain and highlights the act currently observed from the boss.
* The overlay is display-only and does not change Repeat Last Act or Force Act state.
* A **hotkey binding** for the Acts Overlay toggle is available in the Settings tab.

### Other Additions
* **Import from Practice Tool** — import hotkey bindings directly from the Elden Ring Practice Tool, mapping compatible bindings in one click.
* **Change Runes** — set your exact rune count directly (clamped to valid game range).
* **Draw Stable Position** — rendering toggle to display the player's stable position indicator in-game, bindable to a hotkey.
* **No-Clip** now defaults to speed `3`.

---

## Features
This tool offers plenty of features such as:
* Auto-Attaching to the game.
* Saving / Restoring position.
* Character cheats such as: No Death, No Damage, Infinite Items, Stat Editing, No Time Change on Death, and many more.
* Changing the game cycle (Takes into account the bugged scaling of Fia's Champions)
* Grace / Boss warps.
* Custom Location Warps.
* Unlock All Graces & Maps options (with an option to remove the "Map Found" Pop-Up)
* Custom Grace Unlocking Presets (with the ability to unlock on game start)
* Lock Divine Lion's phases / force phases (both Rauh and Belurat)
* Forcing Elden beast to do Elden Stars with specific follow ups.
* Disabling Rykard's Mega attack.
* Disable Ai / Enemies no Death and more
* Boss Revives (with the option to revive back to first encounter)
* Many options for the locked on target.
* Warping out of uncleared dungeons / during combat.
* Adjusting Game Speed.
* Adjusting FPS Cap.
* No Clip (works with keyboard and mouse)
* Free Cam.
* Rendering Settings such as: Hitbox View, Event view, Sound view, Ragdolls, High/Low Hit, Map Tiles, and more.
* Opening various menus and shops from anywhere.
* Applying / Remove Special Effects as well as seeing active Special Effects on player
* Item Spawn / Mass Spawn / Item preset creation (With the ability to spawn a loadout or a weapon on game start)
* Changing Weather & Day Time.
* Event Flags checking, activating, and deactivating.
* Unlocking All Affinites & Gestures.
* Useful flag scripts like skipping Metyr's Questline, deactivating the snowfield mausoleum, and toggling the DLC clear flag.
* Events Logger.
* Applying / Removing and seeing the Player's Active SpEffects.
* Param Patcher.
* Character List (NpcManager) with features such as warping to the selected entity, AI Viewer and AI Script injections.
* Debug features such as animation IDs (both player and enemies) and Enemy Acts.
* Logo Skip.
* Stutter Fix / Disable Achievements options.




## Credits
- [DSLuaDecompiler](https://github.com/ElaDiDu/DSLuaDecompiler) – For looking at AI scripts  
  by [katalash](https://github.com/katalash) and [ElaDiDu](https://github.com/ElaDiDu)
- [ESDLang](https://github.com/thefifthmatt/ESDLang) – For looking at Npc talk scripts.  
  by [thefifthmatt](https://github.com/thefifthmatt)
- [Smithbox](https://github.com/vawser/Smithbox) - For looking at different game params and maps
  by [vawser](https://github.com/vawser)
- [DarkScript3](https://github.com/AinTunez/DarkScript3) – For looking at EMEVD files by [AinTunez](https://github.com/AinTunez)
- [ERAiAPI](https://eladidu.github.io/readable-ds-lua/index.html) - Documentation for Elden Ring AI
  by [ElaDiDu](https://github.com/ElaDiDu)
- [Recycle](https://www.youtube.com/@1recyclebin1) - For helping with providing a huge amount of ids, the app icon and testing in general.
- [Marshaal](https://www.twitch.tv/marshaal04) - For help with all the boss warp locations.
- [Oppai](https://www.youtube.com/channel/UCFfKUbX6L8a3IO4fbWEIlYw) - For providing lists of graces needed for All Remembrances runs.
- [Bender](https://www.twitch.tv/benderzgreat) - For lots of thorough testing and feedback
- [axd1x8a](https://github.com/axd1x8a), [nex3](https://github.com/nex3) and [ndahn](https://github.com/ndahn) - For working on Elden Ring's ParamDefs and [vawser](https://github.com/vawser) for both working on the ParamDefs as well as giving permissions to borrow them from [Smithbox](https://github.com/vawser/Smithbox).
- [ooloh](https://www.youtube.com/@ooloh/) - For adding Dusk to Set Time.
