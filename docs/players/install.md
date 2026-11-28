---
title: Installing
parent: For players
nav_order: 1
---

# Installing the loader and mods
{: .no_toc }

Five minutes, once. After that, adding a mod is one unzip.
{: .fs-5 .fw-300 }

For the native Linux Steam build, see [Installing on Linux](install-linux).

1. TOC
{:toc}

---

## 1. Find the game folder

In Steam: right-click **Melvor Idle 2** in your library, then **Manage → Browse local files**. A folder opens
that contains `Melvor2.exe`. That is the game folder; everything below happens there.

The usual place is:

```
C:\Program Files (x86)\Steam\steamapps\common\Melvor Idle 2
```

## 2. Install Melvor Mod Loader

1. Download **Melvor Mod Loader** from [Nexus Mods](https://www.nexusmods.com/melvoridle2/mods/2) (Files tab,
   main file).
2. Close the game.
3. Extract the zip **into the game folder**. When you are done, the folder looks like this:

```
Melvor Idle 2\
  Melvor2.exe                                 the game
  data_GamesByMalcs.Melvor2_windows_x86_64\   the game
  override.cfg                                NEW: tells the game to start the loader
  data_MelvorModLoader_windows_x86_64\        NEW: the loader
  README.txt                                  NEW: the loader's short manual
```

{: .warning }
> `override.cfg` must sit **right next to `Melvor2.exe`**, not in a sub-folder. Some unzip tools create an extra
> folder named after the zip. If that happened, move the contents one level up.

## 3. Check that it works

Start the game from Steam as usual. For about 15 seconds a small green line appears in the top-left corner:

![The loader's line in the top-left corner of the game](../assets/img/loader-banner.png)

The number at the end counts the mods it found (the picture was taken with 7 installed). Right now you will
see `Melvor Mod Loader 0.1.3 - 0 mod`: the loader works and no mods are installed yet. That is the right
result at this point.

No green line? See [When something goes wrong](troubleshooting).

## 4. Install mods

Every mod is a zip that you extract into the **game folder** too, exactly like the loader. It creates (or
fills) a `mods` folder:

```
Melvor Idle 2\
  mods\
    CheatMenu\CheatMenu.dll
    ActionQueue\ActionQueue.dll
    ...
```

Restart the game. The green line now counts your mods, for example `7 mod`.

{: .note }
> Most mods are opened with a key while a character is open. Each mod's page and its `README.txt` (in its
> folder under `mods`) say which key. The mods on this wiki use **F5** to **F9**.

## Updating

- **A mod:** close the game, extract the new zip over the old one, start the game. Your mod settings live in
  the mod's own folder and are kept.
- **The loader:** the same. Extract the new zip over the old one.

## Removing

- **One mod:** delete its folder in `mods`, for example `mods\CheatMenu`.
- **Everything:** delete `override.cfg` and the folder `data_MelvorModLoader_windows_x86_64`. The game then
  starts exactly as before. The `mods` folder and `MelvorModLoader.log` can stay or go; without the loader the
  game ignores them.

{: .note }
> **Your saves are safe either way.** The loader writes nothing into your save files. A mod that changes your
> character (more gold, a higher level) changes it like playing would, and those changes stay when you remove
> the mod. Bonuses that a mod *adds* (faster actions, extra bank slots) disappear with the mod.

## Is it safe?

- The loader and the mods on this wiki contain **no `.exe` and no script**: only `.dll` files and text.
- They are **open source**. Anyone can read the code and build it themselves:
  [github.com/falorfrozen-cmd/MelvorModLoader](https://github.com/falorfrozen-cmd/MelvorModLoader).
- The loader folder includes Microsoft's .NET 10 runtime (MIT license), which the game engine needs in order
  to start the loader. Those files are signed by Microsoft.
- Nexus Mods scans every upload for viruses.
