# Melvor Mod Loader

**Wiki: [falorfrozen-cmd.github.io/MelvorModLoader](https://falorfrozen-cmd.github.io/MelvorModLoader/)** - installing mods, writing your own, the full API, tested recipes.

An unofficial mod loader for **Melvor Idle 2** (Steam, Windows), plus the mods built on it.

The loader lets the game load C# mods from a `mods` folder. It changes none of the game's own files, ships no `.exe` and no script, and is removed by deleting two items.

Tested on game version 0.11.6-alpha.1. The game is in alpha: a game update can break mods until they are updated.

## Downloads

Ready-to-use zips are on Nexus Mods. Extract each into the game folder.

| Mod | What it does | Key |
| --- | --- | --- |
| [Melvor Mod Loader](https://www.nexusmods.com/melvoridle2/mods/2) | Required by everything below. Also hosts the Modder's Kit and the optional Debug Menu. | |
| [Cheat Menu](https://www.nexusmods.com/melvoridle2/mods/3) | 60+ settings and tools in one window: XP, speed, bank, skills, combat, loot filter, materials no recipe needs. | F8 |
| [Bulk Sell](https://www.nexusmods.com/melvoridle2/mods/4) | Bulk sell window and "Sell junk", "Bury all", "Open all" buttons in the bank. | F7 |
| [Action Queue](https://www.nexusmods.com/melvoridle2/mods/5) | Chain actions: "cut 1,000 logs, burn them, then go fishing". Its planner turns "I want 100 of this item" into the steps to get there. | F6 |
| [Save Backup](https://www.nexusmods.com/melvoridle2/mods/6) | Dated copies of your saves on every start. | |
| [Multitask](https://www.nexusmods.com/melvoridle2/mods/8) | Two slots keep extra actions running beside your main one: fish while you cook, mine while you fight. A cheat. | F5 |
| [Merchant](Mods/Merchant) (in development, source only) | A travelling merchant with a page of his own next to the Bank and the Shop: raw materials for sale, goods he buys above bank price, new every 4 hours. | F3 |

## What is in this repository

| Folder | Contents |
| --- | --- |
| `Loader` | The loader itself and the mod API (`IMod`, `ModContext`, `GameAccess`). |
| `Mods` | Source of the mods. Each folder has the `README.txt` that ships in its zip. |
| `ModdersKit` | [`MODDING.md`](ModdersKit/MODDING.md), the guide for mod authors, and a complete example mod. |
| `Dev` | The automated test rig: starts the game hidden on a test character and checks every feature. |
| `nexus` | The page texts used on Nexus Mods. |
| `docs` | The wiki (GitHub Pages). Its code examples are checked by `Dev/TestHarness/DocsRecipes.cs` and `DocsMod.cs`. |
| `README.txt` | The readme that ships inside the loader zip. |

## Writing your own mod

Start with the wiki's [Your first mod](https://falorfrozen-cmd.github.io/MelvorModLoader/modding/first-mod) (or [`ModdersKit/MODDING.md`](ModdersKit/MODDING.md) offline). The short version:

```csharp
using MelvorModLoader;

public class MyMod : IMod
{
    public void OnLoad(ModContext ctx) => ctx.Log("Hello from my mod");
}
```

Build it as a `net10.0` class library and put the DLL in `mods\MyMod\`.

## Building from source

You need the game installed and the .NET 10 SDK. The projects reference the game's DLLs by relative path, so clone into the game folder under the name `MelvorModLoader_src`:

```
<game folder>\Melvor2.exe
<game folder>\data_GamesByMalcs.Melvor2_windows_x86_64\
<game folder>\MelvorModLoader_src\        <- this repository
```

Then run `dotnet build -c Release` in `Loader` or in any mod folder. Mods need the loader installed first, because they reference `MelvorModLoader.dll` in the game folder.

On macOS there is nothing to extract: one script builds the loader and installs it into the game bundle. See the wiki's [Installing on macOS](docs/players/install-macos.md).

No game files are included here, and none may be added.

## How it works

`override.cfg` next to `Melvor2.exe` is a standard Godot feature. It tells the engine to load `MelvorModLoader.dll` first. The loader loads the game's own DLL untouched, starts the game normally, and then loads every DLL in `mods`.

## Help

- Discord: https://discord.gg/q6aexZZSAf
- When something does not work, attach `MelvorModLoader.log` from the game folder.

## License

The source is public so anyone can see what the files do. You may use the mods, change them for yourself and
learn from the code for your own mods; publishing them or a changed version of them (translations and repacks
included) needs permission. See [LICENSE](LICENSE). Versions published before 19 November 2026 stay under the
MIT license. Not affiliated with Games by Malcs or Jagex.
