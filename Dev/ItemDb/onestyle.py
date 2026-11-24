"""One Style's part of the item database: its 58 unique items and its three rarities (Magic, Rare, Rare with 4 affixes).

Read from the mod's own data (Mods/OneStyle/data: uniques.json, affixes.json, elites.json), with the same numbers the
mod uses in the game (RareItems*.cs, Uniques.cs, UniquePowers*.cs). Every record gets "mod": "OneStyle" and a "rarity",
so the page can show them apart and colour them.
"""
import json
import math
import os
import shutil

HERE = os.path.dirname(os.path.abspath(__file__))
MOD = os.path.normpath(os.path.join(HERE, "..", "..", "Mods", "OneStyle"))

STYLE_SKILL = {"MelvorBase:Melee": "Attack", "MelvorBase:Ranged": "Ranged", "MelvorBase:Magic": "Magic"}
ARMOUR = {"Helmet", "Torso", "Legs", "Gloves", "Boots", "Offhand"}
JEWEL = {"Ring", "Amulet", "Cape"}
UNIQUE_CHANCE = 0.15            # Uniques.Chance: each Unique kill
UNIQUE_SPAWN_AREA = 0.008       # Elites.Chances: a Unique in a combat area


def load(name):
    with open(os.path.join(MOD, "data", name), encoding="utf-8") as f:
        return json.load(f)


def unique_reqs(u, base):
    """RareItemsRequirements.RequirementsFor for a unique item: its own level in its style, Defence or Hitpoints."""
    level = max(1, min(99, u["Level"]))
    slot = u["Slot"]
    skills = []
    style = STYLE_SKILL.get(u.get("Style") or "")
    if style and slot not in JEWEL:
        skills.append(style)
    if slot in ARMOUR:
        skills.append("Defence")
    if slot in JEWEL:
        skills.append("Hitpoints")
    if not skills:
        skills.append(STYLE_SKILL.get(base.get("styleId", ""), "Attack") if slot in ("Weapon", "Ammo") else "Hitpoints")
    return [{"skill": s, "level": level} for s in dict.fromkeys(skills)]


def tier_top(t):
    """AffixDef.Range for T1: from halfway between T2 and T1 to T1 +15%."""
    step = 1 if all(v == math.floor(v) for v in t) else 0.1
    snap = lambda v, how: round(how(round(v / step, 6)) * step, 1)
    if t[-1] == t[-2]:
        return f"{t[-1]:g}"
    lo = snap((t[-2] + t[-1]) / 2, math.ceil)
    hi = snap(t[-1] * 1.15, lambda x: math.floor(x + 0.5))
    return f"{lo:g}-{hi:g}"


def add(db, img_dir):
    uniques = load("uniques.json")
    affixes = load("affixes.json")
    elites = {e["Id"]: e for e in load("elites.json")}
    items = {i["id"]: i for i in db["items"]}
    monsters = {m["id"]: m for m in db["monsters"]}
    art_out = os.path.join(img_dir, "items")
    added = 0
    for u in uniques:
        base_id = u["BaseId"].split(":")[1]
        base = items.get(base_id)
        if base is None:
            continue
        mid = u["Source"].split(":")[1]
        mon = monsters.get(mid)
        who = elites.get(u["Source"], {}).get("UniqueName") or u["SourceName"]
        rec = {k: v for k, v in base.items() if k in ("slot", "cat", "stats", "wtype", "style", "styleId", "speed", "hands", "ammoType", "kind")}
        rec.update({
            "id": u["Id"], "name": u["Name"], "desc": u.get("Flavour", ""), "mod": "OneStyle", "rarity": "Unique",
            "sell": max((base.get("sell") or 0) * 5, 500), "sellCur": base.get("sellCur", "Gold Pieces"), "tags": ["Combat"],
            "reqs": unique_reqs(u, base),
            "bonuses": [s["Text"] for s in u["Stats"]] + [f"{u['MechanicName']}: {u['Mechanic']}"],
            "base": base_id,
            "src": [{"t": "kill", "via": f"{who} (Unique {mon['name'] if mon else mid})", "link": {"kind": "monster", "id": mid},
                     "per": "Unique kill", "area": (mon or {}).get("areas", [{}])[0].get("name", "") if mon else "",
                     "ch": UNIQUE_CHANCE, "q": [1, 1], "exp": UNIQUE_CHANCE,
                     "note": f"{UNIQUE_CHANCE:.0%} each time you kill {who}; about {UNIQUE_SPAWN_AREA * UNIQUE_CHANCE:.2%} a kill of a {mon['name'] if mon else mid} in a combat area"}],
            "use": [],
        })
        art = os.path.join(MOD, u["Icon"].replace("/", os.sep))
        if os.path.exists(art) and img_dir:
            os.makedirs(art_out, exist_ok=True)
            shutil.copyfile(art, os.path.join(art_out, u["Id"] + ".webp"))
            rec["img"] = 1
        db["items"].append(rec)
        added += 1
        if mon is not None:
            mon["oneStyle"] = {"unique": who, "item": u["Id"], "chance": UNIQUE_CHANCE}
            mon.setdefault("loot", [])
    # the three rarities: one row each, with the rules and every affix that can roll
    pre = [a for a in affixes if a["Kind"] == "P"]
    suf = [a for a in affixes if a["Kind"] == "S"]
    lines = lambda group: [f"{a['Name']}: {a['Text'].replace('{v}', 'X').replace('{style}', 'your style')} (T1 {tier_top(a['Tiers'])})" for a in group]
    rules = ("Rolled by One Style when a monster dies, from a piece of gear near the monster's level. Item level = the monster's "
             "combat level (+5 on your slayer task or a dungeon's last boss); best tier T5 below 15, T4 from 15, T3 from 35, T2 from 60, T1 "
             "from 85. An affix rolls its best tier 50%, one lower 35%, two lower 15%. Hold Alt over one in the game to see its tiers.")
    for rid, name, rarity, count, srcs in [
        ("OsRMagic", "Magic item", "Magic", "2 affixes (1 prefix, 1 suffix)",
         [("Any monster", "kill", 0.01), ("Champion", "Champion kill", 0.60), ("Unique (its second item)", "Unique kill", 0.60)]),
        ("OsRRare", "Rare item", "Rare", "3 affixes (at most 2 prefixes and 2 suffixes)",
         [("Any monster", "kill", 0.01 * 0.8), ("Champion", "Champion kill", 0.33), ("Unique", "Unique kill", 0.8)]),
        ("OsRRare4", "Rare item (4 affixes)", "Rare4", "4 affixes (2 prefixes, 2 suffixes)",
         [("Any monster", "kill", 0.01 * 0.2), ("Champion", "Champion kill", 0.07), ("Unique", "Unique kill", 0.2)]),
    ]:
        db["items"].append({
            "id": rid, "name": name, "desc": f"{count}. {rules}", "mod": "OneStyle", "rarity": rarity, "cat": "One Style rarity",
            "tags": ["Combat"], "sell": 0, "sellCur": "Gold Pieces",
            "bonuses": [f"Prefix — {l}" for l in lines(pre)] + [f"Suffix — {l}" for l in lines(suf)],
            "src": [{"t": "kill", "via": via, "per": per, "ch": ch, "q": [1, 1], "exp": ch} for via, per, ch in srcs],
            "use": [],
        })
    return added
