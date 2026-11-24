using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatEffects;
using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Modifiers;

namespace OneStyle;

/// <summary>
/// The eight Uniques of the build-gap round (yeni-uniqueler/karar.md, 2026-11-24): an early melee and magic weapon, an early
/// ring, a melee shield, a critical-hit, a Rage, a lifesteal and a Blind/Wither item. The on-hit chances of Divide-Fang
/// Howler and Barrens Hush are the game's own combat effects (so Oathbound Gauntlets' second roll works with them too).
/// </summary>
public static partial class UniquePowers
{
    const string TwinLurkerFangs = "OsUTwinLurkerFangs", BellYokeWand = "OsUBellYokeWand", ResinHoardRing = "OsUResinHoardRing",
        SentinelAegis = "OsUDivideSentinelSAegis", TalonOfTheDivide = "OsUTalonOfTheDivide", DivideFangHowler = "OsUDivideFangHowler",
        VeinboundChalice = "OsUVeinboundChalice", BarrensHush = "OsUBarrensHush";

    static bool MeleeWeapon() => !RangedWeapon() && !MagicWeapon();

    static ModifierValues Effects(string json)
    {
        using JsonDocument doc = JsonDocument.Parse("{\"Global\":[],\"Component\":[],\"CombatEffect\":[" + json + "]}");
        return new ModifierValues(s_game!.Data, doc.RootElement.Clone());
    }

    static string OnHit(string type, string effect, string config, double chance) =>
        "{\"Character\":{\"Entity\":\"MelvorBase:Combat\",\"Component\":\"Player\"},\"Modifier\":{\"Effect\":{\"Type\":\"MelvorBase:" + type
        + "\",\"Data\":{\"Effect\":\"MelvorBase:" + effect + "\",\"Configuration\":" + config + "}},\"Trigger\":{\"Type\":\"MelvorBase:OnAttack\","
        + "\"Data\":{\"Type\":\"OnHit\"}},\"AppliesTo\":\"" + (effect == "Rage" ? "Self" : "Target") + "\"},\"Chance\":"
        + chance.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"Condition\":null,\"Scaling\":null}";

    // ================================================================ always on while worn

    static void AlwaysOn3()
    {
        // Pack Howl: 25% on hit to gain a stack of the game's Rage
        SetIf("Howler:rage", DivideFangHowler, true, () => Effects(OnHit("Stacking", "Rage", "{\"StacksToAdd\":null}", 0.25)));
        // Overflow: +5% lifesteal
        SetIf("Chalice", VeinboundChalice, true, () => A(("Leeching", 5)));
        // Hush: 12% Blind for 4 turns, 8% Wither for 6 turns
        SetIf("Hush:effects", BarrensHush, true, () => Effects(
            OnHit("RandomMagnitude", "Blind", "{\"Turns\":4,\"MinStacks\":null,\"MaxStacks\":null}", 0.12) + ","
            + OnHit("TurnCounting", "Wither", "{\"Turns\":6}", 0.08)));
    }

    // ================================================================ your hits

    static int RageStacks() => s_combat is { } c
        ? c.CombatEffects.GetActiveEffectsForCharacter(c.Player).OfType<ActiveStackingEffect>().Where(a => a.Data.Id == "MelvorBase:Rage").Sum(a => a.Stacks)
        : 0;

    static bool EnemyHas(params string[] effectIds) => s_combat is { } c && Enemy is { } enemy
        && c.CombatEffects.GetActiveEffectsForCharacter(enemy).Any(a => effectIds.Contains(a.Data.Id));

    static void PlayerAttacked3(AttackEventArgs e)
    {
        if (s_combat is not { } c) return;
        bool hit = e.WasHit;

        // Twin Strike: 25% on hit, a second strike for 50% (its own hit roll; it triggers nothing else)
        if (hit && Wears(TwinLurkerFangs) && Random.Shared.NextDouble() < 0.25 && Fire("TwinStrike"))
            After(150, TwinLurkerFangs, () => { if (Enemy is { Health.Current: > 0 } && RollToHit()) StrikeAsAttack(RollHit(0.5, canCrit: true)); });

        // Sentinel's Answer: the readied answer goes with this melee hit (+40% was on for it), 20% to stun
        if (hit && BuffOn("Aegis:answer") && MeleeWeapon())
        {
            B("Aegis:answer").Off();
            Fire("AegisAnswer");
            if (Random.Shared.NextDouble() < 0.20 && !EnemyStunned()) StunEnemy("MelvorBase:Stun", 1);
        }

        // Rising Edge: the sure critical hit was this attack; otherwise each hit rolls your critical chance for an Edge
        if (Wears(TalonOfTheDivide))
        {
            if (BuffOn("Edge:sure"))
            {
                B("Edge:sure").Off();
                B("Edge").Off();
                s_keep["Edge"] = 0;
                Fire("EdgeSure");
            }
            else if (hit && Random.Shared.NextDouble() < c.Player.Stats.CriticalHitChance.Value)
            {
                double edges = Math.Min(3, s_keep.GetValueOrDefault("Edge") + 1);
                s_keep["Edge"] = edges;
                Fire("Edge");
                B("Edge", TalonOfTheDivide).Set(A(("Brutal", 10 * edges)), 15000);
                if (edges >= 3) B("Edge:sure", TalonOfTheDivide).Set(A(("Deadly", 100), ("Brutal", 50)), 15000);
            }
        }

        // Pack Howl: +2% damage a Rage stack; at 5 stacks this hit howls (uses the Rage, stuns 1 turn, once in 10 s)
        if (Wears(DivideFangHowler))
        {
            int rage = RageStacks();
            if (rage != (int)s_keep.GetValueOrDefault("Howler:rage"))
            {
                s_keep["Howler:rage"] = rage;
                SetIf("Howler:fury", DivideFangHowler, rage > 0, () => A(("Damage", 2 * Math.Min(5, rage))));
            }
            if (hit && rage >= 5 && s_nowMs >= s_keep.GetValueOrDefault("Howl:next"))
            {
                s_keep["Howl:next"] = s_nowMs + 10000;
                foreach (var a in c.CombatEffects.GetActiveEffectsForCharacter(c.Player).Where(a => a.Data.Id == "MelvorBase:Rage").ToList())
                    c.CombatEffects.RemoveEffect(a);
                B("Howler:fury").Off();
                s_keep["Howler:rage"] = 0;
                if (!EnemyStunned()) StunEnemy("MelvorBase:Stun", 1);
                Fire("Howl");
            }
        }

        // Overflow: lifesteal your full hit points cannot take is kept as Blood (20% of your max hit points at most)
        if (hit && e.HealthDamage > 0 && Wears(VeinboundChalice) && c.Player.Stats.ModifiedCombatStatTotals.Dict.FirstOrDefault(kv => kv.Key.Id == "MelvorBase:Lifesteal").Value is { } ls)
        {
            var health = c.Player.Health;
            int top = Wears(TollkeeperGreaves) ? health.Max / 2 : health.Max;
            if (health.Current >= top)
            {
                double cap = health.Max * 0.20;
                double before = s_keep.GetValueOrDefault("Blood");
                s_keep["Blood"] = Math.Min(cap, before + e.HealthDamage * ls.Value);
                if (before < cap && s_keep["Blood"] >= cap) Fire("BloodFull");
            }
            Chalice_Update();
        }
    }

    static void Chalice_Update()
    {
        if (s_combat is not { } c) return;
        bool full = s_keep.GetValueOrDefault("Blood") >= c.Player.Health.Max * 0.20 - 0.5;
        SetIf("Chalice:full", VeinboundChalice, full, () => A(("Damage", 15)));
    }

    // ================================================================ spells, the monster's hits, food, hit points

    /// <summary>Bell Toll: after every 3rd spell, one more hit of 35% (called with the cast count at your turn).</summary>
    static void Bell(double casts)
    {
        if (!Wears(BellYokeWand) || casts % 3 != 0 || Enemy is not { Health.Current: > 0 }) return;
        Fire("Bell");
        After(250, BellYokeWand, () => { if (Enemy is { Health.Current: > 0 } && RollToHit()) StrikeAsAttack(RollHit(0.35, canCrit: true)); });
    }

    /// <summary>Sentinel's Answer: every 3rd monster attack that hit you readies an answer (1 at most, once in 8 s).</summary>
    static void EnemyLanded3()
    {
        if (!Wears(SentinelAegis)) return;
        double landed = Up(s_keep, "Aegis:landed");
        if (landed % 3 != 0 || BuffOn("Aegis:answer") || s_nowMs < s_keep.GetValueOrDefault("Aegis:next")) return;
        s_keep["Aegis:next"] = s_nowMs + 8000;
        Fire("AegisReady");
        B("Aegis:answer", SentinelAegis).Set(A(("Damage", 40)));
    }

    /// <summary>Resin Shell: food that heals gives 2 hits of ward for 6 s (once in 8 s).</summary>
    static void Ate3(FoodEatenEventArgs e)
    {
        if (!Wears(ResinHoardRing) || e.Healing <= 0 || s_combat is not { } c || s_nowMs < s_keep.GetValueOrDefault("Resin:next")) return;
        s_keep["Resin:next"] = s_nowMs + 8000;
        Fire("Resin");
        B("Resin", ResinHoardRing).Set(V(Patches.Bonus("Character", "FlatCombatStatValue", 2, "Player", ("CombatStat", "MelvorBase:MaximumWard"))), 6000);
        After(20, ResinHoardRing, () => c.Player.Ward.Set(Math.Min(c.Player.Ward.Max, c.Player.Ward.Current + 2)));
    }

    /// <summary>Overflow: below 40% hit points the Blood heals you.</summary>
    static void PlayerHealthChanged3()
    {
        if (!Wears(VeinboundChalice) || s_combat is not { } c) return;
        double blood = s_keep.GetValueOrDefault("Blood");
        if (blood < 1 || c.Player.Health.Current <= 0 || c.Player.Health.Current >= c.Player.Health.Max * 0.40) return;
        s_keep["Blood"] = 0;
        Fire("BloodHeal");
        HealPlayer((int)Math.Round(blood));
        Chalice_Update();
    }

    /// <summary>Every tick: Hush's +15% against a Blinded or Withered monster (from any source).</summary>
    static void Tick3()
    {
        if (!Wears(BarrensHush)) return;
        bool on = EnemyHas("MelvorBase:Blind", "MelvorBase:Wither");
        if (on != BuffOn("Hush:dmg")) SetIf("Hush:dmg", BarrensHush, on, () => A(("Damage", 15)));
    }
}
