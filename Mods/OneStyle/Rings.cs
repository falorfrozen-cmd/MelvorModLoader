using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.ItemEquipping;
using GamesByMalcs.Melvor2.Components.Loadouts;
using GamesByMalcs.Melvor2.Conditions;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Localization;
using GamesByMalcs.Melvor2.Saves;
using Godot;

namespace OneStyle;

/// <summary>
/// Six ring slots instead of one. The game has a Ring Slot 2 in its data (behind Crafting 150, no skill goes that high)
/// but does not give it to the player yet: it is opened without the lock, and four more
/// ring slots are added to the equipment grid, for every character while the mod is installed.
///
/// The game builds its slots from its data when a character is opened and reads the save straight after, before any
/// mod can step in, so it skips the rings in the added slots as slots it does not know. They are still in the save
/// file (the game writes every slot it has): once the slots exist this class reads just those slots back from the same
/// file with the game's own reader, and books each ring the way the game does on load. The save stays the only record:
/// nothing to get out of step, nothing doubled.
///
/// Without the mod the game skips those slots and the rings in them are gone: <see cref="ToBank"/> takes them off first.
/// </summary>
public static class Rings
{
    /// <summary>Where the added slots go: a new bottom row, and the gap left of Legs.</summary>
    static readonly (string Id, string Name, int X, int Y)[] Added =
    {
        ("OneStyleRingSlot3", "Ring Slot 3", 0, 5),
        ("OneStyleRingSlot4", "Ring Slot 4", 1, 5),
        ("OneStyleRingSlot5", "Ring Slot 5", 2, 5),
        ("OneStyleRingSlot6", "Ring Slot 6", 0, 3),
    };
    public static int Total => 2 + Added.Length;
    /// <summary>The full ids of the ring slots One Style adds (the game does not know them without the mod).</summary>
    public static HashSet<string> AddedSlotIds { get; } = Added.Select(a => "MelvorBase:" + a.Id).ToHashSet();

    public static Action<string>? Log;
    /// <summary>What went wrong the last time, for the window and the tests.</summary>
    public static readonly List<string> Problems = new();
    /// <summary>Rings read back from the save the last time a character was opened (tests).</summary>
    public static int ReadBack { get; private set; }

    static Game? s_game;

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AddActiveSlot")]
    static extern void AddActiveSlot(EquipmentComponent equipment, EquipmentSlot slot);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AddSetBonusesForItem")]
    static extern void AddSetBonuses(EquipmentComponent equipment, Item item);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AddEquipmentModifiers")]
    static extern void AddModifiers(EquipmentComponent equipment, Item item, EquipmentCapability capability);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AddCombatStats")]
    static extern void AddStats(EquipmentComponent equipment, Item item, EquipmentCapability capability);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_equippedItems")]
    static extern ref Dictionary<Item, ActiveEquipmentSlot> EquippedItems(EquipmentComponent equipment);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Loadouts")]
    static extern Dictionary<LoadoutSlot, EquipmentLoadout> Loadouts(EquipmentComponent equipment);
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_equipmentSlots")]
    static extern ref NamespacedPriorityList<EquipmentSlot> SlotList(CombatEntityConfig config);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_UnlockRequirements")]
    static extern void SetUnlock(EquipmentSlot slot, ConditionGroup requirements);

    public static CombatEntity? Combat(Game game) =>
        game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) ? e as CombatEntity : null;

    /// <summary>Every ring slot of the open character, in order (tests and the window).</summary>
    public static List<IActiveEquipmentSlot> Slots(Game game)
    {
        var list = new List<IActiveEquipmentSlot>();
        if (Combat(game) is not { } combat) return list;
        foreach (string id in new[] { "MelvorBase:RingSlot1", "MelvorBase:RingSlot2" }.Concat(Added.Select(a => "MelvorBase:" + a.Id)))
            if (game.Data.TryGetObject(id, out EquipmentSlot? slot) && combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? active))
                list.Add(active);
        return list;
    }

    /// <summary>
    /// Call as soon as a character is open, before it saves for the first time. Once per character; later calls do
    /// nothing.
    /// </summary>
    public static void Add(Game game)
    {
        if (ReferenceEquals(s_game, game)) return;
        s_game = game;
        Problems.Clear();
        ReadBack = 0;
        try
        {
            if (Combat(game) is not { } combat) { Problems.Add("no combat"); return; }
            EquipmentComponent equipment = combat.Equipment;
            if (!game.Data.TryGetObject("MelvorBase:RingSlot1", out EquipmentSlot? first))
            {
                Problems.Add("the game has no Ring Slot 1");
                return;
            }
            var registry = (DataRegistry<EquipmentSlot>)game.Data.GetDataRegistryOutput<EquipmentSlot>();
            var config = (CombatEntityConfig)equipment.Config;
            ref NamespacedPriorityList<EquipmentSlot> list = ref SlotList(config);
            int priority = 100000;
            var ours = new Dictionary<EquipmentSlot, ActiveEquipmentSlot>();
            // Ring Slot 2 is in the game's data (locked behind Crafting 150) but not yet in the player's slot list.
            // It goes in under the game's own name, so a game version that opens it finds the same ring there.
            if (game.Data.TryGetObject("MelvorBase:RingSlot2", out EquipmentSlot? second))
            {
                if (second.UnlockRequirements.Conditions.Count > 0) SetUnlock(second, new ConditionGroup(Array.Empty<Condition>()));
                if (!equipment.TryGetActiveSlot(second, out _))
                {
                    list.Insert(priority++, second);
                    AddActiveSlot(equipment, second);
                    if (equipment.TryGetActiveSlot(second, out IActiveEquipmentSlot? active) && active is ActiveEquipmentSlot a) ours[second] = a;
                }
            }
            foreach (var (id, name, x, y) in Added)
            {
                if (game.Data.TryGetObject("MelvorBase:" + id, out EquipmentSlot? had)) continue;   // already there
                var slot = new EquipmentSlot(new LocalizedString(name), first.FitsType, new ConditionGroup(Array.Empty<Condition>()),
                    new Vector2I(x, y), null, first.Namespace, id);
                registry.RegisterObject(slot);
                list.Insert(priority++, slot);
                AddActiveSlot(equipment, slot);
                if (equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? active) && active is ActiveEquipmentSlot a) ours[slot] = a;
            }
            if (ours.Count > 0) ReadFromSave(game, equipment, ours);
            Regrid();
        }
        catch (Exception e)
        {
            Problems.Add(e.Message);
            Log?.Invoke("One Style: the extra ring slots could not be set up: " + e);
        }
    }

    public static void Forget() => s_game = null;

    // ---- reading the added slots back from the save -------------------------------------------------------------

    static object? SaveManagerValue(string name) =>
        typeof(SaveManager).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(null);

    static void ReadFromSave(Game game, EquipmentComponent equipment, Dictionary<EquipmentSlot, ActiveEquipmentSlot> ours)
    {
        if (SaveManagerValue("ActiveSaveId") is not Guid id || id == Guid.Empty) return;
        if (SaveManagerValue("ActiveSaveSource") is not ISaveSource source) return;
        Stream file;
        // always the active save: after loading a backup the game has already written it over the active save and
        // renumbered its backups, so the backup number would now point at another file
        try { file = source.GetReadStreamForId(id); }
        catch (Exception) { return; }   // a new character has no file yet
        using (file)
        using (var unpacked = new BrotliStream(file, CompressionMode.Decompress))
        using (var reader = new SaveReader(unpacked, game.Data, game.Entities))
        {
            reader.Read<SaveHeader>();
            int entities = reader.ReadInt32();
            for (int i = 0; i < entities; i++)
            {
                string entityId = reader.ReadString();
                if (entityId != "MelvorBase:Combat") { reader.SkipRegion(); continue; }
                using SaveReader combat = Inner(reader, game);
                int components = combat.ReadInt32();
                for (int j = 0; j < components; j++)
                {
                    string componentId = combat.ReadString();
                    if (componentId != "Equipment") { combat.SkipRegion(); continue; }
                    using SaveReader inner = Inner(combat, game);
                    ReadEquipment(inner, equipment, ours);
                    return;
                }
                return;
            }
        }
    }

    /// <summary>The next region of <paramref name="outer"/> as a reader of its own (no exact-length check needed).</summary>
    static SaveReader Inner(SaveReader outer, Game game)
    {
        int length = outer.ReadInt32();
        byte[] bytes = outer.ReadBytes(length);
        var stream = new MemoryStream(length + 4);
        stream.Write(BitConverter.GetBytes(outer.VersionInt));
        stream.Write(bytes);
        stream.Position = 0;
        return new SaveReader(stream, game.Data, game.Entities);
    }

    static void ReadEquipment(SaveReader reader, EquipmentComponent equipment, Dictionary<EquipmentSlot, ActiveEquipmentSlot> ours)
    {
        int slots = reader.ReadInt32();
        for (int i = 0; i < slots; i++)
        {
            if (reader.TryReadNamespacedObject(out EquipmentSlot? slot) && ours.TryGetValue(slot, out ActiveEquipmentSlot? active))
            {
                reader.ReadInRegion(active, "ActiveEquipmentSlot");
                Book(equipment, active);
            }
            else reader.SkipRegion();
        }
        int loadouts = reader.ReadInt32();
        Dictionary<LoadoutSlot, EquipmentLoadout> kept = Loadouts(equipment);
        for (int i = 0; i < loadouts; i++)
        {
            if (!reader.TryReadNamespacedObject(out LoadoutSlot? loadoutSlot) || !kept.TryGetValue(loadoutSlot, out EquipmentLoadout? loadout))
            {
                reader.SkipRegion();
                continue;
            }
            using SaveReader inner = Inner(reader, s_game!);
            int count = inner.ReadInt32();
            for (int j = 0; j < count; j++)
            {
                if (inner.TryReadNamespacedObject(out EquipmentSlot? slot) && ours.ContainsKey(slot))
                {
                    var saved = new EquipmentSlotLoadout();
                    inner.ReadInRegion(saved, "EquipmentSlotLoadout");
                    loadout.Slots[slot] = saved;
                }
                else inner.SkipRegion();
            }
        }
    }

    /// <summary>What the game does for each worn item when a save is loaded (EquipmentComponent.OnSaveLoaded, LoadBonuses).</summary>
    static void Book(EquipmentComponent equipment, ActiveEquipmentSlot active)
    {
        if (active.IsEmpty || active.IsOccupied || active.Equipped is not { } worn) return;
        ref Dictionary<Item, ActiveEquipmentSlot> equipped = ref EquippedItems(equipment);
        if (!equipped.TryAdd(worn.Item, active))
        {
            // The same ring is worn elsewhere already: the game allows one of each. The extra one goes to the bank
            // (it waits when there is no room); it is never thrown away.
            Item extra = worn.Item;
            int quantity = Math.Max(1, active.Quantity);
            active.SetEmpty();
            if (s_game != null)
                try { RareItems.BankOrWait(s_game, extra, quantity); Problems.Add(extra.Id + " was worn twice; the extra one is in your bank"); }
                catch (Exception e) { Problems.Add(extra.Id + " was worn twice and could not be put in the bank: " + e.Message); }
            return;
        }
        active.OnSaveLoaded();
        AddSetBonuses(equipment, worn.Item);
        AddModifiers(equipment, worn.Item, worn.Equipment);
        AddStats(equipment, worn.Item, worn.Equipment);
        ReadBack++;
    }

    // ---- the equipment grid ---------------------------------------------------------------------------------------

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ItemEquipping")]
    static extern ItemEquippingComponent? GridOwner(EquipmentGrid grid);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_EquipmentGridContainer")]
    static extern GridContainer GridBox(EquipmentGrid grid);

    /// <summary>An equipment grid the game drew before the slots were added is drawn again.</summary>
    public static void Regrid()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        foreach (EquipmentGrid grid in Grids(tree.Root))
        {
            try
            {
                if (GridOwner(grid) is not { } owner) continue;
                GridContainer box = GridBox(grid);
                EquipmentComponent equipment = owner.Equipment;
                if (box.GetChildCount() == equipment.ColumnCount * equipment.RowCount) continue;
                foreach (Node child in box.GetChildren()) { box.RemoveChild(child); child.QueueFree(); }
                grid.Bind(owner);
            }
            catch (Exception e) { Problems.Add("grid: " + e.Message); }
        }
    }

    public static IEnumerable<EquipmentGrid> Grids(Node node)
    {
        if (node is EquipmentGrid grid) yield return grid;
        foreach (Node child in node.GetChildren())
            foreach (EquipmentGrid g in Grids(child)) yield return g;
    }

    /// <summary>The number of filled slots in the grids on screen (tests).</summary>
    public static int GridCells()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return 0;
        int most = 0;
        foreach (EquipmentGrid grid in Grids(tree.Root))
            try { most = Math.Max(most, GridBox(grid).GetChildren().Count(c => c is EquipmentSlotView && !c.IsQueuedForDeletion())); }
            catch (Exception) { }
        return most;
    }

    // ---- before removing the mod ----------------------------------------------------------------------------------

    /// <summary>
    /// Takes the rings off Ring Slot 2 to 6 and puts them in the bank. Returns how many are in the bank now, how many are
    /// still worn (the bank had no room) and how many wait for room (rings of the loadouts that are not worn now).
    /// </summary>
    public static (int Moved, int Worn, int Waiting) ToBankCounted(Game game)
    {
        int before = RareItems.Data.NormalPending.Values.Sum() + RareItems.Data.Pending.Count;
        int moved = ToBank(game);
        int waiting = Math.Max(0, RareItems.Data.NormalPending.Values.Sum() + RareItems.Data.Pending.Count - before);
        return (moved - waiting, InAdded(game), waiting);
    }

    /// <summary>Takes the rings off Ring Slot 2 to 6 and puts them in the bank. Returns how many came off.</summary>
    public static int ToBank(Game game)
    {
        if (Combat(game) is not { } combat) return 0;
        int moved = 0;
        foreach (IActiveEquipmentSlot active in Slots(game).Skip(1))
        {
            if (active.IsEmpty) continue;
            combat.ItemEquipping.UnequipSlot(active.Slot);
            if (active.IsEmpty) moved++;
        }
        // the rings kept in Ring Slot 3 to 6 of the loadouts that are not worn now: without the mod the game drops those
        // slots, so they go to the bank as well (one that finds no room waits, as every item of One Style does)
        try
        {
            var ids = Added.Select(a => "MelvorBase:" + a.Id).ToHashSet();
            var current = combat.Loadouts.CurrentLoadout?.Data;
            foreach (var (key, loadout) in Loadouts(combat.Equipment))
            {
                if (ReferenceEquals(key, current)) continue;
                foreach (var (slot, saved) in loadout.Slots.ToList())
                {
                    if (!ids.Contains(slot.Id) || saved.IsOccupied || saved.Equipped is not { } e) continue;
                    // while rare items rest nothing can wait (One Style's part of the save is not written anew): the ring
                    // leaves its loadout only when the bank takes it
                    if (RareItems.SafeMode && !RareItems.BankHasRoomFor(game, e.Item)) continue;
                    RareItems.BankOrWait(game, e.Item, Math.Max(1, saved.Quantity));
                    RareItems.ClearLoadoutSlot(saved);
                    moved++;
                }
            }
        }
        catch (Exception e) { Problems.Add("rings of other loadouts: " + e.Message); }
        return moved;
    }

    /// <summary>
    /// The rings in Ring Slot 3 to 6, worn now and in every loadout: without the mod the game skips those slots, so
    /// One Style's part of the save lists them and the mod loader gives them back (Ring Slot 2 is the game's own and keeps its ring).
    /// </summary>
    public static List<(Item Item, int Quantity)> InAddedSlots(Game game)
    {
        var list = new List<(Item, int)>();
        if (Combat(game) is not { } combat) return list;
        var ids = Added.Select(a => "MelvorBase:" + a.Id).ToHashSet();
        foreach (IActiveEquipmentSlot active in Slots(game))
            if (ids.Contains(active.Slot.Id) && !active.IsEmpty && !active.IsOccupied && active.Equipped is { } e) list.Add((e.Item, Math.Max(1, active.Quantity)));
        foreach (EquipmentLoadout loadout in Loadouts(combat.Equipment).Values)
            foreach (var (slot, saved) in loadout.Slots)
                if (ids.Contains(slot.Id) && !saved.IsOccupied && saved.Equipped is { } e) list.Add((e.Item, Math.Max(1, saved.Quantity)));
        return list;
    }

    /// <summary>How many rings Ring Slot 2 to 6 hold now.</summary>
    public static int InAdded(Game game) => Slots(game).Skip(1).Count(s => !s.IsEmpty);
}
