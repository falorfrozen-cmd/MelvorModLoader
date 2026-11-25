"""
Builds the item database for the wiki (docs/db/data.json) from the game's own data file.

    python build.py                       # reads the data straight out of Melvor2.exe (the game's package)
    python build.py --json MelvorBase.json

Nothing from the game is copied as is: no images, no text files. The output is our own summary
(names, numbers, where an item comes from and what it is used for), the same as any wiki.
"""
import argparse
import json
import math
import os
import re
import struct
import sys
from collections import defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.path.normpath(os.path.join(HERE, "..", "..", ".."))
GAME_VERSION = "0.11.7"  # the game version this data was read from (see the game's title bar)
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "docs", "db", "data.json"))


# ---------------------------------------------------------------- reading the game's package

class Package:
    """The game's files sit in a Godot package (format 4) at the end of Melvor2.exe."""

    def __init__(self, exe):
        with open(exe, "rb") as f:
            self.data = data = f.read()
        start = data.rfind(b"GDPC", 0, len(data) - 64)
        while start >= 0:
            version, major = struct.unpack_from("<II", data, start + 4)
            if version in (2, 3, 4) and major == 4:
                break
            start = data.rfind(b"GDPC", 0, start)
        if start < 0:
            raise SystemExit("No Godot package found in " + exe)
        flags = struct.unpack_from("<I", data, start + 20)[0]
        file_base, dir_offset = struct.unpack_from("<QQ", data, start + 24)
        if flags & 2:                   # offsets are relative to the package
            file_base += start
            dir_offset += start
        self.files = {}
        pos = dir_offset
        count = struct.unpack_from("<I", data, pos)[0]
        pos += 4
        for _ in range(count):
            length = struct.unpack_from("<I", data, pos)[0]
            pos += 4
            path = data[pos:pos + length].rstrip(b"\0").decode("utf-8")
            pos += length
            offset, size = struct.unpack_from("<QQ", data, pos)
            pos += 16 + 16 + 4         # offset, size, md5, flags
            self.files[path] = (file_base + offset, size)

    def read(self, path):
        if path not in self.files:
            return None
        at, size = self.files[path]
        return self.data[at:at + size]

    def image(self, res_path):
        """A picture as the game imported it: the .import file names a .ctex, which holds a WebP or PNG."""
        path = res_path.replace("res://", "")
        meta = self.read(path + ".import")
        if not meta:
            return None
        m = re.search(rb'path="res://([^"]+\.ctex)"', meta)
        tex = m and self.read(m.group(1).decode())
        if not tex or tex[:4] != b"GST2":
            return None
        for sig in (b"RIFF", b"\x89PNG"):
            i = tex.find(sig, 0, 128)
            if i >= 0:
                size = struct.unpack_from("<I", tex, i - 4)[0]
                return tex[i:i + size]
        return None


def read_package_file(exe, wanted):
    data = Package(exe).read(wanted)
    if data is None:
        raise SystemExit(wanted + " is not in the game's package")
    return data


def export_images(pack, objects, out_dir, size_items=64, size_monsters=96):
    """Small WebP copies of the item and monster pictures, for the database page."""
    from io import BytesIO
    from PIL import Image
    done = {"items": set(), "monsters": set()}
    for kind, items, key, px in (("items", objects["Item"], "Icon", size_items), ("monsters", objects.get("Enemy", []), "Sprite", size_monsters)):
        folder = os.path.join(out_dir, kind)
        os.makedirs(folder, exist_ok=True)
        for x in items:
            src = x.get(key) or ""
            if not src or "missing_artwork" in src:
                continue
            raw = pack.image(src)
            if not raw:
                continue
            img = Image.open(BytesIO(raw)).convert("RGBA")
            img.thumbnail((px, px), Image.LANCZOS)
            img.save(os.path.join(folder, x["Id"] + ".webp"), "WEBP", quality=88, method=4)
            done[kind].add(x["Id"])
    return done


# ---------------------------------------------------------------- names

def short(ref):
    """'MelvorBase:BirdNest' -> 'BirdNest'."""
    if ref is None:
        return None
    return ref.split(":", 1)[1] if ":" in ref else ref


def words(ident):
    """'Sword2H' -> 'Sword 2H', 'GoldPieces' -> 'Gold Pieces'."""
    return re.sub(r"(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[A-Z][a-z])", " ", ident or "")


class Names:
    """Display names for every object in the data, by type and id."""

    def __init__(self, objects):
        self.by_type = {}
        self.any = {}
        for kind, items in objects.items():
            table = {}
            for x in items:
                name = x.get("DisplayName")
                if isinstance(name, dict):
                    name = name.get("Singular")
                if isinstance(name, str) and name:
                    table[x["Id"]] = name
                    self.any.setdefault(x["Id"], name)
            self.by_type[kind] = table
        # skills and other game parts are named by their navigation buttons
        for kind in ("SkillNavigationOption", "LegacySkillNavigationOption", "CombatNavigationOption",
                     "SimpleNavigationOption", "BankNavigationOption", "ShopNavigationOption", "QuestsNavigationOption"):
            for x in objects.get(kind, []):
                ent = short(x.get("Entity") or x.get("Combat") or x.get("Bank") or x.get("Page"))
                if ent and x.get("DisplayName"):
                    self.by_type.setdefault("Entity", {}).setdefault(ent, x["DisplayName"])

    def of(self, kind, ref):
        ident = short(ref)
        return self.by_type.get(kind, {}).get(ident) or self.any.get(ident) or words(ident)

    def entity(self, ref):
        ident = short(ref)
        return self.by_type.get("Entity", {}).get(ident) or words(ident)


# ---------------------------------------------------------------- the game's modifier texts

def dotnet_number(value, fmt):
    """A small part of .NET's custom number formats, enough for the game's texts."""
    sections = fmt.split(";")
    if value < 0 and len(sections) > 1:
        section, value = sections[1], -value
    elif value == 0 and len(sections) > 2:
        section = sections[2]
    else:
        section = sections[0]
    if section in ("N0", "N", "N2"):
        digits = 0 if section in ("N0",) else 2
        return f"{value:,.{digits}f}"
    percent = "%" in section
    if percent:
        value *= 100
    body = re.search(r"[0#][0#,]*(\.[0#]+)?", section)
    if not body:
        return section
    spec = body.group(0)
    decimals = spec.split(".")[1] if "." in spec else ""
    fixed, optional = decimals.count("0"), decimals.count("#")
    text = f"{abs(value):,.{fixed + optional}f}" if "," in spec else f"{abs(value):.{fixed + optional}f}"
    if optional and "." in text:
        keep = len(text.split(".")[1]) - optional
        text = text.rstrip("0") if keep <= 0 else text[:len(text) - optional] + text[len(text) - optional:].rstrip("0")
        text = text.rstrip(".")
    if value < 0 and len(sections) == 1:
        text = "-" + text
    return section[:body.start()] + text + section[body.end():]


def interval_text(ms):
    seconds = abs(ms) / 1000
    text = f"{seconds:.2f}".rstrip("0").rstrip(".") + "s"
    return ("-" if ms < 0 else "") + text


class Modifiers:
    """Turns a bonus from the data into the same sentence the game shows."""

    def __init__(self, data, names):
        self.names = names
        # a flat combat stat bonus is written the stat's own way (Lifesteal 0.03 is "+3%", not "+0")
        self.percent_stats = {x["Id"] for x in data["Objects"].get("CombatStat", []) if x.get("IsPercent")}
        self.global_ = {x["StatGroup"]: x["TemplateString"] for x in data["GlobalModifierDescriptions"]}
        self.component = defaultdict(list)
        for x in data["ComponentModifierDescriptions"]:
            key = x["Key"]
            stat = key["Stat"]
            self.component[(stat["StatsType"], stat["Name"])].append(
                (set(key.get("LocalContextObjectCombination") or []), x["TemplateString"]))

    def context_name(self, part, ref, ctx):
        if part == "Component":
            return self.names.entity((ref or {}).get("Entity"))
        kinds = {"Recipe": None, "Category": None, "Item": "Item", "Effect": None}
        return self.names.of(kinds.get(part) or part, ref)

    def fill(self, template, value, ctx):
        def one(m):
            inner = m.group(1)
            if inner.startswith("Value:") or inner.startswith("0:"):
                fmt = inner.split(":", 1)[1]
                if fmt.startswith("cond:"):
                    for case in fmt[5:].split("|"):
                        cond, _, out = case.partition("?")
                        op, num = re.match(r"(<=|>=|<|>|==)?(-?[\d.]+)", cond).groups()
                        num = float(num)
                        ok = {"<": value < num, ">": value > num, "<=": value <= num, ">=": value >= num}.get(op or "==", value == num)
                        if ok:
                            return out
                    return ""
                if fmt.startswith("intervaltime") or fmt.startswith("numbertime"):
                    return interval_text(value)
                if fmt.startswith("combatStatModifier"):
                    if short((ctx or {}).get("CombatStat")) in self.percent_stats:
                        return dotnet_number(value, "+0.##%;-0.##%")
                    return dotnet_number(value, "+0;-#")
                return dotnet_number(value, fmt)
            path = inner.split(".")
            if path[0] in ("LocalContext", "FromContext", "LocComponent"):
                part = path[1] if path[0] != "LocComponent" else "Component"
                ref = (ctx or {}).get(part)
                if ref is None:
                    return "?"
                return self.context_name(part, ref, ctx)
            return m.group(0)

        return re.sub(r"\{([^}]*)\}", one, template)

    def text(self, bonus):
        stat = bonus.get("Stat") or {}
        value = bonus.get("Value") or 0
        if isinstance(stat, str):            # a global group
            template = self.global_.get(short(stat))
            return self.fill(template, value, {}) if template else f"{words(short(stat))} {value:+g}"
        ctx = bonus.get("LocalContext") or {}
        present = {k for k, v in ctx.items() if v is not None}
        choices = self.component.get((stat.get("StatsType"), stat.get("Name")), [])
        template = next((t for keys, t in choices if keys == present), None) \
            or next((t for keys, t in choices if keys <= present), None) \
            or (choices[0][1] if choices else None)
        if not template:
            return f"{words(stat.get('Name'))} {value:+g} ({words(short(stat.get('StatsType')))})"
        return self.fill(template, value, ctx)

    def all(self, bonuses):
        if not bonuses:
            return []
        out = []
        for g in bonuses.get("Global") or []:
            out.append(self.text({"Stat": g.get("StatGroup") or g.get("Stat"), "Value": g.get("Value")}))
        for c in bonuses.get("Component") or []:
            if not isinstance(c, dict):
                continue
            out.append(self.text(c))
        return out

    def effects(self, list_):
        out = []
        for e in list_ or []:
            mod = e.get("Modifier") or {}
            eff = (mod.get("Effect") or {})
            name = self.names.of("Effect", ((eff.get("Data") or {}).get("Effect")) or eff.get("Type"))
            trig = mod.get("Trigger") or {}
            when = words(((trig.get("Data") or {}).get("Type")) or short(trig.get("Type")) or "")
            who = "the enemy" if mod.get("AppliesTo") == "Target" else "you"
            chance = e.get("Chance", 1)
            lead = "" if chance >= 1 else dotnet_number(chance, "0.##%") + " chance: "
            out.append(f"{lead}{name} on {who} ({when.lower()})")
        return out


# ---------------------------------------------------------------- items

STYLE_OF_WEAPON = {
    "Wand": "Magic", "Staff": "Magic",
    "Bow": "Ranged", "Crossbow": "Ranged", "Crossbow2H": "Ranged", "Javelin": "Ranged", "Boomerang": "Ranged",
    "ThrowHammer": "Ranged",
}


def requirements(reqs, names):
    out = []
    for r in reqs or []:
        data = r.get("Data") or {}
        kind = short(r.get("Type"))
        if kind == "Level":
            out.append({"skill": names.entity((data.get("Component") or {}).get("Entity")), "level": data.get("Level")})
        elif kind == "ShopPurchase":
            out.append({"text": "Shop: " + names.of("ShopPurchase", data.get("ShopPurchase"))})
        elif kind:
            out.append({"text": words(kind)})
    return out


def item_record(x, names, mods, stat_names, toolbelt_types):
    caps = x.get("Capabilities") or {}
    rec = {
        "id": x["Id"],
        "name": names.of("Item", x["Id"]),
        "desc": x.get("Description") or "",
        "sell": (x.get("SellsFor") or {}).get("Quantity", 0),
        "sellCur": names.of("Currency", (x.get("SellsFor") or {}).get("Currency")),
        "tags": [short(t["BankTag"]) for t in x.get("BankTags") or []],
    }
    eq = caps.get("MelvorBase:Equipment")
    wp = caps.get("MelvorBase:Weapon")
    tb = caps.get("MelvorBase:Toolbelt")
    if eq:
        slot = short(eq.get("Type"))
        rec["slot"] = slot
        rec["cat"] = "Weapon" if slot == "Weapon" else "Ammo" if slot == "Ammo" else "Armour"
        rec["reqs"] = requirements(eq.get("Requirements"), names)
        occupies = [short(s) for s in eq.get("OccupiesSlots") or []]
        if occupies:
            rec["also"] = occupies
        stats = {}
        for s in (eq.get("Stats") or {}).get("Stats") or []:
            stats[short(s["Stat"])] = s["Value"]
        for p in (eq.get("Stats") or {}).get("PowerRatings") or []:
            stats["Power"] = stats.get("Power", 0) + p["Value"]
        for p in (eq.get("Stats") or {}).get("ProtectionRatings") or []:
            stats["Protection"] = stats.get("Protection", 0) + p["Value"]
        rec["stats"] = stats
        bonuses = mods.all(eq.get("Bonuses")) + mods.effects((eq.get("Bonuses") or {}).get("CombatEffect"))
        if bonuses:
            rec["bonuses"] = bonuses
        if eq.get("AmmoType"):
            rec["ammo"] = names.of("AmmoType", eq["AmmoType"])
        if eq.get("CanEquipQuantity"):
            rec["stack"] = True
    if wp:
        wtype = short(wp.get("Type"))
        rec["wtype"] = names.of("WeaponType", wtype)
        rec["hands"] = 2 if "Offhand" in (rec.get("also") or []) else 1
        rec["style"] = STYLE_OF_WEAPON.get(wtype, "Melee")
        rec["elements"] = [names.of("Element", e) for e in wp.get("Elements") or [] if short(e) != "Neutral"]
        if wp.get("RequiredAmmoType"):
            rec["needsAmmo"] = names.of("AmmoType", wp["RequiredAmmoType"])
    if tb:
        slot_type = short(tb.get("FitsInSlot"))
        info = toolbelt_types.get(slot_type, {})
        rec["cat"] = rec.get("cat") or {"Potion": "Potion", "UtilityItem": "Utility"}.get(short(info.get("Category")), "Tool")
        rec["tool"] = info.get("DisplayName") or words(slot_type)
        rec["reqs"] = rec.get("reqs") or requirements(tb.get("Requirements"), names)
        bonuses = mods.all(tb.get("Bonuses")) + mods.effects((tb.get("Bonuses") or {}).get("CombatEffect"))
        if bonuses:
            rec["bonuses"] = rec.get("bonuses", []) + bonuses
        uses = []
        for c in tb.get("ConsumesOn") or []:
            d = c.get("Data") or {}
            ch = d.get("Chance")
            uses.append(words(short(c.get("Type"))) + (f" ({dotnet_number(ch, '0.##%')})" if ch not in (None, 1) else ""))
        if uses:
            rec["consumed"] = uses
    if "MelvorBase:Food" in caps:
        rec["cat"] = rec.get("cat") or "Food"
        rec["heal"] = caps["MelvorBase:Food"].get("BaseHealing")
        fb = caps["MelvorBase:Food"].get("Bonuses") or {}
        b = mods.all(fb.get("Modifiers") or fb) + mods.effects((fb.get("Modifiers") or {}).get("CombatEffect"))
        if b:
            rec["bonuses"] = rec.get("bonuses", []) + b
    if "MelvorBase:Openable" in caps:
        rec["cat"] = rec.get("cat") or "Openable"
    if "MelvorBase:HuntingTrap" in caps:
        rec["cat"] = rec.get("cat") or "Trap"
        rec["trap"] = names.of("HuntingTrapType", caps["MelvorBase:HuntingTrap"].get("TrapType"))
    if "MelvorBase:Bury" in caps:
        g = caps["MelvorBase:Bury"].get("CurrencyGain") or {}
        rec["bury"] = {"cur": names.of("Currency", g.get("Currency")), "q": g.get("Quantity")}
        rec["cat"] = rec.get("cat") or "Bones"
    if "MelvorBase:Compost" in caps:
        rec["cat"] = rec.get("cat") or "Compost"
        rec["bonuses"] = rec.get("bonuses", []) + [mods.text(b) for b in caps["MelvorBase:Compost"].get("CompostModifierTemplate") or []]
    if "MelvorBase:FishingBait" in caps:
        rec["cat"] = rec.get("cat") or "Bait"
        rec["bonuses"] = rec.get("bonuses", []) + mods.all(caps["MelvorBase:FishingBait"].get("Bonuses"))
    rec.setdefault("cat", "Material")
    return rec


# ---------------------------------------------------------------- sources and uses

class Book:
    """Where each item comes from (sources) and what it is used for (uses)."""

    def __init__(self):
        self.sources = defaultdict(list)
        self.uses = defaultdict(list)

    def source(self, item_ref, **entry):
        ident = short(item_ref)
        if ident:
            self.sources[ident].append({k: v for k, v in entry.items() if v is not None})

    def use(self, item_ref, **entry):
        ident = short(item_ref)
        if ident:
            self.uses[ident].append({k: v for k, v in entry.items() if v is not None})


def level_req(reqs, names):
    for r in reqs or []:
        if short(r.get("Type")) == "Level":
            d = r["Data"]
            return names.entity(d["Component"]["Entity"]), d.get("Level")
    return None, None


# Not real items: the bare-hands weapon and a developer test item.
HIDDEN = {"DefaultWeapon", "ToolbeltOPnessItem"}

RECIPE_SKILL = {
    "CookingRecipe": "Cooking", "HerbloreRecipe": "Herblore", "FarmingRecipe": "Farming",
    "GrainMillRecipe": "Farming", "FiremakingRecipe": "Firemaking",
}


def recipes(objects, names, book):
    """Every recipe: its products are a source, its ingredients a use."""
    for kind in ("ArtisanRecipe", "CookingRecipe", "HerbloreRecipe", "FarmingRecipe", "GrainMillRecipe", "FiremakingRecipe"):
        for r in objects.get(kind, []):
            skill, lvl = level_req(r.get("Requirements"), names)
            skill = skill or RECIPE_SKILL.get(kind)
            name = r.get("DisplayName") or words(r["Id"])
            ingredients = [{"id": short(i["Item"]), "q": i["Quantity"]} for i in (r.get("Ingredients") or {}).get("Items") or []]
            currencies = [{"cur": names.of("Currency", c["Currency"]), "q": c["Quantity"]} for c in (r.get("Ingredients") or {}).get("Currencies") or []]
            products = list(r.get("Products") or [])
            if r.get("Product"):
                products.append(dict(r["Product"], Chance=1))
            t, per, note = {"FiremakingRecipe": ("fm", "log", None), "FarmingRecipe": ("farm", "harvest", "less if the crop has pests or disease"),
                            "GrainMillRecipe": ("craft", "batch", None)}.get(kind, ("craft", "craft", None))
            if kind == "CookingRecipe" and r.get("Difficulty"):
                note = f"success = (69 + Cooking level) / (69 + {r['Difficulty']}); a failure gives nothing"
            for p in products:
                book.source(p["Item"], t=t, per=per, skill=skill, lvl=lvl, via=name, q=[p.get("Quantity", 1)] * 2,
                            ch=p.get("Chance", 1), ing=ingredients, cur=currencies or None, note=note)
            for i in ingredients:
                book.use(i["id"], t="craft", skill=skill, lvl=lvl, via=name, q=i["q"],
                         makes=[short(p["Item"]) for p in products])


# Every formula below follows the game's code, with no player bonuses. Dev/ItemDb/README.md
# says where each one comes from.

def table_odds(table):
    """One weighted pick: each entry's chance is its weight over the total."""
    total = sum(e.get("Weight", 0) for e in table or [])
    for e in table or []:
        if total and e.get("Weight", 0) > 0:
            yield e, e["Weight"] / total


def combine(rows):
    """Several independent rolls for one item: chance of at least one, quantity range, expected amount."""
    out = {}
    for item, ch, lo, hi in rows:
        r = out.setdefault(short(item), {"miss": 1.0, "lo": lo, "hi": hi, "exp": 0.0})
        r["miss"] *= 1 - ch
        r["lo"], r["hi"] = min(r["lo"], lo), max(r["hi"], hi)
        r["exp"] += ch * (lo + hi) / 2
    return {k: {"ch": round(1 - v["miss"], 10), "q": [v["lo"], v["hi"]], "exp": round(v["exp"], 10)} for k, v in out.items()}


def drops_rows(drops):
    """Enemy drops and dungeon rewards: fixed items, then each table rolled on its own with its Chance."""
    rows = []
    for f in (drops or {}).get("FixedItems") or []:
        rows.append((f["Item"], 1.0, f.get("MinQuantity", 1), f.get("MaxQuantity", 1)))
    for t in (drops or {}).get("ItemTables") or []:
        for e, w in table_odds(t.get("Table")):
            rows.append((e["Item"], t.get("Chance", 1) * w, e.get("MinQuantity", 1), e.get("MaxQuantity", 1)))
    gp = None
    for c in (drops or {}).get("FixedCurrencies") or []:
        if short(c["Currency"]) == "GoldPieces":
            gp = [c.get("MinQuantity", 0), c.get("MaxQuantity", 0)]
    return combine(rows), gp


STANCE_STYLE = {"Stab": "Melee", "Slash": "Melee", "Crush": "Melee", "Block": "Melee",
                "Accurate": "Ranged", "Powerful": "Ranged", "LongRange": "Ranged"}


def combat(objects, names, book):
    monsters, areas = {}, []
    cats = {c["Id"]: c.get("DisplayName") for c in objects.get("CombatAreaCategory", [])}
    for e in objects.get("Enemy", []):
        loot, gp = drops_rows(e.get("Drops"))
        levels = {names.entity(l["Key"]): l["Value"] for l in e.get("Levels") or []}
        stance = max(e.get("Stances") or [{"Stance": "Stab", "Weight": 1}], key=lambda s: s.get("Weight", 0))
        name = e.get("DisplayName")
        monsters[e["Id"]] = {
            "id": e["Id"], "name": name if isinstance(name, str) else names.of("Enemy", e["Id"]),
            "levels": levels, "lvl": max(levels.values() or [1]),
            "style": STANCE_STYLE.get(short(stance["Stance"]), "Magic"),
            "loot": [{"id": k, **v} for k, v in loot.items()], "gp": gp, "areas": [],
        }
    for kind in ("CombatArea", "Dungeon"):
        for a in objects.get(kind, []):
            ids = []
            for g in a.get("Enemies") or []:
                if isinstance(g, str):
                    ids.append(short(g))
                else:
                    ids += [short(x["Enemy"]) for x in g.get("Enemies") or []]
            ids = list(dict.fromkeys(i for i in ids if i in monsters))
            area = {"id": a["Id"], "name": a.get("DisplayName") or words(a["Id"]),
                    "cat": cats.get(short(a.get("Category"))) or words(short(a.get("Category"))),
                    "diff": names.of("CombatAreaDifficulty", a.get("Difficulty")), "monsters": ids, "dungeon": kind == "Dungeon"}
            if kind == "Dungeon":
                rewards, gp = drops_rows(a.get("Rewards"))
                area["rewards"] = [{"id": k, **v} for k, v in rewards.items()]
                area["gp"] = gp
                for k, v in rewards.items():
                    book.source(k, t="dungeon", via=area["name"], link={"kind": "area", "id": a["Id"]}, per="clear", **v)
            areas.append(area)
            for i in ids:
                monsters[i]["areas"].append({"id": a["Id"], "name": area["name"], "dungeon": kind == "Dungeon"})
    for m in monsters.values():
        # Monsters inside a dungeon drop nothing there; only the dungeon's clear reward counts.
        open_areas = [a["name"] for a in m["areas"] if not a["dungeon"]]
        if not open_areas:
            if m["areas"]:
                m["note"] = "Only met in dungeons, where monsters drop nothing. The dungeon's clear reward counts instead."
            continue
        for x in m["loot"]:
            book.source(x["id"], t="kill", via=m["name"], link={"kind": "monster", "id": m["id"]}, per="kill",
                        area=", ".join(open_areas[:3]) + (" …" if len(open_areas) > 3 else ""), ch=x["ch"], q=x["q"], exp=x["exp"])
    return list(monsters.values()), areas


def thieving(objects, names, book):
    locs = {l["Id"]: l.get("DisplayName") for l in objects.get("ThievingLocation", [])}
    for r in objects.get("ThievingRecipe", []):
        skill, lvl = level_req(r.get("Requirements"), names)
        where = locs.get(short(r.get("Category")), "")
        note = f"per successful steal; success = (100 + Thieving level) / (100 + {r.get('Perception', 0)})"
        rows = []
        for key, ch_key in (("CommonDropTable", "BaseCommonLootChance"), ("RareDropTable", "BaseRareLootChance")):
            for e, w in table_odds(r.get(key)):
                rows.append((e["Item"], min(1, r.get(ch_key, 0)) * w, e.get("MinQuantity", 1), e.get("MaxQuantity", 1)))
        stages = max(1, r.get("Stages") or 1)
        for e, w in table_odds(r.get("CompletionDropTable")):
            rows.append((e["Item"], w / stages, e.get("MinQuantity", 1), e.get("MaxQuantity", 1)))
        heist = f"; heist reward after {stages} successes" if r.get("CompletionDropTable") else ""
        for k, v in combine(rows).items():
            book.source(k, t="steal", via=r.get("DisplayName") or words(r["Id"]), skill="Thieving", lvl=lvl, area=where,
                        per="success", note=note + heist, **v)


def mining_hits(p):
    """Average hits for one ore: each hit breaks the rock with chance p, and it always breaks once p adds up to 0.9999."""
    if p <= 0:
        return None
    k, acc = 0, 0.0
    while acc < 0.9999:
        acc += p
        k += 1
    return (1 - (1 - p) ** k) / p


def gathering(objects, names, book):
    for r in objects.get("WoodcuttingRecipe", []):
        skill, lvl = level_req(r.get("Requirements"), names)
        hits = math.ceil(r.get("BaseMaxHealth") or 1)
        name = r.get("DisplayName") or words(r["Id"])
        plural = "hits" if hits != 1 else "hit"
        if r.get("Product"):
            book.source(r["Product"]["Item"], t="wc", via=name, skill="Woodcutting", lvl=lvl, per="tree", ch=1,
                        q=[r["Product"]["Quantity"]] * 2, note=f"{hits} {plural} per tree")
        for p in r.get("RandomProductsPerHit") or []:
            book.source(p["Item"], t="wc", via=name, skill="Woodcutting", lvl=lvl, per="hit", ch=p["Chance"], q=[p.get("Quantity", 1)] * 2)
        for p in r.get("RandomProductsPerCut") or []:
            book.source(p["Item"], t="wc", via=name, skill="Woodcutting", lvl=lvl, per="tree", ch=p["Chance"],
                        q=[p.get("Quantity", 1)] * 2, note=f"when the tree falls ({hits} {plural})")
    for r in objects.get("MiningRecipe", []):
        skill, lvl = level_req(r.get("Requirements"), names)
        name = r.get("DisplayName") or words(r["Id"])
        hits = mining_hits(min(1, r.get("BaseProductChance", 1)))
        if r.get("Product"):
            book.source(r["Product"]["Item"], t="mine", via=name, skill="Mining", lvl=lvl, per="rock break", ch=1,
                        q=[r["Product"]["Quantity"]] * 2, note=f"about {hits:.2f} hits each" if hits else None)
        for p in r.get("RandomProductsPerHit") or []:
            book.source(p["Item"], t="mine", via=name, skill="Mining", lvl=lvl, per="hit", ch=p["Chance"], q=[p.get("Quantity", 1)] * 2)
    locs = {l["Id"]: l.get("DisplayName") for l in objects.get("FishingLocation", [])}
    for r in objects.get("FishingRecipe", []):
        skill, lvl = level_req(r.get("Requirements"), names)
        name = r.get("DisplayName") or words(r["Id"])
        where = locs.get(short(r.get("Location")), "")
        fish = min(1, r.get("FishChance", 1))
        for e, w in table_odds(r.get("RandomProducts")):
            book.source(e["Item"], t="fish", via=name, skill="Fishing", lvl=lvl, area=where, per="catch", ch=fish * w,
                        q=[e.get("MinQuantity", 1), e.get("MaxQuantity", 1)])
        if r.get("JunkItem") and fish < 1:
            book.source(r["JunkItem"], t="fish", via=name, skill="Fishing", lvl=lvl, area=where, per="catch", ch=1 - fish,
                        q=[1, 1], label="Fishing junk")
        miss = 1.0          # specials: rarest first, and the first one that hits stops the rest
        for p in sorted(r.get("SpecialProducts") or [], key=lambda x: x["Chance"]):
            c = min(1, p["Chance"])
            book.source(p["Item"], t="fish", via=name, skill="Fishing", lvl=lvl, area=where, per="catch", ch=c * miss,
                        q=[p.get("Quantity", 1)] * 2, label="Fishing special")
            miss *= 1 - c
    species = {x["Id"]: x for x in objects.get("HuntingAnimalSpecies", [])}
    hlocs = {l["Id"]: l.get("DisplayName") for l in objects.get("HuntingLocation", [])}
    for a in objects.get("HuntingAnimal", []):
        skill, lvl = level_req(a.get("Requirements"), names)
        name = a.get("DisplayName") or words(a["Id"])
        where = hlocs.get(short(a.get("Location")), "")
        for p in a.get("Products") or []:
            book.source(p["Item"], t="hunt", via=name, skill="Hunting", lvl=lvl, area=where, per="catch", ch=p["Chance"],
                        q=[p.get("Quantity", 1)] * 2, note="quantity grows with the animal's journal level (about ×0.5 to ×1.3 at first)")
        for p in a.get("SurveyingProducts") or []:
            book.source(p["Item"], t="hunt", via=name, skill="Hunting", lvl=lvl, area=where, per="survey", ch=p["Chance"],
                        q=[p.get("Quantity", 1)] * 2, label="Hunting survey", note="at most one survey find per hunt")
        rare = min(1, a.get("BaseRareSpeciesChance", 0))
        for s_, w in table_odds([{"Item": x["Species"], "Weight": x["Weight"]} for x in a.get("RareSpecies") or []]):
            sp = species.get(short(s_["Item"])) or {}
            for p in sp.get("ExtraProducts") or []:
                book.source(p["Item"], t="hunt", via=f"{name} ({sp.get('DisplayName')})", skill="Hunting", lvl=lvl, area=where,
                            per="catch", ch=rare * w * min(1, p["Chance"]), q=[math.floor(p.get("Quantity", 1))] * 2, label="Rare species")


def openables(objects, names, book, items_by_id):
    for x in objects["Item"]:
        o = (x.get("Capabilities") or {}).get("MelvorBase:Openable")
        if not o:
            continue
        name = names.of("Item", x["Id"])
        link = {"kind": "item", "id": x["Id"]}
        contents = {"always": [], "table": []}
        for a in o.get("AlwaysDrops") or []:
            contents["always"].append({"id": short(a["Item"]), "q": [a["Quantity"]] * 2})
            book.source(a["Item"], t="open", via=name, link=link, per="open", ch=1, q=[a["Quantity"]] * 2)
        for e, w in table_odds(o.get("DropTable")):
            q = [e.get("MinQuantity", 1), e.get("MaxQuantity", 1)]
            contents["table"].append({"id": short(e["Item"]), "ch": w, "q": q})
            book.source(e["Item"], t="open", via=name, link=link, per="open", ch=w, q=q)
        if contents["table"]:
            contents["note"] = "One item from the list per open, plus everything that always comes."
        if x["Id"] in items_by_id:
            items_by_id[x["Id"]]["contents"] = contents


def others(objects, names, book):
    for p in objects.get("ShopPurchase", []):
        prov = p.get("Provides") or {}
        costs = p.get("Costs") or {}
        ing = [{"id": short(i["Item"]), "q": i["Quantity"]} for i in costs.get("Items") or []]
        cur = [{"cur": names.of("Currency", c["Currency"]), "q": c["Quantity"]} for c in costs.get("Currencies") or []]
        skill, lvl = level_req(p.get("Requirements"), names)
        made = [short(i["Item"]) for i in prov.get("Items") or []]
        limit = f"buy limit {p['DefaultBuyLimit']}" if p.get("DefaultBuyLimit") else None
        for i in prov.get("Items") or []:
            book.source(i["Item"], t="shop", via=p.get("DisplayName") or names.of("Item", i["Item"]), per="buy", q=[i["Quantity"]] * 2,
                        ing=ing, cur=cur, skill=skill, lvl=lvl, note=limit)
        for i in costs.get("Items") or []:
            book.use(i["Item"], t="shop", skill="Shop", via=p.get("DisplayName") or "Shop purchase", q=i["Quantity"], makes=made)
    for q in objects.get("Quest", []):
        name = q.get("DisplayName") or words(q["Id"])
        for i in (q.get("Rewards") or {}).get("Items") or []:
            book.source(i["Item"], t="quest", via=name, per="quest", q=[i["Quantity"]] * 2, ch=1)
        for st in q.get("Steps") or []:
            for task in st.get("TasksInOrder") or []:
                for i in (task.get("Costs") or {}).get("Items") or []:
                    book.use(i["Item"], t="quest", skill="Quests", via=name, q=i["Quantity"])
    for u in objects.get("ItemUpgrade", []):
        costs = u.get("Costs") or {}
        ing = [{"id": short(i["Item"]), "q": i["Quantity"]} for i in costs.get("Items") or []]
        cur = [{"cur": names.of("Currency", c["Currency"]), "q": c["Quantity"]} for c in costs.get("Currencies") or []]
        book.source(u["UpgradedItem"], t="upgrade", via="Upgrade", per="upgrade", q=[u.get("QuantityPerUpgrade", 1)] * 2, ch=1, ing=ing, cur=cur)
        for i in costs.get("Items") or []:
            book.use(i["Item"], t="upgrade", skill="Upgrades", via=names.of("Item", u["UpgradedItem"]), q=i["Quantity"], makes=[short(u["UpgradedItem"])])
    paths = {p["Id"]: p for p in objects.get("AgilityCoursePath", [])}
    for c in objects.get("AgilityCourse", []):
        skill, lvl = level_req(c.get("Requirements"), names)
        name = c.get("DisplayName") or words(c["Id"])
        for pid in c.get("Paths") or []:
            p = paths.get(short(pid)) or {}
            for r in p.get("RandomObstacleProducts") or []:
                book.source(r["Item"], t="agility", via=f"{name}, {p.get('DisplayName')}", skill="Agility", lvl=lvl, per="lap",
                            ch=min(1, r["Chance"]), q=[r.get("Quantity", 1)] * 2, note="picked up on the next lap; lost if you fail that obstacle")
        for i in (c.get("CompletionRewards") or {}).get("Items") or []:
            book.source(i["Item"], t="agility", via=name, skill="Agility", lvl=lvl, per="lap", ch=1, q=[i["Quantity"]] * 2)
        for i in (c.get("EntryCost") or {}).get("Items") or []:
            book.use(i["Item"], t="agility", skill="Agility", via=f"Entry to {name}", q=i["Quantity"])
        for tier in sorted(c.get("TimedCompletionRewards") or [], key=lambda t: t["TimeInMS"]):
            for i in tier.get("Items") or []:
                book.source(i["Item"], t="agility", via=name, skill="Agility", lvl=lvl, per="lap", ch=min(1, tier["Chance"]),
                            q=[i["Quantity"]] * 2, note=f"lap in {tier['TimeInMS'] / 1000:.2f}s or less (only the fastest tier you beat is rolled)")
    for e in objects.get("AstrologyItemEvent", []):
        if e.get("Item"):
            book.source(e["Item"], t="event", via=names.of("AstrologyItemEvent", e["Id"]), skill="Astrology", per="study",
                        ch=min(1, e.get("Chance", 0)), q=[e.get("BaseQuantity", 1)] * 2, label="Astrology event",
                        note=f"plus 0 to {e.get('LevelScalingQuantity', 0)} × Astrology level more")
    for e in objects.get("FiremakingEvent", []):
        rw = e.get("Reward") or {}
        if rw.get("Item"):
            book.source(rw["Item"], t="event", via=e.get("DisplayName"), skill="Firemaking", per="log", ch=min(1, e.get("Chance", 0)),
                        label="Firemaking event", note="needs an Astrology major node; the amount grows with the burn time")
    for m in objects.get("SacrificialAnvilRewardMode", []):
        total = (m.get("BonusXpWeight") or 0) + (m.get("BonusSaleValueWeight") or 0) + (m.get("CollectableWeight") or 0)
        share = (m.get("CollectableWeight") or 0) / total if total else 0
        for e, w in table_odds(m.get("Collectables")):
            book.source(e["Item"], t="craft", label=m.get("DisplayName"), via="any Smithing craft", skill="Smithing", per="step", ch=share * w,
                        q=[e.get("MinQuantity", 1), e.get("MaxQuantity", 1)],
                        note="with the Sacrificial Anvil on (an Astrology major node); every step of a craft rolls, and the product turns into GP")
    for m in objects.get("AstralOfferingsRewardMode", []):
        for r in m.get("Rewards") or []:
            book.source(r["Item"], t="craft", label=m.get("DisplayName"), via="Beastcrafting weapons and armour", skill="Beastcrafting",
                        per="craft", q=[r.get("Quantity", 1)] * 2,
                        note=f"recipes of level {r.get('MinLevel')}–{r.get('MaxLevel')}; chance 1 − 0.99^steps per craft, and it replaces the product")
    for a in objects.get("RanchingAnimal", []):
        name = names.of("RanchingAnimal", a["Id"])
        for p in a.get("Products") or []:
            book.source(p["Item"], t="ranch", via=name, skill="Ranching", per="hour", q=[p["Quantity"]] * 2,
                        note="per animal, times its happiness; nothing while it is hungry or unsafe")
        for p in a.get("Eats") or []:
            book.use(p["Item"], t="ranch", skill="Ranching", via=f"Food for {name}", q=p.get("Quantity", 1))
        for i in (a.get("PurchaseCosts") or {}).get("Items") or []:
            book.use(i["Item"], t="ranch", skill="Ranching", via=f"Buying a {name}", q=i["Quantity"])


def build(data):
    objects = data["Objects"]
    names = Names(objects)
    mods = Modifiers(data, names)
    stat_names = {x["Id"]: x for x in objects.get("CombatStat", [])}
    toolbelt_types = {x["Id"]: x for x in objects.get("ToolbeltSlotType", [])}
    items = []
    for x in objects["Item"]:
        if x["Id"] in HIDDEN:
            continue
        rec = item_record(x, names, mods, stat_names, toolbelt_types)
        if x.get("IgnoreCompletion"):
            rec["noCompletion"] = True
        items.append(rec)
    # Equipment sets (game 0.11.7 moved the set bonus out of the item description into its own list): each piece shows its
    # set, the other pieces and the bonus for each number of pieces worn.
    by_id = {i["id"]: i for i in items}
    for s in objects.get("EquipmentSetBonus", []):
        pieces = [short(p) for p in s.get("Items") or []]
        steps = []
        for b in s.get("Bonuses") or []:
            mod = b.get("Modifiers") or {}
            lines = mods.all(mod) + mods.effects(mod.get("CombatEffect"))
            steps.append({"n": b.get("ItemCount"), "lines": lines})
        entry = {"name": s.get("DisplayName") or words(s.get("Id")), "items": pieces, "bonuses": steps}
        for p in pieces:
            if p in by_id:
                by_id[p]["set"] = entry
    book = Book()
    recipes(objects, names, book)
    monsters, areas = combat(objects, names, book)
    thieving(objects, names, book)
    gathering(objects, names, book)
    openables(objects, names, book, {i["id"]: i for i in items})
    others(objects, names, book)
    for it in items:
        it["src"] = book.sources.get(it["id"], [])
        it["use"] = book.uses.get(it["id"], [])
    stats = [{"id": x["Id"], "name": x.get("DisplayName"), "short": x.get("DisplayNameShort"), "cat": short(x.get("Category")),
              "pct": bool(x.get("IsPercent"))} for x in objects.get("CombatStat", [])]
    import datetime
    return {"version": GAME_VERSION, "built": datetime.date.today().isoformat(), "items": items, "stats": stats,
            "monsters": monsters, "areas": areas}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--json", help="MelvorBase.json (default: read it from Melvor2.exe)")
    ap.add_argument("--out", default=OUT)
    ap.add_argument("--no-images", action="store_true", help="skip the item and monster pictures")
    args = ap.parse_args()
    pack = None if args.json else Package(os.path.join(GAME, "Melvor2.exe"))
    raw = open(args.json, "rb").read() if args.json else pack.read("Data/Packages/MelvorBase.json")
    data = json.loads(raw)
    db = build(data)
    if pack and not args.no_images:
        done = export_images(pack, data["Objects"], os.path.join(os.path.dirname(args.out), "img"))
        for i in db["items"]:
            if i["id"] in done["items"]:
                i["img"] = 1
        for m in db["monsters"]:
            if m["id"] in done["monsters"]:
                m["img"] = 1
        print(f"pictures: {len(done['items'])} items, {len(done['monsters'])} monsters")
    # One Style's unique items and rarities (Dev/ItemDb/onestyle.py)
    sys.path.insert(0, HERE)
    import onestyle
    print(f"One Style: {onestyle.add(db, os.path.join(os.path.dirname(args.out), 'img') if not args.no_images else '')} unique items and 3 rarities")
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, "w", encoding="utf-8") as f:
        json.dump(db, f, ensure_ascii=False, separators=(",", ":"))
    print(f"{len(db['items'])} items -> {args.out} ({os.path.getsize(args.out) // 1024} KB)")
    lost = [i["id"] for i in db["items"] if not i["src"]]
    print(f"{len(lost)} items with no known source: {', '.join(lost)}")


if __name__ == "__main__":
    main()
