#!/usr/bin/env bash
set -euo pipefail
unset CDPATH

fail() { printf '%s\n' "$*" >&2; exit 1; }

[[ $# == 2 ]] || fail 'Usage: bash Dev/package-linux.sh GAME_DIR OUTPUT_ZIP'
[[ -n $1 && $2 == *.zip ]] || fail 'Specify a game directory and an output path ending in .zip.'
command -v dotnet >/dev/null || fail 'Install the .NET 10 SDK (dotnet is missing).'
command -v python3 >/dev/null || fail 'Install python3 to create the ZIP.'

root=$(cd -- "${BASH_SOURCE[0]%/*}/.." && pwd -P)
game=$(cd -- "$1" 2>/dev/null && pwd -P) || fail 'Choose a native Linux Steam game directory.'
data="$game/data_GamesByMalcs.Melvor2_linuxbsd_x86_64"
[[ -f "$game/MelvorIdle2.x86_64" && -f "$data/GodotSharp.dll" && -f "$data/GamesByMalcs.Melvor2.dll" ]] ||
    fail 'Expected the native Linux x86_64 Steam executable and game assemblies.'

output=$2
[[ $output == /* ]] || output="$PWD/$output"
output_dir=$(cd -- "${output%/*}/" 2>/dev/null && pwd -P) || fail 'The output directory must already exist.'
output="$output_dir/${output##*/}"
[[ ! -e "$output" && ! -L "$output" ]] || fail "Output already exists: $output"
[[ $output_dir != "$game" && $output_dir != "$game/"* ]] || fail 'Choose an output directory outside the game install.'

cd -- "$root"
sdk=$(dotnet --version) || fail 'Could not select the .NET 10 SDK.'
[[ $sdk == 10.* ]] || fail "Select the .NET 10 SDK (found $sdk)."

# Keep the completed archive on the destination filesystem so it can be linked atomically.
stage=$(mktemp -d -- "$output_dir/.melvor-package.XXXXXXXX")
trap 'rm -rf -- "$stage"' EXIT
payload=data_MelvorModLoader_linuxbsd_x86_64

dotnet build "$root/Loader/MelvorModLoader.csproj" -c Release \
    -p:RuntimeIdentifier=linux-x64 -p:GameData="$data" \
    -p:BaseIntermediateOutputPath="$stage/obj/" \
    -p:DefaultItemExcludesInProjectFolder='obj/**%3Bbin/**' \
    -o "$stage/$payload"

[[ -f "$stage/$payload/MelvorModLoader.dll" && -f "$stage/$payload/libhostfxr.so" ]] ||
    fail 'The loader build did not produce a self-contained Linux payload.'
[[ ! -e "$stage/$payload/GodotSharp.dll" && ! -e "$stage/$payload/GamesByMalcs.Melvor2.dll" ]] ||
    fail 'Game assemblies must not be included in the ZIP.'

# The self-contained build omits the runtime pack's license and notices.
python3 - "$stage/obj/project.assets.json" "$stage/$payload" <<'PY'
import json
import sys
from pathlib import Path
from shutil import copyfile

assets, payload = map(Path, sys.argv[1:])
config = json.loads((payload / 'MelvorModLoader.runtimeconfig.json').read_text())
version = next(f['version'] for f in config['runtimeOptions']['includedFrameworks']
               if f['name'] == 'Microsoft.NETCore.App')
roots = json.loads(assets.read_text())['packageFolders']
for root in roots:
    pack = Path(root) / 'microsoft.netcore.app.runtime.linux-x64' / version
    if pack.is_dir():
        for name in ('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT'):
            copyfile(pack / name, payload / name)
        break
else:
    sys.exit(f'Could not locate the .NET {version} Linux runtime pack notices.')
PY

printf '[dotnet]\nproject/assembly_name="MelvorModLoader"\n' > "$stage/override.cfg"
(cd -- "$stage" && python3 -m zipfile -c loader.zip override.cfg "$payload")
ln -T -- "$stage/loader.zip" "$output" || fail "Could not create output (it may already exist): $output"
printf 'Created %s\n' "$output"
