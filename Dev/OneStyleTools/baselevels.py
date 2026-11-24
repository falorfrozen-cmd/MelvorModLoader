# One Style: the level a rare item's base counts as when a monster's drop pool is made.
# A game item's own requirement is not enough: Hypnotic Amulet, Scent of Blood Ring, the lit lanterns and others ask for no
# level at all, so they were in a level 1 Golbin's pool. Here every piece of gear gets the level of its easiest source
# (the monster that drops it, the chest it comes from, its crafting / firemaking level, what it is upgraded from), and
# counts as max(its requirement, that level). Reads the item database (docs/db/data.json, Dev/ItemDb/build.py), writes
# Mods/OneStyle/data/base-levels.json with only the items whose level goes up. Run after build.py.
import json, os, sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
db = json.load(open(os.path.join(ROOT, 'docs', 'db', 'data.json'), encoding='utf-8'))
items = {i['id']: i for i in db['items']}
# the game's own combat level of each monster (written by the test group "-Window -Only rarepool"): the database's "lvl"
# is on another scale (Kraken 1900)
import csv
mlvl = {r['id'].replace('MelvorBase:', ''): int(r['combat level'])
        for r in csv.DictReader(open(os.path.join(ROOT, 'Dev', 'rare-pool-monsters.tsv'), encoding='utf-8'), delimiter='	')}
area = {a['id']: max((mlvl.get(m, 1) for m in a['monsters']), default=1) for a in db['areas']}

memo, busy = {}, set()

def level(iid):
    """The level of an item's easiest source (1 when it has none or can be bought)."""
    if iid in memo: return memo[iid]
    if iid in busy or iid not in items: return 1
    busy.add(iid)
    best = None
    for s in items[iid].get('src') or []:
        t = s['t']
        ing = max((level(x['id']) for x in s.get('ing') or []), default=1)
        if t == 'kill': v = mlvl.get(s.get('link', {}).get('id'), 1)
        elif t == 'dungeon': v = area.get(s.get('link', {}).get('id'), 1)
        elif t == 'open': v = level(s.get('link', {}).get('id'))
        elif t == 'upgrade': v = ing
        elif t == 'quest': continue   # a one-off quest reward (Mithril Lantern from the first quest) does not make it common gear
        elif t in ('shop', 'ranch'): v = 1
        else: v = max(s.get('lvl') or 1, ing)   # craft, fm, steal, hunt, fish, mine, wc, farm, agility, event
        best = v if best is None else min(best, v)
    busy.discard(iid)
    memo[iid] = best or 1
    return memo[iid]

out = {}
for i in db['items']:
    if not i.get('slot') or i.get('mod'): continue
    req = max((r['level'] for r in i.get('reqs') or []), default=1)
    lv = level(i['id'])
    lv = min(lv, 99)
    if lv > req: out['MelvorBase:' + i['id']] = lv
path = os.path.join(ROOT, 'Mods', 'OneStyle', 'data', 'base-levels.json')
json.dump(dict(sorted(out.items())), open(path, 'w', encoding='utf-8'), indent=1)
print(len(out), 'items count higher than their requirement ->', path)
for k in ['HypnoticAmulet', 'ScentOfBloodRing', 'PlagueheartAmulet', 'RoyalBeeRing', 'DragonruneLanternLit', 'GasMask',
          'FarmersScythe', 'TreantNecklace', 'WilloWispLantern', 'NecromancerAcolyteRing', 'StoneSword', 'MithrilSword']:
    print(' ', k, out.get('MelvorBase:' + k, '(own requirement)'))
