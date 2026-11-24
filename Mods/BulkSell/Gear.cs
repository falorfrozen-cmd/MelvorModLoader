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
    static MethodInfo? s_facts;

    /// <summary>True when One Style (with its rare items) is loaded.</summary>
    public static bool RaresLoaded => Facts() != null;

    static MethodInfo? Facts()
    {
        if (s_facts != null) return s_facts;
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.GetName().Name != "OneStyle") continue;
            s_facts = a.GetType("OneStyle.RareItems")?.GetMethod("Facts", BindingFlags.Public | BindingFlags.Static, new[] { typeof(Item) });
            if (s_facts != null) break;
        }
        return s_facts;
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
