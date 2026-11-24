# The eight Uniques of the build-gap round (2026-11-24, Ortak-Calisma/hack-and-slash/yeni-uniqueler/karar.md): their English
# power names, texts and stories in uniques-en.json, exactly as UniquePowers.Powers3.cs does them. Run once, then uniquegen.py
# (which reads the design records from yeni-uniqueler/yeni-esyalar.json).
import json, os
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'uniques-en.json')
e = json.load(open(P, encoding='utf-8'))
NEW = {
 "Twin-Lurker Fangs": ("Twin Strike",
    "On hit, 25% chance to strike once more for 50% of a normal hit (it rolls its own accuracy and critical hit, and triggers nothing else).",
    "Skarn never needed a second blade. He just liked the second wound."),
 "Bell-Yoke Wand": ("Bell Toll",
    "Magic only. Every 3rd spell you cast (misses count) is followed by a bell strike for 35% of a normal hit of your spell (it rolls its own accuracy and critical hit, and triggers nothing else).",
    "The herd came home to the bell. So did everything else."),
 "Resin Hoard Ring": ("Resin Shell",
    "When food restores your hit points, you gain 2 more hits of ward for 6 s: the next 2 hits you take go through it and do less damage. At most once every 8 s; your other ward stays.",
    "Ploxx kept everything it swallowed. This ring was the least sticky part."),
 "Divide Sentinel's Aegis": ("Sentinel's Answer",
    "Melee only. Every 3rd enemy attack that hits you readies one Answer (only one is kept; a new one at most every 8 s from when the last was readied): your next successful melee hit deals +40% damage and has a 20% chance to stun for 1 turn.",
    "The mountain does not dodge. It waits, and then it answers."),
 "Talon of the Divide": ("Rising Edge",
    "After each successful hit your critical hit chance is rolled again, on its own: on a success you gain an Edge (3 at most; each new Edge renews them all to 15 s), +10% critical damage each. With 3 Edges your next attack is a sure critical hit with +80% critical damage in all (the Edges' +30% and 50% more), and the Edges are spent.",
    "Kethra struck once to mark you, and once more to finish it."),
 "Divide-Fang Howler": ("Pack Howl",
    "On hit, 25% chance to gain 1 stack of the game's Rage (Rage from anything else counts too). Each stack: +2% damage, up to +10%. At 5 stacks your next successful hit howls: it uses up all your Rage and stuns the enemy for 1 turn. A howl at most every 10 s; while it waits your Rage stays and keeps its bonus.",
    "Kaul's pack is long dead. The howl still answers."),
 "Veinbound Chalice": ("Overflow",
    "+5% lifesteal. Lifesteal healing your full hit points cannot take is stored as Blood (up to 20% of your max hit points). When you drop below 40% hit points, all the Blood heals you at once. While the Blood is full you deal +15% damage.",
    "Sareth never spilled a drop. She saved them for later."),
 "Barrens Hush": ("Hush",
    "Each successful hit, on its own, has a 12% chance to Blind the enemy for 4 of its turns and an 8% chance to Wither it for 6 of its turns (the game's own Blind and Wither). You deal +15% damage to an enemy that is Blinded or Withered, from any source; it does not add up when it has both.",
    "You never hear Osvyr coming. That is the point."),
}
for name, (mech_name, text, flavour) in NEW.items():
    e[name] = {"MechanicName": mech_name, "Mechanic": text, "Flavour": flavour}
json.dump(e, open(P, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(NEW), 'new uniques;', len(e), 'in uniques-en.json')
