using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Core;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Combat;

namespace OneStyle;

/// <summary>Rare items while a character is open: back from the save, dropping, and the give-back list at every save.</summary>
public static partial class RareItems
{
    static Game? s_game;
    static OneStyleData? s_entity;
    public static SavedData Data => s_entity?.Data ?? new SavedData();
    public static int Restored { get; private set; }
    public static List<string> Problems { get; } = new();
    /// <summary>The last items that dropped (tests and the window).</summary>
    public static List<RolledItem> LastDrops { get; } = new();

    /// <summary>The game's core systems (a private record of Game, so read by reflection).</summary>
    static ICoreSystems CoreOf(Game game) =>
        (ICoreSystems)typeof(Game).GetProperty("Core", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(game)!;
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Equipped")]
    static extern void SetLoadoutEquipped(EquipmentSlotLoadout slot, EquipmentItem? item);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Quantity")]
    static extern void SetLoadoutQuantity(EquipmentSlotLoadout slot, int quantity);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Loadouts")]
    static extern Dictionary<GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot, EquipmentLoadout> LoadoutsOf(EquipmentComponent equipment);

    public static bool IsOurs(Item item) => item.LocalId.StartsWith("OsR") || item.LocalId.StartsWith("OsU");
    public static RolledItem? RecordOf(Item item) =>
        !IsOurs(item) ? null
        : Data.Items.FirstOrDefault(r => r.LocalId == item.LocalId)
          ?? (Uniques.Defs.TryGetValue(item.LocalId, out UniqueDef? u) ? Uniques.RecordOf(u) : null);   // a unique item is always the same

    static CombatEntity? Combat(Game game) => game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) ? e as CombatEntity : null;
    static BankEntity? Bank(Game game) => game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) ? e as BankEntity : null;

    /// <summary>
    /// A character was opened (before its first save): One Style's part of the save is read back, every item of it is
    /// made into a game item and registered, and the entity that writes it again is added to the game.
    /// Step one of two; <see cref="Restore"/> comes after the extra ring slots exist.
    /// </summary>
    public static SaveScan? Bind(Game game)
    {
        if (ReferenceEquals(s_game, game)) return null;
        s_game = game;
        Problems.Clear();
        LastDrops.Clear();
        Restored = 0;
        Forget();
        SaveScan scan;
        try { scan = SaveScan.Of(game); }
        catch (Exception e) { Problems.Add("the save could not be read: " + e.Message); scan = new SaveScan(); }
        try
        {
            DataNamespace ns = game.Data.GetObject<Item>("MelvorBase:Bone").Namespace;
            if (game.Entities.TryGetObjectById(OneStyleData.FullId, out GameEntity? had) && had is OneStyleData old) s_entity = old;
            else
            {
                s_entity = new OneStyleData(CoreOf(game), ns);
                game.Entities.RegisterObject(s_entity);
            }
            s_entity.Data = scan.Data ?? new SavedData();
            OneStyleData.GiveBack = () => GiveBackList(game);
            foreach (RolledItem r in s_entity.Data.Items.ToList())
                if (Make(game, r) == null) Problems.Add("an item could not be made: " + r.Name);
        }
        catch (Exception e) { Problems.Add("rare items: " + e.Message); Log("One Style: rare items could not be set up: " + e); }
        return scan;
    }

    /// <summary>Step two: the bank stacks, worn items and loadout items the game skipped go back where they were.</summary>
    public static void Restore(Game game, SaveScan? scan)
    {
        if (scan == null || !scan.Read || Combat(game) is not { } combat || Bank(game) is not { } bank) return;
        try
        {
            var registry = game.Data.GetDataRegistryOutput<Item>();
            Item? Ours(string id) => id != "" && registry.TryGetObjectById(id, out Item? i) && IsOurs(i) ? i : null;
            BankInventoryComponent inventory = bank.Inventory;
            // back into the same tab at the same place (in the order they were, so each lands where it was), locked again
            // when the player had locked it
            foreach (SaveScan.Stack s in scan.Stacks.OrderBy(s => s.Tab).ThenBy(s => s.Index))
            {
                if (Ours(s.ItemId) is not { } item || s.Quantity <= 0 || inventory.GetQuantity(item) > 0) continue;
                bool inTab = s.Tab >= 0 && s.Tab < inventory.InventoryTabs.Count;
                if (inTab) inventory.DefaultItemTabs[item] = inventory.InventoryTabs[s.Tab];
                if (inventory.AddQuantity(item, s.Quantity, AddItemFlags.None) <= 0) { Data.Pending.Add(item.LocalId); continue; }
                Restored++;
                if (!inTab) continue;
                try
                {
                    var tab = inventory.InventoryTabs[s.Tab];
                    int at = -1;
                    for (int i = tab.Stacks.Count - 1; i >= 0; i--) if (ReferenceEquals(tab.Stacks[i].Item, item)) { at = i; break; }
                    if (at >= 0 && at != s.Index) tab.MoveStack(at, Math.Min(s.Index, tab.StackCount - 1));
                    if (s.Locked)
                    {
                        tab.SetItemAsLocked(item, true);
                        LockedItemsOf(inventory).Add(item);
                    }
                }
                catch (Exception e) { Log("One Style: a rare item went back to the end of its tab: " + e.Message); }
            }
            EquipmentComponent equipment = combat.Equipment;
            foreach (SaveScan.Slot s in scan.Slots)
            {
                if (s.Occupied || Ours(s.ItemId) is not { } item || !game.Data.TryGetObject(s.SlotId, out EquipmentSlot? slot)) continue;
                if (!equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? active) || !active.IsEmpty) continue;
                if (equipment.TryEquipItem(item, Math.Max(1, s.Quantity), slot)) Restored++;
                else Data.Pending.Add(item.LocalId);
            }
            Dictionary<GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot, EquipmentLoadout> loadouts = LoadoutsOf(equipment);
            foreach (SaveScan.LoadoutSlot s in scan.Loadouts)
            {
                if (s.Occupied || Ours(s.ItemId) is not { } item) continue;
                if (!game.Data.TryGetObject(s.LoadoutId, out GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot? loadoutSlot)
                    || !loadouts.TryGetValue(loadoutSlot, out EquipmentLoadout? loadout) || !game.Data.TryGetObject(s.SlotId, out EquipmentSlot? slot)) continue;
                if (loadout.Slots.TryGetValue(slot, out EquipmentSlotLoadout? have) && have.Equipped != null) continue;
                var saved = new EquipmentSlotLoadout();
                SetLoadoutEquipped(saved, new EquipmentItem(item, item.Capabilities.Get<EquipmentCapability>()));
                SetLoadoutQuantity(saved, Math.Max(1, s.Quantity));
                loadout.Slots[slot] = saved;
                Restored++;
            }
            DeliverPending(game);
            WatchGear(game);
        }
        catch (Exception e) { Problems.Add("restore: " + e.Message); Log("One Style: rare items could not be put back: " + e); }
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = "get_LockedItems")]
    static extern HashSet<Item> LockedItemsOf(BankInventoryComponent bank);

    /// <summary>
    /// With the game's "automatic placeholders" on, a rare item that is sold, lost or destroyed would keep an empty
    /// placeholder in the bank for ever (the same item never comes back). Those go; a rare item still worn or in a
    /// loadout keeps its placeholder.
    /// </summary>
    public static void TidyPlaceholders(Game game)
    {
        if (Bank(game) is not { } bank) return;
        try
        {
            var owned = Owned(game).Select(o => o.Item).ToHashSet();
            foreach (Item item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
                if (IsOurs(item) && !owned.Contains(item) && bank.Inventory.GetQuantity(item) <= 0)
                    bank.Inventory.SetItemAsPlaceholder(item, false);
        }
        catch (Exception e) { Log("One Style: rare item placeholders: " + e.Message); }
    }

    public static void Unbind()
    {
        s_capsOn = false;
        s_watchedEquipment = null;
        s_game = null;
        s_entity = null;
        OneStyleData.GiveBack = null;
    }

    // ================================================================ owning, giving back

    /// <summary>Every One Style item the player has now: in the bank, worn and in loadouts (occupied slots do not count).</summary>
    public static List<(Item Item, int Quantity)> Owned(Game game)
    {
        var list = new List<(Item, int)>();
        if (Combat(game) is not { } combat || Bank(game) is not { } bank) return list;
        var registry = game.Data.GetDataRegistryOutput<Item>();
        foreach (RolledItem r in Data.Items)
        {
            if (!registry.TryGetObjectById("MelvorBase:" + r.LocalId, out Item? item)) continue;
            int n = bank.Inventory.GetQuantity(item);
            foreach (EquipmentSlot slot in combat.Equipment.Config.EquipmentSlots)
                if (combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty && !a.IsOccupied && ReferenceEquals(a.Equipped!.Item, item))
                    n += Math.Max(1, a.Quantity);
            foreach (EquipmentLoadout l in LoadoutsOf(combat.Equipment).Values)
                foreach (EquipmentSlotLoadout s in l.Slots.Values)
                    if (!s.IsOccupied && s.Equipped != null && ReferenceEquals(s.Equipped.Item, item)) n += Math.Max(1, s.Quantity);
            if (n > 0) list.Add((item, n));
        }
        return list;
    }

    /// <summary>At every save: what to give back as normal items if One Style is gone; records no longer owned are dropped.</summary>
    static List<(string, int)> GiveBackList(Game game)
    {
        List<(Item Item, int Quantity)> owned = Owned(game);
        var ownedIds = owned.Select(o => o.Item.LocalId).ToHashSet();
        Data.Items.RemoveAll(r => !ownedIds.Contains(r.LocalId) && !Data.Pending.Contains(r.LocalId));
        var back = new Dictionary<string, int>();
        foreach (var (item, n) in owned)
        {
            string baseId = RecordOf(item) is { } r ? (r.UniqueId != "" ? Uniques.BaseOf(r.UniqueId) : r.BaseId) : "";
            if (baseId != "") back[baseId] = back.GetValueOrDefault(baseId) + n;
        }
        foreach (string pending in Data.Pending)
            if (Data.Items.FirstOrDefault(r => r.LocalId == pending) is { } r)
            {
                string baseId = r.UniqueId != "" ? Uniques.BaseOf(r.UniqueId) : r.BaseId;
                if (baseId != "") back[baseId] = back.GetValueOrDefault(baseId) + 1;
            }
        // normal rings in Ring Slot 3 to 6 (rare ones are counted above as their base ring)
        foreach (var (ring, n) in Rings.InAddedSlots(game))
            if (!IsOurs(ring)) back[ring.Id] = back.GetValueOrDefault(ring.Id) + n;
        return back.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// "Make all normal" (before removing the mod loader too): every One Style item is swapped for its normal base item,
    /// in the bank and where it is worn. Returns how many.
    /// </summary>
    public static int MakeAllNormal(Game game)
    {
        if (Combat(game) is not { } combat || Bank(game) is not { } bank) return 0;
        int done = 0;
        foreach (var (item, n) in Owned(game))
        {
            RolledItem? r = RecordOf(item);
            string baseId = r == null ? "" : r.UniqueId != "" ? Uniques.BaseOf(r.UniqueId) : r.BaseId;
            if (!game.Data.TryGetObject(baseId, out Item? baseItem)) continue;
            int inBank = bank.Inventory.GetQuantity(item);
            if (inBank > 0)
            {
                bank.Inventory.RemoveQuantity(item, inBank);
                bank.Inventory.AddQuantity(baseItem, inBank, AddItemFlags.None);
            }
            foreach (EquipmentSlot slot in combat.Equipment.Config.EquipmentSlots)
                if (combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty && !a.IsOccupied && ReferenceEquals(a.Equipped!.Item, item))
                {
                    int q = Math.Max(1, a.Quantity);
                    combat.Equipment.TryUnequipItem(item);
                    if (!combat.Equipment.TryEquipItem(baseItem, q, slot)) bank.Inventory.AddQuantity(baseItem, q, AddItemFlags.None);
                }
            foreach (EquipmentLoadout l in LoadoutsOf(combat.Equipment).Values)
                foreach (var (slot, s) in l.Slots.ToList())
                    if (!s.IsOccupied && s.Equipped != null && ReferenceEquals(s.Equipped.Item, item))
                    {
                        SetLoadoutEquipped(s, new EquipmentItem(baseItem, baseItem.Capabilities.Get<EquipmentCapability>()));
                    }
            done += n;
        }
        Data.Items.Clear();
        Data.Pending.Clear();
        return done;
    }

    // ================================================================ drops (03-karar.md 1; 04-esya-tasarim.md)

    /// <summary>A monster died online: elites always drop, a normal monster 2% of the time (1% Magic, 1% Rare).</summary>
    public const double NormalMagic = 0.01, NormalRare = 0.01;

    public static void OnKilled(Game game, Enemy enemy, EliteTier tier, bool special)
    {
        if (Affixes.Count == 0 || !ReferenceEquals(s_game, game)) return;
        try
        {
            int iLvl = Math.Max(1, enemy.CombatLevel + (special ? 5 : 0));
            string style = DropStyle();
            var drops = new List<Rarity>();
            double r1 = s_rng.NextDouble();
            Rarity ChampionRoll() { double r = s_rng.NextDouble(); return r < 0.60 ? Rarity.Magic : r < 0.93 ? Rarity.Rare : Rarity.Rare4; }
            switch (tier)
            {
                case EliteTier.Normal:
                    // 1% Magic, 1% Rare (the player's pick, 2026-11-24, after Codex and Gemini); Hoarder's Eye and Scrap Hunter raise both
                    double more = UniquePowers.RareLootMultiplier;
                    if (r1 < NormalMagic * more) drops.Add(Rarity.Magic);
                    else if (r1 < (NormalMagic + NormalRare) * more) drops.Add(s_rng.NextDouble() < 0.8 ? Rarity.Rare : Rarity.Rare4);
                    break;
                case EliteTier.Champion:
                    drops.Add(ChampionRoll());
                    break;
                case EliteTier.Unique:
                    drops.Add(s_rng.NextDouble() < 0.8 ? Rarity.Rare : Rarity.Rare4);
                    drops.Add(ChampionRoll());
                    break;
            }
            if (tier != EliteTier.Normal && special) drops.Add(ChampionRoll());   // on your slayer task or as a dungeon's last boss: one more
            if (tier != EliteTier.Normal) for (int i = UniquePowers.ExtraEliteRares(); i > 0; i--) drops.Add(Rarity.Rare);   // Deep Signal, Scrap Hunter
            foreach (Rarity rarity in drops)
                if (PickBase(game, iLvl, style) is { } baseItem) Give(game, Roll(game, baseItem, rarity, iLvl, style));
            if (tier == EliteTier.Unique) Uniques.OnUniqueKilled(game, enemy);
        }
        catch (Exception e) { Log("One Style: a drop failed: " + e.Message); }
    }

    /// <summary>A new item: kept in the save data, made, and put in the bank (or kept for later when the bank is full).</summary>
    public static Item? Give(Game game, RolledItem record)
    {
        Data.Items.Add(record);
        Item? item = Make(game, record);
        if (item == null) { Data.Items.Remove(record); return null; }
        LastDrops.Insert(0, record);
        if (LastDrops.Count > 20) LastDrops.RemoveAt(LastDrops.Count - 1);
        if (Bank(game) is not { } bank || bank.Inventory.AddQuantity(item, 1, AddItemFlags.None) <= 0) Data.Pending.Add(record.LocalId);
        DropToast(record);
        return item;
    }

    /// <summary>
    /// Sells every Magic item in the bank that is not locked, through the game's own sale (its price, its statistics).
    /// Rare and Unique items are never sold here; lock a Magic item in the bank to keep it. Returns (items, gold).
    /// </summary>
    public static (int Items, long Gold) SellMagic(Game game)
    {
        if (Bank(game) is not { } bank) return (0, 0);
        var magic = bank.Inventory.Stacks.Where(s => s.Quantity > 0 && !s.IsLocked && !s.IsPlaceholder && RecordOf(s.Item) is { Rarity: Rarity.Magic })
            .Select(s => (s.Item, (int)s.Quantity)).ToList();
        long gold = 0;
        int sold = 0;
        foreach (var (item, quantity) in magic)
        {
            var stats = bank.ItemSales.GetItemStats(item);
            try { bank.ItemSales.SellItem(item, stats, quantity); sold += quantity; gold += item.SellsFor.Quantity * quantity; }
            catch (Exception e) { Log("One Style: selling " + item.Id + " failed: " + e.Message); }
            finally { stats.Destroy(); }
        }
        DeliverPending(game);
        return (sold, gold);
    }

    /// <summary>Drops that did not fit go in when there is room.</summary>
    public static void DeliverPending(Game game)
    {
        if (Data.Pending.Count == 0 || Bank(game) is not { } bank) return;
        var registry = game.Data.GetDataRegistryOutput<Item>();
        foreach (string id in Data.Pending.ToList())
            if (registry.TryGetObjectById("MelvorBase:" + id, out Item? item) && bank.Inventory.AddQuantity(item, 1, AddItemFlags.None) > 0)
                Data.Pending.Remove(id);
    }
}

public static partial class RareItems
{
    /// <summary>For tests: an item with chosen affixes (id, tier, value), given like a drop.</summary>
    public static Item? Forge(Game game, string baseId, Rarity rarity, params (string Id, int Tier, double Value)[] affixes)
    {
        if (!game.Data.TryGetObject(baseId, out Item? baseItem)) return null;
        var record = new RolledItem
        {
            LocalId = "OsR" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), BaseId = baseId, Rarity = rarity, ILvl = 50,
            Affixes = affixes.Select(a => new RolledAffix { Id = a.Id, Tier = a.Tier, Value = a.Value }).ToList(),
        };
        record.Name = NameFor(record, baseItem);
        return Give(game, record);
    }

    /// <summary>For tests: a random drop of a rarity at an item level.</summary>
    public static Item? Drop(Game game, Rarity rarity, int iLvl) =>
        PickBase(game, iLvl, "") is { } b ? Give(game, Roll(game, b, rarity, iLvl, "")) : null;
}
