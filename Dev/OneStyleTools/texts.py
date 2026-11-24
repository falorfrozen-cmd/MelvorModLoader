# The mechanic texts of the unique items, exactly as One Style 0.4 does them (UniquePowers*.cs).
import json, os
P = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'uniques-en.json')
e = json.load(open(P, encoding='utf-8'))
NEW = {
 "Thornshot": "Your first hit in each fight always Staggers, then 15% chance on hit (at most once every 6 s): the enemy's next attack comes 25% of its attack interval later.",
 "Hoarder's Prism": "When a monster drops an item whose drop chance is 5% or less, gain +25% damage and +15% chance of rare items for 90 s. Triggering again refreshes the time; it does not stack.",
 "Mire Throat": "+8% lifesteal, but food heals 40% less.",
 "Veil Rebuttal": "Magic only. 15% chance that an enemy attack does no damage at all, and you strike back with a free hit of your spell (it cannot crit and triggers no items).",
 "Oathlocked Plate": "Above half your hit points you take 15% less damage; at half or below you deal 15% more damage.",
 "Wreckshot": "Grows with your Champion and Unique kills while you wear it (kept with the character): 25 = +10% damage, 50 = +6% crit chance, 75 = -8% attack interval, 100 = +25% damage against elites, 125 = +1 loot roll per elite kill.",
 "Pinnacle Diadem": "Your first successful hit in each fight strips 2 hits of the enemy's ward and gives you 2 hits of ward for 10 s (4 if it had ward). The strip does no HP damage.",
 "Thunder Traces": "Every 4th successful hit in a fight records 70% of the HP it removed (capped at 15% of the enemy's max HP). 2 s later, if the same enemy is still alive, that amount strikes again; it cannot crit.",
 "Usurper's Circle": "Copies the bonuses (not the equipment stats) of the ring in its paired ring slot (1-2, 3-4, 5-6). Unique rings are not copied; two Usurper's Circles copy nothing.",
 "Soulstring Quiver": "Ranged only. Never used up when you shoot. Every 5th successful ranged attack (every 4th against elites) fires a free extra shot at 75% damage that can crit.",
 "Fourfold Writ": "Magic only. Every 4th spell keeps its runes and prayer points and opens the next seal in order: 1) +40% damage on that spell; 2) Burn for 3% of your max hit per second for 5 s; 3) heal 5% of your max HP; 4) the enemy's next attack deals 50% less damage. The cycle is kept between fights.",
 "Threevoice Cleaver": "Every 3rd successful hit deals +35% damage, and the enemy's evasion is 30% lower for it.",
 "Sunken Volley": "Each attack fires a second arrow: both deal 60% damage. The second uses one more ammo and rolls its own hit and crit.",
 "Twin-Eye Longwatch": "+60% attack interval, but your shots almost never miss and hit for at least 80% of your max hit. The first shot of each fight is a guaranteed crit.",
 "Conclave Spire": "Magic only. At the start of each fight your spell's element switches to the one strongest against the enemy (the spell's effects and rune costs stay). Advantaged hits deal +15% damage.",
 "Sanguine Liturgy": "Spells keep their runes (you still need one cast's runes in the bank); each cast costs 3% of your current HP instead (never below 1 HP). With magic, +1% damage per 2% of missing HP (max +30%).",
 "Hollow Harvest Crown": "Your max hit points are 35% lower, and you gain +6 ward. Your ward is full at the start of each fight, regains 1 every 4 s (not for 2 s after you are hit), and eating gives 1 back (once in 2 s).",
 "Veil-Antler Reprieve": "Once every 3 minutes of fighting (the wait is kept when you stop or restart): when you drop below 20% HP or take a fatal blow you survive, heal 40% of your max HP, enemy attacks do no damage for 3 s, and you gain +30% damage and -15% attack interval for 15 s.",
 "Amber Reservoir": "Combat potions in your toolbelt are never used up, and the helpful numbers of their bonuses are 1.5 times (chances, intervals and durations stay the same).",
 "Dune-Vizier Wrappings": "Prayers cost 50% fewer points (at least 1) and their bonuses are 30% stronger. Each kill gives 15 prayer points.",
 "Oathbound Gauntlets": "Every on-hit effect of your gear that does not happen on a hit gets a second roll (chance 1-(1-p)^2).",
 "Abyssal Beacon": "Twice the chance of Champions and Uniques (with your other elite bonuses, at most 2.5 times). Every elite gets one more affix and +20% HP, and drops one more Rare (3-affix) item.",
 "Scrapwind Treads": "On a Slayer task each task monster you kill gives +1% damage and +1% chance of rare items (30 at most), and task monsters come 30% faster. When the task ends the stacks go and you bank a token: your next elite kill drops one more Rare (3-affix) item.",
 "Ossuary Magazine": "Ranged only. Never used up when you shoot. Every 4th ranged attack deals double damage.",
 "Hemal Palimpsest": "Magic only. Every 5th spell is followed by a free echo: one more hit of your spell (its own hit roll, can crit).",
 "Mirage Astrolabe": "Your spell imbuement's bonuses are 80% stronger; on every 6th spell they are 2.7 times for that spell.",
 "Caravan Warcloak": "Depends on your stance: Stab +30% accuracy; Slash 20% chance on hit to Bleed; Crush stuns for 1 turn on every 4th hit (8 s cooldown).",
 "Lantern Veil": "20% chance on hit to Burn the enemy for 4% of that hit's damage per second for 4 s. Killing an enemy that carries a Poison, Bleed or Burn grants a Ration Light charge (max 1, kept between fights): your next meal is not used up.",
}
for name, text in NEW.items():
    assert name in e, name
    e[name]['Mechanic'] = text
json.dump(e, open(P, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(NEW), 'texts')
