using System;
using System.Collections.Generic;
using System.Linq;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatEffects;
using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Modifiers;
using GamesByMalcs.Melvor2.Core;

namespace OneStyle;

/// <summary>
/// The special powers of the 58 unique items. Each power is worked by One Style from the game's own events while you
/// play: a hit of yours (CharacterComponent.Attacked, raised after the hit's damage), an attack of the monster, a kill,
/// a new monster, eating, and the fight's ticks. What a power gives goes into the game's modifier system as a bonus of
/// its own (so the game's numbers show it), or is done directly: extra damage, healing, ward, a stun.
/// Like the elites, powers work only while you play, not while the game works out the time it was closed.
///
/// This file: the shared parts. UniquePowers.*.cs: the powers.
/// </summary>
public static partial class UniquePowers
{
    public static Action<string> Log = _ => { };
    static Game? s_game;
    static CombatEntity? s_combat;
    static int s_mainThread;

    /// <summary>The unique items worn now (their ids, "OsUCopperOath"...).</summary>
    static readonly HashSet<string> s_worn = new();
    public static bool Wears(string id) => s_worn.Contains(id);
    public static IReadOnlyCollection<string> Worn => s_worn;

    static bool Offline() =>
        Environment.CurrentManagedThreadId != s_mainThread || MelvorModLoader.GameAccess.IsComputingOffline || s_game?.Flags.IsOffline == true;

    // ================================================================ for tests and the window

    /// <summary>Everything the powers count now: f: this fight, k: kept between fights, d: saved, b: bonuses on, dot: our DoTs.</summary>
    public static Dictionary<string, double> Snapshot()
    {
        var all = new Dictionary<string, double>();
        foreach (var (k, v) in s_fight) all["f:" + k] = v;
        foreach (var (k, v) in s_keep) all["k:" + k] = v;
        foreach (var (k, v) in RareItems.Data.Counters) all["d:" + k] = v;
        foreach (var (k, b) in s_buffs) if (b.On) all["b:" + k] = 1;
        foreach (var g in s_dots.GroupBy(d => d.Key)) all["dot:" + g.Key] = g.Count();
        all["spiders"] = s_spiders.Count;
        all["trophies"] = s_trophies.Count;
        return all;
    }

    static readonly Dictionary<string, int> s_fired = new();

    /// <summary>A power did its thing (counted for the tests and the window). Always true, to sit inside a condition.</summary>
    static bool Fire(string key) { s_fired[key] = s_fired.GetValueOrDefault(key) + 1; return true; }

    public static IReadOnlyDictionary<string, int> Fired => s_fired;

    /// <summary>For the One Style window: what each worn unique's power has counted or is waiting for.</summary>
    public static List<string> StatusLines()
    {
        var lines = new List<string>();
        string Name(string id) => Uniques.Defs.TryGetValue(id, out UniqueDef? d) ? d.Name : id;
        double K(string key) => s_keep.GetValueOrDefault(key);
        double D(string key) => RareItems.Data.Counters.GetValueOrDefault(key);
        foreach (string id in s_worn.OrderBy(Name))
        {
            string? state = id switch
            {
                "OsUWreckshot" => $"{D("Wreckshot"):0} elite kills (next step at {Math.Min(125, (Math.Floor(D("Wreckshot") / 25) + 1) * 25):0})",
                "OsUVeilAntlerReprieve" => D("VeilAntler:left") > 0 ? $"ready again after {D("VeilAntler:left") / 1000:0} s of fighting" : "ready",
                "OsUGoremantle" => $"{K("Gore"):0} Hunger (+{3 * K("Gore"):0}% damage)",
                "OsUThicketTrophies" => s_trophies.Count == 0 ? "no trophy" : "trophies: " + string.Join(", ", s_trophies.Select(t => $"{t.Affix} {Math.Max(0, (t.Until - s_nowMs) / 1000):0} s")),
                "OsUFourfoldWrit" => $"next seal {K("Fourfold:seal") % 4 + 1}, in {6 - K("casts") % 6:0} spells",
                "OsUScrapwindTreads" => $"{D("Scrap:stacks"):0} stacks{(D("Scrap:token") > 0 ? ", a token for the next elite" : "")}",
                "OsUCaravanSeals" => K("CaravanSeals:seal") > 0 ? "a seal is ready" : $"{K("CaravanSeals:kills"):0} of 3 kills",
                "OsUPlungeheartTusk" => $"freeze in {5 - K("Plunge") % 5:0} hits",
                "OsUSoulstringQuiver" => $"{K("Soulstring"):0} of 5 attacks to the free shot",
                "OsUCavernBastion" => $"{K("Cavern") % 5:0} of 5 hits taken",
                "OsULanternVeil" => K("Lantern:light") > 0 ? "Ration Light ready" : "no Ration Light",
                "OsURingbarkReprisal" => $"{K("Ringbark"):0} reflected damage stored",
                "OsUGatebreaker" => K("Gate:open") > 0 ? "the gate is open for the next fight" : "",
                "OsUBroodSignGrips" => $"{s_spiders.Count} spiderlings",
                _ => "",
            };
            lines.Add(state == "" ? $"{Name(id)}: its power is on" : $"{Name(id)}: {state}");
        }
        return lines;
    }

    static readonly Patches.Source s_testHp = new("One Style: test monster hit points");
    static bool s_testHpOn;

    /// <summary>For tests: monsters get so many times their hit points (1: back to normal), for fights long enough to count.</summary>
    public static void TestEnemyHp(double times, double accuracyTimes = 1)
    {
        if (Patches.Modifiers is not { } mods) return;
        if (s_testHpOn) { mods.Remove(s_testHp); s_testHpOn = false; }
        if (times <= 1 && accuracyTimes <= 1) return;
        mods.Add(s_testHp, Patches.Bonuses(Patches.Bonus("Character", "PercentMaxHitpoints", Math.Max(0, times - 1), "Enemy"),
            Patches.Bonus("Character", "PercentAccuracyRating", Math.Max(0, accuracyTimes - 1), "Enemy")));
        s_testHpOn = true;
    }

    // ================================================================ binding

    public static void Bind(Game game)
    {
        if (ReferenceEquals(s_game, game)) return;
        Unbind();
        if (!game.Entities.TryGetObjectById("MelvorBase:Combat", out var e) || e is not CombatEntity combat) return;
        s_game = game;
        s_combat = combat;
        s_mainThread = RareItems.MainThread;
        combat.Player.Attacked += OnPlayerAttack;
        combat.Enemy.Attacked += OnEnemyAttack;
        combat.Enemy.Health.Damaged += OnEnemyDamaged;
        combat.Player.Health.Damaged += OnPlayerDamaged;
        combat.Player.Health.CurrentChanged += OnPlayerHealth;
        combat.Enemy.Spawned += OnSpawned;
        combat.Player.FightStarted += OnFightStarted;
        combat.Player.FightEnded += OnFightEnded;
        combat.Player.AttackTurnStarted += OnPlayerTurnStart;
        combat.Player.AttackTurnExited += OnPlayerTurnEnd;
        combat.Enemy.AttackTurnStarted += OnEnemyTurnStart;
        combat.Enemy.AttackTurnExited += OnEnemyTurnEnd;
        combat.Loot.EnemyLooted += OnLooted;
        combat.MagicSpells.SelectedImbuementChanged += OnImbuement;
        combat.Enemy.Killed += OnKilled;
        combat.Action.Ticked += OnTicked;
        combat.Action.Stopped += OnStopped;
        combat.Food.FoodEaten += OnEaten;
        Refresh(game);
    }

    public static void Unbind()
    {
        if (s_combat is { } c)
        {
            c.Player.Attacked -= OnPlayerAttack;
            c.Enemy.Attacked -= OnEnemyAttack;
            c.Enemy.Health.Damaged -= OnEnemyDamaged;
            c.Player.Health.Damaged -= OnPlayerDamaged;
            c.Player.Health.CurrentChanged -= OnPlayerHealth;
            c.Enemy.Spawned -= OnSpawned;
            c.Player.FightStarted -= OnFightStarted;
            c.Player.FightEnded -= OnFightEnded;
            c.Player.AttackTurnStarted -= OnPlayerTurnStart;
            c.Player.AttackTurnExited -= OnPlayerTurnEnd;
            c.Enemy.AttackTurnStarted -= OnEnemyTurnStart;
            c.Enemy.AttackTurnExited -= OnEnemyTurnEnd;
            c.Loot.EnemyLooted -= OnLooted;
            c.MagicSpells.SelectedImbuementChanged -= OnImbuement;
            c.Enemy.Killed -= OnKilled;
            c.Action.Ticked -= OnTicked;
            c.Action.Stopped -= OnStopped;
            c.Food.FoodEaten -= OnEaten;
        }
        foreach (Buff b in s_buffs.Values) b.Off();
        s_buffs.Clear();
        s_timers.Clear();
        s_dots.Clear();
        s_fight.Clear();
        s_worn.Clear();
        s_game = null;
        s_combat = null;
    }

    /// <summary>Gear changed (or the character opened): which uniques are worn, and the powers that are always on.</summary>
    public static void Refresh(Game game)
    {
        if (s_combat == null) return;
        s_worn.Clear();
        foreach (EquipmentSlot slot in s_combat.Equipment.Config.EquipmentSlots)
            if (s_combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty && !a.IsOccupied
                && RareItems.RecordOf(a.Equipped!.Item) is { UniqueId: { Length: > 0 } id })
                s_worn.Add(id);
        try { AlwaysOn(); } catch (Exception e) { Log("One Style: unique powers: " + e.Message); }
        // powers whose item came off end at once
        foreach (var (key, b) in s_buffs.ToList())
            if (b.Owner != "" && !Wears(b.Owner)) b.Off();
    }

    // ================================================================ timed and lasting bonuses

    /// <summary>A bonus of one power in the game's modifier system; on until taken off, or for a time of fighting.</summary>
    sealed class Buff(string key, string owner)
    {
        public readonly string Key = key, Owner = owner;
        public readonly Patches.Source Source = new("One Style: " + key);
        public bool On;
        public double LeftMs = -1;   // -1: until taken off
        public void Set(ModifierValues values, double ms = -1, double scale = 1)
        {
            if (Patches.Modifiers is not { } mods) return;
            if (On) mods.Remove(Source);
            mods.Add(Source, values, scale);
            On = true;
            LeftMs = ms;
        }
        public void Off()
        {
            if (On && Patches.Modifiers is { } mods) mods.Remove(Source);
            On = false;
            LeftMs = -1;
        }
    }

    static readonly Dictionary<string, Buff> s_buffs = new();

    static Buff B(string key, string owner = "")
    {
        if (!s_buffs.TryGetValue(key, out Buff? b)) s_buffs[key] = b = new Buff(key, owner);
        return b;
    }

    public static bool BuffOn(string key) => s_buffs.TryGetValue(key, out Buff? b) && b.On;

    static ComponentModifierValue P(string name, double value) => Patches.Bonus("Character", name, value, "Player");
    static ModifierValues V(params ComponentModifierValue[] values) => Patches.Bonuses(values);

    // ================================================================ counters

    /// <summary>Counters of the current fight (cleared for every new monster).</summary>
    static readonly Dictionary<string, double> s_fight = new();
    /// <summary>Counters kept between fights (not between sessions unless saved in RareItems.Data.Counters).</summary>
    static readonly Dictionary<string, double> s_keep = new();

    static double F(string key) => s_fight.GetValueOrDefault(key);
    static double Up(Dictionary<string, double> d, string key, double by = 1) => d[key] = d.GetValueOrDefault(key) + by;

    // ================================================================ timers, in fight time

    static double s_nowMs;
    static readonly List<(double At, string Owner, Action Do)> s_timers = new();
    static void After(double ms, string owner, Action act) => s_timers.Add((s_nowMs + ms, owner, act));

    // ================================================================ damage over time of our own

    /// <summary>A damage over time of a power: so much a second, for so long, on the monster or on you.</summary>
    sealed class Dot
    {
        public string Key = "", Owner = "";
        public bool OnPlayer;
        public double PerSecond, LeftMs, NextMs = 1000;
        public bool CanCrit;
        public int EnemyGeneration;
    }

    static readonly List<Dot> s_dots = new();
    static int s_generation;   // +1 for every new monster: a monster's DoTs end with it

    static void AddDot(string owner, string key, double perSecond, double seconds, int maxStacks = 1, bool canCrit = false)
    {
        if (perSecond <= 0) return;
        var mine = s_dots.Where(d => d.Key == key && !d.OnPlayer && d.EnemyGeneration == s_generation).ToList();
        if (mine.Count >= maxStacks) s_dots.Remove(mine.OrderBy(d => d.LeftMs).First());
        s_dots.Add(new Dot { Key = key, Owner = owner, PerSecond = perSecond, LeftMs = seconds * 1000, CanCrit = canCrit, EnemyGeneration = s_generation });
    }

    static bool EnemyHasOurDot() => s_dots.Any(d => !d.OnPlayer && d.EnemyGeneration == s_generation);

    // ================================================================ direct effects

    static CharacterComponent? Player => s_combat?.Player;
    static CharacterComponent? Enemy => s_combat?.Enemy.Enemy != null ? s_combat.Enemy : null;
    static bool EnemyIsElite => Elites.Tier != EliteTier.Normal;

    /// <summary>Damage to the monster that is not an attack (it is not reduced again, ward and all are already counted).</summary>
    static int Strike(int amount, HealthChangeCause cause = HealthChangeCause.CombatEffect)
    {
        if (amount <= 0 || s_combat is not { } c || Enemy is not { } enemy || enemy.Health.Current <= 0) return 0;
        return enemy.Health.Damage(new ModifiedDamage(amount, 0, 0, 0, amount), cause, c.Player, enemy, null);
    }

    /// <summary>Damage to the monster as if from one of your attacks: its damage taken, resistances, ward and armour count.</summary>
    static int StrikeAsAttack(int amount, DamageType? type = null)
    {
        if (amount <= 0 || s_combat is not { } c || Enemy is not { } enemy || enemy.Health.Current <= 0) return 0;
        type ??= c.DamageTypes.GetObjectById("MelvorBase:Normal");
        ModifiedDamage d = c.Player.ModifyAttackDamage(amount, type);
        return enemy.Health.Damage(d, HealthChangeCause.Attack, c.Player, enemy, null);
    }

    static void HealPlayer(int amount)
    {
        if (amount > 0 && s_combat is { } c && c.Player.Health.Current > 0)
            c.Player.Health.Heal(amount, HealthChangeCause.Lifesteal, c.Player, c.Player);
    }

    /// <summary>A rolled hit of yours (between your min and max hit), with your critical hits when it can crit.</summary>
    static int RollHit(double scale, bool canCrit)
    {
        if (s_combat is not { } c) return 0;
        var stats = c.Player.Stats;
        int max = (int)stats.MaxHit.Value, min = Math.Min(max, (int)stats.MinHit.Value);
        double dmg = Random.Shared.Next(min, max + 1) * scale;
        if (canCrit && Random.Shared.NextDouble() < stats.CriticalHitChance.Value) dmg *= stats.CriticalHitMultiplier.Value;
        return (int)Math.Round(dmg);
    }

    static bool RollToHit() => s_combat is { } c && c.Player.HasTarget && Random.Shared.NextDouble() < c.Player.TargetStats.HitChance.Value;

    static void StunEnemy(string effectId, int turns)
    {
        if (s_combat is not { } c || Enemy is not { } enemy || s_game == null) return;
        if (s_game.Data.TryGetObject(effectId, out StunEffect? effect)) enemy.Stun(effect, turns);
    }

    static bool EnemyStunned() =>
        s_combat is { } c && Enemy is { } enemy && c.CombatEffects.GetActiveEffectsForCharacter(enemy).Any(a => a is ActiveStunEffect);

    // ================================================================ the events

    static void OnPlayerAttack(object? sender, AttackEventArgs e)
    {
        if (s_worn.Count == 0 || Offline() || s_combat == null) return;
        try { PlayerAttacked(e); } catch (Exception ex) { Log("One Style: unique power (your hit): " + ex.Message); }
    }

    static void OnEnemyAttack(object? sender, AttackEventArgs e)
    {
        if (s_worn.Count == 0 || Offline() || s_combat == null) return;
        try { EnemyAttacked(e); } catch (Exception ex) { Log("One Style: unique power (monster's hit): " + ex.Message); }
    }

    static void OnEnemyDamaged(object? sender, HealthDamagedEventArgs e)
    {
        if (s_worn.Count == 0 || Offline() || s_combat == null) return;
        try { EnemyDamaged(e); } catch (Exception ex) { Log("One Style: unique power (monster hurt): " + ex.Message); }
    }

    static void OnPlayerDamaged(object? sender, HealthDamagedEventArgs e)
    {
        if (s_worn.Count == 0 || Offline() || s_combat == null) return;
        try { PlayerDamaged(e); } catch (Exception ex) { Log("One Style: unique power (you hurt): " + ex.Message); }
    }

    static void OnPlayerHealth(object? sender, ValueChangedEventArgs<int> e)
    {
        if (s_worn.Count == 0 || Offline() || s_combat == null) return;
        try { PlayerHealthChanged(); } catch (Exception ex) { Log("One Style: unique power (your hit points): " + ex.Message); }
    }

    static void OnSpawned(object? sender, EventArgs e)
    {
        s_generation++;
        s_fight.Clear();
        foreach (Buff b in s_buffs.Values.Where(b => b.Key.EndsWith(":fight"))) b.Off();
        // the game works out offline time: nothing timed stays on through it
        if (Offline()) foreach (Buff b in s_buffs.Values.Where(b => b.LeftMs >= 0)) b.Off();
    }

    static void Guard(string what, Action act)
    {
        if (s_worn.Count == 0 || Offline() || s_combat == null) return;
        try { act(); } catch (Exception ex) { Log($"One Style: unique power ({what}): " + ex.Message); }
    }

    static void OnFightEnded(object? sender, EventArgs e) => Guard("fight ended", FightEnds2);
    static void OnPlayerTurnStart(object? sender, EventArgs e) => Guard("your turn", PlayerTurnStarts);
    static void OnPlayerTurnEnd(object? sender, EventArgs e) => Guard("your turn ended", PlayerTurnEnds);
    static void OnEnemyTurnStart(object? sender, EventArgs e) => Guard("monster's turn", EnemyTurnStarts);
    static void OnEnemyTurnEnd(object? sender, EventArgs e) => Guard("monster's turn ended", EnemyTurnEnds);
    static void OnLooted(object? sender, GamesByMalcs.Melvor2.Components.CombatLoot.EnemyLootedEventArgs e) => Guard("loot", () => Looted(e));
    static void OnImbuement(object? sender, EventArgs e) => Guard("imbuement", () => Imbuement_Update());

    /// <summary>The game's own "a fight starts": you have a target and the elite is made (Elites works on Spawned).</summary>
    static void OnFightStarted(object? sender, EventArgs e)
    {
        if (s_worn.Count == 0 || Offline() || s_combat == null) return;
        try { FightStarts(); } catch (Exception ex) { Log("One Style: unique power (new monster): " + ex.Message); }
    }

    static void OnKilled(object? sender, EnemyKilledEventArgs e)
    {
        if (s_worn.Count == 0 || Offline() || s_combat == null) return;
        try { Killed(e); } catch (Exception ex) { Log("One Style: unique power (kill): " + ex.Message); }
    }

    static void OnEaten(object? sender, FoodEatenEventArgs e)
    {
        if (s_worn.Count == 0 || Offline() || s_combat == null) return;
        try { Ate(e); } catch (Exception ex) { Log("One Style: unique power (eating): " + ex.Message); }
    }

    static void OnStopped(object? sender, EventArgs e)
    {
        s_fight.Clear();
        s_timers.Clear();
        s_dots.Clear();
        foreach (Buff b in s_buffs.Values.Where(b => b.LeftMs >= 0 || b.Key.EndsWith(":fight"))) b.Off();
        if (s_combat == null) return;
        try { CombatStopped(); } catch (Exception ex) { Log("One Style: unique power (fight stopped): " + ex.Message); }
    }

    static void OnTicked(object? sender, GamesByMalcs.Melvor2.Components.Actions.ActionTickedEventArgs e)
    {
        if (e.IsOffline || Offline() || s_combat is not { } c) return;
        if (c.Action.State != CombatActionState.Fighting || c.Action.IsPaused) return;
        double dt = Constants.TickInterval;
        s_nowMs += dt;
        try
        {
            // bonuses for a time
            foreach (Buff b in s_buffs.Values.Where(b => b.On && b.LeftMs >= 0).ToList())
                if ((b.LeftMs -= dt) <= 0) b.Off();
            // timers
            if (s_timers.Count > 0)
                foreach (var t in s_timers.Where(t => t.At <= s_nowMs).ToList())
                {
                    s_timers.Remove(t);
                    if (t.Owner == "" || Wears(t.Owner)) t.Do();
                }
            // our damage over time, once a second
            foreach (Dot d in s_dots.ToList())
            {
                if (!d.OnPlayer && d.EnemyGeneration != s_generation) { s_dots.Remove(d); continue; }
                d.LeftMs -= dt;
                d.NextMs -= dt;
                if (d.NextMs <= 0)
                {
                    d.NextMs += 1000;
                    double dmg = d.PerSecond;
                    if (d.CanCrit && Random.Shared.NextDouble() < c.Player.Stats.CriticalHitChance.Value) dmg *= c.Player.Stats.CriticalHitMultiplier.Value;
                    if (d.OnPlayer)
                    {
                        // never kills you
                        int amount = Math.Min((int)Math.Round(dmg), c.Player.Health.Current - 1);
                        if (amount > 0) c.Player.Health.Damage(new ModifiedDamage(amount, 0, 0, 0, amount), HealthChangeCause.CombatEffect, c.Enemy, c.Player, null);
                    }
                    else Strike((int)Math.Round(dmg));
                }
                if (d.LeftMs <= 0) s_dots.Remove(d);
            }
            Tick(dt);
        }
        catch (Exception ex) { Log("One Style: unique powers (tick): " + ex.Message); }
    }
}
