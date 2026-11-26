using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CheatMenu;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Combat;
using Godot;

namespace ActionQueue;

/// <summary>
/// One Style's rare items (Magic, Rare and Unique gear): each one is an item of its own that One Style adds to the
/// game while it is played ("OsR..." for one drop, "OsU..." for a unique), and a sold one stays in the game until it
/// is restarted. The pick lists show them only while the character owns them, in their rarity colour. One Style is
/// read through its public RareItems.Facts by reflection, so the queue works the same without it.
/// </summary>
internal static class Rares
{
    static MethodInfo? s_facts;

    /// <summary>True for an item One Style made (its local id starts with "Os").</summary>
    public static bool Is(Item item) => item.LocalId.StartsWith("Os", StringComparison.Ordinal);

    /// <summary>True for the id of a single rare drop (sold ones go away when the game restarts).</summary>
    public static bool IsDropId(string id) => id.AsSpan(id.IndexOf(':') + 1).StartsWith("OsR", StringComparison.Ordinal);

    static MethodInfo? FactsMethod()
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

    /// <summary>One Style's facts of the item: rarity, item level, base item, colour, then one line per affix (null when it has none).</summary>
    static string[]? Facts(Item item)
    {
        try { return FactsMethod()?.Invoke(null, new object[] { item }) is string[] { Length: >= 4 } facts ? facts : null; }
        catch (Exception) { return null; }
    }

    /// <summary>The rarity colour, or null.</summary>
    public static Color? ColourOf(Item item)
    {
        if (Facts(item) is not { } f) return null;
        try { return Color.FromHtml(f[3]); }
        catch (Exception) { return null; }
    }

    /// <summary>"Rare Iron Helmet, item level 40" and the affix lines, for a tooltip ("" without One Style).</summary>
    public static string Tooltip(Item item) =>
        Facts(item) is { } f ? Lang.F("rares.item_level", "{rarity} {item}, item level {level}", ("rarity", f[0]), ("item", f[2]), ("level", f[1])) + string.Concat(f.Skip(4).Select(l => "\n" + l)) : "";

    static BankEntity? Bank(Game game) => game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) ? e as BankEntity : null;
    static CombatEntity? Combat(Game game) => game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) ? e as CombatEntity : null;

    /// <summary>True when the character has the item in the bank or wears it.</summary>
    public static bool Owned(Game game, Item item) =>
        (Bank(game)?.Inventory.GetQuantity(item) ?? 0) > 0 || (Combat(game)?.Equipment.IsItemEquipped(item) ?? false);

    /// <summary>True when the character wears the item.</summary>
    public static bool Worn(Game game, Item item) => Combat(game)?.Equipment.IsItemEquipped(item) ?? false;

    /// <summary>Every item for the item lists: the normal ones, and the rare ones only while they are owned.</summary>
    public static List<Item> ItemList(Game game) =>
        game.Data.GetDataRegistryOutput<Item>().GetAllObjects().Where(i => !Is(i) || Owned(game, i))
            .OrderBy(i => i.DisplayName.GetPluralText(1), StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Gives the rare entries of an item list their tooltip.</summary>
    public static List<PickEntry<Item>> Describe(List<PickEntry<Item>> entries) =>
        entries.Select(e => Is(e.Value) && Tooltip(e.Value) is { Length: > 0 } tip ? e with { Tooltip = tip, Tint = ColourOf(e.Value) } : e).ToList();

    /// <summary>
    /// Draws the rare entries of a pick list in their rarity colour. The shared pick list has no colour of its own per
    /// entry, so its rows are found in its popup (a row's first metadata is its key) after each fill.
    /// </summary>
    public static void Paint<T>(PickList<T> pick, Game game, IEnumerable<PickEntry<T>> entries)
    {
        var registry = game.Data.GetDataRegistryOutput<Item>();
        var colours = new Dictionary<string, Color>();
        foreach (PickEntry<T> e in entries)
        {
            Item? item = e.Value as Item;
            if (item == null && e.Value is Target t && !t.Id.StartsWith(Step.LoadoutPrefix, StringComparison.Ordinal))
                registry.TryGetObjectById(t.Id, out item);
            if (item != null && Is(item) && ColourOf(item) is { } c) colours[e.Key] = c;
        }
        if (colours.Count == 0) return;
        foreach (Node n in pick.Control.FindChildren("*", "Tree", true, false))
            if (n is Tree tree && tree.GetRoot() is { } root) PaintRows(root, colours);
    }

    static void PaintRows(TreeItem item, Dictionary<string, Color> colours)
    {
        foreach (TreeItem child in item.GetChildren())
        {
            if (child.GetMetadata(0).AsString() is { Length: > 0 } key) { if (colours.TryGetValue(key, out Color c)) child.SetCustomColor(0, c); }
            else PaintRows(child, colours);
        }
    }
}
