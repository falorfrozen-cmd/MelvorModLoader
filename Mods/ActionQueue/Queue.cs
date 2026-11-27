using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Components.ArtisanAction;
using GamesByMalcs.Melvor2.Entities.Agility;
using GamesByMalcs.Melvor2.Entities.Hunting;
using GamesByMalcs.Melvor2.EventBus;
using GamesByMalcs.Melvor2.Saves;
using GamesByMalcs.Melvor2.Components.ItemSales;
using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Entities.Astrology;

namespace ActionQueue;

/// <summary>When a step is over.</summary>
public enum Until { Stops, Item, Level, Minutes, ItemLeft, Made, Times, Spent, Nodes, Event, Mastery, FoodRunsOut }   // new kinds go at the end: saved queues store the number

/// <summary>
/// One goal of a step and what it counts from. A step is a goal itself; <see cref="Step.Or"/> is its second one.
/// (Internal: the counters are the runner's business, not the window's or the saved file's.)
/// </summary>
internal interface IGoal
{
    Until Until { get; }
    double Amount { get; }
    string Subject { get; }
    string SubjectName { get; }
    /// <summary>"Make N more" / "N times" / "use up N" / "seen N times": what was counted before a pause.</summary>
    long CountBefore { get; set; }
    long GainedAtStart { get; set; }
    long DoneAtStart { get; set; }
    long SpentAtStart { get; set; }
    long SeenAtStart { get; set; }
    int OwnedAtStart { get; set; }
}

/// <summary>A step's second goal ("... or until ..."): the step is over when either one is reached.</summary>
public sealed class Goal : IGoal
{
    public Until Until { get; set; }
    public double Amount { get; set; }
    public string Subject { get; set; } = "";
    public string SubjectName { get; set; } = "";
    long IGoal.CountBefore { get; set; }
    long IGoal.GainedAtStart { get; set; }
    long IGoal.DoneAtStart { get; set; }
    long IGoal.SpentAtStart { get; set; }
    long IGoal.SeenAtStart { get; set; }
    int IGoal.OwnedAtStart { get; set; }

    public Goal Copy() => new() { Until = Until, Amount = Amount, Subject = Subject, SubjectName = SubjectName };
    public bool SameAs(Goal? other) => other != null && other.Until == Until && other.Amount == Amount && other.Subject == Subject;
    [JsonIgnore] public string Text => Step.GoalText(Until, Amount, SubjectName, Subject);
}

/// <summary>Idle: not started yet. Waiting: its turn has come but it cannot run right now. Done: over (also when skipped).</summary>
public enum RunState { Idle, Running, Waiting, Done }

/// <summary>
/// What a step's <see cref="Step.State"/> means (0.14.0). The words can be in any language, so the queue and the window
/// decide by this, never by the words.
/// </summary>
public enum StepStatus { None, Running, Waiting, Paused, Done, Skipped, Stopped, NotTaken }

/// <summary>How the window colours what the idle action is up to (0.14.0: not read from the words).</summary>
public enum IdleTone { Plain, Good, Bad }

/// <summary>One line of the queue: do this, until that.</summary>
public sealed class Step : IGoal
{
    public string Entity { get; set; } = "";      // the skill, e.g. MelvorBase:Woodcutting
    public string TargetId { get; set; } = "";    // the tree, recipe, monster...
    public string Extra { get; set; } = "";       // combat only: the area
    public string Name { get; set; } = "";        // "Woodcutting: Oak Tree"
    public Until Until { get; set; }
    public double Amount { get; set; }
    public string Subject { get; set; } = "";     // item id (Until.Item), skill name (Until.Level) or mastery task id (Until.Mastery)
    public string SubjectName { get; set; } = "";
    /// <summary>The lane (1..MaxLanes): with steps in more than one lane, every lane does its own steps in order, side by side.</summary>
    public int Lane { get; set; } = 1;
    /// <summary>A recipe's other way of making for this step (Smithing's Sacrificial Anvil): its id to switch it on
    /// when the step starts, "" to switch it off, null (older steps) to leave the game's switch as it is.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; set; }

    /// <summary>A second goal: the step is also over when this one is reached (null: only the goal above).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Goal? Or { get; set; }

    /// <summary>
    /// The third and later goals (0.15.0), only next to <see cref="Or"/>: the step is over when any one goal is reached.
    /// A version before 0.15.0 reads the step without them.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Goal>? More { get; set; }

    /// <summary>The most goals a step has (its own and three "or").</summary>
    public const int MostGoals = 4;

    /// <summary>The goals after the step's own, in order: <see cref="Or"/>, then <see cref="More"/>.</summary>
    [JsonIgnore] public IEnumerable<Goal> Others => Or == null ? Enumerable.Empty<Goal>() : More == null ? new[] { Or } : More.Prepend(Or);

    /// <summary>True when the step has more than its own goal.</summary>
    [JsonIgnore] public bool HasOthers => Or != null;

    /// <summary>
    /// The goals after the step's own in their one shape: the first in Or, the rest in More (null when none), no empty
    /// or "stops by itself" goal, no more than fit.
    /// </summary>
    public void Normalize() => SetOthers((More == null ? new[] { Or } : More.Prepend(Or))!);

    /// <summary>Puts the goals after the step's own in place (the first in Or, the rest in More), no more than fit.</summary>
    public void SetOthers(IEnumerable<Goal> goals)
    {
        var list = goals.Where(g => g != null && g.Until != Until.Stops).Take(MostGoals - 1).ToList();
        Or = list.Count > 0 ? list[0] : null;
        More = list.Count > 1 ? list.Skip(1).ToList() : null;
    }

    /// <summary>A "sell" step is not an action: TargetId is the item, Amount how many to keep in the bank (0: sell all).</summary>
    public const string SellEntity = "ActionQueue:Sell";
    [JsonIgnore] public bool IsSell => Entity == SellEntity;
    /// <summary>An "equip" step: TargetId is an item from the bank (Amount: how many of a stack to keep in the bank), or
    /// <see cref="LoadoutPrefix"/> + a loadout id to switch to that loadout.</summary>
    public const string EquipEntity = "ActionQueue:Equip";
    public const string LoadoutPrefix = "loadout:";
    [JsonIgnore] public bool IsEquip => Entity == EquipEntity;
    /// <summary>A "go to a saved queue" step: TargetId is the saved queue's name. The queue goes on with that one.</summary>
    public const string SwitchEntity = "ActionQueue:Switch";
    [JsonIgnore] public bool IsSwitch => Entity == SwitchEntity;
    /// <summary>
    /// An Equip step that chooses the food one skill eats (0.13.0, by madmousepad): TargetId is the food, Subject
    /// <see cref="FoodSubject"/>, Extra the skill whose food slot it goes in (Combat, Thieving, Agility...), SubjectName that skill's name.
    /// </summary>
    public const string FoodSubject = "food";
    [JsonIgnore] public bool IsFood => IsEquip && Subject == FoodSubject && !TargetId.StartsWith(LoadoutPrefix, StringComparison.Ordinal);
    /// <summary>A step that is not an action: it takes no place, is over at once, and waits for the steps before it.</summary>
    [JsonIgnore] public bool IsInstant => IsSell || IsEquip || IsSwitch;

    /// <summary>What happened to this step in the current run, in words (not saved).</summary>
    public string State = "";
    /// <summary>What <see cref="State"/> means (0.14.0): the queue decides by this, never by the words.</summary>
    internal StepStatus Status;
    /// <summary>A running step's progress alone ("1,234 / 5,000 Oak Logs"), for the one-line headline (not saved).</summary>
    internal string Detail = "";
    /// <summary>How this step last ended ("done: out of runes"), still shown while it waits for its turn in the next round (not saved).</summary>
    internal string LastResult = "";
    /// <summary>How long the step ran the last time it ended (its Seconds then), shown while it waits for its turn in the next round (not saved).</summary>
    internal double LastSeconds;
    /// <summary>Where the step stands in the current run (not saved).</summary>
    public RunState Run;
    internal double Seconds, RetryAt, GrewAt;
    /// <summary>The time the step had run when the queue was paused: it carries on from there, like "make N more" (0.13.1).</summary>
    internal double SecondsBefore;
    internal long Pile, GainedAtStart, DoneAtStart, SpentAtStart, SeenAtStart;
    /// <summary>"Make N more" / "N times" / "use up N": what was counted before a pause, so carrying on does not start from zero.</summary>
    internal long CountBefore;
    internal int OwnedAtStart;
    internal bool Ran, CountGains;
    internal string Following = "";   // a slayer step: the task monster it is fighting at the moment

    long IGoal.CountBefore { get => CountBefore; set => CountBefore = value; }
    long IGoal.GainedAtStart { get => GainedAtStart; set => GainedAtStart = value; }
    long IGoal.DoneAtStart { get => DoneAtStart; set => DoneAtStart = value; }
    long IGoal.SpentAtStart { get => SpentAtStart; set => SpentAtStart = value; }
    long IGoal.SeenAtStart { get => SeenAtStart; set => SeenAtStart = value; }
    int IGoal.OwnedAtStart { get => OwnedAtStart; set => OwnedAtStart = value; }

    [JsonIgnore] public string Goal => IsSell ? (Amount <= 0 ? Lang.T("goal.sell_all", "sell all of them") : Lang.F("goal.sell_all_but", "sell all but {amount:N0}", ("amount", Amount)))
        : IsSwitch ? GoOnText()
        : IsFood ? Lang.F("goal.as_food_for", "as the food for {skill}", ("skill", SubjectName != "" ? SubjectName : Extra))
        : IsEquip ?(TargetId.StartsWith(LoadoutPrefix, StringComparison.Ordinal) ? Lang.T("goal.switch_loadout", "switch to this loadout") : Subject != "stack" ? Lang.T("goal.put_it_on", "put it on")
                     : Amount <= 0 ? Lang.T("goal.put_all_on", "put all of them on") : Lang.F("goal.put_on_keep", "put them on, keep {amount:N0} in the bank", ("amount", Amount)))
        : Others.Aggregate(GoalText(Until, Amount, SubjectName, Subject), (text, g) => Lang.F("goal.or", "{goal}, or {second}", ("goal", text), ("second", g.Text)));

    /// <summary>A "go to a saved queue" step: and when that queue stops (its own stop, saved with it).</summary>
    string GoOnText()
    {
        Preset? preset;
        try { preset = Queue.GetPreset(TargetId); }
        catch (Exception) { preset = null; }   // (the saved queues changed on another thread meanwhile: say it plainly)
        if (preset?.StopWhen is not { } stop) return Lang.T("goal.go_on_with_it", "and go on with it");
        return preset.StopAtEnd ? Lang.F("goal.go_on_stops_round", "and go on with it (it stops at the end of a round once {stop})", ("stop", Queue.StopText(stop)))
            : Lang.F("goal.go_on_stops", "and go on with it (it stops when {stop})", ("stop", Queue.StopText(stop)));
    }

    public static string GoalText(Until until, double amount, string subjectName, string subject = "") => until switch
    {
        Until.Item => Lang.F("goal.item", "until {amount:N0} {item} in the bank", ("amount", amount), ("item", subjectName)),
        Until.Made => Lang.F("goal.made", "until {amount:N0} more {item} are made", ("amount", amount), ("item", subjectName)),
        Until.ItemLeft => amount <= 0 ? Lang.F("goal.item_left_none", "until no {item} is left in the bank", ("item", subjectName))
            : Lang.F("goal.item_left", "until only {amount:N0} {item} are left in the bank", ("amount", amount), ("item", subjectName)),
        Until.Level => Lang.F("goal.level", "until {skill} level {amount:0}", ("skill", subjectName), ("amount", amount)),
        Until.Minutes => Lang.P("goal.minutes", amount, "for {count:0.#} minute", "for {count:0.#} minutes"),
        Until.Times => Lang.P("goal.times", amount, "once", "{count:N0} times"),
        Until.Spent => Lang.F("goal.spent", "until {amount:N0} {item} are used up", ("amount", amount), ("item", subjectName)),
        Until.Nodes => Lang.P("goal.nodes", amount, "until {count:N0} {kind} node unlock is earned", "until {count:N0} {kind} node unlocks are earned", ("kind", NodeKind(subject, subjectName))),
        Until.Event => Lang.P("goal.event", amount, "until {event} is seen", "until {event} is seen {count:N0} times", ("event", subjectName)),
        Until.Mastery => Lang.F("goal.mastery", "until the mastery task \"{task}\" is complete", ("task", Mastery.NameOf(subject, subjectName))),
        Until.FoodRunsOut => Lang.T("goal.food_runs_out", "until the food runs out"),
        _ => Lang.T("goal.stops_by_itself", "until it stops by itself"),
    };

    /// <summary>"minor" / "major" node unlocks in the language in use (the step keeps the English word as its value).</summary>
    public static string NodeKind(string subject, string subjectName) => subject switch
    {
        "major" => Lang.T("goal.node_kind.major", "major"),
        "minor" => Lang.T("goal.node_kind.minor", "minor"),
        _ => subjectName,
    };
}

/// <summary>A queue kept under a name, to switch back to later.</summary>
public sealed class Preset
{
    public List<Step> Steps { get; set; } = new();
    public bool Repeat { get; set; }
    /// <summary>0.11.0+: with "start over" on, the lanes that run once.</summary>
    public List<int> OnceLanes { get; set; } = new();
    /// <summary>0.12.0+: "stop the whole queue when ..." (null: none).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Goal? StopWhen { get; set; }
    /// <summary>0.13.0+: the stop is looked at only when a round is over (false: at once).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool StopAtEnd { get; set; }
}

/// <summary>
/// Runs the steps while the game is open. A step ends when its goal is reached or when the action stops by
/// itself (materials used up, bank full, death). If the player starts something else by hand, the queue pauses
/// instead of fighting them. The steps are kept per character in queue.json.
///
/// Normally the steps run one after another. When the Multitask mod has made room for several actions at once,
/// as many steps as fit run side by side: the fish are cooked while more are caught. A step that needs what an
/// earlier step makes waits for it instead of being skipped, and "until I own N" counts what was gathered,
/// not what happens to lie in the bank after another step has used some of it.
/// </summary>
public static class Queue
{
    sealed class Saved
    {
        public List<Step> Steps { get; set; } = new();
        public bool Repeat { get; set; }
        /// <summary>0.10.2+: with "start over" on, the lanes that run once and do not start over.</summary>
        public List<int> OnceLanes { get; set; } = new();
        /// <summary>0.12.0+: "stop the whole queue when ..." (null: none).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Goal? StopWhen { get; set; }
        /// <summary>0.13.0+: the stop is looked at only when a round is over.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool StopAtEnd { get; set; }
        /// <summary>0.13.0+: the saved queue to go on with once this one has ended ("" or missing: none).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Then { get; set; }
        /// <summary>The saved queue the steps above came from ("" when they were never saved).</summary>
        public string Name { get; set; } = "";
        public Dictionary<string, Preset> Presets { get; set; } = new();
        public bool SideBySide { get; set; } = true;
        /// <summary>What to do while the queue waits and once it is done (null: nothing).</summary>
        public Step? Idle { get; set; }
        /// <summary>The idle action also runs while the queue is not running (paused, finished, never used).</summary>
        public bool IdleAlways { get; set; }
        /// <summary>The game's own Create button (on the recipe pages) adds the recipe to the queue.</summary>
        public bool GameCreate { get; set; }
        /// <summary>Where the queue stood when the game last saved (null: nothing to carry on).</summary>
        public Snapshot? Run { get; set; }
    }

    /// <summary>
    /// Where a run stood when the game saved itself. Written only then, so it always matches the save the game
    /// loads next time; the queue carries on from it when the character is opened again.
    /// </summary>
    sealed class Snapshot
    {
        public string SaveId { get; set; } = "";
        /// <summary>The step list it belongs to: edited since, it is not used.</summary>
        public string Steps { get; set; } = "";
        public bool Running { get; set; }
        public bool Resume { get; set; }
        public bool RanThisRound { get; set; }
        public bool IdleOwned { get; set; }
        /// <summary>How many actions Multitask let run at once when the game saved (it has not said so yet while the
        /// game computes the closed time right after a restart).</summary>
        public int Capacity { get; set; }
        /// <summary>How many of those places Multitask kept locked for the player's own actions (0.11.0+).</summary>
        public int Locked { get; set; }
        public List<StepSnapshot> Progress { get; set; } = new();
    }

    sealed class StepSnapshot
    {
        public RunState Run { get; set; }
        public string State { get; set; } = "";
        /// <summary>What State means (0.14.0; None in a snapshot from before, whose State is English words).</summary>
        public StepStatus Status { get; set; }
        public bool Ran { get; set; }
        public double Seconds { get; set; }
        /// <summary>"Make N more" / "N times" / "use up N": what was counted so far.</summary>
        public long Count { get; set; }
        /// <summary>"Until I own N" side by side: what it counts from (gains so far included).</summary>
        public long OwnedBase { get; set; }
        public bool CountGains { get; set; }
        public string Following { get; set; } = "";
        /// <summary>The same two numbers for the step's second goal ("or until ...").</summary>
        public long OrCount { get; set; }
        public long OrOwnedBase { get; set; }
        /// <summary>The same for the third and later goals, in their order (0.15.0; null without them).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<long>? MoreCounts { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<long>? MoreOwnedBase { get; set; }
    }

    /// <summary>Where the queue that was on screen goes when another one is loaded over unsaved changes.</summary>
    public const string AutoName = "Unsaved queue (auto)";

    /// <summary>
    /// A saved queue's name as the window shows it: the queue that keeps unsaved steps is saved under an English name
    /// (it is a key in queue.json), shown in the language in use.
    /// </summary>
    public static string Shown(string queueName) =>
        string.Equals(queueName, AutoName, StringComparison.OrdinalIgnoreCase) ? Lang.T("queue.auto_name", "Unsaved queue (auto)") : queueName;

    /// <summary>
    /// A step's name as the window shows it. Sell, equip and "go to" steps keep an English word before the item's name
    /// in saves and share codes ("Sell: Oak Logs"): that word is shown in the language in use.
    /// </summary>
    public static string Shown(Step step) => ShownName(step.Name);

    /// <summary>A target's or step's saved English words ("Sell: ", "Dungeon: ", "Slayer tasks of ...") in the language in use.</summary>
    public static string ShownName(string name)
    {
        if (Lang.IsEnglish) return name;
        // a fight step's name starts with the skill ("Combat: Dungeon: Chicken Coop")
        int colon = name.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && Shown1(name[(colon + 2)..]) is { } rest && !name.StartsWith("Sell: ", StringComparison.Ordinal)
            && !name.StartsWith("Equip: ", StringComparison.Ordinal) && !name.StartsWith("Loadout: ", StringComparison.Ordinal))
            return name[..(colon + 2)] + rest;
        return Shown1(name) ?? name;
    }

    static string? Shown1(string name)
    {
        string? After(string english) => name.StartsWith(english, StringComparison.Ordinal) ? name[english.Length..] : null;
        if (After("Sell: ") is { } sell) return Lang.F("step.name.sell", "Sell: {item}", ("item", sell));
        if (After("Equip: ") is { } equip) return Lang.F("step.name.equip", "Equip: {item}", ("item", equip));
        if (After("Loadout: ") is { } loadout) return Lang.F("step.name.loadout", "Loadout: {loadout}", ("loadout", loadout));
        if (After("Go to saved queue: ") is { } go) return Lang.F("step.name.go_to", "Go to saved queue: {queue}", ("queue", go));
        if (After("Dungeon: ") is { } dungeon) return Lang.F("step.name.dungeon", "Dungeon: {dungeon}", ("dungeon", dungeon));
        var slayer = System.Text.RegularExpressions.Regex.Match(name, @"^Slayer tasks of (.+)  \(follows the task\)$");
        if (slayer.Success) return Lang.F("step.name.slayer", "Slayer tasks of {master}  (follows the task)", ("master", slayer.Groups[1].Value));
        return null;
    }

    public static string Dir = "";
    public static Action<string> Log { get; set; } = _ => { };
    static string FilePath => Path.Combine(Dir, "queue.json");

    static Game? s_game;
    static string s_character = "";
    static string s_saveId = "";
    static Dictionary<string, Item> s_items = new();
    static readonly object s_itemsLock = new();

    /// <summary>
    /// The item with this id. Items added while the game runs (One Style's rare drops) are not in the copy taken when
    /// the character was opened: they are looked up in the game and kept. Also called on the game's own thread.
    /// </summary>
    static Item? ItemOf(Game game, string id)
    {
        lock (s_itemsLock) { if (s_items.TryGetValue(id, out Item? known)) return known; }
        Item? live = null;
        try { if (id != "" && !game.Data.GetDataRegistryOutput<Item>().TryGetObjectById(id, out live)) live = null; }
        catch (Exception) { live = null; }
        if (live != null) lock (s_itemsLock) s_items[id] = live;
        return live;
    }

    /// <summary>Why an item step cannot find its item: a rare drop that is gone was sold (or otherwise lost).</summary>
    static string NoItem(string id) => Rares.IsDropId(id) ? Lang.T("queue.skipped_rare_item_no_longer", "skipped: this rare item is no longer owned") : Lang.T("queue.skipped_item_not_game", "skipped: this item is not in the game");

    static bool s_ranThisRound;
    /// <summary>
    /// Real work this round: an action step started, or a sale sold something. An equip, a loadout or a food choice can be
    /// undone and done again for ever, so it does not count as "something was done" for saved queues that go round.
    /// </summary>
    static bool s_workedThisRound;
    static volatile bool s_died;   // set from the game's own thread too (a death while offline progress is computed)
    /// <summary>True after a pause: the next Start carries on (finished steps stay finished, wherever they are now).</summary>
    static bool s_resume;
    static double s_clock;
    /// <summary>Everything the bank has received, per item, since the character was opened (other steps may use it up again).</summary>
    static readonly Dictionary<string, long> s_gained = new();
    /// <summary>What each skill has used up, per "skill|item", since the character was opened (for "use up N"): only
    /// what the skill's own actions took, the way the game's own "Ingredients Spent" counts it, not what was sold.</summary>
    static readonly Dictionary<string, long> s_spent = new();
    /// <summary>How many times each skill has finished one action since the character was opened (for "N times").</summary>
    static readonly Dictionary<string, long> s_done = new();

    /// <summary>A waiting recipe starts again once there is enough for this many crafts, or the materials stopped growing for a while.</summary>
    const int PileSize = 10;
    const double SettleSeconds = 30, RetrySeconds = 2;
    /// <summary>Seconds the "always" idle action waits after a character is opened or offline progress was computed.</summary>
    const double SettleAfterLoad = 5;

    /// <summary>Run steps side by side when the Multitask mod has room for it (the player can switch this off).</summary>
    public static bool SideBySide { get; private set; } = true;
    /// <summary>How many steps may run at once right now.</summary>
    public static int Lanes => SideBySide || LaneMode ? Math.Max(1, Capacity() - Locked()) : 1;

    /// <summary>
    /// Places Multitask keeps locked for the player's own actions (they count in <see cref="Capacity"/> and among the
    /// player's kept actions; the queue's lanes use only the rest). While the game computes the closed time Multitask
    /// has not said so yet: then what it said when the game last saved.
    /// </summary>
    static int Locked() => Math.Min(Capacity() - 1, Targets.OnMainThread ? Targets.MultitaskLocked() : Math.Max(Targets.MultitaskLocked(), s_savedLocked));

    /// <summary>Multitask's locked places as they were when the game last saved.</summary>
    static int s_savedLocked;

    /// <summary>How many lanes a queue can have.</summary>
    public const int MaxLanes = Targets.MostActions;   // as many lanes as Multitask can run actions at once (was 5)
    static int LaneOf(Step step) => Math.Clamp(step.Lane, 1, MaxLanes);
    /// <summary>
    /// True when the steps are spread over more than one lane: then every lane does its own steps one after another,
    /// and the lanes run side by side. With every step in lane 1 the queue works as it always did.
    /// </summary>
    public static bool LaneMode => Steps.Any(s => LaneOf(s) > 1);
    /// <summary>The step a lane is at: its first one that is not over.</summary>
    static Step? Head(int lane) => Steps.FirstOrDefault(s => LaneOf(s) == lane && s.Run != RunState.Done);
    /// <summary>Lanes that started something in this round (with "start over", a lane that did nothing does not go round again).</summary>
    static readonly HashSet<int> s_laneRan = new();

    public static List<Step> Steps { get; private set; } = new();
    public static bool Repeat { get; private set; }

    /// <summary>
    /// "Stop the whole queue when ...": once it is reached, every step stops, the queue ends (also with "start over" on,
    /// and in lanes) and the idle action takes over. Only goals that read the game: an item in the bank, a skill level,
    /// an item running low, a Mastery Guild task complete. Null: the queue has no stop of its own. Asked for on Nexus: farm until a drop, then stop.
    /// </summary>
    public static Goal? StopWhen { get; private set; }

    /// <summary>
    /// 0.13.0 (madmousepad's "stop step" idea): the queue's stop is looked at only when a round is over, so the round ends
    /// cleanly (the sale or the equip at the end still happens). In lanes, a lane that is through does not start over
    /// while the stop holds, and the queue ends once every lane is through. False: the queue stops at once.
    /// </summary>
    public static bool StopAtEnd { get; private set; }

    /// <summary>
    /// 0.13.0 (by madmousepad): the saved queue to go on with once this queue has ended, by finishing its steps or by
    /// its own stop ("" : none, the idle action takes over). A queue never goes on with itself.
    /// </summary>
    public static string Then { get; private set; } = "";

    /// <summary>The kinds of goal the queue's own stop can be.</summary>
    public static bool StopKind(Until until) => until is Until.Item or Until.Level or Until.ItemLeft or Until.Mastery;

    /// <summary>The highest level the queue's stop can wait for (the window's number box goes up to it too).</summary>
    public const int MaxStopLevel = 200;
    /// <summary>The highest item count the queue's stop can wait for.</summary>
    public const int MaxStopAmount = 1_000_000_000;

    /// <summary>A stop read from a file, a saved queue or a share code: kept only when it makes sense.</summary>
    public static Goal? CleanStop(Goal? goal) =>
        goal is { Until: Until.Mastery } task && !string.IsNullOrWhiteSpace(task.Subject)
            ? new Goal { Until = Until.Mastery, Amount = 1, Subject = task.Subject, SubjectName = task.SubjectName ?? "" }
        : goal != null && StopKind(goal.Until) && !string.IsNullOrWhiteSpace(goal.Subject) && !double.IsNaN(goal.Amount) && goal.Amount >= 0
            && goal.Amount <= (goal.Until == Until.Level ? MaxStopLevel : MaxStopAmount) && (goal.Until == Until.ItemLeft || goal.Amount >= 1)
            ? new Goal { Until = goal.Until, Amount = goal.Amount, Subject = goal.Subject, SubjectName = goal.SubjectName ?? "" } : null;

    /// <summary>
    /// Offline the stop was reached while a fight step was at it, and fights wait for the game (the fight's music could
    /// not be held, see Offline): the other steps stopped, nothing new starts, and the queue ends (fight and all) as
    /// soon as the game is open.
    /// </summary>
    static bool s_stopPending;

    /// <summary>What the queue's stop waits for, in words ("you have 1 Bandit Gloves in the bank").</summary>
    public static string StopText(Goal g) => g.Until switch
    {
        Until.Item => Lang.F("queue.you_have_bank", "you have {amount:N0} {subjectName} in the bank", ("amount", g.Amount), ("subjectName", g.SubjectName)),
        Until.Level => Lang.F("queue.reaches_level", "{subjectName} reaches level {amount:0}", ("subjectName", g.SubjectName), ("amount", g.Amount)),
        Until.ItemLeft => g.Amount <= 0 ? Lang.F("queue.no_left_bank", "no {subjectName} is left in the bank", ("subjectName", g.SubjectName)) : Lang.F("queue.only_left_bank", "only {amount:N0} {subjectName} are left in the bank", ("amount", g.Amount), ("subjectName", g.SubjectName)),
        Until.Mastery => Lang.F("queue.mastery_task_complete", "the mastery task \"{task}\" is complete", ("task", Mastery.NameOf(g.Subject, g.SubjectName))),
        _ => g.Text,
    };

    /// <summary>Sets the queue's own stop (null: none).</summary>
    public static void SetStopWhen(Goal? goal)
    {
        Goal? clean = CleanStop(goal);
        if (clean == null ? StopWhen == null : clean.SameAs(StopWhen)) return;
        StopWhen = clean;
        Trace(StopWhen == null ? "the queue has no stop of its own" : $"the whole queue stops when {StopText(StopWhen)}");
        Save();
        RaiseChanged();
    }

    /// <summary>The queue's stop: at once (false) or when a round is over (true).</summary>
    public static void SetStopAtEnd(bool atEnd)
    {
        if (StopAtEnd == atEnd) return;
        StopAtEnd = atEnd;
        Trace(atEnd ? "the queue's stop is looked at when a round is over" : "the queue's stop ends the queue at once");
        Save();
        RaiseChanged();
    }

    /// <summary>The saved queue to go on with once this one has ended ("" : none).</summary>
    public static void SetThen(string? name)
    {
        name = Share.CleanName(name);
        if (string.Equals(name, AutoName, StringComparison.OrdinalIgnoreCase)) name = "";
        if (name == Then) return;
        Then = name;
        Trace(Then == "" ? "when the queue ends: the idle action takes over" : $"when the queue ends: it goes on with the saved queue '{Then}'");
        Save();
        RaiseChanged();
    }

    /// <summary>True when the queue's own stop is reached right now (false when it has none).</summary>
    static bool StopReached(Game game) => StopWhen is { } g && g.Until switch
    {
        Until.Item => Owned(game, g) >= g.Amount,
        Until.ItemLeft => Owned(game, g) <= g.Amount,
        Until.Level => Level(game, g) >= g.Amount,
        Until.Mastery => Mastery.Find(g.Subject) is { Done: true },
        _ => false,
    };

    /// <summary>True when the queue's own stop is reached right now (the window says so when the player sets one).</summary>
    public static bool StopReachedNow => s_game != null && StopReached(s_game);

    /// <summary>
    /// Looks at the queue's own stop. True when it was reached and the queue is ending: nothing more may start. A stop
    /// looked at when a round is over (<see cref="StopAtEnd"/>) is not looked at here, but where a round ends (see Tick).
    /// </summary>
    static bool StopCheck(Game game)
    {
        if (!s_stopPending && (StopAtEnd || !StopReached(game))) return false;
        s_switchTo = null;   // a "go to a saved queue" step that just came up does not replace a queue that has stopped
        StopForGood(game);
        Publish();   // (it may have gone on with another saved queue: Multitask hears of its steps at once)
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// The queue's own stop was reached: every step's action stops, the queue ends, and the idle action takes over.
    /// Offline with fights waiting (see OfflineTick) a fight is not ended: then the other steps stop, nothing new
    /// starts, and the queue ends with the first look once the game is open.
    /// </summary>
    static void StopForGood(Game game)
    {
        bool fightLeft = false;
        foreach (Step s in Steps)
        {
            if (s.Run == RunState.Running && Targets.Find(game, s.Entity) is { } action && StepDoing(action, s))
            {
                if (HandsOff(action)) { fightLeft = true; Say(s, StepStatus.Stopped, Lang.T("queue.queue_stop_was_reached_fight", "the queue's stop was reached: the fight ends when the game is open")); continue; }
                StopAction(action);
            }
            if (s.Run is RunState.Running or RunState.Waiting) Say(s, StepStatus.Stopped, Lang.T("queue.stopped_queue_stop_was_reached", "stopped: the queue's stop was reached"));
            s.Run = RunState.Done;
        }
        if (fightLeft)
        {
            if (!s_stopPending) Trace("offline: the queue's stop was reached; the fight is ended when the game is open");
            s_stopPending = true;
            return;
        }
        s_stopPending = false;
        string stop = StopWhen is { } g ? StopText(g) : Lang.T("queue.stop_was_reached", "its stop was reached");
        string why = StopAtEnd ? Lang.F("queue.stopped_at_round_end", "The queue stopped at the end of the round: {stop} (the queue's stop).", ("stop", stop))
            : Lang.F("queue.stopped_by_its_stop", "The queue stopped: {stop} (the queue's stop).", ("stop", stop));
        EndRun(game, why);
    }

    /// <summary>
    /// The queue has really ended: its steps are done, or its own stop was reached. With a saved queue chosen under "When
    /// the queue ends, go on with" (and it is not this very queue), the queue goes on with that one from its first step;
    /// otherwise it finishes and the idle action takes over (as the player's own from then on).
    /// </summary>
    static void EndRun(Game game, string why, string? whyGoingOn = null)
    {
        string then = Then;
        string missing = "";
        if (then != "" && !string.Equals(then, Name, StringComparison.OrdinalIgnoreCase))
        {
            if (GetPreset(then) is { Steps.Count: > 0 })
            {
                Trace($"ended: {why} It goes on with the saved queue '{then}'.");
                SwitchNow(then);
                if (Running && string.Equals(Name, then, StringComparison.OrdinalIgnoreCase))
                {
                    // ("so it stopped" would read oddly right before "going on": whyGoingOn says it without that)
                    Note = Lang.F("queue.going_with_saved_queue", "{why} Going on with the saved queue '{name}'.", ("why", whyGoingOn ?? why), ("name", Shown(Name)));
                    return;
                }
                if (!Running) { RaiseChanged(); return; }   // (SwitchNow ended it: queues that only go to each other)
            }
            else missing = Lang.F("queue.saved_queue_go_with_no", " The saved queue '{then}' to go on with no longer exists (or is empty).", ("then", then));
        }
        s_idleOwned = false;   // a real finish: the idle action keeps going, as the player's own from now on
        Finish(why + missing);
        try { IdleAfterEnd(game); }
        catch (Exception e) { Log("The idle action could not be started: " + e.Message); }
    }

    /// <summary>True while the game does what this running step started (a slayer step: also the monster it is still on).</summary>
    static bool StepDoing(ActionComponent action, Step s) =>
        Targets.IsDoing(action, new Target(s.TargetId, s.Name, s.Extra))
        || s.Extra == Targets.Slayer && s.Following != "" && Targets.Current(action)?.Id == s.Following;
    /// <summary>With "start over" on and steps in lanes: the lanes that run once (the others start over by themselves).</summary>
    static HashSet<int> s_onceLanes = new();
    /// <summary>True when this lane starts over when it is through ("start over" on, and the lane not set to run once).</summary>
    public static bool LaneRepeats(int lane) => Repeat && (!LaneMode || !s_onceLanes.Contains(lane));
    /// <summary>One lane's own "start over" (only counts while "start over at the end" is on).</summary>
    public static void SetLaneRepeat(int lane, bool repeat)
    {
        if (repeat) s_onceLanes.Remove(lane); else s_onceLanes.Add(lane);
        Save();
        RaiseChanged();
    }
    static Dictionary<string, Preset> s_presets = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The saved queue the current steps came from, or "" when they are not saved.</summary>
    public static string Name { get; private set; } = "";
    public static bool Running { get; private set; }
    /// <summary>Paused: Start carries on with what was done (the window keeps showing last round's results then).</summary>
    public static bool Paused => !Running && s_resume;
    public static int Index { get; private set; }
    /// <summary>The last thing worth telling the player (why it paused, that it finished...).</summary>
    public static string Note { get; private set; } = "";
    /// <summary>Another language was chosen: the last message, written in the old one, is not shown any more.</summary>
    internal static void ForgetNote() { Note = ""; RaiseChanged(); }
    public static event Action? Changed;

    const string CombatId = "MelvorBase:Combat";

    /// <summary>
    /// "Something changed" for the window. On the game's own thread (offline progress) the window must not be
    /// touched: it is told later, on the main thread.
    /// </summary>
    static volatile bool s_changedLater;
    /// <summary>The steps changed while the game computed offline progress: written to the file on the main thread.</summary>
    static volatile bool s_saveLater;
    static void RaiseChanged()
    {
        if (!Targets.OnMainThread) { s_changedLater = true; return; }
        s_changedLater = false;
        Changed?.Invoke();
    }

    /// <summary>For tests and the window: offline progress the queue took part in (game seconds, steps started).</summary>
    public static double OfflineSeconds { get; private set; }
    public static int OfflineStarts { get; private set; }
    public static string OfflineLog { get; private set; } = "";

    /// <summary>
    /// The queue's turn while the game computes offline progress: called on the game's own thread, once a game
    /// second, between two of its ticks (so nothing else touches the game at that moment).
    /// </summary>
    static bool s_toldFightOffline;

    /// <summary>How many actions ran at once at the last look (online), or the most seen (offline).</summary>
    static int s_running;

    /// <summary>
    /// How many actions may run at once. Multitask tells it on the scene root, but only from its first frame on: while
    /// the game computes the time a character was closed (right after opening it), that note is still the one from
    /// before. The game itself has then loaded what ran (a fight and the queue's mining: two), which is proof enough.
    /// </summary>
    static int Capacity() => Math.Max(Targets.MultitaskCapacity(), Targets.OnMainThread ? 1 : Math.Max(s_running, s_savedCapacity));

    /// <summary>Multitask's room as it was when the game last saved (see <see cref="Snapshot.Capacity"/>).</summary>
    static int s_savedCapacity;

    /// <summary>
    /// True for a fight while the game computes offline progress and the fight's music could not be held there (see
    /// <see cref="Offline.FightsAllowed"/>): the queue must neither start nor end it then. Since 0.16.0 the music is
    /// held offline, so this is only the fallback after a game update.
    /// </summary>
    static bool HandsOff(ActionComponent action) => action.Entity.Id == CombatId && FightsWait;

    /// <summary>Offline without the music held: fights wait for the game to be open.</summary>
    static bool FightsWait => !Targets.OnMainThread && !Offline.FightsAllowed;

    /// <summary>Offline, with a fight running and fights waiting: a step may only start next to it (with a free Multitask slot), never in its place.</summary>
    static bool FightKeptOffline(Game game) => FightsWait && game.ActionManager.ActiveActions.Any(a => a.Entity.Id == CombatId);

    public static void OfflineTick(Game game, double seconds)
    {
        if (!ReferenceEquals(game, s_game)) return;
        OfflineSeconds += seconds;
        if (!Running) return;
        // Since 0.16.0 fights are started and ended offline too (the fight's music is held, see Offline). Without that
        // hold (a game update), a fight going on is not touched offline - starting or ending one would have the game
        // switch its music on this thread - but the other steps go on (until 0.9.0 the whole queue stood still).
        if (FightsWait && game.ActionManager.ActiveActions.Any(a => a.Entity.Id == CombatId))
        {
            if (!s_toldFightOffline) Trace("offline: a fight is running; it is left alone, the other steps go on beside it");
            s_toldFightOffline = true;
        }
        string before = string.Join(",", Steps.Select(s => s.Run));
        Tick(game, seconds);
        string after = string.Join(",", Steps.Select(s => s.Run));
        if (before != after)
        {
            OfflineStarts++;
            OfflineLog += $"{OfflineSeconds / 3600:0.00}h: {after}; ";
            Trace($"offline, {OfflineSeconds / 3600:0.00} h in: {after}");
        }
    }

    /// <summary>
    /// The "while idle" action: done while every step the queue has left is waiting (for materials, for its skill
    /// to be free...) and after the queue has finished. Only Entity, TargetId, Extra and Name are used. It only
    /// ever takes a free slot: the player's own actions are never pushed out for it.
    /// </summary>
    public static Step? Idle { get; private set; }
    /// <summary>What the "while idle" action is up to, for the window ("" when there is nothing to say).</summary>
    public static string IdleState { get; private set; } = "";
    /// <summary>How the window colours <see cref="IdleState"/> (0.14.0).</summary>
    internal static IdleTone IdleTone { get; private set; }
    /// <summary>True while the idle action runs because the queue started it (so the queue may stop it again).</summary>
    static bool s_idleOwned, s_idleBlocked, s_idleRanOut;
    static double s_idleRetryAt, s_idleGrewAt;
    static long s_idlePile;

    /// <summary>The idle action also runs while the queue is not running: whenever the character does nothing at all.</summary>
    public static bool IdleAlways { get; private set; }
    /// <summary>The game's own Create button on the recipe pages adds the recipe to the queue (see <see cref="GameButtons"/>).</summary>
    public static bool GameCreate { get; private set; }
    /// <summary>Not before this moment (queue clock): just after a character is opened or offline progress ends, the game and Multitask settle first.</summary>
    static double s_idleNotBefore;
    /// <summary>What ran at the last look, to notice the player starting something by hand (that lifts a block).</summary>
    static HashSet<string> s_runningBefore = new();

    /// <summary>Skills whose action the player stopped with one of the game's own buttons, and when (Environment.TickCount64).</summary>
    static readonly Dictionary<string, long> s_handStops = new();

    // ---------------------------------------------------------------- loading and saving

    static Dictionary<string, Saved> Read()
    {
        try
        {
            var all = JsonSerializer.Deserialize<Dictionary<string, Saved>>(File.ReadAllText(FilePath)) ?? new();
            foreach (Saved? saved in all.Values)
            {
                if (saved == null) continue;   // (a broken entry of one character must not cost the others theirs)
                foreach (Step s in saved.Steps ?? new()) s?.Normalize();
                foreach (Preset preset in (saved.Presets ?? new()).Values) foreach (Step s in preset?.Steps ?? new()) s?.Normalize();
            }
            return all;
        }
        catch { return new(); }
    }

    public static void Bind(Game game)
    {
        if (ReferenceEquals(s_game, game)) return;
        s_game = game;
        s_character = game.CharacterName;
        lock (s_itemsLock) s_items = game.Data.GetDataRegistryOutput<Item>().GetAllObjects().ToDictionary(i => i.Id);
        Read().TryGetValue(s_character, out Saved? saved);
        Steps = saved?.Steps ?? new List<Step>();
        Repeat = saved?.Repeat ?? false;
        s_onceLanes = new HashSet<int>(saved?.OnceLanes ?? new List<int>());
        StopWhen = CleanStop(saved?.StopWhen);
        StopAtEnd = saved?.StopAtEnd ?? false;
        Then = Share.CleanName(saved?.Then);
        s_stopPending = false;
        s_emptyVisited.Clear();   // (another character: what the last one went round through does not count)
        s_switchTo = null;
        s_switchFrom = null;
        s_presets = new Dictionary<string, Preset>(saved?.Presets ?? new(), StringComparer.OrdinalIgnoreCase);
        foreach (Preset? preset in s_presets.Values) if (preset != null) preset.StopWhen = CleanStop(preset.StopWhen);
        Name = saved?.Name is { } savedName && s_presets.ContainsKey(savedName) ? savedName : "";
        SideBySide = saved?.SideBySide ?? true;
        Idle = saved?.Idle is { Entity: not "", TargetId: not "" } idle ? idle : null;
        IdleAlways = saved?.IdleAlways ?? false;
        GameCreate = saved?.GameCreate ?? false;
        IdleSays("");
        s_idleOwned = s_idleBlocked = s_idleRanOut = false;
        s_idleNotBefore = s_clock + SettleAfterLoad;
        s_runningBefore = new HashSet<string>();
        lock (s_handStops) s_handStops.Clear();
        s_died = false;
        lock (s_gained) { s_gained.Clear(); s_spent.Clear(); }
        if (Bank(game) is { } bank)
            bank.Inventory.ItemQuantityChanged += (_, e) =>
            {
                if (!ReferenceEquals(game, s_game) || e.QuantityChange <= 0) return;
                lock (s_gained) s_gained[e.Item.Id] = s_gained.GetValueOrDefault(e.Item.Id) + e.QuantityChange;
            };
        Running = false;
        s_resume = false;
        s_running = 0;
        Index = 0;
        Note = "";
        // A death must never turn into "walk straight back in": remember it, the runner pauses on it.
        if (game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? combat) && combat is CombatEntity fighter)
            fighter.Action.PlayerDied += (_, _) => { if (ReferenceEquals(game, s_game)) s_died = true; };
        CountActions(game);
        try { Mastery.Bind(game); }
        catch (Exception e) { Log("The Mastery Guild tasks could not be read: " + e.Message); }
        WatchStops(game);
        s_saveId = ActiveSaveId();
        Targets.PrimeMeta();
        try { Restore(game, saved?.Run); }
        catch (Exception e) { Log("The queue could not carry on from where it was: " + e.Message); }
        if (!s_savesWatched)
        {
            s_savesWatched = true;
            SaveManager.LocalSaved += (_, _) => { if (s_game is { IsSaveLoaded: true } && ActiveSaveId() == s_saveId) Save(withRun: true); };
        }
        RaiseChanged();
    }

    static bool s_savesWatched;

    /// <summary>For tests: what the queue picked up when the character was opened ("" when nothing).</summary>
    public static string Restored { get; private set; } = "";

    static string ActiveSaveId()
    {
        try { return typeof(SaveManager).GetProperty("ActiveSaveId", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)?.ToString() ?? ""; }
        catch { return ""; }
    }

    static string Signature(bool lanes = true) => string.Join("\n", Steps.Select(s => $"{s.Entity}|{s.TargetId}|{s.Extra}|{s.Until}|{s.Amount}|{s.Subject}"
        + (s.Or == null ? "" : $"|or {s.Or.Until}|{s.Or.Amount}|{s.Or.Subject}")
        + (s.More == null ? "" : string.Concat(s.More.Select(g => $"|or {g.Until}|{g.Amount}|{g.Subject}"))) + (lanes ? $"|{LaneOf(s)}" : "")));

    static Snapshot TakeSnapshot() => new()
    {
        SaveId = s_saveId,
        Steps = Signature(),
        Running = Running,
        Resume = s_resume,
        RanThisRound = s_ranThisRound,
        IdleOwned = s_idleOwned,
        Capacity = Targets.MultitaskCapacity(),
            Locked = Targets.MultitaskLocked(),
        Progress = Steps.Select(s => new StepSnapshot
        {
            Run = s.Run, State = s.State, Status = s.Status, Ran = s.Ran, Seconds = s.Seconds,
            // a paused step keeps what it had counted at the pause; a live one counts on until now
            Count = Running && s.Ran && s.Run is RunState.Running or RunState.Waiting ? Counter(s) : s.CountBefore,
            OwnedBase = s.OwnedAtStart + (s.CountGains && s.Ran ? Gained(s) - s.GainedAtStart : 0),
            CountGains = s.CountGains, Following = s.Following,
            OrCount = s.Or is IGoal o ? (Running && s.Ran && s.Run is RunState.Running or RunState.Waiting ? Counter(s, o) : o.CountBefore) : 0,
            OrOwnedBase = s.Or is IGoal og ? og.OwnedAtStart + (s.CountGains && s.Ran ? Gained(og) - og.GainedAtStart : 0) : 0,
            MoreCounts = s.More?.Select(m => (IGoal)m).Select(m => Running && s.Ran && s.Run is RunState.Running or RunState.Waiting ? Counter(s, m) : m.CountBefore).ToList(),
            MoreOwnedBase = s.More?.Select(m => (IGoal)m).Select(m => (long)m.OwnedAtStart + (s.CountGains && s.Ran ? Gained(m) - m.GainedAtStart : 0)).ToList(),
        }).ToList(),
    };

    /// <summary>
    /// The character was opened again: carry on where the queue stood when the game last saved itself. This runs
    /// before the game computes the time it was closed, so the steps go on through that time too.
    /// </summary>
    static void Restore(Game game, Snapshot? snap)
    {
        Restored = "";
        if (snap == null || !snap.Running && !snap.Resume) return;
        // Not this save, or the steps were edited after the game last saved: nothing to carry on (said in the diary,
        // so "it did not go on while closed" can be answered).
        string? why = s_saveId == "" ? "the game did not say which save is open"
            : snap.SaveId != s_saveId ? "the queue was last running in another save"
            : snap.Progress.Count != Steps.Count || snap.Steps != Signature() && (LaneMode || snap.Steps != Signature(lanes: false)) ? "the steps were changed after the game last saved"
            : null;
        if (why != null)
        {
            Trace($"opened again: does not carry on ({why}); the queue waits for Start");
            return;
        }
        for (int i = 0; i < Steps.Count; i++)
        {
            Step s = Steps[i];
            StepSnapshot p = snap.Progress[i];
            s.Run = p.Run;
            s.State = p.State;
            s.Status = p.Status != StepStatus.None ? p.Status : StatusOf(p.State);   // (before 0.14.0 only the English words were saved)
            s.Detail = "";
            s.Ran = p.Ran;
            s.Seconds = p.Seconds;
            s.SecondsBefore = s.Run != RunState.Done && (s.Status == StepStatus.Paused || !s.Ran) ? p.Seconds : 0;
            if (s.Run == RunState.Done) { s.LastResult = s.State; s.LastSeconds = s.Seconds; }
            s.CountBefore = p.Count;
            s.OwnedAtStart = (int)Math.Min(int.MaxValue, p.OwnedBase);
            if (s.Or is IGoal or)
            {
                or.CountBefore = p.OrCount;
                or.OwnedAtStart = (int)Math.Min(int.MaxValue, p.OrOwnedBase);
            }
            // (the signature matched, so the goals are the same ones in the same order)
            for (int m = 0; m < (s.More?.Count ?? 0); m++)
            {
                IGoal more = s.More![m];
                more.CountBefore = p.MoreCounts != null && m < p.MoreCounts.Count ? p.MoreCounts[m] : 0;
                more.OwnedAtStart = (int)Math.Min(int.MaxValue, p.MoreOwnedBase != null && m < p.MoreOwnedBase.Count ? p.MoreOwnedBase[m] : 0);
            }
            s.CountGains = p.CountGains;
            s.Following = p.Following;
            // the counters start from nothing in a new session: what came before is in CountBefore / OwnedAtStart
            foreach (IGoal g in Goals(s))
            {
                g.GainedAtStart = Gained(g);
                g.DoneAtStart = DoneBy(s.Entity);
                g.SpentAtStart = SpentOf(s, g);
                g.SeenAtStart = SeenOf(s, g);
            }
            s.RetryAt = 0;
            s.GrewAt = s_clock;
            s.Pile = 0;
            // the game loads its actions with the save; one that is not going any more is simply started again
            if (snap.Running && s.Run == RunState.Running
                && !(Targets.Find(game, s.Entity) is { } action && StepDoing(action, s)))
                s.Run = RunState.Idle;
        }
        Running = snap.Running;
        s_resume = snap.Resume;
        s_savedCapacity = snap.Capacity;
        s_savedLocked = snap.Locked;
        s_ranThisRound = snap.RanThisRound;
        s_workedThisRound = snap.RanThisRound;
        s_laneRan.Clear();
        foreach (Step s in Steps.Where(s => s.Ran)) s_laneRan.Add(LaneOf(s));
        s_idleOwned = snap.IdleOwned && Idle != null && Targets.Find(game, Idle.Entity) is { } idle && Targets.IsDoing(idle, IdleTarget(Idle));
        SetIndex();
        Restored = string.Join(",", Steps.Select(s => s.Run)) + (Running ? " running" : " paused");
        Trace($"opened again: carries on where it was at the last save ({Restored}): " + string.Join(" | ", Steps.Select(s => $"{s.Name} {s.Goal} [{s.Run}{(s.Run == RunState.Running ? " " + Progress(game, s) : "")}]")));
        Publish();
    }

    /// <param name="withRun">The game is saving itself: also write where the run stands (else what was written then stays).</param>
    static void Save(bool withRun = false)
    {
        if (s_character == "") return;
        // On the game's own thread (offline progress) the file is written later, on the main thread.
        if (!Targets.OnMainThread && !withRun) { s_saveLater = true; return; }
        s_saveLater = false;
        try
        {
            // Only this character's entry is replaced in what is on disk right now.
            Dictionary<string, Saved> all = Read();
            Snapshot? run = withRun ? TakeSnapshot() : all.TryGetValue(s_character, out Saved? old) ? old.Run : null;
            all[s_character] = new Saved { Steps = Steps, Repeat = Repeat, OnceLanes = s_onceLanes.OrderBy(l => l).ToList(), StopWhen = StopWhen, StopAtEnd = StopAtEnd,
                Then = Then == "" ? null : Then, Name = Name, Presets = new Dictionary<string, Preset>(s_presets), SideBySide = SideBySide, Idle = Idle, IdleAlways = IdleAlways, GameCreate = GameCreate, Run = run };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Log("The queue could not be saved: " + e.Message); }
    }

    // ---------------------------------------------------------------- editing

    public static void Add(Step step) { Steps.Add(step); Save(); RaiseChanged(); }

    public static void AddRange(IEnumerable<Step> steps) { Steps.AddRange(steps); Save(); RaiseChanged(); }

    /// <summary>
    /// Stops the action of a step that is running, if the character is still on it. Taking a running step out of
    /// the queue (or emptying the queue) must not leave its action going on: to the player that looks as if the
    /// queue were still at work.
    /// </summary>
    static void StopOwn(Step step)
    {
        if (s_game == null || !Running || step.Run != RunState.Running) return;
        ActionComponent? action = Targets.Find(s_game, step.Entity);
        // (with fights waiting, a fight is not ended while the game works out the time it was closed: see OfflineTick)
        if (action != null && StepDoing(action, step) && !HandsOff(action)) action.TryStop(force: true);
    }

    /// <summary>
    /// Tells Multitask which skills the queue is driving: its running steps, and the idle action while the queue
    /// runs it. Multitask leaves those alone (it never starts them again and never counts them as the player's).
    /// </summary>
    static void Publish()
    {
        // Not running: only an idle action the queue has started is its own (Multitask must not adopt it as the player's).
        if (!Running) { Targets.PublishDriving(s_idleOwned && Idle != null ? Idle.Entity : ""); return; }
        IEnumerable<string> driving = Steps.Where(s => s.Run == RunState.Running).Select(s => s.Entity);
        if (s_idleOwned && Idle != null) driving = driving.Append(Idle.Entity);
        Targets.PublishDriving(string.Join("|", driving.Distinct()));
    }

    static void SetIndex()
    {
        int first = Steps.FindIndex(s => s.Run != RunState.Done);
        Index = first < 0 ? 0 : first;
    }

    /// <summary>
    /// Puts a changed step in the place of another (the window's Edit). A step that runs is stopped and starts again with
    /// its new settings, from nothing counted; one that is already done this round stays done (it runs again next round).
    /// </summary>
    public static void ReplaceAt(int index, Step step)
    {
        if (index < 0 || index >= Steps.Count) return;
        Step old = Steps[index];
        StopOwn(old);
        step.Run = old.Run == RunState.Done ? RunState.Done : RunState.Idle;
        Steps[index] = step;
        if (Running) LineUpAgain();
        Save();
        RaiseChanged();
    }

    public static void RemoveAt(int index)
    {
        if (index < 0 || index >= Steps.Count) return;
        StopOwn(Steps[index]);
        Steps.RemoveAt(index);
        if (Steps.Count == 0) Finish(Lang.T("queue.queue_empty", "The queue is empty."));
        else if (Running) SetIndex();
        else if (index < Index) Index--;
        Save();
        RaiseChanged();
    }

    /// <summary>
    /// Moves a step, also while the queue runs. What a step has done stays with the step, not with its row: a
    /// running step goes on running wherever it is moved (nothing is stopped), a finished one stays finished, and
    /// the new order decides what starts next.
    /// </summary>
    public static bool Move(int index, int by) => MoveTo(index, by) >= 0;

    /// <summary>Like <see cref="Move"/>; returns the step's new place, or -1 when it could not move.</summary>
    public static int MoveTo(int index, int by)
    {
        int to = index + by;
        if (index >= 0 && index < Steps.Count && LaneMode)
        {
            // In lanes the order that counts is the one inside the lane: swap with the next step of the same lane.
            int lane = LaneOf(Steps[index]);
            while (to >= 0 && to < Steps.Count && LaneOf(Steps[to]) != lane) to += Math.Sign(by);
        }
        if (index < 0 || index >= Steps.Count || to < 0 || to >= Steps.Count) return -1;
        (Steps[index], Steps[to]) = (Steps[to], Steps[index]);
        if (Running)
        {
            LineUpAgain();
            Trace($"moved a step while running: {string.Join(" | ", Steps.Select(s => $"{s.Name} [{s.Run}]"))}");
        }
        Save();
        RaiseChanged();
        return to;
    }

    /// <summary>Puts a step into another lane (1..MaxLanes). A waiting step lines up again; a running one goes on.</summary>
    public static void SetLane(int index, int lane)
    {
        if (index < 0 || index >= Steps.Count) return;
        lane = Math.Clamp(lane, 1, MaxLanes);
        Step step = Steps[index];
        if (LaneOf(step) == lane) return;
        step.Lane = lane;
        if (Running && step.Run == RunState.Waiting) { step.Run = RunState.Idle; Say(step, StepStatus.None, ""); }
        if (step.Ran) s_laneRan.Add(lane);
        if (Running) LineUpAgain();
        Trace($"{Number(step)}: now in lane {lane}");
        Save();
        RaiseChanged();
    }

    /// <summary>
    /// After the order or the lanes changed while running. A waiting step holds a place: when a step that has not
    /// started yet now comes before it (in lanes: when it is no longer the step its lane is at), it lines up again
    /// (what it has counted is kept).
    /// </summary>
    static void LineUpAgain()
    {
        bool laneMode = LaneMode;
        for (int i = 0; i < Steps.Count; i++)
        {
            Step s = Steps[i];
            if (s.Run != RunState.Waiting) continue;
            bool behind = laneMode ? Head(LaneOf(s)) != s : Steps.Take(i).Any(o => o.Run == RunState.Idle);
            if (behind) { s.Run = RunState.Idle; Say(s, StepStatus.None, ""); }
        }
        SetIndex();
    }

    public static void Clear()
    {
        foreach (Step step in Steps) StopOwn(step);
        Steps.Clear();
        s_onceLanes.Clear();
        StopWhen = null;   // the queue's own stop goes with its steps
        StopAtEnd = false;
        s_stopPending = false;
        Name = "";
        Finish(Running ? Lang.T("queue.queue_was_emptied_so_what", "The queue was emptied, so what it had started was stopped.") : "");
        Save();
        RaiseChanged();
    }

    public static void SetRepeat(bool repeat) { Repeat = repeat; Save(); RaiseChanged(); }

    public static void SetSideBySide(bool on) { SideBySide = on; Save(); RaiseChanged(); }

    /// <summary>Chooses the "while idle" action (null: nothing). One the queue had started is stopped when it changes.</summary>
    public static void SetIdle(Step? idle)
    {
        if (idle != null && (idle.Entity == "" || idle.TargetId == "")) idle = null;
        bool same = idle != null && Idle != null && idle.Entity == Idle.Entity && idle.TargetId == Idle.TargetId && idle.Extra == Idle.Extra;
        if (!same)
        {
            StopIdle();
            Idle = idle == null ? null : new Step { Entity = idle.Entity, TargetId = idle.TargetId, Extra = idle.Extra, Name = idle.Name };
            IdleSays("");
            s_idleBlocked = s_idleRanOut = false;
            s_idleRetryAt = 0;
            Trace(Idle == null ? "while idle: nothing" : $"while idle: {Idle.Name}");
        }
        Save();
        Publish();
        RaiseChanged();
    }

    /// <summary>"Also when the queue is not running": the idle action fills every moment the character does nothing.</summary>
    public static void SetIdleAlways(bool on)
    {
        if (IdleAlways == on) return;
        IdleAlways = on;
        s_idleBlocked = false;
        s_idleRetryAt = 0;
        IdleSays("");
        if (!on && !Running) StopIdle();   // switched off: an idle action it started goes too
        Trace(on ? "idle action: also when the queue is not running" : "idle action: only while the queue runs");
        Save();
        Publish();
        RaiseChanged();
    }

    public static void SetGameCreate(bool on)
    {
        if (GameCreate == on) return;
        GameCreate = on;
        Trace(on ? "the game's Create button adds to the queue" : "the game's Create button works as usual");
        Save();
        RaiseChanged();
    }

    /// <summary>
    /// A recipe from the game's own page ("+ Queue", or Create with the setting on). It always goes to the end of the
    /// queue. A running queue goes on with what it does; a queue with steps left that is not running stays as it is
    /// (it is the player's to start). With nothing left to do (empty, or every step finished), the queue is started
    /// with the new step, so the recipe begins at once and the next one lines up behind it. Returns what to tell.
    /// </summary>
    public static string AddFromGame(Step step)
    {
        bool left = Steps.Any(s => s.Run != RunState.Done);
        bool wasRunning = Running;
        Steps.Add(step);
        Save();
        int number = Steps.Count;
        Trace($"added from the game's page: {Number(step)}");
        if (wasRunning) { RaiseChanged(); return Lang.F("queue.added_as_step_2", "added as step {number}: {name}, {goal}.", ("number", number), ("name", Shown(step)), ("goal", step.Goal)); }
        if (left)
        {
            RaiseChanged();
            return ActionQueueMod.Instance?.Key.Label is "" ? Lang.F("queue.added_not_running_menu", "added as step {number}: {name}, {goal}. The queue is not running: press Start in its window (in the Mods menu).", ("number", number), ("name", Shown(step)), ("goal", step.Goal))
                : Lang.F("queue.added_not_running_key", "added as step {number}: {name}, {goal}. The queue is not running: press Start in its window ({key}).", ("number", number), ("name", Shown(step)), ("goal", step.Goal), ("key", ActionQueueMod.Instance?.Key.Label ?? "F6"));
        }
        // Nothing else to do: run it now. The finished steps stay finished (they are not done again).
        s_resume = Steps.Count > 1;
        Start();
        if (!Running) return Lang.F("queue.added_as_step", "added as step {number}: {name}, {goal}. {note}", ("number", number), ("name", Shown(step)), ("goal", step.Goal), ("note", Note));
        return Lang.F("queue.started_step_add_more_they", "started {name}, {goal} (step {number}). Add more and they follow it.", ("name", Shown(step)), ("goal", step.Goal), ("number", number));
    }

    /// <summary>The game is starting up or has just computed offline progress: the idle action waits a few seconds.</summary>
    public static void HoldIdle() => s_idleNotBefore = s_clock + SettleAfterLoad;

    // ---------------------------------------------------------------- the game's own Stop buttons

    /// <summary>
    /// Remembers the actions the player stops with one of the game's own buttons ("Stop Cutting", "Stop", "Run"...).
    /// Told apart from the game stopping an action by itself (out of materials, bank full, a stop condition met:
    /// those happen inside a game tick) and from another action being started (the game stops the old one first).
    /// </summary>
    static void WatchStops(Game game)
    {
        foreach ((GameEntity entity, ActionComponent action) in Targets.Skills(game))
        {
            string id = entity.Id;
            ((IInventoryWalletComponent)action).Inventory.ItemAdded += (_, e) =>
            {
                if (!ReferenceEquals(game, s_game) || e.Quantity >= 0 || e.Item == null) return;
                lock (s_gained) s_spent[$"{id}|{e.Item.Id}"] = s_spent.GetValueOrDefault($"{id}|{e.Item.Id}") - e.Quantity;
            };
            action.Stopped += (_, _) =>
            {
                if (!ReferenceEquals(game, s_game) || game.Flags.IsOffline) return;
                if (StoppedToStartAnother()) { lock (s_bumped) s_bumped[id] = Environment.TickCount64; return; }
                if (!StoppedByHand()) return;
                lock (s_handStops) s_handStops[id] = Environment.TickCount64;
            };
        }
    }

    /// <summary>
    /// True when the stop comes from a button of the game's own screen: a "...Pressed..." handler of one of the game's
    /// controls is on the call stack, and neither a game tick nor "stop everything to start something else" is.
    /// (Only button handlers count: picking a trap the bank has none of also stops hunting from the screen, but that
    /// is the game giving up, not the player pressing Stop.)
    /// </summary>
    /// <summary>
    /// True when the game stops this action because another one is starting ("stop everything to start something
    /// else"), and it is not the queue's own start (0.14.0).
    /// </summary>
    static bool StoppedToStartAnother()
    {
        if (Targets.Starting) return false;
        foreach (StackFrame frame in new StackTrace(1, false).GetFrames())
        {
            MethodBase? method = frame.GetMethod();
            if (method?.DeclaringType == typeof(ActiveActionManager) && method.Name.StartsWith("TryStopActive", StringComparison.Ordinal)) return true;
            if (method?.DeclaringType == typeof(Game) && method.Name == nameof(Game.PropagateTick)) return false;
        }
        return false;
    }

    /// <summary>Skills the game stopped to start another action, and when (they are taken within five seconds).</summary>
    static readonly Dictionary<string, long> s_bumped = new();

    static bool TakeBumped(string entityId)
    {
        lock (s_bumped)
        {
            if (!s_bumped.Remove(entityId, out long at)) return false;
            return Targets.OnMainThread && Environment.TickCount64 - at < 5000;
        }
    }

    /// <summary>
    /// The room left beside the queue's running steps (this one counted): Multitask's slots less the steps and every
    /// other action, the ones Multitask keeps for the player and the ones running now (it may not have looked yet).
    /// </summary>
    static int RoomBeside(Game game)
    {
        var queue = Steps.Where(s => s.Run == RunState.Running).Select(s => s.Entity).ToHashSet();
        var others = new HashSet<string>(Targets.PlayerKept() ?? new HashSet<string>());
        foreach (ActionComponent a in game.ActionManager.ActiveActions) others.Add(a.Entity.Id);
        others.ExceptWith(queue);
        return Capacity() - queue.Count - others.Count;
    }

    static bool StoppedByHand()
    {
        bool screen = false;
        Assembly gameCode = typeof(Game).Assembly;
        foreach (StackFrame frame in new StackTrace(1, false).GetFrames())
        {
            MethodBase? method = frame.GetMethod();
            Type? type = method?.DeclaringType;
            if (type == null || type.Assembly != gameCode) continue;
            if (type == typeof(Game) && method!.Name == nameof(Game.PropagateTick)) return false;
            if (type == typeof(ActiveActionManager) && method!.Name.StartsWith("TryStopActive", StringComparison.Ordinal)) return false;
            // The game stopping it because something ran out or a requirement or stop condition changed (selling,
            // unequipping, buying...), or hunting giving up after a trap change: not the Stop button.
            if (method!.Name is nameof(ActionComponent.QueueOrTryStop) or "ActiveAnimalTraplessEnabledChangedHandler" or "ActiveAnimalSelectedTrapChangedHandler") return false;
            if (screen || !method!.Name.Contains("Pressed", StringComparison.Ordinal)) continue;
            for (Type? t = type; t != null && !screen; t = t.DeclaringType)
                if (typeof(Godot.Control).IsAssignableFrom(t)) screen = true;
        }
        return screen;
    }

    /// <summary>True (once) when the player stopped this skill's action by hand in the last few seconds.</summary>
    static bool TakeHandStop(string entityId)
    {
        lock (s_handStops)
        {
            if (!s_handStops.Remove(entityId, out long at)) return false;
            return Targets.OnMainThread && Environment.TickCount64 - at < 5000;   // (offline: the player is not there)
        }
    }

    // ---------------------------------------------------------------- saved queues

    public static List<string> PresetNames => s_presets.Keys.OrderBy(n => n == AutoName ? 1 : 0).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public static Preset? GetPreset(string? name) => name != null && s_presets.TryGetValue(name, out Preset? preset) ? preset : null;

    static Step Copy(Step s) => new()
    {
        Entity = s.Entity, TargetId = s.TargetId, Extra = s.Extra, Name = s.Name,
        Until = s.Until, Amount = s.Amount, Subject = s.Subject, SubjectName = s.SubjectName, Lane = s.Lane, Mode = s.Mode, Or = s.Or?.Copy(),
        More = s.More is { Count: > 0 } ? s.More.Select(g => g.Copy()).ToList() : null,
    };

    static bool Same(Step a, Step b) => a.Entity == b.Entity && a.TargetId == b.TargetId && a.Extra == b.Extra
        && a.Until == b.Until && a.Amount == b.Amount && a.Subject == b.Subject && LaneOf(a) == LaneOf(b) && a.Mode == b.Mode
        && (a.Or == null ? b.Or == null : a.Or.SameAs(b.Or))
        && (a.More?.Count ?? 0) == (b.More?.Count ?? 0) && Enumerable.Range(0, a.More?.Count ?? 0).All(i => a.More![i].SameAs(b.More![i]));

    /// <summary>True when the steps on screen differ from the saved queue they came from.</summary>
    public static bool Modified =>
        Name != "" && s_presets.TryGetValue(Name, out Preset? preset)
        && (preset.Repeat != Repeat || !(preset.OnceLanes ?? new List<int>()).OrderBy(l => l).SequenceEqual(s_onceLanes.OrderBy(l => l)) || preset.Steps.Count != Steps.Count || preset.Steps.Where((s, i) => !Same(s, Steps[i])).Any()
            || (preset.StopWhen == null ? StopWhen != null : !preset.StopWhen.SameAs(StopWhen)) || StopWhen != null && preset.StopAtEnd != StopAtEnd);

    /// <summary>The steps and settings on screen, kept under a name.</summary>
    static Preset Current() => new() { Steps = Steps.Select(Copy).ToList(), Repeat = Repeat, OnceLanes = s_onceLanes.OrderBy(l => l).ToList(), StopWhen = StopWhen?.Copy(), StopAtEnd = StopWhen != null && StopAtEnd };

    /// <summary>Keeps the current steps under a name (an existing queue of that name is replaced).</summary>
    public static bool SavePreset(string? name, out string message)
    {
        name = Share.CleanName(name);
        if (name == "") { message = Lang.T("queue.type_name_queue_first", "Type a name for the queue first."); return false; }
        if (Steps.Count == 0) { message = Lang.T("queue.there_no_steps_save_add", "There are no steps to save. Add some first."); return false; }
        bool replaced = s_presets.ContainsKey(name);
        s_presets[name] = Current();
        s_emptyVisited.Clear();   // (a queue saved again may do something now)
        Name = s_presets.Keys.First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        Save();
        message = replaced ? Lang.P("queue.saved_over", Steps.Count, "Saved over '{name}' ({count} step).", "Saved over '{name}' ({count} steps).", ("name", Shown(Name)))
            : Lang.P("queue.saved_as", Steps.Count, "Saved as '{name}' ({count} step).", "Saved as '{name}' ({count} steps).", ("name", Shown(Name)));
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// Puts a saved queue in place of the current one. A running queue keeps running, from the first step of the
    /// new one. Steps that were never saved (or were changed since) are kept under <see cref="AutoName"/>.
    /// </summary>
    public static bool LoadPreset(string name, out string message) => LoadPresetCore(name, out message, byQueue: false);

    /// <param name="byQueue">The queue itself goes on with this saved queue ("go to a saved queue", "go on with"), not the player.</param>
    static bool LoadPresetCore(string name, out string message, bool byQueue)
    {
        if (!s_presets.TryGetValue(name, out Preset? preset)) { message = Lang.T("queue.saved_queue_no_longer_exists", "That saved queue no longer exists."); return false; }
        bool wasRunning = Running;
        bool kept = false;
        if (Steps.Count > 0 && (Name == "" || Modified) && !string.Equals(name, AutoName, StringComparison.OrdinalIgnoreCase))
        {
            s_presets[AutoName] = Current();
            kept = true;
        }
        foreach (Step step in Steps) StopOwn(step);   // a clean switch: the old queue's actions do not linger
        if (!byQueue) s_emptyVisited.Clear();   // loaded by the player: a new run as far as queues going round for nothing are concerned
        Steps = preset.Steps.Select(Copy).ToList();
        s_laneRan.Clear();
        Repeat = preset.Repeat;
        s_onceLanes = new HashSet<int>(preset.OnceLanes ?? new List<int>());
        StopWhen = CleanStop(preset.StopWhen);   // a saved queue brings its own stop (or none)
        StopAtEnd = StopWhen != null && preset.StopAtEnd;
        s_stopPending = false;
        Name = s_presets.Keys.First(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
        Index = 0;
        s_ranThisRound = false;
        s_workedThisRound = false;
        s_resume = false;
        Running = wasRunning && Steps.Count > 0;
        Note = "";
        Save();
        message = (Running ? Lang.P("queue.loaded_running", Steps.Count, "Loaded '{name}' ({count} step), and it goes on running from its first step.",
                                    "Loaded '{name}' ({count} steps), and it goes on running from its first step.", ("name", Shown(Name)))
                           : Lang.P("queue.loaded", Steps.Count, "Loaded '{name}' ({count} step).", "Loaded '{name}' ({count} steps).", ("name", Shown(Name))))
            + (kept ? Lang.F("queue.what_was_here_before_kept", " What was here before is kept as '{autoName}'.", ("autoName", Shown(AutoName))) : "");
        RaiseChanged();
        return true;
    }

    public static bool DeletePreset(string name, out string message)
    {
        if (!s_presets.Remove(name)) { message = Lang.T("queue.saved_queue_no_longer_exists", "That saved queue no longer exists."); return false; }
        if (string.Equals(Name, name, StringComparison.OrdinalIgnoreCase)) Name = "";
        Save();
        message = Lang.F("queue.deleted_saved_queue_steps_screen", "Deleted the saved queue '{name}'. The steps on screen are untouched.", ("name", name));
        RaiseChanged();
        return true;
    }

    /// <summary>The share code of a saved queue, or of the steps on screen when <paramref name="name"/> is null.</summary>
    public static string? ExportCode(string? name)
    {
        if (name == null) return Steps.Count == 0 ? null : Share.Export(Name, Steps, Repeat, StopWhen, StopAtEnd);
        return s_presets.TryGetValue(name, out Preset? preset) ? Share.Export(name, preset.Steps, preset.Repeat, preset.StopWhen, preset.StopAtEnd) : null;
    }

    /// <summary>Reads a share code and keeps its queue as a saved queue (nothing on screen changes).</summary>
    public static bool ImportCode(string? code, out string importedName, out string message)
    {
        importedName = "";
        if (!Share.TryImport(code, out string name, out List<Step> steps, out bool repeat, out Goal? stop, out bool stopAtEnd, out message)) return false;
        if (name == "" || string.Equals(name, AutoName, StringComparison.OrdinalIgnoreCase)) name = "Shared queue";
        string unique = name;
        for (int n = 2; s_presets.ContainsKey(unique); n++) unique = $"{name} ({n})";
        Goal? cleanStop = CleanStop(stop);
        s_presets[unique] = new Preset { Steps = steps, Repeat = repeat, StopWhen = cleanStop, StopAtEnd = cleanStop != null && stopAtEnd };
        importedName = unique;
        Save();

        // Tell the player how much of it exists in their game (levels aside: locked steps are skipped when they come up).
        int known = 0;
        if (s_game != null)
            foreach (Step step in steps)
            {
                if (Goals(step).Any(g => g.Until == Until.Mastery && Mastery.Find(g.Subject) == null)) continue;   // a mastery task it does not have
                if (step.IsSell) { if (ItemOf(s_game, step.TargetId) != null) known++; continue; }
                if (step.IsSwitch) { known++; continue; }   // (a name: checked when its turn comes)
                // food for a skill: the food, and a skill of this game that eats; "until the food runs out": a skill that eats
                if (step.IsFood)
                {
                    if (ItemOf(s_game, step.TargetId) is { } food && food.Capabilities.TryGet<FoodCapability>(out _) && FoodSlot(s_game, step.Extra) != null) known++;
                    continue;
                }
                if (Goals(step).Any(g => g.Until == Until.FoodRunsOut) && FoodSlot(s_game, step.Entity) == null) continue;
                if (step.IsEquip) { if (step.TargetId.StartsWith(Step.LoadoutPrefix, StringComparison.Ordinal) || ItemOf(s_game, step.TargetId) != null) known++; continue; }
                ActionComponent? action = Targets.Find(s_game, step.Entity);
                if (action != null && Targets.All(action).Any(t => t.Id == step.TargetId && t.Extra == step.Extra)) known++;
            }
        string stopNote = "";
        if (s_presets[unique].StopWhen is { } kept)
        {
            bool missing = s_game != null && kept.Until switch
            {
                Until.Level => !s_game.LeveledEntities.Any(s => s.DisplayName.Text == kept.Subject),
                Until.Mastery => Mastery.Find(kept.Subject) == null,
                _ => ItemOf(s_game, kept.Subject) == null,
            };
            stopNote = s_presets[unique].StopAtEnd
                ? Lang.F("queue.imported_stop_round_end", " Its own stop: the whole queue stops at the end of a round once {stop}", ("stop", StopText(kept)))
                : Lang.F("queue.imported_stop", " Its own stop: the whole queue stops when {stop}", ("stop", StopText(kept)))
                + (missing ? Lang.F("queue.your_game_has_no_so", ". Your game has no {subjectName}, so change that stop before you run it.", ("subjectName", kept.SubjectName)) : ".");
        }
        message = Lang.P("queue.imported", steps.Count, "Imported '{name}': {count} step", "Imported '{name}': {count} steps", ("name", unique))
            + (known == steps.Count ? "." : Lang.F("queue.them_things_your_game_does", ", {unknown} of them for things your game does not have (those will be skipped).", ("unknown", steps.Count - known)))
            + stopNote + Lang.T("queue.select_press_load_use", " Select it and press Load to use it.");
        RaiseChanged();
        return true;
    }

    // ---------------------------------------------------------------- running

    public static void Start()
    {
        if (Steps.Count == 0) { Note = Lang.T("queue.add_step_first", "Add a step first."); RaiseChanged(); return; }
        // (A stop looked at when a round is over lets the queue start: it ends after this round.)
        if (s_game != null && !StopAtEnd && StopReached(s_game))
        {
            Note = Lang.F("queue.nothing_do_already_queue_stop", "Nothing to do: {stopText} already, and that is the queue's stop. Change or remove the stop to run the queue.", ("stopText", StopText(StopWhen!)));
            Trace("not started: the queue's stop is already reached");
            RaiseChanged();
            return;
        }
        // After a pause: carry on. What was finished stays finished, wherever it has been moved since; the rest
        // starts again in the order of the list. Otherwise every step starts afresh.
        bool resume = s_resume && Steps.Any(s => s.Run != RunState.Done);
        foreach (Step s in Steps)
        {
            if (!resume) { s.LastResult = ""; s.LastSeconds = 0; }
            if (resume && s.Run == RunState.Done) continue;
            if (s.Status != StepStatus.Paused) ClearCounts(s);    // "make N more" / "N times" carry on after a pause, else count afresh
            Say(s, StepStatus.None, "");
            s.Run = RunState.Idle;
            s.Ran = false;
            s.Seconds = s.SecondsBefore;   // (0, or what it had run before a pause: shown and saved at once)
            s.RetryAt = s.GrewAt = 0;
            s.Pile = 0;
        }
        Running = true;
        s_resume = false;
        s_stopPending = false;
        s_emptyVisited.Clear();   // (the saved queues left without doing anything, in one run)
        lock (s_handStops) s_handStops.Clear();
        s_ranThisRound = resume && Steps.Any(s => s.Run == RunState.Done && s.Ran);
        s_workedThisRound = s_ranThisRound;
        s_laneRan.Clear();
        if (resume) foreach (Step s in Steps.Where(s => s.Run == RunState.Done && s.Ran)) s_laneRan.Add(LaneOf(s));
        SetIndex();
        Note = "";
        // An idle action left running from the last time belongs to the player now: the queue does not take it over.
        // (One that "also when the queue is not running" started is still the queue's: it makes room for the steps.)
        if (!IdleAlways) s_idleOwned = false;
        s_idleBlocked = s_idleRanOut = false;
        s_idleRetryAt = 0;
        IdleSays("");
        Trace($"started at step {Index + 1} of {Steps.Count}, up to {Lanes} at once: " + string.Join(" | ", Steps.Select(s => $"{s.Name} {s.Goal}")));
        RaiseChanged();
    }

    /// <summary>
    /// A small diary of what the queue did and why (queue-log.txt next to the mod). It exists so that
    /// "it skipped a step" can be answered from facts; it holds nothing but step names and item counts.
    /// </summary>
    static void Trace(string text)
    {
        try
        {
            string file = Path.Combine(Dir, "queue-log.txt");
            var info = new FileInfo(file);
            if (info.Exists && info.Length > 200_000)
            {
                string[] lines = File.ReadAllLines(file);
                File.WriteAllLines(file, lines.Skip(lines.Length / 2));
            }
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{s_character}] {text}{Environment.NewLine}");
        }
        catch { }
    }

    public static void Pause(string why)
    {
        if (!Running) return;
        SetIndex();
        Trace($"paused at step {Index + 1}: {why}");
        Running = false;
        s_resume = true;
        foreach (Step s in Steps.Where(s => s.Run is RunState.Running or RunState.Waiting
                     || s.Run == RunState.Idle && (s.Ran || s.SecondsBefore > 0 || Goals(s).Any(g => g.CountBefore > 0))))
        {
            Say(s, StepStatus.Paused, Lang.T("state.paused", "paused"));
            if (s.Ran) KeepCounts(s);   // keep what "make N more" / "N times" has already counted
        }
        // Like the steps' own actions, an idle action that runs keeps running: the player decides from here.
        // With "also when the queue is not running" it stays the queue's own, and goes on as such.
        if (!IdleAlways) s_idleOwned = false;
        IdleSays("");
        Note = why;
        Publish();
        RaiseChanged();
    }

    static void Finish(string why)
    {
        if (Running) Trace($"ended: {why}");
        StopIdle();   // emptied or cut short: the idle action the queue started goes with it (a real finish lets go of it first)
        IdleSays("");
        Running = false;
        s_resume = false;
        Index = 0;
        Note = why;
        Publish();
    }

    static string Number(Step step) => $"step {Steps.IndexOf(step) + 1} '{step.Name}' ({step.Goal})";

    /// <summary>What a step shows in its State column, and what that means (the queue only ever looks at the meaning).</summary>
    static void Say(Step step, StepStatus status, string text, string detail = "")
    {
        step.Status = status;
        step.State = text;
        step.Detail = detail;
        if (text != "" && Lang.IsEnglish && StatusOf(text) != status) StatusMismatches.Enqueue($"step: {status} written as '{text}'");
    }

    /// <summary>
    /// What English state words mean: a snapshot saved before 0.14.0 holds only the words. It is also the check that
    /// every state the queue writes in English starts with the word of its meaning (<see cref="StatusMismatches"/>).
    /// </summary>
    internal static StepStatus StatusOf(string words)
    {
        bool Starts(string w) => words.StartsWith(w, StringComparison.Ordinal);
        return words == "" ? StepStatus.None
            : Starts("running") ? StepStatus.Running
            : words == "paused" ? StepStatus.Paused
            : Starts("done") ? StepStatus.Done
            : Starts("skipped") ? StepStatus.Skipped
            : Starts("stopped") || Starts("the queue's stop was reached") ? StepStatus.Stopped
            : Starts("not taken") ? StepStatus.NotTaken
            : Starts("waiting") || Starts("waits") ? StepStatus.Waiting
            : StepStatus.None;
    }

    /// <summary>What the English words of the idle action's state stood for in the window's colours before 0.14.0.</summary>
    internal static IdleTone ToneOf(string words) =>
        words.StartsWith("running", StringComparison.Ordinal) ? IdleTone.Good
        : words.StartsWith("stopped", StringComparison.Ordinal) || words.StartsWith("cannot", StringComparison.Ordinal) ? IdleTone.Bad
        : IdleTone.Plain;

    /// <summary>
    /// For tests: states written with a meaning their English words do not carry (it stays empty when every place that
    /// writes one says what it means). Looked at only while the words are English.
    /// </summary>
    public static readonly System.Collections.Concurrent.ConcurrentQueue<string> StatusMismatches = new();

    static void IdleSays(string text, IdleTone tone = IdleTone.Plain)
    {
        IdleState = text;
        IdleTone = tone;
        if (text != "" && Lang.IsEnglish && ToneOf(text) != tone) StatusMismatches.Enqueue($"idle: {tone} written as '{text}'");
    }

    static void End(Step step, StepStatus status, string state)
    {
        Say(step, status, state);
        step.LastResult = state;
        step.LastSeconds = step.Seconds;
        step.Run = RunState.Done;
        Trace($"{Number(step)}: {state}");
    }

    /// <summary>Call a few times per second while a character is open.</summary>
    public static void Tick(Game game, double seconds)
    {
        if (!ReferenceEquals(game, s_game)) return;
        if (Targets.OnMainThread) { Targets.PrimeMeta(); s_toldFightOffline = false; s_running = game.ActionManager.ActiveActions.Count; }
        else s_running = Math.Max(s_running, game.ActionManager.ActiveActions.Count);
        if (s_changedLater && Targets.OnMainThread) RaiseChanged();
        if (s_saveLater && Targets.OnMainThread) Save();
        s_clock += seconds;
        if (!Running)
        {
            if (game.Flags.IsOffline) return;
            string before = IdleState;
            bool owned = s_idleOwned, blocked = s_idleBlocked;
            try { KeepIdleOutside(game); }
            catch (Exception e) { Log("The idle action failed: " + e.Message); IdleSays(Lang.T("queue.failed_see_log", "failed (see the log)")); }
            Publish();
            if (IdleState != before || owned != s_idleOwned || blocked != s_idleBlocked) RaiseChanged();
            return;
        }
        if (Steps.Count == 0) { Finish(Lang.T("queue.queue_empty", "The queue is empty.")); RaiseChanged(); return; }

        // The queue's own stop ("stop the whole queue when ..."): looked at first, so nothing more is started once it is reached.
        if (StopCheck(game)) return;

        // 0. The idle action was a fight and the character died in it: never walk straight back in. (A combat step
        //    cannot run at the same time - one skill, one thing - so the death was the idle action's.)
        if (s_died && s_idleOwned && Idle?.Entity == "MelvorBase:Combat" && !Steps.Any(s => s.Run == RunState.Running && s.Entity == Idle.Entity))
        {
            s_died = false;
            s_idleOwned = false;
            s_idleBlocked = true;
            IdleSays(Lang.T("queue.stopped_your_character_died_starts", "stopped: your character died (it starts again when you start the queue)"), IdleTone.Bad);
            Trace("while idle: the character died, the fight is not started again");
        }

        // 1. The steps that are at work.
        foreach (Step step in Steps.Where(s => s.Run == RunState.Running).ToList())
        {
            if (!Running) break;   // one of them paused the queue
            Guard(step, () => Watch(game, step, seconds));
        }

        // In lanes, with "start over" on, every lane starts over by itself as soon as it is through (a short lane
        // does not wait for a long one); a lane that did nothing in its round stays finished. Done before the
        // scheduling, so a lane that makes what another lane uses is never seen "finished" for a moment.
        // (Not while a "go to a saved queue" step waits for the other lanes to get through: it would wait for ever.)
        if (Running && Repeat && LaneMode && !Steps.Any(s => s.IsSwitch && s.Run != RunState.Done && Head(LaneOf(s)) == s))
            foreach (int lane in Steps.Select(LaneOf).Distinct().ToList())
            {
                var mine = Steps.Where(s => LaneOf(s) == lane).ToList();
                if (!LaneRepeats(lane)) continue;   // this lane runs once: it stays finished
                if (!mine.All(s => s.Run == RunState.Done)) continue;
                // The queue's stop, looked at when a round is over: a lane that is through does not start over while it holds.
                if (StopAtEnd && StopReached(game)) continue;
                if (!s_laneRan.Remove(lane)) continue;
                if (s_workedThisRound) s_emptyVisited.Clear();   // this lane did something: queues that follow it are not going round for nothing
                foreach (Step s in mine) { Say(s, StepStatus.None, ""); s.Run = RunState.Idle; s.Ran = false; s.Seconds = s.RetryAt = s.GrewAt = 0; s.Pile = 0; ClearCounts(s); }
                Trace($"lane {lane} starts over");
            }

        // 2. Room for more? Waiting steps try again; then the next steps in order take the free places.
        //    In lanes: only the step each lane is at, and a waiting one holds no place (it is not doing anything).
        if (Running)
        {
            int lanes = Lanes;
            bool laneMode = LaneMode;
            int Busy() => laneMode ? Steps.Count(s => s.Run == RunState.Running)
                                   : Steps.Count(s => !s.IsInstant && s.Run is RunState.Running or RunState.Waiting);
            foreach (Step step in Steps.ToList())
            {
                if (!Running) break;
                if (step.Run == RunState.Done || step.Run == RunState.Running) continue;
                if (laneMode && Head(LaneOf(step)) != step) continue;   // a lane does its steps one after another
                // Selling, equipping and going to another saved queue take no place and are over at once; they only
                // wait for what comes before them.
                if (step.IsInstant)
                {
                    Guard(step, () => Begin(game, step));
                    if (StopCheck(game)) return;   // selling or equipping may reach the queue's stop: nothing after it starts
                    if (s_switchTo != null) break;   // the queue is being replaced: nothing else of the old one starts
                    continue;
                }
                if (!laneMode && Steps.TakeWhile(s => s != step).Any(s => s.IsInstant && s.Run != RunState.Done)) continue;   // nothing passes them
                // A skill does one thing at a time. In lanes only a running step holds its skill (starting would stop it).
                bool skillTaken = laneMode && Steps.Any(s => s != step && s.Run == RunState.Running && s.Entity == step.Entity);
                if (step.Run == RunState.Waiting)
                {
                    if (!laneMode || Busy() < lanes && !skillTaken) Guard(step, () => Begin(game, step));
                    continue;
                }
                if (Busy() >= lanes || skillTaken) continue;
                // In one list a later step for the same skill waits its turn.
                if (!laneMode && Steps.Any(s => s != step && s.Run is RunState.Running or RunState.Waiting && s.Entity == step.Entity)) continue;
                Guard(step, () => Begin(game, step));
            }
        }

        // A "go to a saved queue" step came up: the queue is replaced now, between two rounds of looking at it.
        if (s_switchTo != null)
        {
            string to = s_switchTo;
            s_switchTo = null;
            // Going on with another saved queue ends this queue's round: its own stop, looked at when a round is over, comes first.
            if (StopAtEnd && StopReached(game))
            {
                if (s_switchFrom is { } from) { Say(from, StepStatus.NotTaken, Lang.F("queue.not_taken_queue_stop_was", "not taken: the queue's stop was reached, so it does not go on with '{queue}'", ("queue", to))); from.LastResult = from.State; }
                StopForGood(game);
                Publish();
                RaiseChanged();
                return;
            }
            SwitchNow(to);
            Publish();
            RaiseChanged();
            return;
        }

        // 3. All done?
        if (Running && Steps.All(s => s.Run == RunState.Done))
        {
            // The queue's stop, looked at when a round is over: the round is over now.
            if (StopAtEnd && StopReached(game)) StopForGood(game);
            else if (Repeat && s_ranThisRound && !LaneMode)
            {
                if (s_workedThisRound) s_emptyVisited.Clear();   // this round did something: queues that follow it are not going round for nothing
                s_ranThisRound = false;
                s_workedThisRound = false;
                foreach (Step s in Steps) { Say(s, StepStatus.None, ""); s.Run = RunState.Idle; s.Ran = false; s.Seconds = s.RetryAt = s.GrewAt = 0; s.Pile = 0; ClearCounts(s); }
            }
            else if (s_ranThisRound) EndRun(game, Lang.T("queue.queue_has_finished", "The queue has finished."));
            else EndRun(game, Lang.T("queue.nothing_queue_could_done_so", "Nothing in the queue could be done, so it stopped."), Lang.T("queue.nothing_queue_could_done", "Nothing in the queue could be done."));
        }

        // 4. Every step that is left waits: the idle action takes a free slot meanwhile (or makes room again).
        if (Running)
        {
            try { KeepIdle(game); }
            catch (Exception e) { Log("The idle action failed: " + e.Message); IdleSays(Lang.T("queue.failed_see_log", "failed (see the log)")); }
        }
        if (Running) SetIndex();
        Publish();
        RaiseChanged();
    }

    static void Guard(Step step, Action work)
    {
        try { work(); }
        catch (Exception e)
        {
            Log($"Step '{step.Name}' failed: {e.Message}");
            End(step, StepStatus.Skipped, Lang.T("queue.skipped_error_occurred_see_log", "skipped: an error occurred (see the log)"));
        }
    }

    /// <summary>True while a step before this one is not over yet: it may still make what this one needs.</summary>
    static bool EarlierAtWork(Step step)
    {
        bool laneMode = LaneMode;
        return Steps.TakeWhile(s => s != step).Any(s => s.Run != RunState.Done && (!laneMode || LaneOf(s) == LaneOf(step)));
    }

    /// <summary>
    /// True while more of what this step uses may still come in: an earlier step is not over, or something
    /// running beside it (the player's own mining, an action Multitask keeps going) makes one of its materials.
    /// "Until it stops" takes the stop at its word when the supply is not the queue's own: the player may mine forever.
    /// </summary>
    static bool MoreComing(Game game, Step step, ActionComponent action, Target target) =>
        EarlierAtWork(step) || OtherLaneMakes(game, step, action, target) || (step.Until != Until.Stops && Supplied(game, action, target));

    /// <summary>In lanes: another lane still has a step to do that makes one of this step's materials (fish for the cooking).</summary>
    static bool OtherLaneMakes(Game game, Step step, ActionComponent action, Target target)
    {
        if (!LaneMode) return false;
        HashSet<string> needs = Targets.Ingredients(action, target);
        if (needs.Count == 0) return false;
        foreach (Step other in Steps)
        {
            if (other.Run == RunState.Done || other.IsInstant || LaneOf(other) == LaneOf(step)) continue;
            if (Targets.Find(game, other.Entity) is { } a && Targets.Product(a, new Target(other.TargetId, other.Name, other.Extra)) is { } made && needs.Contains(made.Id))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True while some other running action (the player's own, one kept by Multitask, another step, the idle
    /// action) makes an item this recipe uses: mining uncut emeralds beside crafting emeralds.
    /// </summary>
    static bool Supplied(Game game, ActionComponent action, Target target)
    {
        HashSet<string> needs = Targets.Ingredients(action, target);
        if (needs.Count == 0) return false;
        // What runs beside it, and on what. Every product counts, not just the main one: a fishing spot gives several fish.
        var beside = new Dictionary<string, string>();
        foreach (ActionComponent other in game.ActionManager.ActiveActions.ToList())
            if (!ReferenceEquals(other, action) && Targets.Current(other) is Target doing) beside[other.Entity.Id] = doing.Id;
        if (beside.Count == 0) return false;
        foreach ((Item item, List<Maker> makers) in Planner.Makers(game))
            if (needs.Contains(item.Id) && makers.Any(m => beside.TryGetValue(m.Entity.Id, out string? on) && on == m.Target.Id)) return true;
        return false;
    }

    // ---------------------------------------------------------------- the "while idle" action

    static Target IdleTarget(Step idle) => new(idle.TargetId, idle.Name, idle.Extra);

    static void StopAction(ActionComponent action)
    {
        if (HandsOff(action)) return;   // fights waiting offline: not ended (see OfflineTick)
        if (!action.TryStop()) action.TryStop(force: true);
    }

    /// <summary>Stops the idle action if the queue started it and it still runs.</summary>
    static void StopIdle()
    {
        if (!s_idleOwned) return;
        s_idleOwned = false;
        if (s_game == null || Idle == null) return;
        ActionComponent? action = Targets.Find(s_game, Idle.Entity);
        if (action != null && Targets.IsDoing(action, IdleTarget(Idle))) StopAction(action);
    }

    /// <summary>
    /// How many more actions fit right now: the slots Multitask gives (1 without it), less the queue's running
    /// steps, the idle action the queue runs, and the player's own actions - running or waiting to be started
    /// again by Multitask. Below 1 there is no room.
    /// </summary>
    static int FreeSlots(Game game)
    {
        var queue = Running ? Steps.Where(s => s.Run == RunState.Running).Select(s => s.Entity).ToHashSet() : new HashSet<string>();
        string idle = s_idleOwned && Idle != null ? Idle.Entity : "";
        IEnumerable<string> player = Targets.PlayerKept() ?? game.ActionManager.ActiveActions.Select(a => a.Entity.Id).ToHashSet();
        int used = queue.Count + (idle != "" ? 1 : 0) + player.Count(e => !queue.Contains(e) && e != idle);
        return Capacity() - used;
    }

    /// <summary>Why the idle action may not run right now while the queue runs ("" when the queue is at work), or null when it may.</summary>
    static string? IdleBlocker(Game game, ActionComponent action, Target target, bool doing)
    {
        if (s_idleBlocked) return IdleState;
        int running = Steps.Count(s => s.Run == RunState.Running);
        if (!Steps.Any(s => s.Run is RunState.Waiting or RunState.Idle) || running >= Lanes) return "";   // the queue is at work
        if (Steps.Any(s => s.Run == RunState.Running && s.Entity == action.Entity.Id)) return Lang.T("queue.not_now_step_using_skill", "not now: a step is using this skill");
        if (action.IsStarted && !doing) return Lang.T("queue.not_now_skill_doing_something", "not now: this skill is doing something else");
        HashSet<string> uses = Targets.Ingredients(action, target);
        if (uses.Count > 0)
            foreach (Step step in Steps.Where(s => s.Run != RunState.Done))
                if (Targets.Find(game, step.Entity) is { } stepAction
                    && Targets.Ingredients(stepAction, new Target(step.TargetId, step.Name, step.Extra)).Overlaps(uses))
                    return Lang.T("queue.not_now_would_use_up", "not now: it would use up what a step of the queue needs");
        int free = FreeSlots(game) + (s_idleOwned && doing ? 1 : 0);
        if (free <= 0) return Lang.T("queue.not_now_no_free_slot", "not now: no free slot (switch on a Multitask slot for it)");
        return null;
    }

    /// <summary>While the queue runs: start the idle action when every step left is waiting, stop it when a step needs the place.</summary>
    static void KeepIdle(Game game)
    {
        if (Idle == null) { IdleSays(""); return; }
        ActionComponent? action = Targets.Find(game, Idle.Entity);
        if (action == null) { IdleSays(Lang.T("queue.skill_not_game", "this skill is not in the game")); return; }
        Target target = IdleTarget(Idle);
        bool doing = Targets.IsDoing(action, target);
        StopIdleFightWithoutAmmo(game, action, ref doing);
        if (s_idleOwned && !doing)
        {
            s_idleOwned = false;
            if (TakeHandStop(Idle.Entity))
            {
                s_idleBlocked = true;
                IdleSays(Lang.T("queue.stopped_by_you_comes_back", "stopped by you; it comes back when you start the queue again"), IdleTone.Bad);
                Trace($"while idle: {Idle.Name} was stopped with the game's button, it is not started again in this run");
            }
            else if (Targets.Batches(game, action, target) == 0)
            {
                // Out of materials: it starts again once there is a little pile, like a waiting step.
                s_idleRanOut = true;
                s_idlePile = 0;
                s_idleGrewAt = s_clock;
                Trace($"while idle: {Idle.Name} ran out of materials");
            }
            else
            {
                // Not out of anything: the player stopped it or started something by hand, or the bank is full.
                // Starting it again would fight them.
                s_idleBlocked = true;
                IdleSays(Lang.T("queue.stopped_by_you_bank_full", "stopped (by you, or the bank is full); it comes back when you start the queue again"), IdleTone.Bad);
                Trace($"while idle: {Idle.Name} was stopped, it is not started again in this run");
            }
        }
        string? why = IdleBlocker(game, action, target, doing);
        if (why != null)
        {
            if (s_idleOwned && doing && !HandsOff(action))
            {
                StopAction(action);
                s_idleOwned = false;
                Trace($"while idle: stopped {Idle.Name} ({(why == "" ? "the queue is at work again" : why)})");
            }
            if (!s_idleBlocked) IdleSays(why);
            return;
        }
        if (doing) { IdleSays(s_idleOwned ? Lang.T("queue.running_now", "running now") : Lang.T("queue.running_now_started_by_you", "running now (started by you)"), IdleTone.Good); return; }
        TryStartIdle(game, action, target);
    }

    static double s_idleNoAttackSince = -1;

    /// <summary>
    /// The idle action is a fight the queue started and the player can no longer attack in it (out of ammo, out of
    /// runes): after two seconds of it (not a moment's gear change) it is stopped, instead of standing there while the
    /// monster hits. It is not started again until the player can fight (see <see cref="TryStartIdle"/>).
    /// </summary>
    static void StopIdleFightWithoutAmmo(Game game, ActionComponent action, ref bool doing)
    {
        if (!doing || !s_idleOwned || action is not CombatActionComponent || HandsOff(action) || CannotAttack(game) is not { Length: > 0 } why)
        {
            s_idleNoAttackSince = -1;
            return;
        }
        if (s_idleNoAttackSince < 0) s_idleNoAttackSince = s_clock;
        if (s_clock - s_idleNoAttackSince < 2) return;
        s_idleNoAttackSince = -1;
        StopAction(action);
        s_idleOwned = false;
        doing = false;
        IdleSays(Lang.F("idle.stopped", "stopped: {why}", ("why", why)), IdleTone.Bad);
        Trace($"while idle: stopped {Idle?.Name} ({why})");
    }

    static void TryStartIdle(Game game, ActionComponent action, Target target)
    {
        if (s_clock < s_idleRetryAt) return;
        s_idleRetryAt = s_clock + RetrySeconds;
        if (!Targets.CanStart(action, target, out string problem)) { IdleSays(Lang.F("idle.cannot_start", "cannot start: {why}", ("why", problem)), IdleTone.Bad); return; }
        if (HandsOff(action)) { IdleSays(Lang.T("queue.waits_game_open_fight", "waits for the game to be open (a fight)")); return; }
        if (action is CombatActionComponent && CannotAttack(game) is { Length: > 0 } cannot)
        {
            IdleSays(Lang.F("queue.not_started_starts_once_you", "not started: {cannot} (it starts once you can fight again)", ("cannot", cannot)));
            return;
        }
        long batches = Targets.Batches(game, action, target);
        if (batches == 0)
        {
            if (!s_idleRanOut) { s_idleRanOut = true; s_idlePile = 0; s_idleGrewAt = s_clock; }
            IdleSays(Lang.T("queue.waiting_materials_have_run_out", "waiting: its materials have run out"));
            return;
        }
        if (s_idleRanOut && batches < PileSize)
        {
            if (batches > s_idlePile) s_idleGrewAt = s_clock;
            s_idlePile = batches;
            if (s_clock - s_idleGrewAt < SettleSeconds) { IdleSays(Lang.F("queue.waiting_collecting_materials", "waiting: collecting materials ({batches}/{pileSize})", ("batches", batches), ("pileSize", PileSize))); return; }
        }
        bool others = game.ActionManager.ActiveActions.Count > 0;
        if (!(others ? Targets.StartBeside(game, action, target, out problem) : Targets.Start(game, action, target, out problem)))
        {
            IdleSays(Lang.F("idle.cannot_start", "cannot start: {why}", ("why", problem)), IdleTone.Bad);
            return;
        }
        s_idleOwned = true;
        s_idleRanOut = false;
        s_idlePile = 0;
        IdleSays(Lang.T("queue.running_now", "running now"), IdleTone.Good);
        Trace($"while idle: started {Idle!.Name}{(others ? " beside what runs" : "")}");
    }

    /// <summary>
    /// The queue is not running (paused, finished, emptied, never started) and "also when the queue is not running"
    /// is on: start the idle action whenever the character does nothing at all. It never pushes anything out and
    /// never takes a slot Multitask keeps for one of the player's own actions. After the player stops it (or stops
    /// anything else) with a game button, or after a death, it waits until the player starts something by hand.
    /// </summary>
    static void KeepIdleOutside(Game game)
    {
        var runningNow = game.ActionManager.ActiveActions.Select(a => $"{a.Entity.Id}|{Targets.Current(a)?.Id}").ToHashSet();
        HashSet<string> before = s_runningBefore;
        s_runningBefore = runningNow;
        bool died = s_died;
        s_died = false;
        if (!IdleAlways || Idle == null)
        {
            lock (s_handStops) s_handStops.Clear();
            if (!s_idleOwned) IdleSays("");
            return;
        }
        ActionComponent? action = Targets.Find(game, Idle.Entity);
        if (action == null) { IdleSays(Lang.T("queue.skill_not_game", "this skill is not in the game")); return; }
        Target target = IdleTarget(Idle);
        bool doing = Targets.IsDoing(action, target);
        StopIdleFightWithoutAmmo(game, action, ref doing);
        string idleKey = $"{Idle.Entity}|{target.Id}";

        // Something the player stopped with a game button: hands off until they start something themselves.
        bool handStop;
        lock (s_handStops) { handStop = s_handStops.Count > 0; s_handStops.Clear(); }
        if (handStop && !doing)
        {
            s_idleOwned = false;
            s_idleBlocked = true;
            IdleSays(Lang.T("queue.waiting_you_stopped_action_so", "waiting: you stopped an action, so it starts again after your next action of your own"));
            Trace("idle (queue not running): the player stopped an action, the idle action waits");
        }
        if (died && Idle.Entity == "MelvorBase:Combat")
        {
            s_idleOwned = false;
            s_idleBlocked = true;
            IdleSays(Lang.T("queue.waiting_your_character_died_starts", "waiting: your character died; it starts again after your next action of your own"));
            Trace("idle (queue not running): the character died, the fight is not started again");
        }
        // The player started something new by hand: a block is lifted (the idle action waits for that to end).
        if (s_idleBlocked && runningNow.Any(k => k != idleKey && !before.Contains(k)))
        {
            s_idleBlocked = false;
            IdleSays("");
        }

        if (s_idleOwned && !doing)
        {
            s_idleOwned = false;
            if (Targets.Batches(game, action, target) == 0)
            {
                s_idleRanOut = true;
                s_idlePile = 0;
                s_idleGrewAt = s_clock;
                Trace($"idle (queue not running): {Idle.Name} ran out of materials");
            }
            else if (game.ActionManager.ActiveActions.Count == 0)
            {
                // Not out of anything and nothing else took its place: the bank is full, or the game stopped it.
                s_idleBlocked = true;
                IdleSays(Lang.T("queue.stopped_bank_may_full_starts", "stopped (the bank may be full); it starts again after your next action of your own"), IdleTone.Bad);
                Trace($"idle (queue not running): {Idle.Name} stopped, it is not started again for now");
            }
        }
        if (doing) { IdleSays(s_idleOwned ? Lang.T("queue.running_now", "running now") : Lang.T("queue.running_now_started_by_you", "running now (started by you)"), IdleTone.Good); return; }
        if (s_idleBlocked) return;
        if (s_clock < s_idleNotBefore) { IdleSays(Lang.T("queue.starting_moment", "starting in a moment")); return; }
        if (game.ActionManager.ActiveActions.Count > 0) { IdleSays(Lang.T("queue.waits_until_your_character_does", "waits until your character does nothing")); return; }
        if (FreeSlots(game) <= 0) { IdleSays(Lang.T("queue.waits_multitask_keeps_place_one", "waits: Multitask keeps the place for one of your actions")); return; }
        TryStartIdle(game, action, target);
    }

    /// <summary>A step is about to start: the idle action makes room when it uses the same skill or the last free slot.</summary>
    static void MakeRoomFor(Game game, Step step, bool beside)
    {
        if (!s_idleOwned || Idle == null) return;
        if (beside && Idle.Entity != step.Entity && FreeSlots(game) > 0) return;   // there is room for both
        ActionComponent? action = Targets.Find(game, Idle.Entity);
        if (action != null && HandsOff(action)) return;   // fights waiting offline: an idle fight is not ended
        s_idleOwned = false;
        if (action != null && Targets.IsDoing(action, IdleTarget(Idle))) StopAction(action);
        IdleSays(Lang.T("queue.made_room_step", "made room for a step"));
        Trace($"while idle: stopped {Idle.Name} to make room for {Number(step)}");
    }

    /// <summary>The queue has finished: start the idle action (it is the player's own from then on).</summary>
    static void IdleAfterEnd(Game game)
    {
        if (Idle == null) return;
        ActionComponent? action = Targets.Find(game, Idle.Entity);
        if (action == null) return;
        Target target = IdleTarget(Idle);
        string Tell(string text) { Note = $"{Note} {text}"; Trace("after the end: " + text); return text; }
        string idle = Idle.Name;
        if (Targets.IsDoing(action, target)) { Tell(Lang.F("idle.end.goes_on", "{idle} goes on (your idle action).", ("idle", idle))); return; }
        if (s_idleBlocked) return;
        if (action.IsStarted) { Tell(Lang.F("idle.end.skill_busy", "{idle} was not started: that skill is doing something else.", ("idle", idle))); return; }
        if (FreeSlots(game) <= 0) { Tell(Lang.F("idle.end.no_slot", "{idle} was not started: there is no free slot for it.", ("idle", idle))); return; }
        if (!Targets.CanStart(action, target, out string problem)) { Tell(Lang.F("idle.end.not_started", "{idle} was not started: {why}.", ("idle", idle), ("why", problem))); return; }
        if (HandsOff(action)) { Tell(Lang.F("idle.end.game_closed", "{idle} was not started while the game was closed (a fight waits for the game to be open).", ("idle", idle))); return; }
        if (action is CombatActionComponent && CannotAttack(game) is { Length: > 0 } cannot) { Tell(Lang.F("idle.end.not_started", "{idle} was not started: {why}.", ("idle", idle), ("why", cannot))); return; }
        if (Targets.Batches(game, action, target) == 0) { Tell(Lang.F("idle.end.no_materials", "{idle} was not started: its materials have run out.", ("idle", idle))); return; }
        bool others = game.ActionManager.ActiveActions.Count > 0;
        if (others ? Targets.StartBeside(game, action, target, out problem) : Targets.Start(game, action, target, out problem))
            Tell(Lang.F("idle.end.now_doing", "Now doing your idle action: {idle}.", ("idle", idle)));
        else Tell(Lang.F("idle.end.not_started", "{idle} was not started: {why}.", ("idle", idle), ("why", problem)));
    }

    static string WaitingText(Step step) =>
        EarlierAtWork(step) ? Lang.T("queue.waiting_materials_from_earlier_step", "waiting: for materials from an earlier step") : Lang.T("queue.waiting_materials_still_being_gathered", "waiting: for materials (still being gathered)");

    static void Wait(Step step, string why)
    {
        if (step.Run != RunState.Waiting) Trace($"{Number(step)}: {why}");
        step.Run = RunState.Waiting;
        Say(step, StepStatus.Waiting, why);
    }

    static void Begin(Game game, Step step)
    {
        if (step.IsSell) { Sell(game, step); return; }
        if (step.IsEquip) { Equip(game, step); return; }
        if (step.IsSwitch) { SwitchTo(game, step); return; }
        // Offline progress without the fight's music held (see Offline.FightsAllowed): a fight is neither started nor
        // ended by the queue, the game's music player and its notice would react to it right there. The step waits.
        if (FightsWait && step.Entity == CombatId)
        {
            if (step.Run != RunState.Waiting) { step.Run = RunState.Waiting; Say(step, StepStatus.Waiting, Lang.T("queue.waits_game_open_fight", "waits for the game to be open (a fight)")); }
            return;
        }
        ActionComponent? action = Targets.Find(game, step.Entity);
        if (action == null) { End(step, StepStatus.Skipped, Lang.T("queue.skipped_skill_not_game", "skipped: this skill is not in the game")); return; }
        // A fight the player could not take part in (no ammo for the weapon, no runes for the spell) is not started:
        // the character would only stand there while the monster hits. With "start over" on, the fight that ended
        // for that reason is not started again and again either: a round where nothing could start ends the queue.
        if (action is CombatActionComponent && CannotAttack(game) is { Length: > 0 } cannot)
        {
            End(step, StepStatus.Skipped, Lang.F("state.skipped_because", "skipped: {why}", ("why", cannot)));
            return;
        }
        // "Until this mastery task is complete" for a task this game does not have (another version): never reached.
        if (step.Until == Until.Mastery && Mastery.Find(step.Subject) == null)
        {
            End(step, StepStatus.Skipped, Lang.F("queue.skipped_mastery_task_not_game", "skipped: the mastery task \"{subjectName}\" is not in this game", ("subjectName", step.SubjectName)));
            return;
        }
        if (!step.Ran && step.Run != RunState.Waiting)
            foreach (Goal orTask in step.Others.Where(g => g.Until == Until.Mastery && Mastery.Find(g.Subject) == null))
                Trace($"{Number(step)}: a goal of it waits for a mastery task this game does not have ({orTask.SubjectName}): only its other goals can end it");
        // "Until the food runs out" for a skill that eats nothing (no food slot): never reached.
        if (step.Until == Until.FoodRunsOut && FoodSlot(game, step.Entity) == null)
        {
            End(step, StepStatus.Skipped, Lang.F("queue.skipped_has_no_food_run", "skipped: {skill} has no food to run out of", ("skill", action.Entity.DisplayName.Text)));
            return;
        }
        if (!step.Ran && Goals(step).FirstOrDefault(g => ReadsTheGame(g) && Reached(game, step, g)) is { } already)
        {
            // Not needed: say what was found, so "it never mined" explains itself.
            End(step, StepStatus.Done, already.Until switch
            {
                Until.Item => Lang.F("queue.done_bank_already_holds_nothing", "done: the bank already holds {owned:N0} {subjectName}, nothing to do", ("owned", Owned(game, already)), ("subjectName", already.SubjectName)),
                Until.ItemLeft => Lang.F("queue.done_already_only_left_nothing", "done: already only {owned:N0} {subjectName} left, nothing to do", ("owned", Owned(game, already)), ("subjectName", already.SubjectName)),
                Until.Nodes => Lang.F("queue.done_node_unlocks_already_earned", "done: {nodes:N0} {subjectName} node unlocks are already earned (of {mostNodes:N0}), nothing to do", ("nodes", Nodes(game, step, already)), ("subjectName", already.SubjectName), ("mostNodes", MostNodes(game, step.TargetId, already.Subject == "major"))),
                Until.Mastery => Lang.T("queue.done_mastery_task_already_complete", "done: that mastery task is already complete, nothing to do"),
                Until.FoodRunsOut => FoodSlot(game, step.Entity)?.SelectedFood?.Item is { } food
                    ? Lang.F("queue.done_no_left_bank_food", "done: no {food} left in the bank (its food), nothing to do", ("food", food.DisplayName.GetPluralText(2)))
                    : Lang.T("queue.done_no_food_selected_nothing", "done: no food is selected for it, nothing to do"),
                _ => Lang.F("queue.done_already_level_nothing_do", "done: {subjectName} is already level {level}, nothing to do", ("subjectName", already.SubjectName), ("level", Level(game, already))),
            });
            return;
        }
        if (step.Run == RunState.Waiting && s_clock < step.RetryAt) return;
        step.RetryAt = s_clock + RetrySeconds;

        var target = new Target(step.TargetId, step.Name, step.Extra);
        bool earlier = MoreComing(game, step, action, target);
        string problem;
        if (earlier)
        {
            // An earlier step, or something running beside, may yet provide what this one lacks: wait instead of giving up.
            if (!Targets.CanStart(action, target, out problem)) { Wait(step, Lang.F("state.waiting_because", "waiting: {why}", ("why", problem))); return; }
            long batches = Targets.Batches(game, action, target);
            if (batches > step.Pile) step.GrewAt = s_clock;
            step.Pile = batches;
            if (batches == 0) { Wait(step, WaitingText(step)); return; }
            // Starting for every single fish would have the game report "action stopped" each time: work in piles.
            if (batches < PileSize && s_clock - step.GrewAt < SettleSeconds) { Wait(step, Lang.F("queue.waiting_collecting_materials", "waiting: collecting materials ({batches}/{pileSize})", ("batches", batches), ("pileSize", PileSize))); return; }
        }

        bool othersAtWork = Steps.Any(s => s != step && s.Run == RunState.Running);
        // Offline next to a fight, with fights waiting: starting in its place would end the fight on the game's thread.
        // Without a free Multitask slot the step waits until the game is back.
        if (FightKeptOffline(game) && !(Capacity() > 1 && FreeSlots(game) > 0))
        {
            Wait(step, Lang.T("queue.waits_game_open_fight_running", "waits for the game to be open (a fight is running)"));
            return;
        }
        // With Multitask's room, what the player keeps running in another skill (a fight beside the queue's smithing)
        // stays running: starting in place of everything would stop it, and Multitask would start it again from the
        // beginning (a fight against a new monster). Without a free slot it is the plain game: the step takes over.
        if (!othersAtWork && Capacity() > 1 && (FreeSlots(game) > 0 || Locked() > 0)
            && game.ActionManager.ActiveActions.Any(a => a.Entity.Id != step.Entity && (!(s_idleOwned && Idle?.Entity == a.Entity.Id) || HandsOff(a))))
        {
            othersAtWork = true;
            if (action.IsStarted) StopAction(action);   // the step's own skill does one thing at a time
        }
        MakeRoomFor(game, step, othersAtWork);
        // The step's own choice of the recipe's other way of making (the Sacrificial Anvil switch), as the game's switch sets it.
        if (step.Mode != null && !Targets.SetMode(action, target.Id, step.Mode))
            Trace($"{Number(step)}: the way of making '{step.Mode}' could not be switched (locked, or not for this recipe)");
        bool started = othersAtWork ?Targets.StartBeside(game, action, target, out problem) : Targets.Start(game, action, target, out problem);
        if (!started)
        {
            if (earlier) Wait(step, Lang.F("state.waiting_because", "waiting: {why}", ("why", problem)));
            else if (step.Ran) End(step, StepStatus.Done, Lang.T("queue.done_stopped_by_itself", "done (it stopped by itself)"));
            else End(step, StepStatus.Skipped, Lang.F("state.skipped_because", "skipped: {why}", ("why", problem)));
            return;
        }
        if (!step.Ran)
        {
            step.Seconds = step.SecondsBefore;   // (0, or what it had run before a pause)
            MarkStart(game, step);
            step.CountGains = Lanes > 1;
        }
        step.Ran = true;
        step.Run = RunState.Running;
        step.Following = Targets.Current(action)?.Id ?? "";
        Say(step, StepStatus.Running, Lang.T("state.running", "running"));
        s_ranThisRound = true;
        s_workedThisRound = true;
        s_laneRan.Add(LaneOf(step));
        s_died = false;
        Trace($"{Number(step)}: started{(othersAtWork ? " beside the others" : "")}{(step.Until is Until.Item or Until.ItemLeft or Until.Made ? $", bank holds {Owned(game, step):N0} {step.SubjectName}" : "")}{(step.Until is Until.Made or Until.Times or Until.Spent && step.CountBefore > 0 ? $", {step.CountBefore:N0} counted before the pause" : "")}");
    }

    /// <summary>
    /// A sell step: once what comes before it is over, sells all of the item but the number to keep, through the game's
    /// own sale code (like the bank's Sell button). A stack the player has locked in the bank is never sold.
    /// </summary>
    static void Sell(Game game, Step step)
    {
        if (EarlierAtWork(step)) { Wait(step, Lang.T("queue.waiting_steps_before_finish", "waiting: for the steps before it to finish")); return; }
        string name = step.SubjectName != "" ? step.SubjectName : step.Name;
        if (Bank(game) is not { } bank) { End(step, StepStatus.Skipped, Lang.T("queue.skipped_item_not_game", "skipped: this item is not in the game")); return; }
        if (ItemOf(game, step.TargetId) is not { } item) { End(step, StepStatus.Skipped, NoItem(step.TargetId)); return; }
        var stacks = bank.Inventory.Stacks.Where(st => st.Item == item && !st.IsPlaceholder && st.Quantity > 0).ToList();
        if (stacks.Any(st => st.IsLocked)) { End(step, StepStatus.Skipped, Lang.F("queue.skipped_locked_your_bank", "skipped: {name} is locked in your bank", ("name", name))); return; }
        long owned = stacks.Sum(st => (long)st.Quantity), keep = (long)Math.Max(0, Math.Floor(step.Amount));
        // A rare drop that was sold stays in the game until it restarts: it is gone all the same.
        if (owned == 0 && Rares.Is(item) && !Rares.Owned(game, item)) { End(step, StepStatus.Skipped, NoItem(step.TargetId)); return; }
        if (owned <= keep) { End(step, StepStatus.Done, Lang.F("queue.done_nothing_sell_bank_holds", "done: nothing to sell (the bank holds {owned:N0})", ("owned", owned))); return; }
        long sold = 0, paid = 0;
        string currency = Lang.T("sell.currency_gold", "gold");
        void OnSold(object? _, ItemSoldEventArgs e) { sold += e.Quantity; paid += e.CurrencyGain.Quantity; currency = e.CurrencyGain.Currency.DisplayName.Text; }
        bank.ItemSales.ItemSold += OnSold;
        try
        {
            ItemSaleComponentStats stats = bank.ItemSales.GetItemStats(item);
            try { bank.ItemSales.SellItem(item, stats, (int)Math.Min(int.MaxValue, owned - keep)); }
            finally { stats.Destroy(); }
        }
        finally { bank.ItemSales.ItemSold -= OnSold; }
        if (sold > 0) { step.Ran = true; s_ranThisRound = true; s_workedThisRound = true; s_laneRan.Add(LaneOf(step)); }
        End(step, StepStatus.Done, sold > 0 ? Lang.F("queue.done_sold", "done: sold {sold:N0} for {paid:N0} {currency}", ("sold", sold), ("paid", paid), ("currency", currency)) : Lang.T("queue.done_game_sold_nothing", "done: the game sold nothing"));
    }

    /// <summary>
    /// An "equip" step: once what comes before it is over, puts an item from the bank on (through the game's own
    /// equip code, as the bank's Equip button does) or switches to one of the game's loadouts. A stack (arrows,
    /// bolts...) goes on whole, but for the number to keep in the bank.
    /// </summary>
    static void Equip(Game game, Step step)
    {
        if (EarlierAtWork(step)) { Wait(step, Lang.T("queue.waiting_steps_before_finish", "waiting: for the steps before it to finish")); return; }
        // Gear (and a loadout's food, prayers and spells) is only changed while the game is open (fights go on offline
        // since 0.16.0; gear changes were never tried there).
        if (!Targets.OnMainThread)
        {
            string later = step.IsFood ? Lang.T("queue.waits_game_open_food", "waits for the game to be open (food)") : Lang.T("queue.waits_game_open_equipment", "waits for the game to be open (equipment)");
            if (step.State != later) { step.Run = RunState.Waiting; Say(step, StepStatus.Waiting, later); Trace($"{Number(step)}: {later}"); }
            return;
        }
        if (step.IsFood) { EquipFood(game, step); return; }
        if (!game.Entities.TryGetObjectById(CombatId, out GameEntity? e) || e is not CombatEntity combat) { End(step, StepStatus.Skipped, Lang.T("queue.skipped_game_has_no_equipment", "skipped: this game has no equipment")); return; }
        if (step.TargetId.StartsWith(Step.LoadoutPrefix, StringComparison.Ordinal))
        {
            string id = step.TargetId[Step.LoadoutPrefix.Length..];
            var loadout = combat.Loadouts.ActiveLoadouts.Values.FirstOrDefault(l => l.Data.Id == id);
            if (loadout == null) { End(step, StepStatus.Skipped, Lang.T("queue.skipped_loadout_not_game", "skipped: this loadout is not in the game")); return; }
            if (!loadout.IsUnlocked) { End(step, StepStatus.Skipped, Lang.F("queue.skipped_loadout_locked", "skipped: the loadout '{name}' is locked", ("name", loadout.Name))); return; }
            if (combat.Loadouts.CurrentLoadout == loadout) { End(step, StepStatus.Done, Lang.F("queue.done_already_use", "done: '{name}' is already in use", ("name", loadout.Name))); return; }
            combat.Loadouts.SwapToLoadout(loadout.Data);
            bool swapped = combat.Loadouts.CurrentLoadout == loadout;
            if (swapped) { step.Ran = true; s_ranThisRound = true; s_laneRan.Add(LaneOf(step)); }
            if (swapped) End(step, StepStatus.Done, Lang.F("queue.done_switched", "done: switched to '{name}'", ("name", loadout.Name)));
            else End(step, StepStatus.Skipped, Lang.T("queue.skipped_game_did_not_switch", "skipped: the game did not switch the loadout"));
            return;
        }
        if (ItemOf(game, step.TargetId) is not { } item) { End(step, StepStatus.Skipped, NoItem(step.TargetId)); return; }
        string name = item.DisplayName.GetPluralText(2);
        if (!item.Capabilities.TryGet<GamesByMalcs.Melvor2.Components.Equipment.EquipmentCapability>(out var equip)) { End(step, StepStatus.Skipped, Lang.F("queue.skipped_cannot_equipped", "skipped: {name} cannot be equipped", ("name", name))); return; }
        int have = Bank(game)?.Inventory.GetQuantity(item) ?? 0;
        bool stack = equip.CanEquipQuantity;
        if (!stack && combat.Equipment.IsItemEquipped(item)) { End(step, StepStatus.Done, Lang.F("queue.done_already_equipped", "done: {item} is already equipped", ("item", item.DisplayName.GetPluralText(1)))); return; }
        long keep = (long)Math.Max(0, Math.Floor(step.Amount));
        int quantity = stack ? (int)Math.Max(0, have - keep) : Math.Min(1, have);
        if (quantity <= 0 && have <= 0 && Rares.Is(item)) { End(step, StepStatus.Skipped, NoItem(step.TargetId)); return; }   // (sold, but still in the game until it restarts)
        if (quantity <= 0 && have <= 0) { End(step, StepStatus.Skipped, Lang.F("queue.skipped_no_bank", "skipped: no {name} in the bank", ("name", name))); return; }
        if (quantity <= 0) { End(step, StepStatus.Done, Lang.F("queue.done_nothing_equip_bank_holds", "done: nothing to equip (the bank holds {have:N0}, {keep:N0} are kept)", ("have", have), ("keep", keep))); return; }
        if (!equip.Requirements.AreMet) { End(step, StepStatus.Skipped, Lang.F("queue.skipped_cannot_equipped_yet", "skipped: {name} cannot be equipped yet ({missing})", ("name", name), ("missing", Targets.Missing(equip.Requirements)))); return; }
        var slots = combat.Equipment.GetAvailableSlotsForItem(item);
        if (slots.Count == 0) { End(step, StepStatus.Skipped, Lang.F("queue.skipped_there_no_free_slot", "skipped: there is no free slot for {name}", ("name", name))); return; }
        // A stack that is already on is topped up in its own slot; otherwise an empty slot of its kind first (a second ring).
        var slot = slots.FirstOrDefault(a => !a.Slot.IsEmpty && a.Slot.Equipped.Item == item) ?? slots.FirstOrDefault(a => a.Slot.IsEmpty) ?? slots[0];
        int before = combat.Equipment.GetEquippedQuantity(item);
        combat.ItemEquipping.EquipItem(item, quantity, slot.Slot.Slot);
        int after = combat.Equipment.GetEquippedQuantity(item);
        if (after > before) { step.Ran = true; s_ranThisRound = true; s_laneRan.Add(LaneOf(step)); }
        if (after > before) End(step, StepStatus.Done, stack ? Lang.F("queue.done_equipped_now", "done: equipped {added:N0} {name} ({after:N0} on now)", ("added", after - before), ("name", name), ("after", after)) : Lang.F("queue.done_equipped", "done: equipped {item}", ("item", item.DisplayName.GetPluralText(1))));
        else End(step, StepStatus.Skipped, Lang.F("queue.skipped_game_did_not_equip", "skipped: the game did not equip {name}", ("name", name)));
    }

    // ---------------------------------------------------------------- food (0.13.0, by madmousepad)

    /// <summary>The food slot of a skill (Combat, Thieving, Agility...): what the game eats from while that skill runs (null: it eats nothing).</summary>
    static FoodComponent? FoodSlot(Game game, string entityId)
    {
        try { return game.Entities.TryGetObjectById(entityId, out GameEntity? e) && e != null ? e.Components.Values.OfType<FoodComponent>().FirstOrDefault() : null; }
        catch (Exception) { return null; }
    }

    /// <summary>The skills that eat food (each has its own food slot), as the game's bank lists them: (entity id, name).</summary>
    public static List<(string Id, string Name)> FoodSkills(Game game)
    {
        try { return Bank(game)?.FoodComponents.Select(f => (f.Entity.Id, f.Entity.DisplayName.Text)).Distinct().ToList() ?? new(); }
        catch (Exception) { return new(); }
    }

    /// <summary>How much of the food a skill's slot has selected is left in the bank (0 with none selected; null: no food slot).</summary>
    static int? FoodLeft(Game game, Step step)
    {
        if (FoodSlot(game, step.Entity) is not { } slot) return null;
        return slot.SelectedFood?.Item is { } item ? slot.Inventory.GetQuantity(item) : 0;
    }

    /// <summary>"12 Shrimp left" / "no food selected", for the State column.</summary>
    static string FoodText(Game game, Step step)
    {
        if (FoodSlot(game, step.Entity) is not { } slot) return Lang.T("queue.no_food_slot", "no food slot");
        if (slot.SelectedFood?.Item is not { } item) return Lang.T("queue.no_food_selected", "no food selected");
        int left = slot.Inventory.GetQuantity(item);
        return Lang.F("queue.left", "{left:N0} {item} left", ("left", left), ("item", item.DisplayName.GetPluralText(left)));
    }

    /// <summary>
    /// An Equip step for food: once what comes before it is over, makes this food the one the step's skill eats, as the
    /// skill's own food box does (Thieving's, Agility's, the fight's). Only while the game is open, like other gear.
    /// </summary>
    static void EquipFood(Game game, Step step)
    {
        if (ItemOf(game, step.TargetId) is not { } item) { End(step, StepStatus.Skipped, NoItem(step.TargetId)); return; }
        string name = item.DisplayName.GetPluralText(2), skill = step.SubjectName != "" ? step.SubjectName : step.Extra;
        if (!item.Capabilities.TryGet<FoodCapability>(out _)) { End(step, StepStatus.Skipped, Lang.F("queue.skipped_not_food", "skipped: {name} are not food", ("name", name))); return; }
        if (FoodSlot(game, step.Extra) is not { } slot) { End(step, StepStatus.Skipped, Lang.F("queue.skipped_eats_no_food_game", "skipped: {skill} eats no food in this game", ("skill", skill))); return; }
        int have = slot.Inventory.GetQuantity(item);
        if (slot.SelectedFood?.Item == item) { End(step, StepStatus.Done, Lang.F("queue.done_already_eats_bank", "done: {skill} already eats {name} ({have:N0} in the bank)", ("skill", skill), ("name", name), ("have", have))); return; }
        if (have <= 0) { End(step, StepStatus.Skipped, Lang.F("queue.skipped_no_bank", "skipped: no {name} in the bank", ("name", name))); return; }
        slot.SelectFood(item);
        bool selected = slot.SelectedFood?.Item == item;
        if (selected) { step.Ran = true; s_ranThisRound = true; s_laneRan.Add(LaneOf(step)); }
        if (selected) End(step, StepStatus.Done, Lang.F("queue.done_now_eats_bank", "done: {skill} now eats {name} ({have:N0} in the bank)", ("skill", skill), ("name", name), ("have", have)));
        else End(step, StepStatus.Skipped, Lang.F("queue.skipped_game_did_not_give", "skipped: the game did not give {skill} the {name}", ("skill", skill), ("name", name)));
    }

    // ---------------------------------------------------------------- "run from here" (0.13.0, by madmousepad)

    /// <summary>
    /// The queue goes on from this step. Steps before it that are not over yet are skipped this round (a running one is
    /// stopped); the step itself and the ones after it start afresh, also when they were done. In lanes only the step's
    /// own lane jumps: the other lanes go on with what they do. A queue that is not running starts from this step.
    /// </summary>
    public static bool RunFrom(int index)
    {
        if (s_game == null || index < 0 || index >= Steps.Count || !Targets.OnMainThread) return false;
        Step target = Steps[index];
        bool laneMode = LaneMode, wasRunning = Running;
        bool paused = !wasRunning && s_resume;   // a paused queue: Start carries on with what it had done
        int lane = LaneOf(target);
        if (!wasRunning && !StopAtEnd && StopReached(s_game))
        {
            Note = Lang.F("queue.nothing_do_already_queue_stop", "Nothing to do: {stopText} already, and that is the queue's stop. Change or remove the stop to run the queue.", ("stopText", StopText(StopWhen!)));
            RaiseChanged();
            return false;
        }
        for (int i = 0; i < Steps.Count; i++)
        {
            Step s = Steps[i];
            bool mine = !laneMode || LaneOf(s) == lane;
            // A finished (or new) queue starts a new run: nothing of the last one counts as done in it, or is shown as "last time".
            if (!wasRunning && !paused) { s.Ran = false; s.LastResult = ""; s.LastSeconds = 0; }
            if (!mine)
            {
                // Not running: the other lanes of a paused queue carry on where they were (Start resumes them); those of
                // a finished or new one start from their beginning, as with Start.
                if (!wasRunning && !paused) { s.Run = RunState.Idle; Say(s, StepStatus.None, ""); }
                continue;
            }
            if (i < index && s.Run == RunState.Done && (wasRunning || paused)) continue;   // over already this round
            StopOwn(s);
            StopPausedAction(s);
            s_cannotAttack.Remove(s);
            if (i < index)
            {
                if (s.Run != RunState.Done || !wasRunning) Say(s, StepStatus.Skipped, Lang.F("queue.skipped_queue_went_from_step", "skipped: the queue went on from step {number}", ("number", index + 1)));
                s.Run = RunState.Done;
            }
            else
            {
                s.Run = RunState.Idle;
                Say(s, StepStatus.None, "");
                s.Ran = false;
                ClearCounts(s);
            }
            s.Seconds = s.RetryAt = s.GrewAt = 0;
            s.Pile = 0;
            s.Following = "";
        }
        Trace($"run from {Number(target)}{(wasRunning ? "" : " (the queue was not running)")}");
        if (!wasRunning)
        {
            // Start carries on from here: what is marked done stays done.
            s_resume = true;
            Start();
            if (!Running) return false;
            Note = Lang.F("queue.started_from_step", "Started from step {number}: {name}.", ("number", index + 1), ("name", ShownName(target.Name)));
            RaiseChanged();
            return true;
        }
        s_laneRan.Clear();
        foreach (Step s in Steps.Where(s => s.Ran)) s_laneRan.Add(LaneOf(s));
        s_ranThisRound = Steps.Any(s => s.Ran);
        s_workedThisRound = s_ranThisRound;
        s_emptyVisited.Clear();   // the player chose where the queue goes on: a fresh start for queues going round
        SetIndex();
        Note = Lang.F("queue.went_from_step", "Went on from step {number}: {name}.", ("number", index + 1), ("name", ShownName(target.Name)));
        Publish();
        RaiseChanged();
        return true;
    }

    /// <summary>
    /// A step the queue was at when it was paused: Pause leaves the character doing its action. When the queue jumps
    /// ("Run from here"), that action is stopped like a running step's, so a skipped fight does not go on by itself.
    /// </summary>
    static void StopPausedAction(Step s)
    {
        // (only a step that was running: a waiting one never started its action, what runs in its skill is somebody else's)
        if (s_game == null || Running || s.Status != StepStatus.Paused || s.Run != RunState.Running) return;
        if (Targets.Find(s_game, s.Entity) is { } action && StepDoing(action, s)) StopAction(action);
    }

    /// <summary>The saved queue a "go to a saved queue" step chose this round (carried out after the round, see Tick).</summary>
    static string? s_switchTo;
    /// <summary>
    /// The saved queues the queue has left without doing anything since something was last done (by name; "" for steps that
    /// were never saved). Going to one of them again means the queues only go round: then the queue stops. Until 0.13.0 it
    /// stopped after three such switches in a row, which also stopped chains that skip a few queues locked for now
    /// (reported on Discord).
    /// </summary>
    static readonly HashSet<string> s_emptyVisited = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The "go to a saved queue" step that chose <see cref="s_switchTo"/>.</summary>
    static Step? s_switchFrom;

    /// <summary>
    /// A "go to a saved queue" step: once every step before it is over (in lanes: every other lane is through), the
    /// queue goes on with the saved queue it names, from that queue's first step.
    /// </summary>
    static void SwitchTo(Game game, Step step)
    {
        if (EarlierAtWork(step)) { Wait(step, Lang.T("queue.waiting_steps_before_finish", "waiting: for the steps before it to finish")); return; }
        // In lanes: every other lane is through first (also a lane whose steps have not started yet).
        if (LaneMode && Steps.Any(s => LaneOf(s) != LaneOf(step) && s.Run != RunState.Done))
        {
            Wait(step, Lang.T("queue.waiting_other_lanes_finish", "waiting: for the other lanes to finish"));
            return;
        }
        if (GetPreset(step.TargetId) == null || string.Equals(step.TargetId, AutoName, StringComparison.OrdinalIgnoreCase))
        {
            End(step, StepStatus.Skipped, Lang.F("queue.skipped_there_no_saved_queue", "skipped: there is no saved queue called '{targetId}'", ("targetId", step.TargetId)));
            return;
        }
        End(step, StepStatus.Done, Lang.F("queue.done_going_with", "done: going on with '{targetId}'", ("targetId", step.TargetId)));
        s_switchTo = step.TargetId;
        s_switchFrom = step;
    }

    static void SwitchNow(string name)
    {
        // Saved queues that only send each other round (A goes to B, B goes back to A) with nothing done in between: the
        // queue stops when it comes back to a queue it already left without doing anything since the last thing done. Any
        // number of queues that do nothing for now (locked) can be passed on the way to one that works.
        if (s_workedThisRound) s_emptyVisited.Clear();
        else
        {
            s_emptyVisited.Add(Name);
            if (s_emptyVisited.Contains(name))
            {
                s_emptyVisited.Clear();
                string saved = s_presets.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) ?? name;
                Finish(string.Equals(Name, saved, StringComparison.OrdinalIgnoreCase)
                    ? Lang.F("queue.queue_stopped_nothing_could_done", "The queue stopped: nothing in '{saved}' could be done, and it goes back to itself.", ("saved", saved))
                    : Lang.F("queue.queue_stopped_came_back_with", "The queue stopped: it came back to '{saved}' with nothing done in between.", ("saved", saved)));
                return;
            }
        }
        string from = Name;
        if (!LoadPresetCore(name, out string message, byQueue: true)) { Finish(message); return; }
        Note = from == "" ? Lang.F("queue.went_on_with_saved", "the queue went on with the saved queue '{name}'.", ("name", Shown(Name)))
            : Lang.F("queue.went_on_from_saved", "'{from}' went on with the saved queue '{name}'.", ("from", Shown(from)), ("name", Shown(Name)));
        Trace($"went on with the saved queue '{Name}' ({Steps.Count} step(s)): {message}");
    }

    static void Watch(Game game, Step step, double seconds)
    {
        ActionComponent? action = Targets.Find(game, step.Entity);
        if (action == null) { End(step, StepStatus.Skipped, Lang.T("queue.skipped_skill_not_game", "skipped: this skill is not in the game")); return; }
        // Fights waiting offline: a fight step is looked at again when the game is back; the time it fights meanwhile still counts (Time column, "for N minutes").
        if (HandsOff(action)) { if (StepDoing(action, step)) step.Seconds += seconds; return; }
        var wanted = new Target(step.TargetId, step.Name, step.Extra);
        Target? now = Targets.Current(action);
        if (now == null)
        {
            if (s_died && step.Entity == CombatId) { s_died = false; Pause(Lang.T("queue.your_character_died_so_queue", "Your character died, so the queue is paused.")); return; }
            // It stopped just as the goal was reached (the last materials made the last of it): that is a finish.
            if (Reached(game, step)) { End(step, StepStatus.Done, DoneText(game, step)); return; }
            // Stopped with the game's own Stop button: the player is done with this step. It leaves the queue
            // (with "start over at the end" on it is only skipped this round) and the queue goes on.
            if (TakeHandStop(step.Entity)) { StoppedByPlayer(step); return; }
            // With Multitask's room (0.14.0): the game stopped this step to start an action of the player's in another
            // skill (it stops everything when an action starts; Multitask leaves the queue's steps to the queue). When
            // there is room for both, the step goes on beside it instead of the whole queue pausing: a fight started by
            // hand again after a death, next to the queue's runecrafting.
            if (Targets.OnMainThread && TakeBumped(step.Entity) && Capacity() > 1)
            {
                ActionComponent? theirs = game.ActionManager.ActiveActions.LastOrDefault(a => a.Entity.Id != step.Entity
                    && !Steps.Any(s => s.Run == RunState.Running && s.Entity == a.Entity.Id) && !(s_idleOwned && Idle?.Entity == a.Entity.Id));
                if (theirs != null && RoomBeside(game) >= 0 && Targets.StartBeside(game, action, wanted, out _))
                {
                    Trace($"{Number(step)}: the game stopped it to start {theirs.Entity.DisplayName.Text} (yours); there is room for both, so it goes on beside it");
                    return;
                }
            }
            // Did the player start something else? Actions kept running by Multitask, and the queue's own
            // other steps, do not count.
            ActionComponent? main = Targets.Main(game);
            bool idleOfOurs = main != null && s_idleOwned && Idle != null && main.Entity.Id == Idle.Entity && Targets.IsDoing(main, IdleTarget(Idle));
            // (Only while the game is open: while it computes the time it was closed nobody starts anything by hand,
            // and right after a restart Multitask has not said yet which of the running actions are its own.)
            if (Targets.OnMainThread && main != null && !idleOfOurs && !Steps.Any(s => s.Run == RunState.Running && s.Entity == main.Entity.Id))
            {
                Pause(Lang.T("queue.you_started_something_else_so", "You started something else, so the queue is paused."));
                return;
            }
            // A fight the game ended because the player could not attack any more (out of ammo, out of runes): say why.
            if (action is CombatActionComponent && CannotAttack(game) is { Length: > 0 } cannot) { End(step, StepStatus.Done, Lang.F("state.done_because", "done: {why}", ("why", cannot))); return; }
            // It stopped by itself. Out of materials while an earlier step, or something running beside it, is
            // still making them: wait for more instead of calling the step done.
            if (Targets.Batches(game, action, wanted) == 0 && MoreComing(game, step, action, wanted))
            {
                step.Pile = 0;
                step.GrewAt = s_clock;
                Wait(step, WaitingText(step));
                return;
            }
            End(step, StepStatus.Done, action is HuntingActionComponent && Targets.Batches(game, action, wanted) == 0
                ? Lang.T("queue.done_out_traps_stopped_by", "done: out of traps (it stopped by itself)") : Lang.T("queue.done_stopped_by_itself", "done (it stopped by itself)"));
            return;
        }
        if (!Targets.IsDoing(action, wanted))
        {
            // A slayer step follows its master: the task was completed, so walk to the new task monster.
            if (step.Extra == Targets.Slayer && now.Id == step.Following)
            {
                // Next to the queue's other steps, or next to what the player keeps running with Multitask's room: the
                // new task's monster is taken beside them (a plain start stops everything, and offline Multitask cannot
                // start the player's action again).
                bool others = Steps.Any(s => s != step && s.Run == RunState.Running)
                    || Capacity() > 1 && game.ActionManager.ActiveActions.Any(a => a.Entity.Id != step.Entity);
                if (others ? Targets.StartBeside(game, action, wanted, out string problem) : Targets.Start(game, action, wanted, out problem))
                    step.Following = Targets.Current(action)?.Id ?? "";
                else End(step, StepStatus.Done, Lang.F("state.done_because", "done: {why}", ("why", problem)));
                return;
            }
            // Offline it is not the player: the step is looked at again when the game is back.
            if (Targets.OnMainThread) Pause(Lang.T("queue.you_changed_what_skill_doing", "You changed what this skill is doing, so the queue is paused."));
            return;
        }
        step.Following = now.Id;

        step.Seconds += seconds;
        if (Reached(game, step))
        {
            // Only this step's own action is stopped: everything else that runs goes on.
            string done = DoneText(game, step);   // (before the stop: the count it read is the one that ended it)
            if (!action.TryStop()) action.TryStop(force: true);
            End(step, StepStatus.Done, done);
            return;
        }
        // A fight the player can no longer take part in (no ammo left for the weapon, not enough runes for the spell)
        // does not end by itself in the game: the monster keeps hitting while the player stands there. After two
        // seconds of it (not a moment's gear change) the step ends and the queue goes on.
        if (action is CombatActionComponent && CannotAttack(game) is { Length: > 0 } why)
        {
            double since = s_cannotAttack.GetValueOrDefault(step) + seconds;
            s_cannotAttack[step] = since;
            if (since >= 2)
            {
                s_cannotAttack.Remove(step);
                if (!action.TryStop()) action.TryStop(force: true);
                End(step, StepStatus.Done, Lang.F("state.done_because", "done: {why}", ("why", why)));
                return;
            }
        }
        else s_cannotAttack.Remove(step);
        string progress = Progress(game, step);
        // A paused fight (the game's pause button, or a boss that waits for the player): it says so. While the game is
        // closed nobody unpauses it, so a kill goal waits for the player; time still counts, as with the game open.
        if (action is CombatActionComponent { IsPaused: true })
            Say(step, StepStatus.Running, Lang.F("state.running_fight_paused", "running  {progress} (the fight is paused)", ("progress", progress)), progress);
        else Say(step, StepStatus.Running, Lang.F("state.running_progress", "running  {progress}", ("progress", progress)), progress);
    }

    static readonly Dictionary<Step, double> s_cannotAttack = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Why the player cannot attack in a fight right now, as the game itself decides it (PlayerComponent.CanAttack): the
    /// weapon needs ammo and none of its kind is equipped, or the stance casts spells and no spell is picked or the
    /// bank lacks its runes. "" when the player can attack.
    /// </summary>
    public static string CannotAttack(Game game)
    {
        if (!game.Entities.TryGetObjectById(CombatId, out GameEntity? e) || e is not CombatEntity combat) return "";
        try
        {
            var weapon = combat.Equipment.CurrentWeapon.Weapon;
            if (weapon.RequiredAmmoType is { } ammo && !combat.Equipment.IsAmmoEquipped(ammo)) return Lang.T("queue.out_ammo", "out of ammo");
            if (combat.Player.GetSelectedStance(weapon.Type).CombatStyle.UsesSpells)
            {
                var spell = combat.MagicSpells.SelectedSpell;
                if (spell == null) return Lang.T("queue.no_spell_picked", "no spell picked");
                if (spell.RuneStats.RuneCosts.Values.Any(v => combat.MagicSpells.Inventory.GetQuantity(v.Value.Item) < v.Value.Quantity))
                    return Lang.T("queue.out_runes", "out of runes");
            }
        }
        catch { }   // (a game version that reads differently: the fight goes on as before)
        return "";
    }

    /// <summary>
    /// The player stopped a running step with the game's own Stop button: they are done with it, so it is taken out of
    /// the queue (a saved queue keeps it) and the queue goes on with the next step.
    /// </summary>
    static void StoppedByPlayer(Step step)
    {
        bool othersLeft = Steps.Any(s => s != step && s.Run != RunState.Done);
        Trace($"{Number(step)}: stopped with the game's button, taken out of the queue");
        Steps.Remove(step);
        Save();
        string told = Lang.F("queue.you_stopped_so_was_taken", "You stopped '{name}', so it was taken out of the queue.", ("name", Shown(step)));
        if (othersLeft || Repeat && Steps.Count > 0) { Note = told + Lang.T("queue.queue_goes", " The queue goes on."); return; }
        // That was the last thing to do: the queue ends here, and the idle action does not jump in right away.
        Finish(told + Lang.T("queue.nothing_left_do", " Nothing is left to do."));
        s_idleBlocked = true;
        IdleSays(Lang.T("queue.not_started_you_stopped_last", "not started: you stopped the last step"));
    }

    static BankEntity? Bank(Game game) => game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) ? e as BankEntity : null;

    // Every count below exists for a step's own goal and for its second one ("or until ..."): g is the goal, the step
    // gives the skill, the constellation and whether it has run. The one-argument forms are the step's own goal.

    /// <summary>The step's goals: its own, and the second one when it has one.</summary>
    static IEnumerable<IGoal> Goals(Step step) => step.Others.Cast<IGoal>().Prepend(step);

    static int Owned(Game game, Step step) => Owned(game, (IGoal)step);
    static int Owned(Game game, IGoal g) =>
        ItemOf(game, g.Subject) is { } item ? Bank(game)?.Inventory.GetQuantity(item) ?? 0 : 0;

    static long Gained(IGoal g) { lock (s_gained) return s_gained.GetValueOrDefault(g.Subject); }

    static long SpentOf(Step step, IGoal g) { lock (s_gained) return s_spent.GetValueOrDefault($"{step.Entity}|{g.Subject}"); }
    static long SpentOf(Step step) => SpentOf(step, step);

    /// <summary>"Use up N": how many of the item the step's skill has used since the step first started (pauses included).</summary>
    static long Spent(Step step, IGoal g) => g.CountBefore + (step.Ran ? SpentOf(step, g) - g.SpentAtStart : 0);

    /// <summary>
    /// What counts for "until I own N". One step at a time: what the bank holds. Side by side: what the bank
    /// held at the start plus what came in since - another step may be using the item up as fast as it comes.
    /// </summary>
    static long Counted(Game game, Step step) => Counted(game, step, step);
    static long Counted(Game game, Step step, IGoal g) =>
        step.CountGains && step.Ran ? Math.Max(Owned(game, g), g.OwnedAtStart + (Gained(g) - g.GainedAtStart)) : Owned(game, g);

    /// <summary>"Make N more": how many of the item came into the bank since the step first started (pauses included).</summary>
    static long Made(Step step, IGoal g) => g.CountBefore + (step.Ran ? Gained(g) - g.GainedAtStart : 0);

    /// <summary>"N times": how many actions the step's skill has finished since the step first started (pauses included).</summary>
    static long Times(Step step, IGoal g) => g.CountBefore + (step.Ran ? DoneBy(step.Entity) - g.DoneAtStart : 0);

    /// <summary>"Until an astrology event is seen N times": how often it came up in the step's constellation since the step first started.</summary>
    static long Seen(Step step, IGoal g) => g.CountBefore + (step.Ran ? SeenOf(step, g) - g.SeenAtStart : 0);

    static long SeenOf(Step step, IGoal g) { lock (s_seen) return s_seen.GetValueOrDefault($"{step.TargetId}|{g.Subject}"); }

    /// <summary>What a counting goal has counted so far (0 for the goals that read a number off the game).</summary>
    static long Counter(Step step) => Counter(step, step);
    static long Counter(Step step, IGoal g) => g.Until switch
    {
        Until.Made => Made(step, g), Until.Times => Times(step, g), Until.Spent => Spent(step, g), Until.Event => Seen(step, g), _ => 0,
    };

    /// <summary>The step (re)starts counting now: every goal takes the game's numbers of this moment as its start.</summary>
    static void MarkStart(Game game, Step step)
    {
        foreach (IGoal g in Goals(step))
        {
            g.OwnedAtStart = Owned(game, g);
            g.GainedAtStart = Gained(g);
            g.DoneAtStart = DoneBy(step.Entity);
            g.SpentAtStart = SpentOf(step, g);
            g.SeenAtStart = SeenOf(step, g);
        }
    }

    /// <summary>Keeps what the counting goals have counted (a pause, or the game saving a snapshot).</summary>
    static void KeepCounts(Step step) { foreach (IGoal g in Goals(step)) g.CountBefore = Counter(step, g); step.SecondsBefore = step.Seconds; }

    /// <summary>Counting starts from nothing (a fresh start, or a new round of "start over").</summary>
    static void ClearCounts(Step step) { foreach (IGoal g in Goals(step)) g.CountBefore = 0; step.SecondsBefore = 0; }

    /// <summary>How many node unlocks the step's constellation has earned (minor or major, the game's own numbers).</summary>
    /// <summary>The most node unlocks a constellation has (minor, or major), so a goal above it never waits for ever.</summary>
    public static int MostNodes(Game game, string constellationId, bool major)
    {
        if (Targets.Find(game, "MelvorBase:Astrology") is not AstrologyActionComponent astrology) return 0;
        AstrologyConstellation? c = astrology.ActiveConstellations.Keys.FirstOrDefault(k => k.Id == constellationId);
        if (c == null) return 0;
        int minor = c.MinorNodeUnlockKnowledge.Count;
        return !major ? minor : c.MinorNodesPerMajorNode > 0 ? Math.Min(c.MajorNodes.Count, minor / c.MinorNodesPerMajorNode) : 0;
    }

    static int Nodes(Game game, Step step, IGoal g)
    {
        if (Targets.Find(game, step.Entity) is not AstrologyActionComponent astrology) return 0;
        ActiveAstrologyConstellation? c = astrology.ActiveConstellations.Values.FirstOrDefault(a => a.Constellation.Id == step.TargetId);
        if (c == null) return 0;
        // Major: the unlocks the earned minor ones are worth (the game's own count only grows as minor nodes are
        // clicked, which never happens while the character studies alone).
        if (g.Subject == "major") return c.Constellation.MinorNodesPerMajorNode > 0 ? c.AvailableMinorNodeCount / c.Constellation.MinorNodesPerMajorNode : 0;
        return c.AvailableMinorNodeCount;
    }

    /// <summary>
    /// The events a constellation can show while it is studied, for the event list of a step: its own events, and the
    /// events inside its event tables (a table itself is never shown; only what it rolls is). Each once, by name.
    /// </summary>
    public static List<(string Id, string Name)> AstrologyEvents(Game game, string constellationId)
    {
        if (Targets.Find(game, "MelvorBase:Astrology") is not AstrologyActionComponent astrology) return new();
        AstrologyConstellation? c = astrology.ActiveConstellations.Keys.FirstOrDefault(k => k.Id == constellationId);
        // One entry per name (the goal counts every event of that name); the name is also what the goal stores.
        return c == null ? new() : EventsOf(c).Select(e => e.Name).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Select(n => (n, n)).ToList();
    }

    /// <summary>Every event a constellation can show (its own, and those inside its event tables), with its name.</summary>
    static List<(string Id, string Name)> EventsOf(AstrologyConstellation c)
    {
        var found = new List<(string Id, string Name)>();
        var seen = new HashSet<string>();
        void Walk(AstrologyEvent e, int depth)
        {
            if (depth > 8 || !seen.Add(e.Id)) return;
            if (e is AstrologyTableEvent table) { foreach (WeightedAstrologyEvent w in table.Events) Walk(w.Event, depth + 1); return; }
            if (!e.IsMeta) found.Add((e.Id, e.DisplayName.GetPluralText(1)));
        }
        foreach (AstrologyEvent e in c.Events) Walk(e, 0);
        return found;
    }

    /// <summary>Event id -> name, worked out on the main thread (the counting can happen on the game's offline thread).</summary>
    static readonly Dictionary<string, string> s_eventNames = new();

    /// <summary>Astrology events seen, per "constellation|event", since the character was opened.</summary>
    static readonly Dictionary<string, long> s_seen = new();

    static long DoneBy(string entityId) { lock (s_done) return s_done.GetValueOrDefault(entityId); }

    static void Count(Game game, string entityId)
    {
        if (!ReferenceEquals(game, s_game)) return;
        lock (s_done) s_done[entityId] = s_done.GetValueOrDefault(entityId) + 1;
    }

    /// <summary>
    /// Listens for "one action finished", the way the game itself counts it for its own triggers: the game's event
    /// bus says so for most skills. Where that event fires for parts of an action, the whole is counted instead:
    /// artisan skills (smithing, crafting...) announce every step of a recipe, so a finished recipe counts; agility
    /// announces every obstacle, so a finished lap counts; hunting and herblore mark their parts as sub-actions.
    /// Combat has no such event: a monster killed counts. The events can come on the game's own thread while it
    /// computes offline progress, so they only add to a counter.
    /// </summary>
    static void CountActions(Game game)
    {
        lock (s_done) s_done.Clear();
        if (game.Entities.TryGetObjectById("MelvorBase:EventBus", out GameEntity? busEntity))
            foreach (EventBusComponent bus in busEntity.Components.Values.OfType<EventBusComponent>())
                bus.EventBus.ActionPerformed.Subscribe((sender, e) =>
                {
                    if (sender is not ActionComponent action || e.IsSubAction) return;
                    if (action is ArtisanActionComponent or AgilityActionComponent) return;   // counted below, as a whole
                    Count(game, action.Entity.Id);
                });
        lock (s_seen) s_seen.Clear();
        foreach ((GameEntity entity, ActionComponent action) in Targets.Skills(game))
        {
            string id = entity.Id;
            if (action is ArtisanActionComponent artisan) artisan.RecipeCreated += (_, _) => Count(game, id);
            else if (action is AgilityActionComponent agility) agility.CourseCompleted += (_, _) => Count(game, id);
            else if (action is CombatActionComponent && entity is CombatEntity combat) combat.Enemy.Killed += (_, _) => Count(game, id);
            // "Until an event is seen N times": every event the game shows for a constellation (also while closed).
            // Counted by the event's name: the game has several events of one name (one per reward size).
            if (action is AstrologyActionComponent astrology)
            {
                s_eventNames.Clear();
                foreach (AstrologyConstellation c in astrology.ActiveConstellations.Keys)
                    foreach ((string eventId, string name) in EventsOf(c)) s_eventNames[eventId] = name;
                astrology.EventObserved += (_, e) =>
                {
                    if (!ReferenceEquals(game, s_game) || e.Constellation == null) return;
                    string key = $"{e.Constellation.Id}|{s_eventNames.GetValueOrDefault(e.Event.Id, e.Event.Id)}";
                    lock (s_seen) s_seen[key] = s_seen.GetValueOrDefault(key) + 1;
                };
            }
            // "Use up N" in a fight: the runes its spells took and the arrows (bolts, javelins...) it shot.
            if (entity is CombatEntity fighter)
            {
                fighter.MagicSpells.Inventory.ItemAdded += (_, e) =>
                {
                    if (!ReferenceEquals(game, s_game) || e.Quantity >= 0 || e.Item == null) return;
                    string key = $"{id}|{e.Item.Id}";
                    lock (s_gained) s_spent[key] = s_spent.GetValueOrDefault(key) - e.Quantity;
                };
                fighter.Equipment.AmmoConsumed += (_, e) =>
                {
                    if (!ReferenceEquals(game, s_game) || e.Item == null) return;
                    string key = $"{id}|{e.Item.Id}";
                    lock (s_gained) s_spent[key] = s_spent.GetValueOrDefault(key) + 1;
                };
            }
            // "Use up N" of a food: what the skill's own food slot ate counts too (Thieving, Agility, a fight). Counted
            // from what really left the bank: the game also reports a meal that its preservation chance saved.
            foreach (FoodComponent food in entity.Components.Values.OfType<FoodComponent>())
                food.Inventory.ItemAdded += (_, e) =>
                {
                    if (!ReferenceEquals(game, s_game) || e.Quantity >= 0 || e.Item == null) return;
                    string key = $"{id}|{e.Item.Id}";
                    lock (s_gained) s_spent[key] = s_spent.GetValueOrDefault(key) - e.Quantity;
                };
        }
    }

    static int Level(Game game, Step step) => Level(game, (IGoal)step);
    static int Level(Game game, IGoal g) =>
        game.LeveledEntities.FirstOrDefault(s => s.DisplayName.Text == g.Subject)?.Experience.Level ?? 0;

    /// <summary>True when either of the step's goals is reached.</summary>
    static bool Reached(Game game, Step step) => Goals(step).Any(g => Reached(game, step, g));

    static bool Reached(Game game, Step step, IGoal g) => g.Until switch
    {
        Until.Item => Counted(game, step, g) >= g.Amount,
        Until.ItemLeft => Owned(game, g) <= g.Amount,   // counts down: food being eaten, materials being used
        Until.Level => Level(game, g) >= g.Amount,
        Until.Minutes => step.Seconds >= g.Amount * 60,
        Until.Made => Made(step, g) >= g.Amount,
        Until.Times => Times(step, g) >= g.Amount,
        Until.Spent => Spent(step, g) >= g.Amount,
        // (all the constellation has also ends it: a number above that is never reached)
        Until.Nodes => Nodes(game, step, g) >= Math.Min(g.Amount, Math.Max(1, MostNodes(game, step.TargetId, g.Subject == "major"))),
        Until.Event => Seen(step, g) >= g.Amount,
        Until.Mastery => Mastery.Find(g.Subject) is { Done: true },
        Until.FoodRunsOut => FoodLeft(game, step) == 0,   // (a skill with no food slot never runs out)
        _ => false,
    };

    /// <summary>Goals that read a number off the game (the bank, a level, a constellation): already reached before the step starts means nothing to do.</summary>
    static bool ReadsTheGame(IGoal g) => g.Until is Until.Item or Until.Level or Until.ItemLeft or Until.Nodes or Until.Mastery or Until.FoodRunsOut;

    /// <summary>"done" with the reason when the step's second goal ended it (the first one is the plain "done").</summary>
    static string DoneText(Game game, Step step) =>
        step.Until == Until.FoodRunsOut && Reached(game, step, step) ? Lang.T("queue.done_food_ran_out", "done: the food ran out")
        : !Reached(game, step, step) && step.Others.FirstOrDefault(g => Reached(game, step, g)) is { } other
            ? Lang.F("queue.done_2", "done: {progress} ({second})", ("progress", Progress(game, step, other)), ("second", other.Text)) : Lang.T("state.done", "done");

    static string Clock(double seconds) => Duration(seconds);

    /// <summary>A step's time for the list's Time column (0.13.1): "4:07", or "1:02:15" from an hour on.</summary>
    public static string Duration(double seconds)
    {
        long s = (long)Math.Max(0, seconds);
        return s < 3600 ? $"{s / 60}:{s % 60:00}" : $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
    }

    static string Progress(Game game, Step step) =>
        step.Others.Aggregate(Progress(game, step, step), (text, g) => Lang.F("queue.or", "{progress}  |  or  {progress2}", ("progress", text), ("progress2", Progress(game, step, g))));

    static string Progress(Game game, Step step, IGoal g) => g.Until switch
    {
        Until.Item => $"{Counted(game, step, g):N0} / {g.Amount:N0}" + (step.HasOthers ? $" {g.SubjectName}" : ""),
        Until.ItemLeft => step.HasOthers
            ? Lang.F("progress.item_left_named", "{owned:N0} {item} left, stops at {amount:N0}", ("owned", Owned(game, g)), ("item", g.SubjectName), ("amount", g.Amount))
            : Lang.F("progress.item_left", "{owned:N0} left, stops at {amount:N0}", ("owned", Owned(game, g)), ("amount", g.Amount)),
        Until.Level => step.HasOthers
            ? Lang.F("progress.level_named", "{skill} level {level} / {amount:0}", ("skill", g.SubjectName), ("level", Level(game, g)), ("amount", g.Amount))
            : Lang.F("progress.level", "level {level} / {amount:0}", ("level", Level(game, g)), ("amount", g.Amount)),
        Until.Minutes => $"{Clock(step.Seconds)} / {Clock(g.Amount * 60)}",
        Until.Made => step.HasOthers
            ? Lang.F("progress.made_named", "{made:N0} / {amount:N0} {item} made", ("made", Made(step, g)), ("amount", g.Amount), ("item", g.SubjectName))
            : Lang.F("progress.made", "{made:N0} / {amount:N0} made", ("made", Made(step, g)), ("amount", g.Amount)),
        Until.Times => Lang.P("progress.times", g.Amount, "{times:N0} / {count:N0} time", "{times:N0} / {count:N0} times", ("times", Times(step, g))),
        Until.Spent => step.HasOthers
            ? Lang.F("progress.spent_named", "{spent:N0} / {amount:N0} {item} used up", ("spent", Spent(step, g)), ("amount", g.Amount), ("item", g.SubjectName))
            : Lang.F("progress.spent", "{spent:N0} / {amount:N0} used up", ("spent", Spent(step, g)), ("amount", g.Amount)),
        Until.Nodes => Lang.P("progress.nodes", g.Amount, "{nodes:N0} / {count:N0} {kind} node", "{nodes:N0} / {count:N0} {kind} nodes",
            ("nodes", Nodes(game, step, g)), ("kind", Step.NodeKind(g.Subject, g.SubjectName))),
        Until.Event => $"{g.SubjectName} {Seen(step, g):N0} / {g.Amount:N0}",
        Until.Mastery => Mastery.Find(g.Subject) is { } task ? Lang.F("queue.mastery_task", "mastery task {progressText}", ("progressText", Mastery.ProgressText(task))) : Lang.T("queue.mastery_task_not_game", "mastery task not in this game"),
        Until.FoodRunsOut => FoodText(game, step),
        _ => Clock(step.Seconds),
    };

    /// <summary>One line for the little on-screen reminder.</summary>
    public static string Headline
    {
        get
        {
            if (!Running) return "";
            var atWork = Steps.Where(s => s.Run == RunState.Running).ToList();
            string meanwhile = s_idleOwned && Idle != null ? Lang.F("headline.meanwhile", "   (meanwhile: {name})", ("name", Shown(Idle))) : "";
            if (atWork.Count == 0) return Index < Steps.Count ? Lang.F("headline.one", "Queue {number}/{total}: {step}  -  {state}{meanwhile}",
                ("number", Index + 1), ("total", Steps.Count), ("step", Shown(Steps[Index])), ("state", Steps[Index].State), ("meanwhile", meanwhile)) : "";
            if (atWork.Count == 1) return Lang.F("headline.one", "Queue {number}/{total}: {step}  -  {state}{meanwhile}",
                ("number", Steps.IndexOf(atWork[0]) + 1), ("total", Steps.Count), ("step", Shown(atWork[0])), ("state", atWork[0].State), ("meanwhile", meanwhile));
            return Lang.F("headline.many", "Queue: {steps}{meanwhile}",
                ("steps", string.Join("   +   ", atWork.Select(s => $"{Shown(s)} - {(s.Detail != "" ? s.Detail : s.State)}"))), ("meanwhile", meanwhile));
        }
    }

    /// <summary>For tests: true while the idle action runs because the queue started it.</summary>
    public static bool IdleRunsForQueue => s_idleOwned;
}
