using System.Collections.Generic;
using System.Linq;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using Godot;

namespace OneStyle;

/// <summary>What a monster's Normal, Champion and Unique versions drop of rare items, for the Elites window.</summary>
public static partial class RareItems
{
    public static void HookWindow() => Elites.DropLines = DropLines;

    /// <summary>The item level a drop of this monster gets now: +5 while it is your Slayer task, as the real drop.</summary>
    public static int PreviewItemLevel(Enemy enemy) => enemy.CombatLevel + (Elites.OnSlayerTask(enemy) ? 5 : 0);

    /// <summary>Which game items a rare drop is made from: not the monster's own drops, any gear of about its level.</summary>
    public static string PoolLine(int iLvl, bool inDrops = false)
    {
        if (s_game is not { } game) return "";
        List<Item> pool = Window(game, iLvl, DropStyle());
        if (pool.Count == 0) return "";
        int lo = pool.Min(PoolLevelOf), hi = pool.Max(PoolLevelOf);
        var slots = pool.Select(SlotOf).Distinct().OrderBy(x => x).ToList();
        string style = DropStyle() != "" ? $" Weapons only of your class's style ({Patches.Current})." : "";
        string level = lo == hi ? $"level {lo}" : $"level {lo}-{hi}";
        return $"Made from one of {pool.Count} game items of {level} ({string.Join(", ", slots)}), not from this monster's own drops: "
             + $"each of the {slots.Count} slots is equally likely, then each item in that slot.{style}"
             + (inDrops ? "" : " The list is in this monster's drops (the chest button).");
    }

    static IEnumerable<(string Text, Color Color, Texture2D? Icon)> DropLines(Enemy enemy, EliteTier tier)
    {
        int iLvl = PreviewItemLevel(enemy);
        int best = BestTier(iLvl);
        string task = iLvl > enemy.CombatLevel ? " (+5: your Slayer task)" : "";
        switch (tier)
        {
            case EliteTier.Normal:
                yield return ($"Normal kill: 1% chance for 1 Magic item (2 affixes) and 1% for 1 Rare item (80% with 3 affixes, 20% with 4). Item level {iLvl}{task}: affix tiers up to T{best} (T1 is the best).", Colors[Rarity.Rare], null);
                yield return (PoolLine(iLvl), new Color(0.85f, 0.85f, 0.85f), null);
                break;
            case EliteTier.Champion:
                yield return ($"1 item: Magic (2 affixes) 60%, Rare (3 affixes) 33%, Rare (4 affixes) 7%. On your Slayer task or as a dungeon's last boss every drop gets +5 item level, and a Champion or Unique drops 1 more item with these chances.",
                    Colors[Rarity.Magic], null);
                break;
            case EliteTier.Unique:
                yield return ("2 items: 1 Rare (80% with 3 affixes, 20% with 4), plus 1 with a Champion's chances.", Colors[Rarity.Rare], null);
                if (Uniques.ForMonster(enemy.Id) is { } u)
                {
                    double chance = Uniques.ChanceNow(u);
                    yield return ($"Unique item: {u.Name} ({u.Slot}). Chance: {chance * 100:0}% on every Unique kill.",
                        Colors[Rarity.Unique], Uniques.IconOf(u));
                    foreach (UniqueStat s in u.Stats) yield return ("   " + s.Text, new Color(1f, 0.78f, 0.55f), null);
                    if (u.MechanicName != "") yield return ($"   {u.MechanicName}: {u.Mechanic}", new Color(1f, 0.78f, 0.55f), null);
                }
                break;
        }
    }
}
