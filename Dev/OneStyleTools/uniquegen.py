# Writes One Style's data/uniques.json (the 50 unique items) and their icons as art/items/*.webp.
# Sources: the hack-and-slash design (esyalar.json, build/build-esyalar.json), English texts (uniques-en.json),
# the elites (data/elites.json: which Unique drops which item) and the game data (a normal base item per unique).
import json, os, re, sys

HS = r"C:\Users\falor\OneDrive\Belgeler\Ortak-Calisma\hack-and-slash\esyalar"
MOD = r"C:\Program Files (x86)\Steam\steamapps\common\Melvor Idle 2\MelvorModLoader_src\Mods\OneStyle"
TOOLS = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.join(os.path.dirname(TOOLS), "gamedata", "MelvorBase.json")

items = json.load(open(os.path.join(HS, "esyalar.json"), encoding="utf-8"))
builds = json.load(open(os.path.join(HS, "build", "build-esyalar.json"), encoding="utf-8"))
for b in builds:
    b["_art"] = os.path.join(HS, "build", "resim")
for i in items:
    i["_art"] = os.path.join(HS, "resim")
# the build-gap round (2026-11-24): eight more, with their base item chosen by hand ("base": no style penalty)
NEW_DIR = os.path.join(os.path.dirname(HS), "yeni-uniqueler")
news = json.load(open(os.path.join(NEW_DIR, "yeni-esyalar.json"), encoding="utf-8")) if os.path.exists(os.path.join(NEW_DIR, "yeni-esyalar.json")) else []
for n in news:
    n["_art"] = os.path.join(NEW_DIR, "resim")
all_items = items + builds + news
english = json.load(open(os.path.join(TOOLS, "uniques-en.json"), encoding="utf-8")) if os.path.exists(os.path.join(TOOLS, "uniques-en.json")) else {}
elites = {e["UniqueName"]: e["Id"] for e in json.load(open(os.path.join(MOD, "data", "elites.json"), encoding="utf-8"))}
game = json.load(open(GAME, encoding="utf-8"))

# ---- game gear: slot, style, level
def level_of(cap):
    best = 1
    for r in cap.get("Requirements", []):
        lv = (r.get("Data") or {}).get("Level")
        if isinstance(lv, int):
            best = max(best, lv)
    return best

gear = []
for it in game["Objects"]["Item"]:
    caps = it.get("Capabilities") or {}
    eq = caps.get("MelvorBase:Equipment")
    if not eq or "Collectable" in it["Id"] or "Belra" in it["Id"] or it["Id"] in ("GasMask",) or "Lantern" in it["Id"]:
        continue
    slot = eq["Type"].split(":")[1]
    w = caps.get("MelvorBase:Weapon")
    skills = {(r.get("Data") or {}).get("Component", {}).get("Entity", "").split(":")[-1] for r in eq.get("Requirements", [])}
    gear.append({"Id": "MelvorBase:" + it["Id"], "Slot": slot, "Level": level_of(eq), "Weapon": (w or {}).get("Type", ""), "Name": it["Id"],
                 "Skills": skills})

WEAPON_WORDS = [("crossbow", "Crossbow"), ("bow", "Bow"), ("staff", "Staff"), ("wand", "Wand"), ("maul", "Maul"), ("maul", "Mallet"), ("hammer", "Hammer"),
                ("claw", "Dagger"), ("cleaver", "2HSword"), ("two-handed sword", "2HSword"), ("sword", "Sword"), ("spear", "Spear")]
OFFHAND_WORDS = [("tome", "Book"), ("codex", "Book"), ("palimpsest", "Book"), ("liturgy", "Book"), ("orb", "Book"), ("shield", "Shield"),
                 ("bastion", "Shield"), ("reprisal", "Shield")]


def base_for(item):
    slot = item["slot"]
    want = max(1, int(item.get("level", 1)))
    kind = (item.get("type", "") + " " + item["name"]).lower()
    pool = [g for g in gear if g["Slot"] == slot]
    # a weapon's base must attack with the unique's own style (a melee maul is no ranged throwing hammer)
    style = style_of(item).split(":")[-1]
    if slot in ("Weapon", "Ammo") and style:
        own = {"Melee": {"Attack", "Strength"}, "Ranged": {"Ranged"}, "Magic": {"Magic"}}[style]
        other = {"Attack", "Strength", "Ranged", "Magic"} - own
        fit = [g for g in pool if g["Skills"] & own and not g["Skills"] & other and "Throw" not in g["Weapon"]]
        pool = fit or pool
    words = WEAPON_WORDS if slot == "Weapon" else OFFHAND_WORDS if slot == "Offhand" else []
    for word, part in words:
        if word in kind:
            narrowed = [g for g in pool if part.lower() in g["Name"].lower()]
            if part == "Sword":
                narrowed = [g for g in narrowed if "2H" not in g["Name"]]
            if narrowed:
                pool = narrowed
                break
    below = [g for g in pool if g["Level"] <= want] or pool
    best = max(below, key=lambda g: (g["Level"], -len(g["Name"])))
    return best["Id"]


def style_of(item):
    kind = (item.get("type", "") + " " + item.get("build", "")).lower()
    if any(w in kind for w in ("bow", "crossbow", "quiver", "arrow", "ranged")):
        return "MelvorBase:Ranged"
    if any(w in kind for w in ("staff", "wand", "tome", "codex", "magic", "mage", "spell")):
        return "MelvorBase:Magic"
    if any(w in kind for w in ("sword", "maul", "claw", "cleaver", "melee")):
        return "MelvorBase:Melee"
    return ""


STATS = {
    "PercentDamageDealt": ("Character", "PercentMaxHit", "Player", 0.01, "max hit"),
    "PercentMaxHit": ("Character", "PercentMaxHit", "Player", 0.01, "max hit"),
    "PercentMaxHitpoints": ("Character", "PercentMaxHitpoints", "Player", 0.01, "max hit points"),
    "PercentAccuracyRating": ("Character", "PercentAccuracyRating", "Player", 0.01, "accuracy"),
    "PercentAttackInterval": ("Character", "PercentAttackInterval", "Player", 0.01, "attack interval"),
    "PercentAttackReflect": ("Character", "PercentAttackReflect", "Player", 0.01, "damage reflected"),
    "PercentDamageTaken": ("Character", "PercentDamageTaken", "Player", 0.01, "damage taken"),
    "PercentEvasionRating": ("Character", "PercentEvasionRating", "Player", 0.01, "evasion"),
    "FlatCriticalHitChance": ("Character", "FlatCriticalHitChance", "Player", 0.01, "critical hit chance"),
    "FlatCriticalHitMultiplier": ("Character", "FlatCriticalHitMultiplier", "Player", 0.01, "critical hit damage"),
    "FlatProtectionRating": ("Character", "FlatProtectionRating", "Player", 1, "protection"),
    "FoodPreservationChance": ("CombatFood", "FoodPreservationChance", "Food", 0.01, "chance to keep food"),
    "PercentHealing": ("CombatFood", "PercentHealing", "Food", 0.01, "healing from food"),
    "AmmoPreservationChance": ("AmmoConsumption", "AmmoPreservationChance", "Player", 0.01, "chance to keep ammo"),
    "RunePreservationChance": ("MagicSpell", "RunePreservationChance", "MagicSpells", 0.01, "chance to keep runes"),
    "PercentRareLootChance": ("CombatLoot", "ItemDoublingChance", "Loot", 0.01, "chance to double combat loot"),
    "StunAvoidanceChance": ("CombatEffect", "AvoidanceChance", "Player", 0.01, "chance to shrug off a stun"),
}


def stat(text):
    m = re.match(r"([+-]?[\d.]+)(%?)\s+(?:mod:)?(\w+)", text)
    if not m or m.group(3) not in STATS:
        print("  unknown stat:", text, file=sys.stderr)
        return None
    value = float(m.group(1))
    st, name, comp, scale, words = STATS[m.group(3)]
    lc = {"Component": {"Entity": "MelvorBase:Combat", "Component": comp}}
    if m.group(3) == "StunAvoidanceChance":
        lc["Effect"] = "MelvorBase:Stun"
    bonus = {"Stat": {"StatsType": "MelvorBase:" + st, "Name": name}, "Value": value * scale, "FromContext": None,
             "Condition": None, "Scaling": None, "LocalContext": lc}
    shown = f"{value:+g}{m.group(2)} {words}"
    return {"Text": shown, "Bonus": bonus}


out = []
missing_art = []
for it in all_items:
    camel = re.sub(r"[^A-Za-z0-9]", "", it["name"].title())
    en = english.get(it["name"], {})
    # uniques-en.json may replace the design's stat lines ("+5% FlatCriticalHitChance"), e.g. a stat its power makes useless
    stats = [s for s in (stat(t) for t in en.get("Stats", it["stats"])) if s]
    art_file = os.path.join(it["_art"], camel + ".png")
    if not os.path.exists(art_file):
        cands = [f for f in os.listdir(it["_art"]) if f.lower().replace("-", "").replace("'", "") == (camel + ".png").lower()]
        art_file = os.path.join(it["_art"], cands[0]) if cands else ""
    if not art_file:
        missing_art.append(it["name"])
    out.append({
        "Id": "OsU" + camel, "Name": it["name"], "Slot": it["slot"], "Style": style_of(it) if it["slot"] in ("Weapon", "Offhand", "Ammo") else "",
        "Level": int(it.get("level", 1)), "Source": elites.get(it["source_unique"], ""), "SourceName": it["source_unique"],
        "BaseId": it.get("base") or base_for(it), "Stats": stats,
        "MechanicName": en.get("MechanicName", it.get("mechanic_name", "")), "Mechanic": en.get("Mechanic", it.get("mechanic", "")),
        "Flavour": en.get("Flavour", ""), "Icon": "art/items/" + camel + ".webp", "_art": art_file,
    })

if "--art" in sys.argv:
    from PIL import Image
    os.makedirs(os.path.join(MOD, "art", "items"), exist_ok=True)
    for u in out:
        if u["_art"]:
            Image.open(u["_art"]).convert("RGBA").resize((128, 128), Image.LANCZOS).save(
                os.path.join(MOD, u["Icon"].replace("/", os.sep)), "WEBP", quality=90, method=4)
for u in out:
    u.pop("_art")
with open(os.path.join(MOD, "data", "uniques.json"), "w", encoding="utf-8", newline="\n") as f:
    json.dump(out, f, ensure_ascii=False, indent=1)
print(len(out), "uniques;", sum(1 for u in out if not u["Source"]), "without a source;", "missing art:", missing_art,
      "; english:", sum(1 for u in out if u["Name"] in english))
