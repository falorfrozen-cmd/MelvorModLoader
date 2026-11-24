using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Components.ItemBurying;
using GamesByMalcs.Melvor2.Components.ItemOpening;
using GamesByMalcs.Melvor2.Components.ItemSales;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;

namespace BulkSell;

/// <summary>One sellable stack of the bank, as the window shows it.</summary>
public sealed class Entry
{
    public required Item Item;
    public required int Tab;        // 0-based bank tab
    public required int Order;      // position in the bank, for the default order
    public int Quantity;
    public long Price;              // per item, with the character's sale bonuses
    public string Slot = "";        // equipment slot ("Helmet"), "" for anything that is not gear
    public int Level;               // a rare item's item level, else the level the gear asks for (0: none)
    public string Rarity = "";      // One Style: "Magic", "Rare" or "Unique"; "" for a normal item
    public string BaseName = "";    // One Style: the game item it is made from
    public Godot.Color? Color;      // One Style: the rarity's colour
    public string[] Affixes = Array.Empty<string>();
}

/// <summary>Reading the bank and selling from it, always through the game's own sale code.</summary>
public static class Bank
{
    public static BankEntity? Of(Game game) =>
        game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) ? e as BankEntity : null;

    public static string Name(Item item) => item.DisplayName.GetPluralText(1);

    /// <summary>What one of this item sells for right now. The game's price lookup is released again after use.</summary>
    public static long Price(BankEntity bank, Item item)
    {
        ItemSaleComponentStats stats = bank.ItemSales.GetItemStats(item);
        try { return stats.PricePerItem.Value; }
        finally { stats.Destroy(); }
    }

    /// <summary>Every stack that may be sold: not locked, not an empty placeholder.</summary>
    public static List<Entry> Sellable(BankEntity bank)
    {
        var list = new List<Entry>();
        int order = 0;
        for (int tab = 0; tab < bank.Inventory.InventoryTabs.Count; tab++)
            foreach (IReadonlyItemStack stack in bank.Inventory.InventoryTabs[tab].Stacks)
            {
                order++;
                if (stack.IsLocked || stack.IsPlaceholder || stack.Quantity <= 0) continue;
                var entry = new Entry { Item = stack.Item, Tab = tab, Order = order, Quantity = stack.Quantity, Price = Price(bank, stack.Item) };
                Gear.Describe(entry);
                list.Add(entry);
            }
        return list;
    }

    public static int LockedCount(BankEntity bank) => bank.Inventory.Stacks.Count(s => s.IsLocked && !s.IsPlaceholder);

    /// <summary>
    /// Sells up to (owned - keep) of each item. Locked stacks are skipped even if they are on the list.
    /// Returns the number of items sold and what was paid, per currency.
    /// </summary>
    public static (long Items, Dictionary<Currency, long> Paid) Sell(BankEntity bank, IEnumerable<Item> items, int keep)
    {
        var wanted = items.ToHashSet();
        var paid = new Dictionary<Currency, long>();
        long sold = 0;
        void OnSold(object? _, ItemSoldEventArgs e)
        {
            sold += e.Quantity;
            paid[e.CurrencyGain.Currency] = paid.GetValueOrDefault(e.CurrencyGain.Currency) + e.CurrencyGain.Quantity;
        }

        // Decide first, sell afterwards: selling changes the list of stacks.
        List<(Item Item, int Amount)> plan = bank.Inventory.Stacks
            .Where(s => wanted.Contains(s.Item) && !s.IsLocked && !s.IsPlaceholder && s.Quantity > keep)
            .Select(s => (s.Item, s.Quantity - keep)).ToList();

        bank.ItemSales.ItemSold += OnSold;
        try
        {
            foreach (var (item, amount) in plan)
            {
                ItemSaleComponentStats stats = bank.ItemSales.GetItemStats(item);
                try { bank.ItemSales.SellItem(item, stats, amount); }
                finally { stats.Destroy(); }
            }
        }
        finally { bank.ItemSales.ItemSold -= OnSold; }
        return (sold, paid);
    }

    // ---------------------------------------------------------------- bury everything, open everything

    static IEnumerable<IReadonlyItemStack> Free(BankEntity bank) =>
        bank.Inventory.Stacks.Where(s => !s.IsLocked && !s.IsPlaceholder && s.Quantity > 0);

    /// <summary>Everything in the bank that can be buried (locked stacks excluded), and what burying it all pays.</summary>
    public static (List<Item> Items, long Count, Dictionary<Currency, long> Reward) Buriable(BankEntity bank)
    {
        var items = new List<Item>();
        var reward = new Dictionary<Currency, long>();
        long count = 0;
        foreach (IReadonlyItemStack s in Free(bank))
        {
            if (!s.Item.Capabilities.TryGet<BuryCapability>(out BuryCapability? bury)) continue;
            ItemBuryingStats stats = bank.ItemBurying.GetItemStats(s.Item);
            long each = stats.RewardPerBury.Value;
            stats.Destroy();
            items.Add(s.Item);
            count += s.Quantity;
            Currency c = bury.CurrencyGain.Currency;
            reward[c] = reward.GetValueOrDefault(c) + each * s.Quantity;
        }
        return (items, count, reward);
    }

    public static (long Buried, Dictionary<Currency, long> Reward) BuryAll(BankEntity bank)
    {
        var reward = new Dictionary<Currency, long>();
        long buried = 0;
        void OnBuried(object? _, ItemBuriedEventArgs e)
        {
            buried += e.Quantity;
            reward[e.CurrencyGained.Currency] = reward.GetValueOrDefault(e.CurrencyGained.Currency) + e.CurrencyGained.Quantity;
        }
        List<(Item Item, int Amount)> plan = Free(bank).Where(s => s.Item.Capabilities.TryGet<BuryCapability>(out _))
            .Select(s => (s.Item, s.Quantity)).ToList();
        bank.ItemBurying.ItemBuried += OnBuried;
        try
        {
            foreach (var (item, amount) in plan)
            {
                ItemBuryingStats stats = bank.ItemBurying.GetItemStats(item);
                try { bank.ItemBurying.BuryItem(item, stats, amount); }
                finally { stats.Destroy(); }
            }
        }
        finally { bank.ItemBurying.ItemBuried -= OnBuried; }
        return (buried, reward);
    }

    /// <summary>Chests, bags and other openable items the character is allowed to open (locked stacks excluded).</summary>
    public static (List<Item> Items, long Count) Openable(BankEntity bank)
    {
        var items = new List<Item>();
        long count = 0;
        foreach (IReadonlyItemStack s in Free(bank))
        {
            if (!s.Item.Capabilities.TryGet<OpenableCapability>(out OpenableCapability? open)) continue;
            if (open.Requirements != null && !open.Requirements.AreMet) continue;
            items.Add(s.Item);
            count += s.Quantity;
        }
        return (items, count);
    }

    /// <summary>Opens everything openable. The game itself stops a stack early when the bank has no room for its contents.</summary>
    public static (long Opened, int Kinds, bool BankFull) OpenAll(BankEntity bank)
    {
        long opened = 0;
        var kinds = new HashSet<Item>();
        bool full = false;
        List<(Item Item, int Amount)> plan = Free(bank)
            .Where(s => s.Item.Capabilities.TryGet<OpenableCapability>(out OpenableCapability? o) && (o.Requirements == null || o.Requirements.AreMet))
            .Select(s => (s.Item, s.Quantity)).ToList();
        foreach (var (item, amount) in plan)
        {
            ItemOpenedEventArgs result = bank.ItemOpening.OpenItem(item, amount);
            opened += result.Count;
            foreach (ItemOpenResult drop in result.Drops) kinds.Add(drop.Item);
            full |= result.WasStopped;
        }
        return (opened, kinds.Count, full);
    }

    public static string Money(Dictionary<Currency, long> amounts) =>
        amounts.Count == 0 ? "nothing" : string.Join(" + ", amounts.Select(a => $"{a.Value:N0} {a.Key.DisplayName.Text}"));
}

/// <summary>
/// The per-character junk list (item ids) in bulksell.json next to the mod DLL.
/// A write replaces only the open character's entry in what is on disk at that moment.
/// </summary>
public static class Junk
{
    public static string Dir = "";
    static string FilePath => Path.Combine(Dir, "bulksell.json");
    static HashSet<string> s_ids = new();
    static string s_character = "";

    public static Action<string> Log { get; set; } = _ => { };
    public static int Count => s_ids.Count;
    public static bool Contains(Item item) => s_ids.Contains(item.Id);

    static Dictionary<string, List<string>> Read()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public static void Bind(string character)
    {
        if (character == s_character) return;
        s_character = character;
        s_ids = Read().TryGetValue(character, out List<string>? ids) ? ids.ToHashSet() : new HashSet<string>();
    }

    public static void Set(IEnumerable<Item> items) => Save(items.Select(i => i.Id).ToHashSet());

    /// <summary>Puts items on the list; what is already on it stays (also items not in the bank now).</summary>
    public static int Add(IEnumerable<Item> items)
    {
        var ids = new HashSet<string>(s_ids);
        int added = items.Count(i => ids.Add(i.Id));
        Save(ids);
        return added;
    }

    /// <summary>Takes items off the list; the rest stays.</summary>
    public static int Remove(IEnumerable<Item> items)
    {
        var ids = new HashSet<string>(s_ids);
        int removed = items.Count(i => ids.Remove(i.Id));
        Save(ids);
        return removed;
    }

    static void Save(HashSet<string> ids)
    {
        s_ids = ids;
        if (s_character == "") return;
        try
        {
            Dictionary<string, List<string>> all = Read();
            all[s_character] = s_ids.OrderBy(i => i).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Log("The junk list could not be saved: " + e.Message); }
    }

    /// <summary>The junk items that are in the bank and may be sold right now, with what they would bring.</summary>
    public static (List<Item> Items, long Count, Dictionary<Currency, long> Worth) InBank(BankEntity bank)
    {
        var items = new List<Item>();
        var worth = new Dictionary<Currency, long>();
        long count = 0;
        if (s_ids.Count == 0) return (items, count, worth);
        foreach (IReadonlyItemStack s in bank.Inventory.Stacks)
        {
            if (!s_ids.Contains(s.Item.Id) || s.IsLocked || s.IsPlaceholder || s.Quantity <= 0) continue;
            items.Add(s.Item);
            count += s.Quantity;
            Currency c = s.Item.SellsFor.Currency;
            worth[c] = worth.GetValueOrDefault(c) + Bank.Price(bank, s.Item) * s.Quantity;
        }
        return (items, count, worth);
    }
}
