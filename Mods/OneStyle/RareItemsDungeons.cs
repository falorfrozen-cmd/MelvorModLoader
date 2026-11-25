using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.CombatLoot;
using GamesByMalcs.Melvor2.Components.ItemOpening;
using GamesByMalcs.Melvor2.Data.DropTables;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using Godot;

namespace OneStyle;

/// <summary>What a dungeon gives as gear: its chest's gear with the chance of each per chest opened, its final boss.</summary>
public sealed class DungeonGear
{
    public Dungeon Dungeon = null!;
    public Item Chest = null!;
    public Enemy? Boss;
    /// <summary>The item level of its rare gear: the final boss's combat level + 5, as the boss's own rare drops.</summary>
    public int ILvl;
    public readonly List<(Item Item, double Chance)> Gear = new();
    /// <summary>How often a chest gives gear: also the share of the final boss's rare drops that are this dungeon's gear.</summary>
    public double GearChance => Math.Min(1, Gear.Sum(g => g.Chance));
    public string Name => Dungeon.DisplayName.Message;
}

/// <summary>
/// Rare versions of a dungeon's own gear, for target farming (zindan-nadir/karar.md, 2026-11-25; Codex and Gemini
/// consulted, the player said yes):
/// 1. Gear that comes out of a dungeon's chest when it is opened comes Magic 20%, Rare 8% or Rare with 4 affixes 2%
///    of the time, in place of the normal copy. Its item level: the dungeon's final boss + 5.
/// 2. The final boss's rare drops take their base from that chest's gear as often as the chest gives gear (Kraken 30%,
///    Black Dragon 1%), with the chest's own weights.
/// 3. The 40 pieces of gear that only a dungeon gives (data/dungeon-gear.json) are never in the general pool: each comes
///    rare only from its own dungeon.
/// Not touched by Unique powers that raise rare loot (one cannot change gear before opening a stack of chests), not
/// sold by Bulk Sell's auto-sell (the player opened the chest for it), nothing while rare items rest (safe mode).
/// </summary>
public static partial class RareItems
{
    public const double ChestMagic = 0.20, ChestRare = 0.08, ChestRare4 = 0.02;

    static HashSet<string> s_dungeonOnly = new();
    static Game? s_dungeonsOf;
    static Dictionary<string, DungeonGear> s_byChest = new(), s_byDungeon = new(), s_byBoss = new();

    /// <summary>For tests: rare items that came out of chests since the character was opened.</summary>
    public static int ChestRares { get; private set; }

    public static void LoadDungeonGear(string modDir)
    {
        try
        {
            string path = Path.Combine(modDir, "data", "dungeon-gear.json");
            if (!File.Exists(path)) { s_dungeonOnly = new(); return; }
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            s_dungeonOnly = doc.RootElement.EnumerateArray().Select(e => e.GetProperty("Item").GetString() ?? "").Where(s => s != "").ToHashSet();
        }
        catch (Exception e) { Log("One Style: data/dungeon-gear.json could not be read: " + e.Message); s_dungeonOnly = new(); }
    }

    /// <summary>Only a dungeon gives it: never in the general pool of rare item bases.</summary>
    public static bool IsDungeonOnly(Item item) => s_dungeonOnly.Contains(item.Id);

    /// <summary>Gear that can be a rare item's base (the general pool's rules without its price limit). A dungeon's own gear
    /// need not have a picture: the game has none yet for some (the Drowned set, Black Dragon Boots), and the normal one
    /// looks the same; the general pool leaves such gear out (an empty square dropping from any monster).</summary>
    static bool CanBeRare(Item i, bool needArt = true) =>
        !i.LocalId.StartsWith("Os") && SlotOf(i) is not ("" or "Ammo") && !i.Id.Contains("Collectable") && !i.Id.Contains("Belra")
        && !i.Id.Contains("Placeholder") && i.Id != "MelvorBase:DefaultWeapon" && (!needArt || HasArt(i));

    static bool HasArt(Item i)
    {
        // the game gives gear without a picture its "missing artwork" square
        try { return i.Icon is { } icon && !icon.ResourcePath.Contains("missing_artwork", StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    /// <summary>Every dungeon with a chest that gives gear, read once per game from the game's own data.</summary>
    public static IReadOnlyCollection<DungeonGear> Dungeons(Game game)
    {
        if (ReferenceEquals(s_dungeonsOf, game)) return s_byDungeon.Values;
        s_dungeonsOf = game;
        s_byChest = new(); s_byDungeon = new(); s_byBoss = new();
        try
        {
            foreach (Dungeon d in game.Data.GetDataRegistryOutput<Dungeon>().GetAllObjects())
            {
                var rewards = d.Rewards.FixedItems.Select(f => f.Item).Concat(d.Rewards.ItemTables.SelectMany(t => t.Table.Items.Select(w => w.Item)));
                Item? chest = rewards.FirstOrDefault(i => i.Capabilities.Contains<OpenableCapability>());
                if (chest == null || !chest.Capabilities.TryGet(out OpenableCapability? open)) continue;
                Enemy? boss = d.Enemies.Count > 0 ? d.Enemies[^1].Enemies.Items.Select(w => w.Enemy).OrderByDescending(e => e.CombatLevel).FirstOrDefault() : null;
                var dg = new DungeonGear { Dungeon = d, Chest = chest, Boss = boss, ILvl = Math.Max(1, (boss?.CombatLevel ?? 1) + 5) };
                foreach (WeightedItemDrop drop in open.DropTable.Items)
                    if (CanBeRare(drop.Item, needArt: false)) dg.Gear.Add((drop.Item, open.DropTable.GetItemChance(drop)));
                foreach (ItemQuantity always in open.AlwaysDrops)
                    if (CanBeRare(always.Item, needArt: false)) dg.Gear.Add((always.Item, 1.0));
                if (dg.Gear.Count == 0) continue;
                s_byChest[chest.Id] = dg;
                s_byDungeon[d.Id] = dg;
                if (boss != null) s_byBoss[boss.Id] = dg;
            }
            int missing = s_dungeonOnly.Count(id => !game.Data.TryGetObject(id, out Item? _));
            Log($"One Style: {s_byDungeon.Count} dungeon(s) give gear from their chest; {s_dungeonOnly.Count - missing} dungeon-only piece(s) come rare only from their dungeon"
                + (missing > 0 ? $" ({missing} of data/dungeon-gear.json are not in the game)." : "."));
        }
        catch (Exception e) { Log("One Style: the dungeons' gear could not be read: " + e.Message); }
        return s_byDungeon.Values;
    }

    public static DungeonGear? DungeonOf(Game game, string dungeonId) { Dungeons(game); return s_byDungeon.GetValueOrDefault(dungeonId); }
    public static DungeonGear? DungeonOfChest(Game game, Item chest) { Dungeons(game); return s_byChest.GetValueOrDefault(chest.Id); }
    public static DungeonGear? DungeonOfBoss(Game game, Enemy enemy) { Dungeons(game); return s_byBoss.GetValueOrDefault(enemy.Id); }

    /// <summary>The dungeon whose final boss is being fought right now (null anywhere else).</summary>
    static DungeonGear? FinalBossNow(Game game)
    {
        if (Combat(game)?.Areas is not { } areas || areas.CurrentArea?.Area is not Dungeon d || areas.CurrentProgress < areas.MaxProgress) return null;
        return DungeonOf(game, d.Id);
    }

    /// <summary>A base from the dungeon's chest gear with the chest's weights; weapons of the player's style only (null: none fits).</summary>
    public static Item? PickDungeonBase(DungeonGear dg, string style)
    {
        var fits = dg.Gear.Where(g => style == "" || SlotOf(g.Item) != "Weapon" || StyleOf(g.Item) == style).ToList();
        double total = fits.Sum(g => g.Chance);
        if (fits.Count == 0 || total <= 0) return null;
        double r = s_rng.NextDouble() * total;
        foreach (var (item, chance) in fits) { r -= chance; if (r < 0) return item; }
        return fits[^1].Item;
    }

    /// <summary>A piece of gear from a chest: Magic, Rare, Rare with 4 affixes, or null (it stays normal).</summary>
    static Rarity? ChestRarity()
    {
        double r = s_rng.NextDouble();
        return r < ChestMagic ? Rarity.Magic : r < ChestMagic + ChestRare ? Rarity.Rare : r < ChestMagic + ChestRare + ChestRare4 ? Rarity.Rare4 : null;
    }

    static ItemOpeningComponent? s_opening;

    /// <summary>Listens to the bank's chest opening (each character's own).</summary>
    static void WatchChests(Game game)
    {
        UnwatchChests();
        if (Bank(game) is not { } bank) return;
        s_opening = bank.ItemOpening;
        s_opening.ItemsOpened += OnItemsOpened;
    }

    static void UnwatchChests()
    {
        if (s_opening != null) s_opening.ItemsOpened -= OnItemsOpened;
        s_opening = null;
    }

    static readonly System.Reflection.FieldInfo? s_resultsList = typeof(System.Collections.ObjectModel.ReadOnlyCollection<ItemOpenResult>)
        .GetField("list", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>The list behind what the game will show as "what came out of the chests" (null: cannot be changed).</summary>
    static IList<ItemOpenResult>? ResultsOf(IReadOnlyList<ItemOpenResult> drops) =>
        drops is System.Collections.ObjectModel.ReadOnlyCollection<ItemOpenResult> ro && s_resultsList?.GetValue(ro) is IList<ItemOpenResult> list && !list.IsReadOnly ? list : null;

    /// <summary>
    /// Chests were opened in the bank: gear from a dungeon's chest may come rare. The normal copy leaves the bank and the
    /// rare one comes in its place (what finds no room waits, as every drop does), and the window that shows what came
    /// out of the chests shows the rare items. Main thread only (the bank's own Open button).
    /// </summary>
    static void OnItemsOpened(object? sender, ItemOpenedEventArgs e)
    {
        if (SafeMode || s_game is not { } game || System.Environment.CurrentManagedThreadId != MainThread || e.Drops.Count == 0) return;
        try
        {
            if (DungeonOfChest(game, e.Item) is not { } dg || Bank(game) is not { } bank) return;
            var gear = dg.Gear.Select(g => g.Item).ToHashSet();
            IList<ItemOpenResult>? shown = null;
            try { shown = ResultsOf(e.Drops); }
            catch (Exception) { shown = null; }   // the window then shows them normal; the bank has the rare ones all the same
            if (shown == null) Once("chest results", "One Style: the chest window's list cannot be changed: it shows rare gear as normal (the bank has the rare items).");
            int made = 0;
            string style = DropStyle();
            foreach (ItemOpenResult result in e.Drops.ToList())
            {
                if (!gear.Contains(result.Item)) continue;
                var rares = new List<Item>();
                for (int i = 0; i < result.Quantity; i++)
                {
                    if (ChestRarity() is not { } rarity) continue;
                    // the normal copy leaves first: never both
                    if (bank.Inventory.RemoveQuantity(result.Item, 1) <= 0) break;
                    int iLvl = Math.Max(dg.ILvl, PoolLevelOf(result.Item));
                    Item? rare = Give(game, Roll(game, result.Item, rarity, iLvl, style), newDrop: false);
                    if (rare == null) { bank.Inventory.AddQuantity(result.Item, 1, GamesByMalcs.Melvor2.Components.Inventories.AddItemFlags.None); break; }
                    rares.Add(rare);
                }
                if (rares.Count == 0) continue;
                made += rares.Count;
                if (shown == null) continue;
                int at = shown.IndexOf(result);
                if (at < 0) continue;
                int left = result.Quantity - rares.Count;
                if (left > 0) shown[at] = new ItemOpenResult(result.Item, left, result.Chance, result.NewItem, result.Inventory);
                else shown.RemoveAt(at);
                foreach (Item rare in rares) shown.Insert(Math.Min(at, shown.Count), new ItemOpenResult(rare, 1, result.Chance, true, result.Inventory));
            }
            if (made == 0) return;
            ChestRares += made;
            Log($"One Style: {made} piece(s) of gear from {e.Count} x {e.Item.DisplayName.Singular} came rare.");
        }
        catch (Exception ex) { Log("One Style: chest gear could not be made rare: " + ex.Message); }
    }

    // ================================================================ in the game's windows

    const string DungeonNoteName = "OneStyleDungeonNote";

    static string Pct(double p) => FormattableString.Invariant($"{p * 100:0.#}%");

    /// <summary>The two lines a dungeon's rewards and its chest show: what its gear can come as, and its final boss.</summary>
    public static (string Chest, string Boss) DungeonLines(DungeonGear dg) =>
        ($"Gear from the {dg.Chest.DisplayName.Singular} can come rare: Magic {Pct(ChestMagic)} · Rare {Pct(ChestRare)} · Rare (4 affixes) {Pct(ChestRare4)}, item level {dg.ILvl}",
         $"{(dg.Boss != null ? dg.Boss.DisplayName.Message : "The final boss")}: {Pct(dg.GearChance)} of its rare drops are this dungeon's gear");

    static Control NoteBox(DungeonGear dg, bool boss = true)
    {
        var box = new VBoxContainer { Name = DungeonNoteName, MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeConstantOverride("separation", 2);
        var (chest, bossLine) = DungeonLines(dg);
        var a = new Label { Text = chest, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        a.AddThemeFontSizeOverride("font_size", 13);
        a.AddThemeColorOverride("font_color", Colors[Rarity.Rare]);
        box.AddChild(a);
        if (boss)
        {
            var (champion, unique) = Elites.DungeonRewardTimes;
            bossLine += $"  ·  as a Champion the reward comes x{champion}, as a Unique x{unique}";
            var b = new Label { Text = bossLine, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.AddThemeColorOverride("font_color", new Color(0.62f, 0.64f, 0.68f));
            box.AddChild(b);
        }
        return box;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_DungeonName")]
    static extern Label DungeonNameOf(ViewDungeonRewardsModal modal);

    /// <summary>A dungeon's "View Rewards" window: two short lines (the window cannot scroll), none for a dungeon without gear.</summary>
    static void DungeonNote(CombatDropsContainer drops, ViewDungeonRewardsModal modal)
    {
        if (s_game is not { } game) return;
        VBoxContainer tables = TablesOf(drops);
        Node? have = tables.GetNodeOrNull(DungeonNoteName);
        string name = DungeonNameOf(modal).Text;
        DungeonGear? dg = Dungeons(game).FirstOrDefault(d => d.Name == name);
        string key = dg?.Dungeon.Id ?? "";
        if (have != null && have.GetMeta("key", "").AsString() == key && !have.IsQueuedForDeletion()) return;
        if (have != null) { tables.RemoveChild(have); have.QueueFree(); }
        if (dg == null) return;
        Control box = NoteBox(dg);
        box.SetMeta("key", key);
        tables.AddChild(box);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ItemName")]
    static extern Label ChestNameOf(OpenableItemDropsView view);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_PossibleDropsContainer")]
    static extern VBoxContainer ChestDropsOf(OpenableItemDropsView view);

    /// <summary>A chest's list of what it gives (the bank's Open panel): one line when it is a dungeon's chest.</summary>
    static void ChestNote(OpenableItemDropsView view)
    {
        if (s_game is not { } game) return;
        VBoxContainer list = ChestDropsOf(view);
        Node? have = list.GetNodeOrNull(DungeonNoteName);
        string name = ChestNameOf(view).Text;
        DungeonGear? dg = Dungeons(game).FirstOrDefault(d => d.Chest.DisplayName.Singular == name);
        string key = dg?.Chest.Id ?? "";
        if (have != null && have.GetMeta("key", "").AsString() == key && !have.IsQueuedForDeletion()) return;
        if (have != null) { list.RemoveChild(have); have.QueueFree(); }
        if (dg == null) return;
        Control box = NoteBox(dg, boss: false);
        box.SetMeta("key", key);
        list.AddChild(box);
        list.MoveChild(box, 0);
    }
}
