# The players' review of 2026-11-24 (Codex C1-C30, Gemini G1-G35): flavour lines for the 24 uniques without one,
# clearer mechanic texts, two stats that contradicted their item's power, and the weak powers made more fun.
# Run once, then uniquegen.py. Kept for the record of what changed and why.
import json, os
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'uniques-en.json')
e = json.load(open(P, encoding='utf-8'))

FLAVOUR = {
 "Threevoice Cleaver": "Three throats, one blade, and none of them ever agreed.",
 "Root-Shear Claws": "It cut the roots first, so nothing could hold on.",
 "Thornstep Spinecoat": "Tavio never stopped dancing; the thorns kept time.",
 "Tollkeeper Greaves": "Everyone pays the toll. Some just pay it slower.",
 "Sunken Volley": "Two arrows to a string, drowned and still drawn.",
 "Twin-Eye Longwatch": "One eye to find you, one eye to never miss.",
 "Conclave Spire": "Four voices argue; the right one always wins.",
 "Sanguine Liturgy": "Runes are a poor man's blood.",
 "Plungeheart Tusk": "Cold enough to stop a heart, sharp enough to break it.",
 "Hollow Harvest Crown": "Empty inside, and harder for it.",
 "Veil-Antler Reprieve": "The stag walked out of the veil once. It can show you the way.",
 "Threefold Decree": "Urzath ruled three ways at once, and every way was his.",
 "Ringleader's Tally": "He never counted the pieces, only the sets.",
 "Amber Reservoir": "A drop of venom, kept forever in gold.",
 "Dune-Vizier Wrappings": "The sand keeps prayers the way it keeps the dead.",
 "Oathbound Gauntlets": "An oath sworn twice is never broken.",
 "Abyssal Beacon": "Light in the deep does not guide. It calls.",
 "Scrapwind Treads": "Baskor walked where the hunt was, and took what was left.",
 "Dreamtide Orb": "She sleeps; the tide does the rest.",
 "Ossuary Magazine": "The bones always come back to the quiver.",
 "Brood-Sign Grips": "A thousand eggs, and every one hatches hungry.",
 "Hemal Palimpsest": "Written over and over, in the same red ink.",
 "Mirage Astrolabe": "It points where the stars should be.",
 "Caravan Warcloak": "Zafir's road had three turns: stab, slash or crush.",
}
MECHANIC = {
 "Threevoice Cleaver": ("Three Voices", "After every 2 successful hits, your next attack deals +35% damage and the enemy's evasion is 30% lower for it (a miss does not use it up; it starts over each fight)."),
 "Tollkeeper Greaves": ("Last Breath", "Your HP cannot go above 50% of your max HP (auto-eat still uses your full max HP). 15% of the direct damage you take is given back and taken over 4 s instead; that delayed damage cannot kill you."),
 "Pinnacle Diadem": ("Stolen Bulwark", "Your first successful hit in each fight also takes up to 2 hits of the enemy's ward (besides its normal damage) and gives you 2 hits of ward for 10 s, or 4 if the enemy had any ward."),
 "Amber Reservoir": ("Amber Vault", "Combat potions in your toolbelt are never used up, and their stat bonuses (damage, accuracy, evasion, protection, healing...) are 1.5 times; chances, intervals and durations stay the same."),
 "Conclave Spire": ("Council's Voice", "Magic only. At the start of each fight your spell's element switches to the enemy's weakness (the spell's effects and rune costs stay; nothing changes against Neutral enemies). Hits with elemental advantage deal +15% damage on top of the normal bonus."),
 "Wreckshot": ("Sunken Reckoning", "Grows with your Champion and Unique kills while you wear it (kept with the character); each step adds to the ones before: 25 = +10% damage, 50 = +6% crit chance, 75 = -8% attack interval, 100 = +25% damage against elites, 125 = +1 loot roll per elite kill."),
 "Thornstep Spinecoat": ("Thorn Dance", "The damage you reflect hits 2.5 times (it can crit) and has a 25% chance to Bleed for 30% of it over 4 s (3 stacks at most); these hits trigger no other items. Your own attacks deal 10% less damage."),
 "Threefold Decree": ("Three Jaws' Edict", "The combat triangle always favours you, with One Style's class too (which otherwise turns the triangle off): you get its advantage (+10% max hit and less damage taken) against every enemy and never its penalty."),
 "Scrapwind Treads": ("Scrap Hunter", "On a Slayer task each task monster you kill gives +1% damage and makes your rare item chance 1% higher (up to +30%, so 2% a kill becomes 2.6%), and task monsters come 30% faster. When the task ends the stacks go and you keep 1 token (1 at most): your next elite kill drops one more Rare (3-affix) item."),
 "Caravan Warcloak": ("Caravan Road", "Melee only, by stance: Stab +30% accuracy; Slash 20% chance on hit to cause Bleed; Crush stuns for 1 turn on every 4th successful hit (once in 8 s). Nothing in Block stance, or with ranged or magic."),
 "Hemal Palimpsest": ("Fifth Seal", "Magic only. Every 5th spell is followed by a free echo: one more hit of your spell (its own hit roll, can crit)."),
 "Mire Throat": ("Bog Gullet", "+8% lifesteal, but food heals 40% less."),
 # the weak ones, made more fun
 "Caravan Seals": ("Four-Seal Passage", "Every 3 normal kills (counted across fights) readies a Passage Seal (1 at most). When a Champion or Unique fight starts the seal is spent, and your first 5 successful hits on that enemy gain +30% crit chance; misses don't use them and unused ones are lost when the fight ends."),
 "Breaker Hooves": ("Eighth Wave", "Every 6 successful hits in a fight remove one damage-over-time effect from you with all its stacks: Poison first, then Bleed, then Burn. With none on you, the wave heals you for 2% of your max HP instead."),
 "Gatebreaker": ("Gate's Verdict", "+40% damage while the current enemy is a Champion or Unique, on top of this item's +35% max hit. Breaking one opens the gate: your next fight starts with +40% damage for 10 s."),
}
STATS = {
 # never used up: the ammo-keeping line was dead weight
 "Ossuary Magazine": ["+10% PercentMaxHit", "+5% FlatCriticalHitChance"],
 # food heals less with its power: no "healing from food" line next to it
 "Mire Throat": ["+6% PercentMaxHitpoints", "+4% PercentMaxHit"],
}
for name, text in FLAVOUR.items():
    assert name in e, name
    e[name]["Flavour"] = text
for name, (mech_name, text) in MECHANIC.items():
    assert name in e, name
    e[name]["MechanicName"] = mech_name
    e[name]["Mechanic"] = text
for name, lines in STATS.items():
    e[name]["Stats"] = lines
json.dump(e, open(P, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(FLAVOUR), 'flavours,', len(MECHANIC), 'texts')
