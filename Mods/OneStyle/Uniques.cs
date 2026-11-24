using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Modifiers;
using Godot;

namespace OneStyle;

/// <summary>One of the 58 unique items (data/uniques.json, made by Dev/OneStyleTools/uniquegen.py).</summary>
public sealed class UniqueDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slot { get; set; } = "";
    public string Style { get; set; } = "";
    public int Level { get; set; }
    /// <summary>The monster whose Unique drops it.</summary>
    public string Source { get; set; } = "";
    public string SourceName { get; set; } = "";
    /// <summary>The game item it is built on (its slot, requirements and own stats; given back when One Style is gone).</summary>
    public string BaseId { get; set; } = "";
    public List<UniqueStat> Stats { get; set; } = new();
    public string MechanicName { get; set; } = "";
    public string Mechanic { get; set; } = "";
    public string Flavour { get; set; } = "";
    public string Icon { get; set; } = "";
}

public sealed class UniqueStat
{
    public string Text { get; set; } = "";
    public JsonElement Bonus { get; set; }
}

/// <summary>
/// The 58 unique items: each one drops from one Unique monster, 15% a Unique kill (no first-kill guarantee, no bad-luck
/// rise: the user wants them farmed). A unique item is one item of the game (always the same), so a second
/// copy stacks with the first.
/// </summary>
public static class Uniques
{
    public static readonly Dictionary<string, UniqueDef> Defs = new();
    static readonly Dictionary<string, Texture2D?> s_icons = new();

    public static void Load(string modDir)
    {
        try
        {
            var list = JsonSerializer.Deserialize<List<UniqueDef>>(File.ReadAllText(Path.Combine(modDir, "data", "uniques.json"))) ?? new();
            Defs.Clear();
            foreach (UniqueDef u in list) Defs[u.Id] = u;
        }
        catch (Exception e) { RareItems.Log("data/uniques.json could not be read: unique items are off. " + e.Message); }
    }

    public static UniqueDef? ForMonster(string enemyId) => Defs.Values.FirstOrDefault(u => u.Source == enemyId);
    public static string BaseOf(string uniqueId) => Defs.TryGetValue(uniqueId, out UniqueDef? u) ? u.BaseId : "";

    public static Texture2D? IconOf(UniqueDef def)
    {
        string path = Path.Combine(RareItems.Dir, def.Icon.Replace('/', Path.DirectorySeparatorChar));
        if (s_icons.TryGetValue(path, out Texture2D? t)) return t;
        try { t = File.Exists(path) ? ImageTexture.CreateFromImage(Image.LoadFromFile(path)) : null; } catch { t = null; }
        return s_icons[path] = t;
    }

    /// <summary>The game item of a unique (made once per opened character).</summary>
    public static Item? Make(Game game, RolledItem record)
    {
        if (!Defs.TryGetValue(record.UniqueId, out UniqueDef? def)) return null;
        if (!game.Data.TryGetObject(def.BaseId, out Item? baseItem) || !baseItem.Capabilities.TryGet(out EquipmentCapability? cap)) return null;
        var global = new List<GlobalModifierValue>(cap.Bonuses?.Global ?? (IEnumerable<GlobalModifierValue>)Array.Empty<GlobalModifierValue>());
        var component = new List<ComponentModifierValue>(cap.Bonuses?.Component ?? (IEnumerable<ComponentModifierValue>)Array.Empty<ComponentModifierValue>());
        var effects = new List<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>(cap.Bonuses?.CombatEffect
            ?? (IEnumerable<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>)Array.Empty<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>());
        foreach (UniqueStat s in def.Stats) component.Add(new ComponentModifierValue(game.Data, s.Bonus.Clone()));
        var registry = (DataRegistry<Item>)game.Data.GetDataRegistryOutput<Item>();
        return RareItems.Register(game, registry, record, baseItem, cap, new ModifierValues(global, component, effects),
            IconOf(def) ?? baseItem.Icon, Math.Max(baseItem.SellsFor.Quantity * 5, 500));
    }

    /// <summary>The record of a unique item (its local id is the unique's id: every copy is the same item).</summary>
    public static RolledItem RecordOf(UniqueDef def) => new()
    {
        LocalId = def.Id, BaseId = def.BaseId, Rarity = Rarity.Unique, Name = def.Name, ILvl = def.Level, UniqueId = def.Id,
    };

    /// <summary>The chance of a monster's unique item on each of its Unique kills.</summary>
    public const double Chance = 0.15;

    /// <summary>For tests: the next Unique kill drops its item.</summary>
    public static bool ForceNext;

    /// <summary>A Unique monster died: its unique item drops 15% of the time.</summary>
    public static void OnUniqueKilled(Game game, Enemy enemy)
    {
        if (ForMonster(enemy.Id) is not { } def) return;
        SavedData data = RareItems.Data;
        bool forced = ForceNext;
        ForceNext = false;
        if (!forced && Random.Shared.NextDouble() >= Chance)
        {
            data.UniqueMisses[def.Id] = data.UniqueMisses.GetValueOrDefault(def.Id) + 1;
            return;
        }
        data.UniqueMisses[def.Id] = 0;
        if (!data.UniquesFound.Contains(def.Id)) data.UniquesFound.Add(def.Id);
        RolledItem record = data.Items.FirstOrDefault(r => r.LocalId == def.Id) ?? RecordOf(def);
        if (!data.Items.Contains(record)) data.Items.Add(record);
        Item? item = RareItems.Make(game, record);
        if (item == null) return;
        RareItems.LastDrops.Insert(0, record);
        if (game.Entities.TryGetObjectById("MelvorBase:Bank", out var e) && e is GamesByMalcs.Melvor2.Entities.Bank.BankEntity bank
            && bank.Inventory.AddQuantity(item, 1, GamesByMalcs.Melvor2.Components.Inventories.AddItemFlags.None) > 0) { }
        else data.Pending.Add(record.LocalId);
        RareItems.DropToast(record);
    }

    /// <summary>The chance of a monster's unique item on its next Unique kill (always the same).</summary>
    public static double ChanceNow(UniqueDef def) => Chance;
}
