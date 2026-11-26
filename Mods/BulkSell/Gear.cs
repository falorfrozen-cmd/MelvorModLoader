using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Experience;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.DataStructures;
using Godot;

namespace BulkSell;

/// <summary>
/// What the window knows about gear: its slot and level, and for One Style's rare items their rarity, item level and
/// affixes. One Style is read through its public RareItems.Facts by reflection, so Bulk Sell works the same without it.
/// </summary>
public static class Gear
{
    static MethodInfo? s_facts, s_ids, s_catalog, s_newDrops;
    static PropertyInfo? s_class;
    static long s_classLookedAt = -1;   // when One Style was last looked for (-1: never)

    /// <summary>The character's One Style class as a style name ("Melee", "Ranged", "Magic"), "" without a class or without One Style.</summary>
    public static string ClassStyle()
    {
        // looked for again now and then until One Style is found (a mod loaded later, or none at all: cheap either way)
        if (s_class == null && (s_classLookedAt < 0 || System.Environment.TickCount64 - s_classLookedAt > 10_000))
        {
            s_classLookedAt = System.Environment.TickCount64;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                if (a.GetName().Name == "OneStyle")
                    s_class = a.GetType("OneStyle.Patches")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
        }
        try { string style = s_class?.GetValue(null)?.ToString() ?? ""; return style is "Melee" or "Ranged" or "Magic" ? style : ""; }
        catch (Exception) { return ""; }
    }

    /// <summary>
    /// The combat style a piece of gear is for, read from the game's own data: a weapon by its stances, other gear by the
    /// skills it asks for (Ranged, Magic; Attack, Strength or Defence alone: melee). Rings, amulets and capes fit every
    /// class (One Style asks Hitpoints for them): "". So does an offhand that only asks Defence (a shield goes with a
    /// one-handed crossbow or wand too). Not gear: "".
    /// </summary>
    public static string StyleOf(Item item, string slot)
    {
        if (slot is "Ring" or "Amulet" or "Cape") return "";
        if (item.Capabilities.TryGet(out WeaponCapability? weapon) && weapon.Type.Stances.Count > 0)
        {
            string id = weapon.Type.Stances[0].CombatStyle.Id;
            string s = id[(id.IndexOf(':') + 1)..];
            return s is "Melee" or "Ranged" or "Magic" ? s : "";
        }
        if (!item.Capabilities.TryGet(out EquipmentCapability? cap)) return "";
        bool ranged = false, magic = false, fighter = false, defence = false;
        foreach (var c in cap.Requirements.Conditions)
        {
            if (c is not LevelCondition l) continue;
            switch (l.Component.Entity.Id)
            {
                case "MelvorBase:Ranged": ranged = true; break;
                case "MelvorBase:Magic": magic = true; break;
                case "MelvorBase:Attack" or "MelvorBase:Strength": fighter = true; break;
                case "MelvorBase:Defence": defence = true; break;
            }
        }
        if (ranged) return "Ranged";
        if (magic) return "Magic";
        if (fighter) return "Melee";
        return defence && slot != "Offhand" ? "Melee" : "";
    }

    /// <summary>The groups the auto-sell page sorts affixes into (by affix id; an affix not listed here is only under "All").</summary>
    public static readonly string[] Categories = { "Attack", "On hit", "Defence", "Gains" };
    static readonly System.Collections.Generic.Dictionary<string, string> s_category = new()
    {
        ["Keen"] = "Attack", ["Swift"] = "Attack", ["True"] = "Attack", ["Deadly"] = "Attack", ["Brutal"] = "Attack",
        ["Steady"] = "Attack", ["Slayers"] = "Attack", ["Vicious"] = "Attack", ["Executing"] = "Attack", ["Raging"] = "Attack",
        ["Mastery"] = "Attack", ["Momentum"] = "Attack", ["SlayerTask"] = "Attack",
        ["Serrated"] = "On hit", ["Venomous"] = "On hit", ["Searing"] = "On hit", ["Shattering"] = "On hit", ["Concussion"] = "On hit",
        ["Frost"] = "On hit", ["Blinding"] = "On hit", ["Withering"] = "On hit", ["Retaliation"] = "On hit", ["Thorned"] = "On hit",
        ["Stalwart"] = "Defence", ["Reinforced"] = "Defence", ["Warding"] = "Defence", ["Bear"] = "Defence", ["Wall"] = "Defence",
        ["Fox"] = "Defence", ["Rock"] = "Defence", ["Cleansing"] = "Defence", ["Warding2"] = "Defence", ["Mending"] = "Defence",
        ["Feasting"] = "Defence", ["Bulwark"] = "Defence", ["Leeching"] = "Defence",
        ["Thrift"] = "Gains", ["Runes"] = "Gains", ["Quiver"] = "Gains", ["Devotion"] = "Gains", ["Plenty"] = "Gains",
        ["Coin"] = "Gains", ["Wisdom"] = "Gains", ["Haste"] = "Gains", ["Embers"] = "Gains", ["Calling"] = "Gains",
        ["Learning"] = "Gains", ["Diligence"] = "Gains",
    };

    /// <summary>An affix's group on the auto-sell page ("" when it has none).</summary>
    public static string CategoryOf(string affixId) => s_category.TryGetValue(affixId, out string? c) ? c : "";

    /// <summary>True when One Style (with its rare items) is loaded.</summary>
    public static bool RaresLoaded => Facts() != null;

    static MethodInfo? Facts()
    {
        if (s_facts != null) return s_facts;
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.GetName().Name != "OneStyle") continue;
            Type? t = a.GetType("OneStyle.RareItems");
            s_facts = t?.GetMethod("Facts", BindingFlags.Public | BindingFlags.Static, new[] { typeof(Item) });
            s_ids = t?.GetMethod("AffixIdsOf", BindingFlags.Public | BindingFlags.Static, new[] { typeof(Item) });
            s_catalog = t?.GetMethod("AffixCatalog", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
            s_newDrops = t?.GetMethod("TakeNewDrops", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
            if (s_facts != null) break;
        }
        return s_facts;
    }

    /// <summary>True when the loaded One Style can tell its new drops (auto-sell needs it; older versions cannot).</summary>
    public static bool CanAutoSell { get { Facts(); return s_newDrops != null && s_ids != null; } }

    /// <summary>The local ids of the rare items that dropped into the bank since the last call (One Style tells them once).</summary>
    public static string[] TakeNewDrops()
    {
        Facts();
        try { return s_newDrops?.Invoke(null, null) as string[] ?? Array.Empty<string>(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    /// <summary>One affix of One Style: its id, its name ("Leeching") and what it does ("Lifesteal").</summary>
    public sealed record Affix(string Id, string Name, string Text);

    /// <summary>Every affix One Style has (empty without it).</summary>
    public static System.Collections.Generic.List<Affix> Catalog()
    {
        if (s_catalogCache is { Count: > 0 }) return s_catalogCache;
        s_catalogCache = ReadCatalog();
        return s_catalogCache;
    }

    static System.Collections.Generic.List<Affix>? s_catalogCache;

    static System.Collections.Generic.List<Affix> ReadCatalog()
    {
        Facts();
        try
        {
            if (s_catalog?.Invoke(null, null) is string[] lines)
                return lines.Select(l => l.Split('\t')).Where(p => p.Length >= 3).Select(p => new Affix(p[0], p[1], AffixName(p[2]))).ToList();
        }
        catch (Exception) { }
        return new();
    }

    /// <summary>Fills the gear part of an entry.</summary>
    public static void Describe(Entry e)
    {
        Item item = e.Item;
        if (item.Capabilities.TryGet(out EquipmentCapability? cap))
        {
            string id = cap.Type.Id;
            e.Slot = id[(id.IndexOf(':') + 1)..];
            int level = 0;
            foreach (var c in cap.Requirements.Conditions)
                if (c is LevelCondition l) level = Math.Max(level, l.Level);
            e.Level = level;
            e.Style = StyleOf(item, e.Slot);
        }
        string[]? facts = null;
        try { facts = Facts()?.Invoke(null, new object[] { item }) as string[]; }
        catch (Exception) { }
        if (facts is not { Length: >= 4 }) return;
        e.Rarity = facts[0];
        if (int.TryParse(facts[1], out int ilvl)) e.Level = ilvl;
        e.BaseName = facts[2];
        e.Color = Color.FromHtml(facts[3]);
        e.Affixes = facts.Skip(4).ToArray();
        try { e.AffixIds = s_ids?.Invoke(null, new object[] { item }) as string[] ?? Array.Empty<string>(); }
        catch (Exception) { }
    }

    /// <summary>An affix line with its numbers taken out ("+12% max hit" -> "+#% max hit"): the same affix on every item.</summary>
    public static string AffixKind(string line) => Regex.Replace(line, @"\d+(\.\d+)?", "#");

    /// <summary>An affix kind as the filter list shows it: "+#% max hit" -> "Max hit", "#% lifesteal" -> "Lifesteal".</summary>
    public static string AffixName(string kind)
    {
        string s = Regex.Replace(kind, @"[+-]?#%?", "").Trim();
        s = Regex.Replace(s, @"\s+", " ");
        return s.Length == 0 ? kind : char.ToUpperInvariant(s[0]) + s[1..];
    }
}
