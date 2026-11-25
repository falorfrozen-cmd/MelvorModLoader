using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Saves;

namespace BulkSell;

/// <summary>A save's auto-sell rules for One Style's rare items (all off at first).</summary>
public sealed class AutoRules
{
    public bool On { get; set; }
    public bool AllMagic { get; set; }
    public bool AllRare { get; set; }
    /// <summary>Sell a Magic / Rare item whose item level is below this (0: off).</summary>
    public int MagicBelow { get; set; }
    public int RareBelow { get; set; }
    /// <summary>Affix ids: an item with any of these is sold.</summary>
    public List<string> SellAffixes { get; set; } = new();
    /// <summary>Affix ids: an item with any of these is never sold, whatever the other rules say.</summary>
    public List<string> KeepAffixes { get; set; } = new();
    /// <summary>Only on an entry of Bulk Sell 0.3.0's first builds, which kept the rules by character name: the save
    /// that took these rules over. No second save with the same name gets them.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TakenBy { get; set; }
}

/// <summary>
/// Auto-sell (designed with Codex, Ortak-Calisma/bulk-sell-auto): One Style's Magic and Rare items that DROP into the
/// bank while the rules are on are sold at once when a rule matches. Which items are new drops is told by One Style
/// itself (RareItems.TakeNewDrops), never guessed from what appears in the bank: an item you take off and bank, an item
/// put back when the character is opened, or one that was there before is never a "new drop" (the window's "Sell
/// matching items now" is for those, with two clicks). A "keep" affix wins over every rule; locked, worn and Unique
/// items are never sold. Everything goes through the game's own selling. Rules per save in bulksell-auto.json, under
/// the save's id ("save:" and 32 hex digits, the form One Style uses): two characters with the same name never share
/// them, and renaming a character keeps them.
/// </summary>
public static class AutoSell
{
    static string FilePath => Path.Combine(Junk.Dir, "bulksell-auto.json");
    static string s_key = "";
    static readonly PropertyInfo? s_activeSave = typeof(SaveManager)
        .GetProperty("ActiveSaveId", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    public static AutoRules Rules { get; private set; } = new();
    /// <summary>The last auto-sales, newest first: "Gloom Guard (Magic, item level 3 &lt; 10): 534 Gold Pieces".</summary>
    public static readonly List<string> History = new();
    public static long SoldItems { get; private set; }
    public static readonly Dictionary<Currency, long> SoldFor = new();
    public static Action<string> Log { get; set; } = _ => { };

    // (a file that is busy throws: the callers keep what is on disk and say so, they never write "nothing" over it)
    static Dictionary<string, AutoRules> Read() => Files.Read<Dictionary<string, AutoRules>>(FilePath, Log);

    static void Write(Dictionary<string, AutoRules> all) => Files.Write(FilePath, all);

    /// <summary>"save:" and the open save's id, or "" when the game gives none.</summary>
    internal static string SaveKey()
    {
        try { if (s_activeSave?.GetValue(null) is Guid id && id != Guid.Empty) return "save:" + id.ToString("N"); }
        catch { }
        return "";
    }

    /// <summary>The name the open save's rules are kept under: "save:" and the save's id, or the character's name when
    /// the game gives no id.</summary>
    public static string KeyOf(Game game)
    {
        string key = SaveKey();
        return key != "" ? key : game.CharacterName;
    }

    /// <summary>The open save's rules.</summary>
    public static void Bind(Game game, BankEntity? bank = null) => Bind(KeyOf(game), game.CharacterName);

    /// <summary>
    /// The rules kept under this key. A save that has none yet takes over the rules an earlier build kept under its
    /// character's name, once: the entry by name stays (an older Bulk Sell still reads it) and is marked, so that
    /// another save with the same name starts with everything off.
    /// </summary>
    public static void Bind(string key, string character)
    {
        if (key == s_key) return;
        s_key = key;
        Dictionary<string, AutoRules> all;
        try { all = Read(); }
        catch (Exception e)
        {
            // the rules cannot be read now: everything off for this session, and nothing is written (Save reads again)
            Log("The auto-sell rules could not be read (" + e.Message + "): auto-sell is off until they can.");
            all = new();
        }
        if (all.TryGetValue(key, out AutoRules? r)) Rules = r;
        else if (key != character && all.TryGetValue(character, out AutoRules? byName) && string.IsNullOrEmpty(byName.TakenBy))
        {
            Rules = JsonSerializer.Deserialize<AutoRules>(JsonSerializer.Serialize(byName)) ?? new AutoRules();
            byName.TakenBy = key;
            all[key] = Rules;
            try { Write(all); }
            catch (Exception e) { Log("The auto-sell rules could not be saved: " + e.Message); }
        }
        else Rules = new AutoRules();
        History.Clear();
        SoldItems = 0;
        SoldFor.Clear();
    }

    /// <summary>Saves the rules; turning them on forgets the drops so far (only drops from now on are auto-sold).</summary>
    public static void Save(BankEntity? bank, bool turnedOn = false)
    {
        if (turnedOn) Gear.TakeNewDrops();
        if (s_key == "") return;
        try
        {
            Dictionary<string, AutoRules> all = Read();
            all[s_key] = Rules;
            Write(all);
        }
        catch (Exception e) { Log("The auto-sell rules could not be saved: " + e.Message); }
    }

    /// <summary>Why the rules sell this item ("" when they do not): a keep affix, Unique and plain items always "".</summary>
    public static string Reason(Entry e)
    {
        if (e.Rarity is not ("Magic" or "Rare")) return "";
        if (e.AffixIds.Any(Rules.KeepAffixes.Contains)) return "";
        if (e.Rarity == "Magic" && Rules.AllMagic) return "every Magic";
        if (e.Rarity == "Rare" && Rules.AllRare) return "every Rare";
        int below = e.Rarity == "Magic" ? Rules.MagicBelow : Rules.RareBelow;
        if (below > 0 && e.Level < below) return $"item level {e.Level} < {below}";
        string? bad = e.AffixIds.FirstOrDefault(Rules.SellAffixes.Contains);
        if (bad != null) return "affix " + (Gear.Catalog().FirstOrDefault(a => a.Id == bad)?.Text ?? bad);
        return "";
    }

    /// <summary>Every unlocked rare item in the bank the rules would sell now, with the reason (for "Sell matching items now").</summary>
    public static List<(Entry Entry, string Reason)> Matching(BankEntity bank) =>
        Bank.Sellable(bank).Where(e => e.Rarity != "").Select(e => (e, Reason(e))).Where(p => p.Item2 != "").ToList();

    /// <summary>About once a second while playing: sells the new rare items the rules match.</summary>
    public static void Pulse(Game game)
    {
        BankEntity? bank = Bank.Of(game);
        if (bank == null) return;
        Bind(game, bank);
        // always taken, also while the rules are off: a drop from before they were switched on never counts later
        string[] fresh = Gear.TakeNewDrops();
        if (!Rules.On || fresh.Length == 0) return;
        var ids = fresh.ToHashSet();
        var sell = new List<(Item Item, string Name, string Reason)>();
        foreach (IReadonlyItemStack s in bank.Inventory.Stacks.ToList())
        {
            if (s.Quantity <= 0 || s.IsLocked || s.IsPlaceholder || !ids.Contains(s.Item.LocalId)) continue;
            var e = new Entry { Item = s.Item, Tab = 0, Order = 0, Quantity = s.Quantity };
            Gear.Describe(e);
            string why = Reason(e);
            if (why != "") sell.Add((s.Item, Bank.Name(s.Item), why));
        }
        if (sell.Count == 0) return;
        Sell(bank, sell);
    }

    /// <summary>Sells the given items (locked ones are skipped by the sale itself) and writes them down.</summary>
    public static (long Items, Dictionary<Currency, long> Paid) Sell(BankEntity bank, List<(Item Item, string Name, string Reason)> items)
    {
        var (sold, paid) = Bank.Sell(bank, items.Select(i => i.Item), keep: 0);
        SoldItems += sold;
        foreach (var (c, n) in paid) SoldFor[c] = SoldFor.GetValueOrDefault(c) + n;
        foreach (var (_, name, why) in items) History.Insert(0, $"{name} ({why})");
        while (History.Count > 12) History.RemoveAt(History.Count - 1);
        Log($"Auto-sell: sold {sold} rare item(s) for {Bank.Money(paid)}: {string.Join(", ", items.Select(i => i.Name))}.");
        return (sold, paid);
    }
}
