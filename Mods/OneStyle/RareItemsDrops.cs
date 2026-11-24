using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.Components.CombatLoot;
using GamesByMalcs.Melvor2.Components.ItemOpening;
using GamesByMalcs.Melvor2.Data.DropTables;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Random;
using Godot;

namespace OneStyle;

/// <summary>
/// The game's own drop list of a monster (the chest button in its row) gets One Style's part at the bottom: the rare
/// item chances of the normal monster, its Champion and its Unique, and the Unique's own item as a real row of the
/// game's drop table (icon, tooltip with its stats and power, the chance per Unique kill).
/// </summary>
public static partial class RareItems
{
    const string DropsName = "OneStyleDrops";

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_EnemyName")]
    static extern Label EnemyNameOf(ViewEnemyDropsModal modal);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_EnemyIcon")]
    static extern TextureRect EnemyIconOf(ViewEnemyDropsModal modal);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Drops")]
    static extern CombatDropsContainer DropsOf(ViewEnemyDropsModal modal);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_DropTablesContainer")]
    static extern VBoxContainer TablesOf(CombatDropsContainer drops);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_DropTableChanceContainerScene")]
    static extern PackedScene TableSceneOf(CombatDropsContainer drops);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Title")]
    static extern Label TitleOf(ItemDropTableChanceContainer container);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ItemName")]
    static extern Label DropNameOf(ItemDropView view);

    /// <summary>For tests: the text of One Style's part of the drop list last built.</summary>
    public static string DropListText { get; private set; } = "";

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Enemy")]
    static extern Enemy? EnemyOfStats(GamesByMalcs.Melvor2.Entities.Combat.ViewEnemyStatsPanel panel);

    static Enemy? EnemyOfModal(ViewEnemyDropsModal modal)
    {
        if (s_game == null) return null;
        Texture2D? icon = EnemyIconOf(modal).Texture;
        string name = EnemyNameOf(modal).Text;
        IEnumerable<Enemy> all = s_game.Data.GetDataRegistryOutput<Enemy>().GetAllObjects();
        return all.FirstOrDefault(e => icon != null && ReferenceEquals(e.Sprite, icon)) ?? all.FirstOrDefault(e => e.DisplayName.Message == name);
    }

    /// <summary>
    /// Called by the frame walk for every drop list on screen: the drops window of a monster's row, and the drops tab
    /// (chest) of the fight screen, which shows the monster being fought. Adds One Style's part once per monster shown.
    /// </summary>
    static void DropList(CombatDropsContainer drops)
    {
        if (s_game is not { } game) return;
        Enemy? enemy = null;
        for (Node? n = drops.GetParent(); n != null && enemy == null; n = n.GetParent())
        {
            if (n is ViewEnemyDropsModal modal) { enemy = EnemyOfModal(modal); break; }
            if (n is GamesByMalcs.Melvor2.Entities.Combat.ViewEnemyStatsPanel stats) { enemy = EnemyOfStats(stats); break; }
            if (n is GamesByMalcs.Melvor2.Entities.Combat.CombatActiveView) { enemy = Combat(game)?.Enemy.Enemy; break; }
        }
        if (enemy == null) return;
        VBoxContainer tables = TablesOf(drops);
        Node? have = tables.GetNodeOrNull(DropsName);
        string key = enemy.Id + "|" + DropStyle() + "|" + PreviewItemLevel(enemy);
        if (have != null && !have.IsQueuedForDeletion() && have.GetMeta("enemy", "").AsString() == key) return;
        if (have != null) { tables.RemoveChild(have); have.QueueFree(); }

        var box = new VBoxContainer { Name = DropsName };
        box.SetMeta("enemy", key);
        box.AddThemeConstantOverride("separation", 4);
        var text = new List<string>();
        void Line(string s, Color c, int size = 13)
        {
            var l = new Label { Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(320, 0) };
            l.AddThemeFontSizeOverride("font_size", size);
            l.AddThemeColorOverride("font_color", c);
            box.AddChild(l);
            text.Add(s);
        }
        box.AddChild(new HSeparator());
        Line("One Style: rare items", new Color(1f, 0.85f, 0.45f), 15);
        bool elite = Elites.InfoOf(enemy.Id) != null;
        int iLvl = PreviewItemLevel(enemy);
        string poolLine = PoolLine(iLvl);
        foreach (var (s, c, _) in DropLines(enemy, EliteTier.Normal)) Line(s == poolLine ? PoolLine(iLvl, inDrops: true) : s, c);
        if (elite)
        {
            foreach (var (s, c, _) in DropLines(enemy, EliteTier.Champion)) Line("As a Champion: " + s, c);
            Line("As a Unique: 2 items: 1 Rare (80% with 3 affixes, 20% with 4), plus 1 with a Champion's chances.", Colors[Rarity.Rare]);
        }
        // the game items a rare drop can be made from, as the game's own rows (icon, tooltip, chance of each)
        var shares = BaseShares(game, iLvl, DropStyle());
        if (shares.Count > 0)
        {
            var slots = shares.GroupBy(x => SlotOf(x.Item)).ToList();
            Line($"The list shows base items only: each drops as a Magic or Rare item with its own name and needs level "
                 + $"{Math.Clamp((int)Math.Round(iLvl * 0.8), 1, 99)}+ to wear. The percentage next to an item is the chance that a dropped "
                 + "rare item uses that base, not a chance per kill.", new Color(0.85f, 0.85f, 0.85f));
            // the list is long: closed until asked for, one part per slot
            var list = new VBoxContainer { Visible = false };
            list.AddThemeConstantOverride("separation", 2);
            string rest = $"the {shares.Count} possible items ({slots.Count} slots)";
            var toggle = new Button { Text = "▸  Show " + rest, Alignment = HorizontalAlignment.Left };
            toggle.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f));
            toggle.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 0.8f));
            toggle.AddThemeFontSizeOverride("font_size", 14);
            toggle.Pressed += () =>
            {
                list.Visible = !list.Visible;
                toggle.Text = (list.Visible ? "▾  Hide " : "▸  Show ") + rest;
            };
            box.AddChild(toggle);
            box.AddChild(list);
            foreach (var group in slots)
            {
                int n = group.Count();
                var head = new Label { Text = FormattableString.Invariant($"{group.Key}: {n} {(n == 1 ? "item" : "items")}, {100.0 / slots.Count:0.#}% of rare drops together") };
                head.AddThemeFontSizeOverride("font_size", 13);
                head.CustomMinimumSize = new Vector2(0, 24);   // a little room above each slot
                head.VerticalAlignment = VerticalAlignment.Bottom;
                head.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.45f));
                list.AddChild(head);
                var table = new WeightedTable<WeightedItemDrop>();
                foreach (var (baseItem, _) in group) table.AddItem(new WeightedItemDrop(1, baseItem, 1, 1));
                var part = TableSceneOf(drops).Instantiate<ItemDropTableChanceContainer>();
                list.AddChild(part);
                // per rare item dropped: per normal kill the numbers would be too small to read (0.00%)
                part.SetTable(new ItemDropTableChance(1.0 / slots.Count, table), null);
                TitleOf(part).Visible = false;
            }
            text.Add(toggle.Text);
        }
        if (elite && Uniques.ForMonster(enemy.Id) is { } u && Make(game, Uniques.RecordOf(u)) is { } item)
        {
            string who = Elites.InfoOf(enemy.Id)?.UniqueName is { Length: > 0 } n ? n : "its Unique";
            double chance = Uniques.ChanceNow(u);
            Line($"Its Unique ({who}) also drops its own item, {u.Name}: {chance * 100:0}% on every Unique kill. "
                 + "Its chance below is per Unique kill.", Colors[Rarity.Unique]);
            var table = new WeightedTable<WeightedItemDrop>();
            table.AddItem(new WeightedItemDrop(1, item, 1, 1));
            var row = TableSceneOf(drops).Instantiate<ItemDropTableChanceContainer>();
            box.AddChild(row);
            row.SetTable(new ItemDropTableChance(Math.Min(1, chance), table), null);
        }
        tables.AddChild(box);
        DropListText = string.Join("\n", text);
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Enemy")]
    static extern Enemy? EnemyOfCard(GamesByMalcs.Melvor2.Components.CombatAreas.EnemyOptionsPanel card);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_LootTablesList")]
    static extern HBoxContainer LootIconsOf(GamesByMalcs.Melvor2.Components.CombatAreas.EnemyOptionsPanel card);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_LootTablesScene")]
    static extern PackedScene LootIconSceneOf(GamesByMalcs.Melvor2.Components.CombatAreas.EnemyOptionsPanel card);

    /// <summary>
    /// A monster's card in the combat area list shows its drops as icons: the Unique's own item joins them, with its
    /// real chance per kill there (the Unique's chance in a combat area times the item's chance per Unique kill).
    /// </summary>
    static void DropIcons(GamesByMalcs.Melvor2.Components.CombatAreas.EnemyOptionsPanel card)
    {
        if (s_game is not { } game || EnemyOfCard(card) is not { } enemy) return;
        HBoxContainer icons = LootIconsOf(card);
        const string name = "OneStyleUniqueIcon";
        Node? have = icons.GetNodeOrNull(name);
        if (have != null && !have.IsQueuedForDeletion() && have.GetMeta("enemy", "").AsString() == enemy.Id) return;
        if (have != null) { icons.RemoveChild(have); have.QueueFree(); }
        if (!Elites.Enabled || Elites.InfoOf(enemy.Id) == null || Uniques.ForMonster(enemy.Id) is not { } u || Make(game, Uniques.RecordOf(u)) is not { } item
            || Combat(game) is not { } combat) return;
        double perKill = Elites.Chances(enemy, inArea: true).Unique * Math.Min(1, Uniques.ChanceNow(u));
        var table = new WeightedTable<WeightedItemDrop>();
        table.AddItem(new WeightedItemDrop(1, item, 1, 1));
        var view = LootIconSceneOf(card).Instantiate<GamesByMalcs.Melvor2.Components.DropTables.EnemyLootTableIconView>();
        view.Name = name;
        view.SetMeta("enemy", enemy.Id);
        icons.AddChild(view);
        view.SetDrops(perKill, table, combat.Areas.Inventory);
        // its own header, so it is not a second "Chance for one of these"
        foreach (Node n in view.FindChildren("*", "Label", true, false))
            if (n is Label l && l.Text.StartsWith("Chance")) l.Text = "Its Unique's item";
        icons.Visible = true;
    }

    /// <summary>A unique item's name in the game's drop rows: in the Unique orange.</summary>
    static void DropRow(ItemDropView view)
    {
        Label name = DropNameOf(view);
        if (s_uniqueNames.Contains(name.Text)) name.Modulate = Colors[Rarity.Unique];
    }

    static HashSet<string> s_uniqueNames => _uniqueNames ??= Uniques.Defs.Values.Select(d => d.Name).ToHashSet();
    static HashSet<string>? _uniqueNames;
}
