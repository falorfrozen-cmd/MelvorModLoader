using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ActionQueue;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Loadouts;
using GamesByMalcs.Melvor2.Components.MagicSpells;
using GamesByMalcs.Melvor2.Components.Prayers;
using GamesByMalcs.Melvor2.Components.Shop;
using GamesByMalcs.Melvor2.Components.SlayerTasks;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Components.ToolbeltEquipping;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Core.InventoryWalletTransactions;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Data.Recipes;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Entities.Farming;
using GamesByMalcs.Melvor2.Entities.Shop;

namespace PhoneScreen;

/// <summary>
/// The phone's other screens, each made on the game's thread when a phone looks at it: gear and loadouts, potions,
/// prayers and spells, Slayer, Farming, the shop, and our other mods (Adventurers, Bulk Sell, Multitask, Cheat Menu).
/// Every change goes through the same calls the game's own buttons make, after the same checks those buttons make
/// (several game methods leave the checks to the button). Other mods are reached by name.
/// </summary>
public static class Parts
{
    public static readonly string[] Names = { "gear", "buffs", "slayer", "farm", "shop", "more" };

    public static byte[] Build(Game game, string name)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("part", name);
            w.WriteString("character", game.CharacterName);
            switch (name)
            {
                case "gear": Gear(w, game); break;
                case "buffs": Buffs(w, game); break;
                case "slayer": Slayer(w, game); break;
                case "farm": Farm(w, game); break;
                case "shop": Shop(w, game); break;
                case "more": More(w, game); break;
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>Which of our other mods are here (the phone shows their screens only then).</summary>
    public static void Has(Utf8JsonWriter w)
    {
        w.WriteBoolean("bulk", s_bulk != null);
        w.WriteBoolean("multitask", s_slots != null);
        w.WriteBoolean("cheats", s_engine != null && s_features != null);
    }

    /// <summary>
    /// For the snapshot (every half second, so kept small): Farming at a glance, and the fight's potion, prayers and
    /// spell. Returns how many plots are ready.
    /// </summary>
    public static int Glance(Utf8JsonWriter w, Game game)
    {
        int ready = 0;
        if (FarmOf(game) is { } farm)
        {
            int growing = 0, empty = 0;
            double next = double.MaxValue;
            foreach (ActiveFarmingPlot p in farm.Passive.ActivePlots.Values)
            {
                if (!p.IsPurchased) continue;
                if (p.CanHarvest) ready++;
                else if (p.CanPlant) empty++;
                else if (p.State.Timer is { } t) { growing++; next = Math.Min(next, GamesByMalcs.Melvor2.Core.TickTimer.TickTimer.TicksToMS(t.TicksLeft)); }
            }
            w.WriteStartObject("farm");
            w.WriteNumber("ready", ready);
            w.WriteNumber("growing", growing);
            w.WriteNumber("empty", empty);
            if (next < double.MaxValue) w.WriteNumber("nextMs", Math.Round(next));
            w.WriteEndObject();
        }
        if (CombatOf(game) is { } combat)
        {
            w.WriteStartObject("fightKit");
            try
            {
                foreach (ToolbeltSlot slot in ((IToolbeltConfig)combat.Toolbelt.Config).ToolbeltSlots)
                    if (combat.Toolbelt.TryGetActiveSlot(slot, out IActiveToolbeltSlot? s) && s.Equipped is { } eq)
                    { w.WriteString("potion", eq.Item.DisplayName.GetPluralText(1)); w.WriteNumber("potionQty", s.Quantity); break; }
                w.WriteString("prayers", string.Join(", ", combat.Prayer.ActivePrayers.Keys.Where(combat.Prayer.IsPrayerSelected).Select(p => p.DisplayName.Text)));
                if (combat.MagicSpells.CanUseSpells && combat.MagicSpells.SelectedSpell is { } sp) w.WriteString("spell", sp.Data.DisplayName.Text);
                if (combat.Loadouts.CurrentLoadout is { } l) w.WriteString("loadout", string.IsNullOrWhiteSpace(l.Name) ? l.Data.DisplayName.Text : l.Name);
                if (combat.SlayerTasks is { } st)
                    foreach (var (master, m) in st.ActiveMasters)
                        if (m.CurrentTask is { } en && combat.Areas.SelectedEnemy == en)
                        { w.WriteString("slayer", $"{master.DisplayName.Text}: {Math.Max(0, m.TaskLength - m.TaskProgress)} {en.DisplayName.Text} left"); break; }
            }
            catch { }
            w.WriteEndObject();
        }
        return ready;
    }

    // ------------------------------------------------------------------------------------------- small pieces

    static CombatEntity? CombatOf(Game game) => game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) ? e as CombatEntity : null;
    static BankEntity? BankOf(Game game) => game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) ? e as BankEntity : null;

    static Dictionary<string, Item>? s_items;
    static Game? s_itemsFor;
    static Item? ItemById(Game game, string id)
    {
        if (s_items == null || s_itemsFor != game) { s_items = game.Data.GetDataRegistryOutput<Item>().GetAllObjects().ToDictionary(i => i.Id); s_itemsFor = game; }
        if (s_items.TryGetValue(id, out Item? known)) return known;
        // (One Style adds a new item for each rare drop during play, after the list above was made: asked live)
        if (game.Data.GetDataRegistryOutput<Item>().TryGetObjectById(id, out Item? item) && item != null) { s_items[id] = item; return item; }
        return null;
    }

    static string Plural(Item item, long n) => item.DisplayName.GetPluralText(n == 1 ? 1 : 2);

    static string Why(ConditionGroup? needs) => needs == null || needs.AreMet ? "" : Targets.Missing(needs);

    static void Need(Utf8JsonWriter w, ConditionGroup? needs)
    {
        bool ok = needs == null || needs.AreMet;
        w.WriteBoolean("can", ok);
        if (!ok) w.WriteString("why", Why(needs));
    }

    static void ItemRef(Utf8JsonWriter w, string name, Item? item, long qty = -1)
    {
        if (item == null) { w.WriteNull(name); return; }
        w.WriteStartObject(name);
        w.WriteString("id", item.Id);
        w.WriteString("name", item.DisplayName.GetPluralText(1));
        if (qty >= 0) w.WriteNumber("qty", qty);
        Rares.Write(w, item);
        w.WriteEndObject();
    }

    /// <summary>Costs as words and whether they can be paid now: "250 Slayer Coins", "1 Iron Bar".</summary>
    static void Costs(Utf8JsonWriter w, string name, RecipeIngredients costs, Func<Item, long> have, Func<Currency, long> wallet, out long max)
    {
        max = long.MaxValue;
        w.WriteStartArray(name);
        foreach (ItemQuantity q in costs.Items)
        {
            long h = have(q.Item);
            if (q.Quantity > 0) max = Math.Min(max, h / q.Quantity);
            w.WriteStartObject(); w.WriteString("name", Plural(q.Item, q.Quantity)); w.WriteString("item", q.Item.Id); w.WriteNumber("qty", q.Quantity); w.WriteNumber("have", h); w.WriteEndObject();
        }
        foreach (CurrencyQuantity q in costs.Currencies)
        {
            long h = wallet(q.Currency);
            if (q.Quantity > 0) max = Math.Min(max, h / q.Quantity);
            w.WriteStartObject(); w.WriteString("name", q.Currency.DisplayName.Text); w.WriteString("currency", q.Currency.Id); w.WriteNumber("qty", q.Quantity); w.WriteNumber("have", h); w.WriteEndObject();
        }
        w.WriteEndArray();
        if (max == long.MaxValue) max = 9999;
    }

    // ------------------------------------------------------------------------------------------- gear

    static void Gear(Utf8JsonWriter w, Game game)
    {
        CombatEntity? combat = CombatOf(game);
        BankEntity? bank = BankOf(game);
        if (combat == null || bank == null) { w.WriteBoolean("available", false); return; }
        w.WriteBoolean("available", true);

        w.WriteStartArray("loadouts");
        foreach (ActiveLoadoutSlot l in combat.Loadouts.LoadoutDisplayOrder)
        {
            w.WriteStartObject();
            w.WriteString("id", l.Data.Id);
            w.WriteString("name", string.IsNullOrWhiteSpace(l.Name) ? l.Data.DisplayName.Text : l.Name);
            w.WriteBoolean("current", combat.Loadouts.CurrentLoadout == l);
            w.WriteBoolean("unlocked", l.IsUnlocked);
            if (!l.IsUnlocked) w.WriteString("why", Why(l.Data.UnlockRequirements));
            w.WriteEndObject();
        }
        w.WriteEndArray();

        var slots = ((IEquipmentConfig)combat.Equipment.Config).EquipmentSlots.ToList();
        w.WriteStartArray("slots");
        foreach (EquipmentSlot slot in slots)
        {
            if (!combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? s)) continue;
            w.WriteStartObject();
            w.WriteString("id", slot.Id);
            w.WriteString("name", slot.DisplayName.Text);
            w.WriteBoolean("unlocked", slot.UnlockRequirements.AreMet);
            if (s.Equipped is { } eq)
            {
                ItemRef(w, "item", eq.Item, Math.Max(0, s.Quantity));
                if (s.Quantity < 0) w.WriteBoolean("occupied", true);           // (an offhand under a two-handed weapon)
            }
            else w.WriteNull("item");
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // what the bank holds that fits each slot
        var options = new Dictionary<string, List<(Item Item, long Qty, bool Can, string Why, bool Stack)>>();
        foreach (var group in bank.Inventory.Stacks.GroupBy(x => x.Item))
        {
            Item item = group.Key;
            if (!item.Capabilities.TryGet(out EquipmentCapability? cap) || cap == null) continue;
            List<AvailableEquipmentSlot> fits;
            try { fits = combat.Equipment.GetAvailableSlotsForItem(item); } catch { continue; }
            long qty = group.Sum(x => (long)x.Quantity);
            bool can = cap.Requirements.AreMet;
            foreach (var f in fits)
            {
                string id = f.Slot.Slot.Id;
                if (!options.TryGetValue(id, out var list)) options[id] = list = new();
                list.Add((item, qty, can, can ? "" : Why(cap.Requirements), cap.CanEquipQuantity));
            }
        }
        w.WriteStartObject("options");
        foreach (var (slot, list) in options)
        {
            w.WriteStartArray(slot);
            foreach (var o in list.OrderByDescending(x => x.Can).ThenBy(x => x.Item.DisplayName.GetPluralText(1)))
            {
                w.WriteStartObject();
                w.WriteString("id", o.Item.Id);
                w.WriteString("name", o.Item.DisplayName.GetPluralText(1));
                w.WriteNumber("qty", o.Qty);
                w.WriteBoolean("can", o.Can);
                if (!o.Can) w.WriteString("why", o.Why);
                if (o.Stack) w.WriteBoolean("stack", true);
                Rares.Write(w, o.Item);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    public static (bool, string) Equip(Game game, string itemId, string slotId, long quantity)
    {
        CombatEntity? combat = CombatOf(game);
        if (combat == null) return (false, "No combat here.");
        Item? item = ItemById(game, itemId);
        if (item == null || !item.Capabilities.TryGet(out EquipmentCapability? cap) || cap == null) return (false, "That is not something to wear.");
        if (!cap.Requirements.AreMet) return (false, $"{item.DisplayName.GetPluralText(1)} needs {Why(cap.Requirements)}.");
        var fits = combat.Equipment.GetAvailableSlotsForItem(item);
        var target = fits.FirstOrDefault(f => f.Slot.Slot.Id == slotId) ?? fits.FirstOrDefault();
        if (target == null) return (false, "It does not fit there.");
        if (!target.Slot.Slot.UnlockRequirements.AreMet) return (false, $"The {target.Slot.Slot.DisplayName.Text} slot is locked.");
        long have = combat.ItemEquipping.Inventory.GetQuantity(item);
        if (have <= 0) return (false, $"You have no {Plural(item, 2)} in the bank.");
        int q = cap.CanEquipQuantity ? (int)Math.Clamp(quantity <= 0 ? have : quantity, 1, Math.Min(have, int.MaxValue)) : 1;
        long before = combat.Equipment.GetEquippedQuantity(item);
        combat.ItemEquipping.EquipItem(item, q, target.Slot.Slot);
        long after = combat.Equipment.GetEquippedQuantity(item);
        if (after <= before) return (false, $"{item.DisplayName.GetPluralText(1)} could not be equipped (bank full?).");
        return (true, cap.CanEquipQuantity ? $"Equipped {after - before:N0} {Plural(item, after - before)}." : $"Equipped {item.DisplayName.GetPluralText(1)}.");
    }

    public static (bool, string) Unequip(Game game, string slotId)
    {
        CombatEntity? combat = CombatOf(game);
        if (combat == null) return (false, "No combat here.");
        EquipmentSlot? slot = ((IEquipmentConfig)combat.Equipment.Config).EquipmentSlots.FirstOrDefault(s => s.Id == slotId);
        if (slot == null || !combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? s) || s.Equipped == null) return (false, "Nothing is worn there.");
        Item item = s.Equipped.Item;
        combat.ItemEquipping.UnequipSlot(slot);
        return combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? after) && after.Equipped?.Item == item
            ? (false, "It could not be taken off (bank full?).") : (true, $"Took off {item.DisplayName.GetPluralText(1)}.");
    }

    public static (bool, string) Loadout(Game game, string id)
    {
        CombatEntity? combat = CombatOf(game);
        if (combat == null) return (false, "No combat here.");
        ActiveLoadoutSlot? l = combat.Loadouts.LoadoutDisplayOrder.FirstOrDefault(x => x.Data.Id == id);
        if (l == null) return (false, "No such loadout.");
        if (!l.IsUnlocked) return (false, $"That loadout is locked: {Why(l.Data.UnlockRequirements)}.");
        if (combat.Loadouts.CurrentLoadout == l) return (true, "That loadout is on already.");
        combat.Loadouts.SwapToLoadout(l.Data);
        string name = string.IsNullOrWhiteSpace(l.Name) ? l.Data.DisplayName.Text : l.Name;
        return combat.Loadouts.CurrentLoadout == l ? (true, $"Loadout: {name}.") : (false, "The loadout did not change.");
    }

    // ------------------------------------------------------------------------------------------- potions, prayers, spells

    /// <summary>The combat entity and every skill with a toolbelt (potions), as (id, name, toolbelt, equipping).</summary>
    static IEnumerable<(GameEntity Entity, ToolbeltComponent Belt, ToolbeltEquippingComponent Equipping)> Belts(Game game)
    {
        foreach (GameEntity e in game.Entities.GetAllObjects())
            if (e is IToolbeltEntity t && t.Toolbelt != null && t.ToolbeltEquipping != null)
                yield return (e, t.Toolbelt, t.ToolbeltEquipping);
    }

    static void Buffs(Utf8JsonWriter w, Game game)
    {
        CombatEntity? combat = CombatOf(game);
        BankEntity? bank = BankOf(game);
        if (combat == null || bank == null) { w.WriteBoolean("available", false); return; }
        w.WriteBoolean("available", true);

        // potions (toolbelt slots) for combat and every skill; what the bank holds for each slot type
        w.WriteStartArray("belts");
        foreach (var (entity, belt, _) in Belts(game).OrderBy(b => b.Entity.Id == "MelvorBase:Combat" ? 0 : 1).ThenBy(b => b.Entity.DisplayName.Text))
        {
            w.WriteStartObject();
            w.WriteString("entity", entity.Id);
            w.WriteString("name", entity.DisplayName.Text);
            w.WriteStartArray("slots");
            foreach (ToolbeltSlot slot in ((IToolbeltConfig)belt.Config).ToolbeltSlots)
            {
                if (!belt.TryGetActiveSlot(slot, out IActiveToolbeltSlot? s)) continue;
                w.WriteStartObject();
                w.WriteString("id", slot.Id);
                w.WriteString("name", slot.DisplayName.Text);
                w.WriteString("fits", slot.FitsType.Id);
                w.WriteBoolean("unlocked", slot.UnlockRequirements.AreMet);
                if (s.Equipped is { } eq) ItemRef(w, "item", eq.Item, s.Quantity); else w.WriteNull("item");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("potions");
        foreach (var group in bank.Inventory.Stacks.GroupBy(x => x.Item))
        {
            if (!group.Key.Capabilities.TryGet(out ToolbeltCapability? tc) || tc == null) continue;
            w.WriteStartObject();
            w.WriteString("id", group.Key.Id);
            w.WriteString("name", group.Key.DisplayName.GetPluralText(1));
            w.WriteNumber("qty", group.Sum(x => (long)x.Quantity));
            w.WriteString("fits", tc.FitsInSlot.Id);
            Need(w, tc.Requirements);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        // prayers: one per unlocked slot of its kind
        var prayer = combat.Prayer;
        w.WriteStartObject("prayers");
        w.WriteStartArray("points");
        foreach (Currency c in prayer.CostCurrencies) { w.WriteStartObject(); w.WriteString("name", c.DisplayName.Text); w.WriteNumber("qty", prayer.Wallet.GetQuantity(c)); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WriteStartArray("slots");
        foreach (var (slot, s) in prayer.ActiveSlots)
        {
            w.WriteStartObject();
            w.WriteString("id", slot.Id);
            w.WriteString("name", slot.Id[(slot.Id.IndexOf(':') + 1)..]);
            w.WriteString("fits", s.FitsType.Id);
            w.WriteBoolean("unlocked", s.UnlockRequirements.AreMet);
            if (!s.UnlockRequirements.AreMet) w.WriteString("why", Why(s.UnlockRequirements));
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("list");
        foreach (var (p, ap) in prayer.ActivePrayers)
        {
            w.WriteStartObject();
            w.WriteString("id", p.Id);
            w.WriteString("name", p.DisplayName.Text);
            w.WriteString("fits", p.FitsInSlot.Id);
            string coin = p.Cost.Currency.DisplayName.Text;     // ("1 Prayer Point", not "1 Prayer Points")
            if (p.Cost.Quantity == 1 && coin.EndsWith('s')) coin = coin[..^1];
            w.WriteString("cost", $"{p.Cost.Quantity:N0} {coin}");
            w.WriteBoolean("selected", prayer.IsPrayerSelected(p));
            w.WriteBoolean("active", prayer.IsPrayerActive(p));
            Need(w, p.UnlockRequirements);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();

        // spells: the attack spell and the imbuement
        var magic = combat.MagicSpells;
        w.WriteStartObject("magic");
        w.WriteBoolean("canUse", magic.CanUseSpells);
        w.WriteString("selected", magic.SelectedSpell?.Data.Id ?? "");
        w.WriteString("imbuement", magic.SelectedImbuement?.Data.Id ?? "");
        w.WriteStartArray("spells");
        foreach (var (spell, active) in magic.ActiveSpells)
        {
            w.WriteStartObject();
            w.WriteString("id", spell.Id);
            w.WriteString("name", spell.DisplayName.Text);
            w.WriteString("book", spell.Book?.DisplayName.Text ?? "");
            w.WriteBoolean("available", active.IsAvailable);
            w.WriteString("runes", string.Join(", ", spell.RuneCosts.Select(r => $"{r.Quantity} {Plural(r.Item, r.Quantity)} ({bank.Inventory.GetQuantity(r.Item):N0})")));
            w.WriteBoolean("short", spell.RuneCosts.Any(r => bank.Inventory.GetQuantity(r.Item) < r.Quantity));
            Need(w, spell.UnlockRequirements);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("imbuements");
        foreach (var (imb, active) in magic.ActiveImbuements)
        {
            w.WriteStartObject();
            w.WriteString("id", imb.Id);
            w.WriteString("name", imb.DisplayName.Text);
            w.WriteBoolean("available", active.IsAvailable);
            w.WriteString("runes", string.Join(", ", imb.RuneCosts.Select(r => $"{r.Quantity} {Plural(r.Item, r.Quantity)}")));
            Need(w, imb.UnlockRequirements);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    public static (bool, string) Potion(Game game, string entityId, string slotId, string itemId)
    {
        var belt = Belts(game).FirstOrDefault(b => b.Entity.Id == entityId);
        if (belt.Belt == null) return (false, "No potion slot there.");
        ToolbeltSlot? slot = ((IToolbeltConfig)belt.Belt.Config).ToolbeltSlots.FirstOrDefault(s => s.Id == slotId);
        if (slot == null) return (false, "No such slot.");
        if (!slot.UnlockRequirements.AreMet) return (false, $"That slot is locked: {Why(slot.UnlockRequirements)}.");
        if (itemId == "")
        {
            if (!belt.Belt.TryGetActiveSlot(slot, out IActiveToolbeltSlot? s) || s.Equipped == null) return (false, "Nothing is there.");
            belt.Equipping.UnequipSlot(slot);
            return (true, $"Took out {s.Equipped?.Item.DisplayName.GetPluralText(2) ?? "the potion"}.");
        }
        Item? item = ItemById(game, itemId);
        if (item == null || !item.Capabilities.TryGet(out ToolbeltCapability? tc) || tc == null) return (false, "That is not a potion.");
        if (tc.FitsInSlot != slot.FitsType) return (false, $"{item.DisplayName.GetPluralText(1)} does not go in {slot.DisplayName.Text}.");
        if (!tc.Requirements.AreMet) return (false, $"{item.DisplayName.GetPluralText(1)} needs {Why(tc.Requirements)}.");
        long have = belt.Equipping.Inventory.GetQuantity(item);
        if (have <= 0) return (false, $"You have no {Plural(item, 2)}.");
        int q = tc.CanEquipQuantity ? (int)Math.Min(have, int.MaxValue) : 1;
        belt.Equipping.EquipItem(item, q, slot);
        return belt.Belt.IsItemEquipped(item) ? (true, $"{belt.Entity.DisplayName.Text}: using {item.DisplayName.GetPluralText(2)} ({belt.Belt.GetEquippedQuantity(item):N0}).")
                                             : (false, "The potion could not be put in (bank full?).");
    }

    public static (bool, string) Prayer(Game game, string id)
    {
        CombatEntity? combat = CombatOf(game);
        if (combat == null) return (false, "No combat here.");
        Prayer? p = combat.Prayer.ActivePrayers.Keys.FirstOrDefault(x => x.Id == id);
        if (p == null) return (false, "No such prayer.");
        bool was = combat.Prayer.IsPrayerSelected(p);
        if (!was && !p.UnlockRequirements.AreMet) return (false, $"{p.DisplayName.Text} needs {Why(p.UnlockRequirements)}.");
        combat.Prayer.TogglePrayer(p);
        bool now = combat.Prayer.IsPrayerSelected(p);
        if (now == was) return (false, was ? "The prayer could not be turned off." : $"No free slot for {p.DisplayName.Text}: turn another prayer off first.");
        return (true, now ? $"Praying: {p.DisplayName.Text}." : $"Stopped praying {p.DisplayName.Text}.");
    }

    public static (bool, string) Spell(Game game, string id, bool imbuement)
    {
        CombatEntity? combat = CombatOf(game);
        if (combat == null) return (false, "No combat here.");
        var magic = combat.MagicSpells;
        if (imbuement)
        {
            if (id == "") { magic.DeselectImbuement(); return (true, "No imbuement."); }
            var (imb, a) = magic.ActiveImbuements.FirstOrDefault(x => x.Key.Id == id);
            if (imb == null) return (false, "No such imbuement.");
            if (!imb.UnlockRequirements.AreMet) return (false, $"{imb.DisplayName.Text} needs {Why(imb.UnlockRequirements)}.");
            if (!a.IsAvailable) return (false, $"{imb.DisplayName.Text} needs a staff of its element.");
            magic.SelectImbuement(imb);
            return magic.SelectedImbuement?.Data == imb ? (true, $"Imbuement: {imb.DisplayName.Text}.") : (false, "The imbuement did not change.");
        }
        var (spell, active) = magic.ActiveSpells.FirstOrDefault(x => x.Key.Id == id);
        if (spell == null) return (false, "No such spell.");
        if (!spell.UnlockRequirements.AreMet) return (false, $"{spell.DisplayName.Text} needs {Why(spell.UnlockRequirements)}.");
        if (!active.IsAvailable) return (false, magic.CanUseSpells ? $"{spell.DisplayName.Text} needs a staff of its element." : "Your weapon and stance do not cast spells.");
        magic.SelectSpell(spell);
        return magic.SelectedSpell?.Data == spell ? (true, $"Spell: {spell.DisplayName.Text}.") : (false, "The spell did not change.");
    }

    // ------------------------------------------------------------------------------------------- Slayer

    static void Slayer(Utf8JsonWriter w, Game game)
    {
        CombatEntity? combat = CombatOf(game);
        var tasks = combat?.SlayerTasks;
        if (combat == null || tasks == null) { w.WriteBoolean("available", false); return; }
        w.WriteBoolean("available", true);
        w.WriteStartArray("masters");
        foreach (var (master, m) in tasks.ActiveMasters)
        {
            w.WriteStartObject();
            w.WriteString("id", master.Id);
            w.WriteString("name", master.DisplayName.Text);
            Need(w, master.UnlockRequirements);
            w.WriteString("coin", master.RewardCurrency.DisplayName.Text);
            w.WriteNumber("coins", tasks.Wallet.GetQuantity(master.RewardCurrency));
            w.WriteNumber("reroll", master.RerollCost.Quantity);
            w.WriteBoolean("extended", m.IsTaskExtended);
            w.WriteBoolean("auto", m.AutoSlayerEnabled);
            if (m.CurrentTask is { } enemy)
            {
                w.WriteStartObject("task");
                w.WriteString("id", enemy.Id);
                w.WriteString("name", enemy.DisplayName.Text);
                w.WriteNumber("left", Math.Max(0, m.TaskLength - m.TaskProgress));
                w.WriteNumber("total", m.TaskLength);
                try { if (m.GetCurrentTaskArea(enemy) is { } area) { w.WriteString("area", area.Id); w.WriteString("areaName", area.DisplayName.Text); } } catch { }
                w.WriteBoolean("fighting", combat.Action.IsStarted && combat.Areas.SelectedEnemy == enemy);
                w.WriteEndObject();
            }
            else w.WriteNull("task");
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    public static (bool, string) SlayerDo(Game game, string masterId, string op)
    {
        CombatEntity? combat = CombatOf(game);
        var tasks = combat?.SlayerTasks;
        if (combat == null || tasks == null) return (false, "No Slayer here.");
        var (master, m) = tasks.ActiveMasters.FirstOrDefault(x => x.Key.Id == masterId);
        if (master == null) return (false, "No such Slayer master.");
        if (!master.UnlockRequirements.AreMet) return (false, $"{master.DisplayName.Text} needs {Why(master.UnlockRequirements)}.");
        switch (op)
        {
            case "new":
                if (m.CurrentTask != null) return (false, "You have a task from this master already.");
                m.StartInitialTask();
                return m.CurrentTask is { } t ? (true, $"New task: {m.TaskLength - m.TaskProgress} {t.DisplayName.Text}.") : (false, "No monster is open for a task yet.");
            case "reroll":
            {
                if (m.CurrentTask == null) return (false, "No task to change.");
                var old = m.CurrentTask;
                long coins = tasks.Wallet.GetQuantity(master.RewardCurrency);
                m.RerollTask();
                if (tasks.Wallet.GetQuantity(master.RewardCurrency) == coins && m.CurrentTask == old) return (false, $"Not enough {master.RewardCurrency.DisplayName.Text}.");
                return (true, $"New task: {m.TaskLength - m.TaskProgress} {m.CurrentTask?.DisplayName.Text}.");
            }
            case "extend":
            {
                if (m.CurrentTask == null || m.IsTaskExtended) return (false, m.CurrentTask == null ? "No task to extend." : "This task was extended already.");
                m.ExtendTask();
                return m.IsTaskExtended ? (true, $"Task extended: {m.TaskLength - m.TaskProgress} to go.") : (false, $"Not enough {master.RewardCurrency.DisplayName.Text}.");
            }
            case "auto":
                m.AutoSlayerEnabled = !m.AutoSlayerEnabled;
                return (true, m.AutoSlayerEnabled ? "Auto Slayer on: a finished task moves you to the next monster." : "Auto Slayer off.");
            case "fight":
            {
                if (m.CurrentTask is not { } enemy) return (false, "No task to fight.");
                var area = m.GetCurrentTaskArea(enemy);
                if (area == null) return (false, "The task's monster cannot be reached now.");
                var action = Targets.Find(game, "MelvorBase:Combat");
                Target? target = action == null ? null : Targets.All(action).FirstOrDefault(x => x.Id == enemy.Id && x.Extra == area.Id);
                if (action == null || target == null) return (false, "The task's monster cannot be started from here.");
                return Catalog.Start(game, "MelvorBase:Combat", target.Id, target.Extra);
            }
        }
        return (false, "Unknown Slayer command.");
    }

    // ------------------------------------------------------------------------------------------- Farming

    static FarmingEntity? FarmOf(Game game) => game.Entities.GetAllObjects().OfType<FarmingEntity>().FirstOrDefault();

    static string PlotState(ActiveFarmingPlot p)
    {
        var s = p.State;
        if (s.CanPurchase) return "locked";
        if (s.CanHarvest) return "ready";
        if (s.CanPlant) return "empty";
        if (s.Timer != null && !s.CanWater && s.RegrowsRemaining < 0) return "bonus";
        if (s.Timer != null && s.RegrowsRemaining >= 0) return "regrowing";
        return "growing";
    }

    static void Farm(Utf8JsonWriter w, Game game)
    {
        FarmingEntity? farm = FarmOf(game);
        BankEntity? bank = BankOf(game);
        if (farm == null || bank == null) { w.WriteBoolean("available", false); return; }
        w.WriteBoolean("available", true);
        var passive = farm.Passive;
        w.WriteStartArray("compost");
        foreach (Item c in passive.CompostItems)
        {
            w.WriteStartObject(); w.WriteString("id", c.Id); w.WriteString("name", c.DisplayName.GetPluralText(1)); w.WriteNumber("qty", bank.Inventory.GetQuantity(c)); w.WriteEndObject();
        }
        w.WriteEndArray();
        int plotIndex = 0;
        w.WriteStartArray("categories");
        foreach (var (category, active) in passive.ActiveCategories)
        {
            w.WriteStartObject();
            w.WriteString("id", category.Id);
            w.WriteString("name", category.DisplayName.Text);
            w.WriteStartArray("seeds");
            foreach (FarmingRecipe r in active.Recipes)
            {
                if (r.DisplayRequirements != null && !r.DisplayRequirements.AreMet) continue;
                w.WriteStartObject();
                w.WriteString("id", r.Id);
                w.WriteString("name", r.DisplayName.Text);
                w.WriteString("product", r.Product.Item.Id);
                Item? seed = r.Ingredients.Items.FirstOrDefault()?.Item;
                if (seed != null)
                {
                    w.WriteString("seed", seed.Id);
                    w.WriteString("seedName", seed.DisplayName.GetPluralText(2));
                    w.WriteNumber("need", r.Ingredients.Items.First().Quantity);
                    w.WriteNumber("have", bank.Inventory.GetQuantity(seed));
                }
                w.WriteNumber("minutes", Math.Round(r.BaseInterval / 60000.0, 1));
                Need(w, r.Requirements);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("plots");
            foreach (ActiveFarmingPlot p in active.Plots)
            {
                plotIndex++;
                if (!p.IsPurchased && !p.Plot.Requirements.AreMet && p.Plot.DisplayRequirements != null && !p.Plot.DisplayRequirements.AreMet) continue;
                w.WriteStartObject();
                w.WriteString("id", p.Plot.Id);
                w.WriteString("name", p.Plot.DisplayName.Text);
                string state = PlotState(p);
                w.WriteString("state", state);
                if (p.PlantedRecipe is { } planted)
                {
                    w.WriteString("crop", planted.Recipe.DisplayName.Text);
                    w.WriteString("cropItem", planted.Product.Item.Id);
                }
                if (p.State.Timer is { } timer && state != "ready")
                {
                    w.WriteNumber("msLeft", Math.Round(GamesByMalcs.Melvor2.Core.TickTimer.TickTimer.TicksToMS(timer.TicksLeft)));
                    w.WriteNumber("progress", Math.Round(Math.Clamp(timer.Progress, 0, 1), 3));
                }
                if (p.CurrentCompost is { } compost) w.WriteString("compost", compost.Item.DisplayName.GetPluralText(1));
                if (p.SelectedRecipe is { } sel) w.WriteString("selected", sel.Recipe.Id);
                if (state == "locked")
                {
                    Costs(w, "costs", p.Plot.Costs, i => bank.Inventory.GetQuantity(i), c => passive.Shop.Wallet.GetQuantity(c), out long max);
                    bool ok = p.Plot.Requirements.AreMet;
                    w.WriteBoolean("canBuy", ok && max >= 1);
                    if (!ok) w.WriteString("why", Why(p.Plot.Requirements));
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    static ActiveFarmingPlot? PlotById(FarmingEntity farm, string id) => farm.Passive.ActivePlots.Values.FirstOrDefault(p => p.Plot.Id == id);

    public static (bool, string) Plant(Game game, string plotId, string recipeId, string compostId)
    {
        FarmingEntity? farm = FarmOf(game);
        if (farm == null) return (false, "No Farming here.");
        ActiveFarmingPlot? plot = PlotById(farm, plotId);
        if (plot == null) return (false, "No such plot.");
        // (the plot's own CanPlant also wants a seed picked, so the state is asked first)
        if (!plot.State.CanPlant) return (false, plot.CanHarvest ? "Harvest it first." : !plot.IsPurchased ? "Buy that plot first." : "Something is growing there.");
        FarmingRecipe? recipe = farm.Passive.ActiveCategories.Values.SelectMany(c => c.Recipes).FirstOrDefault(r => r.Id == recipeId);
        if (recipe == null) return (false, "No such seed.");
        if (recipe.Category != plot.Plot.Category) return (false, $"{recipe.DisplayName.Text} does not grow in {plot.Plot.DisplayName.Text}.");
        if (!recipe.Requirements.AreMet) return (false, $"{recipe.DisplayName.Text} needs {Why(recipe.Requirements)}.");
        plot.SelectRecipe(recipe);
        if (compostId != "" && ItemById(game, compostId) is { } compost && compost.Capabilities.Contains<CompostCapability>() && plot.CanCompost)
        {
            plot.SelectCompost(compost);
            plot.Compost();
        }
        if (!plot.CanPlantSelectedRecipe()) return (false, $"Not enough seeds for {recipe.DisplayName.Text}.");
        plot.Plant();
        return plot.CanPlant ? (false, $"{recipe.DisplayName.Text} could not be planted.") : (true, $"Planted {recipe.DisplayName.Text} in {plot.Plot.DisplayName.Text}.");
    }

    public static (bool, string) Harvest(Game game, string plotId)
    {
        FarmingEntity? farm = FarmOf(game);
        if (farm == null) return (false, "No Farming here.");
        var plots = plotId == "" ? farm.Passive.ActivePlots.Values.Where(p => p.CanHarvest).ToList()
                                 : PlotById(farm, plotId) is { } one ? new List<ActiveFarmingPlot> { one } : new();
        if (plots.Count == 0) return (false, plotId == "" ? "Nothing is ready to harvest." : "No such plot.");
        int done = 0;
        var got = new Dictionary<string, long>();
        foreach (ActiveFarmingPlot p in plots)
        {
            if (!p.CanHarvest) continue;
            var product = p.ReadyToHarvestProduct;
            p.Harvest();
            if (p.CanHarvest) continue;           // (bank full: it stays)
            done++;
            if (product is { } q) got[q.Item.DisplayName.GetPluralText(2)] = got.GetValueOrDefault(q.Item.DisplayName.GetPluralText(2)) + q.Quantity;
        }
        if (done == 0) return (false, "Nothing could be harvested: the bank may be full.");
        string what = string.Join(", ", got.Select(g => $"{g.Value:N0} {g.Key}"));
        return (true, $"Harvested {done} plot{(done == 1 ? "" : "s")}" + (what != "" ? $": {what}." : ".") + (done < plots.Count ? " Some stayed (bank full?)." : ""));
    }

    public static (bool, string) BuyPlot(Game game, string plotId)
    {
        FarmingEntity? farm = FarmOf(game);
        if (farm == null) return (false, "No Farming here.");
        ActiveFarmingPlot? plot = PlotById(farm, plotId);
        if (plot == null) return (false, "No such plot.");
        if (plot.IsPurchased) return (false, "You own that plot already.");
        if (!plot.Plot.Requirements.AreMet) return (false, $"That plot needs {Why(plot.Plot.Requirements)}.");
        var tx = farm.Passive.GetCostsTransaction(plot.Plot.Costs);
        if (tx.Validate() != InventoryWalletTransactionResult.Success) return (false, "You cannot pay for it yet.");
        plot.Purchase();
        return plot.IsPurchased ? (true, $"Bought {plot.Plot.DisplayName.Text}.") : (false, "The plot could not be bought.");
    }

    // ------------------------------------------------------------------------------------------- the shop

    static ShopComponent? ShopOf(Game game) => game.Entities.GetAllObjects().OfType<ShopEntity>().FirstOrDefault()?.Shop;

    static void Shop(Utf8JsonWriter w, Game game)
    {
        ShopComponent? shop = ShopOf(game);
        if (shop == null) { w.WriteBoolean("available", false); return; }
        w.WriteBoolean("available", true);
        w.WriteStartArray("wallet");
        foreach (Currency c in game.Data.GetDataRegistryOutput<Currency>().GetAllObjects())
        {
            long q = shop.Wallet.GetQuantity(c);
            if (q <= 0) continue;
            w.WriteStartObject(); w.WriteString("id", c.Id); w.WriteString("name", c.DisplayName.Text); w.WriteNumber("qty", q); w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("categories");
        foreach (var (category, purchases) in shop.PurchasesByCategory)
        {
            if (category.DisplayRequirements != null && !category.DisplayRequirements.AreMet) continue;
            var shown = purchases.Where(p => p.DisplayRequirements == null || p.DisplayRequirements.AreMet).ToList();
            if (shown.Count == 0) continue;
            w.WriteStartObject();
            w.WriteString("id", category.Id);
            w.WriteString("name", category.DisplayName.Text);
            w.WriteStartArray("items");
            foreach (ShopPurchase p in shown)
            {
                w.WriteStartObject();
                w.WriteString("id", p.Id);
                w.WriteString("name", p.DisplayName.Text);
                // (several share a name, like "+20 Bank Slots" for each skill: the group tells them apart)
                try { string group = p.Subcategory.DisplayName.Text; if (group != "" && group != category.DisplayName.Text) w.WriteString("group", group); } catch { }
                if (p.Description != null) { try { string d = p.Description.Text; if (d.Length > 160) d = d[..157] + "..."; w.WriteString("desc", d); } catch { } }
                Item? provides = p.Provides.Items.FirstOrDefault()?.Item;
                if (provides != null) w.WriteString("item", provides.Id);
                w.WriteBoolean("bulk", p.AllowBulkPurchase);
                int bought = shop.GetUpgradesPurchased(p);
                w.WriteNumber("bought", bought);
                w.WriteNumber("limit", p.DefaultBuyLimit);
                bool done = shop.IsPurchased(p);
                w.WriteBoolean("owned", done);
                Costs(w, "costs", p.Costs, i => shop.Inventory.GetQuantity(i), c => shop.Wallet.GetQuantity(c), out long max);
                if (p.DefaultBuyLimit > 0) max = Math.Min(max, Math.Max(0, p.DefaultBuyLimit - bought));
                w.WriteNumber("max", Math.Min(max, 100000));
                bool can = !done && p.Requirements.AreMet;
                w.WriteBoolean("can", can);
                if (!p.Requirements.AreMet) w.WriteString("why", Why(p.Requirements));
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    public static (bool, string) Buy(Game game, string purchaseId, long quantity)
    {
        ShopComponent? shop = ShopOf(game);
        if (shop == null) return (false, "No shop here.");
        ShopPurchase? p = shop.Purchases.FirstOrDefault(x => x.Id == purchaseId);
        if (p == null) return (false, "That is not in the shop.");
        if (p.DisplayRequirements != null && !p.DisplayRequirements.AreMet) return (false, "That is not in the shop yet.");
        if (shop.IsPurchased(p)) return (false, "You have bought all of those already.");
        if (!p.Requirements.AreMet) return (false, $"{p.DisplayName.Text} needs {Why(p.Requirements)}.");
        int q = p.AllowBulkPurchase ? (int)Math.Clamp(quantity, 1, 100000) : 1;
        q = shop.CapPurchaseQuantity(p, q);
        if (q <= 0) return (false, "You have bought all of those already.");
        var result = shop.ProcessPurchase(p, q);
        return result switch
        {
            InventoryWalletTransactionResult.Success => (true, $"Bought {(q > 1 ? $"{q:N0} × " : "")}{p.DisplayName.Text}."),
            InventoryWalletTransactionResult.CantAffordItems or InventoryWalletTransactionResult.CantAffordCurrencies => (false, "You cannot pay for that."),
            InventoryWalletTransactionResult.NotEnoughInventorySpace => (false, "The bank is full."),
            InventoryWalletTransactionResult.RequirementsNotMet => (false, $"{p.DisplayName.Text} needs {Why(p.Requirements)}."),
            _ => (false, "The purchase did not go through."),
        };
    }

    // ------------------------------------------------------------------------------------------- our other mods

    static Type? FindType(string name)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetName().Name != "PhoneScreen" && a.GetType(name, false) is { } t) return t;
        return null;
    }

    static object? Static(Type? t, string name) =>
        t == null ? null : t.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? t.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    static object? Member(object? o, string name) =>
        o == null ? null : o.GetType().GetProperty(name)?.GetValue(o) ?? o.GetType().GetField(name)?.GetValue(o);

    static object? Call(Type? t, string name, params object?[] args)
    {
        MethodInfo? m = t?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
        return m?.Invoke(null, args);
    }

    static long Sum(object? dict) => dict is IDictionary d ? d.Values.Cast<object>().Sum(v => Convert.ToInt64(v)) : 0;

    static readonly Type? s_roster = FindType("Adventurers.Roster");
    static readonly Type? s_bulk = FindType("BulkSell.Bank");
    static readonly Type? s_junk = FindType("BulkSell.Junk");
    static readonly Type? s_slots = FindType("Multitask.Slots");
    static readonly Type? s_engine = FindType("CheatMenu.Engine");
    static readonly Type? s_features = FindType("CheatMenu.Features");

    static void More(Utf8JsonWriter w, Game game)
    {
        BankEntity? bank = BankOf(game);

        // Adventurers: the team with what the phone can order
        w.WriteBoolean("team", s_roster != null);
        if (s_roster != null && Call(s_roster, "BoundTo", game) is true && Static(s_roster, "Team") is IEnumerable team)
        {
            w.WriteStartArray("adventurers");
            foreach (object a in team)
            {
                w.WriteStartObject();
                w.WriteString("id", Member(a, "Id")?.ToString() ?? "");
                w.WriteString("name", Member(a, "Name")?.ToString() ?? "");
                string stopped = Member(a, "Stopped")?.ToString() ?? "";
                bool working = Member(a, "Working") is true;
                w.WriteString("status", stopped != "" ? "Stopped: " + stopped : working ? "Hunting" : "Resting");
                w.WriteBoolean("working", working);
                string enemyId = Member(a, "Enemy")?.ToString() ?? "", areaId = Member(a, "Area")?.ToString() ?? "", dungeon = Member(a, "Dungeon")?.ToString() ?? "";
                w.WriteString("enemy", enemyId);
                w.WriteString("area", areaId);
                if (dungeon != "") w.WriteString("dungeon", dungeon);
                try
                {
                    object? t = Call(s_roster, "TargetOf", game, a);
                    if (t != null && t.GetType().GetField("Item2")?.GetValue(t) is Enemy en) w.WriteString("target", en.DisplayName.Text);
                }
                catch { }
                string food = Member(a, "Food")?.ToString() ?? "";
                if (food != "" && ItemById(game, food) is { } fi) { w.WriteString("food", fi.DisplayName.GetPluralText(2)); w.WriteString("foodId", fi.Id); if (bank != null) w.WriteNumber("foodInBank", bank.Inventory.GetQuantity(fi)); }
                w.WriteNumber("kills", Convert.ToInt64(Member(a, "Kills") ?? 0L));
                w.WriteNumber("bag", Sum(Member(a, "Bag")));
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }

        // Bulk Sell: what each button would do now
        w.WriteBoolean("bulk", s_bulk != null && bank != null);
        if (s_bulk != null && bank != null)
        {
            try
            {
                Call(s_junk, "Bind", game.CharacterName);
                object? junk = Call(s_junk, "InBank", bank);
                object? bury = Call(s_bulk, "Buriable", bank);
                object? open = Call(s_bulk, "Openable", bank);
                w.WriteStartObject("bulkNow");
                w.WriteNumber("junk", Convert.ToInt64(junk?.GetType().GetField("Item2")?.GetValue(junk) ?? 0L));
                if (junk?.GetType().GetField("Item3")?.GetValue(junk) is { } worth) w.WriteString("junkWorth", Call(s_bulk, "Money", worth)?.ToString() ?? "");
                w.WriteNumber("junkKinds", (junk?.GetType().GetField("Item1")?.GetValue(junk) as ICollection)?.Count ?? 0);
                w.WriteNumber("bury", Convert.ToInt64(bury?.GetType().GetField("Item2")?.GetValue(bury) ?? 0L));
                if (bury?.GetType().GetField("Item3")?.GetValue(bury) is { } reward) w.WriteString("buryReward", Call(s_bulk, "Money", reward)?.ToString() ?? "");
                w.WriteNumber("open", Convert.ToInt64(open?.GetType().GetField("Item2")?.GetValue(open) ?? 0L));
                w.WriteEndObject();
            }
            catch (Exception e) { w.WriteString("bulkError", (e.InnerException ?? e).Message); }
        }

        // Multitask
        w.WriteBoolean("multitask", s_slots != null);
        if (s_slots != null)
        {
            w.WriteStartObject("slots");
            w.WriteNumber("extra", Convert.ToInt32(Static(s_slots, "Extra") ?? 0));
            w.WriteNumber("max", Convert.ToInt32(Static(s_slots, "Count") ?? 0));
            if (Static(s_slots, "WaitForMaterials") is bool wait) w.WriteBoolean("wait", wait);
            w.WriteBoolean("canDrop", s_slots.GetMethod("Drop", new[] { typeof(int) }) != null);
            w.WriteStartArray("recent");
            if (Static(s_slots, "Recent") is IEnumerable recent)
                foreach (object k in recent.Cast<object>().Take(16)) { w.WriteStartObject(); w.WriteString("name", Member(k, "Title")?.ToString() ?? Member(k, "Name")?.ToString() ?? ""); w.WriteString("state", Member(k, "State")?.ToString() ?? ""); w.WriteEndObject(); }
            w.WriteEndArray();
            w.WriteEndObject();
        }

        // Cheat Menu: every option with its value
        w.WriteBoolean("cheats", s_engine != null && s_features != null);
        if (s_engine != null && s_features != null && Static(s_engine, "Game") == game && Static(s_features, "All") is IEnumerable all)
        {
            var hints = Static(s_features, "Hints") as IDictionary;
            w.WriteStartArray("cheatList");
            foreach (object f in all)
            {
                string id = Member(f, "Id")?.ToString() ?? "";
                if (id == "" || Member(f, "Custom") is true) continue;
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("tab", Member(f, "Tab")?.ToString() ?? "");
                w.WriteString("name", Member(f, "En")?.ToString() ?? id);
                w.WriteString("kind", Member(f, "Kind")?.ToString() ?? "");
                w.WriteNumber("min", Convert.ToDouble(Member(f, "Min") ?? 0.0));
                w.WriteNumber("max", Convert.ToDouble(Member(f, "Max") ?? 0.0));
                w.WriteNumber("step", Convert.ToDouble(Member(f, "Step") ?? 1.0));
                w.WriteNumber("off", Convert.ToDouble(Member(f, "Off") ?? 0.0));
                w.WriteString("suffix", Member(f, "Suffix")?.ToString() ?? "");
                w.WriteNumber("value", Convert.ToDouble(Call(s_engine, "GetValue", id) ?? 0.0));
                if (hints != null && hints.Contains(id)) w.WriteString("hint", hints[id]?.ToString() ?? "");
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
    }

    public static (bool, string) Adventurer(Game game, string id, string op, string arg1, string arg2)
    {
        if (s_roster == null) return (false, "The Adventurers mod is not installed.");
        if (Call(s_roster, "BoundTo", game) is not true) return (false, "The Adventurers are not ready yet.");
        object? a = (Static(s_roster, "Team") as IEnumerable)?.Cast<object>().FirstOrDefault(x => Member(x, "Id")?.ToString() == id);
        if (a == null) return (false, "No such adventurer.");
        string name = Member(a, "Name")?.ToString() ?? "The adventurer";
        string Result(object? r, string ok) => r is string s && s != "" ? s : ok;
        switch (op)
        {
            case "food":
            {
                Item? food = arg1 == "" ? null : ItemById(game, arg1);
                if (arg1 != "" && food == null) return (false, "No such food.");
                string r = Result(Call(s_roster, "SetFood", game, a, food), "");
                return r == "" ? (true, food == null ? $"{name} carries no food now." : $"{name} eats {food.DisplayName.GetPluralText(2)} now.") : (false, r);
            }
            case "send":
            {
                if (!game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) || e is not CombatEntity combat) return (false, "No combat here.");
                var area = combat.Areas.ActiveAreas.Keys.OfType<GamesByMalcs.Melvor2.Components.CombatAreas.CombatArea>().FirstOrDefault(x => x.Id == arg1);
                Enemy? enemy = area?.Enemies.FirstOrDefault(x => x.Id == arg2);
                if (area == null || enemy == null) return (false, "No such monster there.");
                string r = Result(Call(s_roster, "Send", game, a, area, enemy), "");
                return r == "" ? (true, $"{name} hunts {enemy.DisplayName.Text} now.") : (false, r);
            }
            case "resume":
            {
                object? t = Call(s_roster, "TargetOf", game, a);
                object? area = t?.GetType().GetField("Item1")?.GetValue(t), enemy = t?.GetType().GetField("Item2")?.GetValue(t);
                if (area == null || enemy == null)
                {
                    object? dungeon = Call(s_roster, "DungeonOf", game, a);
                    if (dungeon == null) return (false, $"{name} has no target: pick one.");
                    string rd = Result(Call(s_roster, "SendDungeon", game, a, dungeon), "");
                    return rd == "" ? (true, $"{name} is back in the dungeon.") : (false, rd);
                }
                string r = Result(Call(s_roster, "Send", game, a, area, enemy), "");
                return r == "" ? (true, $"{name} is hunting again.") : (false, r);
            }
            case "recall":
                Call(s_roster, "Recall", game, a);
                return (true, $"{name} is coming home.");
            case "collect":
                Call(s_roster, "Work", game);
                long left = Sum(Member(a, "Bag"));
                return left == 0 ? (true, "Their loot is in your bank.") : (false, $"{left:N0} items are still waiting: the bank is full.");
        }
        return (false, "Unknown order.");
    }

    public static (bool, string) Bulk(Game game, string op)
    {
        BankEntity? bank = BankOf(game);
        if (s_bulk == null || bank == null) return (false, "The Bulk Sell mod is not installed.");
        switch (op)
        {
            case "junk":
            {
                Call(s_junk, "Bind", game.CharacterName);
                object? junk = Call(s_junk, "InBank", bank);
                if (junk?.GetType().GetField("Item1")?.GetValue(junk) is not IEnumerable items || !items.Cast<object>().Any()) return (false, "No junk in the bank (set the junk list in the game's Bulk sell window).");
                var list = items.Cast<Item>().ToList();
                object? r = s_bulk.GetMethod("Sell")?.Invoke(null, new object[] { bank, list, 0 });
                long n = Convert.ToInt64(r?.GetType().GetField("Item1")?.GetValue(r) ?? 0L);
                string paid = Call(s_bulk, "Money", r?.GetType().GetField("Item2")?.GetValue(r))?.ToString() ?? "";
                return n > 0 ? (true, $"Sold {n:N0} junk items for {paid}.") : (false, "Nothing was sold (locked?).");
            }
            case "bury":
            {
                object? r = Call(s_bulk, "BuryAll", bank);
                long n = Convert.ToInt64(r?.GetType().GetField("Item1")?.GetValue(r) ?? 0L);
                string got = Call(s_bulk, "Money", r?.GetType().GetField("Item2")?.GetValue(r))?.ToString() ?? "";
                return n > 0 ? (true, $"Buried {n:N0}" + (got != "" ? $" for {got}." : ".")) : (false, "Nothing to bury.");
            }
            case "open":
            {
                object? r = Call(s_bulk, "OpenAll", bank);
                long n = Convert.ToInt64(r?.GetType().GetField("Item1")?.GetValue(r) ?? 0L);
                bool full = r?.GetType().GetField("Item3")?.GetValue(r) is true;
                return n > 0 ? (true, $"Opened {n:N0}." + (full ? " The bank got full: some are still closed." : "")) : (false, full ? "The bank is full." : "Nothing to open.");
            }
        }
        return (false, "Unknown command.");
    }

    public static (bool, string) Slots(int extra)
    {
        if (s_slots == null) return (false, "The Multitask mod is not installed.");
        int max = Convert.ToInt32(Static(s_slots, "Count") ?? 0);
        extra = Math.Clamp(extra, 0, max);
        Call(s_slots, "SetExtra", extra);
        int now = Convert.ToInt32(Static(s_slots, "Extra") ?? 0);
        return now == extra ? (true, extra == 0 ? "Multitask off: one action at a time." : $"Multitask: {1 + extra} actions at once.") : (false, "Multitask did not change.");
    }

    /// <summary>Multitask's x: one action off the list (stopped if it runs, not started again).</summary>
    public static (bool, string) SlotDrop(int index)
    {
        MethodInfo? drop = s_slots?.GetMethod("Drop", new[] { typeof(int) });
        if (drop == null) return (false, "This Multitask cannot take actions off its list (update it).");
        string name = (Static(s_slots, "Recent") as IEnumerable)?.Cast<object>().Skip(index).Select(k => Member(k, "Title")?.ToString() ?? Member(k, "Name")?.ToString()).FirstOrDefault() ?? "";
        return drop.Invoke(null, new object[] { index }) is true ? (true, $"Took {(name == "" ? "it" : name)} off the list.") : (false, "That action is not on the list any more.");
    }

    /// <summary>Multitask: whether an action whose materials ran out stays on the list, waiting.</summary>
    public static (bool, string) SlotWait(bool wait)
    {
        MethodInfo? set = s_slots?.GetMethod("SetWaitForMaterials", new[] { typeof(bool) });
        if (set == null) return (false, "This Multitask has no such setting (update it).");
        set.Invoke(null, new object[] { wait });
        return (true, wait ? "An action out of materials waits on the list." : "An action out of materials is dropped from the list.");
    }

    public static (bool, string) Cheat(Game game, string id, double value)
    {
        if (s_engine == null || s_features == null) return (false, "The Cheat Menu mod is not installed.");
        if (Static(s_engine, "Game") != game) return (false, "The Cheat Menu is not ready for this character yet.");
        object? f = (Static(s_features, "All") as IEnumerable)?.Cast<object>().FirstOrDefault(x => Member(x, "Id")?.ToString() == id);
        if (f == null) return (false, "No such cheat.");
        Call(s_engine, "SetValue", id, value);
        double now = Convert.ToDouble(Call(s_engine, "GetValue", id) ?? 0.0);
        string name = Member(f, "En")?.ToString() ?? id;
        bool toggle = Member(f, "Kind")?.ToString() == "Toggle";
        return (true, toggle ? $"{name}: {(now != 0 ? "on" : "off")}." : $"{name}: {now:0.##}{Member(f, "Suffix")}.");
    }
}
