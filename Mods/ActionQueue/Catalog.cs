using System;
using System.Collections.Generic;
using System.Linq;
using CheatMenu;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Components.ArtisanAction;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.Experience;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Data.Recipes;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Agility;
using GamesByMalcs.Melvor2.Entities.Astrology;
using GamesByMalcs.Melvor2.Entities.Bank;
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
using Godot;

namespace ActionQueue;

/// <summary>
/// What the pick lists show: each skill's targets grouped by the game's own categories, in the game's own order
/// (the order of the skill page), with the game's icons. A target the game does not show yet (a quest target while
/// its quest step is not active) is left out, as on the skill page; one the character may not do yet is shown
/// dimmed, with the level it needs, because a queue may plan it for later. Only for showing: the runner and
/// <see cref="Targets.All"/> are unchanged.
/// </summary>
internal static class Catalog
{
    /// <summary>A target's key in the lists: its id, and the combat area for a monster that lives in several.</summary>
    public static string Key(string id, string extra) => extra == "" ? id : extra + "|" + id;
    public static string Key(Target t) => Key(t.Id, t.Extra);

    sealed record Info(Target Target, RecipeCategory? Category, string Group, Texture2D? GroupIcon, string Subgroup,
        Texture2D? Icon, ConditionGroup? Requirements, bool Shown);

    static bool Shown(ConditionGroup? display, RecipeCategory? category) =>
        (display?.AreMet ?? true) && (category?.DisplayRequirements?.AreMet ?? true);

    static Info Of(string id, string name, RecipeCategory? category, RecipeSubcategory? sub, Texture2D? icon, ConditionGroup? req, ConditionGroup? display, string extra = "") =>
        new(new Target(id, name, extra), category, category?.DisplayName.Text ?? "", category?.Icon, sub?.DisplayName.Text ?? "", icon, req, Shown(display, category));

    static List<Info> Infos(Game game, ActionComponent action) => action switch
    {
        WoodcuttingActionComponent c => c.Trees.Keys.Select(r => Of(r.Id, r.DisplayName.Text, r.Category, r.Subcategory, r.Media, r.Requirements, r.DisplayRequirements)).ToList(),
        MiningActionComponent c => c.Rocks.Keys.Select(r => Of(r.Id, r.DisplayName.Text, r.Category, r.Subcategory, r.Media, r.Requirements, r.DisplayRequirements)).ToList(),
        FishingActionComponent c => c.Locations.Values.SelectMany(l => l.Recipes).Select(a => a.Recipe)
            .Select(r => Of(r.Id, r.DisplayName.Text, r.Location, r.Subcategory, r.Icon, r.Requirements, r.DisplayRequirements)).ToList(),
        ArtisanActionComponent c => c.ActiveRecipes.Keys.Select(r => Of(r.Id, r.DisplayName.Text, r.Category, r.Subcategory, r.Icon, r.Requirements, r.DisplayRequirements)).ToList(),
        CookingActionComponent c => c.ActiveRecipes.Keys.Select(r => Of(r.Id, r.DisplayName.Text, r.Category, r.Subcategory, r.Icon, r.Requirements, r.DisplayRequirements)).ToList(),
        FiremakingActionComponent c => c.ActiveRecipes.Keys.Select(r => Of(r.Id, r.DisplayName.Text, r.Category, r.Subcategory, r.Icon, r.Requirements, r.DisplayRequirements)).ToList(),
        HerbloreActionComponent c => c.ActiveRecipes.Keys.Select(r => Of(r.Id, r.DisplayName.Text, r.Category, r.Subcategory, r.Icon, r.Requirements, r.DisplayRequirements)).ToList(),
        ThievingActionComponent c => c.ActiveRecipes.Keys.Select(r => Of(r.Id, r.DisplayName.Text, r.Category, null, r.Media, r.Requirements, r.DisplayRequirements) with { Subgroup = r.RecipeType?.DisplayName.Text ?? "" }).ToList(),
        AgilityActionComponent c => c.Courses.Keys.Select(r => Of(r.Id, r.DisplayName.Text, r.Location, r.Subcategory, r.Media, r.Requirements, r.DisplayRequirements)).ToList(),
        AstrologyActionComponent c => c.ActiveConstellations.Keys.Select(r => Of(r.Id, r.DisplayName.Text, r.Category, null, r.Icon, r.Requirements, r.DisplayRequirements)).ToList(),
        HuntingActionComponent c => c.Locations.Values.SelectMany(l => l.Animals).Select(a => a.Animal)
            .Select(r => Of(r.Id, r.DisplayName.Text, r.Location, r.Subcategory, r.Icon, r.Requirements, r.DisplayRequirements)).ToList(),
        CombatActionComponent c => Combat(game, c),
        _ => new List<Info>(),
    };

    /// <summary>Slayer masters first, then every area by the game's area category: monsters under their area, dungeons on their own.</summary>
    static List<Info> Combat(Game game, CombatActionComponent c)
    {
        var list = new List<Info>();
        if (c.Entity is not CombatEntity combat) return list;
        if (game.Entities.TryGetObjectById("MelvorBase:Slayer", out GameEntity? s) && s is SlayerEntity slayer)
            foreach (var master in slayer.Tasks.ActiveMasters.Keys)
                list.Add(new Info(new Target(master.Id, $"Slayer tasks of {master.DisplayName.Text}  (follows the task)", Targets.Slayer),
                    null, Lang.T("catalog.slayer_tasks", "Slayer tasks"), null, "", master.Icon, master.UnlockRequirements, true));
        foreach (BaseCombatArea area in combat.Areas.ActiveAreas.Keys)
        {
            string group = area.Category?.DisplayName.Text ?? Lang.T("catalog.combat_group", "Combat");
            bool shown = area.DisplayRequirements?.AreMet ?? true;
            if (area is CombatArea normal)
                foreach (Enemy e in normal.Enemies)
                    list.Add(new Info(new Target(e.Id, $"{e.DisplayName.Text}  ({area.DisplayName.Text})", area.Id), null, group, area.Category?.Icon,
                        area.DisplayName.Text, e.Sprite, area.UnlockRequirements, shown && (e.DisplayRequirements?.AreMet ?? true)));
            else if (area is Dungeon dungeon)
                list.Add(new Info(new Target(dungeon.Id, $"Dungeon: {dungeon.DisplayName.Text}", Targets.Dungeon), null, group, area.Category?.Icon,
                    "", dungeon.Icon, area.UnlockRequirements, shown));
        }
        return list;
    }

    /// <summary>The ids of the targets of this skill the game does not show right now (the planner leaves them out).</summary>
    public static HashSet<string> HiddenIds(Game game, ActionComponent action)
    {
        try { return Infos(game, action).Where(i => !i.Shown).Select(i => i.Target.Id).ToHashSet(); }
        catch { return new HashSet<string>(); }
    }

    /// <summary>"Lv 25" for the first level the character is still missing (another skill's is named), else "".</summary>
    static string LevelHint(ConditionGroup? req, GameEntity skill)
    {
        if (req == null) return "";
        foreach (LevelCondition level in req.Conditions.OfType<LevelCondition>())
        {
            if (level.IsMet) continue;
            GameEntity? of = level.Component?.Entity;
            return of == null || ReferenceEquals(of, skill) ? Lang.F("catalog.lv", "Lv {level}", ("level", level.Level)) : $"{of.DisplayName.Text} {level.Level}";
        }
        return "";
    }

    /// <summary>
    /// The entries for one skill's target list. <paramref name="keepKey"/> is kept in even when the game hides it
    /// now (a saved step), so the choice is not lost.
    /// </summary>
    public static List<PickEntry<Target>> For(Game game, GameEntity skill, ActionComponent action, string? keepKey = null)
    {
        var list = new List<PickEntry<Target>>();
        List<Info> infos;
        try { infos = Infos(game, action); }
        catch (Exception e) { Queue.Log($"Catalog: {skill.DisplayName.Text} could not be read: {e.Message}"); infos = new List<Info>(); }
        // A skill the catalog cannot read still gets a usable, flat list.
        if (infos.Count == 0) infos = Targets.All(action).Select(t => new Info(t, null, "", null, "", null, null, true)).ToList();
        bool oneGroup = infos.Where(i => i.Shown).Select(i => i.Group).Distinct().Count() <= 1;
        // Pictures: the game leaves some empty ("missing artwork", e.g. every fishing spot) and gives some groups a
        // wide button picture (fishing locations) that shrinks to nothing. Then the thing it makes stands in for an
        // entry, and the group's first entry for the group.
        // Last of all the skill's own icon (agility courses make nothing, some recipes and monsters have no picture).
        Texture2D? skillIcon = Usable(skill.Icon) ? skill.Icon : null;
        Texture2D? IconOf(Info i) => Usable(i.Icon) ? i.Icon : Targets.Product(action, i.Target)?.Icon is { } made && Usable(made) ? made : skillIcon;
        var groupIcons = new Dictionary<string, Texture2D?>();
        foreach (Info i in infos.Where(i => i.Shown || Key(i.Target) == keepKey))
            if (!groupIcons.ContainsKey(i.Group))
                groupIcons[i.Group] = Usable(i.GroupIcon) && Square(i.GroupIcon!) ? i.GroupIcon : IconOf(i);
        foreach (Info i in infos)
        {
            string key = Key(i.Target);
            if (!i.Shown && key != keepKey) continue;
            bool can = Targets.CanStart(action, i.Target, out string problem);
            string hint = can ? "" : LevelHint(i.Requirements, skill);
            if (!can && hint == "") hint = Lang.T("catalog.target.locked", "locked");
            string tip = can ? "" : char.ToUpper(problem[0]) + problem[1..] + ".";
            if (!i.Shown) { hint = Lang.T("catalog.hidden_now", "hidden now"); tip = Lang.T("catalog.game_shows_one_only_at", "The game shows this one only at certain times (a quest step)."); }
            list.Add(new PickEntry<Target>(key, i.Target.Name, i.Target, oneGroup ? "" : i.Group, oneGroup ? "" : i.Subgroup,
                IconOf(i), groupIcons.GetValueOrDefault(i.Group), hint, tip, Enabled: true, Muted: !can || !i.Shown, Keywords: i.Group + " " + i.Subgroup));
        }
        return list;
    }

    /// <summary>False for no picture, and for the game's own "missing artwork" placeholder.</summary>
    static bool Usable(Texture2D? t) => t != null && !(t.ResourcePath ?? "").Contains("missing_artwork", StringComparison.OrdinalIgnoreCase);

    /// <summary>A picture that still reads at icon size (not a wide button or banner).</summary>
    static bool Square(Texture2D t)
    {
        float w = t.GetWidth(), h = t.GetHeight();
        return h > 0 && w / h > 0.5f && w / h < 2f;
    }

    /// <summary>An item list grouped by the bank's own kinds (shared with the Cheat Menu).</summary>
    public static List<PickEntry<Item>> Items(Game game, IEnumerable<Item> items) => ItemPicks.For(game, items);
}
