using System;
using System.Collections.Generic;
using System.Linq;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatLoot;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;

namespace OneStyle;

public static partial class Elites
{
    /// <summary>What the last elite kill gave (tests and the window).</summary>
    public static string LastReward { get; private set; } = "";
    public static long LastEmber { get; private set; }
    public static int LastRolls { get; private set; }

    /// <summary>
    /// A monster died. Online kills count for the pity and the trophies; an elite pays its rewards (karar.md 3):
    /// more rolls of the monster's own loot (all of them added at once, K2), gold for monsters without loot, combat XP
    /// (already a bonus while it lived), Ember and its trophy. Hooked on Killed, not on the game's loot event, which
    /// dungeons never send.
    /// </summary>
    static void OnKilled(object? sender, EnemyKilledEventArgs e) => Safe("OnKilled", () => OnKilledSafe(sender, e));

    static void OnKilledSafe(object? sender, EnemyKilledEventArgs e)
    {
        if (s_combat == null || Offline()) return;
        Momentum();
        string id = e.Enemy.Id;
        bool special = Special(e.Enemy);
        if (InfoOf(id) == null)
        {
            RareItems.OnKilled(s_game!, e.Enemy, EliteTier.Normal, special);
            return;
        }
        int[] kills = s_progress.KillsOf(id);
        s_dirty = true;
        if (Tier == EliteTier.Normal || !ReferenceEquals(e.Enemy, Enemy) || s_killed)
        {
            kills[0]++;
            s_progress.SinceUnique[id] = s_progress.SinceUnique.GetValueOrDefault(id) + 1;
            RareItems.OnKilled(s_game!, e.Enemy, EliteTier.Normal, special);
            return;
        }
        s_killed = true;
        try { Reward(e.Enemy, kills); }
        catch (Exception ex) { Log("One Style: an elite's reward failed: " + ex.Message); }
        RareItems.OnKilled(s_game!, e.Enemy, Tier, special);
    }

    static void Reward(Enemy enemy, int[] kills)
    {
        bool unique = Tier == EliteTier.Unique;
        int tier = unique ? 2 : 1;
        bool first = kills[tier] == 0;
        bool hadTrophy = kills[2] > 0;
        kills[tier]++;
        if (unique) s_progress.SinceUnique[enemy.Id] = 0;
        else s_progress.SinceUnique[enemy.Id] = s_progress.SinceUnique.GetValueOrDefault(enemy.Id) + 1;
        int trophies = s_progress.Trophies;
        int band = Band(s_baseHp);

        // more rolls of its own loot: Champion +2, Unique +5 (+1 with 50 trophies); a first kill of this tier adds
        // +1 / +3 rolls that bring items but no gold (C3)
        CombatDrops drops = enemy.Drops;
        bool hasLoot = drops.FixedItems.Count > 0 || drops.ItemTables.Count > 0 || drops.FixedCurrencies.Count > 0;
        int rolls = (unique ? 5 : 2) + (unique && trophies >= 50 ? 1 : 0) + UniquePowers.ExtraEliteLootRolls;
        int firstRolls = first ? (unique ? 3 : 1) : 0;
        var items = new Dictionary<Item, int>();
        var money = new Dictionary<Currency, long>();
        bool dropsHere = s_combat!.Areas.CurrentArea?.EnemiesDropLoot == true;
        if (hasLoot && dropsHere)
        {
            for (int i = 0; i < rolls + firstRolls; i++) RollLoot(drops, items, money, gold: i < rolls);
            double goldBoost = trophies >= 10 ? 1.02 : 1.0;
            foreach (Currency c in money.Keys.ToList()) money[c] = (long)Math.Round(money[c] * goldBoost);
        }
        double emberMult = 1;
        if (!hasLoot)
        {
            // no loot table and no gold (mostly dungeon bosses): gold from its hit points, and half again the Ember
            double pay = s_baseHp * (0.5 + s_rng.NextDouble()) * (unique ? 2 : 1);
            long goldPay = (long)Math.Min(pay, unique ? 30_000 : 15_000);
            if (s_game!.Data.TryGetObject(CurrencyIds.GoldPieces, out Currency? gp) && goldPay > 0) money[gp] = money.GetValueOrDefault(gp) + goldPay;
            emberMult = 1.5;
        }
        int given = 0;
        foreach (var (item, n) in items) given += s_combat.Loot.Inventory.AddQuantity(item, n);
        foreach (var (c, n) in money) s_combat.Loot.Wallet.AddQuantity(c, n);

        // Ember: Champion 2 + band, Unique 12 + 4 band; first of a tier +10 / +40; a Unique whose trophy is already
        // there +15; trophies +0.1% each, all 179 +25%
        double ember = unique ? 12 + 4 * band : 2 + band;
        if (first) ember += unique ? 40 : 10;
        if (unique && hadTrophy) ember += 15;
        ember *= emberMult * (1 + 0.001 * trophies) * (trophies >= 179 ? 1.25 : 1) * (1 + RareItems.WornCustom(s_game!, "Ember") / 100);
        long emberGot = (long)Math.Round(ember);
        s_progress.Ember += emberGot;
        LastEmber = emberGot;
        LastRolls = rolls + firstRolls;

        string name = unique ? Info?.UniqueName ?? enemy.DisplayName.Text : "Champion " + enemy.DisplayName.Text;
        string trophy = unique && !hadTrophy ? $", trophy {s_progress.Trophies}/179" : unique ? $", trophy kills {kills[2]}" : "";
        string gold = money.Count > 0 ? ", " + string.Join(", ", money.Select(m => $"{m.Value:N0} {m.Key.DisplayName.Text}")) : "";
        LastReward = $"{name}: {given} item(s) from {(hasLoot && dropsHere ? LastRolls : 0)} extra roll(s){gold}, {emberGot} Ember{trophy}.";
        s_progress.Recent.Insert(0, $"{DateTime.Now:HH:mm} {LastReward}");
        if (s_progress.Recent.Count > 12) s_progress.Recent.RemoveRange(12, s_progress.Recent.Count - 12);
        Log(LastReward);
        ApplyTrophyBonus();
    }

    /// <summary>One roll of a monster's own loot, as the game rolls it (no doubling); chests and other openables are left out.</summary>
    static void RollLoot(CombatDrops drops, Dictionary<Item, int> items, Dictionary<Currency, long> money, bool gold)
    {
        var rng = s_combat!.Loot.RNG;
        // the extra rolls use the player's loot and gold doubling too, as the game's own roll does (denge-2 8)
        int twice = rng.RollChance(s_combat.Loot.Stats.ItemDoublingChance.Total.Value) ? 2 : 1;
        foreach (ItemQuantityRange fixedItem in drops.FixedItems)
            if (!fixedItem.Item.Capabilities.Contains<OpenableCapability>())
                items[fixedItem.Item] = items.GetValueOrDefault(fixedItem.Item) + twice * rng.NextInclusiveInt32(fixedItem.MinQuantity, fixedItem.MaxQuantity);
        foreach (var table in drops.ItemTables)
        {
            if (!rng.RollChance(table.Chance)) continue;
            var drop = table.Table.RollForItem(rng);
            if (drop.Item.Capabilities.Contains<OpenableCapability>()) continue;
            items[drop.Item] = items.GetValueOrDefault(drop.Item) + twice * rng.NextInclusiveInt32(drop.MinQuantity, drop.MaxQuantity);
        }
        if (!gold) return;
        foreach (CurrencyQuantityRange c in drops.FixedCurrencies)
            money[c.Currency] = money.GetValueOrDefault(c.Currency)
                + (rng.RollChance(s_combat.Loot.Stats.CurrencyDoublingChance.Total.Value) ? 2 : 1) * rng.NextInclusiveInt64(c.MinQuantity, c.MaxQuantity);
    }

    /// <summary>Trophy milestone 25: +1% max hit points for the player (a bonus while the mod is in).</summary>
    static void ApplyTrophyBonus()
    {
        if (s_mods == null) return;
        try { s_mods.Remove(s_trophyHp); } catch { }
        if (s_progress.Trophies >= 25)
            s_mods.Add(s_trophyHp, Patches.Bonuses(Patches.Bonus("Character", "PercentMaxHitpoints", 0.01, "Player")));
    }

    /// <summary>The milestones reached, for the window.</summary>
    public static IEnumerable<string> Milestones()
    {
        int t = s_progress.Trophies;
        yield return $"10 trophies: +2% elite gold{(t >= 10 ? " (reached)" : "")}";
        yield return $"25 trophies: +1% max hit points{(t >= 25 ? " (reached)" : "")}";
        yield return $"50 trophies: +1 Unique loot roll{(t >= 50 ? " (reached)" : "")}";
        yield return $"100 trophies: mark a monster for +25% Unique chance{(t >= 100 ? " (reached)" : "")}";
        yield return $"179 trophies: Master of the 179, +25% Ember{(t >= 179 ? " (reached)" : "")}";
    }

    /// <summary>Marks the monster being fought (100 trophies).</summary>
    public static bool MarkCurrent()
    {
        if (s_progress.Trophies < 100 || s_combat?.Enemy.Enemy is not { } enemy || InfoOf(enemy.Id) == null) return false;
        s_progress.Marked = enemy.Id;
        s_dirty = true;
        return true;
    }
}
