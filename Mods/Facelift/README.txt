Facelift 0.1.0  -  a mod for Melvor Idle 2          (in development, not released yet)
=====================================================================================

A new look for the whole game: an adventurer's field guide. Charcoal panels with thin
bronze lines instead of torn stone, readable warm text, serif titles, and a few tidier
screens. The game's own pictures (items, monsters, trees, backgrounds) stay as they are.

Tested with game version 0.11.6-alpha.1.


WHAT CHANGES
------------
New look
  - Every panel, button, tab, tooltip and frame of the game gets a clean charcoal
    surface with a thin bronze line and small cut corners. Buttons keep their meaning:
    green for "go", red for "stop" or danger, amber when you point at them.
  - Text is warm ivory; grey notes stay quieter; the neon greens and oranges are softer.
  - Titles (page names, "Equipment", "Offensive Stats", area and tree names...) are in a
    serif font (Georgia, from Windows).
  - Numbers use figures of one width, so counters such as XP per hour do not jitter.
  - Item counts on pictures sit on a dark backing, so a white "2.61K" never disappears
    into a white icon.
  - The windows of other mods (Cheat Menu, One Style, Action Queue...) keep their own look:
    Facelift dresses only the game's screens. This also keeps a big mod window quick to open
    (the Cheat Menu froze for about 14 seconds on its first opening before).

Tidier screens
  - Top bar: the version text and the greyed "Cozy UI Mode - Coming soon" switch are
    hidden. The switch comes back by itself once the game's Cozy mode really works.
  - Shop: opens on its first category instead of an empty page.
  - Bank: with no item selected, the right panel shows Bank Space, Bank Value, Tab Value
    and a bar, instead of a red "No item selected.".
  - Notifications: at most four per side and one banner in the middle at a time; older
    ones leave early. Their rewards are in your bank already, nothing is lost.
  - Enemy cards: "Special Abilities: None" and "Passives: None" are left out.


HOW TO USE
----------
It works as soon as the game starts. To change anything, open the loader's Mods menu
(the "Mods" button in the top bar, or F1) and choose Facelift. Each switch works at once:

  New look, Tidy top bar, Shop opens a category, Bank summary, Fewer notifications,
  Hide empty rows, plus "Game's own look (all off)" and "Everything on".

Your choices are kept in mods\Facelift\facelift.json.


GOOD TO KNOW
------------
- Nothing is written into your save, and no game file is changed. Facelift changes the
  game's styles only in memory while the game runs, writes down what each one was, and
  puts every one back when you switch it off.
- When the game's own "Cozy UI Mode" is switched on (once the developers finish it),
  Facelift steps aside by itself and the game's look returns.
- A few special pictures keep the game's look (Agility courses, Astrology constellations,
  the Slayer master cards, the "You died" screen): they are drawn art, not frames.
- No picture of the game is shipped with the mod; every new frame is drawn by the mod.


UNINSTALL
---------
Delete the mods\Facelift folder. The game looks as before on its next start.
