using System;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using GamesByMalcs.Melvor2.Data.Items;

namespace PhoneScreen;

/// <summary>
/// One Style's rare items (Magic, Rare, Unique): their rarity, colour, item level, base item and affix lines, for the
/// phone's gear and bank lists. One Style is read through its public RareItems.Facts by reflection, so the phone works
/// the same without it (the fields are simply left out). On the game's thread only.
/// </summary>
public static class Rares
{
    static MethodInfo? s_facts;
    static DateTime s_lookedAt = DateTime.MinValue;

    static MethodInfo? Facts()
    {
        if (s_facts != null) return s_facts;
        if (DateTime.UtcNow - s_lookedAt < TimeSpan.FromSeconds(30)) return null;      // (not loaded: not searched every item)
        s_lookedAt = DateTime.UtcNow;
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (a.GetName().Name != "OneStyle") continue;
            s_facts = a.GetType("OneStyle.RareItems")?.GetMethod("Facts", BindingFlags.Public | BindingFlags.Static, new[] { typeof(Item) });
            if (s_facts != null) break;
        }
        return s_facts;
    }

    /// <summary>
    /// Adds the optional rare fields to the item's JSON object: "rarity" ("Magic" / "Rare" / "Unique"), "color"
    /// ("rrggbb"), "ilvl", "base" (the base item's name) and "affixes" (one line each). Nothing for a plain item.
    /// </summary>
    public static void Write(Utf8JsonWriter w, Item item)
    {
        string[]? f = null;
        try { f = Facts()?.Invoke(null, new object[] { item }) as string[]; }
        catch (Exception) { }
        if (f is not { Length: >= 4 } || string.IsNullOrEmpty(f[0])) return;
        w.WriteString("rarity", f[0]);
        if (Regex.IsMatch(f[3] ?? "", "^[0-9a-fA-F]{6}$")) w.WriteString("color", f[3]);
        if (int.TryParse(f[1], out int level) && level > 0) w.WriteNumber("ilvl", level);
        if (!string.IsNullOrEmpty(f[2])) w.WriteString("base", f[2]);
        if (f.Length > 4)
        {
            w.WriteStartArray("affixes");
            for (int i = 4; i < f.Length; i++) if (!string.IsNullOrWhiteSpace(f[i])) w.WriteStringValue(f[i]);
            w.WriteEndArray();
        }
    }
}
