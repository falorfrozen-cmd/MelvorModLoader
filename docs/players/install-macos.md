---
title: Installing on macOS
parent: For players
nav_order: 2
---

# Installing on macOS
{: .no_toc }

One script builds and installs everything. No download from Nexus Mods is needed.
{: .fs-5 .fw-300 }

1. TOC
{:toc}

---

Works with the Steam version of Melvor Idle 2 on Apple Silicon and Intel Macs,
tested on game version 0.11.6-alpha.1, macOS 26.

## 1. What you need

- The game installed (Steam, macOS), not running.
- The [.NET SDK 10](https://dotnet.microsoft.com/download) — with
  [Homebrew](https://brew.sh): `brew install --cask dotnet-sdk`.
- About two minutes.

## 2. Install

Clone this repository anywhere and run the install script:

```
git clone https://github.com/falorfrozen-cmd/MelvorModLoader
cd MelvorModLoader
./Dev/install-macos.sh
```

To also install mods, name them (each is a folder under `Mods`):

```
./Dev/install-macos.sh CheatMenu BulkSell ActionQueue
```

The script builds the loader for both Mac architectures, puts it into the game,
and keeps a pristine backup of the game as `Melvor2.app.orig-mvl` next to it on
the first run. When it is done, the game folder looks like this:

```
Melvor Idle 2/
  Melvor2.app.orig-mvl                      backup of the untouched game
  Melvor2.app/
    Contents/
      MacOS/
        Melvor2                             the game
        override.cfg                        NEW: starts the loader
      Resources/
        data_GamesByMalcs.Melvor2_*         the game (arm64 and x86_64)
        data_MelvorModLoader_macos_*        NEW: the loader
        mods\CheatMenu\CheatMenu.dll        NEW: your mods
```

## 3. Check that it works

Start the game **from Steam** as usual. For about 15 seconds a small green line
appears in the top-left corner, just like on Windows:

`Melvor Mod Loader 0.2.0 - 1 mod`

The loader's log is at
`Melvor2.app/Contents/Resources/MelvorModLoader.log`; it lists every mod it
loaded. If something goes wrong, that file is the one to attach when asking for
help.

## Updating

- **After a game update** (or Steam's "verify integrity of file"): Steam
  replaces the whole game bundle, which removes the loader. Run the script
  again — same command, same two minutes.
- **A mod or the loader:** run the script again.

## Removing

Delete, inside `Melvor2.app`: `Contents/MacOS/override.cfg`, the two
`Contents/Resources/data_MelvorModLoader_macos_*` folders, and
`Contents/Resources/mods`. Or put back the pristine `Melvor2.app.orig-mvl`.
[Your saves are safe either way.](install#is-it-safe)

## Things to know on macOS

- **Start the game from Steam.** Running the binary inside the bundle directly
  trips the game's Steam start-up check and it quits with its error screen.
- **`codesign --verify` reports the game as modified.** That is expected:
  macOS requires `override.cfg` to sit next to the executable, and forbids
  extra files there in a sealed bundle. Steam still launches the game fine.
  Do not re-sign the bundle.
- **"App Management" permission.** Editing an app bundle by hand (Finder or
  terminal) can ask for permission or fail. The script handles this on its own
  by refreshing the bundle; if you edited by hand, give your terminal
  *System Settings > Privacy & Security > App Management*.
- **Two architectures.** The macOS game ships one data folder per
  architecture, and its DLLs only load on their own architecture. The script
  installs the loader for both, so it works whether the game runs native
  (Apple Silicon) or through Rosetta (Intel).
