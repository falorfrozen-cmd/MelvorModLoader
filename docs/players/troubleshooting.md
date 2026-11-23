---
title: When something goes wrong
parent: For players
nav_order: 4
---

# When something goes wrong
{: .no_toc }

1. TOC
{:toc}

---

## First: the log file

The loader writes down everything it does in **`MelvorModLoader.log`**, in the game folder next to
`Melvor2.exe`. It is rewritten every time the game starts. A healthy start looks like this:

```
[16:39:54] Melvor Mod Loader 0.1.3 starting (.NET 10.0.15)
[16:39:54] Game data folder: C:\Program Files (x86)\Steam\steamapps\common\Melvor Idle 2\data_GamesByMalcs.Melvor2_windows_x86_64
[16:39:55] Game started
[16:39:58] [ActionQueue] Ready. Press F6 while a character is open.
[16:39:58] Mod loaded: ActionQueue 0.5.0 (ActionQueue.dll)
[16:39:58] [CheatMenu] Ready. Press F8 while a character is open.
[16:39:58] Mod loaded: CheatMenu 0.5.0 (CheatMenu.dll)
[16:39:58] 2 mod(s) loaded
```

When you ask for help, **attach this file**. It answers most questions at once.

## There is no green line when the game starts

| Check | What to do |
|:--|:--|
| Is `override.cfg` right next to `Melvor2.exe`? | If it is in a sub-folder (often named like the zip), move it and the `data_MelvorModLoader_windows_x86_64` folder up to the game folder. |
| Is there a `MelvorModLoader.log` in the game folder? | No log at all means the game never started the loader: the files are in the wrong place. |
| Does the log end with `STARTUP ERROR`? | The game was probably updated and the loader no longer fits. Delete `override.cfg` to play without mods, and look for a newer loader. |
| Did you start the game from Steam? | Yes, start it from Steam as usual. Starting `Melvor2.exe` directly makes it restart through Steam. |

## A mod does not show up

- Open the log and look for its name.
  - `Mod loaded: ...` - it is loaded. Open a character first; most mods only work inside a character. Check
    the mod's key in its `README.txt`.
  - `Mod FAILED to load: ... ` - the line says why. Usually the mod was made for another version of the game
    or the loader. Look for an update of that mod.
  - Not mentioned at all - the DLL is not where the loader looks. It must be `mods\<ModName>\<ModName>.dll`
    (or directly in `mods`). A mod extracted into its own extra folder (`mods\CheatMenu-0.5.0\mods\CheatMenu\...`)
    is too deep.
- One broken mod never stops the others or the game: it is reported and skipped.

## The game was updated and now something is wrong

Melvor Idle 2 is in Early Access and changes often. A mod uses the game's own code, so a game update can break
it until the mod is updated.

1. To play right away without any mods: delete `override.cfg` (or rename it). Put it back later.
2. To find the culprit: move mods out of the `mods` folder one by one.
3. Check the mod's page for an update, and say which game version you have (it is shown at the top of the game
   window, for example `v0.11.6-alpha.1`).

## "Loading Offline Progress..." takes very long

The game computes what happened while you were away before it lets you play. With the current loader and
mods this takes a few seconds at most, even after a long break (in our tests: half an hour of offline time in
about a tenth of a second). If it takes minutes, an old mod is probably fighting
with the calculation: update the loader and your mods (loader 0.1.3 or newer holds mods back during the
calculation).

## Questions

**Do mods work with Steam Cloud saves?**
Yes. Mods do not touch the save format. Your saves stay ordinary Melvor Idle 2 saves.

**Can I use mods on one character and not on another?**
The loader loads mods for the whole game, but most mods keep their settings per character. A cheat mod with
everything switched off changes nothing.

**Can mods get me into trouble?**
Mods change only your own game on your own computer. Melvor Idle 2 is played on your own, and we know of no
check in the game that looks for mods. Still, if you share saves or compare progress with others, be honest
about cheat mods.

**Does it work on Mac, Linux or Steam Deck?**
Not yet: the loader is built for Windows. On Linux and Steam Deck the Windows version of the game under
Proton has not been tested.

**Where do I ask for help?**
On [Discord](https://discord.gg/q6aexZZSAf) or in the Posts tab of the mod's Nexus page. Attach
`MelvorModLoader.log`.
