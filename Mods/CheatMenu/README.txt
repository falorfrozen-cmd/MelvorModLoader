Cheat Menu 0.8.1  -  a mod for Melvor Idle 2
============================================

One window with more than 60 settings and tools: bank slots, XP, game speed, skill bonuses,
combat, per-skill XP and speed, a loot filter, a list of materials no recipe should ask for,
and tools that give items, currency and levels.

Tested with game version 0.11.6-alpha.1 and Melvor Mod Loader 0.1.3.


REQUIRES
--------
Melvor Mod Loader 0.1.3 or newer. Install the loader first.


INSTALL
-------
1. Open the game folder
   (Steam > right-click Melvor Idle 2 > Manage > Browse local files).
2. Extract this zip there. The mod ends up in the "mods" folder:
      mods\CheatMenu\CheatMenu.dll
3. Start the game.


HOW TO USE
----------
Open a character and press F8 (or pick it from the Mods button in the top bar; the key can be changed or switched off there). Pick a section on the left and change what you like.
A setting takes effect the moment you change it (a slider: when you let go of it).
"Reset" on a row puts that one setting back; "Reset all settings" needs two clicks.

LANGUAGE: the list at the top of the window: English, 简体中文 (Simplified Chinese), 繁體中文 (Traditional Chinese), Español, Français, العربية (Arabic) and हिन्दी (Hindi).
Arabic is written from right to left, so the whole window is mirrored.
"Auto" follows the game's own language when the menu has it, English otherwise.
The choice is kept for all characters. Names that come from the game itself (items,
currencies, skills in the level list) are shown in the game's own language.
The Traditional Chinese text was first made by a player of the community. Thank you!


GOOD TO KNOW
------------
- Settings are remembered per character in "settings.json" next to the mod. They are NOT
  written into your save: remove the mod and the character is back to normal.
  What the tools give you (items, currency, levels) does stay.
- Loot filter and auto-sell never touch a stack you have locked in the bank.
- "Give item" and "Auto-sell from bank" each have their own item list: click it and type
  to search all items; they are grouped by the bank's own kinds.
- One Style's rare items are not in the item lists; they only come from monsters.
- God mode only covers combat. Agility and Thieving have their own settings on the Skills
  page: no damage from a failed obstacle, keep the obstacle's reward, and no damage when
  caught pickpocketing ("never stunned" only stops the stun).
- Game speed runs the whole game faster while you play. It does not change how fast the
  game computes offline progress.
- "Farming: normal pace" and "Ranching: normal pace" (Speed page) keep crops, the grain mill
  and animals at the game's normal pace while Game speed runs everything else faster.
  Farming also gets back what "Skill action speed" takes off its times. The skill's own
  speed on the Per skill page still counts.
- Ranching speed (Per skill page) gives the animals extra ticks while you play: the game
  itself has no bonus that speeds Ranching up. Like Game speed, it does not count for
  offline progress.
- "Skill action speed" and a skill's own speed on the Per skill page multiply: x2 and x4
  together are x8. Together they stop at x20 (no action is left with no time at all).
- A speed setting also counts for crops that are already growing: change it and the plots
  show their new time at once.
- Materials page: mark a material (Coal, for example) as "Not needed" and no recipe or item
  upgrade asks for it any more. Your bank is not touched. "Needed again" puts it back.
  Farming recipes follow after the character has been opened again.
- "Slayer task: most kills" (Combat page): no slayer task asks for more kills than this, e.g. 10,
  at every master. New, rerolled and extended tasks get it at once; a task already running is
  shortened (one already past it ends with its next kill). 0 = the game's own lengths, which grow
  by 3 kills per Slayer level.
- "Infinite ammo" (Combat page): arrows, bolts, javelins, boomerangs and throwing hammers are
  never used up. One is enough to shoot for ever.
- "Infinite lanterns" (Combat page): lit lanterns are never used up in dark areas. One is enough.
- "Ignore immunity" (Combat page): any attack style hits monsters that are immune to it, such as
  Wind Drifter ("Immune to Melee / Immune to Magic"). Poison and bleeding stick to them too.
- "No slayer area penalties" (Combat page): every slayer area loses its bad area effect (burn,
  less healing from food, quicksand and the others), as if you had bought each "Area Effect
  Removal" in the shop. Nothing is bought and no slayer coins are spent: switched off, the
  effects come back. The area's own page shows the effect gone while it is on.
- "Ranching: animals never die" (Skills page): your ranch animals stop aging. Their lifetime
  stands still while it is on (also while the game is closed); switched off, they age on from there.
- "Complete all quests" (Tools page) finishes every quest and gives its rewards, as if you had
  pressed Claim on each. Rewards that do not fit in the bank wait on the Quests page. This is
  written into your save and cannot be undone.
- This mod changes the balance of the game. Use it the way you enjoy playing.


UNINSTALL
---------
Delete the folder "mods\CheatMenu".


HELP AND UPDATES
----------------
Discord: https://discord.gg/q6aexZZSAf
Wiki:    https://falorfrozen-cmd.github.io/MelvorModLoader/  (guides for players and mod authors)
GitHub:  https://github.com/falorfrozen-cmd/MelvorModLoader  (source code; see LICENSE: free to use, not to re-upload)
When something does not work, look at "MelvorModLoader.log" in the game folder
(it is rewritten on every start) and include it when you ask for help.
