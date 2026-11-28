---
title: Installing on Linux
parent: For players
nav_order: 1.1
---

# Installing the loader on Linux

These steps are for the native Linux x86_64 Steam build. They were tested with Melvor Idle 2 `0.11.6-alpha.1` and Melvor Mod Loader `0.2.0`. You do not need Proton or the .NET SDK to use the Linux ZIP.

## Install

1. Once a Linux ZIP is published, download it from the [Melvor Mod Loader files page](https://www.nexusmods.com/melvoridle2/mods/2?tab=files). The Windows ZIP is for the Windows build of the game.
2. Close the game. In Steam, right-click **Melvor Idle 2** and choose **Manage → Browse local files**. The folder should contain `MelvorIdle2.x86_64`.
3. Check whether the game folder already has an `override.cfg` before you extract anything:
   - If it does not, extract the ZIP into the game folder. `override.cfg` and `data_MelvorModLoader_linuxbsd_x86_64/` should sit next to `MelvorIdle2.x86_64`.
   - If it does, back it up and extract the ZIP into a separate folder. Copy only `data_MelvorModLoader_linuxbsd_x86_64/` into the game folder. Set `project/assembly_name="MelvorModLoader"` in the existing file's `[dotnet]` section, adding the section if needed.

## Check the loader

Launch the game through Steam and check `MelvorModLoader.log` in the game folder for `Game started`. If you have no mods yet, the log may say there is no `mods` folder. Create it when you install your first mod, for example `mods/ExampleMod/ExampleMod.dll`. Use the same capitalization as the mod's DLL and dependency files; Linux paths are case sensitive on most filesystems.

## Remove the loader

Close the game. Remove the `override.cfg` you created, or restore your original file if you had one, then remove `data_MelvorModLoader_linuxbsd_x86_64/`. You can leave `mods/` and `MelvorModLoader.log` in place.
