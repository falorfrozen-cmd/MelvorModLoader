# Writes One Style's data/affixes.json: every rare item affix with its tiers (T5 weakest .. T1 best), the slots it can
# roll on and the bonus in the game's own format, where the value placeholder "{v}" is replaced by the rolled value.
import json, os

OUT = r"C:\Program Files (x86)\Steam\steamapps\common\Melvor Idle 2\MelvorModLoader_src\Mods\OneStyle\data\affixes.json"
W, OH, HE, TO, LE, GL, BO, CA, AM, RI, AMMO = "Weapon", "Offhand", "Helmet", "Torso", "Legs", "Gloves", "Boots", "Cape", "Amulet", "Ring", "Ammo"


def comp(stats_type, name, component="Player", entity="MelvorBase:Combat", condition=None, **ctx):
    lc = {"Component": {"Entity": entity, "Component": component}}
    lc.update({k: v for k, v in ctx.items()})
    return {"Stat": {"StatsType": "MelvorBase:" + stats_type, "Name": name}, "Value": "{v}", "FromContext": None,
            "Condition": condition, "Scaling": None, "LocalContext": lc}


def effect(kind, eff, config, trigger, trigger_data, applies="Target"):
    return {"Character": {"Entity": "MelvorBase:Combat", "Component": "Player"},
            "Modifier": {"Effect": {"Type": "MelvorBase:" + kind, "Data": {"Effect": "MelvorBase:" + eff, "Configuration": config}},
                         "Trigger": {"Type": "MelvorBase:" + trigger, "Data": trigger_data}, "AppliesTo": applies},
            "Chance": "{v}", "Condition": None, "Scaling": None}


def glob(group):
    return {"StatGroup": group, "Value": "{v}", "Condition": None, "Scaling": None}


ON_HIT = {"Type": "OnHit"}
WHEN_HIT = {"Type": "WhenHit"}
SLAYER = {"Type": "MelvorBase:FightingSlayerTask", "Data": {"IsOr": False, "Component": {"Entity": "MelvorBase:Slayer", "Component": "Tasks"}}}
COMBAT_SKILLS = ["Attack", "Strength", "Defence", "Hitpoints", "Ranged", "Magic"]

A = []


def add(id, kind, name, text, tiers, slots, scale, component=None, combat_effect=None, glob_=None, custom=None, group="", sign=1):
    """tiers: shown values T5..T1 (what the player reads, e.g. 9 for +9%); scale turns them into the game's value."""
    A.append({"Id": id, "Kind": kind, "Name": name, "Text": text, "Tiers": tiers, "Slots": slots, "Scale": scale * sign,
              "Bonuses": {"Global": glob_ or [], "Component": component or [], "CombatEffect": combat_effect or []},
              "Custom": custom or "", "Group": group})


# ---------------------------------------------------------------- prefixes
add("Keen", "P", "Keen", "+{v}% max hit", [3, 6, 9, 12, 16], [W, GL, RI, AM, AMMO, CA], 0.01, [comp("Character", "PercentMaxHit")])
add("Swift", "P", "Swift", "-{v}% attack interval", [2, 4, 5, 7, 9], [W, GL, BO, LE, CA], 0.01, [comp("Character", "PercentAttackInterval")], sign=-1)
add("True", "P", "True", "+{v}% accuracy", [5, 9, 13, 18, 24], [W, HE, GL, RI, CA, OH], 0.01, [comp("Character", "PercentAccuracyRating")])
add("Deadly", "P", "Deadly", "+{v}% critical hit chance", [2, 3, 4, 6, 8], [W, GL, RI, AM, AMMO], 0.01, [comp("Character", "FlatCriticalHitChance")])
add("Brutal", "P", "Brutal", "+{v}% critical hit damage", [10, 15, 22, 30, 40], [W, AM], 0.01, [comp("Character", "FlatCriticalHitMultiplier")])
add("Steady", "P", "Steady", "+{v}% minimum hit (of the max hit)", [3, 5, 7, 9, 12], [W, GL, OH, BO], 0.01, [comp("Character", "MinHitBasedOnMaxHit")])
add("Slayers", "P", "Slayer's", "+{v}% max hit against Champions and Uniques", [6, 10, 14, 19, 25], [W, HE, RI, TO, CA], 0.01, custom="EliteDamage")
add("Leeching", "P", "Leeching", "{v}% lifesteal", [1, 2, 3, 4, 5], [W, AM, RI], 0.01,
    [comp("Character", "FlatCombatStatValue", CombatStat="MelvorBase:Lifesteal")])
add("Thorned", "P", "Thorned", "reflects {v}% of the damage taken", [3, 5, 8, 11, 15], [TO, OH, LE], 0.01, [comp("Character", "PercentAttackReflect")])
add("Serrated", "P", "Serrated", "{v}% chance on hit to cause Bleed", [5, 6, 8, 10, 12], [W, GL], 0.01,
    combat_effect=[effect("DamageOverTime", "Bleed", {"StacksToAdd": None}, "OnAttack", ON_HIT)], group="OnHitDot")
add("Venomous", "P", "Venomous", "{v}% chance on hit to Poison", [5, 6, 8, 10, 12], [W, AMMO, GL], 0.01,
    combat_effect=[effect("DamageOverTime", "Poison", {"StacksToAdd": None}, "OnAttack", ON_HIT)], group="OnHitDot")
add("Searing", "P", "Searing", "{v}% chance on hit to Burn", [5, 6, 8, 10, 12], [W, GL], 0.01,
    combat_effect=[effect("DamageOverTime", "Burn", {"StacksToAdd": None}, "OnAttack", ON_HIT)], group="OnHitDot")
add("Stalwart", "P", "Stalwart", "-{v}% damage taken", [1, 2, 3, 4, 5], [TO, OH, HE], 0.01, [comp("Character", "PercentDamageTaken")], sign=-1)
# the game has no "enemy has an effect" condition (an effect context means the effect itself): done in code (Elites.Vicious)
add("Vicious", "P", "Vicious", "+{v}% max hit against a poisoned, bleeding or burning enemy", [5, 9, 14, 20, 28], [GL, LE, CA, W], 0.01, custom="Vicious")
add("Executing", "P", "Executing", "+{v}% max hit against an enemy below 35% hit points", [6, 11, 16, 22, 30], [W, BO, CA, RI], 0.01, custom="Execute")
add("Shattering", "P", "Shattering", "{v}% chance on hit to Fracture the enemy's armour", [5, 8, 11, 14, 18], [W, GL, AMMO], 0.01,
    combat_effect=[effect("TurnCounting", "Fracture", {"Turns": None}, "OnAttack", ON_HIT)])
add("Raging", "P", "Raging", "{v}% chance on hit to gain a stack of Rage", [10, 15, 20, 25, 30], [W, GL, RI], 0.01,
    combat_effect=[effect("Stacking", "Rage", {"StacksToAdd": None}, "OnAttack", ON_HIT, applies="Self")])
add("Reinforced", "P", "Reinforced", "+{v} armour", [15, 30, 50, 75, 100], [TO, LE, HE, BO, OH], 1,
    [comp("Character", "FlatCombatStatValue", CombatStat="MelvorBase:Armour")])
add("Warding", "P", "Warded", "+{v} ward (hits it takes for you)", [1, 1, 2, 2, 3], [OH, TO, HE, AM], 1,
    [comp("Character", "FlatCombatStatValue", CombatStat="MelvorBase:MaximumWard")])

# ---------------------------------------------------------------- suffixes
add("Bear", "S", "of the Bear", "+{v}% max hit points", [3, 5, 8, 11, 15], [TO, LE, HE, AM, OH], 0.01, [comp("Character", "PercentMaxHitpoints")])
add("Wall", "S", "of the Wall", "+{v}% protection", [4, 8, 12, 17, 22], [TO, LE, HE, BO, OH], 0.01, [comp("Character", "PercentProtectionRating")])
add("Fox", "S", "of the Fox", "+{v}% evasion", [5, 9, 13, 18, 24], [BO, CA, LE, GL], 0.01, [comp("Character", "PercentEvasionRating")])
add("Rock", "S", "of the Rock", "{v}% chance to shrug off a stun", [5, 9, 13, 18, 25], [HE, BO], 0.01,
    [comp("CombatEffect", "AvoidanceChance", Effect="MelvorBase:Stun")])
add("Cleansing", "S", "of Cleansing", "{v}% chance to avoid Poison, Bleed and Burn", [5, 9, 13, 18, 25], [CA, AM, TO, W], 0.01,
    [comp("CombatEffect", "AvoidanceChance", Effect="MelvorBase:" + e) for e in ("Poison", "Bleed", "Burn")])
add("Warding2", "S", "of Warding", "{v}% chance to take no damage from a hit", [1, 2, 3, 4, 5], [OH, TO], 0.01, [comp("Character", "AttackDamageNegationChance")])
add("Mending", "S", "of Mending", "+{v}% healing from food", [3, 5, 8, 11, 15], [AM, RI, CA], 0.01, [comp("CombatFood", "PercentHealing", component="Food")])
add("Thrift", "S", "of Thrift", "{v}% chance to keep the food you eat", [2, 4, 6, 8, 10], [CA, GL, AM], 0.01, [comp("CombatFood", "FoodPreservationChance", component="Food")])
add("Runes", "S", "of Runes", "{v}% chance to keep runes", [6, 12, 18, 25, 35], [W, OH, CA], 0.01, [comp("MagicSpell", "RunePreservationChance", component="MagicSpells")])
add("Quiver", "S", "of the Quiver", "{v}% chance to keep ammo", [5, 10, 15, 20, 25], [W, AMMO, CA], 0.01, [comp("AmmoConsumption", "AmmoPreservationChance")])
add("Devotion", "S", "of Devotion", "{v}% chance to keep prayer points", [3, 6, 9, 12, 15], [HE, AM, W], 0.01, [comp("Prayer", "CostPreservationChance", component="Prayer")])
add("Plenty", "S", "of Plenty", "{v}% chance to double combat loot", [1, 2, 3, 4, 5], [RI, AM, HE], 0.01, [comp("CombatLoot", "ItemDoublingChance", component="Loot")])
add("Coin", "S", "of Coin", "{v}% chance to double gold from monsters", [10, 18, 28, 40, 55], [RI, GL], 0.01, [comp("CombatLoot", "CurrencyDoublingChance", component="Loot")])
add("Wisdom", "S", "of Wisdom", "+{v}% combat XP", [2, 3, 4, 6, 8], [AM, RI, HE, W, AMMO], 0.01,
    [comp("Experience", "PercentXPGained", component="Experience", entity="MelvorBase:" + s) for s in COMBAT_SKILLS])
add("Haste", "S", "of Haste", "-{v}% time until the next monster", [5, 8, 11, 15, 20], [BO, CA, W], 0.01, [comp("CombatAction", "PercentSpawnInterval", component="Action")], sign=-1)
add("Embers", "S", "of Embers", "+{v}% Ember from elites", [5, 10, 15, 20, 25], [RI, AM, CA, W], 0.01, custom="Ember")
add("Calling", "S", "of Calling", "+{v}% chance of Champions and Uniques", [3, 5, 8, 11, 15], [CA, HE], 0.01, custom="Calling")
add("Mastery", "S", "of Mastery", "+{v}% power with {style}", [5, 10, 15, 22, 30], [W, OH, CA, AMMO], 0.01,
    [comp("Character", "PercentPowerRating", CombatStyle="{style}")])
add("Momentum", "S", "of Momentum", "-{v}% attack interval for 4 s after a kill", [3, 5, 7, 10, 13], [W, BO, GL], 0.01, custom="Momentum")
add("Concussion", "S", "of Concussion", "{v}% chance on hit to Stun for a turn", [2, 3, 4, 5, 6], [W, GL, OH], 0.01,
    combat_effect=[effect("Stun", "Stun", {"Turns": 1}, "OnAttack", ON_HIT)], group="OnHitStun")
add("Frost", "S", "of Frost", "{v}% chance on hit to Freeze for a turn", [1, 1.5, 2, 3, 4], [W, AMMO, AM], 0.01,
    combat_effect=[effect("Stun", "Freeze", {"Turns": 1}, "OnAttack", ON_HIT)], group="OnHitStun")
add("Blinding", "S", "of Blinding", "{v}% chance on hit to Blind for 2 turns", [6, 9, 12, 16, 20], [GL, AMMO, OH, HE], 0.01,
    combat_effect=[effect("RandomMagnitude", "Blind", {"Turns": 2, "MinStacks": None, "MaxStacks": None}, "OnAttack", ON_HIT)])
add("Withering", "S", "of Withering", "{v}% chance on hit to Wither the enemy", [3, 5, 7, 9, 12], [W, AM, RI], 0.01,
    combat_effect=[effect("TurnCounting", "Wither", {"Turns": 3}, "OnAttack", ON_HIT)])
add("Retaliation", "S", "of Retaliation", "{v}% chance when hit to make the attacker Bleed", [5, 8, 11, 14, 18], [TO, OH, HE], 0.01,
    combat_effect=[effect("DamageOverTime", "Bleed", {"StacksToAdd": None}, "WhenAttacked", WHEN_HIT)])
add("Bulwark", "S", "of the Bulwark", "{v}% chance to start a fight Protected (the first hit is blocked)", [20, 35, 50, 65, 85], [OH, TO], 0.01,
    combat_effect=[effect("AttackCounting", "Protected", {"Attacks": 1}, "AtStartOfFight", {}, applies="Self")])
add("SlayerTask", "S", "of the Hunt", "+{v}% max hit on your slayer task", [3, 5, 7, 9, 12], [HE, AM, CA, W], 0.01,
    [comp("Character", "PercentMaxHit", condition=SLAYER)])
add("Feasting", "S", "of Feasting", "+{v}% auto-eat efficiency", [3, 5, 7, 10, 14], [LE, AM, CA], 0.01, [comp("CombatFood", "FlatAutoEatEfficiency", component="Food")])
add("Learning", "S", "of Learning", "+{v}% XP in every skill", [1, 2, 3, 4, 5], [RI, AM, CA], 0.01, glob_=[glob("GlobalPercentXpGained")])
add("Diligence", "S", "of Diligence", "-{v}% action time in every skill", [1, 2, 3, 4, 5], [RI, AM, CA], 0.01, glob_=[glob("GlobalPercentActionInterval")], sign=-1)

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    json.dump(A, f, indent=1)
print(len(A), "affixes:", sum(a["Kind"] == "P" for a in A), "prefixes,", sum(a["Kind"] == "S" for a in A), "suffixes")
