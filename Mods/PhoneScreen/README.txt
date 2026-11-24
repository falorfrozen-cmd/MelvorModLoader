PHONE SCREEN 0.1.1  -  for Melvor Idle 2 (requires Melvor Mod Loader)
=================================================================

Run your character from your phone's browser while the game runs on your PC: see what
is going on, fix what went wrong, start something new, plan the queue. At home nothing
to install on the phone; away from home (mobile data, work) with the free Tailscale app.

HOW
1. In the game press F2 (or the "Phone" button at the end of the top bar). With
   Melvor Mod Loader 0.2.0 or later you can change the key in the Mods menu (F1).
2. Pick your network (your home Wi-Fi, or Tailscale to use it away from home: see
   below) and press Start.
3. Scan the QR code with the phone's camera (or open the address shown and type
   the code). The page pairs itself. One code pairs one phone; a new code appears
   for the next phone.
4. To change things from the phone, tick "Allow phone controls" in the window.
   Without it the phone only looks ("View only").

THE PAGE  (Now · Skills · Combat · Bank · More)
Now      what needs you first (food low or gone, nothing running, bank full, an
         adventurer stopped, the queue waiting, crops ready), each with the button
         that fixes it; then everything running, one card per Multitask slot:
         combat (your HP with the auto-eat line, the monster, food, loadout,
         potion, prayer, spell, Slayer task; Eat once, Food, Gear, New target, Stop)
         and skills (progress, level, Change, Stop); Farming, the queue,
         adventurers and the bank at a glance.
Skills   every skill with its level. Tap one: everything you can start, and what
         is still missing for the rest. "Recent" starts the last ones in one tap.
         Farming: every plot (ready / growing with time left / empty), Harvest
         all, plant a seed (with compost), buy new plots.
Combat   Fight: every area and monster; with the Combat Advisor mod each shows
         SAFE / RISKY / DEADLY with kills, XP and food per hour, and a risky
         fight asks first. Gear: your loadouts and every equipment slot; tap a
         slot to take an item off or put on one from the bank. Buffs: potions
         (combat and every skill), prayers, the attack spell and imbuement.
         Slayer: every master's task with kills left; get, swap, extend, fight
         it, Auto Slayer.
Bank     gold and space, every item with search and selling (it asks first),
         recent gains, food, and with the Bulk Sell mod: Sell junk, Bury all,
         Open all. Shop: every category, what you can buy now, how many.
More     Action Queue (pause, resume, add, move, remove, clear, load and save
         queues), Adventurers (food, target, recall, resume, put their loot in the
         bank), Multitask (how many actions at once), Cheat Menu (opens closed:
         nothing changes until you switch something).
Pictures of skills, items, monsters, prayers and spells come from the game itself.
With One Style, rare items show in their colour with item level and affixes (Gear, Bank).
The bell: alerts, with sound and vibration while the page is open.

AWAY FROM HOME (mobile data, work Wi-Fi): WITH TAILSCALE
A phone away from home cannot reach your PC by itself (your router lets nothing in).
Tailscale, a free app, links your own devices to each other safely from anywhere: no
router setup, nobody else's server in between for the game's data, nothing to pay.
1. Install Tailscale on this PC and on the phone (tailscale.com, the App Store or
   Google Play) and sign in with the same account on both (a Google, Microsoft or
   Apple account will do). The free personal plan is enough.
2. In the game's Phone window pick "Tailscale: works anywhere" as the network (it is
   listed first once Tailscale runs) and press Start.
3. Scan the QR code. That's it: the page now opens at home and away, as long as
   Tailscale is on on the PC and on the phone. Add the page to the phone's home screen.
4. Tick "Turn on by itself when the game starts": then the phone screen is on as soon
   as the game is, and paired phones stay paired (no new QR code after a restart).
   "Keep phone controls on when the game restarts" also keeps the controls switch.
- The PC must stay on and awake with the game running.
- The page stays blank or never loads: the phone is not in the PC's Tailscale network.
  In the phone's Tailscale app the device list must show your PC too; if it shows only
  the phone, it is signed in with another account: log out, sign in with the PC's.
  (A "DNS unavailable" warning in the app does not matter here.)
- Still nothing: check that Tailscale is on on both, and allow Melvor Idle 2 in
  Windows Firewall on private and public networks.
- Another VPN app on the phone or the PC can get in Tailscale's way.
(For the technically minded: the source on GitHub has an end-to-end encrypted relay,
Mods\PhoneScreen\Relay\worker.js, you can run on your own Cloudflare account and name in
mods\PhoneScreen\phone.json as "RelayUrl"; the window then offers it. Not needed with
Tailscale.)

SAFETY
- Off until you press Start, and off again when the game restarts (unless "Turn on by
  itself when the game starts" is ticked).
- Only a phone that paired with the code can see anything. "Sign out all phones"
  signs every phone out.
- Controls are off until you allow them on the PC. The game checks every command
  again; a command for another character, or one older than 10 seconds, is
  refused. Selling and risky fights ask first. Nothing can touch your save file.
- A plain web page on your own network: use it at home, not on public Wi-Fi (through
  Tailscale it is encrypted end to end, so that is fine anywhere).
- Nothing is written into your save. The mod keeps the network you chose and, with
  "Turn on by itself", which phones are paired (as hashes, not their keys) in
  mods\PhoneScreen\phone.json.

IF THE PHONE CANNOT OPEN THE PAGE  (at home, without Tailscale)
- Phone and PC must be on the same Wi-Fi (not a guest network, no VPN on either).
- The first time you press Start, Windows may ask whether Melvor Idle 2 may use the
  network: allow it on private networks. If you said no, allow "Melvor2" in
  Windows Security > Firewall > Allow an app through firewall.
- Your PC must not be asleep, and the game must be running.

INSTALL
Unzip into the game folder (where Melvor2.exe is). The mod ends up in
mods\PhoneScreen. Remove that folder to uninstall.

UPDATING
Unzip the new version over the old one, restart the game, then refresh the page on
the phone: the page comes from the game, so the phone gets the new one at once.
Paired phones stay paired (with "Turn on by itself" ticked).
