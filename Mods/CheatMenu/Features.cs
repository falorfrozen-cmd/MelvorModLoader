using System;
using System.Collections.Generic;
using System.Linq;

namespace CheatMenu;

/// <summary>One bonus the game's own modifier system understands: a stat on one component of one entity.</summary>
public sealed record StatRef(string StatsType, string Name, string Entity, string Component, double Unit)
{
    /// <summary>More of the stat's context as JSON fields (e.g. "\"Category\": \"MelvorBase:SlayerZones\""), or null.</summary>
    public string? Context { get; init; }
}

/// <summary>A game-wide bonus group (applies to every skill at once).</summary>
public sealed record GlobalRef(string StatGroup, double Unit);

public enum FeatureKind { Toggle, Number }

/// <summary>
/// One row in the menu. The menu value is turned into a multiplier; every stat receives Unit x multiplier.
/// A multiplier of 0 means "off" and removes the bonus completely.
/// </summary>
public sealed class Feature
{
    public required string Id { get; init; }
    public required string Tab { get; init; }
    public required string En { get; init; }
    public FeatureKind Kind { get; init; } = FeatureKind.Number;
    public double Min { get; init; }
    public double Max { get; init; } = 1;
    public double Step { get; init; } = 1;
    /// <summary>Menu value that means "no change".</summary>
    public double Off { get; init; }
    public string Suffix { get; init; } = "";
    public Func<double, double> ToMultiplier { get; init; } = v => v;
    public List<StatRef> Stats { get; init; } = new();
    public List<GlobalRef> Globals { get; init; } = new();
    /// <summary>True for features handled in code (game speed) instead of the modifier system.</summary>
    public bool Custom { get; init; }
    /// <summary>Per-skill table only: which skill row and which column ("xp" or "speed") this belongs to.</summary>
    public string? Skill { get; init; }
    public string? Column { get; init; }
}

public static class Features
{
    public const string TabBank = "bank", TabXp = "xp", TabSpeed = "speed", TabSkills = "skills", TabCombat = "combat", TabPerSkill = "perskill";

    // (entity, stats class, component) for every skill that performs actions.
    static readonly (string Entity, string Class, string Component)[] Skills =
    {
        ("Woodcutting", "WoodcuttingAction", "Action"), ("Mining", "MiningAction", "Action"),
        ("Fishing", "FishingAction", "Action"), ("Firemaking", "FiremakingAction", "Action"),
        ("Cooking", "CookingAction", "Action"), ("Smithing", "ArtisanAction", "Action"),
        ("Crafting", "ArtisanAction", "Action"), ("Runecrafting", "ArtisanAction", "Action"),
        ("Fletching", "ArtisanAction", "Action"), ("Beastcrafting", "ArtisanAction", "Action"),
        ("Herblore", "HerbloreAction", "Action"), ("Thieving", "ThievingAction", "Action"),
        ("Agility", "Agility", "Action"), ("Astrology", "AstrologyAction", "Action"),
        ("Hunting", "HuntingAction", "Action"), ("Farming", "FarmingAction", "Passive"),
        ("Ranching", "RanchingAction", "Passive"),
    };

    /// <summary>The same stat name on every skill whose stats class is listed.</summary>
    static List<StatRef> OnSkills(string stat, double unit, params string[] classes) =>
        Skills.Where(s => classes.Contains(s.Class))
              .Select(s => new StatRef(s.Class, stat, s.Entity, s.Component, unit)).ToList();

    static StatRef Player(string stat, double unit) => new("Character", stat, "Combat", "Player", unit);
    static StatRef Combat(string cls, string stat, string component, double unit) => new(cls, stat, "Combat", component, unit);

    static double Percent(double v) => v / 100.0;       // 0..100 slider -> fraction
    static double Times(double v) => v - 1.0;            // "x5" -> +400%
    static double Faster(double v) => 1.0 - 1.0 / v;     // "x5 speed" -> -80% time (Unit is -1)

    static readonly List<Feature> Core = new()
    {
        // ---------------- Bank ----------------
        new() { Id = "bank.slots", Tab = TabBank, En = "Extra bank slots",
                Max = 10000, Step = 50, Stats = { new("BankInventory", "FlatMaxStacks", "Bank", "Inventory", 1) } },
        new() { Id = "bank.sellPrice", Tab = TabBank, En = "Sell price", Suffix = "x",
                Min = 1, Max = 100, Off = 1, ToMultiplier = Times, Stats = { new("ItemSale", "PercentPricePerItem", "Bank", "ItemSales", 1) } },
        new() { Id = "bank.buryReward", Tab = TabBank, En = "Bone burying reward", Suffix = "x",
                Min = 1, Max = 100, Off = 1, ToMultiplier = Times, Stats = { new("ItemBurying", "PercentReward", "Bank", "ItemBurying", 1) } },

        // ---------------- XP ----------------
        new() { Id = "xp.multiplier", Tab = TabXp, En = "XP multiplier", Suffix = "x",
                Min = 1, Max = 1000, Off = 1, ToMultiplier = Times, Globals = { new("GlobalPercentXpGained", 1) } },

        // ---------------- Speed ----------------
        new() { Id = "speed.game", Tab = TabSpeed, En = "Game speed", Suffix = "x",
                Min = 1, Max = 20, Off = 1, Custom = true },
        // The passive skills at their normal pace while the rest of the game runs faster (see NormalPace).
        new() { Id = "speed.farmingNormal", Tab = TabSpeed, En = "Farming: normal pace", Kind = FeatureKind.Toggle, Custom = true },
        new() { Id = "speed.ranchingNormal", Tab = TabSpeed, En = "Ranching: normal pace", Kind = FeatureKind.Toggle, Custom = true },
        new() { Id = "speed.actions", Tab = TabSpeed, En = "Skill action speed", Suffix = "x",
                Min = 1, Max = 20, Off = 1, ToMultiplier = Faster, Globals = { new("GlobalPercentActionInterval", -1) } },
        new() { Id = "speed.spawn", Tab = TabSpeed, En = "Monster respawn speed", Suffix = "x",
                Min = 1, Max = 20, Off = 1, ToMultiplier = Faster, Stats = { Combat("CombatAction", "PercentSpawnInterval", "Action", -1) } },
        new() { Id = "speed.offlineHours", Tab = TabSpeed, En = "Extra offline time limit", Suffix = " h",
                Max = 720, Stats = { new("OfflineProgress", "FlatOfflineTime", "OfflineProgress", "Component", 3600000) } },

        // ---------------- Skills ----------------
        new() { Id = "skills.doubling", Tab = TabSkills, En = "Double product chance", Suffix = "%",
                Max = 100, Step = 5, ToMultiplier = Percent,
                Stats = OnSkills("DoublingChance", 1, "WoodcuttingAction", "MiningAction", "FishingAction", "FiremakingAction", "CookingAction",
                                 "ArtisanAction", "HerbloreAction", "ThievingAction", "Agility", "HuntingAction", "FarmingAction", "RanchingAction")
                        .Append(new StatRef("AstrologyAction", "ItemDoublingChance", "Astrology", "Action", 1)).ToList() },
        new() { Id = "skills.preservation", Tab = TabSkills, En = "Keep materials chance", Suffix = "%",
                Max = 100, Step = 5, ToMultiplier = Percent,
                Stats = OnSkills("PreservationChance", 1, "ArtisanAction", "CookingAction", "FarmingAction", "FiremakingAction",
                                 "HerbloreAction", "HuntingAction", "RanchingAction") },
        new() { Id = "skills.quantityPercent", Tab = TabSkills, En = "Product quantity", Suffix = "x",
                Min = 1, Max = 100, Off = 1, ToMultiplier = Times,
                Stats = OnSkills("PercentProductQuantity", 1, "ArtisanAction", "CookingAction", "FarmingAction", "HerbloreAction",
                                 "HuntingAction", "RanchingAction") },
        new() { Id = "skills.quantityFlat", Tab = TabSkills, En = "Extra products per action", Suffix = "",
                Max = 1000,
                Stats = OnSkills("FlatProductQuantity", 1, "WoodcuttingAction", "MiningAction", "FiremakingAction", "ArtisanAction",
                                 "CookingAction", "FarmingAction", "HerbloreAction", "HuntingAction", "RanchingAction") },
        new() { Id = "skills.cheaper", Tab = TabSkills, En = "Material cost reduction", Suffix = "%",
                Max = 100, Step = 5, ToMultiplier = Percent,
                Stats = OnSkills("PercentIngredientCost", -1, "ArtisanAction", "CookingAction", "FarmingAction", "FiremakingAction",
                                 "HerbloreAction", "RanchingAction") },
        new() { Id = "skills.instantCut", Tab = TabSkills, En = "Woodcutting: instant cut chance", Suffix = "%",
                Max = 100, Step = 5, ToMultiplier = Percent, Stats = { new("WoodcuttingAction", "InstantCutChance", "Woodcutting", "Action", 1) } },
        new() { Id = "skills.noStun", Tab = TabSkills, En = "Thieving: never stunned", Kind = FeatureKind.Toggle,
                Stats = { new("ThievingAction", "StunAvoidanceChance", "Thieving", "Action", 1) } },
        // Getting caught does two things in the game: damage first, then a stun. Each has its own avoid chance.
        new() { Id = "skills.thievingNoDamage", Tab = TabSkills, En = "Thieving: no damage when caught", Kind = FeatureKind.Toggle,
                Stats = { new("ThievingAction", "DamageAvoidanceChance", "Thieving", "Action", 1) } },
        // Only the chance to avoid the damage: setting "Damage Taken" to -100% would make the game roll a number
        // between 1 and 0 for the damage.
        new() { Id = "skills.agilityNoDamage", Tab = TabSkills, En = "Agility: no damage from a failed obstacle", Kind = FeatureKind.Toggle,
                Stats = { new("Agility", "AvoidDamageChance", "Agility", "Action", 1) } },
        new() { Id = "skills.agilityKeepReward", Tab = TabSkills, En = "Agility: keep the reward of a failed obstacle", Kind = FeatureKind.Toggle,
                Stats = { new("Agility", "KeepObstacleRewardOnFail", "Agility", "Action", 1) } },
        new() { Id = "skills.astroKnowledge", Tab = TabSkills, En = "Astrology: knowledge per study", Suffix = "x",
                Min = 1, Max = 1000, Off = 1, ToMultiplier = Times, Stats = { new("AstrologyAction", "PercentKnowledge", "Astrology", "Action", 1) } },
        // The game's own "a minute of life went by" message is answered before its death check (see RanchingForever).
        new() { Id = RanchingForever.Setting, Tab = TabSkills, En = "Ranching: animals never die", Kind = FeatureKind.Toggle, Custom = true },

        // ---------------- Combat ----------------
        new() { Id = "combat.god", Tab = TabCombat, En = "God mode", Kind = FeatureKind.Toggle,
                Stats = { Player("AttackDamageNegationChance", 1), Player("PercentDamageTaken", -1) } },
        new() { Id = "combat.damage", Tab = TabCombat, En = "Damage dealt", Suffix = "x",
                Min = 1, Max = 1000, Off = 1, ToMultiplier = Times, Stats = { Player("PercentDamageDealt", 1) } },
        new() { Id = "combat.attackSpeed", Tab = TabCombat, En = "Attack speed", Suffix = "x",
                Min = 1, Max = 10, Off = 1, ToMultiplier = Faster, Stats = { Player("PercentAttackInterval", -1) } },
        new() { Id = "combat.accuracy", Tab = TabCombat, En = "Accuracy", Suffix = "x",
                Min = 1, Max = 100, Off = 1, ToMultiplier = Times, Stats = { Player("PercentAccuracyRating", 1) } },
        new() { Id = "combat.maxHp", Tab = TabCombat, En = "Maximum hitpoints", Suffix = "x",
                Min = 1, Max = 100, Off = 1, ToMultiplier = Times, Stats = { Player("PercentMaxHitpoints", 1) } },
        new() { Id = "combat.noDeathPenalty", Tab = TabCombat, En = "No death penalty", Kind = FeatureKind.Toggle,
                Stats = { Combat("CombatAction", "NoDeathPenalty", "Action", 1) } },
        new() { Id = "combat.lootDouble", Tab = TabCombat, En = "Double loot chance", Suffix = "%",
                Max = 100, Step = 5, ToMultiplier = Percent,
                Stats = { Combat("CombatLoot", "ItemDoublingChance", "Loot", 1), Combat("CombatLoot", "CurrencyDoublingChance", "Loot", 1),
                          Combat("CombatLoot", "BuriableDoublingChance", "Loot", 1) } },
        new() { Id = "combat.keepSupplies", Tab = TabCombat, En = "Keep supplies chance", Suffix = "%",
                Max = 100, Step = 5, ToMultiplier = Percent,
                Stats = { Combat("CombatFood", "FoodPreservationChance", "Food", 1), Combat("AmmoConsumption", "AmmoPreservationChance", "Player", 1),
                          Combat("MagicSpell", "RunePreservationChance", "MagicSpells", 1), Combat("Prayer", "CostPreservationChance", "Prayer", 1) } },
        // +10000%: the game keeps the ammo without rolling at 100% or more, and no other bonus can pull this back
        // under 100%. The game's "ammo used" event is watched as well (see InfiniteAmmo).
        new() { Id = InfiniteAmmo.Setting, Tab = TabCombat, En = "Infinite ammo", Kind = FeatureKind.Toggle,
                Stats = { Combat("AmmoConsumption", "AmmoPreservationChance", "Player", 100) } },
        // A monster's "Immune to Melee/Ranged/Magic" passive gives it ImmunityFrom = 1 for that style; the game lets
        // every such attack miss and keeps poison and bleeding off it while the total is above 0. -100 on the enemy
        // brings any total back under 0, whatever style is used.
        new() { Id = "combat.ignoreImmunity", Tab = TabCombat, En = "Ignore immunity", Kind = FeatureKind.Toggle,
                Stats = { Combat("Target", "ImmunityFrom", "Enemy", -100) } },
        // A lit lantern is used up each time the player is hit in a dark area: the equipment slot rolls its
        // PreservationChance first. The lit lanterns are the only worn items the game uses up this way (ammo has its
        // own stat), so +10000% on the combat equipment keeps exactly them.
        new() { Id = "combat.infiniteLanterns", Tab = TabCombat, En = "Infinite lanterns", Kind = FeatureKind.Toggle,
                Stats = { Combat("Equipment", "PreservationChance", "Equipment", 100) } },
        // The shop's "<area> - Area Effect Removal" purchases each give DisableAreaEffect = 1 for one slayer area;
        // the game then works the area's effect out to strength 0 (also on the area's page). The same stat for the
        // whole Slayer Zones category does all of them at once, nothing is bought, and switched off they come back.
        new() { Id = "combat.noSlayerAreaEffects", Tab = TabCombat, En = "No slayer area penalties", Kind = FeatureKind.Toggle,
                Stats = { Combat("CombatAreas", "DisableAreaEffect", "Areas", 1) with { Context = "\"Category\": \"MelvorBase:SlayerZones\"" } } },
        new() { Id = "combat.slayerReward", Tab = TabCombat, En = "Slayer coin reward", Suffix = "x",
                Min = 1, Max = 100, Off = 1, ToMultiplier = Times, Stats = { new("SlayerTasks", "PercentRewardCurrency", "Slayer", "Tasks", 1) } },
        new() { Id = "combat.slayerFreeReroll", Tab = TabCombat, En = "Cheap slayer task rerolls", Kind = FeatureKind.Toggle,
                Stats = { new("SlayerTasks", "PercentRerollCost", "Slayer", "Tasks", -1) } },
        new() { Id = SlayerLength.Setting, Tab = TabCombat, En = "Slayer task: most kills", Max = 300, Step = 1, Custom = true },
    };

    /// <summary>Every skill in menu order.</summary>
    public static readonly string[] SkillNames =
    {
        "Woodcutting", "Mining", "Fishing", "Firemaking", "Cooking", "Smithing", "Crafting", "Runecrafting", "Fletching",
        "Beastcrafting", "Herblore", "Thieving", "Agility", "Astrology", "Hunting", "Farming", "Ranching",
        "Attack", "Strength", "Defence", "Hitpoints", "Ranged", "Magic", "Prayer", "Slayer",
    };

    // The stats that set how long one action takes, per stats class.
    static readonly Dictionary<string, string[]> IntervalStats = new()
    {
        ["WoodcuttingAction"] = new[] { "PercentInterval" }, ["MiningAction"] = new[] { "PercentInterval" },
        ["FiremakingAction"] = new[] { "PercentInterval" }, ["CookingAction"] = new[] { "PercentInterval" },
        ["ArtisanAction"] = new[] { "PercentInterval" }, ["ThievingAction"] = new[] { "PercentInterval" },
        ["Agility"] = new[] { "PercentInterval" }, ["AstrologyAction"] = new[] { "PercentInterval" },
        ["FarmingAction"] = new[] { "PercentInterval" },
        ["FishingAction"] = new[] { "PercentMinInterval", "PercentMaxInterval" },
        ["HerbloreAction"] = new[] { "PercentPreparingInterval", "PercentHeatingInterval", "PercentStirringInterval", "PercentBottlingInterval" },
        ["HuntingAction"] = new[] { "PercentTrackingInterval", "PercentTrappingInterval", "PercentSurveyInterval" },
        ["RanchingAction"] = new[] { "PercentProductInterval" },
    };

    /// <summary>
    /// The settings that keep a passive skill at its normal pace: Game speed's extra ticks skip the skill's
    /// passive parts, and what "Skill action speed" takes off its times is given back. The skill's own speed on
    /// the Per skill page still counts.
    /// </summary>
    public static readonly (string Setting, string Skill, string Entity)[] NormalPace =
    {
        ("speed.farmingNormal", "Farming", "MelvorBase:Farming"),
        ("speed.ranchingNormal", "Ranching", "MelvorBase:Ranching"),
    };

    public static bool IsNormalPace(string id) => NormalPace.Any(n => n.Setting == id);

    /// <summary>The normal-pace setting of a skill, or null when it has none.</summary>
    public static string? NormalPaceOf(string? skill) => NormalPace.FirstOrDefault(n => n.Skill == skill).Setting;

    /// <summary>Two settings per skill: its own XP multiplier and, for skills that perform actions, its own speed.</summary>
    static IEnumerable<Feature> PerSkill()
    {
        foreach (string name in SkillNames)
        {
            yield return new Feature
            {
                Id = $"skill.{name}.xp", Tab = TabPerSkill, En = name + " XP", Skill = name, Column = "xp", Suffix = "x",
                Min = 1, Max = 1000, Off = 1, ToMultiplier = Times, Stats = { new("Experience", "PercentXPGained", name, "Experience", 1) }
            };
            var skill = Skills.FirstOrDefault(s => s.Entity == name);
            if (skill.Entity == null) continue;
            var stats = IntervalStats[skill.Class].Select(stat => new StatRef(skill.Class, stat, skill.Entity, skill.Component, -1)).ToList();
            // Farming's grain mill has a time of its own, which "Skill action speed" shortens as well.
            if (name == "Farming") stats.Add(new StatRef("GrainMill", "PercentInterval", "Farming", "GrainMill", -1));
            // The game never reads Ranching's "product interval" bonus (the product time is a fixed hour), so
            // Ranching's speed is extra ticks for its animals, handled in code (see GameSpeed).
            bool ticks = name == "Ranching";
            yield return new Feature
            {
                Id = $"skill.{name}.speed", Tab = TabPerSkill, En = name + " speed", Skill = name, Column = "speed", Suffix = "x",
                Min = 1, Max = 20, Off = 1, ToMultiplier = Faster, Stats = ticks ? new() : stats, Custom = ticks
            };
        }
    }

    public static readonly IReadOnlyList<Feature> All = Core.Concat(PerSkill()).ToList();

    /// <summary>One short line shown under a setting's name.</summary>
    public static readonly Dictionary<string, string> Hints = new()
    {
        ["bank.slots"] = "Adds bank slots on top of the ones you own.",
        ["bank.sellPrice"] = "Multiplies what you get for selling items.",
        ["bank.buryReward"] = "Multiplies what you get for burying bones.",
        ["xp.multiplier"] = "Multiplies all XP you gain, in every skill.",
        ["speed.game"] = "Runs the whole game faster: skills, combat and timers.",
        ["speed.farmingNormal"] = "Crops and the grain mill keep their normal pace when Game speed or Skill action speed is raised. Farming's own speed (Per skill page) still counts.",
        ["speed.ranchingNormal"] = "Animals grow, eat and produce at their normal pace when Game speed is raised. Ranching's own speed (Per skill page) still counts.",
        ["speed.actions"] = "Shortens every skill action. Combat is not affected. Multiplies with a skill's own speed on the Per skill page (x2 here and x4 there is x8); together they stop at x20.",
        ["speed.spawn"] = "Monsters appear sooner after a kill.",
        ["speed.offlineHours"] = "Raises the limit of offline progress you can collect.",
        ["skills.doubling"] = "Chance that a skill action gives double products.",
        ["skills.preservation"] = "Chance that an action does not use up its materials.",
        ["skills.quantityPercent"] = "Multiplies how many items an action produces.",
        ["skills.quantityFlat"] = "Adds this many extra items to every action.",
        ["skills.cheaper"] = "Recipes need fewer materials.",
        ["skills.instantCut"] = "Chance to fell a tree with a single hit.",
        ["skills.noStun"] = "A failed pickpocket never stuns you (it can still hurt: see the next setting).",
        ["skills.thievingNoDamage"] = "A failed pickpocket no longer hurts you (God mode only covers combat).",
        ["skills.agilityNoDamage"] = "Failing an agility obstacle no longer hurts you (God mode only covers combat).",
        ["skills.agilityKeepReward"] = "Failing an agility obstacle no longer loses the item reward it was carrying.",
        ["skills.astroKnowledge"] = "Multiplies the Knowledge each study adds to a constellation. Astrology XP is a separate setting.",
        ["combat.god"] = "You take no damage in combat.",
        ["combat.damage"] = "Multiplies all damage you deal.",
        ["combat.attackSpeed"] = "You attack this many times faster.",
        ["combat.accuracy"] = "Multiplies your accuracy rating.",
        ["combat.maxHp"] = "Multiplies your maximum hitpoints.",
        ["combat.noDeathPenalty"] = "Dying no longer costs you an item.",
        ["combat.lootDouble"] = "Chance that a kill drops double items, coins and bones.",
        ["combat.keepSupplies"] = "Chance not to use up food, ammo, runes or prayer points.",
        [InfiniteAmmo.Setting] = "Arrows, bolts, javelins, boomerangs and throwing hammers are never used up: one is enough.",
        [RanchingForever.Setting] = "Ranch animals stop aging: their lifetime stands still, so they never die of old age.",
        ["combat.infiniteLanterns"] = "Lit lanterns are never used up in dark areas: one is enough.",
        ["combat.ignoreImmunity"] = "Any attack style hits monsters that are immune to it (Wind Drifter, for example). Poison and bleeding stick to them too.",
        ["combat.noSlayerAreaEffects"] = "Slayer areas lose their bad area effects (burn, less healing from food, quicksand...), as if you had bought every Area Effect Removal in the shop.",
        ["combat.slayerReward"] = "Multiplies the slayer coins earned per task kill.",
        ["combat.slayerFreeReroll"] = "Changing your slayer task costs 1 coin (the game's minimum).",
        [SlayerLength.Setting] = "No slayer task asks for more kills than this, at every master and also when extended (0 keeps the game's own lengths).",
    };

    static readonly Dictionary<string, Feature> ById = All.ToDictionary(f => f.Id);

    public static Feature Get(string id) => ById[id];
    public static Feature? Find(string id) => ById.GetValueOrDefault(id);
}
