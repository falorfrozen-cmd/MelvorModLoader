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
            // any other window is not a monster's drop list: a dungeon's "View Rewards" sits inside the fight screen too,
            // and a monster's rare drops there filled a window that cannot scroll past the screen (2026-11-25)
            if (n is GamesByMalcs.Melvor2.CustomControls.PanelContainerPopup) break;
            if (n is GamesByMalcs.Melvor2.Entities.Combat.CombatActiveView) { enemy = Combat(game)?.Enemy.Enemy; break; }
        }
        VBoxContainer tables = TablesOf(drops);
        Node? have = tables.GetNodeOrNull(DropsName);
        if (enemy == null)
        {
            if (have != null) { tables.RemoveChild(have); have.QueueFree(); }
            return;
        }
        string key = enemy.Id + "|" + DropStyle() + "|" + PreviewItemLevel(enemy);
        if (have != null && !have.IsQueuedForDeletion() && have.GetMeta("enemy", "").AsString() == key) return;
        if (have != null) { tables.RemoveChild(have); have.QueueFree(); }

        var box = new VBoxContainer { Name = DropsName };
        box.SetMeta("enemy", key);
        box.AddThemeConstantOverride("separation", 3);
        var text = new List<string>();
        // short, as the game's own lists: what can drop and how often; the long explanation is in the One Style window
        // ("?" on the title, the Elites page's "How rare drops work"), in the README and on the mod page (user, 2026-11-24)
        var title = new HBoxContainer();
        title.AddThemeConstantOverride("separation", 6);
        var head0 = new Label { Text = "Possible rare drops  ·  per kill", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 30),
            VerticalAlignment = VerticalAlignment.Bottom };
        head0.AddThemeFontSizeOverride("font_size", 15);
        title.AddChild(head0);
        var help = new Button { Text = "?", TooltipText = "How rare drops work (One Style window)", FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(30, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        help.Pressed += () => OneStyleMod.Instance?.OpenElitesPage();
        title.AddChild(help);
        box.AddChild(title);
        text.Add(head0.Text);
        void Row(string left, string right, Color c)
        {
            var row = new PanelContainer();
            var bg = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.045f) };
            bg.SetCornerRadiusAll(4);
            bg.SetContentMarginAll(5);
            bg.ContentMarginLeft = bg.ContentMarginRight = 10;
            row.AddThemeStyleboxOverride("panel", bg);
            var h = new HBoxContainer();
            var l = new Label { Text = left, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            l.AddThemeFontSizeOverride("font_size", 14);
            l.AddThemeColorOverride("font_color", c);
            var r = new Label { Text = right, HorizontalAlignment = HorizontalAlignment.Right };
            r.AddThemeFontSizeOverride("font_size", 14);
            h.AddChild(l);
            h.AddChild(r);
            row.AddChild(h);
            box.AddChild(row);
            text.Add($"{left}  {right}");
        }
        void Note(string s)
        {
            var l = new Label { Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(300, 0) };
            l.AddThemeFontSizeOverride("font_size", 12);
            l.AddThemeColorOverride("font_color", new Color(0.62f, 0.64f, 0.68f));
            box.AddChild(l);
            text.Add(s);
        }
        bool elite = Elites.InfoOf(enemy.Id) != null && Elites.Enabled;
        int iLvl = PreviewItemLevel(enemy);
        double m = UniquePowers.RareLootMultiplier;
        string Pct(double p) => FormattableString.Invariant($"{Math.Min(1, p) * 100:0.##}%");
        Row("Magic item", Pct(NormalMagic * m), Colors[Rarity.Magic]);
        Row("Rare item", Pct(NormalRare * m), Colors[Rarity.Rare]);
        if (elite)
        {
            var (champion, unique) = Elites.Chances(enemy, inArea: true);
            Row($"Champion ({Pct(champion)} of kills)", "1 item", new Color(0.55f, 0.75f, 1f));
            Row($"Unique ({Pct(unique)} of kills)", "2 items", new Color(1f, 0.6f, 0.3f));
        }
        Note($"Item level {iLvl}{(iLvl > enemy.CombatLevel ? " (Slayer task +5)" : "")}  ·  level {Math.Clamp((int)Math.Round(iLvl * 0.8), 1, 99)}+ to wear");
        // the game items a rare drop can be made from, as the game's own rows (icon, tooltip, chance of each)
        var shares = BaseShares(game, iLvl, DropStyle());
        if (shares.Count > 0)
        {
            var slots = shares.GroupBy(x => SlotOf(x.Item)).ToList();
            // the list is long: closed until asked for, one part per slot. Open, it scrolls inside a box of its own, so it
            // can never make a window taller than the screen (a window that cannot scroll would lose its close button)
            var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 2);
            var scroll = new ScrollContainer { Visible = false, CustomMinimumSize = new Vector2(0, 340),
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            scroll.AddChild(list);
            string rest = $"possible items ({shares.Count})";
            var toggle = new Button { Text = "▸  Show " + rest, Alignment = HorizontalAlignment.Left };
            toggle.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.55f));
            toggle.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 0.8f));
            toggle.AddThemeFontSizeOverride("font_size", 14);
            toggle.Pressed += () =>
            {
                scroll.Visible = !scroll.Visible;
                toggle.Text = (scroll.Visible ? "▾  Hide " : "▸  Show ") + rest;
            };
            box.AddChild(toggle);
            box.AddChild(scroll);
            foreach (var group in slots)
            {
                int n = group.Count();
                var head = new Label { Text = FormattableString.Invariant($"{group.Key}  ·  {100.0 / slots.Count:0.#}% of rare drops") };
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
            var uh = new Label { Text = $"{who}'s own item (per Unique kill)", CustomMinimumSize = new Vector2(0, 26), VerticalAlignment = VerticalAlignment.Bottom };
            uh.AddThemeFontSizeOverride("font_size", 13);
            uh.AddThemeColorOverride("font_color", Colors[Rarity.Unique]);
            box.AddChild(uh);
            text.Add($"{uh.Text}: {u.Name} {chance * 100:0}%");
            var table = new WeightedTable<WeightedItemDrop>();
            table.AddItem(new WeightedItemDrop(1, item, 1, 1));
            var row = TableSceneOf(drops).Instantiate<ItemDropTableChanceContainer>();
            box.AddChild(row);
            row.SetTable(new ItemDropTableChance(Math.Min(1, chance), table), null);
            TitleOf(row).Visible = false;   // our own line above says what it is
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
