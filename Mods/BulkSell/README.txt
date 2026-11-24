Bulk Sell 0.3.0  -  a mod for Melvor Idle 2
===========================================

Quality-of-life buttons for the bank. Not a cheat: everything goes through the game's own
selling, burying and opening, at the game's own prices.

Tested with game version 0.11.6-alpha.1 and Melvor Mod Loader 0.1.3.


REQUIRES
--------
Melvor Mod Loader 0.1.3 or newer. Install the loader first.


INSTALL
-------
1. Open the game folder
   (Steam > right-click Melvor Idle 2 > Manage > Browse local files).
2. Extract this zip there. The mod ends up in the "mods" folder:
      mods\BulkSell\BulkSell.dll
3. Start the game.


HOW TO USE
----------
Open the bank. Next to the search box there are new buttons:
  Bulk sell   a table of everything you can sell. Click rows to mark them, use the filters,
              "Keep of each" and the column titles, then press "Sell marked" twice.
  Sell junk   sells your saved junk list. In the Bulk sell window, mark items and press
              "Add marked to junk" or "Remove marked from junk". Items on the list show
              "junk" in the first column; "My junk list" marks all of them.
  Bury all    buries every bone in the bank. Shown only when there is something to bury.
  Open all    opens every chest, bag and pack. Shown only when there is something to open.
The Bulk sell window has two rows of filters:
  Search, bank tab and kind (what the bank calls it). The search also finds a rare item by its
  base item ("amulet") and its affixes ("lifesteal").
  Gear slot (or "All gear" / "Not gear"), level ("Level 30 or lower"...) and,
  with the One Style mod, rarity (Normal, Magic, Rare, Unique, Magic + Rare) and affix.
  "Clear filters" puts them all back. The Level column shows a rare item's item level, or the
  level a piece of gear asks for; rare items have their rarity's colour, and their affixes
  show when you point at the name. Example: Rarity "Magic", "Mark: All shown", "Sell marked".
F7 opens the Bulk sell window from anywhere (so does the Mods button in the top bar, where the key can be changed).


GOOD TO KNOW
------------
- Every one-click button asks once before it acts (the button text turns into a question).
- Stacks you have locked in the bank are never sold, buried or opened.
- The junk list is remembered per character in "bulksell.json" next to the mod.
- One Style's rare items never go on the junk list (each is one of a kind): sell them with the
  Rarity filter and "Sell marked".


UNINSTALL
---------
Delete the folder "mods\BulkSell".


HELP AND UPDATES
----------------
Discord: https://discord.gg/q6aexZZSAf
Wiki:    https://falorfrozen-cmd.github.io/MelvorModLoader/  (guides for players and mod authors)
GitHub:  https://github.com/falorfrozen-cmd/MelvorModLoader  (source code; see LICENSE: free to use, not to re-upload)
When something does not work, look at "MelvorModLoader.log" in the game folder
(it is rewritten on every start) and include it when you ask for help.
