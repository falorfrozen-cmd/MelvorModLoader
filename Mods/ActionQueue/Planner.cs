using System;
using System.Collections.Generic;
using System.Linq;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Components.ArtisanAction;
using GamesByMalcs.Melvor2.Components.CraftingAction;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Cooking;
using GamesByMalcs.Melvor2.Entities.Firemaking;
using GamesByMalcs.Melvor2.Entities.Fishing;
using GamesByMalcs.Melvor2.Entities.Herblore;
using GamesByMalcs.Melvor2.Entities.Hunting;
using GamesByMalcs.Melvor2.Entities.Mining;
using GamesByMalcs.Melvor2.Entities.Woodcutting;

namespace ActionQueue;

/// <summary>One way to get an item: a tree, a rock, a fishing spot, a recipe. All numbers are per action.</summary>
public sealed class Maker
{
    public GameEntity Entity = null!;
    public ActionComponent Action = null!;
    public Target Target = null!;
    public Item Product = null!;
    /// <summary>How many of the product one action gives on average, with the character's current bonuses.</summary>
    public double PerAction;
    public List<(Item Item, int Quantity)> Costs = new();
    public List<(string Currency, long Quantity)> Money = new();
    /// <summary>Seconds one action takes at normal game speed; 0 when it cannot be told in advance.</summary>
    public double Seconds;
    /// <summary>False when the yield is a matter of chance (fishing, cooking that can fail...).</summary>
    public bool Certain = true;
    /// <summary>
    /// The chance that one action gives anything at all (a hunt's try that catches the animal and the item drops);
    /// 1 when unknown. <see cref="PerAction"/> already counts it in; the planner also uses it to plan enough tries.
    /// </summary>
    public double Chance = 1;
    /// <summary>True when the product is what this action is mainly for (not a side product).</summary>
    public bool Main = true;
    /// <summary>
    /// True while the game does not show this action on its skill page (a quest target outside its quest step).
    /// A plan never uses it; it is still known, so a step already in the queue can be followed.
    /// </summary>
    public bool Hidden;

    public string Name => $"{Entity.DisplayName.Text}: {Target.Name}";
}

/// <summary>One item of a plan: how many the bank must hold, how many it holds, and how the rest is made.</summary>
public sealed class PlanLine
{
    public Item Item = null!;
    public Maker? Maker;
    public long Need, Have, Missing, Actions;
    public double Seconds;
    /// <summary>Why this line may not work as it is (its action is still locked); empty when fine.</summary>
    public string Problem = "";

    public string ItemName => Item.DisplayName.GetPluralText(1);
}

public sealed class PlanResult
{
    public Item Item = null!;
    /// <summary>How many the bank must hold in the end.</summary>
    public long Own;
    /// <summary>The number the player typed, and whether it meant "this many more" (true) or "this many in total".</summary>
    public long Asked;
    public bool More;
    /// <summary>The steps in the order they must run: what is gathered first, the wanted item last.</summary>
    public List<PlanLine> Steps = new();
    /// <summary>Missing items that no skill on the queue can make: the player has to bring them.</summary>
    public List<PlanLine> Supply = new();
    /// <summary>Materials the plan needs and the bank already holds enough of: no step, but worth showing.</summary>
    public List<PlanLine> Covered = new();
    /// <summary>Planned from nothing: the bank was not counted, every material has its step.</summary>
    public bool Everything;
    /// <summary>What the bank held (and the queue would make) of the item before the plan: a "then sell them" keeps it.</summary>
    public long StockBefore;
    public Dictionary<string, long> Money = new();
    public double Seconds;
    public int UnknownTimes;
    /// <summary>Set when there is nothing to add (already owned, cannot be made).</summary>
    public string Problem = "";
}

/// <summary>
/// Works out what it takes to own N of an item: follows the recipes back to what is gathered, takes off what the
/// bank already holds (and what the steps already in the queue will leave behind), and turns the result into
/// ordinary queue steps. Nothing here changes the game; it only reads it.
/// </summary>
public static class Planner
{
    /// <summary>Extra actions planned when the yield is a matter of chance, so a little bad luck does not leave the plan short.</summary>
    const double LuckMargin = 1.10;

    // ---------------------------------------------------------------- what makes what

    public static Dictionary<Item, List<Maker>> Makers(Game game)
    {
        var all = new List<Maker>();
        foreach ((GameEntity entity, ActionComponent action) in Targets.Skills(game))
        {
            int from = all.Count;
            try { Collect(entity, action, all); }
            catch (Exception e) { Queue.Log($"Planner: {entity.DisplayName.Text} could not be read: {e.Message}"); }
            HashSet<string> hidden = Catalog.HiddenIds(game, action);
            for (int i = from; i < all.Count; i++) all[i].Hidden = hidden.Contains(all[i].Target.Id);
        }
        var index = new Dictionary<Item, List<Maker>>();
        foreach (Maker m in all)
        {
            if (m.PerAction <= 0) continue;
            if (!index.TryGetValue(m.Product, out List<Maker>? list)) index[m.Product] = list = new List<Maker>();
            list.Add(m);
        }
        return index;
    }

    static void Collect(GameEntity entity, ActionComponent action, List<Maker> list)
    {
        Maker New(string id, string name, Item product, double perAction, double seconds, bool certain = true, bool main = true) =>
            new() { Entity = entity, Action = action, Target = new Target(id, name), Product = product, PerAction = perAction, Seconds = seconds, Certain = certain, Main = main };

        switch (action)
        {
            case WoodcuttingActionComponent c:
                // A tree takes one hit per point of health (bonus damage only makes it faster), then gives its logs.
                foreach ((WoodcuttingRecipe recipe, WoodcuttingTree tree) in c.Trees)
                    list.Add(New(recipe.Id, recipe.DisplayName.Text, recipe.Product.Item, tree.Stats.ProductQuantity.Value,
                        tree.Stats.MaxHealth.Value * tree.Stats.Interval.Value / 1000.0));
                break;

            case MiningActionComponent c:
                foreach ((MiningRecipe recipe, MiningRock rock) in c.Rocks)
                {
                    double hits = MiningHits(rock.Stats.RockBreakChance.Value, rock.Stats.MinimumProspecting.Value);
                    list.Add(New(recipe.Id, recipe.DisplayName.Text, recipe.Product.Item, rock.Stats.ProductQuantity.Value,
                        hits <= 0 ? 0 : hits * rock.Stats.Interval.Value / 1000.0));
                }
                break;

            case FishingActionComponent c:
                foreach (ActiveFishingRecipe recipe in c.Locations.Values.SelectMany(l => l.Recipes))
                {
                    var products = recipe.Recipe.RandomProducts.Items.ToList();
                    double weights = products.Sum(p => (double)p.Weight);
                    if (weights <= 0) continue;
                    (double min, double max) = recipe.Stats.Interval.Value;
                    double top = products.Max(p => (double)p.Weight);
                    foreach (FishingProduct p in products)
                        list.Add(New(recipe.Recipe.Id, recipe.Recipe.DisplayName.Text, p.Item,
                            recipe.Stats.FishChance.Value * p.Weight / weights * (p.MinQuantity + p.MaxQuantity) / 2.0,
                            (min + max) / 2000.0, certain: false, main: p.Weight >= top));
                }
                break;

            case ArtisanActionComponent c: Crafting(entity, c, list, r => r.Stats.Steps.Value, _ => 1); break;
            case CookingActionComponent c: Crafting(entity, c, list, _ => 1, r => r.Stats.SuccessChance.Value); break;
            case FiremakingActionComponent c: Crafting(entity, c, list, _ => 1, _ => 1); break;
            case HerbloreActionComponent c: Crafting(entity, c, list, _ => 1, _ => 1, timeKnown: false); break;

            case HuntingActionComponent c:
                // How long a catch takes depends on the trap and the hunt itself, so no time is promised. Every try to
                // trap the animal uses up a trap (unless the animal is hunted without one), and only some tries catch
                // it: one action here is one try, so the traps are planned like any other material.
                foreach (ActiveHuntingAnimal animal in c.Locations.Values.SelectMany(l => l.Animals))
                {
                    Item? trap = animal.TraplessEnabled ? null : Targets.TrapFor(c, animal);
                    double catches = 1;
                    try { catches = Math.Clamp(animal.Stats.TrappingSuccessChance.Value, 0.01, 1); } catch (Exception) { }
                    bool first = true;
                    foreach (ItemQuantityChance p in animal.Animal.Products)
                    {
                        Maker maker = New(animal.Animal.Id, animal.Animal.DisplayName.Text, p.Item, p.Quantity * Math.Clamp(p.Chance, 0, 1) * catches, 0, certain: false, main: first);
                        maker.Chance = Math.Clamp(catches * Math.Clamp(p.Chance, 0, 1), 0.01, 1);
                        if (trap != null) maker.Costs = new List<(Item Item, int Quantity)> { (trap, 1) };
                        list.Add(maker);
                        first = false;
                    }
                }
                break;
        }
    }

    static void Crafting<TRecipe, TActive>(GameEntity entity, CraftingActionComponent<TRecipe, TActive> c, List<Maker> list,
        Func<TActive, double> steps, Func<TActive, double> success, bool timeKnown = true)
        where TRecipe : class, ICraftingRecipe, INamespacedDataClass<TRecipe> where TActive : class, IActiveCraftingRecipe
    {
        foreach ((TRecipe recipe, TActive active) in c.ActiveRecipes)
        {
            ICraftingActionStats stats = active.Stats;
            var products = new List<(Item Item, double Quantity, double Chance)>();
            if (stats.Product != null) products.Add((stats.Product.Value.Item, stats.Product.Value.Quantity, 1));
            if (stats.Products != null)
                products.AddRange(stats.Products.Values.Select(v => (v.Value.Item, (double)v.Value.Quantity, Math.Clamp(v.Value.Chance, 0, 1))));
            if (products.Count == 0) continue;

            var costs = stats.ItemCost.Values.Select(v => (v.Value.Item, v.Value.Quantity)).ToList();
            var money = stats.CurrencyCost.Values.Select(v => (v.Value.Currency.DisplayName.Text, v.Value.Quantity)).ToList();
            double works = Math.Clamp(success(active), 0, 1);
            double seconds = timeKnown ? stats.Interval.Value * Math.Max(1, steps(active)) / 1000.0 : 0;
            double best = products.Max(p => p.Chance);
            foreach ((Item item, double quantity, double chance) in products)
                list.Add(new Maker
                {
                    Entity = entity, Action = c, Target = new Target(recipe.Id, recipe.DisplayName.Text), Product = item,
                    PerAction = quantity * chance * works, Costs = costs, Money = money, Seconds = seconds,
                    Certain = chance >= 1 && works >= 1, Main = chance >= best,
                });
        }
    }

    /// <summary>
    /// Mining: every hit breaks the rock with the given chance; a hit that does not, adds that chance to the
    /// rock's progress, and a full bar breaks it for sure. Returns the average number of hits per break.
    /// </summary>
    static double MiningHits(double chance, double start)
    {
        if (chance <= 0) return 0;
        if (chance >= 1) return 1;
        int last = Math.Max(1, (int)Math.Ceiling((0.9999 - start) / chance));
        double expected = 0, stillWhole = 1;
        for (int hit = 1; hit < last && stillWhole > 1e-9; hit++)
        {
            expected += hit * stillWhole * chance;
            stillWhole *= 1 - chance;
        }
        return expected + last * stillWhole;
    }

    /// <summary>The items a plan can be asked for: everything some action on the queue produces as its main product.</summary>
    public static List<Item> Makeable(Game game) =>
        Makers(game).Where(m => m.Value.Any(x => x.Main && !x.Hidden)).Select(m => m.Key)
            .OrderBy(i => i.DisplayName.GetPluralText(1), StringComparer.OrdinalIgnoreCase).ToList();

    // ---------------------------------------------------------------- the bank, now and after the queue

    static BankEntity? Bank(Game game) => game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) ? e as BankEntity : null;

    /// <summary>
    /// What the bank will hold once the steps already in the queue have run. Only "until I own N" steps of a known
    /// recipe can be foreseen; they add their product and take away what it costs. Without this, two plans that
    /// need the same ore would both count the same ore.
    /// </summary>
    static Dictionary<Item, long> StockAfterQueue(Game game, Dictionary<Item, List<Maker>> makers, IEnumerable<Step> queued)
    {
        var stock = new Dictionary<Item, long>();
        BankEntity? bank = Bank(game);
        long Now(Item item) => stock.TryGetValue(item, out long n) ? n : bank?.Inventory.GetQuantity(item) ?? 0;

        var byId = makers.Keys.Concat(makers.Values.SelectMany(l => l).SelectMany(m => m.Costs).Select(c => c.Item))
            .Distinct().ToDictionary(i => i.Id);
        foreach (Step step in queued)
        {
            if (step.IsSell)
            {
                if (step.Run != RunState.Done && byId.TryGetValue(step.TargetId, out Item? sold)) stock[sold] = Math.Min(Now(sold), (long)Math.Max(0, step.Amount));
                continue;
            }
            // (A step with a second goal may end before it has made all of it: the plan does not count on it.)
            if (step.Or != null || step.Until is not (Until.Item or Until.Made) || !byId.TryGetValue(step.Subject, out Item? item)) continue;
            Maker? maker = makers.TryGetValue(item, out List<Maker>? list) ? list.FirstOrDefault(m => m.Entity.Id == step.Entity && m.Target.Id == step.TargetId) : null;
            if (maker == null) continue;
            long before = Now(item), goal = (long)Math.Ceiling(step.Amount) + (step.Until == Until.Made ? before : 0);
            if (goal <= before) continue;
            long actions = Actions(goal - before, maker);
            foreach ((Item cost, int quantity) in maker.Costs) stock[cost] = Math.Max(0, Now(cost) - actions * quantity);
            stock[item] = goal;
        }
        // Everything the loop did not touch is simply what the bank holds now.
        foreach (Item item in makers.Keys.Concat(makers.Values.SelectMany(l => l).SelectMany(m => m.Costs).Select(c => c.Item)).Distinct())
            if (!stock.ContainsKey(item)) stock[item] = bank?.Inventory.GetQuantity(item) ?? 0;
        return stock;
    }

    /// <summary>
    /// How many actions to plan for. When the yield is a matter of chance, a little more than the average, and for
    /// small numbers clearly more: one Rabbit Fur from traps that catch 40% of the time needs 2.5 tries on average,
    /// but 2 or 3 traps leave the plan short far too often (9 are planned: short about 1 time in 100). The step
    /// itself still ends at its goal, so what is left over is only spare material (a few traps more).
    /// </summary>
    static long Actions(long missing, Maker maker)
    {
        if (maker.Chance < 1)
        {
            // Tries that each succeed with a chance: so many successes are needed, each giving a few of the item.
            // (a hunt's catch gives its quantity times the animal's size, 0.5 to 1.3 at first: count a little under it)
            double p = maker.Chance;
            double perSuccess = Math.Max(1, Math.Floor(maker.PerAction / p * 0.8));
            double successes = Math.Ceiling(missing / perSuccess - 1e-9);
            double tries = successes / p + 3 * Math.Sqrt(successes * (1 - p)) / p;   // short only about 1 time in 100
            return (long)Math.Ceiling(Math.Max(tries, successes / p * LuckMargin) - 1e-9);
        }
        double average = missing / maker.PerAction;
        if (!maker.Certain) average = average * LuckMargin + 2 * Math.Sqrt(average);
        return (long)Math.Ceiling(average - 1e-9);
    }

    // ---------------------------------------------------------------- the plan

    /// <summary>
    /// The best way to make an item right now: one whose whole chain can be done (it and everything its ingredients
    /// are made with are unlocked), then one that is at least unlocked itself, then the rest; a main product before a
    /// side product; then the fastest. So Headless Bolts are made from Iron Bars when the faster recipe needs
    /// Ironsteel Bars the character cannot make yet. Actions the game does not show at the moment (quest targets) are
    /// never chosen: the player could not find them on the skill page, and they only exist for the quest.
    /// </summary>
    static Maker? Choose(List<Maker>? candidates, Dictionary<Maker, string> problems, Func<Maker, bool> chainOpen)
    {
        candidates = candidates?.Where(m => !m.Hidden).ToList();
        if (candidates == null || candidates.Count == 0) return null;
        return candidates
            .OrderBy(m => Problem(m, problems) != "" ? 2 : chainOpen(m) ? 0 : 1)
            .ThenBy(m => m.Main ? 0 : 1)
            .ThenBy(m => m.Seconds <= 0 ? double.MaxValue : m.Seconds / m.PerAction)
            .First();
    }

    static string Problem(Maker maker, Dictionary<Maker, string> problems)
    {
        if (!problems.TryGetValue(maker, out string? problem))
        {
            Targets.Unlocked(maker.Action, maker.Target, out problem);
            problems[maker] = problem;
        }
        return problem;
    }

    /// <summary>
    /// Plans owning <paramref name="own"/> of <paramref name="item"/>, counting the bank and the steps in <paramref name="queued"/>.
    /// With <paramref name="more"/> the number means "this many on top of what I have (and what the queue will make)".
    /// With <paramref name="everything"/> the bank is not counted at all: every material gets its step, for the whole
    /// amount (a queue that starts over makes one round's worth each time, without leftovers piling up).
    /// </summary>
    public static PlanResult Compute(Game game, Item item, long own, IEnumerable<Step>? queued = null, bool more = false, bool everything = false)
    {
        var result = new PlanResult { Item = item, Own = own, Asked = own, More = more, Everything = everything };
        Dictionary<Item, List<Maker>> makers = Makers(game);
        Dictionary<Item, long> stock = everything ? new Dictionary<Item, long>() : StockAfterQueue(game, makers, queued ?? Queue.Steps);
        var problems = new Dictionary<Maker, string>();
        var chosen = new Dictionary<Item, Maker?>();
        var choosing = new HashSet<Item>();          // a recipe loop leads back here: that way is not taken
        var open = new Dictionary<Item, bool>();     // the item can be had without anything locked
        Maker? MakerOf(Item i)
        {
            if (chosen.TryGetValue(i, out Maker? m)) return m;
            if (!choosing.Add(i)) return null;
            m = Choose(makers.GetValueOrDefault(i), problems, ChainOpen);
            choosing.Remove(i);
            return chosen[i] = m;
        }
        // An ingredient no skill on the queue makes has to be brought by the player: that does not lock a chain.
        bool ItemOpen(Item i)
        {
            if (open.TryGetValue(i, out bool known)) return known;
            if (choosing.Contains(i)) return false;   // a recipe loop is not a way to get it
            open[i] = false;                           // asked again while being worked out (an item made from itself): not a way
            Maker? m = MakerOf(i);
            return open[i] = m == null || ChainOpen(m);
        }
        bool ChainOpen(Maker m) => Problem(m, problems) == "" && m.Costs.All(c => ItemOpen(c.Item));
        long Stock(Item i) => everything ? 0 : stock.TryGetValue(i, out long n) ? n : Bank(game)?.Inventory.GetQuantity(i) ?? 0;

        result.StockBefore = everything ? Bank(game)?.Inventory.GetQuantity(item) ?? 0 : Stock(item);
        if (more) result.Own = own = Stock(item) + own;
        if (MakerOf(item) == null) { result.Problem = Lang.T("planner.none_skills_queue_can_drive", "None of the skills the queue can drive makes this item."); return result; }

        // Ingredients before what is made from them. Walking that list backwards visits an item only after
        // everything that needs it, so its total is complete before its own ingredients are worked out.
        var order = new List<Item>();
        var state = new Dictionary<Item, int>();   // 1 = being visited, 2 = done
        void Visit(Item i)
        {
            if (state.ContainsKey(i)) return;      // done, or a recipe loop: do not follow it round
            state[i] = 1;
            Maker? m = MakerOf(i);
            if (m != null) foreach ((Item cost, _) in m.Costs) Visit(cost);
            state[i] = 2;
            order.Add(i);
        }
        Visit(item);

        var need = new Dictionary<Item, long> { [item] = own };
        var lines = new Dictionary<Item, PlanLine>();
        var late = new Dictionary<Item, long>();   // needed by something after the item itself was already settled (recipe loops)
        for (int at = order.Count - 1; at >= 0; at--)
        {
            Item i = order[at];
            long wanted = need.GetValueOrDefault(i);
            if (wanted <= 0) continue;
            Maker? maker = MakerOf(i);
            var line = new PlanLine { Item = i, Maker = maker, Need = wanted, Have = Stock(i) };
            line.Missing = Math.Max(0, line.Need - line.Have);
            lines[i] = line;
            if (line.Missing == 0 || maker == null) continue;
            // Every way to make it is still locked: no step the queue would only skip; the player has to bring it.
            string locked = Problem(maker, problems);
            if (locked != "")
            {
                if (i == item)
                {
                    result.Problem = Lang.F("planner.you_cannot_make_yet", "You cannot make {itemName} yet: {locked}.", ("itemName", line.ItemName), ("locked", locked));
                    return result;
                }
                line.Problem = locked;
                line.Maker = null;
                continue;
            }

            line.Actions = Actions(line.Missing, maker);
            line.Seconds = maker.Seconds * line.Actions;
            foreach ((Item cost, int quantity) in maker.Costs)
            {
                if (lines.ContainsKey(cost)) late[cost] = late.GetValueOrDefault(cost) + line.Actions * quantity;
                else need[cost] = need.GetValueOrDefault(cost) + line.Actions * quantity;
            }
            foreach ((string currency, long quantity) in maker.Money)
                result.Money[currency] = result.Money.GetValueOrDefault(currency) + line.Actions * quantity;
        }

        foreach (Item i in order)
        {
            if (!lines.TryGetValue(i, out PlanLine? line)) continue;
            if (line.Missing == 0) { if (i != item) result.Covered.Add(line); continue; }
            if (line.Maker == null) result.Supply.Add(line);
            else
            {
                result.Steps.Add(line);
                result.Seconds += line.Seconds;
                if (line.Maker.Seconds <= 0) result.UnknownTimes++;
            }
        }
        // Gathering needs nothing from the other steps, so it goes first: easier to read, and the order stays valid.
        result.Steps = result.Steps.Where(l => l.Maker!.Costs.Count == 0).Concat(result.Steps.Where(l => l.Maker!.Costs.Count > 0)).ToList();
        foreach ((Item i, long extra) in late)
            result.Supply.Add(new PlanLine { Item = i, Need = extra, Have = 0, Missing = extra });

        if (result.Steps.Count == 0)
            result.Problem = lines[item].Missing == 0
                ? Lang.F("planner.you_already_own_counting_what", "You already own {have:N0} {item} (counting what the queue will make). Choose \"Make\" on the left to make more.", ("have", lines[item].Have), ("item", lines[item].ItemName))
                : Lang.T("planner.nothing_plan_can_made_by", "Nothing in this plan can be made by a skill.");
        return result;
    }

    /// <summary>
    /// Adds the plan's steps to the end of the queue, in the given lane. A plan's materials are "until I own N": the
    /// bank is topped up to what the plan needs. With "every material" (a queue that starts over) N is one round's need,
    /// so each round tops up only what the last one used. The item itself is then "make N more" when "Make" was chosen:
    /// an "until I own N" for it would be reached in the first round and leave every later round with nothing to do.
    /// With "Own in total" it stays "until I own N": the player asked for a cap (keep 1000 food in the bank), and a
    /// round only makes what was used since (ted1985 on Nexus, 26 Nov). With <paramref name="sell"/> a step that sells
    /// what was made comes last (keeping what the bank held before a "make N more"). Returns how many were added.
    /// </summary>
    public static int AddToQueue(PlanResult plan, int lane = 1, bool sell = false)
    {
        bool makeMore = plan.Everything && plan.More;
        var steps = plan.Steps.Select(line => new Step
        {
            Entity = line.Maker!.Entity.Id, TargetId = line.Maker.Target.Id, Extra = line.Maker.Target.Extra,
            Name = line.Maker.Name,
            Until = makeMore && line.Item == plan.Item ? Until.Made : Until.Item,
            Amount = makeMore && line.Item == plan.Item ? line.Missing : line.Need,
            Subject = line.Item.Id, SubjectName = line.ItemName, Lane = lane,
        }).ToList();
        if (sell && steps.Count > 0)
            steps.Add(SellStep(plan.Item, plan.More ? plan.StockBefore : 0, lane));
        Queue.AddRange(steps);
        return steps.Count;
    }

    /// <summary>A step that sells all of an item but <paramref name="keep"/>.</summary>
    public static Step SellStep(Item item, long keep, int lane = 1) => new()
    {
        Entity = Step.SellEntity, TargetId = item.Id, Extra = "", Name = "Sell: " + item.DisplayName.GetPluralText(2),
        Until = Until.Stops, Amount = keep, Subject = item.Id, SubjectName = item.DisplayName.GetPluralText(2), Lane = lane,
    };

    public static string Time(double seconds)
    {
        if (seconds < 90) return Lang.F("planner.s", "{seconds:0} s", ("seconds", Math.Max(1, Math.Round(seconds))));
        if (seconds < 5400) return Lang.F("planner.min", "{minutes:0} min", ("minutes", Math.Round(seconds / 60)));
        double hours = Math.Floor(seconds / 3600), minutes = Math.Round((seconds - hours * 3600) / 60);
        if (minutes >= 60) { hours++; minutes = 0; }
        return hours >= 48 ? Lang.P("planner.days", Math.Round(seconds / 86400, 1), "{count:0.#} day", "{count:0.#} days") : minutes == 0 ? Lang.F("planner.h", "{hours:0} h", ("hours", hours)) : Lang.F("planner.h_min", "{hours:0} h {minutes:0} min", ("hours", hours), ("minutes", minutes));
    }
}
