using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Experience;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.DataStructures;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Localization;
using GamesByMalcs.Melvor2.Modifiers;

namespace OneStyle;

public enum Rarity { Normal, Magic, Rare, Rare4, Unique }

/// <summary>One affix as written in data/affixes.json (made by scratchpad tools/affixgen.py).</summary>
public sealed class AffixDef
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";      // P prefix, S suffix
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";      // "+{v}% max hit"
    public double[] Tiers { get; set; } = Array.Empty<double>();   // T5 .. T1
    public List<string> Slots { get; set; } = new();
    public double Scale { get; set; } = 0.01;
    public JsonElement Bonuses { get; set; }
    public string Custom { get; set; } = "";
    public string Group { get; set; } = "";
    public bool Prefix => Kind == "P";

    /// <summary>Whole numbers when every tier value is one, else tenths.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public double Step => Tiers.All(v => v == Math.Floor(v)) ? 1 : 0.1;

    (double Min, double Max)[]? _ranges;

    /// <summary>
    /// The value range of a tier (1 = best). The ranges follow each other and never overlap, as in other hack and slash
    /// games: a tier starts halfway between its value and the next lower tier's, and ends just before the next higher
    /// tier starts. The lowest starts 15% under its value, the highest ends 15% over. Tiers with the same value
    /// (Warded: 1, 1, 2, 2, 3) give exactly that value.
    /// </summary>
    public (double Min, double Max) Range(int tier)
    {
        _ranges ??= Ranges();
        return _ranges[Math.Clamp(5 - tier, 0, _ranges.Length - 1)];
    }

    (double Min, double Max)[] Ranges()
    {
        double step = Step;
        double Snap(double v, Func<double, double> how) => Math.Round(how(Math.Round(v / step, 6)) * step, 1);
        int n = Tiers.Length;
        var lo = new double[n];
        var hi = new double[n];
        for (int i = 0; i < n; i++)
        {
            bool same = (i > 0 && Tiers[i - 1] == Tiers[i]) || (i < n - 1 && Tiers[i + 1] == Tiers[i]);
            lo[i] = same ? Tiers[i] : i == 0 ? Snap(Tiers[0] * 0.85, x => Math.Round(x, MidpointRounding.AwayFromZero)) : Snap((Tiers[i - 1] + Tiers[i]) / 2, Math.Ceiling);
            hi[i] = same ? Tiers[i] : i == n - 1 ? Snap(Tiers[i] * 1.15, x => Math.Round(x, MidpointRounding.AwayFromZero)) : Snap((Tiers[i] + Tiers[i + 1]) / 2, Math.Ceiling) - step;
            lo[i] = Math.Round(Math.Min(lo[i], Tiers[i]), 1);
            hi[i] = Math.Round(Math.Max(hi[i], Tiers[i]), 1);
        }
        return Enumerable.Range(0, n).Select(i => (lo[i], hi[i])).ToArray();
    }

    public static double Round(double v) => Math.Abs(v) >= 10 ? Math.Round(v) : Math.Round(v, 1);
}

/// <summary>An affix on one item: which, its tier (1 best .. 5) and the rolled value as the player reads it.</summary>
public sealed class RolledAffix
{
    public string Id { get; set; } = "";
    public int Tier { get; set; }
    public double Value { get; set; }
    public string Style { get; set; } = "";
}

/// <summary>One dropped item: kept in the save (One Style's own part of it), made into a game item when a character opens.</summary>
public sealed class RolledItem
{
    public string LocalId { get; set; } = "";
    public string BaseId { get; set; } = "";
    public Rarity Rarity { get; set; }
    public string Name { get; set; } = "";
    public int ILvl { get; set; }
    public List<RolledAffix> Affixes { get; set; } = new();
    /// <summary>For a Unique item: its id in data/uniques.json.</summary>
    public string UniqueId { get; set; } = "";
    public long Seen { get; set; }
    /// <summary>New values paid for with Ember and not chosen yet (null: none).</summary>
    public List<double>? Reroll { get; set; }
}

/// <summary>
/// Rare items, as in other action RPGs: a monster drops a real item of the game (a "Bronze Sword") as a Magic, Rare or
/// Rare-4 version with random affixes. Each dropped item is an item of its own in the game (its own id, name, colour and
/// bonuses); the save keeps it under that id, and One Style's own part of the save keeps what it is
/// (OneStyleData). Without One Style the game skips those ids; the mod loader then gives the normal base items back.
/// </summary>
public static partial class RareItems
{
    public static Action<string> Log = _ => { };
    public static string Dir = "";
    public static readonly Dictionary<string, AffixDef> Affixes = new();
    static readonly Random s_rng = new();

    public static readonly Dictionary<Rarity, Godot.Color> Colors = new()
    {
        [Rarity.Normal] = new Godot.Color(1, 1, 1),
        [Rarity.Magic] = new Godot.Color(0.45f, 0.66f, 1f),       // blue (light enough to read on the dark panels)
        [Rarity.Rare] = new Godot.Color(1f, 0.85f, 0.29f),        // yellow
        [Rarity.Rare4] = new Godot.Color(1f, 0.85f, 0.29f),
        [Rarity.Unique] = new Godot.Color(0.92f, 0.52f, 0.22f),   // a unique orange, for names (#EA8538: readable on the dark panels)
    };

    public static string RarityName(Rarity r) => r switch
    {
        Rarity.Magic => "Magic", Rarity.Rare => "Rare", Rarity.Rare4 => "Rare", Rarity.Unique => "Unique", _ => "Normal",
    };

    public static void LoadAffixes(string modDir)
    {
        try
        {
            string path = Path.Combine(modDir, "data", "affixes.json");
            var list = JsonSerializer.Deserialize<List<AffixDef>>(File.ReadAllText(path)) ?? new();
            Affixes.Clear();
            foreach (AffixDef a in list) Affixes[a.Id] = a;
        }
        catch (Exception e) { Log("data/affixes.json could not be read: rare items are off. " + e.Message); }
        try
        {
            // the level of each piece of gear's easiest source, where it is above its own requirement (Dev/OneStyleTools/baselevels.py)
            string levels = Path.Combine(modDir, "data", "base-levels.json");
            s_sourceLevels = File.Exists(levels) ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(levels)) ?? new() : new();
        }
        catch (Exception e) { Log("data/base-levels.json could not be read: " + e.Message); s_sourceLevels = new(); }
    }

    static Dictionary<string, int> s_sourceLevels = new();

    /// <summary>
    /// The level a piece of gear counts as in a monster's rare drop pool: its requirement, or the level of its easiest source
    /// when that is higher (Hypnotic Amulet asks for nothing but comes from a level 75 monster).
    /// </summary>
    public static int PoolLevelOf(Item item) => Math.Max(LevelOf(item), s_sourceLevels.GetValueOrDefault(item.Id));

    // ================================================================ the game's items as bases

    static readonly Dictionary<string, string> s_slotOfType = new()
    {
        ["MelvorBase:Weapon"] = "Weapon", ["MelvorBase:Offhand"] = "Offhand", ["MelvorBase:Helmet"] = "Helmet", ["MelvorBase:Torso"] = "Torso",
        ["MelvorBase:Legs"] = "Legs", ["MelvorBase:Gloves"] = "Gloves", ["MelvorBase:Boots"] = "Boots", ["MelvorBase:Cape"] = "Cape",
        ["MelvorBase:Amulet"] = "Amulet", ["MelvorBase:Ring"] = "Ring", ["MelvorBase:Ammo"] = "Ammo",
    };

    public static string SlotOf(Item item) =>
        item.Capabilities.TryGet(out EquipmentCapability? e) && s_slotOfType.TryGetValue(e.Type.Id, out string? s) ? s : "";

    /// <summary>The level an item asks for (its highest level requirement; 1 when none).</summary>
    public static int LevelOf(Item item)
    {
        if (!item.Capabilities.TryGet(out EquipmentCapability? e)) return 1;
        int best = 1;
        foreach (var c in e.Requirements.Conditions)
            if (c is LevelCondition l) best = Math.Max(best, l.Level);
        return best;
    }

    /// <summary>The combat style of a weapon ("" for other gear).</summary>
    public static string StyleOf(Item item) =>
        item.Capabilities.TryGet(out WeaponCapability? w) && w.Type.Stances.Count > 0 ? w.Type.Stances[0].CombatStyle.Id : "";

    static List<Item>? s_bases;

    /// <summary>
    /// Items that can come as rare items: every piece of gear the game has, except quest pieces, collectables,
    /// the Belra rings, very costly items and our own items. Never ammo: it is used up when it is shot.
    /// </summary>
    public static List<Item> Bases(Game game)
    {
        if (s_bases != null) return s_bases;
        s_bases = game.Data.GetDataRegistryOutput<Item>().GetAllObjects()
            .Where(i => !i.LocalId.StartsWith("Os") && SlotOf(i) is not ("" or "Ammo") && !i.Id.Contains("Collectable") && !i.Id.Contains("Belra")
                        && i.SellsFor.Quantity <= 25_000 && !i.Id.Contains("Placeholder") && i.Id != "MelvorBase:DefaultWeapon")   // not bare fists
            .ToList();
        return s_bases;
    }

    public static void Forget() => s_bases = null;

    /// <summary>A base for an item level: gear asking for a level in [iLvl-20, iLvl+5], else the nearest lower one.</summary>
    public static Item? PickBase(Game game, int iLvl, string style)
    {
        // every slot equally likely, then an item of that slot
        var slots = Window(game, iLvl, style).GroupBy(SlotOf).ToList();
        if (slots.Count == 0) return null;
        var group = slots[s_rng.Next(slots.Count)].ToList();
        return group[s_rng.Next(group.Count)];
    }

    /// <summary>The items a rare drop can be made from at an item level: the drop lists show them, PickBase picks one.</summary>
    public static List<Item> Window(Game game, int iLvl, string style)
    {
        List<Item> all = Bases(game);
        if (style != "") all = all.Where(i => SlotOf(i) != "Weapon" || StyleOf(i) == style).ToList();
        var window = all.Where(i => PoolLevelOf(i) >= iLvl - 20 && PoolLevelOf(i) <= iLvl + 5).ToList();
        if (window.Count == 0)
        {
            int below = all.Where(i => PoolLevelOf(i) <= iLvl).Select(PoolLevelOf).DefaultIfEmpty(1).Max();
            window = all.Where(i => PoolLevelOf(i) == below).ToList();
        }
        return window;
    }

    /// <summary>Each possible item with its share of a rare drop (as PickBase picks), by slot then level.</summary>
    public static List<(Item Item, double Share)> BaseShares(Game game, int iLvl, string style)
    {
        var slots = Window(game, iLvl, style).GroupBy(SlotOf).ToList();
        return slots.SelectMany(g => g.Select(i => (Item: i, Share: 1.0 / slots.Count / g.Count())))
            .OrderBy(x => SlotOf(x.Item)).ThenBy(x => PoolLevelOf(x.Item)).ThenBy(x => x.Item.DisplayName.Singular).ToList();
    }

    /// <summary>The player's class style for the drops ("" with no class: weapons of every style).</summary>
    public static string DropStyle() => Patches.Current != Class.None ? Classes.StyleId(Patches.Current) : "";

    // ================================================================ rolling

    /// <summary>The best tier an item level can roll: T5 always, T4 from 15, T3 from 35, T2 from 60, T1 from 85.</summary>
    public static int BestTier(int iLvl) => iLvl >= 85 ? 1 : iLvl >= 60 ? 2 : iLvl >= 35 ? 3 : iLvl >= 15 ? 4 : 5;

    public static RolledItem Roll(Game game, Item baseItem, Rarity rarity, int iLvl, string style)
    {
        string slot = SlotOf(baseItem);
        int count = rarity switch { Rarity.Magic => 2, Rarity.Rare => 3, Rarity.Rare4 => 4, _ => 0 };
        var pool = Affixes.Values.Where(a => a.Slots.Contains(slot)).ToList();
        var chosen = new List<RolledAffix>();
        int prefixes = 0, suffixes = 0;
        var groups = new HashSet<string>();
        for (int guard = 0; chosen.Count < count && guard < 200; guard++)
        {
            AffixDef a = pool[s_rng.Next(pool.Count)];
            if (chosen.Any(c => c.Id == a.Id)) continue;
            if (a.Group != "" && groups.Contains(a.Group)) continue;     // one on-hit DoT, one on-hit stun per item
            if (a.Prefix ? prefixes >= 2 : suffixes >= 2) continue;
            if (rarity == Rarity.Magic && (a.Prefix ? prefixes >= 1 : suffixes >= 1)
                && pool.Any(p => p.Prefix != a.Prefix && chosen.All(c => c.Id != p.Id))) continue;   // Magic: one of each when it can
            int best = BestTier(iLvl);
            double r = s_rng.NextDouble();
            int tier = Math.Min(5, best + (r < 0.50 ? 0 : r < 0.85 ? 1 : 2));
            var (min, max) = a.Range(tier);
            double value = Math.Round(min + a.Step * s_rng.Next(0, (int)Math.Round((max - min) / a.Step) + 1), 1);
            string affixStyle = a.Id == "Mastery" ? (style != "" ? style : StyleOf(baseItem) is { Length: > 0 } s ? s
                : new[] { "MelvorBase:Melee", "MelvorBase:Ranged", "MelvorBase:Magic" }[s_rng.Next(3)]) : "";
            chosen.Add(new RolledAffix { Id = a.Id, Tier = tier, Value = value, Style = affixStyle });
            if (a.Prefix) prefixes++; else suffixes++;
            if (a.Group != "") groups.Add(a.Group);
        }
        chosen = chosen.OrderBy(c => Affixes[c.Id].Prefix ? 0 : 1).ToList();
        var item = new RolledItem
        {
            LocalId = "OsR" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            BaseId = baseItem.Id, Rarity = rarity, ILvl = iLvl, Affixes = chosen,
        };
        item.Name = NameFor(item, baseItem);
        return item;
    }

    static readonly string[] s_first = { "Grim", "Storm", "Blood", "Doom", "Raven", "Ash", "Iron", "Dread", "Wraith", "Ember", "Frost", "Gloom",
        "Rune", "Bone", "Spirit", "Viper", "Dusk", "Thunder", "Shadow", "Plague", "Soul", "Golem", "Wolf", "Hollow" };
    static readonly string[] s_second = { "Bite", "Song", "Ward", "Fang", "Grasp", "Mark", "Veil", "Thirst", "Wrap", "Coil", "Scar", "Brand",
        "Crest", "Whisper", "Call", "Spire", "Guard", "Edge", "Heart", "Shroud", "Knot", "Tooth", "Howl", "Gaze" };

    /// <summary>Magic: "Keen Bronze Sword of the Fox"; Rare: a name of its own ("Grim Fang").</summary>
    static string NameFor(RolledItem item, Item baseItem)
    {
        string baseName = baseItem.DisplayName.Singular;
        if (item.Rarity == Rarity.Magic)
        {
            string pre = item.Affixes.Where(a => Affixes[a.Id].Prefix).Select(a => Affixes[a.Id].Name).FirstOrDefault() ?? "";
            string suf = item.Affixes.Where(a => !Affixes[a.Id].Prefix).Select(a => Affixes[a.Id].Name).FirstOrDefault() ?? "";
            return $"{pre} {baseName} {suf}".Trim();
        }
        return $"{s_first[s_rng.Next(s_first.Length)]} {s_second[s_rng.Next(s_second.Length)]}";
    }

    // ================================================================ making the game item

    static readonly MethodInfo s_add = typeof(TypeDictionary<ItemCapability>).GetMethod("Add")!;

    /// <summary>The affixes as the game's own bonuses (values and style put into each affix's template).</summary>
    public static ModifierValues AffixBonuses(Game game, IEnumerable<RolledAffix> affixes, ModifierValues? baseBonuses)
    {
        var global = new List<GlobalModifierValue>(baseBonuses?.Global ?? (IEnumerable<GlobalModifierValue>)Array.Empty<GlobalModifierValue>());
        var component = new List<ComponentModifierValue>(baseBonuses?.Component ?? (IEnumerable<ComponentModifierValue>)Array.Empty<ComponentModifierValue>());
        var effects = new List<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>(
            baseBonuses?.CombatEffect ?? (IEnumerable<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>)Array.Empty<GamesByMalcs.Melvor2.Components.CombatEffects.CombatEffectModifierValue>());
        foreach (RolledAffix r in affixes)
        {
            if (!Affixes.TryGetValue(r.Id, out AffixDef? a) || a.Custom != "") continue;
            string json = a.Bonuses.GetRawText()
                .Replace("\"{v}\"", (r.Value * a.Scale).ToString("R", CultureInfo.InvariantCulture))
                .Replace("{style}", r.Style == "" ? "MelvorBase:Melee" : r.Style);
            using JsonDocument doc = JsonDocument.Parse(json);
            var values = new ModifierValues(game.Data, doc.RootElement.Clone());
            global.AddRange(values.Global);
            component.AddRange(values.Component);
            effects.AddRange(values.CombatEffect);
        }
        return new ModifierValues(global, component, effects);
    }

    /// <summary>The game item for a record, made and registered once per opened character.</summary>
    public static Item? Make(Game game, RolledItem record)
    {
        var registry = (DataRegistry<Item>)game.Data.GetDataRegistryOutput<Item>();
        if (registry.TryGetObjectById("MelvorBase:" + record.LocalId, out Item? made)) return made;
        if (record.UniqueId != "") return Uniques.Make(game, record);
        if (!game.Data.TryGetObject(record.BaseId, out Item? baseItem) || !baseItem.Capabilities.TryGet(out EquipmentCapability? cap)) return null;
        ModifierValues bonuses = AffixBonuses(game, record.Affixes, cap.Bonuses);
        return Register(game, registry, record, baseItem, cap, bonuses, baseItem.Icon, baseItem.SellsFor.Quantity * (record.Rarity switch
        {
            Rarity.Magic => 1.5, Rarity.Rare => 2.5, Rarity.Rare4 => 4, _ => 1,
        }));
    }

    internal static Item Register(Game game, DataRegistry<Item> registry, RolledItem record, Item baseItem, EquipmentCapability cap,
        ModifierValues bonuses, Godot.Texture2D icon, double price)
    {
        // never used up: a rare or unique item made from ammo or a thrown weapon would be gone after its first attack
        var equipment = new EquipmentCapability(cap.Type, cap.OccupiesSlots?.ToList(), RequirementsFor(game, record, baseItem, cap), cap.Stats, bonuses, cap.CanEquipQuantity,
            cap.ConsumesOn == null ? null : new List<GamesByMalcs.Melvor2.Triggers.Trigger>(), cap.Attacks?.ToList(), cap.AmmoType);
        var caps = new TypeDictionary<ItemCapability>();
        foreach (ItemCapability c in baseItem.Capabilities)
        {
            if (c is EquipmentCapability) continue;
            s_add.MakeGenericMethod(c.GetType()).Invoke(caps, new object[] { c });
        }
        caps.Add(equipment);
        var item = new Item(new PluralString(record.Name, record.Name), new LocalizedString(Describe(record)), icon,
            new CurrencyQuantity(baseItem.SellsFor.Currency, (long)Math.Round(price)), caps, BankTagsFor(game),
            ignoreCompletion: true, baseItem.Namespace, record.LocalId);
        registry.RegisterObject(item);
        return item;
    }

    /// <summary>The affix lines as text; with tiers when <paramref name="tiers"/> (the Alt key).</summary>
    public static string Describe(RolledItem record, bool tiers = false)
    {
        var lines = new List<string>();
        foreach (RolledAffix r in record.Affixes)
            if (Affixes.TryGetValue(r.Id, out AffixDef? a)) lines.Add(AffixLine(a, r, tiers));
        return string.Join("\n", lines);
    }

    public static string AffixLine(AffixDef a, RolledAffix r, bool tiers)
    {
        string style = r.Style switch { "MelvorBase:Melee" => "melee", "MelvorBase:Ranged" => "ranged", "MelvorBase:Magic" => "magic", _ => "" };
        string text = a.Text.Replace("{v}", r.Value.ToString("0.#", CultureInfo.InvariantCulture)).Replace("{style}", style);
        if (!tiers) return text;
        var (min, max) = a.Range(r.Tier);
        string range = min == max ? min.ToString("0.#", CultureInfo.InvariantCulture)
            : $"{min.ToString("0.#", CultureInfo.InvariantCulture)}–{max.ToString("0.#", CultureInfo.InvariantCulture)}";
        return $"{text}   [T{r.Tier} {(a.Prefix ? "prefix" : "suffix")} \"{a.Name}\": {range}]";
    }
}
