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
/// OFFLINE TIME: THE RULE FOR EVERY POWER (2026-11-25). While the game works out the time it was closed, nothing scripted
/// here runs: no second arrow, no bleed, no ward refill, no counting. So for that time
///   - a power may still GIVE what is a plain stat of the game that needs no script: nothing to pay, count, follow or
///     end while you play (a quiver that is never used up, lifesteal, accuracy, a set bonus);
///   - a power must not TAKE anything whose payoff is scripted: Sunken Volley's -40% damage without its second arrow,
///     Root-Shear's -20% without its bleed, Hollow Harvest's -35% hit points without its ward refill made a set-up that
///     is safe while you play die offline. A penalty stays only when what it pays for stays too (both plain stats);
///   - everything else (stacks, charges, timed and per-fight bonuses, bonuses that follow your hit points, prayers,
///     the monster) is taken off for that time and put back as it was when play resumes.
/// The list of what stays is KeptOffline (one line and its reason for each); a bonus not named there is off, so a new
/// power is offline-safe until someone decides otherwise. The switch is Away(): see there for when and on which thread.
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

    // ================================================================ offline time

    /// <summary>The bonuses are switched for offline time now (what KeptOffline does not name is off).</summary>
    static volatile bool s_away;
    static bool s_switching;

    /// <summary>The game is working out offline time and the powers are switched for it (for the window and the tests).</summary>
    public static bool IsAway => s_away;

    /// <summary>
    /// True while the game works out offline time; and the one place where the powers are switched for it and back.
    /// Every handler of the game's events asks here first, so the switch is made by the first event that finds the
    /// state changed, before that event does anything else:
    ///   - going offline: the game's first offline tick. Its ticks run on a thread of its own with the scene tree
    ///     paused (Main.RunOfflineProgress: pause, then Task.Run(RunOfflineTicks)); nothing tells a mod on the main
    ///     thread beforehand, and from the main thread the game must not be touched while that thread runs. So the
    ///     switch is made on the game's offline thread, from inside the game's own event (as OnSpawned always did for
    ///     the timed bonuses). The first event of every game tick is the character statistics' tick (OnGameTick:
    ///     that entity is registered, and so ticked, before combat), so the penalties are off before the first
    ///     offline tick of the fight. The combat handlers ask too, should that order ever change.
    ///   - coming back: the first online game tick (main thread), again before the fight's part of it; and
    ///     Bind, which One Style calls a few times a second while the tree is not paused, and Refresh (gear changed).
    /// Asking twice changes nothing: the switch is made only when the answer differs from s_away.
    /// </summary>
    static bool Away()
    {
        bool away = Offline();
        if (away != s_away && !s_switching) Switch(away);
        return away;
    }

    static void Switch(bool away)
    {
        s_switching = true;
        try
        {
            s_away = away;
            if (away)
            {
                // what a script was in the middle of ends here: nothing will run to finish it
                foreach (Buff b in s_buffs.Values.ToList())
                {
                    if (b.LeftMs >= 0 || b.Key.EndsWith(":fight")) b.Off();   // timed, or of this fight: over
                    else if (!KeptOffline(b.Key)) b.Hold();                    // lasting: back when play resumes
                }
                s_timers.Clear();
                s_dots.RemoveAll(d => !d.OnPlayer);   // (what you still owe yourself, Last Breath, waits)
                s_spiders.Clear();
                s_veilDodge = false;
                s_seal4 = 0;
                s_enemyLanded = s_enemyMissed = s_wasFrozen = false;
                FightEnds2();   // Council's Voice: your spell's own element back
            }
            else
            {
                foreach (Buff b in s_buffs.Values.ToList()) b.Release();
                // what follows the state of the moment is worked out again rather than trusted
                s_fight.Remove("Sanguine:bonus");
                if (s_buffs.TryGetValue("Sanguine:blood", out Buff? blood)) blood.Off();
                if (Wears(DivideFangHowler)) s_keep["Howler:rage"] = -1;
                AlwaysOn();
                if (Wears(ThicketTrophies)) Trophies_Update();
                Chalice_Update();
            }
        }
        catch (Exception e) { Log("One Style: unique powers (offline switch): " + e.Message); }
        finally { s_switching = false; }
    }

    /// <summary>
    /// What stays on while the game works out offline time: plain stats that need no script. Every other bonus of a
    /// power is off for that time (a lasting one is held and put back when play resumes).
    /// </summary>
    static bool KeptOffline(string key) => key switch
    {
        // Bog Gullet: +8% lifesteal and -40% food healing are both the game's own stats; the price and what it pays for stay together
        "MireThroat" => true,
        // Honey Frenzy: +15% food preservation is a plain stat (the frenzy after a meal is scripted: off)
        "CombKnuckles:thrift" => true,
        // Still Heartbeat: +60% attack interval is paid for by accuracy and the 80% least hit, plain stats that stay with it
        // (the first shot's sure critical hit is scripted: off)
        "TwinEye" => true,
        // Soulstring Quiver, Ossuary Magazine: a quiver that is never used up costs nothing (the free shot and Bone Rain: off)
        "UniqueAmmo" => true,
        // Sunken Reckoning: the steps are earned for good and saved with the character (+25% against elites, the loot roll: off)
        "Wreckshot" => true,
        // Scrap Hunter: +1% damage a stack, saved; the task's end takes the stacks offline too (the game's own event).
        // (30% faster task monsters follows the monster: off)
        "Scrap" => true,
        // Hollow Harvest: +6 ward is a plain stat. Its price, -35% hit points, is "Hollow:cost": OFF, the refills are scripted
        "Hollow" => true,
        // Blood Rite: spells keep their runes. Its price (3% of your hit points a cast) is scripted and not taken offline;
        // kept all the same, because taking it away would use up the runes of a mage who carries one cast's worth and
        // leave them unable to attack: a death offline, which is what this rule is there to prevent. (The damage for
        // missing hit points, "Sanguine:blood", is scripted: off)
        "Sanguine" => true,
        // Sealed Prayers: half the prayer cost is a plain stat (30% stronger prayers follow which prayers are on: off)
        "Dune" => true,
        // Caravan Road: your stance cannot change offline; accuracy and the game's own Bleed chance (Crush's stun: off)
        "Caravan:stab" or "Caravan:slash" => true,
        // Other Hand's Echo: a ring's bonuses, by gear alone
        "Usurper" => true,
        // Pack Howl: the game's own Rage on hit (the +2% a stack and the howl are scripted: off)
        "Howler:rage" => true,
        // Overflow: +5% lifesteal (the Blood and its +15% are scripted: off)
        "Chalice" => true,
        // Hush: the game's own Blind and Wither on hit (the +15% against them follows the monster: off)
        "Hush:effects" => true,
        // Three Jaws' Edict: with a One Style class the advantage is the same against every monster and stays; without
        // one it is worked out for each monster by script: off
        "Triangle:plus" or "Triangle:cancel" => Patches.Current != Class.None,
        // Ringmaster's Count and rare items counting for sets ("Set:"): by gear alone.
        // Amber Vault ("Amber:"): the potion in the toolbelt cannot change offline, never used up and 1.5 times its bonuses
        _ => key.StartsWith("Set:") || key.StartsWith("Amber:"),

        // OFF for that time, held and put back when play resumes (a penalty whose payoff is scripted):
        //   "RootShear"    Deep Wound: -20% damage; the bleed is scripted
        //   "Thornstep"    Thorn Dance: -10% damage; the 2.5 times reflect is scripted
        //   "Sunken"       Twin Volley: -40% damage; the second arrow is scripted
        //   "Hollow:cost"  Hollow Harvest: -35% hit points; the ward refills are scripted
        //   (Last Breath's half hit points is itself scripted: not held offline, like the damage it delays)
        // OFF for that time, held (a bonus that a script gives, sizes, follows or ends):
        //   "Gore" (eating clears it), "Oath:high"/"Oath:low" (follow your hit points), "Scrap:haste" (the monster),
        //   "Sanguine:blood" (hit points), "Prayer:*" (the prayers that are on), "Mirage" (the imbuement, its runes),
        //   "Howler:fury" (Rage stacks), "Chalice:full" (the Blood), "Hush:dmg" (the monster), "Trophies" (90 s of
        //   online fighting), "Lantern:light" (the next meal: left on it made every meal free), "Cavern" (the 5th
        //   attack: left on it halved every attack), "Aegis:answer" (the next hit: left on, +40% on all),
        //   "Fourfold:seal1"/"Fourfold:free" (that spell)
        // OFF for good at that moment: everything timed (Hoarder, Edge, Gate:open, Resin, VeilAntler...) and
        //   everything of the fight (":fight"), with the timers, spiderlings and damage over time that were waiting.
        // No bonus of their own to switch (scripted through and through, so nothing of theirs runs offline):
        //   Copper Oath, Thornshot, Stitched Furrows, Ringbark Reprisal, Hoarder's Prism, Snarestep, Thicket Trophies,
        //   Caravan Seals, Veil's Rebuttal, Oathlocked Plate, Skytoll, Breaker Hooves, Sovereign Diadem, Lantern Veil,
        //   Gatebreaker, Cavern Bastion, Pinnacle Diadem, Goremantle, Thunder Traces, Starbound Mantle, Fourfold Writ,
        //   Threevoice Cleaver, Tollkeeper Greaves, Conclave Spire, Plungeheart Tusk, Veil-Antler Reprieve, Dreamtide
        //   Orb, Brood-Sign Grips, Hemal Palimpsest, Mirage Astrolabe, Oathbound Gauntlets, Abyssal Beacon (no
        //   elites offline), Twin-Lurker Fangs, Bell-Yoke Wand, Resin-Hoard Ring, Sentinel's Aegis, Talon of the Divide
    };

    /// <summary>The first event of every game tick, online and offline (on the game's thread): only the switch.</summary>
    static void OnGameTick(object? sender, GamesByMalcs.Melvor2.Entities.CharacterStatistics.TickedEventArgs e) => Away();

    // ================================================================ for tests and the window

    /// <summary>
    /// Everything the powers count now: f: this fight, k: kept between fights, d: saved, b: bonuses on, h: bonuses held
    /// off for offline time, dot: our DoTs, "away": 1 while switched for offline time.
    /// </summary>
    public static Dictionary<string, double> Snapshot()
    {
        var all = new Dictionary<string, double>();
        foreach (var (k, v) in s_fight) all["f:" + k] = v;
        foreach (var (k, v) in s_keep) all["k:" + k] = v;
        foreach (var (k, v) in RareItems.Data.Counters) all["d:" + k] = v;
        foreach (var (k, b) in s_buffs) if (b.On) all["b:" + k] = 1;
        foreach (var (k, b) in s_buffs) if (b.Held) all["h:" + k] = 1;
        if (s_away) all["away"] = 1;
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
                "OsUFourfoldWrit" => $"next seal {K("Fourfold:seal") % 4 + 1}, in {4 - K("casts") % 4:0} spells",
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
        // (One Style calls this a few times a second while the tree is not paused: play that resumed is noticed here
        // too, should no game tick have told us yet)
        if (ReferenceEquals(s_game, game)) { Away(); return; }
        // a character starts clean: nothing of the one before it
        Unbind();
        if (!game.Entities.TryGetObjectById("MelvorBase:Combat", out var e) || e is not CombatEntity combat) return;
        s_game = game;
        s_combat = combat;
        s_mainThread = RareItems.MainThread;
        s_stats = game.CharacterStatistics.Statistics;
        s_stats.Ticked += OnGameTick;
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
        if (combat.SlayerTasks is { } tasks)
        {
            tasks.TaskCompleted += OnTaskCompleted;
            tasks.TaskRerolled += OnTaskRerolled;
        }
        Refresh(game);
    }

    static GamesByMalcs.Melvor2.Entities.CharacterStatistics.CharacterStatisticsComponent? s_stats;

    /// <summary>
    /// The character is closed (or another one opened). Everything here that belongs to one character goes: the next
    /// one starts with no Hunger, Edge, Blood, trophy, charge or debt of this one. What is meant to last (Sunken
    /// Reckoning's kills, Scrap Hunter's stacks and token, Back From the Veil's wait) is not kept here at all: it is in
    /// RareItems.Data.Counters, which is that character's own part of its save, and is read from there (Kept).
    /// </summary>
    public static void Unbind()
    {
        if (s_stats != null) s_stats.Ticked -= OnGameTick;
        s_stats = null;
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
            if (c.SlayerTasks is { } tasks)
            {
                tasks.TaskCompleted -= OnTaskCompleted;
                tasks.TaskRerolled -= OnTaskRerolled;
            }
        }
        foreach (Buff b in s_buffs.Values.ToList())
            try { b.Off(); } catch (Exception e) { Log("One Style: unique powers (closing): " + e.Message); }
        // the bonuses, and everything counted, timed or waiting (UniquePowers.cs)
        s_buffs.Clear();
        s_timers.Clear();
        s_dots.Clear();
        s_fight.Clear();
        s_keep.Clear();
        s_fired.Clear();
        s_worn.Clear();
        s_nowMs = 0;
        s_generation = 0;
        s_away = false;
        s_switching = false;
        // UniquePowers.Powers.cs
        s_trophies.Clear();
        s_spiders.Clear();
        s_lastEnemyHp = 0;
        s_enemyLanded = s_enemyMissed = false;
        s_inOwnStrike = false;
        s_tollDebt = 0;
        s_slowMs = 0;
        s_wasFrozen = false;
        // UniquePowers.Powers2.cs
        s_veilDodge = false;
        s_seal4 = 0;
        s_conclaveBack = null;
        s_hollowMs = 0;
        s_lastHitMs = 0;
        UsurperCopy = null;
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
        // offline or not is settled before the bonuses are set: what is off for offline time is not put on (Buff.Set).
        // (The game's equip events come on its offline thread too: a quiver that ran out there.)
        Away();
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
        /// <summary>Off for offline time only: what it gave is remembered and put back when play resumes.</summary>
        public bool Held;
        public double LeftMs = -1;   // -1: until taken off
        bool _busy;
        ModifierValues? _values;
        double _scale = 1;
        public void Set(ModifierValues values, double ms = -1, double scale = 1)
        {
            if (Patches.Modifiers is not { } mods) return;
            // adding a bonus can change your hit points (max hit points up), which calls the powers again from inside
            // the game: a second Set of the same bonus then added it twice (Headhunter's Boughs with a Colossal trophy
            // gave double; found by the effect tests, 2026-11-24). The call from inside is left out.
            if (_busy) return;
            _busy = true;
            try
            {
                if (On) { On = false; mods.Remove(Source); }
                if (s_away && !KeptOffline(Key))
                {
                    // the game works out offline time: not put on. A lasting bonus waits for play to resume (gear that
                    // changes offline sets and clears these like any other); a timed one or one of a fight is not kept.
                    Held = ms < 0 && !Key.EndsWith(":fight");
                    _values = Held ? values : null;
                    _scale = scale;
                    LeftMs = -1;
                    return;
                }
                mods.Add(Source, values, scale);
                On = true;   // only once it is really added: Off() must never take off what is not there
                Held = false;
                _values = values;
                _scale = scale;
                LeftMs = ms;
            }
            finally { _busy = false; }
        }
        public void Off()
        {
            if (On && Patches.Modifiers is { } mods) mods.Remove(Source);
            On = false;
            Held = false;
            _values = null;
            LeftMs = -1;
        }
        /// <summary>Offline time starts: off, to come back with Release.</summary>
        public void Hold()
        {
            if (!On) return;
            if (Patches.Modifiers is { } mods) mods.Remove(Source);
            On = false;
            Held = _values != null;
        }
        /// <summary>Play resumes: what was held is on again, as it was.</summary>
        public void Release()
        {
            if (!Held) return;
            Held = false;
            if (_values is { } values) Set(values, -1, _scale);
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
    static int StrikeAsAttack(int amount, DamageType? type = null, int cap = int.MaxValue)
    {
        if (amount <= 0 || s_combat is not { } c || Enemy is not { } enemy || enemy.Health.Current <= 0) return 0;
        type ??= c.DamageTypes.GetObjectById("MelvorBase:Normal");
        ModifiedDamage d = c.Player.ModifyAttackDamage(amount, type);
        // a power's cap holds after your damage bonuses too (Rising Toll's 40%)
        if (d.Modified > cap) return Strike(cap, HealthChangeCause.Attack);
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

    /// <summary>
    /// Puts the game's own stun effect on the monster (Stun, Freeze or Sleep): its icon, the effect's own damage taken,
    /// its after-effect, and the game's rules for it (ward, immunity and avoidance can stop it). True when the monster
    /// has that effect now. (Until 2026-11-24 this only skipped the monster's turn: nothing was on the monster, so
    /// "frozen" and "asleep" were never true and Ice Shatter and Dream Tide never got their bonus.)
    /// </summary>
    static bool StunEnemy(string effectId, int turns)
    {
        if (s_combat is not { } c || Enemy is not { } enemy || s_game == null) return false;
        if (!s_game.Data.TryGetObject(effectId, out StunEffect? effect)) return false;
        c.CombatEffects.ApplyEffect(new ConfiguredStunEffect(effect, new TurnCountingEffectConfig(turns)), enemy, c.Player);
        return c.CombatEffects.GetActiveEffectsForCharacter(enemy).Any(a => a is ActiveStunEffect && a.Data.Id == effectId);
    }

    static bool EnemyStunned() =>
        s_combat is { } c && Enemy is { } enemy && c.CombatEffects.GetActiveEffectsForCharacter(enemy).Any(a => a is ActiveStunEffect);

    // ================================================================ the events

    static void OnPlayerAttack(object? sender, AttackEventArgs e)
    {
        if (Away() || s_worn.Count == 0 || s_combat == null) return;
        try { PlayerAttacked(e); } catch (Exception ex) { Log("One Style: unique power (your hit): " + ex.Message); }
    }

    static void OnEnemyAttack(object? sender, AttackEventArgs e)
    {
        if (Away() || s_worn.Count == 0 || s_combat == null) return;
        try { EnemyAttacked(e); } catch (Exception ex) { Log("One Style: unique power (monster's hit): " + ex.Message); }
    }

    static void OnEnemyDamaged(object? sender, HealthDamagedEventArgs e)
    {
        if (Away() || s_worn.Count == 0 || s_combat == null) return;
        try { EnemyDamaged(e); } catch (Exception ex) { Log("One Style: unique power (monster hurt): " + ex.Message); }
    }

    static void OnPlayerDamaged(object? sender, HealthDamagedEventArgs e)
    {
        if (Away() || s_worn.Count == 0 || s_combat == null) return;
        try { PlayerDamaged(e); } catch (Exception ex) { Log("One Style: unique power (you hurt): " + ex.Message); }
    }

    static void OnPlayerHealth(object? sender, ValueChangedEventArgs<int> e)
    {
        if (Away() || s_worn.Count == 0 || s_combat == null) return;
        try { PlayerHealthChanged(); } catch (Exception ex) { Log("One Style: unique power (your hit points): " + ex.Message); }
    }

    static void OnSpawned(object? sender, EventArgs e)
    {
        // (the game works out offline time: the switch takes off everything timed and of the fight, see Away)
        Away();
        s_generation++;
        s_fight.Clear();
        foreach (Buff b in s_buffs.Values.Where(b => b.Key.EndsWith(":fight"))) b.Off();
    }

    static void Guard(string what, Action act)
    {
        if (Away() || s_worn.Count == 0 || s_combat == null) return;
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
        if (Away() || s_worn.Count == 0 || s_combat == null) return;
        try { FightStarts(); } catch (Exception ex) { Log("One Style: unique power (new monster): " + ex.Message); }
    }

    // (the two task events are followed offline too: a task that ends there takes Scrap Hunter's stacks with it)
    static void OnTaskCompleted(object? sender, GamesByMalcs.Melvor2.Components.SlayerTasks.TaskCompletedEventArgs e)
    {
        Away();
        try { Scrap_TaskOver(completed: true); } catch (Exception ex) { Log("One Style: unique power (task): " + ex.Message); }
    }

    static void OnTaskRerolled(object? sender, GamesByMalcs.Melvor2.Components.SlayerTasks.TaskRerolledEventArgs e)
    {
        Away();
        try { Scrap_TaskOver(completed: false); } catch (Exception ex) { Log("One Style: unique power (task): " + ex.Message); }
    }

    static void OnKilled(object? sender, EnemyKilledEventArgs e)
    {
        if (Away() || s_worn.Count == 0 || s_combat == null) return;
        try { Killed(e); } catch (Exception ex) { Log("One Style: unique power (kill): " + ex.Message); }
    }

    static void OnEaten(object? sender, FoodEatenEventArgs e)
    {
        if (Away() || s_worn.Count == 0 || s_combat == null) return;
        try { Ate(e); } catch (Exception ex) { Log("One Style: unique power (eating): " + ex.Message); }
    }

    static void OnStopped(object? sender, EventArgs e)
    {
        Away();
        s_fight.Clear();
        s_timers.Clear();
        s_dots.Clear();
        foreach (Buff b in s_buffs.Values.Where(b => b.LeftMs >= 0 || b.Key.EndsWith(":fight"))) b.Off();
        if (s_combat == null) return;
        try { CombatStopped(); } catch (Exception ex) { Log("One Style: unique power (fight stopped): " + ex.Message); }
    }

    static void OnTicked(object? sender, GamesByMalcs.Melvor2.Components.Actions.ActionTickedEventArgs e)
    {
        if (Away() || e.IsOffline || s_combat is not { } c) return;
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
                // half a millisecond of slack: the tick length is not a round number, and without it the sums drift so
                // that a 6 s effect ended a hair before its 6th strike (found by the effect tests, 2026-11-24)
                if (d.NextMs <= 0.5)
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
                if (d.LeftMs <= 0.5) s_dots.Remove(d);
            }
            Tick(dt);
        }
        catch (Exception ex) { Log("One Style: unique powers (tick): " + ex.Message); }
    }
}
