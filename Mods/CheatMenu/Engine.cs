using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Components.CombatEffects;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Localization;
using GamesByMalcs.Melvor2.Modifiers;
using Godot;
using MelvorModLoader;

namespace CheatMenu;

/// <summary>
/// Turns menu values into bonuses in the game's own modifier system (the one items, pets and prayers use).
/// Nothing is written into the save: bonuses are re-applied from settings.json each time a character is opened.
/// </summary>
public static class Engine
{
    sealed class Source(string name) : INamedObject
    {
        public ILocalizedString DisplayName { get; } = new LocalizedString(name);
    }

    sealed class Applied
    {
        public required Source Source;
        public required ModifierValues Values;
        public double Multiplier;
        /// <summary>For a setting that shortens times: each of its times on its own (see <see cref="Part"/>).</summary>
        public List<Part>? Parts;
    }

    /// <summary>
    /// One time of a time-shortening setting. Each is set on its own, because they do not all have the same room:
    /// "all skills" reaches most action times but not every one (not the survey of Hunting, for example).
    /// </summary>
    sealed class Part
    {
        public required Source Source;
        public required ModifierValues Values;
        /// <summary>The game's running total for this time, as every tree, animal or recipe of the skill gets it.</summary>
        public required ModifierQuery Query;
        public required ModifierContext Context;
        /// <summary>True when the "Skill action speed" setting shortens this time as well.</summary>
        public required bool ReachedByAll;
        /// <summary>
        /// The game's totals that are added to this one before the time is worked out. Hunting's tracking time,
        /// for example, is shortened by "tracking time" and by "shortest tracking time" together; its journal
        /// gives bonuses to the second. The worst of them counts.
        /// </summary>
        public List<(ModifierQuery Query, ModifierContext Context)> Companions = new();
        public double Multiplier;

        /// <summary>
        /// The time's total for the one tree, animal or recipe that gets the most off: what all of them get, and
        /// on top the worst of what only some of them get (a Hunting journal, a bonus for smelting only).
        /// </summary>
        public double Others => Worst(Query, Context) + (Companions.Count == 0 ? 0 : Math.Min(0, Companions.Min(c => Worst(c.Query, c.Context))));
        public IEnumerable<ModifierQuery> Queries => Companions.Select(c => c.Query).Prepend(Query);
    }

    static readonly PropertyInfo? s_tables = typeof(ModifierQuery).GetProperty("Tables", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly PropertyInfo? s_entries = typeof(ModifierTable).GetProperty("Entries", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// A query's total plus the most that only some of the skill's objects get on top. The game keeps every bonus
    /// in a table with the objects it is for (a tree, an animal, a category of recipes); the query sees only what
    /// counts for all of them. Of what counts for fewer, per kind of object (animal, tool, category...) the worst
    /// single one is taken, and the kinds are added up: an object has one of each.
    /// </summary>
    static double Worst(ModifierQuery query, ModifierContext context)
    {
        double total = query.Total;
        try
        {
            if (s_tables?.GetValue(query) is not IEnumerable<ModifierTable> tables || s_entries == null) return total;
            IReadOnlySet<object> mine = context.Objects;
            var byKind = new Dictionary<string, Dictionary<string, double>>();
            var seen = new HashSet<ModifierEntry>();
            foreach (ModifierTable table in tables)
            {
                if (s_entries.GetValue(table) is not IDictionary<INamedObject, List<ModifierEntry>> entries) continue;
                foreach (List<ModifierEntry> list in entries.Values)
                    foreach (ModifierEntry entry in list)
                    {
                        if (!seen.Add(entry)) continue;
                        IReadOnlySet<object> theirs = entry.BaseValue.Context.Objects;
                        if (theirs.IsSubsetOf(mine) || !mine.IsSubsetOf(theirs)) continue;      // counted already, or another skill's
                        var extra = theirs.Where(o => !mine.Contains(o)).ToList();
                        string kind = string.Join("+", extra.Select(o => o.GetType().Name).OrderBy(n => n));
                        string which = string.Join("+", extra.Select(o => (o as GamesByMalcs.Melvor2.Data.NamespacedObject)?.Id ?? o.GetHashCode().ToString()).OrderBy(n => n));
                        if (!byKind.TryGetValue(kind, out var sums)) byKind[kind] = sums = new();
                        sums[which] = sums.GetValueOrDefault(which) + entry.Value;
                    }
            }
            foreach (var sums in byKind.Values) total += Math.Min(0, sums.Values.Min());
        }
        catch (Exception e) { Log("A speed bonus of the game could not be read: " + e.Message); }
        return total;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Modifiers")]
    static extern ModifierSystem GetModifiers(Game game);

    static Game? s_game;
    static ModifierSystem? s_mods;
    static readonly Dictionary<string, Applied> s_applied = new();
    static Dictionary<string, double> s_values = new();

    public static Action<string> Log { get; set; } = _ => { };
    public static Game? Game => s_game;
    public static string Character { get; private set; } = "";

    /// <summary>Stats the running game version did not accept (shown in the log, skipped).</summary>
    public static List<string> Problems { get; } = new();

    /// <summary>Call when a character is opened. Loads that character's settings and applies them.</summary>
    public static void Bind(Game game)
    {
        if (ReferenceEquals(s_game, game)) return;
        Settings.Flush();
        s_game = game;
        s_mods = GetModifiers(game);
        foreach (Applied old in s_applied.Values)
            foreach (Part part in old.Parts ?? new())
                foreach (ModifierQuery query in part.Queries)
                    try { query.Destroy(); } catch { }
        s_applied.Clear();
        s_dirty = false;
        s_cropFrames = 0;
        Problems.Clear();
        Character = game.CharacterName;
        s_values = Settings.Load(Character);
        AutoSell.Bind(Character);
        LootFilter.Bind(game, Character);
        Unneeded.Bind(game, Character);
        InfiniteAmmo.Bind(game);
        RanchingForever.Bind(game);
        foreach (Feature f in Features.All) Apply(f);
        Rebalance();
        SlayerLength.Check(game);   // before offline progress, so tasks rolled while the game was closed are short too
        Log($"'{Character}': {ActiveCount()} setting(s) applied.");
    }

    /// <summary>How many settings differ from the game's normal behaviour (optionally in one tab).</summary>
    public static int ActiveCount(string? tab = null) =>
        s_values.Count(v => Features.Find(v.Key) is { } f && v.Value != f.Off && (tab == null || f.Tab == tab));

    public static bool IsActive(string id) => GetValue(id) != Features.Get(id).Off;

    public static double GetValue(string id) => s_values.TryGetValue(id, out double v) ? v : Features.Get(id).Off;

    public static void SetValue(string id, double value)
    {
        Feature f = Features.Get(id);
        value = f.Kind == FeatureKind.Toggle ? (value >= 0.5 ? 1 : 0) : Math.Clamp(value, f.Min, f.Max);
        if (value == f.Off) s_values.Remove(id); else s_values[id] = value;
        Apply(f);
        if (id == AllSkillsSpeed || ShortensTime(f) || Features.IsNormalPace(id)) { Rebalance(); RetimeCrops(); }
        Settings.Save(Character, s_values);
        Changed?.Invoke();
    }

    /// <summary>The same value for several settings, as one change.</summary>
    public static void SetValues(IEnumerable<string> ids, double value)
    {
        foreach (string id in ids)
        {
            Feature f = Features.Get(id);
            double v = f.Kind == FeatureKind.Toggle ? (value >= 0.5 ? 1 : 0) : Math.Clamp(value, f.Min, f.Max);
            if (v == f.Off) s_values.Remove(id); else s_values[id] = v;
            Apply(f);
        }
        Rebalance();
        RetimeCrops();
        Settings.Save(Character, s_values);
        Changed?.Invoke();
    }

    /// <summary>Raised after any value changed (the menu repaints its counters).</summary>
    public static event Action? Changed;

    public static void ResetAll()
    {
        s_values.Clear();
        foreach (Feature f in Features.All) Apply(f);
        Rebalance();
        RetimeCrops();
        Settings.Save(Character, s_values);
        Changed?.Invoke();
    }

    /// <summary>For tests: what the game's modifier tables report for each stat of a feature right now.</summary>
    public static List<string> Diagnose(string id)
    {
        var lines = new List<string>();
        if (s_mods == null || !s_applied.TryGetValue(id, out Applied? applied)) return lines;
        foreach (ComponentModifierValue v in applied.Values.Component)
        {
            ModifierQuery query = s_mods.CreateQueryForStat(v.Stat, v.LocalContext);
            lines.Add($"{v.Stat.Name}={query.Total:0.###}");
            query.Destroy();
        }
        foreach (GlobalModifierValue g in applied.Values.Global)
            lines.Add($"{g.StatGroup}(global) x{applied.Multiplier:0.###}");
        return lines;
    }

    /// <summary>
    /// The game adds time reductions up: "x2" is -50% and "x4" is -75%, together -125%, a time below zero. The
    /// game then throws on every tick of that action ("WaitTime must be greater than the tick interval") and
    /// everything behind it stops. So no action is ever left with less than this share of its normal time,
    /// whatever is combined (the menu's own settings, and the game's tools, potions and upgrades).
    /// </summary>
    const double LeastTimeLeft = 0.05;

    const string AllSkillsSpeed = "speed.actions";
    const string AllSkillsGroup = "GlobalPercentActionInterval";

    static bool IsSkillSpeed(Feature f) => f.Id.StartsWith("skill.") && f.Id.EndsWith(".speed");

    /// <summary>True when the skill is kept at its normal pace (see <see cref="Features.NormalPace"/>).</summary>
    public static bool KeepsNormalPace(string? skill) => Features.NormalPaceOf(skill) is { } id && IsActive(id);

    /// <summary>A setting that shortens a time (its stats count downwards).</summary>
    static bool ShortensTime(Feature f) => f.Stats.Count > 0 && f.Stats.All(s => s.Unit < 0 && s.Name.Contains("Interval"));

    /// <summary>True while the menu itself is changing bonuses, so its own changes are not taken for the game's.</summary>
    static bool s_ours;
    /// <summary>Set when something else (a tool, a potion, an upgrade of the game) changed one of the watched times.</summary>
    static bool s_dirty;

    static void Apply(Feature f)
    {
        if (f.Custom || s_game == null || s_mods == null) return;
        double value = GetValue(f.Id);
        double multiplier = value == f.Off ? 0 : f.Kind == FeatureKind.Toggle ? 1 : f.ToMultiplier(value);
        if (ShortensTime(f)) ApplyTime(f, multiplier);
        else Use(f, multiplier);
    }

    /// <summary>
    /// A time-shortening setting, one time after the other.
    /// - One skill's speed and the speed of all skills multiply (x2 and x4 are x8), the way a player expects. In
    ///   the game's adding terms: the skill's own share is counted on top of what "all skills" took off.
    /// - Never more than there is room for: what the game makes of the time with everything counted (the other
    ///   settings, and the game's own tools, potions and upgrades), without this setting's own share, must leave
    ///   <see cref="LeastTimeLeft"/> of the normal time. Below zero room this becomes a small bonus the other way.
    /// The totals stay watched, so a bonus arriving later is noticed (see <see cref="Guard"/>).
    /// </summary>
    static void ApplyTime(Feature f, double multiplier)
    {
        if (s_game == null || s_mods == null) return;
        Applied applied = Entry(f);
        if (applied.Parts == null)
        {
            applied.Parts = new List<Part>();
            ModifierQuery Watch(ComponentModifierValue v)
            {
                ModifierQuery query = s_mods.CreateQueryForStat(v.Stat, v.LocalContext);
                query.TotalChanged += (_, _) => { if (!s_ours) s_dirty = true; };
                // a bonus for one animal or recipe only does not change the query's total: watch its tables too
                try
                {
                    if (s_tables?.GetValue(query) is IEnumerable<ModifierTable> tables)
                        foreach (ModifierTable table in tables)
                            if (s_watched.Add(table)) table.EntryAdded += (_, _) => { if (!s_ours) s_dirty = true; };
                }
                catch { }
                return query;
            }
            foreach (ComponentModifierValue v in applied.Values.Component)
            {
                var part = new Part
                {
                    Source = new Source("Cheat Menu: " + f.En),
                    Values = new ModifierValues(new List<GlobalModifierValue>(), new List<ComponentModifierValue> { v }, new List<CombatEffectModifierValue>()),
                    Query = Watch(v),
                    Context = v.LocalContext,
                    ReachedByAll = v.Stat.StatGroups.Contains(AllSkillsGroup),
                };
                // "PercentTrackingInterval" is added to "PercentMinTrackingInterval" and "PercentMaxTrackingInterval".
                StatRef? stat = f.Stats.FirstOrDefault(s => s.Name == v.Stat.Name);
                if (stat != null && stat.Name.StartsWith("Percent") && !stat.Name.StartsWith("PercentMin") && !stat.Name.StartsWith("PercentMax"))
                    foreach (string side in new[] { "Min", "Max" })
                        if (One(stat with { Name = "PercentMin".Replace("Min", side) + stat.Name["Percent".Length..] }, s_game) is { } companion)
                            part.Companions.Add((Watch(companion), companion.LocalContext));
                applied.Parts.Add(part);
            }
        }
        double all = IsSkillSpeed(f) ? Math.Max(1, GetValue(AllSkillsSpeed)) : 1;
        // A skill kept at its normal pace gets back what "all skills" took off, and only its own speed counts:
        // all x5 (-80%) and own x2 (-50%) is -80% + 30% = -50%; without an own speed -80% + 80% = 0.
        bool normal = IsSkillSpeed(f) && KeepsNormalPace(f.Skill);
        s_ours = true;
        try
        {
            foreach (Part part in applied.Parts)
            {
                double want = !part.ReachedByAll ? multiplier : normal ? 1 / all - (1 - multiplier) : multiplier / all;
                double others = part.Others + part.Multiplier;               // this part counts as -1 x its strength
                double strength = Math.Min(want, others + (1 - LeastTimeLeft));
                if (Math.Abs(strength) < 1e-9)
                {
                    if (part.Multiplier != 0) s_mods.Remove(part.Source);
                    part.Multiplier = 0;
                    continue;
                }
                if (Math.Abs(strength - part.Multiplier) < 1e-9) continue;
                if (part.Multiplier == 0) s_mods.Add(part.Source, part.Values, strength);
                else s_mods.Update(part.Source, part.Values, strength);
                part.Multiplier = strength;
            }
        }
        finally { s_ours = false; }
    }

    static Applied Entry(Feature f)
    {
        if (!s_applied.TryGetValue(f.Id, out Applied? applied))
        {
            applied = new Applied { Source = new Source("Cheat Menu: " + f.En), Values = Build(f, s_game!) };
            s_applied[f.Id] = applied;
        }
        return applied;
    }

    /// <summary>Puts a feature's bonus into the game with this strength (0 takes it out).</summary>
    static void Use(Feature f, double multiplier)
    {
        if (s_game == null || s_mods == null) return;
        Applied applied = Entry(f);
        s_ours = true;
        try
        {
            if (Math.Abs(multiplier) < 1e-9)
            {
                if (applied.Multiplier != 0)
                {
                    s_mods.Remove(applied.Source);
                    applied.Multiplier = 0;
                }
                return;
            }
            if (applied.Multiplier == 0) s_mods.Add(applied.Source, applied.Values, multiplier);
            else s_mods.Update(applied.Source, applied.Values, multiplier);
            applied.Multiplier = multiplier;
        }
        finally { s_ours = false; }
    }

    /// <summary>
    /// Called every frame by the mod: when the game itself changed one of the watched times (a better tool, a
    /// potion), the settings are worked out again before an action can end up with no time at all.
    /// </summary>
    public static void Guard()
    {
        if (s_cropFrames > 0 && s_game != null)
        {
            s_cropFrames--;
            Crops.Retime(s_game);
        }
        if (!s_dirty) return;
        s_dirty = false;
        Rebalance();
    }

    /// <summary>Tables of the game's bonuses whose new entries are watched (a journal level, a new tool).</summary>
    static readonly HashSet<ModifierTable> s_watched = new();

    /// <summary>Frames left in which the crops in the ground are still checked after a speed setting changed.</summary>
    static int s_cropFrames;

    /// <summary>
    /// A speed setting was changed by the player: crops already growing get their new time as well (see
    /// <see cref="Crops"/>). Once now, and for the next half second in case the game works its numbers out later.
    /// Only after the player's own change - a bonus of the game arriving meanwhile is left as the game has it.
    /// </summary>
    static void RetimeCrops()
    {
        if (s_game == null) return;
        s_cropFrames = 30;
        Crops.Retime(s_game);
    }

    /// <summary>For tests: a bonus from somewhere else on the same times as a setting. Returns how to take it back.</summary>
    public static Action AddOutside(string id, double multiplier)
    {
        var source = new Source("Test: a bonus from elsewhere");
        ModifierSystem mods = s_mods!;
        mods.Add(source, Build(Features.Get(id), s_game!), multiplier);
        return () => mods.Remove(source);
    }

    /// <summary>
    /// After a speed setting changed: every setting that shortens a time is worked out again, "all skills"
    /// first, because each skill's share depends on it.
    /// </summary>
    static void Rebalance()
    {
        foreach (Feature f in Features.All.Where(ShortensTime)) Apply(f);
        Recover();
    }

    /// <summary>
    /// An action whose timer died on a time below zero stays dead although the setting is fixed now: it still
    /// counts as started, but nothing fires any more. Start its timer again.
    /// </summary>
    static void Recover()
    {
        if (s_game == null) return;
        try
        {
            foreach (var action in s_game.ActionManager.ActiveActions)
                if (action.IsStarted && action.ActionTimer is { IsStopped: true } timer) timer.Start();
        }
        catch (Exception e) { Log("A stopped action could not be started again: " + e.Message); }
    }

    static ModifierValues Build(Feature f, Game game)
    {
        var globals = new List<GlobalModifierValue>();
        var components = new List<ComponentModifierValue>();
        foreach (GlobalRef g in f.Globals)
        {
            string json = "{\"StatGroup\": \"" + g.StatGroup + "\", \"Value\": " + Num(g.Unit) + ", \"Condition\": null, \"Scaling\": null}";
            try { globals.Add(new GlobalModifierValue(game.Data, JsonDocument.Parse(json).RootElement)); }
            catch (Exception e) { Problem(f, g.StatGroup, e); }
        }
        foreach (StatRef s in f.Stats)
        {
            string json = "{\"Stat\": {\"StatsType\": \"MelvorBase:" + s.StatsType + "\", \"Name\": \"" + s.Name + "\"}, \"Value\": " + Num(s.Unit)
                + ", \"LocalContext\": {\"Component\": {\"Entity\": \"MelvorBase:" + s.Entity + "\", \"Component\": \"" + s.Component + "\"}"
                + (s.Context == null ? "" : ", " + s.Context) + "}"
                + ", \"FromContext\": null, \"Condition\": null, \"Scaling\": null}";
            try { components.Add(new ComponentModifierValue(game.Data, JsonDocument.Parse(json).RootElement)); }
            catch (Exception e) { Problem(f, $"{s.StatsType}.{s.Name} @ {s.Entity}/{s.Component}", e); }
        }
        return new ModifierValues(globals, components, new List<CombatEffectModifierValue>());
    }

    /// <summary>One stat as a bonus value, or null when the game has no such stat (asked for quietly).</summary>
    static ComponentModifierValue? One(StatRef s, Game game)
    {
        string json = "{\"Stat\": {\"StatsType\": \"MelvorBase:" + s.StatsType + "\", \"Name\": \"" + s.Name + "\"}, \"Value\": " + Num(s.Unit)
            + ", \"LocalContext\": {\"Component\": {\"Entity\": \"MelvorBase:" + s.Entity + "\", \"Component\": \"" + s.Component + "\"}"
            + (s.Context == null ? "" : ", " + s.Context) + "}"
            + ", \"FromContext\": null, \"Condition\": null, \"Scaling\": null}";
        try { return new ComponentModifierValue(game.Data, JsonDocument.Parse(json).RootElement); }
        catch { return null; }
    }

    /// <summary>For tests: a bonus from somewhere else on any one stat (a Hunting journal level, say). Returns how to take it back.</summary>
    public static Action AddOutside(StatRef stat, double multiplier)
    {
        var source = new Source("Test: a bonus on " + stat.Name);
        ModifierSystem mods = s_mods!;
        ComponentModifierValue value = One(stat, s_game!) ?? throw new ArgumentException("no such stat: " + stat.Name);
        mods.Add(source, new ModifierValues(new List<GlobalModifierValue>(), new List<ComponentModifierValue> { value }, new List<CombatEffectModifierValue>()), multiplier);
        return () => mods.Remove(source);
    }

    /// <summary>For tests: the companions found for a feature's times ("PercentTrackingInterval+2", ...).</summary>
    public static List<string> Companions(string id) =>
        s_applied.TryGetValue(id, out Applied? applied) && applied.Parts != null
            ? applied.Parts.Select(p => $"{p.Values.Component[0].Stat.Name}+{p.Companions.Count}").ToList()
            : new();

    static void Problem(Feature f, string what, Exception e)
    {
        string msg = $"{f.Id}: {what} -> {(e.InnerException ?? e).Message}";
        Problems.Add(msg);
        Log("Skipped bonus: " + msg);
    }

    static string Num(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>Per-character values in settings.json next to the mod DLL.</summary>
static class Settings
{
    public static string Dir = "";
    static string FilePath => Path.Combine(Dir, "settings.json");
    static string s_character = "";
    static Dictionary<string, double> s_mine = new();

    public static Dictionary<string, double> Load(string character)
    {
        Store.Read<Dictionary<string, double>>(FilePath).TryGetValue(character, out Dictionary<string, double>? values);
        var known = Features.All.Select(f => f.Id).ToHashSet();
        return values?.Where(v => known.Contains(v.Key)).ToDictionary(v => v.Key, v => v.Value) ?? new();
    }

    static bool s_dirty;

    /// <summary>Remembers the values; the file itself is written by <see cref="Flush"/> a moment later.</summary>
    public static void Save(string character, Dictionary<string, double> values)
    {
        if (character == "") return;
        s_character = character;
        s_mine = new Dictionary<string, double>(values);
        s_dirty = true;
    }

    /// <summary>Writes settings.json if something changed since the last write.</summary>
    public static void Flush()
    {
        if (!s_dirty) return;
        s_dirty = false;
        Store.Write(FilePath, s_character, s_mine, "The settings");
    }
}

/// <summary>
/// The mod's files hold one entry per character. A write replaces only the open character's entry in what is
/// on disk at that moment, so two running copies of the game cannot wipe each other's characters.
/// </summary>
static class Store
{
    public static Dictionary<string, T> Read<T>(string path)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, T>>(File.ReadAllText(path)) ?? new(); }
        catch { return new(); }
    }

    public static void Write<T>(string path, string character, T entry, string what)
    {
        if (character == "") return;
        try
        {
            Dictionary<string, T> all = Read<T>(path);
            all[character] = entry;
            File.WriteAllText(path, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Engine.Log(what + " could not be saved: " + e.Message); }
    }
}

/// <summary>Whole-game speed: runs extra game ticks on every physics frame, while the game itself is ticking.</summary>
static class GameSpeed
{
    static GameDriver? s_driver;
    static double s_debt, s_ranchDebt;

    public const string RanchingSpeed = "skill.Ranching.speed";

    public static SceneTree? Tree { get; set; }

    public static void OnPhysicsFrame()
    {
        Game? game = Engine.Game;
        if (game is not { IsSaveLoaded: true }) return;
        double speed = Engine.GetValue("speed.game");
        double ranch = Engine.GetValue(RanchingSpeed);
        if (speed <= 1) s_debt = 0;
        if (ranch <= 1) s_ranchDebt = 0;
        if (speed <= 1 && ranch <= 1) return;

        // While the game computes offline progress it ticks itself on another thread and pauses the scene tree.
        // Ticking it from here at the same time would have two threads inside the game at once.
        if (Tree is { Paused: true }) { s_debt = 0; s_ranchDebt = 0; return; }

        if (s_driver == null || !GodotObject.IsInstanceValid(s_driver))
            s_driver = GameAccess.FindNode<GameDriver>();
        // The game pauses its own ticking during offline catch-up and loading; stay in step with it.
        if (s_driver == null || !s_driver.IsPhysicsProcessing()) return;

        if (speed > 1)
        {
            s_debt += speed - 1;
            if (s_debt >= 1)
            {
                List<PassiveComponent> resting = SitOut(game);
                try
                {
                    while (s_debt >= 1)
                    {
                        game.PropagateTick();
                        s_debt -= 1;
                    }
                }
                finally
                {
                    foreach (PassiveComponent part in resting) part.DisableTicking = false;
                }
            }
        }

        // Ranching's own speed. The game starts the animals' product time at a fixed hour and never reads the
        // "product interval" bonus, so a bonus cannot speed Ranching up: it gets extra ticks of its own instead,
        // on top of the ones it had this frame (one, or one per game speed step when Game speed reaches it).
        if (ranch > 1 && game.Entities.TryGetObjectById("MelvorBase:Ranching", out GameEntity? ranching) && ranching != null)
        {
            double had = speed > 1 && !Engine.KeepsNormalPace("Ranching") ? speed : 1;
            s_ranchDebt += (ranch - 1) * had;
            foreach (PassiveComponent part in ranching.Components.Values.OfType<PassiveComponent>())
                if (!part.DisableTicking)
                    for (double d = s_ranchDebt; d >= 1; d--) part.Tick();
            s_ranchDebt -= Math.Floor(s_ranchDebt);
        }
    }

    /// <summary>
    /// The passive parts (crops, grain mill, animals) of the skills kept at their normal pace, switched off for
    /// the extra ticks with the game's own switch. Parts the game has switched off itself are left alone, so they
    /// are not switched on again afterwards.
    /// </summary>
    static List<PassiveComponent> SitOut(Game game)
    {
        var resting = new List<PassiveComponent>();
        foreach ((string setting, _, string entityId) in Features.NormalPace)
        {
            if (!Engine.IsActive(setting) || !game.Entities.TryGetObjectById(entityId, out GameEntity? entity) || entity == null) continue;
            foreach (PassiveComponent part in entity.Components.Values.OfType<PassiveComponent>())
                if (!part.DisableTicking)
                {
                    part.DisableTicking = true;
                    resting.Add(part);
                }
        }
        return resting;
    }
}
