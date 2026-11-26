using System;
using System.Collections.Generic;
using System.Linq;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Components.ArtisanAction;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.CraftingAction;
using GamesByMalcs.Melvor2.Components.SlayerTasks;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Agility;
using GamesByMalcs.Melvor2.Entities.Astrology;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Entities.Cooking;
using GamesByMalcs.Melvor2.Entities.Firemaking;
using GamesByMalcs.Melvor2.Entities.Fishing;
using GamesByMalcs.Melvor2.Entities.Herblore;
using GamesByMalcs.Melvor2.Entities.Hunting;
using GamesByMalcs.Melvor2.Entities.Mining;
using GamesByMalcs.Melvor2.Entities.Slayer;
using GamesByMalcs.Melvor2.Entities.Thieving;
using GamesByMalcs.Melvor2.Entities.Woodcutting;

namespace ActionQueue;

/// <summary>One thing a skill can do: a tree, a rock, a recipe, a monster... Extra is only used by combat (its area).</summary>
public sealed record Target(string Id, string Name, string Extra = "");

/// <summary>
/// Every skill starts its work in its own way. This class knows, for each kind of action, what can be chosen,
/// what is being done right now, and how to start one particular thing - always through the game's own
/// "select" call and only when the game's own requirements for it are met.
/// </summary>
public static class Targets
{
    /// <summary>
    /// The words of this file (0.14.0). Action Queue gives them its language (Lang.F); Multitask and Phone
    /// Screen, which build this file too, keep the English. A text is a whole sentence with named places: {item}, {amount:N0}.
    /// </summary>
    public static Func<string, string, (string Name, object? Value)[], string> Say = English;

    internal static string W(string id, string en, params (string Name, object? Value)[] args) => Say(id, en, args);

    static string English(string id, string en, (string Name, object? Value)[] args)
    {
        if (args.Length == 0) return en;
        return System.Text.RegularExpressions.Regex.Replace(en, @"\{([A-Za-z_][A-Za-z0-9_]*)(:[^{}]*)?\}", m =>
        {
            foreach ((string Name, object? Value) a in args)
                if (a.Name == m.Groups[1].Value)
                    return a.Value is IFormattable f && m.Groups[2].Success ? f.ToString(m.Groups[2].Value[1..], null) : a.Value?.ToString() ?? "";
            return m.Value;
        });
    }

    public const string Dungeon = "dungeon";
    /// <summary>A combat target that is not one fixed monster but "whatever this slayer master's task is".</summary>
    public const string Slayer = "slayer";

    static SlayerEntity? s_slayer;

    static ActiveSlayerMaster? Master(string masterId) =>
        s_slayer?.Tasks.ActiveMasters.FirstOrDefault(m => m.Key.Id == masterId).Value;

    /// <summary>The skills (and combat) the queue can drive, in the game's own order.</summary>
    public static List<(GameEntity Entity, ActionComponent Action)> Skills(Game game)
    {
        var list = new List<(GameEntity, ActionComponent)>();
        s_slayer = game.Entities.TryGetObjectById("MelvorBase:Slayer", out GameEntity? slayer) ? slayer as SlayerEntity : null;
        foreach (GameEntity entity in game.Entities.GetAllObjects())
            foreach (ActionComponent action in entity.Components.Values.OfType<ActionComponent>())
                if (Supported(action)) { list.Add((entity, action)); break; }
        return list;
    }

    public static ActionComponent? Find(Game game, string entityId) =>
        Skills(game).FirstOrDefault(s => s.Entity.Id == entityId).Action;

    static bool Supported(ActionComponent a) => a is WoodcuttingActionComponent or MiningActionComponent or FishingActionComponent
        or ArtisanActionComponent or CookingActionComponent or FiremakingActionComponent or HerbloreActionComponent
        or ThievingActionComponent or AgilityActionComponent or AstrologyActionComponent or HuntingActionComponent or CombatActionComponent;

    static Target Of(IDescribedObject thing) => new(thing.Id, thing.DisplayName.Text);

    // ---------------------------------------------------------------- what can be chosen

    public static List<Target> All(ActionComponent action) => action switch
    {
        WoodcuttingActionComponent c => c.Trees.Keys.Select(Of).ToList(),
        MiningActionComponent c => c.Rocks.Keys.Select(Of).ToList(),
        FishingActionComponent c => c.Locations.Values.SelectMany(l => l.Recipes).Select(r => Of(r.Recipe)).ToList(),
        ArtisanActionComponent c => Recipes(c),
        CookingActionComponent c => Recipes(c),
        FiremakingActionComponent c => Recipes(c),
        HerbloreActionComponent c => Recipes(c),
        ThievingActionComponent c => c.ActiveRecipes.Keys.Select(Of).ToList(),
        AgilityActionComponent c => c.Courses.Keys.Select(Of).ToList(),
        AstrologyActionComponent c => c.ActiveConstellations.Keys.Select(Of).ToList(),
        HuntingActionComponent c => c.Locations.Values.SelectMany(l => l.Animals).Select(a => Of(a.Animal)).ToList(),
        CombatActionComponent c => CombatTargets(c),
        _ => new List<Target>(),
    };

    static List<Target> Recipes<TRecipe, TActive>(CraftingActionComponent<TRecipe, TActive> c)
        where TRecipe : class, ICraftingRecipe, INamespacedDataClass<TRecipe> where TActive : class, IActiveCraftingRecipe =>
        c.ActiveRecipes.Keys.Select(r => new Target(r.Id, r.DisplayName.Text)).ToList();

    static List<Target> CombatTargets(CombatActionComponent c)
    {
        var list = new List<Target>();
        if (c.Entity is not CombatEntity combat) return list;
        if (s_slayer != null)
            list.AddRange(s_slayer.Tasks.ActiveMasters.Keys.Select(m => new Target(m.Id, $"Slayer tasks of {m.DisplayName.Text}  (follows the task)", Slayer)));
        foreach (BaseCombatArea area in combat.Areas.ActiveAreas.Keys)
        {
            if (area is CombatArea normal)
                list.AddRange(normal.Enemies.Select(e => new Target(e.Id, $"{e.DisplayName.Text}  ({area.DisplayName.Text})", area.Id)));
            else if (area is Dungeon dungeon)
                list.Add(new Target(dungeon.Id, $"Dungeon: {dungeon.DisplayName.Text}", Dungeon));
        }
        return list;
    }

    // ---------------------------------------------------------------- what it makes

    /// <summary>
    /// The item this target mainly produces (the log, the ore, the bar, the most common fish...), or null when
    /// there is no single product (combat, thieving, agility, astrology). Used to fill in "until I own N of".
    /// </summary>
    public static Item? Product(ActionComponent action, Target target) => action switch
    {
        WoodcuttingActionComponent c => c.Trees.Keys.FirstOrDefault(r => r.Id == target.Id)?.Product.Item,
        MiningActionComponent c => c.Rocks.Keys.FirstOrDefault(r => r.Id == target.Id)?.Product.Item,
        FishingActionComponent c => c.Locations.Values.SelectMany(l => l.Recipes).FirstOrDefault(r => r.Recipe.Id == target.Id)
            ?.Recipe.RandomProducts.Items.MaxBy(p => p.Weight)?.Item,
        ArtisanActionComponent c => RecipeProduct(c, target.Id),
        CookingActionComponent c => RecipeProduct(c, target.Id),
        FiremakingActionComponent c => RecipeProduct(c, target.Id),
        HerbloreActionComponent c => RecipeProduct(c, target.Id),
        HuntingActionComponent c => c.Locations.Values.SelectMany(l => l.Animals).FirstOrDefault(a => a.Animal.Id == target.Id)
            ?.Animal.Products.FirstOrDefault()?.Item,
        _ => null,
    };

    static Item? RecipeProduct<TRecipe, TActive>(CraftingActionComponent<TRecipe, TActive> c, string id)
        where TRecipe : class, ICraftingRecipe, INamespacedDataClass<TRecipe> where TActive : class, IActiveCraftingRecipe
    {
        TActive? recipe = c.ActiveRecipes.FirstOrDefault(r => r.Key.Id == id).Value;
        if (recipe == null) return null;
        return recipe.Stats.Product?.Value.Item ?? recipe.Stats.Products?.Values.FirstOrDefault()?.Value.Item;
    }

    // ---------------------------------------------------------------- what is being done now

    public static Target? Current(ActionComponent action)
    {
        if (!action.IsStarted) return null;
        IDescribedObject? thing = action switch
        {
            WoodcuttingActionComponent c => c.CuttingTree?.Recipe,
            MiningActionComponent c => c.CurrentRock?.Recipe,
            FishingActionComponent c => c.CurrentRecipe?.Recipe,
            ArtisanActionComponent c => c.CreatingRecipe?.Recipe,
            CookingActionComponent c => c.CreatingRecipe?.Recipe,
            FiremakingActionComponent c => c.CreatingRecipe?.Recipe,
            HerbloreActionComponent c => c.CreatingRecipe?.Recipe,
            ThievingActionComponent c => c.CurrentRecipe == null ? null : c.ActiveRecipes.FirstOrDefault(r => r.Value == c.CurrentRecipe).Key,
            AgilityActionComponent c => c.CurrentCourse?.Course,
            AstrologyActionComponent c => c.StudiedConstellation?.Constellation,
            HuntingActionComponent c => c.CurrentAnimal?.Animal,
            _ => null,
        };
        if (thing != null) return Of(thing);
        if (action is CombatActionComponent { Entity: CombatEntity combat })
        {
            BaseCombatArea? area = combat.Areas.CurrentArea?.Area;
            if (area is Dungeon dungeon) return new Target(dungeon.Id, $"Dungeon: {dungeon.DisplayName.Text}", Dungeon);
            Enemy? enemy = combat.Areas.SelectedEnemy;
            if (area != null && enemy != null) return new Target(enemy.Id, $"{enemy.DisplayName.Text}  ({area.DisplayName.Text})", area.Id);
        }
        return null;
    }

    // ---------------------------------------------------------------- starting one thing

    /// <summary>
    /// Stops whatever is running and starts this target. Returns false, with a reason, when the game does not
    /// allow it right now (level or other requirement not met, materials missing, target no longer exists).
    /// </summary>
    /// <summary>True while <see cref="Start"/> stops everything to start its own target: the stops are not the player's doing.</summary>
    [ThreadStatic] public static bool Starting;

    public static bool Start(Game game, ActionComponent action, Target target, out string problem)
    {
        problem = "";
        if (!Allowed(action, target, out problem)) return false;
        Starting = true;
        try { game.ActionManager.TryStopActive(force: true); }
        finally { Starting = false; }
        Select(action, target);
        if (IsDoing(action, target)) return true;
        problem = target.Extra == Slayer ? Targets.W("targets.slayer_master_has_no_task", "this slayer master has no task to give right now") : Targets.W("targets.could_not_started_materials_missing", "it could not be started (materials missing?)");
        return false;
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "_activeActions")]
    static extern ref List<ActionComponent> RunningList(ActiveActionManager manager);

    /// <summary>
    /// Starts this target next to whatever is running, instead of in place of it. The game stops every running
    /// action when one starts by walking its list of running actions; that list is put aside for the moment of
    /// the start, so the game's own start code runs untouched and stops nothing. Only used when the Multitask
    /// mod has made room for more than one action.
    /// </summary>
    public static bool StartBeside(Game game, ActionComponent action, Target target, out string problem)
    {
        problem = "";
        if (!Allowed(action, target, out problem)) return false;
        List<ActionComponent> running = RunningList(game.ActionManager);
        List<ActionComponent> kept = running.ToList();
        running.Clear();
        try { Select(action, target); }
        finally
        {
            int at = 0;
            foreach (ActionComponent other in kept)
                if (other.IsStarted && !running.Contains(other)) running.Insert(at++, other);
        }
        if (IsDoing(action, target)) return true;
        problem = target.Extra == Slayer ? Targets.W("targets.slayer_master_has_no_task", "this slayer master has no task to give right now") : Targets.W("targets.could_not_started_materials_missing", "it could not be started (materials missing?)");
        return false;
    }

    /// <summary>True while the action is running and working on this target (a slayer target: on its master's current task).</summary>
    public static bool IsDoing(ActionComponent action, Target target)
    {
        Target? now = Current(action);
        if (now == null) return false;
        return target.Extra == Slayer ? Master(target.Id)?.CurrentTask?.Id == now.Id : now.Id == target.Id;
    }

    // ---------------------------------------------------------------- living beside the Multitask mod

    /// <summary>
    /// The Multitask mod keeps extra actions running beside the main one. It writes the skills it is holding
    /// right now ("id|id") under this name on the scene root, so the two mods need not know each other.
    /// </summary>
    public const string HeldMeta = "MultitaskHeld";

    /// <summary>The Action Queue writes the skill of the step it is running under this name ("" when it runs nothing).</summary>
    public const string DrivingMeta = "ActionQueueDriving";

    // ---- the notes on the scene root, also from the game's own thread
    // While the game computes offline progress (on a thread of its own) the queue may still run, but the scene tree
    // must not be touched from there: a note read there comes from what was last seen on the main thread, and a
    // note written there waits until the main thread is back.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> s_metaSeen = new();
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> s_metaPending = new();

    public static bool OnMainThread
    {
        get { try { return Godot.OS.GetThreadCallerId() == Godot.OS.GetMainThreadId(); } catch { return false; } }
    }

    /// <summary>A note on the scene root ("" when there is none); off the main thread, the last one seen.</summary>
    static string? ReadMeta(string name)
    {
        if (!OnMainThread) return s_metaPending.TryGetValue(name, out string? waiting) ? waiting : s_metaSeen.GetValueOrDefault(name);
        FlushMeta();
        try
        {
            Godot.Node root = ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root;
            string? value = root.HasMeta(name) ? root.GetMeta(name).AsString() : null;
            s_metaSeen[name] = value;
            return value;
        }
        catch { return s_metaSeen.GetValueOrDefault(name); }
    }

    static void WriteMeta(string name, string value)
    {
        s_metaSeen[name] = value;
        if (!OnMainThread) { s_metaPending[name] = value; return; }
        s_metaPending.TryRemove(name, out _);
        try { ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root.SetMeta(name, value); } catch { }
    }

    /// <summary>On the main thread: writes what waited, then reads every note afresh, for the game's own thread to use.</summary>
    public static void PrimeMeta()
    {
        if (!OnMainThread) return;
        FlushMeta();
        foreach (string name in new[] { HeldMeta, DrivingMeta, CapacityMeta, PlayerKeptMeta, LockedMeta }) ReadMeta(name);
    }

    /// <summary>Writes the notes made on the game's own thread (call on the main thread).</summary>
    public static void FlushMeta()
    {
        if (s_metaPending.IsEmpty || !OnMainThread) return;
        foreach (string name in s_metaPending.Keys.ToList())
            if (s_metaPending.TryRemove(name, out string? value))
                try { ((Godot.SceneTree)Godot.Engine.GetMainLoop()).Root.SetMeta(name, value); } catch { }
    }

    public static void PublishDriving(string entityId) => WriteMeta(DrivingMeta, entityId);

    /// <summary>The skill the Action Queue is running a step on right now, or "".</summary>
    public static string QueueDriving() => ReadMeta(DrivingMeta) ?? "";

    /// <summary>The Multitask mod writes how many actions may run at once (1 = the plain game) under this name.</summary>
    public const string CapacityMeta = "MultitaskCapacity";

    public static int MultitaskCapacity() => int.TryParse(ReadMeta(CapacityMeta), out int n) ? Math.Clamp(n, 1, MostActions) : 1;

    /// <summary>
    /// Multitask 0.7.0+ writes how many of the player's actions are locked in their slots under this name. They are
    /// part of <see cref="CapacityMeta"/> and of the player's kept actions; the queue runs its lanes only in the rest.
    /// </summary>
    public const string LockedMeta = "MultitaskLocked";

    public static int MultitaskLocked() => int.TryParse(ReadMeta(LockedMeta), out int n) ? Math.Clamp(n, 0, MostActions - 1) : 0;

    /// <summary>The most actions Multitask runs at once: every action the game has (15 skills plus combat).</summary>
    public const int MostActions = 16;

    /// <summary>
    /// The Multitask mod writes the skills of the player's own actions it is keeping under this name: running, or
    /// waiting to be started again ("id|id"). The queue's own steps are not in it. An older Multitask does not write it.
    /// </summary>
    public const string PlayerKeptMeta = "MultitaskPlayerKept";

    /// <summary>The skills of the player's own actions Multitask keeps a slot for, or null when Multitask does not say.</summary>
    public static HashSet<string>? PlayerKept() =>
        ReadMeta(PlayerKeptMeta) is { } kept ? new HashSet<string>(kept.Split('|', StringSplitOptions.RemoveEmptyEntries)) : null;

    public static void PublishPlayerKept(IEnumerable<string> entityIds) => WriteMeta(PlayerKeptMeta, string.Join("|", entityIds));

    static HashSet<string> Held() => new((ReadMeta(HeldMeta) ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The action the player (or the queue) is doing: the running action that no Multitask slot is holding.</summary>
    public static ActionComponent? Main(Game game)
    {
        HashSet<string> held = Held();
        return game.ActionManager.ActiveActions.LastOrDefault(a => !held.Contains(a.Entity.Id));
    }

    /// <summary>False when a recipe cannot be made right now because ingredients or money are missing. Gathering needs nothing.</summary>
    public static bool HasMaterials(ActionComponent action, Target target) => action switch
    {
        ArtisanActionComponent c => CanCreate(c, target.Id),
        CookingActionComponent c => CanCreate(c, target.Id),
        FiremakingActionComponent c => CanCreate(c, target.Id),
        HerbloreActionComponent c => CanCreate(c, target.Id),
        _ => true,
    };

    /// <summary>How many times a recipe can be made from what the bank holds now; a very large number when it costs no items.</summary>
    public static long Batches(Game game, ActionComponent action, Target target) => action switch
    {
        ArtisanActionComponent c => Batches(game, c, target.Id),
        CookingActionComponent c => Batches(game, c, target.Id),
        FiremakingActionComponent c => Batches(game, c, target.Id),
        HerbloreActionComponent c => Batches(game, c, target.Id),
        HuntingActionComponent c => TrapBatches(c, target.Id),
        _ => long.MaxValue,
    };

    static long Batches<TRecipe, TActive>(Game game, CraftingActionComponent<TRecipe, TActive> c, string id)
        where TRecipe : class, ICraftingRecipe, INamespacedDataClass<TRecipe> where TActive : class, IActiveCraftingRecipe
    {
        TActive? recipe = c.ActiveRecipes.FirstOrDefault(r => r.Key.Id == id).Value;
        if (recipe == null || !c.CanCreateRecipe(recipe)) return 0;
        if (!game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) || e is not GamesByMalcs.Melvor2.Entities.Bank.BankEntity bank) return 1;
        long batches = long.MaxValue;
        foreach (var cost in recipe.Stats.ItemCost.Values)
            if (cost.Value.Quantity > 0) batches = Math.Min(batches, bank.Inventory.GetQuantity(cost.Value.Item) / cost.Value.Quantity);
        return Math.Max(1, batches);
    }

    static bool CanCreate<TRecipe, TActive>(CraftingActionComponent<TRecipe, TActive> c, string id)
        where TRecipe : class, ICraftingRecipe, INamespacedDataClass<TRecipe> where TActive : class, IActiveCraftingRecipe
    {
        TActive? recipe = c.ActiveRecipes.FirstOrDefault(r => r.Key.Id == id).Value;
        return recipe != null && c.CanCreateRecipe(recipe);
    }

    /// <summary>
    /// The game's other ways of making a recipe that the player can use now, such as Smithing's Sacrificial Anvil
    /// (the switch under the recipe on the skill's page), as (id, name). Empty for everything else.
    /// </summary>
    public static List<(string Id, string Name)> Modes(ActionComponent action, string recipeId)
    {
        var modes = new List<(string Id, string Name)>();
        if (action is not ArtisanActionComponent c) return modes;
        try
        {
            ActiveArtisanRecipe? recipe = c.ActiveRecipes.FirstOrDefault(r => r.Key.Id == recipeId).Value;
            if (recipe == null) return modes;
            foreach (AlternativeRewardMode mode in c.Config.RewardModes)
                if (mode.ShowAlternativeMode(recipe) && mode.UnlockRequirements.AreMet) modes.Add((mode.Id, mode.DisplayName.Text));
        }
        catch (Exception) { }
        return modes;
    }

    /// <summary>The id of the other way of making that is switched on for a recipe ("" for the normal way).</summary>
    public static string ModeOf(ActionComponent action, string recipeId) =>
        action is ArtisanActionComponent c ? c.ActiveRecipes.FirstOrDefault(r => r.Key.Id == recipeId).Value?.CurrentRewardMode?.Id ?? "" : "";

    /// <summary>Switches a recipe to one of its other ways of making (or back to normal with ""), as the game's switch does.
    /// False when that way does not exist for the recipe or is still locked.</summary>
    public static bool SetMode(ActionComponent action, string recipeId, string modeId)
    {
        if (action is not ArtisanActionComponent c) return modeId == "";
        ActiveArtisanRecipe? recipe = c.ActiveRecipes.FirstOrDefault(r => r.Key.Id == recipeId).Value;
        if (recipe == null) return false;
        AlternativeRewardMode? now = recipe.CurrentRewardMode;
        if ((now?.Id ?? "") == modeId) return true;
        if (modeId == "") { recipe.ToggleRewardMode(now!); return recipe.CurrentRewardMode == null; }
        AlternativeRewardMode? wanted = c.Config.RewardModes.FirstOrDefault(m => m.Id == modeId);
        if (wanted == null || !wanted.ShowAlternativeMode(recipe) || !wanted.UnlockRequirements.AreMet) return false;
        recipe.ToggleRewardMode(wanted);
        return recipe.CurrentRewardMode == wanted;
    }

    /// <summary>The ids of the items a recipe uses up; empty for gathering, thieving, combat...</summary>
    public static HashSet<string> Ingredients(ActionComponent action, Target target) => action switch
    {
        ArtisanActionComponent c => Ingredients(c, target.Id),
        CookingActionComponent c => Ingredients(c, target.Id),
        FiremakingActionComponent c => Ingredients(c, target.Id),
        HerbloreActionComponent c => Ingredients(c, target.Id),
        HuntingActionComponent c => TrapIds(c, target.Id),
        _ => new HashSet<string>(),
    };

    static HashSet<string> Ingredients<TRecipe, TActive>(CraftingActionComponent<TRecipe, TActive> c, string id)
        where TRecipe : class, ICraftingRecipe, INamespacedDataClass<TRecipe> where TActive : class, IActiveCraftingRecipe
    {
        TActive? recipe = c.ActiveRecipes.FirstOrDefault(r => r.Key.Id == id).Value;
        var needs = new HashSet<string>();
        if (recipe != null)
            foreach (var cost in recipe.Stats.ItemCost.Values)
                if (cost.Value.Quantity > 0) needs.Add(cost.Value.Item.Id);
        return needs;
    }

    /// <summary>Whether the game would let this target be started now (materials aside). Nothing is started.</summary>
    public static bool CanStart(ActionComponent action, Target target, out string problem) => Allowed(action, target, out problem);

    /// <summary>
    /// Like <see cref="CanStart"/>, but what a step can be given (a hunt's trap) does not count: only what the character
    /// has not unlocked yet. Used by the planner, which plans those things too.
    /// </summary>
    public static bool Unlocked(ActionComponent action, Target target, out string problem) => Allowed(action, target, out problem, materials: false);

    /// <summary>The game's own requirement check for a target; nothing is started here.</summary>
    static bool Allowed(ActionComponent action, Target target, out string problem, bool materials = true)
    {
        string locked = W("targets.requirements_not_met", "its requirements are not met yet");
        problem = Targets.W("targets.no_longer_exists_game", "it no longer exists in the game");

        // Two skills need a choice the player makes on the skill's own page; the queue does not make it for them.
        if (action is HuntingActionComponent hunting)
        {
            ActiveHuntingAnimal? animal = hunting.Locations.Values.SelectMany(l => l.Animals).FirstOrDefault(a => a.Animal.Id == target.Id);
            if (animal == null) return false;
            // A trap is needed unless the animal is hunted without one; the queue picks one the bank holds itself.
            problem = !animal.Animal.Requirements.AreMet ? Missing(animal.Animal.Requirements)
                : materials && !animal.TraplessEnabled && HeldTrap(hunting, animal) == null
                    ? Targets.W("targets.no_trap_bank_needs", "no trap for it in the bank (it needs {trapNames})", ("trapNames", TrapNames(animal))) : "";
            return problem == "";
        }
        if (action is CombatActionComponent && target.Extra == Slayer)
        {
            ActiveSlayerMaster? master = Master(target.Id);
            if (master == null) return false;
            Enemy? task = master.CurrentTask;
            CombatArea? taskArea = task == null ? null : master.GetCurrentTaskArea(task);
            problem = !master.Data.UnlockRequirements.AreMet ? Targets.W("targets.slayer_master_not_unlocked_yet", "this slayer master is not unlocked yet")
                : taskArea != null && !taskArea.UnlockRequirements.AreMet ? Targets.W("targets.task_monster_area_not_unlocked", "the task monster's area ({area}) is not unlocked yet", ("area", taskArea.DisplayName.Text))
                : task != null && !task.Requirements.AreMet ? Targets.W("targets.task_monster_cannot_fought_yet", "the task monster ({monster}) cannot be fought yet", ("monster", task.DisplayName.Text)) : "";
            return problem == "";
        }
        if (action is AgilityActionComponent agility)
        {
            ActiveAgilityCourse? course = agility.Courses.FirstOrDefault(c => c.Key.Id == target.Id).Value;
            if (course == null) return false;
            problem = !course.Course.Requirements.AreMet ? Missing(course.Course.Requirements)
                : course.SelectedPath == null ? Targets.W("targets.choose_path_agility_page_first", "choose a path for it on the Agility page first") : "";
            return problem == "";
        }

        if (action is ThievingActionComponent thieving)
        {
            var recipe = thieving.ActiveRecipes.FirstOrDefault(r => r.Key.Id == target.Id).Value;
            if (recipe == null) return false;
            problem = thieving.CanThieveRecipe(recipe) ? "" : locked;
            return problem == "";
        }

        // Everything else is one requirement list of the game's own (combat: the area's, then the monster's).
        ConditionGroup? needs = action switch
        {
            WoodcuttingActionComponent c => c.Trees.Keys.FirstOrDefault(r => r.Id == target.Id)?.Requirements,
            MiningActionComponent c => c.Rocks.Keys.FirstOrDefault(r => r.Id == target.Id)?.Requirements,
            FishingActionComponent c => c.Locations.Values.SelectMany(l => l.Recipes).FirstOrDefault(r => r.Recipe.Id == target.Id)?.Recipe.Requirements,
            ArtisanActionComponent c => RecipeNeeds(c, target.Id),
            CookingActionComponent c => RecipeNeeds(c, target.Id),
            FiremakingActionComponent c => RecipeNeeds(c, target.Id),
            HerbloreActionComponent c => RecipeNeeds(c, target.Id),
            AstrologyActionComponent c => c.ActiveConstellations.Keys.FirstOrDefault(r => r.Id == target.Id)?.Requirements,
            CombatActionComponent { Entity: CombatEntity combat } => CombatNeeds(combat, target),
            _ => null,
        };
        if (needs == null) return false;
        problem = needs.AreMet ? "" : Missing(needs);
        return needs.AreMet;
    }

    static ConditionGroup? RecipeNeeds<TRecipe, TActive>(CraftingActionComponent<TRecipe, TActive> c, string id)
        where TRecipe : class, ICraftingRecipe, INamespacedDataClass<TRecipe> where TActive : class, IActiveCraftingRecipe =>
        c.ActiveRecipes.Keys.FirstOrDefault(r => r.Id == id)?.Requirements;

    static ConditionGroup? CombatNeeds(CombatEntity combat, Target target)
    {
        BaseCombatArea? area = CombatArea(combat, target);
        if (area == null) return null;
        if (!area.UnlockRequirements.AreMet || area is not CombatArea normal) return area.UnlockRequirements;
        return normal.Enemies.FirstOrDefault(e => e.Id == target.Id)?.Requirements ?? area.UnlockRequirements;
    }

    /// <summary>What is still missing from a requirement list, in the game's own words ("needs Fletching Level 45").
    /// Conditions marked IsOr belong with the one before them, as in the game's ConditionGroup; a met group is left out.</summary>
    public static string Missing(ConditionGroup needs)
    {
        string locked = W("targets.requirements_not_met", "its requirements are not met yet");
        var parts = new List<string>();
        try
        {
            var all = needs.Conditions;
            for (int i = 0; i < all.Count; i++)
            {
                int end = i;
                while (end + 1 < all.Count && all[end + 1].IsOr) end++;
                var group = all.Skip(i).Take(end - i + 1).ToList();
                i = end;
                if (group.Any(c => c.IsMet)) continue;
                var words = group.Select(Words).Where(w => w != "").Distinct().ToList();
                if (words.Count > 0 && !parts.Contains(string.Join(Targets.W("targets.or", " or "), words))) parts.Add(string.Join(Targets.W("targets.or", " or "), words));
            }
        }
        catch (Exception) { return locked; }
        return parts.Count == 0 ? locked : Targets.W("targets.needs", "needs {what}", ("what", string.Join(", ", parts)));
    }

    /// <summary>A condition's description without the game's markup ([b], [color], icons).</summary>
    static string Words(Condition condition)
    {
        try
        {
            string text = condition.GetDescription(false, false);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[img[^\]]*\].*?\[/img\]", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[/?[a-z]+(=[^\]]*)?[^\]]*\]", "");
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        }
        catch (Exception) { return ""; }
    }

    static BaseCombatArea? CombatArea(CombatEntity combat, Target target) =>
        combat.Areas.ActiveAreas.Keys.FirstOrDefault(a => a.Id == (target.Extra == Dungeon ? target.Id : target.Extra));

    static void Select(ActionComponent action, Target target)
    {
        switch (action)
        {
            case WoodcuttingActionComponent c: c.SelectTree(c.Trees.First(t => t.Key.Id == target.Id).Value); break;
            case MiningActionComponent c: c.SelectRock(c.Rocks.First(r => r.Key.Id == target.Id).Value); break;
            case FishingActionComponent c:
            {
                ActiveFishingRecipe recipe = c.Locations.Values.SelectMany(l => l.Recipes).First(r => r.Recipe.Id == target.Id);
                c.SetCurrentLocation(recipe.ActiveLocation);
                c.SelectRecipe(recipe);
                break;
            }
            case ArtisanActionComponent c: StartRecipe(c, target.Id); break;
            case CookingActionComponent c: StartRecipe(c, target.Id); break;
            case FiremakingActionComponent c: StartRecipe(c, target.Id); break;
            case HerbloreActionComponent c: StartRecipe(c, target.Id); break;
            case ThievingActionComponent c: c.TryStartActiveRecipe(c.ActiveRecipes.First(r => r.Key.Id == target.Id).Value); break;
            case AgilityActionComponent c: c.SelectCourse(c.Courses.First(r => r.Key.Id == target.Id).Value); break;
            case AstrologyActionComponent c: c.StudyConstellation(c.ActiveConstellations.First(r => r.Key.Id == target.Id).Value); break;
            case HuntingActionComponent c:
            {
                ActiveHuntingAnimal animal = c.Locations.Values.SelectMany(l => l.Animals).First(a => a.Animal.Id == target.Id);
                c.SelectLocation(animal.Location);
                c.SelectAnimal(animal);
                // No trap chosen for it, or the chosen one is used up: take one the bank holds, as the game's own
                // "swap to the next trap" does (a plan made the traps in an earlier step).
                if (!animal.TraplessEnabled && (animal.SelectedTrap == null || c.Inventory.GetQuantity(animal.SelectedTrap) <= 0)
                    && HeldTrap(c, animal) is { } held)
                    animal.SelectTrap(held);
                c.StartTracking();
                break;
            }
            case CombatActionComponent { Entity: CombatEntity combat } c when target.Extra == Slayer:
            {
                ActiveSlayerMaster master = Master(target.Id)!;
                if (master.CurrentTask == null) master.StartInitialTask();   // the same as asking the master for a first task
                Enemy? task = master.CurrentTask;
                if (task == null) break;
                combat.Areas.SelectEnemy(master.GetCurrentTaskArea(task), task);
                if (!c.IsStarted) c.TryStart();
                break;
            }
            case CombatActionComponent { Entity: CombatEntity combat } c:
            {
                BaseCombatArea area = CombatArea(combat, target)!;
                if (area is Dungeon dungeon) combat.Areas.SelectDungeon(dungeon);
                else if (area is CombatArea normal) combat.Areas.SelectEnemy(normal, normal.Enemies.First(e => e.Id == target.Id));
                if (!c.IsStarted) c.TryStart();   // selecting an enemy usually starts the fight by itself
                break;
            }
        }
    }

    // ---------------------------------------------------------------- hunting traps

    /// <summary>
    /// The trap a hunt for this animal uses: the one chosen for it on the Hunting page, else the best one the bank
    /// holds, else the first one it can use (what a plan would make). Null for an animal hunted without a trap.
    /// </summary>
    public static Item? TrapFor(HuntingActionComponent hunting, ActiveHuntingAnimal animal)
    {
        if (animal.TraplessEnabled) return null;
        return animal.SelectedTrap ?? HeldTrap(hunting, animal) ?? animal.CompatibleTraps.FirstOrDefault();
    }

    /// <summary>A trap for this animal that the bank holds: the chosen one if there are any left, else the best one (the game's own order).</summary>
    static Item? HeldTrap(HuntingActionComponent hunting, ActiveHuntingAnimal animal)
    {
        if (animal.SelectedTrap is { } chosen && hunting.Inventory.GetQuantity(chosen) > 0) return chosen;
        for (int i = animal.CompatibleTraps.Count - 1; i >= 0; i--)
            if (hunting.Inventory.GetQuantity(animal.CompatibleTraps[i]) > 0) return animal.CompatibleTraps[i];
        return null;
    }

    static string TrapNames(ActiveHuntingAnimal animal) =>
        animal.CompatibleTraps.Count == 0 ? Targets.W("targets.trap", "a trap") : string.Join(Targets.W("targets.or", " or "), animal.CompatibleTraps.Select(t => t.DisplayName.GetPluralText(1)));

    static ActiveHuntingAnimal? Animal(HuntingActionComponent hunting, string id) =>
        hunting.Locations.Values.SelectMany(l => l.Animals).FirstOrDefault(a => a.Animal.Id == id);

    /// <summary>How many tries the traps in the bank give a hunt (every try can use one up); very large without a trap.</summary>
    static long TrapBatches(HuntingActionComponent hunting, string id)
    {
        ActiveHuntingAnimal? animal = Animal(hunting, id);
        if (animal == null || animal.TraplessEnabled) return long.MaxValue;
        return animal.CompatibleTraps.Sum(t => (long)hunting.Inventory.GetQuantity(t));
    }

    static HashSet<string> TrapIds(HuntingActionComponent hunting, string id)
    {
        ActiveHuntingAnimal? animal = Animal(hunting, id);
        return animal == null || animal.TraplessEnabled ? new HashSet<string>() : animal.CompatibleTraps.Select(t => t.Id).ToHashSet();
    }

    static void StartRecipe<TRecipe, TActive>(CraftingActionComponent<TRecipe, TActive> c, string id)
        where TRecipe : class, ICraftingRecipe, INamespacedDataClass<TRecipe> where TActive : class, IActiveCraftingRecipe
    {
        c.SelectRecipe(c.ActiveRecipes.First(r => r.Key.Id == id).Value);
        c.TryStart();
    }
}
