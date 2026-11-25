# The gear that only a dungeon gives (its clear reward, or the chest that is its clear reward), from the item database
# (docs/db/data.json, built by Dev/ItemDb/build.py from the game's own data). One Style keeps these out of its general
# pool of rare item bases: each comes rare only from its own dungeon (zindan-nadir/karar.md, 2026-11-25).
# Run again after a game update: python -I Dev/OneStyleTools/dungeongear.py
import json, os
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
db = json.load(open(os.path.join(ROOT, 'docs', 'db', 'data.json'), encoding='utf-8'))
items = {i['id']: i for i in db['items']}
# chests (and other items) that are a dungeon's clear reward -> the dungeon's id
reward_of = {}
for i in items.values():
    for s in i.get('src', []):
        if s['t'] == 'dungeon':
            reward_of[i['id']] = (s.get('link') or {}).get('id', '') or s.get('via', '')


def dungeons_of(item):
    """The dungeons this item comes from, or None when it also comes from anywhere else."""
    out = set()
    for s in item.get('src', []):
        if s['t'] == 'dungeon':
            out.add((s.get('link') or {}).get('id', ''))
        elif s['t'] == 'open':
            vid = (s.get('link') or {}).get('id', '')
            chest = vid if vid in reward_of else next((c for c in reward_of if items[c]['name'] == s.get('via')), None)
            if chest is None:
                return None
            out.add(reward_of[chest])
        else:
            return None
    return out or None


rows = []
for i in items.values():
    if i.get('cat') not in ('Armour', 'Weapon'):
        continue
    ds = dungeons_of(i)
    if ds:
        rows.append({'Item': 'MelvorBase:' + i['id'], 'Name': i['name'], 'Dungeons': sorted('MelvorBase:' + d for d in ds)})
rows.sort(key=lambda r: (r['Dungeons'], r['Name']))
out = os.path.join(ROOT, 'Mods', 'OneStyle', 'data', 'dungeon-gear.json')
with open(out, 'w', encoding='utf-8', newline='\n') as f:
    json.dump(rows, f, indent=1, ensure_ascii=False)
    f.write('\n')
print(f'{len(rows)} dungeon-only pieces of gear -> {out}')
