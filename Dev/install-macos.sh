#!/usr/bin/env bash
#
# Builds Melvor Mod Loader (and any mods named on the command line) and installs
# them into the Steam macOS build of Melvor Idle 2.
#
#   ./Dev/install-macos.sh                loader only
#   ./Dev/install-macos.sh CheatMenu      loader + Cheat Menu
#   ./Dev/install-macos.sh CheatMenu BulkSell ActionQueue ...
#
# Nothing outside the game folder is touched, and the game's own files are left
# as they are. Run the script again after a game update (Steam replaces the
# whole bundle) or to add more mods. The game must be closed.

set -eu

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
GAME_ROOT="${MELVOR_GAME_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/Melvor Idle 2}"
APP="$GAME_ROOT/Melvor2.app"
RES="$APP/Contents/Resources"
MACOS="$APP/Contents/MacOS"

command -v dotnet >/dev/null 2>&1 || {
    echo "dotnet not found. Install the .NET SDK 10 first: https://dotnet.microsoft.com/download" >&2
    exit 1
}
[ -d "$RES/data_GamesByMalcs.Melvor2_macos_arm64" ] || [ -d "$RES/data_GamesByMalcs.Melvor2_macos_x86_64" ] || {
    echo "Game not found: $APP" >&2
    echo "Point MELVOR_GAME_ROOT at your 'Melvor Idle 2' folder." >&2
    exit 1
}
pgrep -x Melvor2 >/dev/null 2>&1 && {
    echo "Close the game first; its files are replaced by the install." >&2
    exit 1
}

# The game's DLLs are used only as compile-time references, and reference
# reading is architecture-neutral, so either data folder will do.
REFS="$RES/data_GamesByMalcs.Melvor2_macos_arm64"
[ -d "$REFS" ] || REFS="$RES/data_GamesByMalcs.Melvor2_macos_x86_64"

echo "== Building the loader (arm64 and x86_64) =="
# NoWarn=8012: the game's DLLs are ready-to-run images for one architecture;
# as compile-time references that is fine.
dotnet publish "$ROOT/Loader/MelvorModLoader.csproj" -c Release -r osx-arm64 \
    -p:GameData="$REFS" -p:NoWarn=8012 -o "$ROOT/out/loader-osx-arm64"
dotnet publish "$ROOT/Loader/MelvorModLoader.csproj" -c Release -r osx-x64 \
    -p:GameData="$REFS" -p:NoWarn=8012 -o "$ROOT/out/loader-osx-x64"
LOADER_DIR="$ROOT/out/loader-osx-arm64"

for mod in "$@"; do
    echo "== Building mod: $mod =="
    dotnet build "$ROOT/Mods/$mod/$mod.csproj" -c Release \
        -p:GameData="$REFS" -p:LoaderDir="$LOADER_DIR" -p:NoWarn=8012
done

# Keep a pristine copy of the bundle next to the game on the very first install.
if [ ! -e "$MACOS/override.cfg" ] && [ ! -d "$GAME_ROOT/Melvor2.app.orig-mvl" ]; then
    echo "== Keeping a pristine backup: Melvor2.app.orig-mvl =="
    cp -cR "$APP" "$GAME_ROOT/Melvor2.app.orig-mvl" 2>/dev/null || cp -R "$APP" "$GAME_ROOT/Melvor2.app.orig-mvl"
fi

# macOS "App Management" protection blocks writes into an app bundle that has
# been launched before. Renaming the bundle aside and cloning it back gives a
# writable copy of it (clonefile: instant, shares its disk space).
if ! touch "$RES/.mml-writable" 2>/dev/null; then
    echo "== Refreshing the app bundle (App Management workaround) =="
    mv "$APP" "$APP.mml-old"
    cp -cR "$APP.mml-old" "$APP" 2>/dev/null || cp -R "$APP.mml-old" "$APP"
    rm -rf "$APP.mml-old"
    touch "$RES/.mml-writable" || {
        echo "Cannot write into $APP." >&2
        echo "If you copied files into the bundle by hand, give Terminal permission" >&2
        echo "(System Settings > Privacy & Security > App Management) and retry." >&2
        exit 1
    }
fi
rm -f "$RES/.mml-writable"

echo "== Installing into the game =="
rm -rf "$RES/data_MelvorModLoader_macos_arm64" "$RES/data_MelvorModLoader_macos_x86_64"
cp -R "$ROOT/out/loader-osx-arm64" "$RES/data_MelvorModLoader_macos_arm64"
cp -R "$ROOT/out/loader-osx-x64"  "$RES/data_MelvorModLoader_macos_x86_64"
printf '[dotnet]\nproject/assembly_name="MelvorModLoader"\n' > "$MACOS/override.cfg"

for mod in "$@"; do
    mkdir -p "$RES/mods/$mod"
    cp "$ROOT/Mods/$mod/bin/Release/net10.0/$mod.dll" "$RES/mods/$mod/"
    [ -f "$ROOT/Mods/$mod/README.txt" ] && cp "$ROOT/Mods/$mod/README.txt" "$RES/mods/$mod/"
done

echo
echo "Done. Start the game from Steam and watch the top-left corner for"
echo "\`Melvor Mod Loader - N mod\`. The log lives at"
echo "  $RES/MelvorModLoader.log"
