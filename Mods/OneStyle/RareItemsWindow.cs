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
                yield return ($"Rare drops: Magic 1%, Rare 1% a kill  ·  item level {iLvl}{task}, tiers up to T{best}", Colors[Rarity.Rare], null);
                break;
            case EliteTier.Champion:
                yield return ("Rare drops: 1 item (Magic 60%, Rare 40%)", Colors[Rarity.Magic], null);
                break;
            case EliteTier.Unique:
                yield return ("Rare drops: 2 items (1 Rare, 1 as a Champion's)", Colors[Rarity.Rare], null);
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
