using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Characters;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.CombatPassives;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Core;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Modifiers;

namespace OneStyle;

/// <summary>
/// Champion and Unique monsters (hack-and-slash design, Ortak-Calisma\hack-and-slash\denge\karar.md and
/// onestyle-birlesim\03-elitler-karar.md). Online only: a monster that spawns while the game works out the time it was
/// closed is always normal. Everything is a bonus in the game's own modifier system (never in the save) and is taken
/// off again with the next monster, when the fight stops or the monster is gone.
/// </summary>
public static partial class Elites
{
    public static Action<string> Log = _ => { };

    static Dictionary<string, EliteInfo> s_info = new();
    static Game? s_game;
    static ModifierSystem? s_mods;
    static CombatEntity? s_combat;
    static string s_key = "";
    static EliteProgress s_progress = new();
    static int s_mainThread;
    static readonly Random s_rng = new();
    static bool s_dirty;

    // ---- the elite alive now
    public static EliteTier Tier { get; private set; }
    public static EliteInfo? Info { get; private set; }
    public static Enemy? Enemy { get; private set; }
    public static List<string> Affixes { get; } = new();
    public static bool SignatureOn { get; private set; }
    public static int WardHits { get; private set; }
    /// <summary>The bonuses as numbers (tests and the window): hit points, damage, interval, accuracy, protection.</summary>
    public static (double Hp, double Damage, double Interval, double Accuracy, double Protection) Numbers { get; private set; }
    static bool s_vampiric, s_enraging, s_enraged, s_killed;
    static double s_baseHp, s_baseInterval, s_dpsCap, s_hitCap, s_fightMs;
    static int s_exhaustSteps;
    static ModifierValues? s_exhaustValues;
    static readonly Patches.Source s_stats = new("One Style: elite"), s_enrage = new("One Style: elite enraged"),
        s_exhaust = new("One Style: elite exhausted"), s_xp = new("One Style: elite XP"), s_noDeath = new("One Style: elite, no death penalty"),
        s_passive = new("One Style: elite passive"), s_trophyHp = new("One Style: 25 trophies"),
        s_eliteDamage = new("One Style: Slayer's"), s_execute = new("One Style: Executing"), s_momentum = new("One Style: Momentum");
    static bool s_executeOn, s_momentumOn;
    static double s_momentumMs;
    static readonly List<Patches.Source> s_onElite = new();

    static Godot.SceneTree? s_tree;

    /// <summary>The combat page was opened during a fight: its monster and name are drawn by the game, so the look goes on again.</summary>
    static void OnNodeAdded(Godot.Node node)
    {
        if (Tier != EliteTier.Normal && node is EnemySprite or EnemyNamePanel or GamesByMalcs.Melvor2.Entities.Combat.CombatActiveView) ShowLook();
        if (node is GamesByMalcs.Melvor2.Components.CombatAreas.EnemyOptionsPanel panel)
            Godot.Callable.From(() => AddEliteButton(panel)).CallDeferred();
    }

    /// <summary>For tests: starts the character's elite progress afresh (kept settings).</summary>
    public static void ResetProgressForTests()
    {
        s_progress = new EliteProgress { Enabled = s_progress.Enabled, NoDeathPenalty = s_progress.NoDeathPenalty };
        s_dirty = true;
    }

    /// <summary>For tests: the next monster that spawns online is this tier (ignored offline).</summary>
    public static EliteTier? Forced { get; set; }
    public static EliteProgress Progress => s_progress;
    public static int Known => s_info.Count;
    public static bool Enabled
    {
        get => s_progress.Enabled;
        set { s_progress.Enabled = value; s_dirty = true; }
    }
    public static bool NoDeathPenalty
    {
        get => s_progress.NoDeathPenalty;
        set { s_progress.NoDeathPenalty = value; s_dirty = true; }
    }

    public static void LoadInfo(string modDir) => s_info = EliteStore.LoadInfo(modDir);
    public static EliteInfo? InfoOf(string enemyId) => s_info.TryGetValue(enemyId, out EliteInfo? i) ? i : null;

    // ================================================================ binding

    public static void Bind(Game game, string key, ModifierSystem? mods)
    {
        Unbind();
        s_game = game;
        s_mods = mods;
        s_key = key;
        s_mainThread = Environment.CurrentManagedThreadId;
        s_progress = EliteStore.Load(key);
        s_combat = (CombatEntity)game.Entities.GetObjectById("MelvorBase:Combat");
        s_combat.Enemy.Killed += OnKilled;
        s_combat.Enemy.EnemyChanged += OnEnemyChanged;
        s_combat.Enemy.Health.CurrentChanged += OnEnemyHealth;
        s_combat.Player.Health.Damaged += OnPlayerDamaged;
        s_combat.Action.Ticked += OnTicked;
        s_combat.Action.Stopped += OnStopped;
        s_combat.Action.StateChanged += OnStateChanged;
        s_combat.Enemy.Ward.CurrentChanged += OnWardChanged;
        if (Godot.Engine.GetMainLoop() is Godot.SceneTree tree) { s_tree = tree; tree.NodeAdded += OnNodeAdded; }
        ApplyTrophyBonus();
    }

    public static void Unbind()
    {
        if (s_combat != null)
        {
            Clear();
            s_combat.Enemy.Killed -= OnKilled;
            s_combat.Enemy.EnemyChanged -= OnEnemyChanged;
            s_combat.Enemy.Health.CurrentChanged -= OnEnemyHealth;
            s_combat.Player.Health.Damaged -= OnPlayerDamaged;
            s_combat.Action.Ticked -= OnTicked;
            s_combat.Action.Stopped -= OnStopped;
            s_combat.Action.StateChanged -= OnStateChanged;
            s_combat.Enemy.Ward.CurrentChanged -= OnWardChanged;
        }
        if (s_tree != null) { s_tree.NodeAdded -= OnNodeAdded; s_tree = null; }
        if (s_mods != null) try { s_mods.Remove(s_trophyHp); } catch { }
        SaveNow();
        s_combat = null;
        s_game = null;
        s_mods = null;
        s_key = "";
    }

    /// <summary>Called by the mod a few times a second on the main thread: writes the progress when it changed.</summary>
    public static void Poll()
    {
        if (s_dirty) SaveNow();
    }

    /// <summary>Spends Ember (written at once). False, and nothing spent, when there is not enough.</summary>
    public static bool SpendEmber(long amount)
    {
        if (amount < 0 || s_progress.Ember < amount) return false;
        s_progress.Ember -= amount;
        SaveNow();
        return true;
    }

    static void SaveNow()
    {
        if (s_key == "") return;
        s_dirty = false;
        EliteStore.Save(s_key, s_progress);
    }

    /// <summary>True while the game works out the time it was closed (or any tick off the main thread).</summary>
    static bool Offline() =>
        Environment.CurrentManagedThreadId != s_mainThread || MelvorModLoader.GameAccess.IsComputingOffline || s_game?.Flags.IsOffline == true;

    // ================================================================ a monster appears

    /// <summary>Called first when a monster spawns (before the weak spot): a roll, and the elite's bonuses.</summary>
    public static void OnSpawned()
    {
        Clear();
        if (s_combat?.Enemy.Enemy is not { } enemy || s_mods == null) return;
        if (Offline()) return;
        EliteInfo? info = InfoOf(enemy.Id);
        if (info == null) return;
        EliteTier tier = Forced ?? (Enabled ? Roll(enemy, info) : EliteTier.Normal);
        Forced = null;
        if (tier == EliteTier.Normal) return;
        s_progress.Gap[enemy.Id] = 3;
        s_dirty = true;
        Make(enemy, info, tier);
    }

    /// <summary>The chances where the monster is (karar.md 1), the pity and the trophies.</summary>
    static EliteTier Roll(Enemy enemy, EliteInfo info)
    {
        if (s_progress.Gap.TryGetValue(enemy.Id, out int gap) && gap > 0)
        {
            s_progress.Gap[enemy.Id] = gap - 1;
            s_dirty = true;
            return EliteTier.Normal;
        }
        (double champion, double unique) = Chances(enemy);
        double roll = s_rng.NextDouble();
        if (roll < unique) return EliteTier.Unique;
        return s_rng.NextDouble() < champion ? EliteTier.Champion : EliteTier.Normal;
    }

    /// <param name="inArea">as in a combat area (for the monster lists), whatever is being fought now</param>
    public static (double Champion, double Unique) Chances(Enemy enemy, bool inArea = false)
    {
        var areas = s_combat!.Areas;
        bool dungeon = !inArea && areas.CurrentArea?.Area is Dungeon;
        bool boss = dungeon && areas.CurrentProgress >= areas.MaxProgress;
        bool slayer = s_combat.SlayerTasks?.HasTaskForEnemy(enemy) == true;
        (double champion, double unique) = boss ? (0.10, 0.04) : dungeon ? (0.08, 0.006) : slayer ? (0.10, 0.015) : (0.06, 0.008);
        int[] kills = s_progress.KillsOf(enemy.Id);
        bool first = kills[2] == 0;
        int since = s_progress.SinceUnique.TryGetValue(enemy.Id, out int k) ? k : 0;
        if (first) unique *= 1.5;
        int start = first ? 60 : 100, certain = first ? 120 : 200;
        if (since >= certain) unique = 1;
        else if (since >= start) unique += 0.0025 * (since - start);
        int trophies = s_progress.Trophies;
        // trophies, and "of Calling" on worn rare items (at most +50%)
        double calling = s_game != null ? Math.Min(50, RareItems.WornCustom(s_game, "Calling")) / 100 : 0;
        double more = (1 + 0.0015 * trophies) * (1 + calling);
        if (UniquePowers.Wears("OsUAbyssalBeacon")) more = Math.Min(2.5, 2 * more);   // Deep Signal
        champion *= more;
        unique = Math.Min(1, unique * more * (s_progress.Marked == enemy.Id && trophies >= 100 ? 1.25 : 1));
        return (champion, unique);
    }

    // ================================================================ making an elite (karar.md 2, 03-elitler-karar.md)

    static readonly string[] s_allAffixes = { "Hasted", "Colossal", "Warded", "Armoured", "Vampiric", "Enraging" };

    static bool Clashes(string a, string b) =>
        (a, b) is ("Hasted", "Enraging") or ("Enraging", "Hasted") or ("Armoured", "Vampiric") or ("Vampiric", "Armoured");

    static void Make(Enemy enemy, EliteInfo info, EliteTier tier)
    {
        CombatEntity combat = s_combat!;
        EnemyComponent e = combat.Enemy;
        bool unique = tier == EliteTier.Unique;
        double hp = Math.Max(1, e.Health.Max), interval = Math.Max(1, e.Stats.AttackInterval.Value);
        s_baseHp = hp;
        s_baseInterval = interval;
        // affixes: a Unique's two fixed ones, a Champion's random one (two from 1200 hit points)
        Affixes.Clear();
        if (unique) Affixes.AddRange(info.Affixes.Where(a => s_allAffixes.Contains(a)));
        else
        {
            bool healsAlready = e.Stats.ModifiedCombatStatTotals.Dict.Any(kv => kv.Key.Id == "MelvorBase:Lifesteal" && kv.Value.Value > 0);
            var pool = s_allAffixes.Where(a => !(a == "Vampiric" && healsAlready)).ToList();
            int count = hp >= 1200 ? 2 : 1;
            while (Affixes.Count < count && pool.Count > 0)
            {
                string pick = pool[s_rng.Next(pool.Count)];
                pool.Remove(pick);
                if (Affixes.Any(x => Clashes(x, pick))) continue;
                Affixes.Add(pick);
            }
        }
        // Deep Signal: one more affix
        if (UniquePowers.Wears("OsUAbyssalBeacon"))
            foreach (string extra in s_allAffixes.OrderBy(_ => s_rng.Next()))
                if (!Affixes.Contains(extra) && !Affixes.Any(x => Clashes(x, extra))) { Affixes.Add(extra); break; }
        EliteNumbers n = Compute(hp, interval, unique, Affixes, unique ? info : null);
        if (UniquePowers.Wears("OsUAbyssalBeacon")) n = n with { HpMult = n.HpMult * 1.2 };
        double hpMult = n.HpMult, damage = n.Damage, speed = n.Speed, accuracy = n.Accuracy, protection = n.Protection, taken = n.Taken;
        WardHits = n.WardHits;
        s_dpsCap = n.DpsCap;
        s_hitCap = n.HitCap;
        bool signature = n.Signature;
        s_vampiric = Affixes.Contains("Vampiric");
        s_enraging = Affixes.Contains("Enraging");

        var values = new List<ComponentModifierValue>
        {
            Patches.Bonus("Character", "PercentMaxHitpoints", hpMult - 1, "Enemy"),
            Patches.Bonus("Character", "PercentDamageDealt", damage, "Enemy"),
            Patches.Bonus("Character", "PercentAttackInterval", speed, "Enemy"),
            Patches.Bonus("Character", "PercentAccuracyRating", accuracy, "Enemy"),
            Patches.Bonus("Character", "PercentProtectionRating", protection, "Enemy"),
        };
        if (taken != 0) values.Add(Patches.Bonus("Character", "PercentDamageTaken", taken, "Enemy"));
        if (WardHits > 0) values.Add(Patches.Bonus("Character", "FlatCombatStatValue", WardHits, "Enemy", ("CombatStat", "MelvorBase:MaximumWard")));
        Add(s_stats, Patches.Bonuses(values.ToArray()));
        Numbers = (hpMult - 1, damage, speed, accuracy, protection);

        // more combat XP while it lives (also for the kill), no death penalty (a CombatAction stat)
        double xp = unique ? 0.50 : 0.25;
        Add(s_xp, Patches.Bonuses(new[] { "Attack", "Strength", "Defence", "Hitpoints", "Ranged", "Magic" }
            .Select(s => Patches.BonusOn("MelvorBase:" + s, "Experience", "Experience", "PercentXPGained", xp)).ToArray()));
        if (NoDeathPenalty) Add(s_noDeath, Patches.Bonuses(Patches.Bonus("CombatAction", "NoDeathPenalty", 1, "Action")));
        // "Slayer's" on worn rare items: more max hit against Champions and Uniques
        double eliteDamage = RareItems.WornCustom(s_game!, "EliteDamage");
        if (eliteDamage > 0) Add(s_eliteDamage, Patches.Bonuses(Patches.Bonus("Character", "PercentMaxHit", eliteDamage / 100, "Player")));

        // a Unique: its own passive (as bonuses, never into the save) and its signature attack
        SignatureOn = false;
        if (unique)
        {
            if (info.Passive != "" && s_game!.Data.TryGetObject(info.Passive, out CombatPassive? passive) && !enemy.Passives.Contains(passive))
                Add(s_passive, passive.Modifiers);
            if (signature) SignatureOn = AddSignature(e, info);
        }

        e.Health.Set(e.Health.Max);
        e.Ward.Set(e.Ward.Max);
        Tier = tier;
        Info = info;
        Enemy = enemy;
        s_killed = false;
        s_enraged = false;
        s_fightMs = 0;
        s_exhaustSteps = 0;
        string name = tier == EliteTier.Unique ? $"{info.UniqueName} (Unique {enemy.DisplayName.Text})" : $"Champion {enemy.DisplayName.Text}";
        Log($"{name}: {string.Join(", ", Affixes)}{(SignatureOn ? ", signature " + info.Signature.Replace("MelvorBase:", "") : "")}.");
        ShowLook();
    }

    public readonly record struct EliteNumbers(double HpMult, double Damage, double Speed, double Accuracy, double Protection,
        double Taken, int WardHits, double DpsCap, double HitCap, bool Signature);

    /// <summary>
    /// An elite's numbers from the normal monster's hit points and attack interval (karar.md 2 and its safety rules):
    /// the same for a live elite and for the preview in the Elites window.
    /// </summary>
    public static EliteNumbers Compute(double hp, double interval, bool unique, IEnumerable<string> affixes, EliteInfo? info)
    {
        hp = Math.Max(1, hp);
        interval = Math.Max(1, interval);
        double t = Math.Clamp(Math.Log10(Math.Max(hp, 20) / 20) / 3, 0, 1);
        // a small monster gets a little more (80 / 30 hit points), but never more than x6 / x3 (denge-2 7: 10 hit points made x9)
        double hpMult = unique ? Math.Max(6.00 - 4.30 * t, 1 + Math.Min(80, 5 * hp) / hp) : Math.Max(3.00 - 1.65 * t, 1 + Math.Min(30, 2 * hp) / hp);
        double damage = unique ? (hp >= 3000 ? 0.14 : 0.22) : (hp >= 3000 ? 0.08 : 0.12);
        double speed = unique ? -0.08 : -0.05;
        double accuracy = unique ? 0.15 : 0.08, protection = unique ? 0.18 : 0.10;
        double taken = 0;
        int ward = 0, band = Band(hp);
        foreach (string a in affixes)
            switch (a)
            {
                case "Hasted": speed -= interval <= 2000 ? 0.12 : 0.18; break;
                case "Colossal": hpMult *= 1.25; damage += 0.10; speed += 0.10; break;
                case "Warded": ward = 2 + (band - 1) / 2; break;   // the game's ward counts hits: 2 to 6 (K1)
                case "Armoured": taken -= 0.18; break;
            }
        // safety, on the totals (the game adds percent bonuses together)
        if (hp >= 5000) hpMult = Math.Min(hpMult, 2.2);
        speed = Math.Max(speed, Math.Min(1200, interval) / interval - 1);   // interval floor
        double dpsCap = hp >= 3000 ? 1.30 : unique ? 1.55 : 1.35;
        damage = Math.Min(damage, dpsCap * (1 + speed) - 1);               // damage per second
        // one hit at most 1.3x the monster's own; a Unique's harder signature lowers the damage bonus (one above 1.3x is left out)
        bool signature = unique && info != null && info.Signature != "" && info.SignaturePeak >= 0 && info.SignaturePeak <= 1.30;
        double hitCap = 0.30;
        if (signature && info!.SignaturePeak > 1) hitCap = Math.Min(hitCap, 1.30 / info.SignaturePeak - 1);
        damage = Math.Min(damage, hitCap);
        return new EliteNumbers(hpMult, damage, speed, accuracy, protection, taken, ward, dpsCap, hitCap, signature);
    }

    public static int Band(double hp) => Math.Clamp(1 + (int)Math.Floor(3 * Math.Log10(Math.Max(hp, 20) / 20)), 1, 9);

    static void Add(Patches.Source source, ModifierValues values)
    {
        s_mods!.Add(source, values);
        s_onElite.Add(source);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_availableAttacks")]
    static extern ref List<AttackChance> AvailableAttacks(CharacterComponent character);

    /// <summary>
    /// Adds the signature to this spawn's own attack list (never the monster's shared data): the other attacks share
    /// what is left. The game's attack list view follows the change event.
    /// </summary>
    static bool AddSignature(EnemyComponent e, EliteInfo info)
    {
        if (!s_game!.Data.TryGetObject(info.Signature, out Attack? attack)) return false;
        List<AttackChance> list = AvailableAttacks(e);
        // A new attack gets its own share (usually 18%); one the monster already has (its trademark) 10 points more.
        double had = list.Where(a => a.Attack == attack).Sum(a => a.Chance);
        double want = had > 0 ? Math.Min(had + 0.10, 0.6) : Math.Clamp(info.SignatureChance, 0, 0.3);
        double rest = list.Where(a => a.Attack != attack).Sum(a => a.Chance);
        var merged = new Dictionary<Attack, double>();
        foreach (AttackChance a in list.Where(a => a.Attack != attack))
            merged[a.Attack] = merged.GetValueOrDefault(a.Attack) + (rest > 0 ? a.Chance / rest * (1 - want) : 0);
        merged[attack] = want;
        list.Clear();
        foreach (var (a, c) in merged) list.Add(new AttackChance(a, c));
        RaiseAttacksChanged(e);
        return true;
    }

    static void RaiseAttacksChanged(CharacterComponent c)
    {
        var field = typeof(CharacterComponent).GetField("AvailableAttacksChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        (field?.GetValue(c) as EventHandler)?.Invoke(c, EventArgs.Empty);
    }

    /// <summary>Takes the elite's bonuses off (the monster is gone, the fight stopped, or the next one spawns).</summary>
    static void Clear()
    {
        if (s_mods != null)
            foreach (Patches.Source source in s_onElite.Concat(new[] { s_enrage, s_exhaust }).Distinct())
                try { s_mods.Remove(source); } catch { }
        if (s_executeOn && s_mods != null) { try { s_mods.Remove(s_execute); } catch { } s_executeOn = false; }
        s_onElite.Clear();
        bool was = Tier != EliteTier.Normal;
        Tier = EliteTier.Normal;
        Info = null;
        Enemy = null;
        Affixes.Clear();
        SignatureOn = false;
        WardHits = 0;
        s_exhaustSteps = 0;
        s_vampiric = s_enraging = s_enraged = false;
        if (was) ShowLook();
    }

    // ================================================================ while it lives

    /// <summary>The Warded badge counts the ward hits left.</summary>
    static void OnWardChanged(object? sender, ValueChangedEventArgs<int> e)
    {
        if (Tier != EliteTier.Normal && Affixes.Contains("Warded")) ShowLook();
    }

    static void OnEnemyChanged(object? sender, ValueChangedEventArgs<Enemy?> e)
    {
        if (e.NewValue == null && Tier != EliteTier.Normal) Clear();
    }

    static void OnStopped(object? sender, ActionEventArgs e)
    {
        if (Tier != EliteTier.Normal) Clear();
    }

    /// <summary>Enraging: below 40% hit points, +20% damage and -10% attack interval (within the safety limits).</summary>
    static void OnEnemyHealth(object? sender, ValueChangedEventArgs<int> e)
    {
        Execute();
        if (!s_enraging || s_enraged || s_killed || s_combat == null || s_mods == null) return;
        var health = s_combat.Enemy.Health;
        if (health.Current <= 0 || health.Percent >= 0.4) return;
        s_enraged = true;
        double speed = Math.Max(Numbers.Interval - 0.10, Math.Min(1200, s_baseInterval) / s_baseInterval - 1);
        double damage = Math.Min(Math.Min(Numbers.Damage + 0.20, s_dpsCap * (1 + speed) - 1), s_hitCap);
        s_mods.Add(s_enrage, Patches.Bonuses(
            Patches.Bonus("Character", "PercentDamageDealt", damage - Numbers.Damage, "Enemy"),
            Patches.Bonus("Character", "PercentAttackInterval", speed - Numbers.Interval, "Enemy")));
        ShowLook();
    }

    /// <summary>"Executing" on worn rare items: more max hit against any monster below 35% of its hit points.</summary>
    static void Execute()
    {
        if (s_executeOn || s_combat == null || s_mods == null || s_game == null || Offline()) return;
        var health = s_combat.Enemy.Health;
        if (s_combat.Enemy.Enemy == null || health.Current <= 0 || health.Percent >= 0.35) return;
        double v = RareItems.WornCustom(s_game, "Execute");
        if (v <= 0) return;
        s_mods.Add(s_execute, Patches.Bonuses(Patches.Bonus("Character", "PercentMaxHit", v / 100, "Player")));
        s_executeOn = true;
    }

    /// <summary>Vampiric: heals 10% of the damage it deals, at most 2.5% of its max hit points a hit, fading to 0 from 25 to 45 s.</summary>
    static void OnPlayerDamaged(object? sender, HealthDamagedEventArgs e)
    {
        if (!s_vampiric || s_killed || s_combat == null || !ReferenceEquals(e.SourceCharacter, s_combat.Enemy) || e.Amount <= 0) return;
        double seconds = s_fightMs / 1000;
        double rate = seconds <= 25 ? 0.10 : seconds >= 45 ? 0 : 0.10 * (45 - seconds) / 20;
        int heal = (int)Math.Round(Math.Min(e.Amount * rate, s_combat.Enemy.Health.Max * 0.025));
        if (heal > 0 && s_combat.Enemy.Health.Current > 0)
        {
            s_combat.Enemy.Health.Heal(heal, HealthChangeCause.Lifesteal, s_combat.Enemy, s_combat.Player);
            // a short green flash on the bar
            if (System.Environment.CurrentManagedThreadId == s_mainThread && Godot.Engine.GetMainLoop() is Godot.SceneTree tree)
            {
                s_healFlashUntil = Godot.Time.GetTicksMsec() / 1000.0 + 0.35;
                ShowLook();
                tree.CreateTimer(0.4).Timeout += ShowLook;
            }
        }
    }

    /// <summary>Fight time (game ticks, fighting and not paused): Vampiric fades, and after 60 s the elite tires.</summary>
    static void OnTicked(object? sender, ActionTickedEventArgs e)
    {
        if (s_momentumOn && s_mods != null && s_combat != null)
        {
            // its 4 seconds run only while fighting: waiting for the next monster does not use them up (denge-2 6)
            if (!s_combat.Action.IsStarted) s_momentumMs = 0;
            else if (s_combat.Action.State == CombatActionState.Fighting && !s_combat.Action.IsPaused) s_momentumMs -= Constants.TickInterval;
            if (s_momentumMs <= 0) { s_mods.Remove(s_momentum); s_momentumOn = false; }
        }
        if (s_game != null && !e.IsOffline) RareItems.IntervalFloor(s_game);
        if (Tier == EliteTier.Normal || s_killed || s_combat == null || s_mods == null || e.IsOffline) return;
        if (s_combat.Action.State != CombatActionState.Fighting || s_combat.Action.IsPaused) return;
        s_fightMs += Constants.TickInterval;
        if (s_fightMs < 60_000) return;
        int steps = 1 + (int)((s_fightMs - 60_000) / 5000);
        if (steps == s_exhaustSteps) return;
        s_exhaustValues ??= Patches.Bonuses(Patches.Bonus("Character", "PercentDamageTaken", 0.05, "Enemy"));
        if (s_exhaustSteps == 0) s_mods.Add(s_exhaust, s_exhaustValues, steps);
        else s_mods.Update(s_exhaust, s_exhaustValues, steps);
        s_exhaustSteps = steps;
    }

    /// <summary>"of Momentum" on worn rare items: a kill makes attacks faster for 4 seconds.</summary>
    static void Momentum()
    {
        if (s_mods == null || s_game == null) return;
        double v = RareItems.WornCustom(s_game, "Momentum");
        if (v <= 0) return;
        if (s_momentumOn) s_mods.Remove(s_momentum);
        s_mods.Add(s_momentum, Patches.Bonuses(Patches.Bonus("Character", "PercentAttackInterval", -v / 100, "Player")));
        s_momentumOn = true;
        s_momentumMs = 4000;
    }

    /// <summary>A Slayer task monster or a dungeon's last boss (one more item level, one more item from an elite).</summary>
    /// <summary>The monster is your Slayer task now (its drops get +5 item level).</summary>
    public static bool OnSlayerTask(Enemy enemy) => s_combat?.SlayerTasks?.HasTaskForEnemy(enemy) == true;

    static bool Special(Enemy enemy)
    {
        if (s_combat == null) return false;
        var areas = s_combat.Areas;
        return (areas.CurrentArea?.Area is Dungeon && areas.CurrentProgress >= areas.MaxProgress) || s_combat.SlayerTasks?.HasTaskForEnemy(enemy) == true;
    }

    public static double FightSeconds => s_fightMs / 1000;
    public static int ExhaustSteps => s_exhaustSteps;
    public static bool Enraged => s_enraged;
}
