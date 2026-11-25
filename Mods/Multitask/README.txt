Multitask 0.7.1  -  a mod for Melvor Idle 2
===========================================

Do more than one thing at a time. Nothing to set up: pick how many extra slots and play as usual.
When you start a new action, the one you were doing keeps running next to it.

   1 extra slot         your last two actions run together
   2 extra slots        your last three actions run together
   15 extra slots       all of them: every action you start keeps running
                        (the game has 16: 15 skills plus combat; Farming and
                        Ranching run on their own anyway)

Fish, then start cooking: both run. Go mining, then start a fight: both run.

This is a cheat: the game is balanced around one action at a time. Use it if that is the
game you want to play.

Tested with game version 0.11.6-alpha.1 and Melvor Mod Loader 0.2.0.


REQUIRES
--------
Melvor Mod Loader 0.1.3 or newer. Install the loader first.


INSTALL
-------
1. Open the game folder
   (Steam > right-click Melvor Idle 2 > Manage > Browse local files).
2. Extract this zip there. The mod ends up in the "mods" folder:
      mods\Multitask\Multitask.dll
3. Start the game.


HOW TO USE
----------
Open a character, press F5 (or use the top bar's Mods button) and set "Extra slots"
with + and - (or Off, 2, All). That is all.
  - Start a new action in the game: the one before it keeps going.
  - Start another one: with no room left, an action whose materials ran out
    drops out first; otherwise the oldest.
  - To drop an action, press the x next to it in the F5 window, or its own Stop
    button in the game.
  - Choose another rock, tree or recipe: that skill simply does the new thing.
    (A skill can only do one thing at a time.)
  - Fewer slots: what no longer fits is stopped, the oldest first.
  - Lock an action (the Lock button next to it in the F5 window): starting something new
    never pushes it out of its slot. New actions then swap only among the slots that are
    not locked, so you can switch quickly without stopping it by mistake. At most as many
    locks as extra slots: one slot always stays free for switching. Its own Stop button
    and the x still stop it.
The window lists what is running together right now, your latest first.


GOOD TO KNOW
------------
- An action that makes something (cooking, smithing...) waits when its materials run out
  and goes on when there are some again. It works in small piles, about ten at a time,
  so the game does not report "action stopped" for every single fish.
  Untick "Keep an action on the list when its materials run out" in the F5 window and
  such an action leaves the list instead, making room for the next one.
- Work done in steps is kept: a heist's stage (Thieving), a hunt's survey, tracks and
  tracking steps (Hunting), how far a potion has got (Herblore: preparing, heating,
  stirring, bottling) and the obstacle an Agility course has reached. The game throws those
  steps away whenever it stops an action, and it stops every action when you start another;
  Multitask puts them back when it starts the action again. Starting the same heist, hunt,
  potion or course yourself still begins it afresh.
- An Agility course with an entry fee (the Arena courses take Arena Entry Tickets) is not
  paid twice: the game takes the fee on every start, so when Multitask starts a run that
  was already paid for, the fee is not taken again. A run you started with your last
  tickets goes on too. The run also keeps its time so far, which counts for the course's
  timed rewards.
- A crafting action's "stop when" (the game's box for "make N", "reach level N"...) is kept:
  when you start something else and Multitask starts the crafting again, the goal is put
  back with the progress it had. Once the goal is reached the action stops for good.
- A fight is dropped if your character dies. Nothing walks back in.
- Offline progress: everything that was running when you closed the game goes on.
  An action that runs out of materials while the game is closed stays stopped until you
  are back.
- It never bypasses the game's requirements.
- With no extra slot (Off) the mod does nothing at all: the game plays as it always did.
- Works together with Action Queue 0.5.0 or newer: with slots on, the queue runs that
  many of its own steps at the same time (fish, cook the catch and mine, all at once).
  The queue's steps take the room; what you were doing before makes way for them.
  Switch "Side by side" off in the queue window to have the queue do one step at a time
  beside what you were doing instead.
  Locked actions keep their slots: the queue (Action Queue 0.11.0 or newer) runs its steps
  only in the slots that are not locked, one step at a time when every extra slot is locked.
  With Action Queue 0.6.0 or newer, a free slot is used for the queue's "when the queue
  waits or ends" action. Your own actions keep their slots.
- The game's bottom bar follows the page you have open: on the page of a skill that is
  running, it shows that skill (XP per hour, time to level, what it gathered). On any other
  page it shows your latest action. The full list is in the F5 window.
- The slots, that box and the list are remembered per character in "multitask.json".
- For mod authors: Multitask publishes "MultitaskOrder" on the scene root (skill ids of
  every action it keeps, running or waiting, latest first, "id|id"). A mod that starts
  an action only as a stand-in can name it in "MultitaskStandIn" (same "id|id" form);
  it is then the first to make room. "DefaultActionDriving" (true, or ids) works too.


UNINSTALL
---------
Switch the slots OFF, then delete the folder "mods\Multitask".
(If you delete it with a slot still on, the extra actions simply stop the next time you
start anything in the game.)


HELP AND UPDATES
----------------
Discord: https://discord.gg/q6aexZZSAf
Wiki:    https://falorfrozen-cmd.github.io/MelvorModLoader/  (guides for players and mod authors)
GitHub:  https://github.com/falorfrozen-cmd/MelvorModLoader  (source code; see LICENSE: free to use, not to re-upload)
When something does not work, look at "MelvorModLoader.log" in the game folder
(it is rewritten on every start) and include it when you ask for help.
