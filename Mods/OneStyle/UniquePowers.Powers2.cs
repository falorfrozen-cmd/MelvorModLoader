using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatLoot;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.MagicSpells;
using GamesByMalcs.Melvor2.Core.TickTimer;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Modifiers;

namespace OneStyle;

/// <summary>The powers that reach into the game's deeper systems: attack timers, spells, prayers, sets, the triangle.</summary>
public static partial class UniquePowers
{
    const string Thornshot = "OsUThornshot", VeilRebuttal = "OsUVeilRebuttal", OathlockedPlate = "OsUOathlockedPlate",
        PinnacleDiadem = "OsUPinnacleDiadem", UsurperSCircle = "OsUUsurperSCircle", FourfoldWrit = "OsUFourfoldWrit",
        ThreevoiceCleaver = "OsUThreevoiceCleaver", SunkenVolley = "OsUSunkenVolley", ConclaveSpire = "OsUConclaveSpire",
        SanguineLiturgy = "OsUSanguineLiturgy", HollowHarvestCrown = "OsUHollowHarvestCrown", ThreefoldDecree = "OsUThreefoldDecree",
        RingleaderSTally = "OsURingleaderSTally", AmberReservoir = "OsUAmberReservoir", DuneVizierWrappings = "OsUDuneVizierWrappings",
        OathboundGauntlets = "OsUOathboundGauntlets", HemalPalimpsest = "OsUHemalPalimpsest", MirageAstrolabe = "OsUMirageAstrolabe";

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_ticksLeft")]
    static extern ref int TicksLeftOf(TickTimer timer);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "SetElement")]
    static extern void SetElementOf(CharacterComponent character, Element element);

    static ComponentModifierValue On(string component, string statsType, string name, double value, params (string, string)[] context) =>
        Patches.Bonus(statsType, name, value, component, context);

    static string PlayerStyle() =>
        s_combat?.Player.Equipment.CurrentWeapon is { } w ? RareItems.StyleOf(w.Item) is { Length: > 0 } s ? s : "MelvorBase:Melee" : "MelvorBase:Melee";

    static bool MagicWeapon() => PlayerStyle() == "MelvorBase:Magic";

    // ================================================================ always on (second group)

    static void AlwaysOn2()
    {
        // Sealed Oath: above half hit points -15% damage taken, at half or below +15% damage
        Oath_Update();
        // Hollow Harvest: -35% max hit points, +6 ward
        SetIf("Hollow", HollowHarvestCrown, true, () => V(P("PercentMaxHitpoints", -0.35),
            Patches.Bonus("Character", "FlatCombatStatValue", 6, "Player", ("CombatStat", "MelvorBase:MaximumWard"))));
        // Blood Rite: spells keep their runes
        SetIf("Sanguine", SanguineLiturgy, true, () => A(("Runes", 100)));
        // Sealed Prayers: prayers cost half (at least 1)
        SetIf("Dune", DuneVizierWrappings, true, () => V(On("Prayer", "Prayer", "PercentPrayerCost", -0.50)));
        // Twin Volley: each arrow deals 60%
        SetIf("Sunken", SunkenVolley, true, () => A(("Damage", -40)));
        // Caravan Road, Slash: 20% chance on hit to Bleed
        SetIf("Caravan:slash", CaravanWarcloak, Stance() == "Slash", () => A(("Serrated", 20)));
        Usurper_Update();
        Sets_Update();
        Triangle_Update();
        Prayers_Update();
        Imbuement_Update();
        Amber_Update();
    }

    // ---- Sealed Oath

    static void Oath_Update()
    {
        if (s_combat == null) return;
        bool high = s_combat.Player.Health.Current > s_combat.Player.Health.Max / 2;
        SetIf("Oath:high", OathlockedPlate, high, () => A(("Stalwart", 15)));
        SetIf("Oath:low", OathlockedPlate, !high, () => A(("Damage", 15)));
    }

    // ---- Other Hand's Echo: the bonuses of the ring in the paired slot (1-2, 3-4, 5-6)

    static readonly string[] s_ringSlots = { "MelvorBase:RingSlot1", "MelvorBase:RingSlot2", "MelvorBase:OneStyleRingSlot3",
        "MelvorBase:OneStyleRingSlot4", "MelvorBase:OneStyleRingSlot5", "MelvorBase:OneStyleRingSlot6" };

    /// <summary>The ring whose bonuses Usurper's Circle copies now (its affixes count for the caps too).</summary>
    public static Item? UsurperCopy { get; private set; }

    static void Usurper_Update()
    {
        B("Usurper").Off();
        UsurperCopy = null;
        if (!Wears(UsurperSCircle) || s_combat == null || s_game == null) return;
        var worn = new Dictionary<int, Item>();
        for (int i = 0; i < s_ringSlots.Length; i++)
            if (s_game.Data.TryGetObject(s_ringSlots[i], out EquipmentSlot? slot) && s_combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a)
                && !a.IsEmpty && a.Equipped is { } e) worn[i] = e.Item;
        var circles = worn.Where(kv => kv.Value.LocalId == UsurperSCircle).Select(kv => kv.Key).ToList();
        if (circles.Count != 1) return;   // two Circles copy nothing
        int pair = circles[0] ^ 1;
        if (!worn.TryGetValue(pair, out Item? other) || other.LocalId.StartsWith("OsU")) return;
        if (other.Capabilities.TryGet(out EquipmentCapability? cap) && cap.Bonuses is { } bonuses)
        {
            var plain = new ModifierValues(bonuses.Global.ToList(), bonuses.Component.Where(c => c.Condition == null).ToList(), new List<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>());
            B("Usurper", UsurperSCircle).Set(plain);
            UsurperCopy = other;
        }
    }

    // ---- Ringmaster's Count, and rare items counting for sets

    static void Sets_Update()
    {
        if (s_combat == null || s_game == null) return;
        foreach (var key in s_buffs.Keys.Where(k => k.StartsWith("Set:")).ToList()) s_buffs[key].Off();
        var worn = new List<Item>();
        foreach (EquipmentSlot slot in s_combat.Equipment.Config.EquipmentSlots)
            if (s_combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty && !a.IsOccupied && a.Equipped is { } e) worn.Add(e.Item);
        var baseOf = worn.ToDictionary(i => i, i => RareItems.RecordOf(i) is { } r ? (r.UniqueId != "" ? Uniques.BaseOf(r.UniqueId) : r.BaseId) : i.Id);
        bool tally = Wears(RingleaderSTally);
        foreach (EquipmentSetBonus set in s_game.Data.GetDataRegistryOutput<EquipmentSetBonus>().GetAllObjects())
        {
            var ids = set.Items.Select(i => i.Id).ToHashSet();
            int native = worn.Count(i => set.Items.Contains(i));
            int counted = worn.Count(i => ids.Contains(baseOf[i]));
            if (tally && counted >= 2) counted++;
            if (counted <= native) continue;
            int k = 0;
            foreach (SetBonusModifiers tier in set.Bonuses.Where(t => t.ItemCount > native && t.ItemCount <= counted))
                B($"Set:{set.Id}:{k++}", "").Set(tier.Modifiers);
        }
    }

    public static IEnumerable<string> SetBonusesAdded => s_buffs.Where(kv => kv.Key.StartsWith("Set:") && kv.Value.On).Select(kv => kv.Key);

    // ---- Three Jaws' Edict: the combat triangle's advantage against every monster

    static void Triangle_Update()
    {
        B("Triangle:plus").Off();
        if (!Wears(ThreefoldDecree) || s_combat == null || s_game == null) return;
        string style = PlayerStyle();
        var all = s_game.Data.GetDataRegistryOutput<CombatTriangleBonus>().GetAllObjects().ToList();
        CombatTriangleBonus? good = all.FirstOrDefault(t => t.PlayerStyle.Id == style && t.IsPositiveForPlayer);
        if (good == null) return;
        // One Style's class turns the triangle off: the advantage is then always added. Off, the game's own row
        // counts too: no bonus is added when the matchup already favours you, a penalty is cancelled.
        string enemyStyle = s_combat.Enemy.Enemy?.AvailableStyles.FirstOrDefault()?.Id ?? "";
        bool triangleOn = Patches.Current == Class.None;
        CombatTriangleBonus? now = all.FirstOrDefault(t => t.PlayerStyle.Id == style && t.EnemyStyle.Id == enemyStyle);
        if (triangleOn && now is { IsPositiveForPlayer: true }) return;
        B("Triangle:plus", ThreefoldDecree).Set(good.Bonuses);
        if (triangleOn && now is { IsPositiveForPlayer: false }) B("Triangle:cancel", ThreefoldDecree).Set(now.Bonuses, -1, -1);
        else B("Triangle:cancel").Off();
    }

    // ---- Sealed Prayers: active prayers 30% stronger

    static void Prayers_Update()
    {
        foreach (var key in s_buffs.Keys.Where(k => k.StartsWith("Prayer:")).ToList()) s_buffs[key].Off();
        if (!Wears(DuneVizierWrappings) || s_combat == null) return;
        foreach (var (prayer, active) in s_combat.Prayer.ActivePrayers)
            if (s_combat.Prayer.IsPrayerActive(prayer)) B("Prayer:" + prayer.Id, DuneVizierWrappings).Set(prayer.Bonuses, -1, 0.30);
    }

    // ---- Amber Vault: combat potions in the toolbelt are never used up, and their bonuses are 1.5 times

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_activeSlots")]
    static extern ref Dictionary<GamesByMalcs.Melvor2.Components.Toolbelt.ToolbeltSlot, GamesByMalcs.Melvor2.Components.Toolbelt.ActiveToolbeltSlot>
        ToolbeltSlotsOf(GamesByMalcs.Melvor2.Components.Toolbelt.ToolbeltComponent toolbelt);

    static void Amber_Update()
    {
        foreach (var key in s_buffs.Keys.Where(k => k.StartsWith("Amber:")).ToList()) s_buffs[key].Off();
        if (!Wears(AmberReservoir) || s_combat == null) return;
        foreach (var slot in ToolbeltSlotsOf(s_combat.Toolbelt).Values)
        {
            if (slot.Equipped is not { } held || held.Toolbelt.Bonuses is not { } bonuses) continue;
            string id = held.Item.Id;
            B("Amber:keep:" + id, AmberReservoir).Set(V(Patches.Bonus("Toolbelt", "PreservationChance", 1, "Toolbelt", ("Item", id))));
            // the numbers that help, half again (chances, intervals and durations stay as they are)
            var more = bonuses.Component.Where(c => c.Value > 0 && !c.Stat.Name.Contains("Chance") && !c.Stat.Name.Contains("Interval") && !c.Stat.Name.Contains("Duration")).ToList();
            if (more.Count > 0)
                B("Amber:more:" + id, AmberReservoir).Set(new ModifierValues(new List<GlobalModifierValue>(), more,
                    new List<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>()), -1, 0.5);
        }
    }

    // ---- False Horizon: the imbuement 80% stronger (x2.7 on every 6th spell)

    static void Imbuement_Update(bool burst = false)
    {
        B("Mirage").Off();
        if (!Wears(MirageAstrolabe) || s_combat?.MagicSpells.SelectedImbuement is not { } imbue) return;
        B("Mirage", MirageAstrolabe).Set(imbue.Data.Bonuses, -1, burst ? 1.7 : 0.8);
        if (burst) Fire("MirageBurst");
    }

    // ================================================================ attack turns

    static void PlayerTurnStarts()
    {
        if (!MagicWeapon()) return;
        double casts = Up(s_keep, "casts");
        Bell(casts);
        // Blood Rite: each cast costs 3% of your current hit points (never below 1)
        if (Wears(SanguineLiturgy) && s_combat!.Player.Health.Current > 1)
        {
            var h = s_combat.Player.Health;
            h.Set(Math.Max(1, h.Current - Math.Max(1, (int)(h.Current * 0.03))));
            Fire("Sanguine");
        }
        // False Horizon: every 6th spell the imbuement bursts
        if (Wears(MirageAstrolabe)) Imbuement_Update(burst: casts % 6 == 0);
        // Four Seals: every 6th spell keeps its runes and prayer points and opens the next seal
        if (Wears(FourfoldWrit))
        {
            B("Fourfold:free").Off();
            B("Fourfold:seal1").Off();
            if (casts % 4 == 0)   // every 4th spell (was 6th: a full cycle took 24 spells; denge-2 9)
            {
                Fire("Fourfold");
                double seal = Up(s_keep, "Fourfold:seal") % 4;   // 1, 2, 3, 0 = 4th
                s_fight["Fourfold:open"] = seal == 0 ? 4 : seal;
                B("Fourfold:free", FourfoldWrit).Set(A(("Runes", 100), ("Devotion", 100)));
                if (seal == 1) B("Fourfold:seal1", FourfoldWrit).Set(A(("Damage", 40)));
            }
        }
    }

    static void PlayerTurnEnds()
    {
        if (!MagicWeapon() || s_combat == null) return;
        double casts = s_keep.GetValueOrDefault("casts");
        // Seven Seals: every 5th spell is followed by an echo of it (a normal roll of your hit, free)
        if (Wears(HemalPalimpsest) && casts % 5 == 0 && Enemy is { Health.Current: > 0 } && Fire("Hemal"))
            After(200, HemalPalimpsest, () => { if (RollToHit()) StrikeAsAttack(RollHit(1, canCrit: true)); });
        // Four Seals: what the open seal does after the spell
        if (Wears(FourfoldWrit) && F("Fourfold:open") is > 0 and var open)
        {
            s_fight["Fourfold:open"] = 0;
            switch ((int)open)
            {
                case 2: AddDot(FourfoldWrit, "FourfoldBurn", s_combat.Player.Stats.MaxHit.Value * 0.5 * 0.06, 5); break;
                case 3: HealPlayer((int)(s_combat.Player.Health.Max * 0.05)); break;
                case 4: s_seal4 = 1; B("Fourfold:seal4:fight", FourfoldWrit).Set(A(("Stalwart", 50))); break;
            }
        }
    }

    static bool s_veilDodge;
    static int s_seal4;   // Four Seals' 4th seal: 1 = waits for the monster's next attack, 2 = on for this attack

    /// <summary>A monster's attack starts (its hits come before its turn ends).</summary>
    static void EnemyTurnStarts()
    {
        s_enemyLanded = false;
        s_enemyMissed = false;
        if (s_seal4 == 1) s_seal4 = 2;
        // Veil's Refusal: 15% that the monster's whole attack does no damage, and you strike back
        s_veilDodge = Wears(VeilRebuttal) && MagicWeapon() && Random.Shared.NextDouble() < 0.15;
        if (s_veilDodge) B("Veil:fight", VeilRebuttal).Set(A(("Warding2", 100)));
    }

    /// <summary>The monster's attack is over: what answered it, and what was on only for it.</summary>
    static void EnemyTurnEnds()
    {
        if (s_veilDodge)
        {
            s_veilDodge = false;
            B("Veil:fight").Off();
            if (Enemy is { Health.Current: > 0 } && Fire("Veil")) After(100, VeilRebuttal, () => StrikeAsAttack(RollHit(1, canCrit: false)));
        }
        if (s_seal4 == 2) { s_seal4 = 0; B("Fourfold:seal4:fight").Off(); }
        if (s_enemyLanded)
        {
            EnemyLanded3();
            // Fifth Stratum: the attack that landed while the bonus was on was the 5th; the count goes on
            if (Wears(CavernBastion))
            {
                B("Cavern").Off();
                if (Up(s_keep, "Cavern") % 5 == 4 && Fire("Cavern")) B("Cavern", CavernBastion).Set(A(("Stalwart", 50)));
            }
            // Dream Tide: a drowsy monster's next attack was weakened
            if (F("Dreamtide:drowsy") > 0) { s_fight["Dreamtide:drowsy"] = 0; B("Dreamtide:drowsy:fight").Off(); }
        }
        // Step Into Nothing: every hit of the attack missed
        else if (s_enemyMissed && Wears(Snarestep) && Fire("Snarestep")) B("Snarestep:fight", Snarestep).Set(A(("Damage", 35)));
        s_enemyLanded = s_enemyMissed = false;
    }

    // ================================================================ second-group hit, fight and loot parts

    static void PlayerAttacked2(AttackEventArgs e)
    {
        if (!e.WasHit) return;
        double hits = F("hits");
        // Thorn Stagger: the first hit of a fight always, then 15% on hit (once in 6 s a monster), delays the monster's next
        // attack by 25% of its interval
        if (Wears(Thornshot) && s_nowMs >= F("Thornshot:next") && (F("Thornshot:first") == 0 || Random.Shared.NextDouble() < 0.15) && s_combat is { } c
            && c.Enemy.TurnTimer.IsRunning && !EnemyStunned() && F("Thornshot:delayed") != c.Enemy.TurnTimer.WaitTicks * 1000 + TicksLeftOf(c.Enemy.TurnTimer))
        {
            s_fight["Thornshot:next"] = s_nowMs + 6000;
            s_fight["Thornshot:first"] = 1;
            TickTimer t = c.Enemy.TurnTimer;
            int extra = Math.Max(1, (int)Math.Round(t.WaitTicks * 0.25));
            t.WaitTicks += extra;
            TicksLeftOf(t) += extra;
            s_fight["Thornshot:delayed"] = t.WaitTicks * 1000 + TicksLeftOf(t);
            Fire("Thornshot");
        }
        // Stolen Bulwark: the first hit of a fight takes the monster's ward and gives you ward for 10 s
        if (Wears(PinnacleDiadem) && F("Pinnacle") == 0 && s_combat is { } c2)
        {
            s_fight["Pinnacle"] = 1;
            Fire("Pinnacle");
            int had = c2.Enemy.Ward.Current;
            if (had > 0) c2.Enemy.Ward.Set(Math.Max(0, had - 2));
            int give = had > 0 ? 4 : 2;
            B("Pinnacle:fight", PinnacleDiadem).Set(V(Patches.Bonus("Character", "FlatCombatStatValue", give, "Player", ("CombatStat", "MelvorBase:MaximumWard"))), 10000);
            After(20, PinnacleDiadem, () => c2.Player.Ward.Set(Math.Min(c2.Player.Ward.Max, c2.Player.Ward.Current + give)));
        }
        // Three Voices: every 3rd successful hit +35% damage and the monster's evasion -30% (on for that hit)
        if (Wears(ThreevoiceCleaver))
        {
            B("Threevoice:fight").Off();
            if (hits % 3 == 2) B("Threevoice:fight", ThreevoiceCleaver).Set(V(P("PercentDamageDealt", 0.35), Patches.Bonus("Character", "PercentEvasionRating", -0.30, "Enemy")));
        }
        // Soul Anvil: an on-hit effect of your gear that did not happen gets a second roll
        if (Wears(OathboundGauntlets)) SecondRoll();
    }

    static void PlayerAttackStarted2(AttackEventArgs e)
    {
        // Twin Volley: a second arrow at 60%, with its own hit roll and one more ammo
        if (Wears(SunkenVolley) && RangedWeapon() && s_combat is { } c && Enemy is { Health.Current: > 0 }
            && c.Player.Equipment.CurrentWeapon?.Weapon?.RequiredAmmoType is { } ammo && c.Player.Equipment.ConsumeAmmo(ammo) && Fire("Sunken"))
            After(150, SunkenVolley, () => { if (RollToHit()) StrikeAsAttack(RollHit(1, canCrit: true)); });
    }

    // ---- Soul Anvil

    static void SecondRoll()
    {
        if (s_combat is not { } c || Enemy is not { } enemy || Patches.Modifiers is not { } mods) return;
        var present = c.CombatEffects.GetActiveEffectsForCharacter(enemy).Select(a => a.Data.Id).ToHashSet();
        foreach (var (modifier, table) in mods.CombatEffects.GetTablesForCharacter(c.Player))
        {
            if (modifier.Trigger is not GamesByMalcs.Melvor2.Components.CombatEffects.EffectTriggers.OnAttackCombatEffectTrigger t
                || t.Type != GamesByMalcs.Melvor2.Components.CombatEffects.EffectTriggers.OnAttackTriggerType.OnHit) continue;
            if (modifier.AppliesTo != RelativeCharacter.Target || present.Contains(modifier.Effect.Data.Id)) continue;
            double p = table.TotalChance;
            if (p <= 0 || p >= 1) continue;
            if (Random.Shared.NextDouble() < p && Fire("SoulAnvil")) c.CombatEffects.ApplyEffect(modifier.Effect, enemy, c.Player);
        }
    }

    static void FightStarts2()
    {
        // Council's Voice: the element strongest against the monster for this fight (+15% damage when it is)
        if (Wears(ConclaveSpire) && MagicWeapon() && s_combat is { } c && s_game != null && Enemy != null)
        {
            Element? mine = c.Player.Stats.Element?.Value;
            Element? theirs = c.Enemy.Stats.Element?.Value;
            Element? best = null;
            if (theirs != null)
                foreach (string id in new[] { "MelvorBase:Air", "MelvorBase:Water", "MelvorBase:Earth", "MelvorBase:Fire" })
                    if (s_game.Data.TryGetObject(id, out Element? el) && c.Action.GetElementalBonusKind(el, theirs, true) == BonusKind.Positive) { best = el; break; }
            if (best != null && mine != null && c.Action.GetElementalBonusKind(mine, theirs!, true) != BonusKind.Positive)
            {
                s_conclaveBack = mine;
                SetElementOf(c.Player, best);
                Fire("Conclave");
            }
            bool advantage = theirs != null && c.Player.Stats.Element?.Value is { } now && c.Action.GetElementalBonusKind(now, theirs, true) == BonusKind.Positive;
            SetIf("Conclave:fight", ConclaveSpire, advantage, () => A(("Damage", 15)));
        }
        // Hollow Harvest: the ward is full at the start of each fight
        if (Wears(HollowHarvestCrown) && s_combat is { } c3) c3.Player.Ward.Set(c3.Player.Ward.Max);
        Triangle_Update();
    }

    static Element? s_conclaveBack;

    static void FightEnds2()
    {
        if (s_conclaveBack != null && s_combat is { } c) SetElementOf(c.Player, s_conclaveBack);
        s_conclaveBack = null;
    }

    static void Tick2(double dt)
    {
        // Hollow Harvest: 1 ward every 4 s, not in the 2 s after you are hit
        if (Wears(HollowHarvestCrown) && s_combat is { } c && c.Player.Ward.Current < c.Player.Ward.Max && s_nowMs - s_lastHitMs >= 2000
            && (s_hollowMs += dt) >= 4000)
        {
            s_hollowMs = 0;
            c.Player.Ward.Set(c.Player.Ward.Current + 1);
        }
        Oath_Update();
        // Blood Rite: +1% damage for every 2% of hit points missing (at most +30%), with magic
        if (Wears(SanguineLiturgy) && s_combat is { } c4)
        {
            double missing = 1 - (double)c4.Player.Health.Current / Math.Max(1, c4.Player.Health.Max);
            double bonus = Math.Min(30, Math.Floor(missing * 100 / 2));
            if (bonus != F("Sanguine:bonus"))
            {
                s_fight["Sanguine:bonus"] = bonus;
                SetIf("Sanguine:blood", SanguineLiturgy, bonus > 0 && MagicWeapon(), () => A(("Damage", bonus)));
            }
        }
    }

    static double s_hollowMs, s_lastHitMs;

    static void Looted(EnemyLootedEventArgs e)
    {
        // Hoarder's Eye: a drop whose chance is 5% or less: +25% damage and +15% rare item chance for 90 s
        if (!Wears(HoarderSPrism)) return;
        foreach (var drop in e.Items)
        {
            double chance = 0;
            foreach (var table in e.Enemy.Drops.ItemTables)
                foreach (var entry in table.Table.Items.Where(w => w.Item == drop.Item))
                    chance += table.Chance * table.Table.GetItemChance(entry);
            if (chance > 0 && chance <= 0.05)
            {
                Fire("Hoarder");
                B("Hoarder", HoarderSPrism).Set(A(("Damage", 25)), 90_000);
                return;
            }
        }
    }

    static void Ate2()
    {
        // Hollow Harvest: eating restores 1 ward (once in 2 s)
        if (Wears(HollowHarvestCrown) && s_combat is { } c && s_nowMs >= s_keep.GetValueOrDefault("Hollow:eat"))
        {
            s_keep["Hollow:eat"] = s_nowMs + 2000;
            c.Player.Ward.Set(Math.Min(c.Player.Ward.Max, c.Player.Ward.Current + 1));
        }
    }

    static void Killed2()
    {
        // Sealed Prayers: each kill gives 15 prayer points
        if (Wears(DuneVizierWrappings) && s_combat is { } c && c.Prayer.CostCurrencies.FirstOrDefault() is { } points && Fire("DunePoints"))
            c.Prayer.Wallet.AddQuantity(points, 15);
    }
}
