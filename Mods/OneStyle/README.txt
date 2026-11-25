One Style 0.4.0  -  a mod for Melvor Idle 2
==========================================

Play the whole game with one combat style: melee, ranged or magic. Pick your class once
and the game stops asking you to train the other two.

Tested with game version 0.11.7 (the update of 25 November 2026) and Melvor Mod Loader 0.3.0.


REQUIRES
--------
Melvor Mod Loader 0.3.0 or newer. Install the loader first. (0.3.0 gives your items back
if you ever remove One Style: see UNINSTALL.)


INSTALL
-------
1. Open the game folder
   (Steam > right-click Melvor Idle 2 > Manage > Browse local files).
2. Extract this zip there. The mod ends up in the "mods" folder:
      mods\OneStyle\OneStyle.dll
3. Start the game.


HOW TO USE
----------
Open a character, press the Mods button in the top bar and pick "One Style" (a key can be
given to it there). Click Melee, Ranged or Magic. Off gives you the normal game back.
The class is kept per character and can be changed at any time, for free.


WHAT YOUR CLASS DOES
--------------------
- Monsters immune to your style can be hit with it: Wind Drifter, the Amalgamations of
  the Sunken Tower, and the Blood Sanctum dungeon.
- The combat triangle is off for your class: no penalty and no bonus against any monster.
- Quest goals and monsters that ask for another style take yours. For example, the
  archery targets of the first main quest can be shot with melee or magic.
- Requirements on another style's level read your class level instead: Battle Loadout,
  thieving distractions, the four combat constellations in Astrology, item upgrades,
  Hunting by hand, and rings and amulets. For Ranged also the crossbows and throwing
  hammers that ask for Strength.
- Melee and Ranged: your Defence counts in full when you dodge spells. The game normally
  uses Magic for 70% of it.
- Class tiers at class level 20, 40, 60, 80 and 99, each with a title and bonuses:
    Melee:  +3% max hit, +4% minimum hit, -5% monster respawn time (per tier)
    Ranged: +8% chance to keep ammo, +4% accuracy, -2% attack time (per tier)
    Magic:  +8% chance to keep runes, +4% accuracy, +3% max hit (per tier)
  The class level is Ranged or Magic. For melee it is the higher of Attack and Strength.


EQUAL FOOTING (new in 0.2)
--------------------------
Whoever you fight, no class is worse off than the others:
- Weak spot: every monster has a weakest style (Wind Elemental dodges magic 7 times
  better than ranged, Talm dodges ranged 10 times better than melee). With a class,
  your style's stances meet the monster's lowest evasion of all nine stances. Worked
  out again for every monster that appears; area and darkness effects still count.
- Combat triangle off: no style is good or bad against another (see above). In 0.1
  the good matchups stayed, which favoured Magic against 78 monsters, Melee against 59
  and Ranged against 55.
- Elemental edge (Melee and Ranged): +15% max hit against the 135 monsters with an
  element (water, air, earth, fire), as a mage gets with the right spell.
- Melee shares its XP: whatever Attack gains, Strength gains too, and the other way
  round. Melee has two skills to train, Ranged and Magic have one: now all have one.
  XP from quests, lamps and other mods (a "set level" too) is shared as well.
- Guild and Astrology equivalents (Ranged and Magic): melee gets combat rewards from
  two guilds and two constellations. Your own guild ranks and constellation now give
  the same:
    Ranged/Magic guild rank 3: +6% ranged strength / +6% magic damage   (Strength rank 3)
    Ranged/Magic guild rank 4: -10% monster evasion, -10% monster armour (Attack and Strength rank 4)
    Ranged/Magic guild rank 5: +5% minimum hit, +15% power             (Attack and Strength rank 5)
    Ranged, Syllia major 3:    +4 Ranged levels                         (Terra major 4)
    Ranged, Syllia major 4 / Magic, Arachi major 4: +15% accuracy       (Iridan major 3)
  The window lists what you have earned.
- Class identity:
    Melee:  3% lifesteal with melee
    Ranged: +5% critical hit chance and +25% critical damage with ranged, +30 light
    Magic:  +3 ward and 25% chance of double ward damage with magic, +30 light
  (Ranged and Magic mostly use two-handed weapons, which leave no hand for a lantern.)
- Pets: Fen (+1% accuracy) and Bruno (+1% max hit) drop from Attack and Strength XP.
  For Ranged and Magic they drop from your class's XP.


CHAMPIONS AND UNIQUES (new in 0.3)
----------------------------------
Any of 179 monsters can come as a Champion (blue outline) or, rarely, as a Unique (orange,
its own name, story and picture). Under its health bar an elite shows CHAMPION or UNIQUE and
a badge for each affix; the bar itself gets a frame, turns red while it is enraged, flashes
green when a Vampiric elite heals, and shows its ward hits ("Warded 2/2").
The "Elites" button next to "View Stats" in a monster's list shows its three versions side by
side (normal, Champion, Unique: pictures, numbers, affixes, rewards, chances) and what each
drops, with the Unique's own item. Works with every class, and with Off. Switch it off on the
Elites page of the One Style window. Elites only appear while you play: never while the game
works out the time it was closed.

How often:
    Combat areas                 Champion 6%    Unique 0.8%
    Your slayer task monster     Champion 10%   Unique 1.5%
    Dungeon waves                Champion 8%    Unique 0.6%
    A dungeon's last boss        Champion 10%   Unique 4%
  After 100 kills of a monster without its Unique, the Unique chance rises by 0.25 points a
  kill and is certain at 200 (60 and 120 until that monster's first Unique, with 1.5 times
  the chance). Two elites of the same monster never come in a row (3 normal ones between).

How strong (the smaller the monster, the bigger the change):
    Champion: 1.35 to 3 times the hit points, +8 to 12% damage, 5% faster attacks,
              +8% accuracy, +10% protection, one random affix (two from 1200 hit points).
    Unique:   1.7 to 6 times the hit points, +14 to 22% damage, 8% faster attacks,
              +15% accuracy, +18% protection, its own two affixes, its own signature
              attack (usually 18% of its attacks; 10 points more when it is
              an attack it already has) and sometimes a passive of its own.
    A Colossal one has a quarter more hit points on top of that (so up to 3.75 and 7.5 times).
  Affixes: Hasted (faster), Colossal (+25% hit points, +10% damage, slower), Warded (2 to 6
  hits of ward), Armoured (-18% damage taken), Vampiric (heals 10% of the damage it deals,
  at most 2.5% of its hit points a hit, fading away between 25 and 45 seconds), Enraging
  (below 40% hit points: +20% damage, 10% faster).
  Safety: an elite's single hit is never more than 1.3 times the normal monster's biggest
  hit, its damage per second at most 1.35 (Champion) / 1.55 (Unique) times, and after 60
  seconds it tires: +5% damage taken every 5 seconds. While an elite lives you have no
  death penalty (a setting on the Elites page, on by default). A very small monster's elite
  has at most 3 times (Champion) / 6 times (Unique) its hit points; Colossal's quarter comes
  on top of that (3.75 / 7.5 times), and so does what Abyssal Beacon adds when you wear it.

Rewards:
    Champion: 2 more rolls of the monster's own loot (gold too), +25% combat XP while it
              lives, 3 to 11 Ember. Its first kill: +1 roll and +10 Ember.
    Unique:   5 more rolls, +50% combat XP, 16 to 48 Ember. Its first kill: +3 rolls,
              +40 Ember and the monster's trophy.
  The extra loot comes in at once (one message). Your loot and gold doubling (of Plenty, of
  Coin, the game's own) work on the extra rolls too. Chests and other boxes are not rolled
  again. Monsters without loot (most dungeon bosses) pay gold from their hit points and
  half again the Ember. A dungeon's own reward does not change.
  Champions and Uniques also drop rare items (see RARE ITEMS).
  Ember is the mod's own currency. Its first use: reroll a rare item's numbers, a small gamble.
  Select a Magic or Rare item in your bank (not one you wear) and press "Reroll". The affixes
  and their tiers stay, every number is rolled again inside its tier's range, and the item
  keeps the new numbers at once: there is no going back. A window shows the item before (left)
  and after (right), green where a number went up, red where it went down; "Reroll again" pays
  once more. At least one number always changes; an item whose numbers are all fixed at its
  tiers (common at item level 1) cannot be rerolled. It costs 2 x (1 + item level / 20, at
  most 5) Ember: 2 to 10. Unique items are not rerolled.

Trophies (179): the first Unique of a monster gives its trophy. Each trophy: +0.15% elite
chance and +0.1% Ember. 10 trophies: +2% elite gold. 25: +1% max hit points. 50: +1 Unique
loot roll. 100: mark one monster for +25% Unique chance. 179: "Master of the 179", +25% Ember.

Your Ember, trophies and counts are kept in "elites-progress.json" next to the mod, per
character, not in your save. Closing the game during an elite fight ends it: the monster
goes on as a normal one.


RARE ITEMS (new in 0.4)
----------------------
Monsters drop gear as rare items, straight at their rarity with random affixes, as in other
hack and slash games. They go into your bank and work with every class (and with Off).

Rarity, and its colour (on the name, and as a frame in the bank and on worn slots):
    Magic   (blue)         2 affixes: one prefix, one suffix
    Rare    (yellow)       3 affixes; 1 in 5 Rare items has 4
    Unique  (orange)       a fixed item of one monster's Unique (see below)
  At most 2 prefixes and 2 suffixes, never the same affix twice, one "on hit" poison / bleed /
  burn and one stun per item.

Who drops what:
    Any monster          2% a kill: 1% a Magic item, 1% a Rare item (1 in 5 with 4 affixes)
    Champion             1 item: Magic 60%, Rare 33%, Rare with 4 affixes 7%
    Unique               2 items: a Rare (1 in 5 with 4 affixes), then one as from a Champion,
                         plus the Unique's own item (see below)
    On your slayer task or as a dungeon's last boss: +5 item level; an elite there drops one
    more item.
  The base item is NOT taken from the monster's own drops: it is any piece of gear of a level
  from (item level - 20) to (item level + 5), every slot as likely, then every item of that
  slot (with a class chosen, weapons only of your class's style). So a Golbin can drop a rare
  Stone Sword, never a level 50 item. A piece of gear counts as the higher of its own
  requirement and the level of its easiest source (the monster that drops it, its crafting
  level...): Hypnotic Amulet asks for no level but comes from a level 75 monster, so it is
  level 75 here. A weapon of another style can drop when no class is chosen: wear it, or keep it.
  Every monster's drop list has a short "Possible rare drops" part: the Magic and Rare chance
  a kill, how often its Champion and Unique come and what they drop, the item level, and
  "Show possible items" for the gear list, slot by slot, with each item's share of the
  rare items dropped. Its "?" opens this explanation in the One Style window (Elites page,
  "How rare drops work").
  Most rare items come from Champions: about 6 kills in 100 are a Champion and it always
  drops one. (Cheat Menu's faster respawns mean more kills a minute, so more of everything.)
  The game has no picture yet for a few pieces of gear (Beastslayer armour, Storm Fang...):
  they never come as rare items.

Item level and tiers: the item level is the monster's combat level (+5, see above). It sets
the best tier an affix can roll:
    item level  1-14: T5    15-34: T4    35-59: T3    60-84: T2    85+: T1
  An affix rolls its best tier half the time, one lower 35%, two lower 15%. T1 is the highest.
  Every tier has its own range and the ranges never overlap.
  The item card. Every effect of a rare item is written once, in the part it comes from:
    Rare  ·  Scabjaw Beast Gauntlets  ·  item level 88
    AFFIXES  3 rolled · T1 best / T5 lowest
    ◆ 8% chance on hit to Burn                     T3
    ◆ +16% max hit                                 T1
    ◆ 5% chance on hit to Stun for a turn          T2
    BASE ITEM BONUS  (a box: what every Scabjaw Beast Gauntlets has)
      If First Attack of a Turn is a Hit: 5% chance to inflict Stun for 1 turn
  The AFFIXES are what makes the item rare. The BASE ITEM BONUS is the base item's own bonus:
  every item of that kind has it (most have none, and then one short line says so). The
  game's own green "Bonuses" list, which mixed both, is not shown for these items. A Unique
  shows its power, its fixed UNIQUE BONUSES and the base item's bonus the same way. Hold Alt
  (in the bank, a tooltip, or over a worn item) to see each affix's range next to its tier:
  +16% max hit   T1   [14-18]
  The same card in the item tooltip, the bank's Selected Item panel and a worn item's tooltip.
  With the Better Tooltips mod, its tooltips show the name in the rarity's colour and the same
  parts as text.
  Our other mods know rare items too: Bulk Sell filters them by rarity, slot, affix and level,
  Action Queue and Phone Screen can equip and sell them, Cheat Menu does not list them.

The 48 affixes (X = the rolled value; the best tier's range; where it can roll):
  Prefixes:
    Keen             +X% max hit  (T1: 14-18; weapon, gloves, ring, amulet, cape)
    Swift            -X% attack interval  (T1: 8-10; weapon, gloves, boots, legs, cape)
    True             +X% accuracy  (T1: 21-28; weapon, helmet, gloves, ring, cape, offhand)
    Deadly           +X% critical hit chance  (T1: 7-9; weapon, gloves, ring, amulet)
    Brutal           +X% critical hit damage  (T1: 35-46; weapon, amulet)
    Steady           +X% minimum hit (of the max hit)  (T1: 11-14; weapon, gloves, offhand, boots)
    Slayer's         +X% max hit against Champions and Uniques  (T1: 22-29; weapon, helmet, ring, body, cape)
    Leeching         X% lifesteal  (T1: 5-6; weapon, amulet, ring)
    Thorned          reflects X% of the damage taken  (T1: 13-17; body, offhand, legs)
    Serrated         X% chance on hit to cause Bleed  (T1: 11-14; weapon, gloves)
    Venomous         X% chance on hit to Poison  (T1: 11-14; weapon, gloves)
    Searing          X% chance on hit to Burn  (T1: 11-14; weapon, gloves)
    Stalwart         -X% damage taken  (T1: 5-6; body, offhand, helmet)
    Vicious          +X% max hit against a poisoned, bleeding or burning enemy  (T1: 24-32; gloves, legs, cape, weapon)
    Executing        +X% max hit against an enemy below 35% hit points  (T1: 26-35; weapon, boots, cape, ring)
    Shattering       X% chance on hit to Fracture the enemy's armour  (T1: 16-21; weapon, gloves)
    Raging           X% chance on hit to gain a stack of Rage  (T1: 28-35; weapon, gloves, ring)
    Reinforced       +X armour  (T1: 88-115; body, legs, helmet, boots, offhand)
    Warded           +X ward (hits it takes for you)  (T1: 3; offhand, body, helmet, amulet)
  Suffixes:
    of the Bear      +X% max hit points  (T1: 13-17; body, legs, helmet, amulet, offhand)
    of the Wall      +X% protection  (T1: 20-25; body, legs, helmet, boots, offhand)
    of the Fox       +X% evasion  (T1: 21-28; boots, cape, legs, gloves)
    of the Rock      X% chance to shrug off a stun  (T1: 22-29; helmet, boots)
    of Cleansing     X% chance to avoid Poison, Bleed and Burn  (T1: 22-29; cape, amulet, body, weapon)
    of Warding       X% chance to take no damage from a hit  (T1: 5-6; offhand, body)
    of Mending       +X% healing from food  (T1: 13-17; amulet, ring, cape)
    of Thrift        X% chance to keep the food you eat  (T1: 9-12; cape, gloves, amulet)
    of Runes         X% chance to keep runes  (T1: 30-40; weapon, offhand, cape)
    of the Quiver    X% chance to keep ammo  (T1: 23-29; weapon, cape)
    of Devotion      X% chance to keep prayer points  (T1: 14-17; helmet, amulet, weapon)
    of Plenty        X% chance to double combat loot  (T1: 5-6; ring, amulet, helmet)
    of Coin          X% chance to double gold from monsters  (T1: 48-63; ring, gloves)
    of Wisdom        +X% combat XP  (T1: 7-9; amulet, ring, helmet, weapon)
    of Haste         -X% time until the next monster  (T1: 18-23; boots, cape, weapon)
    of Embers        +X% Ember from elites  (T1: 23-29; ring, amulet, cape, weapon)
    of Calling       +X% chance of Champions and Uniques  (T1: 13-17; cape, helmet)
    of Mastery       +X% power with one style (yours)  (T1: 26-35; weapon, offhand, cape)
    of Momentum      -X% attack interval for 4 s after a kill  (T1: 12-15; weapon, boots, gloves)
    of Concussion    X% chance on hit to Stun for a turn  (T1: 6-7; weapon, gloves, offhand)
    of Frost         X% chance on hit to Freeze for a turn  (T1: 3.5-4.6; weapon, amulet)
    of Blinding      X% chance on hit to Blind for 2 turns  (T1: 18-23; gloves, offhand, helmet)
    of Withering     X% chance on hit to Wither the enemy  (T1: 11-14; weapon, amulet, ring)
    of Retaliation   X% chance when hit to make the attacker Bleed  (T1: 16-21; body, offhand, helmet)
    of the Bulwark   X% chance to start a fight Protected (the first hit is blocked)  (T1: 75-98; offhand, body)
    of the Hunt      +X% max hit on your slayer task  (T1: 11-14; helmet, amulet, cape, weapon)
    of Feasting      +X% auto-eat efficiency  (T1: 12-16; legs, amulet, cape)
    of Learning      +X% XP in every skill  (T1: 5-6; ring, amulet, cape)
    of Diligence     -X% action time in every skill  (T1: 5-6; ring, amulet, cape)
  Caps on what the affixes of your worn rare items give together (a unique item's power has its
  own limits, written on it): attack interval -40%, lifesteal 10%,
  "take no damage" 15%, damage taken -20%, loot doubling 25%, time to the next monster -50%,
  elite chance +50%, action time in every skill -40% (of Diligence), XP in every skill +50%
  (of Learning). What goes over a cap does nothing. Usurper's Circle's copy of a ring counts
  for these caps too.
  Attack interval floor: while you play, nothing makes you attack faster than once every
  0.6 s (affixes, Momentum, your class, unique powers and Rage together). Cheat Menu's "Attack
  speed" is not held back: when you turn it on, the floor steps aside. The floor is worked out
  while you play; the time the game was closed is worked out by the game with what the floor
  was when you last played.
  The same ring more than once: each game ring counts once, as in the game. Rare copies of one
  ring are each their own item and all their affixes count, but that ring's own stats and
  bonuses count once (six rare Silver Topaz Rings give their +3 attack bonuses once).
  of Momentum: its 4 seconds run only while you fight; waiting for the next monster does not
  use them up.
  A full bank: the One Style window (Elites page) has "Sell unlocked Magic items": it sells every
  Magic item in your bank that you have not locked, at its price. Rare and Unique items are never
  sold by it. Drops that did not fit wait and go in when there is room (the window counts them).

Unique items (58): each belongs to one monster's Unique and drops 15% of the times you kill
that Unique (no guarantee, also not on the first kill: they are worth farming). In a combat
area that is about 0.12% a kill of the monster (its Unique comes 0.8% of the time).
A Unique item has fixed stats and a special power, both written on it (hover it; the monster's
Elites window shows them too). The powers work while you play, like the elites: not while the
game works out the time it was closed. For that time a Unique still gives what is a plain stat
(a quiver that is never used up, lifesteal, accuracy, the damage Wreckshot has earned), and the
price a power takes is not taken either: Twin Volley's -40% damage, Deep Wound's -20%, Thorn
Dance's -10% and Hollow Harvest's -35% hit points are off for that time and back the moment
you play. The powers that protect you rest for that time as well (Oathlocked Plate's damage
cut, Veil-Antler Reprieve's rescue, Cavern Bastion's block, Tollkeeper Greaves' delay): a fight
you only survive because of one of them is not safe to leave running while the game is closed.
A few examples: Copper Oath's first hit of each fight
hits harder, Soulstring Quiver fires a free extra shot every 5th attack, Veil-Antler Reprieve saves
you from a fatal blow once in 3 minutes, Abyssal Beacon doubles the elites and gives them one more
affix and one more drop. The Elites page of the One Style window lists the uniques you wear and
what their powers have counted (Wreckshot's elite kills, Veil-Antler's wait, Goremantle's
Hunger...). Ammo uniques (the two quivers) are never used up.
    Copper Oath              helmet   level 1   Grik (Golbin)
    Thornshot                weapon   level 1   Snikt (Golbin Scout)
    Twin-Lurker Fangs        weapon   level 4   Skarn (Golbin Ambusher)
    Bell-Yoke Wand           weapon   level 9   Mora (Magic Cow)
    Stitched Furrows         legs     level 15  Hobb (Scarecrow)
    Resin Hoard Ring         ring     level 16  Ploxx (Blue Slime)
    Hollow Harvest Crown     helmet   level 21  Orren (Jack-O-Lantern)
    Comb Knuckles            gloves   level 23  Izzet (Bee)
    Hoarder's Prism          ring     level 28  Qivra (Rainbow Slime)
    Mire Throat              amulet   level 28  Eshra (Bog Bloomer)
    Ringbark Reprisal        offhand  level 36  Druven (Treant)
    Snarestep                boots    level 36  Velkith (Murkwood Spider)
    Ringleader's Tally       ring     level 39  Gorvuk (Ringleader)
    Thornstep Spinecoat      body     level 40  Tavio (Dancing Cactus)
    Thicket Trophies         cape     level 43  Vuldrek (Murkwood Trapper)
    Sovereign Diadem         helmet   level 43  Zelith (Royal Bee)
    Amber Reservoir          amulet   level 43  Ophis (Poisonous Serpent)
    Fourfold Writ            offhand  level 45  Ithrek (Magic Sentinel Minion)
    Root-Shear Claws         weapon   level 45  Ssilak (Mangrove Mantis)
    Twin-Eye Longwatch       weapon   level 45  Azkiel (Ranged Sentinel Minion)
    Veil-Antler Reprieve     amulet   level 50  Auvrel (Spirit Stag)
    Skytoll                  weapon   level 51  Skyrra (Wyvern)
    Talon of the Divide      ring     level 52  Kethra (Harpy)
    Divide-Fang Howler       weapon   level 55  Kaul (Dire Wolf)
    Plungeheart Tusk         weapon   level 60  Hrokk (Ice Yeti)
    Divide Sentinel's Aegis  offhand  level 60  Keldrun (Stone Elemental)
    Oathlocked Plate         body     level 62  Vauld (Phantasm Knight)
    Breaker Hooves           boots    level 62  Nacreon (Sea Pegasus)
    Soulstring Quiver        ammo     level 62  Qor (Phantasm Wraith)
    Brood-Sign Grips         gloves   level 62  Aulzha (Zuraxa the Brood Mother)
    Caravan Seals            legs     level 63  Zahir (Sand Nomad)
    Threevoice Cleaver       weapon   level 67  Nulgrath (Melee Amalgamation)
    Sunken Volley            weapon   level 67  Nerzul (Ranged Amalgamation)
    Conclave Spire           weapon   level 67  Zhulen (Magic Amalgamation)
    Dune-Vizier Wrappings    body     level 68  Neshu (Sand Mummy)
    Lantern Veil             amulet   level 70  Selyth (Glowing Jellyfish)
    Caravan Warcloak         cape     level 70  Zafir (Sand Nomad Warrior)
    Abyssal Beacon           helmet   level 73  Suvrill (Glowing Eel)
    Mirage Astrolabe         amulet   level 74  Zahim (Sand Nomad Mage)
    Dreamtide Orb            offhand  level 75  Iovra (Mesmeris the Slumbering Princess)
    Veil Rebuttal            weapon   level 79  Selkris (Dark Wizard)
    Veinbound Chalice        amulet   level 79  Sareth (Vampire)
    Barrens Hush             cape     level 79  Osvyr (Nightwing)
    Starbound Mantle         cape     level 80  Vaelis (Starblighted Mage)
    Oathbound Gauntlets      gloves   level 80  Deyrath (Starblighted Warrior)
    Ossuary Magazine         ammo     level 81  Sarn (Dreadspine)
    Pinnacle Diadem          helmet   level 83  Ydrak (Elder Wyvern)
    Wreckshot                weapon   level 90  Dask (Drowned Rifleman)
    Goremantle               body     level 90  Kharvul (Blood Fiend)
    Sanguine Liturgy         offhand  level 90  Yzren (Blood Summoner)
    Threefold Decree         ring     level 90  Urzath (King Black Dragon)
    Usurper's Circle         ring     level 91  Thulvek (Drowned Possessor)
    Hemal Palimpsest         offhand  level 91  Vezhar (Sanguis the Crimson Arcanist)
    Thunder Traces           gloves   level 92  Teyr (Storm Lynx)
    Tollkeeper Greaves       legs     level 92  Dhevran (Kelsarin Skirmisher)
    Gatebreaker              weapon   level 93  Gharom (Kelsarin Mauler)
    Scrapwind Treads         boots    level 93  Baskor (Kelsarin Wastelander)
    Cavern Bastion           offhand  level 95  Khazren (Dracolith Tyrant)
  The two quivers are never used up when you shoot (no rare item ever is).

Your rare items in the save:
- They are kept in your save, like any item: in the bank (same tab, same place, locked if
  you locked them), worn, and in every loadout. Save copies and cloud saves carry them.
- A rare item sells for 1.5 times (Magic), 2.5 times (Rare) or 4 times (Rare with 4 affixes)
  its base item's price.
- With the game's "automatic placeholders" on, a rare item you sell leaves no empty
  placeholder behind (one you wear keeps its place in the bank).
- When your bank is full, a drop waits and comes in as soon as there is room (the Elites page
  of the window shows how many wait).
- Rare items cannot be upgraded and are not in the item log. They do count for set bonuses: a
  rare Air Wizard Hat counts as an Air Wizard Hat.
- What a rare item asks for: its base item's skills, each at least 80% of its item level
  (a Rare ring from a level 100 monster asks for level 80). Rings, amulets and capes ask for
  Hitpoints instead of a combat style, so every class can wear them. A unique item asks for
  its own level: weapons and ammo in their style (melee: Attack), armour and offhands
  Defence (and the style when they have one), rings, amulets and capes Hitpoints.
- The item panel's "Found in" shows the combat icon: rare items only drop from monsters.
- The game's drop lists show them: the chest tab of the fight screen, the drop icons on each
  monster in the combat area list (the Unique's item with its real chance per kill), and
  "View all Stats & Drops" (with the button "Show possible items": the gear a rare item
  can be made from, slot by slot). Rare items are rolled on their own, next to the monster's
  loot; the monster's own loot does not change.
- The Elites page of the window lists your last drops.


SIX RING SLOTS
--------------
Every character gets six ring slots instead of one, whatever the class (also with Off):
- Ring Slot 2 is open. The game has it, locked behind Crafting level 150.
- Four more ring slots sit on the equipment grid: a new bottom row and the gap left of Legs.
Wear six different rings; the game counts each ring once. The rings stay on in every
loadout and after the game is closed: they are kept in your save like any worn item.
Ring Slot 2 is the game's own slot; Ring Slot 3 to 6 exist only with the mod (see UNINSTALL).


GOOD TO KNOW
------------
- Weapons and armour of other styles keep their own requirements: a class stays a class.
- The class is not written into your save. It lives in "onestyle.json" next to the mod.
  Remove the mod and the character is back to normal.
- Mastery Guild tasks that ask for another style are not changed yet. That is planned
  for a later version.
- Changing the class (or Off) takes every bonus of the old class away at once.
- Rare items are kept in your save. Ember, trophies and kill counts are kept in
  "elites-progress.json" next to the mod.


UNINSTALL
---------
Delete the folder "mods\OneStyle". That is all.
One Style keeps a list in your save of what the game cannot keep without it: your rare items
and the rings in Ring Slot 3 to 6. The next time you open each character without One Style,
Melvor Mod Loader 0.3.0 or newer puts their normal items in your bank (a Rare "Grim Fang"
Bronze Sword comes back as a Bronze Sword), once. Nothing is lost, nothing is doubled: when
your bank has no room for one of them, the loader keeps it and puts it in as soon as there is
room (a note on the screen and its log say how many wait). One Style's own copy of those items
(see GAME UPDATES AND YOUR RARE ITEMS) is not used after that: installing One Style again does
not bring them back a second time.
The loader does this only when the folder is gone. A One Style that is still in the mods
folder and did not start (after a game update) keeps its rare items rare.
(Ring Slot 2 is the game's own: its ring stays where it is.)

Removing Melvor Mod Loader too? Then nobody is left to give them back: first open the One
Style window on every character and press "Make all rare items normal" (Elites page) and
"Put rings 2-6 in the bank" (Class page). If the window says that some items wait for room in
your bank, free some bank slots and wait until they are in before you remove anything.

GAME UPDATES AND YOUR RARE ITEMS
--------------------------------
Melvor Idle 2 is in alpha and is updated often. An update can stop One Style, or the mod
loader, from working until they are updated too. The game cannot keep an item it does not
know: when it runs without One Style and saves, the rare items are no longer in that save.
They are not lost. This is what keeps them:

- A second copy outside the save. With every save One Style writes all rare items of the
  character (what they are, where each one is, how many) to a file of its own in
  "%AppData%\Games By Malcs\Melvor Idle 2\onestyle-items", next to the game's saves and
  not in the mod's folder. The next time One Style works and finds a save that has lost its
  rare items, it puts them back from that file: where they were, or into your bank when that
  place is taken (what finds no room waits and goes in later). A note on the screen says so.
- The game's own backups. The game copies a save every time you open the character (15 per
  character). Without a file of its own (another computer, a save from before this version)
  One Style looks there for the last save that still had the rare items.
- A copy of the save before the first play after a game update, in the same folder
  ("...before-game-update-....sav", the five newest per character stay).
- A mod that did not start is not a mod you removed. When OneStyle.dll is in the mods folder
  and did not start, the mod loader does not turn the rare items into normal ones (it does that
  only when the folder is gone, see UNINSTALL), and it says on the screen that the mod did not
  start.
- One part failing does not take the rest along: if a part of One Style runs into an error
  (as the item tooltips did with the game update of 25 November 2026), that part rests until
  the game is started again and everything else goes on.
- Nothing is thrown away on a guess: while a character's rare items could not all be put back
  after loading, a save drops none of their records.

After a game update: start the game as usual. If a note says that a mod did not start or was
switched off, look for a newer version of that mod. You can go on playing; the rare items come
back with the mod. Do not delete the "onestyle-items" folder.

If One Style cannot understand its own part of a save (a save written by a newer One Style), it
says so on the screen and in its window, and for that character its rare items rest (none drop,
none are changed), their data stays in the save exactly as it was (written back byte for byte),
and a copy of the save is kept in "mods\OneStyle\kept-saves". Update One Style and open the
character again.

What this cannot do: on a computer that never had One Style's file for a character, after the
game has made 15 backups of that character without the mod, there is nowhere left to look.


HELP AND UPDATES
----------------
Discord: https://discord.gg/q6aexZZSAf
Wiki:    https://falorfrozen-cmd.github.io/MelvorModLoader/
GitHub:  https://github.com/falorfrozen-cmd/MelvorModLoader
When something does not work, look at "MelvorModLoader.log" in the game folder
and include it when you ask for help.


CHANGES
-------
0.4.0
- The item card (25 November 2026): affixes once with their tiers (Alt: ranges), the base
  item's own bonus in a box of its own, a Unique's power and fixed bonuses apart; the game's
  merged "Bonuses" list, which showed the affixes a second time, is hidden for these items
- Rare items: monsters drop gear as Magic (blue), Rare (yellow) and Unique (orange) items
  with random affixes from 48, tiers by item level (T1 the best, ranges never overlap),
  Alt shows the tiers, coloured names and bank frames (see RARE ITEMS)
- 58 Unique items, each from one monster's Unique, with their stats and special powers
- Badges under an elite's health bar: CHAMPION / UNIQUE and its affixes, a framed bar,
  red while enraged, green when it heals, ward hits shown
- An "Elites" button in the monster list: the three versions of a monster and their drops
- Uninstall is automatic: the mod loader (0.3.0) gives rare items back as normal items and
  the rings of Ring Slot 3 to 6 back into the bank

0.3.0
- Champion and Unique monsters: 179 monsters with their own pictures, Unique names and
  stories, affixes, signature attacks, better loot, Ember and trophies (see above)
- The window has two pages: Class and Elites

0.2.0
- Equal footing: weak spot, combat triangle off, elemental edge for melee and ranged,
  melee XP sharing, guild and astrology equivalents, class identity, pets (see above)
- Six ring slots for every character, and a button that puts rings 2-6 in the bank
- The window scrolls when the screen is small

0.1.0
- First version: one class per character (immunity, triangle penalties, quest and
  level locks, magic evasion, class tiers)
