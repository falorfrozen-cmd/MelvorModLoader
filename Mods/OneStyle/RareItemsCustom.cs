using System;
using System.Collections.Generic;
using System.Linq;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Modifiers;

namespace OneStyle;

/// <summary>
/// The affixes the game has no bonus for (worked by One Style: Slayer's, Executing, of Embers, of Calling, of Momentum)
/// and the caps on everything the worn rare items give together (03-karar.md 5): attack interval -40%, lifesteal 10%,
/// damage negation 15%, damage taken -20%, loot doubling 25%, monster spawn time -50%, elite chance +50%; and since the
/// balance review (denge-2/karar.md): action time in every skill -40%, XP in every skill +50%, the attack interval never
/// below 600 ms, a ring's own bonuses once however many rare copies of it are worn, Usurper's copy counted for the caps.
/// </summary>
public static partial class RareItems
{
    static readonly Patches.Source s_caps = new("One Style: rare item caps");
    static bool s_capsOn;

    /// <summary>What the worn rare items give of one affix, as the player reads it (9 for +9%).</summary>
    public static double Worn(Game game, string affixId) => WornAffixes(game).Where(a => a.Id == affixId).Sum(a => a.Value);

    /// <summary>The same for the custom affixes, by their Custom name.</summary>
    public static double WornCustom(Game game, string custom) =>
        WornAffixes(game).Where(a => Affixes.TryGetValue(a.Id, out AffixDef? d) && d.Custom == custom).Sum(a => a.Value);

    static IEnumerable<RolledAffix> WornAffixes(Game game)
    {
        if (Combat(game) is not { } combat) yield break;
        foreach (EquipmentSlot slot in combat.Equipment.Config.EquipmentSlots)
            if (combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty && !a.IsOccupied && RecordOf(a.Equipped!.Item) is { } r)
                foreach (RolledAffix x in r.Affixes) yield return x;
        // Usurper's Circle gives its paired ring's bonuses a second time: they count for the caps as well
        if (UniquePowers.UsurperCopy is { } copy && RecordOf(copy) is { } c)
            foreach (RolledAffix x in c.Affixes) yield return x;
    }

    static readonly (string Affix, double Cap, string StatsType, string Name, string Component, double Scale)[] s_capped =
    {
        ("Swift", 40, "Character", "PercentAttackInterval", "Player", -0.01),
        ("Leeching", 10, "Character", "FlatCombatStatValue", "Player", 0.01),
        ("Warding2", 15, "Character", "AttackDamageNegationChance", "Player", 0.01),
        ("Stalwart", 20, "Character", "PercentDamageTaken", "Player", -0.01),
        ("Plenty", 25, "CombatLoot", "ItemDoublingChance", "Loot", 0.01),
        ("Haste", 50, "CombatAction", "PercentSpawnInterval", "Action", -0.01),
    };

    /// <summary>Caps on affixes whose bonus is a global stat (every skill): the cut is the affix's own bonus with a negative value.</summary>
    static readonly (string Affix, double Cap)[] s_globalCaps = { ("Diligence", 40), ("Learning", 50) };

    /// <summary>Nothing makes the player attack faster than this (denge-2 1): affixes, Momentum, class, Uniques and Rage together.</summary>
    public const double MinAttackInterval = 600;
    static readonly Patches.Source s_floor = new("One Style: attack interval floor 600 ms");
    static double s_floorMs;

    /// <summary>
    /// Every game tick: when the attack interval is below 600 ms, a flat amount brings it back up; when it rises again the
    /// amount goes down. It settles in a tick or two and costs nothing while the interval is above the floor.
    /// </summary>
    public static void IntervalFloor(Game game)
    {
        if (Patches.Modifiers is not { } mods || Combat(game) is not { } combat) return;
        double now = combat.Player.Stats.AttackInterval.Value;
        double want = s_floorMs;
        // the player asked Cheat Menu for faster attacks: the floor steps aside (our two mods never fight each other)
        if (CheatAttackSpeedOn()) want = 0;
        else
        if (now < MinAttackInterval - 0.5) want = s_floorMs + (MinAttackInterval - now);
        else if (s_floorMs > 0 && now > MinAttackInterval + 0.5) want = Math.Max(0, s_floorMs - (now - MinAttackInterval));
        if (Math.Abs(want - s_floorMs) < 0.5) return;
        Log($"One Style: attack interval floor {(want > 0 ? $"+{want:0} ms (the interval was {now:0} ms)" : "off")}.");
        if (s_floorMs > 0) mods.Remove(s_floor);
        s_floorMs = want;
        if (s_floorMs > 0) mods.Add(s_floor, Patches.Bonuses(Patches.Bonus("Character", "FlatAttackInterval", Math.Ceiling(s_floorMs), "Player")));
    }

    static System.Reflection.MethodInfo? s_cheatActive;
    static bool s_cheatLooked;

    /// <summary>Cheat Menu's "Attack speed" is on (looked up by name: One Style does not need Cheat Menu).</summary>
    static bool CheatAttackSpeedOn()
    {
        if (!s_cheatLooked)
        {
            s_cheatLooked = true;
            s_cheatActive = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("CheatMenu.Engine")).FirstOrDefault(t => t != null)
                ?.GetMethod("IsActive", new[] { typeof(string) });
        }
        try { return s_cheatActive?.Invoke(null, new object[] { "combat.attackSpeed" }) is true; }
        catch { return false; }
    }

    static readonly Dictionary<string, Patches.Source> s_sameRing = new();
    static readonly List<Patches.Source> s_sameRingOn = new();

    /// <summary>
    /// The game lets each of its rings count once; rare copies of one ring are each their own item. Their affixes all count,
    /// the ring's own bonuses once: what the second and later copies add of them is taken off again (the ring's own
    /// bonuses at -1 per extra copy; its on-hit effects are not doubled by the game anyway).
    /// </summary>
    static void SameRingOnce(Game game, ModifierSystem mods)
    {
        foreach (Patches.Source on in s_sameRingOn) mods.Remove(on);
        s_sameRingOn.Clear();
        if (Combat(game) is not { } combat) return;
        var bases = new List<Item>();
        foreach (EquipmentSlot slot in combat.Equipment.Config.EquipmentSlots)
            if (combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty && !a.IsOccupied && a.Equipped!.Item is { } item
                && SlotOf(item) == "Ring")
                bases.Add(RecordOf(item) is { UniqueId: "" } r && game.Data.TryGetObject(r.BaseId, out Item? b) ? b! : item);
        foreach (var group in bases.GroupBy(i => i.Id).Where(g => g.Count() > 1))
        {
            if (!group.First().Capabilities.TryGet(out EquipmentCapability? cap)) continue;
            // its stats (attack and defence bonuses, power and protection ratings) and its bonuses, taken off once per extra copy
            var component = new List<ComponentModifierValue>(cap.Bonuses?.Component ?? (IEnumerable<ComponentModifierValue>)Array.Empty<ComponentModifierValue>());
            foreach (var stat in cap.Stats.Stats)
                component.Add(Patches.Bonus("Character", "FlatCombatStatValue", stat.Value, "Player", ("CombatStat", stat.Stat.Id)));
            foreach (var power in cap.Stats.PowerRatings)
                component.Add(Patches.Bonus("Character", "FlatPowerRating", power.Value, "Player", ("DamageType", power.DamageType.Id)));
            foreach (var protection in cap.Stats.ProtectionRatings)
                component.Add(Patches.Bonus("Character", "FlatProtectionRating", protection.Value, "Player", ("DamageType", protection.DamageType.Id)));
            var plain = new ModifierValues(cap.Bonuses?.Global.ToList() ?? new List<GlobalModifierValue>(), component,
                new List<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>());
            if (!s_sameRing.TryGetValue(group.Key, out Patches.Source? source)) s_sameRing[group.Key] = source = new("One Style: " + group.Key + " counted once");
            mods.Add(source, plain, -(group.Count() - 1));
            s_sameRingOn.Add(source);
        }
    }

    /// <summary>Worked out again whenever gear changes: what the rare items give above a cap is taken off again.</summary>
    public static void UpdateCaps(Game game)
    {
        UniquePowers.Refresh(game);
        if (Patches.Modifiers is not { } mods) return;
        if (s_capsOn) { mods.Remove(s_caps); s_capsOn = false; }
        var values = new List<ComponentModifierValue>();
        foreach (var (affix, cap, statsType, name, component, scale) in s_capped)
        {
            double over = Worn(game, affix) - cap;
            if (over <= 0) continue;
            values.Add(affix == "Leeching"
                ? Patches.Bonus(statsType, name, -over * scale, component, ("CombatStat", "MelvorBase:Lifesteal"))
                : Patches.Bonus(statsType, name, -over * scale, component));
        }
        var cut = Patches.Bonuses(values.ToArray());
        var globalCut = new List<GlobalModifierValue>(cut.Global);
        var componentCut = new List<ComponentModifierValue>(cut.Component);
        foreach (var (affix, cap) in s_globalCaps)
        {
            double over = Worn(game, affix) - cap;
            if (over <= 0) continue;
            ModifierValues less = AffixBonuses(game, new[] { new RolledAffix { Id = affix, Value = -over } }, null);
            globalCut.AddRange(less.Global);
            componentCut.AddRange(less.Component);
        }
        SameRingOnce(game, mods);
        if (globalCut.Count + componentCut.Count == 0) return;
        mods.Add(s_caps, new ModifierValues(globalCut, componentCut, new List<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>()));
        s_capsOn = true;
    }

    static EquipmentComponent? s_watchedEquipment;
    static readonly EventHandler s_onSwap = (_, _) => { if (s_game != null) UpdateCaps(s_game); };
    static readonly EventHandler<ItemEquippedEventArgs> s_onGear = (_, _) => { if (s_game != null) UpdateCaps(s_game); };

    /// <summary>Follows equipping and unequipping (for the caps).</summary>
    static void WatchGear(Game game)
    {
        if (s_watchedEquipment != null)
        {
            s_watchedEquipment.ItemEquipped -= s_onGear;
            s_watchedEquipment.ItemUnequipped -= s_onGear;
            s_watchedEquipment.LoadoutSwapped -= s_onSwap;
            s_watchedEquipment = null;
        }
        if (Combat(game) is not { } combat) return;
        s_watchedEquipment = combat.Equipment;
        s_watchedEquipment.ItemEquipped += s_onGear;
        s_watchedEquipment.ItemUnequipped += s_onGear;
        s_watchedEquipment.LoadoutSwapped += s_onSwap;
        UpdateCaps(game);
    }
}
