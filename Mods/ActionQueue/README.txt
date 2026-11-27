Action Queue 0.16.0  -  a mod for Melvor Idle 2
==============================================

Chains actions: do one thing until a goal is reached, then start the next. For example:
cut oak until you own 1,000 logs, burn them all, fish until level 30, then fight for 45 minutes.
Works with 16 skills including combat, dungeons and slayer tasks. Not a cheat.
It also plans for you ("I want 100 Steel Platebodies" -> the steps), keeps several queues
under a name, and turns a queue into a short code you can share.

Tested with game version 0.11.7 (the update of 25 November 2026) and Melvor Mod Loader 0.1.3.


REQUIRES
--------
Melvor Mod Loader 0.1.3 or newer. Install the loader first.


INSTALL
-------
1. Open the game folder
   (Steam > right-click Melvor Idle 2 > Manage > Browse local files).
2. Extract this zip there. The mod ends up in the "mods" folder:
      mods\ActionQueue\ActionQueue.dll
3. Start the game.


HOW TO USE
----------
Open a character and press F6 (or pick it from the Mods button in the top bar; the key can be changed there).
Drag the window by its title bar to move it; it opens where you left it. Double-click the title bar
to put it back in the middle. Drag its right edge to make it wider, or the grip in its bottom-right
corner to make it wider and taller (the step list gets the room). A double-click on that grip gives
it back its normal size. In the list's title row, the thin handles after "Do" and "Until" set those
columns' widths; a double-click on one puts the normal widths back. All of it is kept for next time.
  1. Pick a skill and what to do (or press "Use what I am doing now"). Click the list and
     type to search; arrows and Enter work too. The list is grouped like the skill's own
     page, in the game's order. Things you have not unlocked yet are dimmed with the level
     they need (you can still queue them for later); quest-only targets appear only while
     their quest step is active, as in the game.
  2. Pick when the step ends: when it stops by itself, when you own a number of an item,
     when you have made a number more of an item (whatever the bank already holds),
     when it has been done a number of times, when a skill reaches a level, after a
     number of minutes, or when only a number of an item are left (for example: fight
     until only 5 of your food are left).
     "Done this many times" counts each finished action, the way mastery does: every
     catch at a fishing spot (whatever fish it gives), every hit on a tree or rock,
     every finished bar or potion, every lap of an agility course, every monster killed.
     For that goal the food you are eating is filled in (or, with none chosen, the food
     your bank has most of) and the number starts at 5.
     For the item goal, the item is filled in with what the chosen action makes
     (Smithing: Gold Bar -> Gold Bar). Type in the search box to count another item.
     "It has used up this many of an item" counts only what the step's own skill used
     (the game's "Ingredients Spent"); the item is filled in with what the recipe uses.
     It also counts the food the step's food slot ate (Thieving, Agility, a fight) and, in a
     fight, the runes your spells used and the arrows, bolts or other ammo you shot. Food
     your preservation chance kept is not counted: only what really left the bank.
     Astrology has two more goals (see ASTROLOGY). Every skill also has "a mastery task
     is complete" (see MASTERY TASKS). A fight, Thieving and Agility also have "the food
     runs out" (see FOOD).
  2b. Tick "or" to give the step a second goal: a row "or until ..." appears. "+ or" at
     the end of that row adds another one, up to four goals on a step, and "-" takes the
     last one away again. The step is over when any one of its goals is reached, and its
     state says which one ended it. For example: thieve the Bandit Chief until a Gold Topaz
     Ring is in the bank, or until 780 Salmon are used up, or for 2 hours. Every goal keeps
     counting through pauses, "start over" and closing the game.
  3. "Add step". Repeat for the next steps, then "Start the queue".
  A step can also sell, equip, or go on with a saved queue: choose "Sell from the bank",
  "Equip" or "Go to a saved queue" at the end of the skill list (see SELLING, EQUIP and
  GO TO A SAVED QUEUE).
While the queue runs, one line at the top of the screen shows the current step.
The list's Time column shows how long a running step has been at it, and how long a finished
one took. Only the time a step really runs counts, not the time it waits for materials or a
free slot; after a pause it carries on from where it was. With "start over at the end" on, a
step that waits for its turn in the next round shows last round's time in brackets.
Steps can be moved up and down at any time, also while the queue runs: a running step
is not interrupted, and the new order decides what comes next.
To change a step, select it and press "Edit": the step is shown in the form above. Change
what you like and press "Save step N": it stays in its place ("Cancel" leaves it as it
was). A running step that is changed starts again with its new settings; one that is
already done this round stays done.
To jump, select a step and press "Run from here" (or right-click the step): the queue goes on
from that step. The steps before it are skipped this round (one that runs is stopped), and the
step and the ones after it start afresh, also when they were done. With lanes only that step's
lane jumps; the other lanes go on. A queue that is not running is started from that step.


THE GAME'S OWN BUTTONS
----------------------
On the Smithing, Crafting, Fletching, Runecrafting, Beastcrafting, Cooking and Herblore
pages of the game:
  - "+ Queue" (under the game's Create button) puts the selected recipe at the end of the
    queue, with the stop option you chose on the page: Recipe Creations, Products Owned,
    Products Gained, Ingredients Spent, Ingredients Remaining or Reach Level (no stop
    option: until it stops by itself). It works even while you lack the materials.
  - With "The game's Create button adds to the queue" ticked in the queue window, Create
    reads "Add to queue" and does the same. With nothing left in the queue, the recipe is
    started through the queue at once, so the next one you add lines up behind it.
    A paused queue stays paused (press Start). Off by default.
  - The game's Stop button on a step the queue is running ("Stop Cutting", "Stop",
    "Run"...) takes that step out of the queue, and the queue goes on with the next one.
    A saved queue keeps the step; only the list on screen changes.
  - A stop option that counts money (not an item) cannot be queued yet; the mod says so.


PLAN AN ITEM
------------
Press "Plan an item" at the top of the window.
  1. Choose "Make" (this many more than you have) or "Own in total" (until the bank
     holds this many), type the number and pick the item.
  2. The planner follows the recipes back to what has to be gathered (armour <- bars <- ores),
     takes off what your bank already holds, and lists the steps with the time they take.
  3. "Add these steps to the queue" puts them at the end of the queue. They are ordinary
     steps: reorder, remove or mix them with steps you add by hand.
"every material" (next to the Add button) is for a queue that starts over. Every material
gets its step, none is skipped because the bank already has some. Each material step tops
the bank up to what one round needs ("I own this many"), so a round only gathers what the
round before used and nothing piles up. With "Make" the item itself is "make this many
more", so every round makes it again. With "Own in total" it stays "until I own this many":
a round only makes what was used since, and the bank stays at the number you asked for.
"every material" is ticked by itself while "Start over at the end" is on for the plan's lane.
Several plans can be added one after another. A later plan knows what the earlier ones will
make and use up, so two plans never count on the same ore.
The planner only uses what you can do now: when a faster recipe needs something you have not
unlocked (Headless Bolts from Ironsteel Bars), it takes the one you can do (from Iron Bars).
Yellow lines are items to bring yourself: no skill makes them (monster drops, shop items),
or every recipe for them is still locked. An item you cannot make at all yet is not planned.
"then sell them" (next to the Add button) adds a last step that sells what the plan made;
with "Make", what you had before is kept.
Times are for normal game speed and are a forecast, not a promise.


SIDE BY SIDE (WITH THE MULTITASK MOD)
-------------------------------------
On its own the queue does one step after another. With the Multitask mod installed and
its slots switched on, the queue runs as many steps at once as Multitask has room for:
one extra slot = two steps, two = three, up to all 16 actions of the game.
  - The fish are cooked while more are being caught, and a third step can mine meanwhile.
  - A step that needs what an earlier step makes waits for it (in piles of about ten)
    instead of being skipped.
  - "Until I own N" then counts what was gathered, so a step that uses the item up as
    it comes does not make the gathering step run twice as long.
  - Two steps for the same skill still run one after the other.
  - The button "Side by side" at the bottom of the window switches this off again.
  - A step also waits when something you run yourself is making what it needs: mine the
    uncut gems by hand and the queue's crafting step waits for more instead of ending.


LANES (SEVERAL QUEUES AT ONCE, WITH THE MULTITASK MOD)
------------------------------------------------------
With Multitask slots switched on, a "Lane" list appears next to "Add step" (and next to the
planner's Add button). Lanes are queues of their own that run at the same time:
  - Every lane does its own steps one after another. For example lane 1 cuts logs and
    burns them, lane 2 fishes and cooks, lane 3 fights.
  - The list shows each lane under its own heading. "Lane 1 > 2" at the bottom moves the
    selected step into the next lane; Move up / Move down move it inside its lane.
  - A step that needs what another lane makes waits for it (cooking in lane 1 waits for
    the fishing in lane 2).
  - "Start over at the end" works per lane: a short lane starts again as soon as it is
    through and does not wait for a long one. Under the list, "Lanes that start over" has a
    box per lane: untick one and that lane runs once and stays finished while the others
    keep going round.
  - As many lanes run at once as Multitask has room for; two lanes that use the same skill
    take turns. With every step in lane 1 the queue works as one list, as before.
  - Up to 16 lanes: the list offers as many lanes as Multitask runs actions at once (at
    least five).
  - Lanes are kept in saved queues and share codes.


SELLING
-------
Choose "Sell from the bank" at the end of the skill list, pick the item and how many to keep
(0 sells all of them). For example: smith 1,000 Iron Bars, then sell all but 100.
  - The sale waits until the steps before it are finished (in lanes: the steps before it
    in its own lane), and nothing after it starts before it is done.
  - It sells through the game's own sale, at the game's price. A stack you have locked in
    the bank is never sold. With nothing to sell, the step simply ends.
  - It also sells while the game computes the time you were away.


EQUIP
-----
Choose "Equip" at the end of the skill list, then what to put on:
  - The game's loadouts are at the top of the list: the step switches to that loadout
    (its gear, food, prayers and spells), as the game's own loadout buttons do. A locked
    loadout is skipped.
  - Below them is every piece of gear, grouped by kind, with how many your bank holds.
    You can also pick something you do not have yet: arrows an earlier step makes, say.
  - A stack (arrows, bolts, javelins...) goes on whole, but for "keep": that many stay in
    the bank (0 puts all of them on). It is added to what you already wear. Any other
    piece of gear is simply put on, in a free slot of its kind if there is one.
  - Nothing to put on (none in the bank, or it is already worn) simply ends the step.
    Requirements you do not meet skip it with the reason; if the game does not put it on
    (a bank too full to take back what comes off), it is skipped as well and says so.
  - Like a sale, it waits for the steps before it, and nothing after it starts before it
    is done. Gear is only changed while the game is open: while the game works out the
    time you were away, the step waits ("waits for the game to be open").
  - At the end of the list is the food. Pick one and, next to it, the skill that eats it
    (a fight, Thieving, Agility): the step makes it that skill's food, as the skill's own food
    box does (see FOOD).
  - With the One Style mod: its Magic, Rare and Unique items work in Equip, Sell and item
    goals. The lists show the ones you own (in the bank or worn), in their rarity colour,
    with their affixes when you point at them. A step for one you have since sold is
    skipped with "this rare item is no longer owned".
Example (start over at the end ON): fight the Golbins (the step ends when the arrows run
out) -> Fletching: Bronze Arrows, make 500 more -> Equip: Bronze Arrows, keep 0 -> again.


FOOD
----
A fight, Thieving and Agility each eat from their own food box. Two things work with it:
  - Equip, then a food from the end of the list, then the skill that eats it: the step puts
    that food in the skill's food box. Only while the game is open, like other gear. No food
    of that kind in the bank skips the step; a food that is chosen already simply ends it.
  - "the food runs out" as a goal (or as the second goal, "or until"): the step ends when the
    food its skill has chosen is gone from the bank. It follows whatever food the box holds
    when the step runs, so an Equip step before it can change the food and the goal still
    fits. With no food chosen, or none left, the step ends at once and says so.
Example (start over at the end ON): Cooking: Shrimp, make 200 more -> Equip: Shrimp for
Thieving -> Thieving: Bandit Chief until the food runs out -> again.


GO TO A SAVED QUEUE
-------------------
Choose "Go to a saved queue" at the end of the skill list and pick one of your saved
queues. When every step before it is over (with lanes: every lane), the queue goes on
with that saved queue, from its first step, as if you had loaded it. Put it last to chain
queues: "Smithing day" -> "Fishing evening" -> back to "Smithing day".
  - The steps on screen are kept the way loading always keeps them (a changed or unsaved
    queue as "Unsaved queue (auto)"). In lanes it waits until every other lane is through;
    steps after it in its own lane are not done (the queue goes on with the saved queue).
  - A name that no longer exists skips the step.
  - To make it stop at a point (train Magic until level 30, then something else), give the
    saved queue its own stop: load it, choose "Stop the whole queue when", and save it again
    under the same name. The go-to step then says when that queue stops. What comes after it
    is the character's "When the queue ends, go on with" (it is not saved with a queue).
  - Saved queues that only send each other round stop: when the chain comes back to a
    queue it already left without anything being done since, the queue ends and says so.
    Any number of queues that do nothing for now (things you have not unlocked yet) can be
    passed on the way to one that works. Putting on gear, a loadout or food does not count as
    doing something here (it can be undone and done again for ever).
  - It also works while the game is closed. Share codes keep it (as a name: the other
    player needs a saved queue with that name).


ASTROLOGY
---------
With Astrology chosen, the goal list has two more entries:
  - "node unlocks of this constellation are earned" [number] [minor/major]: minor counts
    the node unlocks the constellation's Knowledge has earned (the ones you have already
    clicked count too). Major counts the major unlocks those minor ones are worth, one
    for every few, as the game gives them; you do not have to click the minor nodes for it.
    Already earned: the step ends at once ("nothing to do").
  - "an astrology event has been seen this many times" [number] [event]: the list holds
    the events that constellation can show (also the ones inside its event tables). Only
    the events seen after the step starts count. Tip: while one bonus event of the game is
    waiting or active, the game does not roll another bonus event.
  Both work with "or" too: "until 10 minor node unlocks are earned, or for 30 minutes".


MASTERY TASKS
-------------
"a mastery task is complete" ends a step once one of the Mastery Guild's tasks is done, e.g.
fish at Deep Blue until the task "Catch Deep Blue 8,000 times" is complete.
  - The list holds the tasks of the step's skill. For a fight it holds the tasks of Attack,
    Strength, Defence, Hitpoints, Ranged, Magic, Prayer and Slayer.
  - The tasks for the chosen target come first, from the easiest, and the next one you have
    not done yet is picked for you. The others follow by tier. Click the list and type to
    search.
  - Next to each task you see how far it has come ("1,234 / 2,500", or a percent).
  - Tasks you have done are dimmed. You can still pick one (for a share code or another
    character), but a step waiting for it ends at once.
  - A task's extra conditions follow in brackets ("using Melee", "enemies of combat level
    1-9"). The step only ends when the game marks the task complete, so let the step do
    what the task asks.
  - It also works as the second goal ("or"), and in "Stop the whole queue when", where the
    list holds every skill's tasks with the skills of the queue on top.
  - The queue only reads the task. The game does the counting, also while it is closed.
  - A task your game does not have (a code from another version) is skipped with a note.


WHEN THE QUEUE WAITS OR ENDS
----------------------------
At the bottom of the window: "When the queue waits or ends, do" [skill] [action].
Pick something there (or press "Use what I am doing now") and your character never stands
still:
  - While every step that is left is waiting (for materials, for its skill to be free),
    this action runs in the free slot. When a step can go on, it makes room again.
  - When the queue has finished, this action starts and simply keeps going.
  - It only ever takes a free slot. Your own actions are never stopped for it. Without the
    Multitask mod it only runs after the queue has finished.
  - It is not started when it would use up what a waiting step needs.
  - Press Stop on it in the game and the queue leaves it alone until you start the queue
    again. If it is a fight and your character dies, it does not walk back in.
  - "Also when the queue is not running" (a check box under it, off by default): the
    action then also runs while the queue is paused, finished or not used at all,
    whenever your character does nothing. Press Stop on it (or on anything else) and it
    waits until you start something yourself; when that ends, it comes back. It waits a
    few seconds after a character is opened.
  - "Nothing" switches it off. The choices are kept per character.
Under it, "When the queue ends, go on with" [saved queue] starts one of your saved queues, from
its first step, once this queue has ended: its steps are done ("Start over at the end" off), or
its own stop was reached (see below). Pausing, emptying the queue or stopping its last step
yourself does not count. A queue never starts itself again this way, so when the saved queue
you picked ends, the queue finishes and the idle action takes over. "nothing" switches it off;
a saved queue you have since deleted is shown as "(no longer saved)" and the queue simply ends.
It is a setting of the character, like the idle action, not part of a saved queue.


STOP THE WHOLE QUEUE WHEN
-------------------------
Under the buttons: "Stop the whole queue when" [what] [number] [item or skill] [when]. Once
that is reached, every step stops and the queue ends, also with "Start over at the end" on and
in lanes, and also while the game is closed. The action under "When the queue waits or ends"
then takes over (or the saved queue under "When the queue ends, go on with").
  - [when] is "at once" (the queue stops the moment it is true, in the middle of a step) or
    "when a round is over": the queue first finishes the round it is in, so a sale or an equip
    at the end of the round still happens. In lanes, a lane that is through does not start
    over while it is true, and the queue ends once every lane is through. Started while it is
    true already, the queue does one round and then stops.
  - "I own this many of an item": e.g. thieve, fish and cook round after round until 1
    Bandit Gloves is in the bank, then stop.
  - "a skill reaches this level", "only this many of an item are left", "a mastery task is
    complete" (see MASTERY TASKS).
  - "never: the steps decide" switches it off (the queue ends when its steps are done, as
    before).
  - Pick the kind first, then the item or skill. Until you pick one the queue has no stop of
    its own, so nothing left over from before can stop it by surprise.
  - Already reached when you set it or press Start: the window says so, and the queue stops
    or does not start ("at once"), or does this one round ("when a round is over").
  - A sale or an equip step that brings you there ends the queue at once: the next step does
    not start.
  - It also works while the game is closed, and since 0.16.0 it ends a running fight then
    too.
  - It looks twice a second, so a very fast action can make one or two more.
  - It is kept with the queue, in saved queues and in share codes. "Remove all" takes it
    away too.


SAVED QUEUES AND SHARE CODES
----------------------------
Press "Saved queues" at the top of the window.
  - Type a name and press Save: the queue below is kept under that name.
  - Select a saved queue and press Load (or double-click it) to put it in place of the
    queue below. The list at the very top of the window does the same in one click.
  - A queue that is running keeps running when you switch: it goes on from the first step
    of the new one.
  - If the queue on screen was never saved, or was changed, it is kept as
    "Unsaved queue (auto)" when you load another one. Nothing is lost by switching.
  - "Copy share code" turns a queue into a short text (AQ1:... or, when it uses an "or"
    goal, an astrology goal, an Equip or a "Go to a saved queue" step, AQ2:...; with a
    "Stop the whole queue when", AQ3:...; with food for a skill, "the food runs out" or a stop
    "when a round is over", AQ4:...; with a step of three or four goals, AQ5:...). Paste it in a
    chat. A version older than 0.10 cannot read an AQ2 code, one older than 0.12 an AQ3 code,
    one older than 0.13 an AQ4 code and one older than 0.15 an AQ5 code: it
    refuses it instead of running the queue without those parts. A code made by a newer version
    than yours says so: update the mod to read it.
  - To use somebody's code: paste it into the box and press Import, then Load it.
    A code only holds names and numbers. Steps for things your game does not have are
    skipped, and so are steps you have not unlocked yet.


LANGUAGES
---------
The window and the queue's messages can be in another language. A language is a file in the
mod's "lang" folder, for example mods\ActionQueue\lang\ru.json for Russian. When there is one,
a list at the top right of the window picks it, and the choice is kept for next time. Names of
items, skills and monsters, and the game's own requirement texts, stay as the game writes them
(the game itself is in English for now).

To translate it yourself, copy lang\en.json, name the copy after the language (de.json, ru.json)
and change the texts; the first lines of en.json say how. Keep every {name} in curly brackets as
it is, though you can move it in the sentence. A text that has "one" and "other" depends on a
number: give the forms your language uses (Russian has one, few, many and other). A text you
leave out, or get wrong, shows in English, so a new version of the mod never breaks an older
translation. After you edit the file, pick English and then your language again in the list
to see the change. If you send me the file, it can ship with the mod, with your name in the thanks.


GOOD TO KNOW
------------
- The queue also runs while the game is closed: when you come back, the game works out the
  time you were away and the queue moves on through its steps in that time, as if you had
  watched. After the game is closed and opened again, a running queue carries on where it
  was at the game's last save. Since 0.16.0 that includes fights: a fight step starts, counts
  its kills and ends at its goal while the game is closed, like any other step, and the next
  step takes over. Until then fights waited for the game to be open, because the game's
  music broke when a fight started or ended in that time (its "now playing" notice stopped
  showing until the game was restarted). The queue now holds the fight's music while the
  game is closed and sets it once when you come back. If a game update changes that part of
  the game, fights wait for the game to be open again, as before.
  Good to know about that time:
    - Gear and food are still only changed while the game is open: an equip step waits for
      that, and the steps after it wait with it.
    - If your character dies in a fight while the game is closed, the queue pauses, as it does
      while the game is open.
    - A paused fight (a boss that waits for you, or the game's pause button) stays paused
      while the game is closed. The step says "the fight is paused"; a kill goal waits for
      you, a minutes goal keeps counting, as with the game open.
    - The game itself works out at most 24 hours (more with bonuses that raise the limit),
      and only after a normal close (the game saves then).
    - If the steps were changed after the game last saved, the queue does not carry on
      after a restart: it waits for Start. The reason is written in queue-log.txt.
    - With "start over at the end" ON, prefer "make this many more" / "this many times" /
      "minutes" for steps that should take turns. "I own this many" is reached once, and a
      round in which every step was already reached ends the queue. (A plan made with
      "every material" works round after round: see PLAN AN ITEM.)
- It never bypasses the game's requirements. A step you are not allowed to do yet, or have
  no materials for, is marked "skipped" and the next one starts.
- If you start something else by hand, the queue pauses instead of fighting you. With the
  Multitask mod and a free slot it does not: a fight (or anything in another skill) you start by
  hand runs beside the queue, also again after your character died, and the queue goes on.
  If you press the game's Stop on a step, the queue moves on without that step.
- After a pause, "Start the queue" carries on: finished steps stay finished, wherever
  they have been moved.
- "Remove all" (or removing the step that is running) also stops what that step had started.
  "Pause" leaves your character doing what it was doing.
- If your character dies, the queue pauses and does not walk back into the fight.
- A fight step ends when you can no longer attack: your bow or crossbow has no ammo of its
  kind equipped, or you cast spells and the bank lacks the runes (or no spell is picked).
  The game itself would keep the fight going while the monster hits you. The step reads
  "done: out of ammo" (or "out of runes") and the queue goes on with the next step.
  A fight you cannot attack in is not started at all ("skipped: out of ammo"), so with
  "start over at the end" on it is not started again and again; and a fight as the idle
  action is stopped (or not started) the same way. When the game itself ends such a fight,
  the step says why too.
- With "start over at the end" on, a step that waits for its turn in the next round shows how
  it ended the last time ("last time: done: out of runes"), until it starts again.
- With the Multitask mod and a free slot, what you keep running yourself (a fight next to
  the queue's smithing) goes on while the queue moves from one step to the next; it is no
  longer stopped and started again from the beginning.
- Sacrificial Anvil: when the recipe you pick has the game's anvil switch (and you have it
  unlocked), a "Sacrificial Anvil" box shows next to it. Ticked, that step switches the
  anvil on when it starts; unticked, it switches it off. So one step can smith with the
  anvil and the next without. Steps show "(Sacrificial Anvil)" in their name.
- Hunting: the step uses the trap chosen on the Hunting page; when none is chosen, or it is
  used up, it takes a trap the bank holds by itself. A plan counts the traps a hunt uses
  up and adds a step that makes them first (Fur Camo Hat: make the traps, hunt the fur,
  craft the hat).
- Agility needs a path chosen on the skill's own page first.
- The queue is remembered per character in "queue.json" next to the mod.
- With side by side switched off, the Multitask mod keeps what you were doing before
  running beside the queue instead.


THANKS
------
"Run from here", food for a skill, "the food runs out", the queue to go on with, the stop when a
round is over, the bigger window and the column widths came from madmousepad (lmadrabbitl on
GitHub), who made them for their own game and shared them. Thank you!


UNINSTALL
---------
Delete the folder "mods\ActionQueue".


HELP AND UPDATES
----------------
Discord: https://discord.gg/q6aexZZSAf
Wiki:    https://falorfrozen-cmd.github.io/MelvorModLoader/  (guides for players and mod authors)
GitHub:  https://github.com/falorfrozen-cmd/MelvorModLoader  (source code; see LICENSE: free to use, not to re-upload)
When something does not work, look at "MelvorModLoader.log" in the game folder
(it is rewritten on every start) and include it when you ask for help.
