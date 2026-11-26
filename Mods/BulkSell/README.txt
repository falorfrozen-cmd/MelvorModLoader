Bulk Sell 0.4.0  -  a mod for Melvor Idle 2
===========================================

Quality-of-life buttons for the bank. Not a cheat: everything goes through the game's own
selling, burying and opening, at the game's own prices.

Tested with game version 0.11.7 (the update of 25 November 2026) and Melvor Mod Loader 0.3.0.


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
Shift+click marks every row between the last row you clicked and this one (as in Windows).
AUTO-SELL (with the One Style mod 0.4.0 or newer): the window's "Auto-sell" button opens rules for new Magic
and Rare items as they drop:
  - every Magic, every Rare, Rares with 3 affixes (the ones with 4 stay), Magic or Rare below an
    item level;
  - gear for other combat styles: "Other styles" follows your One Style class (a Melee character
    sells Ranged and Magic gear), or tick Melee, Ranged or Magic yourself. Rings, amulets and capes
    fit every class and never count. This rule goes before Keep affixes, since your character
    cannot use that gear. If you ever change your class, gear of your new style may already have
    been sold;
  - "Sell by slot": every Magic and Rare item of the slots you pick (the slot you wear a Unique in,
    for example);
  - affixes: click to cycle off, Sell (red), Keep (green). Keep saves an item from every rule but
    the style rule. Search the affixes, show one group (Attack, On hit, Defence, Gains) or only the
    ones you set ("Active"). The number on a button is how many unlocked items in the bank have it.
"Ready-made rules" sets the rarity, style and level rules in one go and leaves your affix choices
alone. "Reset rules" turns everything off (asks once). Locked, worn and Unique items are never
sold. Only an item that has just dropped counts as new: one you take off, or one that was
already in the bank, is never sold on its own, and after you change a rule only the next drops
are judged by it. "Sell matching items now" shows what the rules match in the bank and sells it
with a second click. "Show in the list" opens those items in the list, all marked, so you can
unmark the ones to keep and sell the rest. The line turns orange when many Rare items match.
"Last N sales" lists the last 30 auto-sales with the time and the reason. All rules start off;
they are kept per save in "bulksell-auto.json": two characters with the same name never share them.
F7 opens the Bulk sell window from anywhere (so does the Mods button in the top bar, where the key can be changed).


GOOD TO KNOW
------------
- Every one-click button asks once before it acts (the button text turns into a question).
- Stacks you have locked in the bank are never sold, buried or opened.
- The junk list is remembered per save in "bulksell.json" next to the mod (under the save's
  id: two characters with the same name do not share it, and renaming a character keeps it; a
  list an older version kept under the character's name is taken over once).
- A list file that cannot be read is never written over: it is set aside under another name
  ("....unreadable-...") and a new one is started.
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
