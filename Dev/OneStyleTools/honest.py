# Texts made to say what the code does (release review 2026-11-25, Ortak-Calisma/hack-and-slash/yayin-hazirlik/karar.md 7):
# the three powers that lower your damage do it through the game's "damage dealt" stat, which the game also uses for the
# damage you reflect and for its own poison, bleed and burn. Run once, then uniquegen.py.
import json, os
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'uniques-en.json')
e = json.load(open(P, encoding='utf-8'))
NEW = {
 "Root-Shear Claws":
    "25% chance on hit to Bleed for 30% of the hit's damage over 4 s (max 5 stacks); each bleed tick can crit. Hitting a bleeding enemy refreshes your bleeds. "
    "While worn, your damage is 20% lower: your hits, the damage you reflect and the game's own poison, bleed and burn (not this bleed).",
 "Thornstep Spinecoat":
    "The damage you reflect hits 2.5 times (it can crit) and has a 25% chance to Bleed for 30% of it over 4 s (3 stacks at most); these hits trigger no other items. "
    "While worn, your damage is 10% lower: your attacks, the game's own poison, bleed and burn, and the reflected hit before it is multiplied.",
 "Sunken Volley":
    "Each attack fires a second arrow: both deal 60% damage. The second uses one more ammo and rolls its own hit and crit. "
    "While worn, the damage you reflect and the game's own poison, bleed and burn are 40% lower too.",
}
for name, text in NEW.items():
    assert name in e, name
    e[name]["Mechanic"] = text
json.dump(e, open(P, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(NEW), 'texts changed')
