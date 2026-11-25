using System;
using System.Collections.Generic;
using System.Linq;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatEffects;
using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Modifiers;

namespace OneStyle;

/// <summary>The powers, by what starts them. Each line is the power's own text in data/uniques.json.</summary>
public static partial class UniquePowers
{
    const string CopperOath = "OsUCopperOath", StitchedFurrows = "OsUStitchedFurrows", RingbarkReprisal = "OsURingbarkReprisal",
        HoarderSPrism = "OsUHoarderSPrism", CombKnuckles = "OsUCombKnuckles", MireThroat = "OsUMireThroat", Snarestep = "OsUSnarestep",
        ThicketTrophies = "OsUThicketTrophies", CaravanSeals = "OsUCaravanSeals", Skytoll = "OsUSkytoll", BreakerHooves = "OsUBreakerHooves",
        SovereignDiadem = "OsUSovereignDiadem", LanternVeil = "OsULanternVeil", Wreckshot = "OsUWreckshot", Gatebreaker = "OsUGatebreaker",
        CavernBastion = "OsUCavernBastion", Goremantle = "OsUGoremantle", ThunderTraces = "OsUThunderTraces", StarboundMantle = "OsUStarboundMantle",
        SoulstringQuiver = "OsUSoulstringQuiver", RootShearClaws = "OsURootShearClaws", ThornstepSpinecoat = "OsUThornstepSpinecoat",
        TollkeeperGreaves = "OsUTollkeeperGreaves", TwinEyeLongwatch = "OsUTwinEyeLongwatch", PlungeheartTusk = "OsUPlungeheartTusk",
        VeilAntlerReprieve = "OsUVeilAntlerReprieve", AbyssalBeacon = "OsUAbyssalBeacon", ScrapwindTreads = "OsUScrapwindTreads",
        DreamtideOrb = "OsUDreamtideOrb", OssuaryMagazine = "OsUOssuaryMagazine", BroodSignGrips = "OsUBroodSignGrips", CaravanWarcloak = "OsUCaravanWarcloak";

    /// <summary>A bonus from the affix table (values as the player reads them: Keen 40 = +40% max hit).</summary>
    static ModifierValues A(params (string Affix, double Value)[] parts)
    {
        // "Damage" is the game's own damage dealt (every hit, not only the max hit)
        ModifierValues table = RareItems.AffixBonuses(s_game!, parts.Where(p => p.Affix != "Damage")
            .Select(p => new RolledAffix { Id = p.Affix, Tier = 1, Value = p.Value }), null);
        double damage = parts.Where(p => p.Affix == "Damage").Sum(p => p.Value);
        if (damage == 0) return table;
        var component = table.Component.ToList();
        component.Add(P("PercentDamageDealt", damage / 100));
        return new ModifierValues(table.Global.ToList(), component, table.CombatEffect.ToList());
    }

    static void SetIf(string key, string owner, bool on, Func<ModifierValues> values, double ms = -1)
    {
        Buff b = B(key, owner);
        if (on && (owner == "" || Wears(owner))) b.Set(values(), ms);
        else b.Off();
    }

    static double Kept(string key) => RareItems.Data.Counters.GetValueOrDefault(key);
    static void Keep(string key, double v) => RareItems.Data.Counters[key] = v;

    static bool RangedWeapon() => s_combat?.Player.Equipment.CurrentWeapon is { } w && RareItems.StyleOf(w.Item) == "MelvorBase:Ranged";

    // ================================================================ always on while worn

    /// <summary>
    /// The bonuses that are on whenever the item is worn. "Offline:" on each says what happens to it while the game
    /// works out offline time (the rule is at the top of UniquePowers.cs, the list is KeptOffline there): the lines
    /// here are written as if you were playing, and Buff.Set leaves out what is off for that time.
    /// </summary>
    static void AlwaysOn()
    {
        // Offline: kept. The price (food heals less) and what it pays for (lifesteal) are both the game's own stats
        SetIf("MireThroat", MireThroat, true, () => A(("Leeching", 8), ("Mending", -40)));
        // Offline: kept. A plain stat
        SetIf("CombKnuckles:thrift", CombKnuckles, true, () => A(("Thrift", 15)));
        // Offline: OFF. The bleed that pays for -20% is scripted
        SetIf("RootShear", RootShearClaws, true, () => A(("Damage", -20)));
        // Offline: OFF. The 2.5 times reflect that pays for -10% is scripted
        SetIf("Thornstep", ThornstepSpinecoat, true, () => A(("Damage", -10)));
        // Offline: kept. The slower attack is paid for by accuracy and the least hit, plain stats in the same bonus
        SetIf("TwinEye", TwinEyeLongwatch, true, () => A(("Swift", -60), ("Steady", 80), ("True", 1000)));
        // a unique quiver is one item: it is never used up when you shoot
        // Offline: kept. It costs nothing
        SetIf("UniqueAmmo", "", Wears(SoulstringQuiver) || Wears(OssuaryMagazine), () => A(("Quiver", 100)));
        // Offline: kept. Steps earned for good, saved with the character
        Wreck();
        // Offline: off. Eating clears the Hunger, and nothing counts the meals offline
        Goremantle_Update();
        // Offline: the stacks' damage kept (saved; the task's end clears them offline too), the faster task monsters off
        Scrap_Update();
        AlwaysOn2();
        AlwaysOn3();
    }

    // ================================================================ a new monster

    static void FightStarts()
    {
        if (Wears(CopperOath)) B("CopperOath:fight", CopperOath).Set(A(("Damage", 40)));
        if (Wears(TwinEyeLongwatch)) B("TwinEye:first:fight", TwinEyeLongwatch).Set(A(("Deadly", 100)));
        if (Wears(Gatebreaker) && s_keep.GetValueOrDefault("Gate:open") > 0 && Fire("GateOpen"))
        {
            s_keep["Gate:open"] = 0;
            B("Gate:open", Gatebreaker).Set(A(("Damage", 40)), 10000);
        }
        B("Starbound:fight", StarboundMantle).Off();
        // Skytoll: what the last kill left strikes the new monster as it appears
        if (Wears(Skytoll) && s_keep.GetValueOrDefault("Skytoll") is > 0 and var toll)
        {
            s_keep["Skytoll"] = 0;
            After(50, Skytoll, () =>
            {
                if (Enemy is { } enemy && Fire("Skytoll"))
                {
                    int cap = (int)(enemy.Health.Max * 0.40);
                    StrikeAsAttack((int)Math.Min(toll, cap), cap: cap);
                }
            });
        }
        FightReady();
        FightStarts2();
    }

    /// <summary>The elite is known now (the game raises FightStarted after Elites made it).</summary>
    static void FightReady()
    {
        bool elite = EnemyIsElite;
        if (Wears(CopperOath) && elite && BuffOn("CopperOath:fight")) B("CopperOath:fight", CopperOath).Set(A(("Damage", 120)));
        SetIf("Gatebreaker:fight", Gatebreaker, elite, () => A(("Damage", 40)));
        if (Wears(CaravanSeals) && elite && s_keep.GetValueOrDefault("CaravanSeals:seal") >= 1)
        {
            s_keep["CaravanSeals:seal"] = 0;
            s_fight["CaravanSeals:left"] = 5;
            B("CaravanSeals:fight", CaravanSeals).Set(A(("Deadly", 30)));
        }
        if (Wears(Wreckshot) && elite && Kept("Wreckshot") >= 100) B("Wreckshot:elite:fight", Wreckshot).Set(A(("Damage", 25)));
        Scrap_Update();
    }

    // ================================================================ your hits

    static int s_lastEnemyHp;

    static void PlayerAttacked(AttackEventArgs e)
    {
        CharacterComponent enemy = s_combat!.Enemy;
        bool hit = e.WasHit;
        bool firstOfAttack = e.HitIndex == 0;
        // the hit points before THIS hit (not the last hit of yours: damage over time and strikes come in between)
        int hpBefore = enemy.Health.Current + e.HealthDamage;
        int hpAfter = enemy.Health.Current;
        double maxHp = Math.Max(1, enemy.Health.Max);

        if (firstOfAttack)
        {
            PlayerAttackStarted2(e);
            // Twin-Eye: the first shot of a fight crits
            B("TwinEye:first:fight", TwinEyeLongwatch).Off();
            // Comb Knuckles: three attacks after a meal
            if (BuffOn("CombKnuckles:fight") && Up(s_fight, "CombKnuckles:used") >= 3) B("CombKnuckles:fight").Off();
            // Bone Rain: every 4th ranged attack deals double damage (the bonus is on for it, then off)
            if (Wears(OssuaryMagazine) && RangedWeapon())
            {
                B("Ossuary:fight").Off();
                if (Up(s_keep, "Ossuary") % 4 == 3 && Fire("Ossuary")) B("Ossuary:fight", OssuaryMagazine).Set(A(("Damage", 100)));
            }
            // Soulstring Quiver: every 5th successful ranged attack (4th against elites) shoots once more for free
            if (hit && Wears(SoulstringQuiver) && RangedWeapon())
            {
                double n = Up(s_keep, "Soulstring");
                if (n >= (EnemyIsElite ? 4 : 5))
                {
                    s_keep["Soulstring"] = 0;
                    After(150, SoulstringQuiver, () => { Fire("Soulstring"); StrikeAsAttack(RollHit(0.75, canCrit: true)); });
                }
            }
        }
        if (!hit) { s_lastEnemyHp = hpAfter; return; }
        double hits = Up(s_fight, "hits");
        PlayerAttacked2(e);
        PlayerAttacked3(e);

        // the next successful hit: Copper Oath, Snarestep's charge, Dream Tide's opening
        B("CopperOath:fight").Off();
        B("Snarestep:fight").Off();
        B("Dreamtide:open:fight").Off();
        // Caravan Seals: three hits with +25% crit
        if (BuffOn("CaravanSeals:fight") && Up(s_fight, "CaravanSeals:used") >= 5) B("CaravanSeals:fight").Off();
        // Bark's Due: the reflected damage stored is added to this hit
        if (Wears(RingbarkReprisal) && s_keep.GetValueOrDefault("Ringbark") is > 0 and var pool)
        {
            s_keep["Ringbark"] = 0;
            Fire("Ringbark");
            s_inOwnStrike = true;
            try { Strike((int)pool, HealthChangeCause.CombatEffect); } finally { s_inOwnStrike = false; }
        }
        // Sum of Stars: +2% max hit a successful hit, at most +40%, for this fight
        if (Wears(StarboundMantle))
        {
            double stacks = Math.Min(20, Up(s_fight, "Starbound"));
            B("Starbound:fight", StarboundMantle).Set(A(("Damage", 2 * stacks)));
        }
        // Queen's Charge: the hit that takes a living monster from above half its hit points to half or below
        if (Wears(SovereignDiadem) && F("Sovereign") == 0 && hpAfter > 0 && hpBefore > maxHp * 0.5 && hpAfter <= maxHp * 0.5)
        {
            s_fight["Sovereign"] = 1;
            Fire("Sovereign");
            B("Sovereign:fight", SovereignDiadem).Set(A(("Swift", 25)), 4000);
        }
        // Eighth Wave: every 8th successful hit takes one poison, bleed or burn off you
        if (Wears(BreakerHooves) && hits % 6 == 0) { Fire("Breaker"); if (!CleanseOne()) HealPlayer((int)(s_combat.Player.Health.Max * 0.02)); }
        // Light's Share: 20% to burn for 4% of the hit a second, 4 s
        if (Wears(LanternVeil) && Random.Shared.NextDouble() < 0.20 && Fire("Lantern")) AddDot(LanternVeil, "LanternBurn", e.HealthDamage * 0.04, 4);
        // Deep Wound: 25% to bleed for 30% of the hit over 4 s (5 stacks, can crit); a hit on a bleeding monster keeps them going
        if (Wears(RootShearClaws))
        {
            foreach (Dot d in s_dots.Where(d => d.Key == "RootBleed" && d.EnemyGeneration == s_generation)) d.LeftMs = 4000;
            if (Random.Shared.NextDouble() < 0.25 && Fire("RootShear")) AddDot(RootShearClaws, "RootBleed", e.HealthDamage * 0.30 / 4, 4, maxStacks: 5, canCrit: true);
        }
        // Delayed Thunder: every 4th successful hit strikes again 2 s later
        if (Wears(ThunderTraces) && hits % 4 == 0)
        {
            int amount = (int)Math.Min(e.HealthDamage * 0.70, maxHp * 0.15);
            int gen = s_generation;
            After(2000, ThunderTraces, () =>
            {
                // "that amount strikes again": as it is, not through your damage bonuses and the monster's armour a second time
                if (gen == s_generation && Enemy is { Health.Current: > 0 } && Fire("Thunder"))
                    Strike(amount, HealthChangeCause.Attack);
            });
        }
        // Ice Shatter: every 5th successful hit freezes (count kept between monsters)
        if (Wears(PlungeheartTusk) && Up(s_keep, "Plunge") % 5 == 0 && hpAfter > 0)
        {
            Fire("PlungeFreeze");
            StunEnemy("MelvorBase:Freeze", 1);
            if (!EnemyFrozen())
            {
                B("Plunge:brittle:fight", PlungeheartTusk).Set(A(("Damage", 20)), EnemyIntervalMs());
                Strike((int)Math.Min(maxHp * 0.03 * (EnemyIsElite ? 0.5 : 1), s_combat.Player.Stats.MaxHit.Value * 2));
            }
        }
        // Dream Tide: every 4th successful hit puts the monster to sleep (once in 8 s)
        if (Wears(DreamtideOrb) && hits % 4 == 0 && hpAfter > 0 && s_nowMs >= F("Dreamtide:next"))
        {
            s_fight["Dreamtide:next"] = s_nowMs + 8000;
            Fire("Dreamtide");
            StunEnemy("MelvorBase:Sleep", 1);
            if (!EnemyStunned()) { s_fight["Dreamtide:drowsy"] = 1; B("Dreamtide:drowsy:fight", DreamtideOrb).Set(A(("Stalwart", 25))); }
        }
        // Thousand Eggs: every 3rd successful hit, a spiderling (3 at most) for 8 s
        if (Wears(BroodSignGrips) && hits % 3 == 0) { Fire("Spiderling"); Spiderling(); }
        // Caravan Road, Crush: every 4th hit stuns (once in 8 s)
        if (Wears(CaravanWarcloak) && Stance() == "Crush" && hits % 4 == 0 && hpAfter > 0 && s_nowMs >= F("Caravan:next"))
        {
            s_fight["Caravan:next"] = s_nowMs + 8000;
            Fire("CaravanCrush");
            StunEnemy("MelvorBase:Stun", 1);
        }
        s_lastEnemyHp = hpAfter;
    }

    // ================================================================ the monster's attacks

    // the monster's attack in progress (its hits come between its turn start and turn end)
    static bool s_enemyLanded, s_enemyMissed;

    static void EnemyAttacked(AttackEventArgs e)
    {
        if (e.WasHit) s_enemyLanded = true;
        // a miss is a miss you avoided: an attack that could not hurt you at all (immunity) is not one
        else if (!(s_combat is { } c && c.Enemy.HasTarget && c.Enemy.TargetStats.ImmunityFrom.Total.Value > 0)) s_enemyMissed = true;
    }

    // ================================================================ damage

    static void EnemyDamaged(HealthDamagedEventArgs e)
    {
        var enemyHealth = s_combat!.Enemy.Health;
        if (enemyHealth.Current <= 0 && e.Amount > 0)
        {
            // Rising Toll: your killing blow keeps its overkill and 8% of the monster's hit points for the next one
            if (Wears(Skytoll) && e.Cause == HealthChangeCause.Attack && ReferenceEquals(e.SourceCharacter, s_combat.Player))
                s_keep["Skytoll"] = Math.Max(0, e.Damage.Modified - e.Amount) + enemyHealth.Max * 0.08;
            // Light's Share: the monster dies carrying a poison, bleed or burn (the game clears them before "killed")
            if (Wears(LanternVeil) && (EnemyHasOurDot() || EnemyHasDot()) && Fire("LanternLight"))
            {
                s_keep["Lantern:light"] = 1;
                B("Lantern:light", LanternVeil).Set(A(("Thrift", 100)));
            }
        }
        if (e.Cause != HealthChangeCause.Reflect || !ReferenceEquals(e.SourceCharacter, s_combat!.Player) || e.Amount <= 0) return;
        if (s_inOwnStrike) return;
        // Bark's Due: reflected damage is stored for your next successful hit (at most 150% of your max hit)
        if (Wears(RingbarkReprisal))
            s_keep["Ringbark"] = Math.Min(s_keep.GetValueOrDefault("Ringbark") + e.Amount, s_combat.Player.Stats.MaxHit.Value * 1.5);
        // Thorn Dance: reflected damage hits 2.5 times (can crit) and may bleed
        if (Wears(ThornstepSpinecoat))
        {
            Fire("Thornstep");
            double extra = e.Amount * 1.5;
            if (Random.Shared.NextDouble() < s_combat.Player.Stats.CriticalHitChance.Value) extra = (e.Amount + extra) * s_combat.Player.Stats.CriticalHitMultiplier.Value - e.Amount;
            s_inOwnStrike = true;
            try { Strike((int)Math.Round(extra), HealthChangeCause.CombatEffect); } finally { s_inOwnStrike = false; }
            if (Random.Shared.NextDouble() < 0.25) AddDot(ThornstepSpinecoat, "ThornBleed", (e.Amount + extra) * 0.30 / 4, 4, maxStacks: 3);
        }
    }

    static bool s_inOwnStrike;
    static double s_tollDebt;

    static void PlayerDamaged(HealthDamagedEventArgs e)
    {
        var player = s_combat!.Player;
        // Last Breath: 15% of a hit's direct damage comes back and is taken over 4 s instead (it cannot kill you)
        if (Wears(TollkeeperGreaves) && e.Cause == HealthChangeCause.Attack && e.Amount > 0 && player.Health.Current > 0)
        {
            // small hits: the parts of a point add up until they make a whole one
            s_tollDebt += e.Amount * 0.15;
            int delayed = (int)Math.Floor(s_tollDebt);
            s_tollDebt -= delayed;
            if (delayed > 0)
            {
                Fire("Tollkeeper");
                player.Health.Heal(delayed, HealthChangeCause.Lifesteal, player, player);
                s_dots.Add(new Dot { Key = "TollDelay", Owner = TollkeeperGreaves, OnPlayer = true, PerSecond = delayed / 4.0, LeftMs = 4000 });
            }
        }
        if (e.Cause == HealthChangeCause.Attack) s_lastHitMs = s_nowMs;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = "set_IsDying")]
    static extern void SetIsDying(CharacterComponent character, bool value);

    /// <summary>Back From the Veil: below 20% hit points (or a blow that would kill), once in 3 minutes of fighting.</summary>
    static void VeilAntler()
    {
        var player = s_combat!.Player;
        if (!Wears(VeilAntlerReprieve) || Kept("VeilAntler:left") > 0 || player.Health.Current >= player.Health.Max * 0.20) return;
        Fire("VeilAntler");
        Keep("VeilAntler:left", 180_000);
        if (player.Health.Current <= 0)
        {
            // the game has already marked you as dying at 0 hit points (its death step would kill you on the next
            // tick whatever your hit points are by then): the mark comes off with the save
            player.Health.Set(1);
            if (player.IsDying) SetIsDying(player, false);
        }
        HealPlayer((int)(player.Health.Max * 0.40));
        B("VeilAntler:guard", VeilAntlerReprieve).Set(A(("Warding2", 100)), 3000);
        B("VeilAntler:fury", VeilAntlerReprieve).Set(A(("Damage", 30), ("Swift", 15)), 15000);
    }

    static void PlayerHealthChanged()
    {
        var health = s_combat!.Player.Health;
        VeilAntler();
        PlayerHealthChanged3();
        // Last Breath: your hit points stay at half of the most at most
        if (Wears(TollkeeperGreaves) && health.Current > health.Max / 2) health.Set(health.Max / 2);
        if (Wears(ThicketTrophies)) Trophies_Update();
    }

    // ================================================================ kills and loot

    static void Killed(EnemyKilledEventArgs e)
    {
        bool elite = Elites.Tier != EliteTier.Normal && ReferenceEquals(e.Enemy, Elites.Enemy);
        // Unsated Triumph: +3% max hit a kill, 10 at most
        if (Wears(Goremantle)) { s_keep["Gore"] = Math.Min(10, s_keep.GetValueOrDefault("Gore") + 1); Goremantle_Update(); }
        // Four-Seal Passage: every 4 normal kills ready a seal for the next elite
        if (Wears(CaravanSeals) && !elite && Up(s_keep, "CaravanSeals:kills") >= 3) { s_keep["CaravanSeals:kills"] = 0; s_keep["CaravanSeals:seal"] = 1; }
        // Headhunter's Boughs: an elite's affixes as trophies
        if (Wears(ThicketTrophies) && elite) Trophies_Add(Elites.Affixes);
        // Gate's Verdict: breaking an elite gives the next fight +40% damage for 10 s
        if (Wears(Gatebreaker) && elite) s_keep["Gate:open"] = 1;
        // Sunken Reckoning: elite kills, kept with the character
        if (Wears(Wreckshot) && elite) { Keep("Wreckshot", Kept("Wreckshot") + 1); Wreck(); }
        // Scrap Hunter
        Scrap_Kill(e, elite);
        Killed2();
        // Sovereign: the bonus ends with the monster
        B("Sovereign:fight").Off();
    }

    static void Ate(FoodEatenEventArgs e)
    {
        // Spoiled Rations: poison the monster for 8% of what was healed a second, 6 s (3 at most, one new in 1.5 s)
        if (Wears(StitchedFurrows) && Enemy != null && s_nowMs >= s_keep.GetValueOrDefault("Stitched:next"))
        {
            Fire("Stitched");
            s_keep["Stitched:next"] = s_nowMs + 1500;
            AddDot(StitchedFurrows, "StitchedPoison", e.Healing * 0.08, 6, maxStacks: 3);
        }
        // Honey Frenzy: the next 3 attacks +25% max hit and 15% faster (once in 8 s)
        if (Wears(CombKnuckles) && s_nowMs >= s_keep.GetValueOrDefault("Comb:next") && !BuffOn("CombKnuckles:fight"))
        {
            Fire("Comb");
            s_keep["Comb:next"] = s_nowMs + 8000;
            s_fight["CombKnuckles:used"] = 0;
            B("CombKnuckles:fight", CombKnuckles).Set(A(("Damage", 25), ("Swift", 15)));
        }
        // Unsated Triumph: eating clears the hunger
        if (Wears(Goremantle)) { s_keep["Gore"] = 0; Goremantle_Update(); }
        // Light's Share: the free meal is used
        if (s_keep.GetValueOrDefault("Lantern:light") > 0) { s_keep["Lantern:light"] = 0; B("Lantern:light").Off(); }
        Ate2();
        Ate3(e);
    }

    static void CombatStopped()
    {
        s_keep["Skytoll"] = 0;
        s_lastEnemyHp = 0;
        foreach (var k in s_spiders.ToList()) s_spiders.Remove(k);
    }

    // ================================================================ every tick of fighting

    static double s_fightTotalMs => s_nowMs;
    static double s_slowMs;
    static bool s_wasFrozen;

    static void Tick(double dt)
    {
        if (s_worn.Count == 0) return;
        // Back From the Veil: its 3 minutes count down only while you fight (kept in the save)
        if (Kept("VeilAntler:left") > 0) Keep("VeilAntler:left", Math.Max(0, Kept("VeilAntler:left") - dt));
        // Ice Shatter: frozen monsters take +40%; when the freeze ends on a living monster it shatters
        if (Wears(PlungeheartTusk))
        {
            bool frozen = EnemyFrozen();
            SetIf("Plunge:frozen:fight", PlungeheartTusk, frozen, () => A(("Damage", 40)));
            if (s_wasFrozen && !frozen && Enemy is { Health.Current: > 0 } enemy)
                { Fire("PlungeShatter"); Strike((int)Math.Min(enemy.Health.Max * 0.06 * (EnemyIsElite ? 0.5 : 1), s_combat!.Player.Stats.MaxHit.Value * 2)); }
            s_wasFrozen = frozen;
        }
        // Dream Tide: the first hit on a sleeping or stunned monster
        if (Wears(DreamtideOrb))
        {
            bool down = EnemyStunned();
            if (down && F("Dreamtide:openedFor") != s_generation * 1000 + F("Dreamtide:stuns"))
            {
                s_fight["Dreamtide:openedFor"] = s_generation * 1000 + F("Dreamtide:stuns");
                B("Dreamtide:open:fight", DreamtideOrb).Set(A(("Damage", 80), ("True", 1000)));
            }
            if (!down && F("Dreamtide:wasDown") > 0) Up(s_fight, "Dreamtide:stuns");
            s_fight["Dreamtide:wasDown"] = down ? 1 : 0;
        }
        // Headhunter's Boughs: trophies run out after 90 s of fighting
        if (Wears(ThicketTrophies) && s_trophies.RemoveAll(t => t.Until <= s_nowMs) > 0) Trophies_Update();
        // Caravan Road, Stab: +30% accuracy
        SetIf("Caravan:stab", CaravanWarcloak, Stance() == "Stab", () => A(("True", 30)));
        // spiderlings
        SpiderTick();
        Tick2(dt);
        Tick3();
        // prayers and stance can change at any time: looked at once a second
        if ((s_slowMs += dt) >= 1000)
        {
            s_slowMs = 0;
            Prayers_Update();
            Amber_Update();
            SetIf("Caravan:slash", CaravanWarcloak, Stance() == "Slash", () => A(("Serrated", 20)));
        }
    }

    // ================================================================ helpers of the powers

    static int EnemyIntervalMs() => s_combat?.Enemy.Stats.AttackInterval.Value is double v ? (int)v : 2400;

    static bool EnemyFrozen() =>
        s_combat is { } c && Enemy is { } enemy
        && c.CombatEffects.GetActiveEffectsForCharacter(enemy).Any(a => a is ActiveStunEffect && a.Data.Id == "MelvorBase:Freeze");

    static readonly string[] s_dotIds = { "MelvorBase:Poison", "MelvorBase:Bleed", "MelvorBase:Burn" };

    static bool EnemyHasDot() =>
        s_combat is { } c && Enemy is { } enemy && c.CombatEffects.GetActiveEffectsForCharacter(enemy).Any(a => s_dotIds.Contains(a.Data.Id));

    /// <summary>Eighth Wave: one poison, then bleed, then burn off you. False when there was none.</summary>
    static bool CleanseOne()
    {
        var c = s_combat!;
        var mine = c.CombatEffects.GetActiveEffectsForCharacter(c.Player).ToList();
        foreach (string id in s_dotIds)
            if (mine.FirstOrDefault(a => a.Data.Id == id) is { } effect) { c.CombatEffects.RemoveEffect(effect); return true; }
        return false;
    }

    /// <summary>Stab, Slash, Crush, Block, Accurate... of the stance you fight with.</summary>
    static string Stance()
    {
        try { return s_combat?.Player.Stats.AttackStance?.Value?.Id.Replace("MelvorBase:", "") ?? ""; }
        catch { return ""; }
    }

    static void Goremantle_Update() => SetIf("Gore", Goremantle, s_keep.GetValueOrDefault("Gore") > 0, () => A(("Damage", 3 * s_keep.GetValueOrDefault("Gore"))));

    static void Wreck()
    {
        double n = Kept("Wreckshot");
        var parts = new List<(string, double)>();
        if (n >= 25) parts.Add(("Damage", 10));
        if (n >= 50) parts.Add(("Deadly", 6));
        if (n >= 75) parts.Add(("Swift", 8));
        SetIf("Wreckshot", Wreckshot, parts.Count > 0, () => A(parts.ToArray()));
    }

    /// <summary>Sunken Reckoning at 125 elite kills: one more loot roll for every elite kill.</summary>
    public static int ExtraEliteLootRolls => Wears(Wreckshot) && Kept("Wreckshot") >= 125 ? 1 : 0;

    // ---- Headhunter's Boughs

    static readonly List<(string Affix, double Until)> s_trophies = new();

    static void Trophies_Add(IEnumerable<string> affixes)
    {
        var list = affixes.Where(a => a is "Hasted" or "Colossal" or "Warded" or "Armoured" or "Vampiric" or "Enraging").ToList();
        if (list.Count == 0) return;
        foreach (string a in list.OrderBy(_ => Random.Shared.Next()).Take(Random.Shared.Next(1, 3)))
        {
            if (s_trophies.Count >= 3) s_trophies.RemoveAt(0);
            s_trophies.Add((a, s_nowMs + 90_000));
        }
        Trophies_Update();
    }

    static void Trophies_Update()
    {
        if (s_trophies.Count == 0 || s_combat == null) { B("Trophies").Off(); return; }
        double interval = 0, hp = 0, taken = 0, steal = 0, dmg = 0;
        bool low = s_combat.Player.Health.Current < s_combat.Player.Health.Max * 0.60;
        foreach (var (a, _) in s_trophies)
            switch (a)
            {
                case "Hasted": interval += 12; break;
                case "Colossal": hp += 20; dmg += 8; break;
                case "Warded": hp += 15; break;
                case "Armoured": taken += 12; break;
                case "Vampiric": steal += 5; break;
                case "Enraging": if (low) { dmg += 15; interval += 8; } break;
            }
        B("Trophies", ThicketTrophies).Set(A(("Swift", Math.Min(40, interval)), ("Bear", Math.Min(60, hp)), ("Stalwart", Math.Min(35, taken)),
            ("Leeching", Math.Min(8, steal)), ("Damage", dmg)));
    }

    public static IReadOnlyList<string> Trophies => s_trophies.Select(t => t.Affix).ToList();

    // ---- Thousand Eggs

    static readonly List<(double Until, double Next)> s_spiders = new();

    static void Spiderling()
    {
        if (s_spiders.Count >= 3) return;
        s_spiders.Add((s_nowMs + 8000, s_nowMs + 2000));
    }

    static void SpiderTick()
    {
        if (s_spiders.Count == 0 || s_combat == null) return;
        for (int i = s_spiders.Count - 1; i >= 0; i--)
        {
            var (until, next) = s_spiders[i];
            if (until <= s_nowMs || !Wears(BroodSignGrips)) { s_spiders.RemoveAt(i); continue; }
            if (next <= s_nowMs)
            {
                s_spiders[i] = (until, next + 2000);
                if (RollToHit() && Fire("SpiderBite")) StrikeAsAttack((int)Math.Round(s_combat.Player.Stats.MaxHit.Value * 0.20));
            }
        }
    }

    public static int Spiderlings => s_spiders.Count;

    // ---- Scrap Hunter (and the extra drops of Deep Signal)

    static void Scrap_Kill(EnemyKilledEventArgs e, bool elite)
    {
        if (!Wears(ScrapwindTreads) || s_combat?.SlayerTasks is not { } tasks) return;
        // (the game counts the kill and may complete the task before this: a completed task's last kill gives no stack)
        if (tasks.HasTaskForEnemy(e.Enemy)) Keep("Scrap:stacks", Math.Min(30, Kept("Scrap:stacks") + 1));
        Scrap_Update();
    }

    /// <summary>
    /// The slayer task really ended (the game's own event, not a guess from the next kill): the stacks go; a completed
    /// task leaves one token for the next elite, a rerolled one does not.
    /// </summary>
    static void Scrap_TaskOver(bool completed)
    {
        if (!Wears(ScrapwindTreads) || Kept("Scrap:stacks") <= 0) return;
        Keep("Scrap:stacks", 0);
        if (completed) Keep("Scrap:token", 1);
        Scrap_Update();
    }

    static void Scrap_Update()
    {
        double stacks = Kept("Scrap:stacks");
        bool taskMonster = s_combat?.SlayerTasks is { } t && Enemy is { } && s_combat.Enemy.Enemy is { } en && t.HasTaskForEnemy(en);
        SetIf("Scrap", ScrapwindTreads, stacks > 0, () => A(("Damage", stacks)));
        SetIf("Scrap:haste", ScrapwindTreads, taskMonster, () => A(("Haste", 30)));
    }

    /// <summary>How much more often a normal monster drops a rare item (Hoarder's Eye, Scrap Hunter).</summary>
    public static double RareLootMultiplier =>
        1 + (BuffOn("Hoarder") ? 0.15 : 0) + (Wears(ScrapwindTreads) ? Kept("Scrap:stacks") / 100 : 0);

    /// <summary>Rare items (3 affixes) an elite kill adds: Deep Signal, and a Scrap Hunter token.</summary>
    public static int ExtraEliteRares()
    {
        int n = 0;
        if (Wears(AbyssalBeacon)) n++;
        if (Wears(ScrapwindTreads) && Kept("Scrap:token") > 0) { Keep("Scrap:token", 0); n++; }
        return n;
    }
}
