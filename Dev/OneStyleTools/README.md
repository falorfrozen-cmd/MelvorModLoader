# One Style data tools

Scripts that write One Style's data files (`Mods/OneStyle/data/`) and pictures (`Mods/OneStyle/art/`).

| Script | Writes | From |
|---|---|---|
| `elitedata.py` | `data/elites.json`, `art/champion`, `art/unique` | the hack-and-slash design folder (OneDrive `Ortak-Calisma/hack-and-slash`) and the game data |
| `affixgen.py` | `data/affixes.json` (48 affixes) | the affix design in the same folder |
| `uniquegen.py` | `data/uniques.json` (58 unique items), with `--art` also `art/items` | the design (`esyalar.json`, `build/build-esyalar.json`, `../yeni-uniqueler/yeni-esyalar.json`), `uniques-en.json`, `data/elites.json`, the game data |
| `newuniques.py` | the eight build-gap uniques' texts in `uniques-en.json` | `yeni-uniqueler/karar.md` (run once, then `uniquegen.py`) |
| `baselevels.py` | `data/base-levels.json` (the level a rare item's base counts as) | the item database `docs/db/data.json` and `Dev/rare-pool-monsters.tsv` (test `-Only rarepool`) |
| `texts.py` | `uniques-en.json` | the mechanic texts as the mod does them (`UniquePowers*.cs`): edit here, run it, then `uniquegen.py` |

The game data (`MelvorBase.json`) is expected in `../gamedata/`. Get it out of `Melvor2.exe` with the item database's reader:

```
python -c "import sys; sys.path.insert(0,'Dev/ItemDb'); import build, os; open('Dev/gamedata/MelvorBase.json','wb').write(build.Package(os.path.join(build.GAME,'Melvor2.exe')).read('Data/Packages/MelvorBase.json'))"
```

Unique item bases: a weapon's base must attack with the unique's own style (`base_for` filters on the base's level skills, and
never a throwing weapon for melee). Requirements and "Found in" are not taken from the base: the mod sets them in the game
(`RareItemsRequirements.cs`).
