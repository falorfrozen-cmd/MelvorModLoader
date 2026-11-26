using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components;
using GamesByMalcs.Melvor2.Components.MasteryGuild;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Goals;
using GamesByMalcs.Melvor2.Triggers;

namespace ActionQueue;

/// <summary>
/// The Mastery Guild's tasks as a step's goal: "until this mastery task is complete" (MagnusBR on Nexus, 26 Nov).
/// Every skill has its own list of tasks in six tiers (Novice upwards); a fight works on the tasks of the combat skills.
/// The queue only reads them: whether a task is complete and how far it has come. That also works on the game's own
/// thread for offline progress (nothing is changed). A task is found by its id across every skill.
/// </summary>
public static class Mastery
{
    /// <summary>One task as the queue shows it.</summary>
    public sealed class Task
    {
        public string Id { get; init; } = "";
        /// <summary>The skill whose task it is (MelvorBase:Fishing).</summary>
        public string Entity { get; init; } = "";
        public string Skill { get; init; } = "";
        /// <summary>Its tier (Novice, Easy, ...) and the tier's place, from the easiest.</summary>
        public string Tier { get; init; } = "";
        public int TierOrder { get; init; }
        /// <summary>What its count goes up to (0 for a task without a count): the ladder of one target in its order.</summary>
        public double Size { get; init; }
        /// <summary>The ids the task names (a recipe, a monster, an item, also in its conditions): how it is matched to a step.</summary>
        public IReadOnlySet<string> Mentions { get; init; } = new HashSet<string>();
        internal MasteryGuildTask Data { get; init; } = null!;
        internal ActiveMasteryGuildTask Active { get; init; } = null!;
        /// <summary>The game's counter of a counted task (null for a condition or trigger task); tests set it near the end.</summary>
        public GamesByMalcs.Melvor2.Goals.IActiveGoal? Counter => Active.ActiveGoal;
        public bool Done => Active.IsComplete;
        /// <summary>The game's own words for it, in the game's language of the moment ("Catch Deep Blue 2,500 times (using ...)").</summary>
        public string Text => Describe(Data, Active);
    }

    /// <summary>The skills whose tasks a fight works on.</summary>
    public static readonly string[] CombatSkills =
    {
        "MelvorBase:Attack", "MelvorBase:Strength", "MelvorBase:Defence", "MelvorBase:Hitpoints",
        "MelvorBase:Ranged", "MelvorBase:Magic", "MelvorBase:Prayer", "MelvorBase:Slayer",
    };

    static readonly object s_lock = new();
    static Dictionary<string, Task> s_byId = new();
    static List<Task> s_all = new();

    /// <summary>Reads every skill's task list (call when a character is loaded).</summary>
    public static void Bind(Game game)
    {
        lock (s_lock) { s_all = new List<Task>(); s_byId = new Dictionary<string, Task>(); }   // never another character's tasks
        var all = new List<Task>();
        foreach (GameEntity entity in game.Entities.GetAllObjects())
            foreach (MasteryGuildComponent guild in entity.Components.Values.OfType<MasteryGuildComponent>())
            {
                // The tiers' order: the points a task of that tier is worth (1 for Novice, more for harder ones).
                var tiers = guild.Categories.Keys.OrderBy(c => c.BasePoints).ToList();
                foreach ((MasteryGuildTask task, ActiveMasteryGuildTask active) in guild.ActiveTasks)
                {
                    try
                    {
                        all.Add(new Task
                        {
                            Id = task.Id, Entity = entity.Id, Skill = entity.DisplayName.Text,
                            Tier = task.Category.DisplayName.Text, TierOrder = tiers.IndexOf(task.Category),
                            Size = task.Goal?.MaxProgress ?? 0, Mentions = Mentions(task), Data = task, Active = active,
                        });
                    }
                    catch (Exception e) { Queue.Log($"Mastery task {task.Id} could not be read: {e.Message}"); }
                }
            }
        lock (s_lock)
        {
            s_all = all;
            s_byId = all.GroupBy(t => t.Id).ToDictionary(g => g.Key, g => g.First());
        }
    }

    /// <summary>The task with this id, or null (not in this game: another version, a mod's task gone).</summary>
    public static Task? Find(string id)
    {
        lock (s_lock) return s_byId.TryGetValue(id, out Task? t) ? t : null;
    }

    /// <summary>
    /// The task's words for a step or the queue's stop: read from the game in its language of the moment, or the words
    /// saved with the goal when the task is not in this game (and off the main thread: offline progress).
    /// </summary>
    public static string NameOf(string id, string saved) => Targets.OnMainThread && Find(id) is { } t ? t.Text : saved;

    /// <summary>True when the task names one of these ids (the step's target, or what it makes).</summary>
    public static bool IsFor(Task t, IReadOnlyCollection<string> ids) => ids.Any(t.Mentions.Contains);

    /// <summary>
    /// The tasks a step can wait for: its skill's (a fight: the combat skills'). Those that name the step's target (or what
    /// it makes) come first: the unfinished ones from the easiest tier and the smallest count up, then the finished
    /// ones. Then the rest by skill and tier, unfinished first. A fixed order: it does not move as the counts grow.
    /// </summary>
    public static List<Task> For(string entity, IReadOnlyCollection<string> ids)
    {
        List<Task> all;
        lock (s_lock) all = s_all;
        bool fight = entity == "MelvorBase:Combat";
        return all.Where(t => fight ? CombatSkills.Contains(t.Entity) : t.Entity == entity)
            .OrderBy(t => IsFor(t, ids) ? 0 : 1)
            .ThenBy(t => IsFor(t, ids) && t.Done ? 1 : 0)
            .ThenBy(t => fight && !IsFor(t, ids) ? Array.IndexOf(CombatSkills, t.Entity) : 0)
            .ThenBy(t => t.TierOrder)
            .ThenBy(t => t.Done ? 1 : 0)
            .ThenBy(t => t.Size)
            .ToList();
    }

    /// <summary>Every task of every skill, by skill (the skills in <paramref name="first"/> on top), tier and count.</summary>
    public static List<Task> All(IReadOnlyCollection<string> first)
    {
        List<Task> all;
        lock (s_lock) all = s_all;
        var order = all.Select(t => t.Entity).Distinct().ToList();
        return all.OrderBy(t => first.Contains(t.Entity) ? 0 : 1).ThenBy(t => order.IndexOf(t.Entity))
            .ThenBy(t => t.TierOrder).ThenBy(t => t.Done ? 1 : 0).ThenBy(t => t.Size).ToList();
    }

    /// <summary>"1,234 / 2,500", "40%" (a task with a condition), "not yet" (done or not, nothing in between) or "complete".</summary>
    public static string ProgressText(Task t)
    {
        if (t.Done) return Lang.T("mastery.complete", "complete");
        ActiveMasteryGuildTask a = t.Active;
        if (a.ActiveGoal is { } goal)
            return goal.MaxProgress > 0 ? $"{Math.Floor(Math.Min(goal.CurrentProgress, goal.MaxProgress)):N0} / {goal.MaxProgress:N0}" : Lang.T("mastery.not_yet", "not yet");
        if (a.ConditionGoal is { } condition) return $"{Math.Floor(Math.Clamp(condition.ProgressPercent, 0, 1) * 100):0}%";
        return Lang.T("mastery.not_yet", "not yet");
    }

    static readonly Regex s_pictures = new(@"\[img[^\]]*\].*?\[/img\]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    /// <summary>The game writes a task's count into its words ("Catch ... [color=..]201 / 2,000[/color] times"): only the target stays.</summary>
    static readonly Regex s_count = new(@"\[color=[^\]]*\]([^\[\]/]*)/([^\[\]]*)\[/color\]", RegexOptions.IgnoreCase);
    static readonly Regex s_markup = new(@"\[/?[a-z_]+(=[^\]]*)?[^\]]*\]", RegexOptions.IgnoreCase);
    static readonly Regex s_spaces = new(@"\s+");

    static string Plain(string text) =>
        s_spaces.Replace(s_markup.Replace(s_pictures.Replace(s_count.Replace(text, m => m.Groups[2].Value.Trim()), ""), ""), " ").Trim();

    /// <summary>
    /// The task's words as the game's Mastery Guild shows them, without the colour markup and without the count it has
    /// reached ("Catch a Fish in Calm Shallows 2,000 times": the progress is shown apart). A goal's own conditions
    /// ("while using Melee") are part of it, in the game's own words, so two tasks that differ only there do not look
    /// the same; after a task's own custom words they follow in brackets.
    /// </summary>
    static string Describe(MasteryGuildTask task, ActiveMasteryGuildTask active)
    {
        try
        {
            if (task.CustomDescription?.Text is { Length: > 0 } custom)
            {
                string text = Plain(custom);
                var conditions = (task.Goal?.Conditions?.Conditions ?? (IEnumerable<Condition>)Array.Empty<Condition>())
                    .Select(c => { try { return Plain(c.GetDescription(centered: false, coloured: false)); } catch { return ""; } })
                    .Where(c => c != "").ToList();
                if (conditions.Count > 0) text += $" ({string.Join("; ", conditions)})";
                return text == "" ? task.Id : text;
            }
            string words = Plain(active.ActiveGoal?.GetLocalDescription()
                ?? active.ConditionGoal?.GetTaskDescription(centered: false, coloured: false)
                ?? active.TriggerGoal?.GetLocalDescription() ?? "");
            return words == "" ? task.Id : words;
        }
        catch { return task.Id; }
    }

    static readonly ConcurrentDictionary<Type, PropertyInfo[]> s_props = new();

    /// <summary>
    /// The ids of the game objects a task names: in its goal, the goal's conditions, its condition or trigger (a few levels
    /// deep, only through the game's goal, condition and trigger data, never into a skill or the game's state).
    /// </summary>
    static HashSet<string> Mentions(MasteryGuildTask task)
    {
        var ids = new HashSet<string>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        static bool IsRule(object o) => o is Goal or Condition or ConditionGroup or Trigger;
        void Value(object? value, int depth)
        {
            switch (value)
            {
                case null or string or GameComponent or IDictionary: return;   // never the game's state
                case var rule when IsRule(rule):
                    if (depth > 0) Look(rule, depth - 1);
                    return;
                case INamespacedObject named: ids.Add(named.Id); return;   // named, not followed (a recipe, an item, a monster)
                case IEnumerable list:
                    if (depth <= 0 || !seen.Add(list)) return;
                    foreach (object? element in list)
                        if (element is INamespacedObject one && !IsRule(one)) ids.Add(one.Id);
                        else if (element != null && IsRule(element)) Look(element, depth - 1);
                    return;
            }
        }
        void Look(object? o, int depth)
        {
            if (o == null || !seen.Add(o)) return;
            foreach (PropertyInfo p in s_props.GetOrAdd(o.GetType(), t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && !p.PropertyType.IsValueType && p.PropertyType != typeof(string)).ToArray()))
            {
                object? value;
                try { value = p.GetValue(o); } catch { continue; }
                Value(value, depth);
            }
        }
        Look(task.Goal, 3);
        Look(task.ConditionGoal, 3);
        Look(task.TriggerGoal, 3);
        return ids;
    }
}
