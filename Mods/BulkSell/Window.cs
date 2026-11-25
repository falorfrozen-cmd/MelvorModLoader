using System;
using System.Collections.Generic;
using System.Linq;
using CheatMenu;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Bank;
using Godot;

namespace BulkSell;

/// <summary>
/// The bulk sell window: every sellable stack of the bank in one table. Click a row to mark it,
/// narrow the table with the filters, then sell everything marked in one go.
/// </summary>
public sealed partial class Window
{
    const int ColMark = 0, ColItem = 1, ColLevel = 2, ColQuantity = 3, ColEach = 4, ColTotal = 5;

    readonly SceneTree _tree;
    readonly CanvasLayer _layer = new() { Layer = 119, Name = "BulkSell", Visible = false };
    readonly Label _status = Style.Label("", Style.Good);
    readonly Label _summary = Style.Label("");
    readonly Label _hidden = Style.Label("", Style.Muted, 12);
    readonly LineEdit _search = new() { PlaceholderText = "Search the bank...", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
    readonly OptionButton _tabPick = new() { CustomMinimumSize = new Vector2(150, 0) };
    readonly OptionButton _kindPick = new() { CustomMinimumSize = new Vector2(220, 0), FitToLongestItem = false, ClipText = true };
    readonly SpinBox _keep = new() { MinValue = 0, MaxValue = 1_000_000, Step = 1, Value = 0, CustomMinimumSize = new Vector2(110, 0), SelectAllOnFocus = true };
    // the gear row: slot, rarity and affix (One Style's rare items), level
    readonly OptionButton _slotPick = new() { CustomMinimumSize = new Vector2(150, 0) };
    readonly OptionButton _rarityPick = new() { CustomMinimumSize = new Vector2(150, 0) };
    readonly OptionButton _affixPick = new() { CustomMinimumSize = new Vector2(260, 0), FitToLongestItem = false, ClipText = true };
    readonly OptionButton _levelPick = new() { CustomMinimumSize = new Vector2(150, 0),
        TooltipText = "Only gear of this level or lower (a rare item's item level, else the level the gear asks for)." };
    static readonly int[] LevelSteps = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
    int MaxLevel => _levelPick.Selected > 0 && _levelPick.Selected < LevelSteps.Length ? LevelSteps[_levelPick.Selected] : 0;
    readonly Label _rarityLabel = Style.Label("Rarity", Style.Muted);
    readonly Label _affixLabel = Style.Label("Affix", Style.Muted);
    static readonly string[] SlotOrder = { "Weapon", "Offhand", "Ammo", "Helmet", "Torso", "Legs", "Gloves", "Boots", "Cape", "Amulet", "Ring" };
    static readonly string[] Rarities = { "Any rarity", "Normal", "Magic", "Rare", "Unique", "Magic + Rare" };
    List<string> _slots = new(), _affixKinds = new();
    readonly Tree _table;
    readonly Button _sell = new() { Text = "Sell marked", CustomMinimumSize = new Vector2(250, 0) };
    readonly Button _junkCheck = new();
    readonly Button _addJunk = new() { Text = "Add marked to junk" };
    readonly Button _removeJunk = new() { Text = "Remove marked from junk" };

    Game? _game;
    BankEntity? _bank;
    List<Entry> _entries = new(), _shown = new();
    readonly HashSet<Item> _marked = new();
    List<string> _kinds = new();
    int _sortColumn = -1;
    bool _sortDescending;
    bool _armed;
    int _armToken;

    public bool Visible => _layer.Visible;
    public Control Panel { get; }
    /// <summary>For tests: the items marked for sale.</summary>
    public IReadOnlyCollection<Item> Marked => _marked;
    public IReadOnlyList<Entry> Shown => _shown;

    public Window(SceneTree tree)
    {
        _tree = tree;
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.25f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(shade);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(980, 700), Theme = Style.Build() };
        StyleBoxFlat frame = Style.Box(Style.Window, 8, Style.AccentDim, 1);
        frame.ShadowColor = new Color(0, 0, 0, 0.55f);
        frame.ShadowSize = 14;
        panel.AddThemeStyleboxOverride("panel", frame);
        center.AddChild(panel);
        Panel = panel;

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        panel.AddChild(root);

        // ---- header
        StyleBoxFlat headBox = Style.Box(Style.Sidebar, 0, null, 0, 18, 12);
        headBox.CornerRadiusTopLeft = headBox.CornerRadiusTopRight = 7;
        headBox.BorderColor = Style.Line;
        headBox.BorderWidthBottom = 1;
        var head = new PanelContainer();
        head.AddThemeStyleboxOverride("panel", headBox);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        header.AddChild(Style.Label("BULK SELL", Style.Accent, 20));
        Label hint = Style.Label("Click a row to mark it, Shift+click to mark a range. Locked items never appear here.", Style.Muted);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.SizeFlagsVertical = Control.SizeFlags.Fill;
        header.AddChild(hint);
        _autoButton.Pressed += () => ShowAutoPage(!_autoPage.Visible);
        header.AddChild(_autoButton);
        header.AddChild(MakeButton("Close", Hide));
        head.AddChild(header);
        root.AddChild(head);

        // ---- body
        var margin = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        foreach (string m in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(m, 16);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        margin.AddChild(body);
        root.AddChild(margin);
        // two pages: the table (filters, marks, selling) and the auto-sell rules
        var list = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);
        body.AddChild(list);
        _listPage = list;
        _autoPage = BuildAutoPage();
        _autoPage.Visible = false;
        body.AddChild(_autoPage);

        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 10);
        _search.TextChanged += _ => Rebuild();
        _tabPick.ItemSelected += _ => Rebuild();
        _kindPick.ItemSelected += _ => Rebuild();
        filters.AddChild(_search);
        filters.AddChild(_tabPick);
        filters.AddChild(_kindPick);
        list.AddChild(filters);

        var gear = new HBoxContainer();
        gear.AddThemeConstantOverride("separation", 8);
        Label gearTitle = Style.Label("Gear slot", Style.Muted);
        gearTitle.VerticalAlignment = VerticalAlignment.Center;
        gear.AddChild(gearTitle);
        _slotPick.ItemSelected += _ => Rebuild();
        _rarityPick.ItemSelected += _ => Rebuild();
        _affixPick.ItemSelected += _ => Rebuild();
        _levelPick.ItemSelected += _ => Rebuild();
        foreach (int l in LevelSteps) _levelPick.AddItem(l == 0 ? "Any level" : $"Level {l} or lower");
        _levelPick.Select(0);
        foreach (string r in Rarities) _rarityPick.AddItem(r);
        _rarityPick.Select(0);
        gear.AddChild(_slotPick);
        _rarityLabel.VerticalAlignment = VerticalAlignment.Center;
        gear.AddChild(_rarityLabel);
        gear.AddChild(_rarityPick);
        _affixLabel.VerticalAlignment = VerticalAlignment.Center;
        gear.AddChild(_affixLabel);
        gear.AddChild(_affixPick);
        gear.AddChild(_levelPick);
        gear.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        gear.AddChild(MakeButton("Clear filters", ClearFilters));
        list.AddChild(gear);

        var picks = new HBoxContainer();
        picks.AddThemeConstantOverride("separation", 10);
        picks.AddChild(Style.Label("Mark:", Style.Muted));
        picks.AddChild(MakeButton("All shown", () => MarkShown(true)));
        picks.AddChild(MakeButton("None", () => { _marked.Clear(); Repaint(); }));
        picks.AddChild(MakeButton("Invert shown", () => { foreach (Entry e in _shown) Toggle(e.Item); Repaint(); }));
        _junkCheck.Pressed += () =>
        {
            foreach (Entry e in _entries) if (Junk.Contains(e.Item)) _marked.Add(e.Item);
            Repaint();
        };
        picks.AddChild(_junkCheck);
        picks.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        Label keepLabel = Style.Label("Keep of each:", Style.Muted);
        keepLabel.TooltipText = "This many of every marked item stay in the bank.";
        keepLabel.MouseFilter = Control.MouseFilterEnum.Pass;
        picks.AddChild(keepLabel);
        _keep.ValueChanged += _ => Repaint();
        picks.AddChild(_keep);
        list.AddChild(picks);

        _table = new Tree
        {
            Columns = 6, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, ColumnTitlesVisible = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 240)
        };
        string[] titles = { "Sell", "Item", "Level", "In bank", "Each", "You get" };
        for (int c = 0; c < titles.Length; c++)
        {
            _table.SetColumnTitle(c, titles[c]);
            _table.SetColumnTitleAlignment(c, c >= ColLevel ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            _table.SetColumnExpand(c, c == ColItem);
        }
        _table.SetColumnCustomMinimumWidth(ColMark, 60);
        _table.SetColumnCustomMinimumWidth(ColLevel, 70);
        _table.SetColumnCustomMinimumWidth(ColQuantity, 120);
        _table.SetColumnCustomMinimumWidth(ColEach, 110);
        _table.SetColumnCustomMinimumWidth(ColTotal, 150);
        _table.ItemSelected += () =>
        {
            TreeItem? row = _table.GetSelected();
            if (row == null) return;
            int index = (int)row.GetMetadata(ColItem);
            // Shift+click: every row from the last clicked one to this one gets the last click's mark, as in Windows
            if (Input.IsKeyPressed(Key.Shift) && AnchorIndex() is >= 0 and var from) MarkRange(from, index, _marked.Contains(_shown[from].Item));
            else
            {
                Toggle(_shown[index].Item);
                _anchor = _shown[index].Item;
            }
            Repaint();
            // The table's own highlight is not used: clear it so the same row can be clicked again.
            Callable.From(_table.DeselectAll).CallDeferred();
        };
        _table.ColumnTitleClicked += (column, button) =>
        {
            if (button != (long)MouseButton.Left) return;
            // First click sorts (numbers: biggest first), a second click on the same title turns the order around.
            if (_sortColumn == (int)column) _sortDescending = !_sortDescending;
            else { _sortColumn = (int)column; _sortDescending = column >= ColLevel; }
            Rebuild();
        };
        list.AddChild(_table);

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 10);
        var totals = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        totals.AddThemeConstantOverride("separation", 0);
        _summary.ClipText = true;
        totals.AddChild(_summary);
        totals.AddChild(_hidden);
        bottom.AddChild(totals);
        _addJunk.Pressed += AddJunkPressed;
        _addJunk.TooltipText = "Puts the marked items on your junk list (remembered for this character).\nThe \"Sell junk\" button in the bank sells everything on it.";
        bottom.AddChild(_addJunk);
        _removeJunk.Pressed += RemoveJunkPressed;
        _removeJunk.TooltipText = "Takes the marked items off your junk list. Tip: \"My junk list\" marks everything on it.";
        bottom.AddChild(_removeJunk);
        StyleBoxFlat go = Style.Box(Style.GoodDim, 5, Style.Good, 1, 12, 7);
        _sell.AddThemeStyleboxOverride("normal", go);
        _sell.AddThemeStyleboxOverride("hover", Style.Box(new Color(0.16f, 0.38f, 0.23f), 5, Style.Good, 1, 12, 7));
        _sell.AddThemeStyleboxOverride("pressed", go);
        _sell.Pressed += SellPressed;
        bottom.AddChild(_sell);
        list.AddChild(bottom);

        // ---- footer
        StyleBoxFlat footBox = Style.Box(Style.Sidebar, 0, null, 0, 18, 8);
        footBox.CornerRadiusBottomLeft = footBox.CornerRadiusBottomRight = 7;
        footBox.BorderColor = Style.Line;
        footBox.BorderWidthTop = 1;
        var foot = new PanelContainer();
        foot.AddThemeStyleboxOverride("panel", footBox);
        _status.ClipText = true;
        _status.CustomMinimumSize = new Vector2(0, 20);
        foot.AddChild(_status);
        root.AddChild(foot);

        tree.Root.AddChild(_layer);
    }

    static Button MakeButton(string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        return b;
    }

    void Say(string text) => _status.Text = text;

    public void Show(Game game)
    {
        _game = game;
        _bank = Bank.Of(game);
        if (_bank == null) return;
        Junk.Bind(game.CharacterName);
        _marked.Clear();
        _search.Text = "";
        Disarm();
        Say("");
        _autoButton.Visible = Gear.CanAutoSell;   // needs a One Style that tells its new drops
        _autoPage.Visible = false;
        _listPage.Visible = true;
        _autoButton.Text = "Auto-sell";
        Reload();
        _layer.Visible = true;
    }

    public void Hide()
    {
        _layer.Visible = false;
        Disarm();
    }

    /// <summary>Called about once a second while open: stacks grow and shrink while the game runs.</summary>
    public void Pulse()
    {
        if (!Visible || _bank == null || _armed) return;
        if (_autoPage.Visible) { if (!_nowArmed) PaintAuto(); return; }
        Dictionary<Item, int> now = _bank.Inventory.Stacks.Where(s => !s.IsLocked && !s.IsPlaceholder && s.Quantity > 0)
            .GroupBy(s => s.Item).ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));
        if (now.Count != _entries.Count || _entries.Any(e => !now.ContainsKey(e.Item))) { Reload(); return; }
        bool changed = false;
        foreach (Entry e in _entries)
            if (e.Quantity != now[e.Item]) { e.Quantity = now[e.Item]; changed = true; }
        if (changed) Repaint();
    }

    /// <summary>Reads the bank again and rebuilds the filters and the table.</summary>
    void Reload()
    {
        if (_bank == null) return;
        _entries = Bank.Sellable(_bank);
        _marked.RemoveWhere(i => _entries.All(e => e.Item != i));

        int tab = _tabPick.Selected, tabs = _bank.Inventory.InventoryTabs.Count;
        _tabPick.Clear();
        _tabPick.AddItem("All tabs");
        for (int i = 0; i < tabs; i++) _tabPick.AddItem($"Tab {i + 1}");
        _tabPick.Select(tab > 0 && tab <= tabs ? tab : 0);

        string kind = _kindPick.Selected > 0 && _kindPick.Selected - 1 < _kinds.Count ? _kinds[_kindPick.Selected - 1] : "";
        _kinds = _entries.SelectMany(e => e.Item.BankTags.Select(t => t.BankTag.DisplayName.Text)).Distinct()
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        _kindPick.Clear();
        _kindPick.AddItem("Any kind");
        foreach (string k in _kinds) _kindPick.AddItem(k);
        _kindPick.AddItem("Untagged");
        int at = _kinds.IndexOf(kind);
        _kindPick.Select(at >= 0 ? at + 1 : 0);

        _slots = _entries.Where(e => e.Slot != "").Select(e => e.Slot).Distinct()
            .OrderBy(s => Array.IndexOf(SlotOrder, s) is int i && i >= 0 ? i : 99).ThenBy(s => s).ToList();
        Refill(_slotPick, new[] { "Any item", "All gear", "Not gear" }.Concat(_slots));
        _affixKinds = _entries.Where(e => e.Rarity is "Magic" or "Rare").SelectMany(e => e.Affixes).Select(Gear.AffixKind).Distinct()
            .OrderBy(k => k.TrimStart('+', '-', '#', '%', ' '), StringComparer.OrdinalIgnoreCase).ToList();
        Refill(_affixPick, new[] { "Any affix" }.Concat(_affixKinds.Select(Gear.AffixName)));
        bool rares = Gear.RaresLoaded;
        _rarityLabel.Visible = _rarityPick.Visible = _affixLabel.Visible = _affixPick.Visible = rares;

        int locked = Bank.LockedCount(_bank);
        _hidden.Text = locked > 0 ? $"{locked} locked stack(s) are protected and not listed." : "";
        Rebuild();
    }

    /// <summary>Applies the filters and the sort order, then fills the table.</summary>
    void Rebuild()
    {
        IEnumerable<Entry> list = _entries;
        if (_tabPick.Selected > 0) list = list.Where(e => e.Tab == _tabPick.Selected - 1);
        int kind = _kindPick.Selected;
        if (kind > 0 && kind <= _kinds.Count) list = list.Where(e => e.Item.BankTags.Any(t => t.BankTag.DisplayName.Text == _kinds[kind - 1]));
        else if (kind == _kinds.Count + 1) list = list.Where(e => e.Item.BankTags.Count == 0);
        string text = _search.Text.Trim();
        // the search also finds a rare item by its base item ("amulet") and its affixes ("lifesteal")
        if (text != "") list = list.Where(e => Bank.Name(e.Item).Contains(text, StringComparison.OrdinalIgnoreCase)
                                               || e.BaseName.Contains(text, StringComparison.OrdinalIgnoreCase)
                                               || e.Affixes.Any(a => a.Contains(text, StringComparison.OrdinalIgnoreCase)));
        int slot = _slotPick.Selected;
        if (slot == 1) list = list.Where(e => e.Slot != "");
        else if (slot == 2) list = list.Where(e => e.Slot == "");
        else if (slot >= 3 && slot - 3 < _slots.Count) list = list.Where(e => e.Slot == _slots[slot - 3]);
        if (_rarityPick.Visible)
            list = _rarityPick.Selected switch
            {
                1 => list.Where(e => e.Rarity == ""),
                2 => list.Where(e => e.Rarity == "Magic"),
                3 => list.Where(e => e.Rarity == "Rare"),
                4 => list.Where(e => e.Rarity == "Unique"),
                5 => list.Where(e => e.Rarity is "Magic" or "Rare"),
                _ => list,
            };
        int affix = _affixPick.Selected;
        if (_affixPick.Visible && affix >= 1 && affix - 1 < _affixKinds.Count)
            list = list.Where(e => e.Affixes.Any(a => Gear.AffixKind(a) == _affixKinds[affix - 1]));
        if (MaxLevel > 0) list = list.Where(e => e.Slot != "" && e.Level <= MaxLevel);

        list = _sortColumn switch
        {
            ColMark => list.OrderByDescending(e => _marked.Contains(e.Item)).ThenBy(e => e.Order),
            ColItem => list.OrderBy(e => Bank.Name(e.Item), StringComparer.OrdinalIgnoreCase),
            ColLevel => list.OrderBy(e => e.Level),
            ColQuantity => list.OrderBy(e => e.Quantity),
            ColEach => list.OrderBy(e => e.Price),
            ColTotal => list.OrderBy(e => e.Price * e.Quantity),
            _ => list.OrderBy(e => e.Order),
        };
        _shown = list.ToList();
        if (_sortDescending && _sortColumn >= 0) _shown.Reverse();


        _table.Clear();
        TreeItem root = _table.CreateItem();
        for (int i = 0; i < _shown.Count; i++)
        {
            Entry e = _shown[i];
            TreeItem row = _table.CreateItem(root);
            row.SetMetadata(ColItem, i);
            row.SetText(ColItem, e.BaseName != "" ? $"{Bank.Name(e.Item)}  ·  {e.BaseName}" : Bank.Name(e.Item));
            row.SetIcon(ColItem, e.Item.Icon);
            row.SetIconMaxWidth(ColItem, 22);
            if (e.Color is { } rarity) row.SetCustomColor(ColItem, rarity);
            row.SetTooltipText(ColItem, e.Rarity == "" ? "" :
                $"{e.Rarity} {e.BaseName}{(e.Level > 0 ? $", item level {e.Level}" : "")}\n{string.Join("\n", e.Affixes)}");
            row.SetText(ColLevel, e.Level > 0 ? e.Level.ToString() : "");
            row.SetCustomColor(ColLevel, Style.Muted);
            for (int c = ColLevel; c <= ColTotal; c++) row.SetTextAlignment(c, HorizontalAlignment.Right);
            row.SetCustomColor(ColEach, Style.Muted);
        }
        Repaint();
    }

    int Sellable(Entry e) => Math.Max(0, e.Quantity - (int)_keep.Value);

    /// <summary>Fills a drop-down list again, keeping what was picked when it is still there.</summary>
    static void Refill(OptionButton pick, IEnumerable<string> options)
    {
        string was = pick.Selected >= 0 && pick.Selected < pick.ItemCount ? pick.GetItemText(pick.Selected) : "";
        pick.Clear();
        int at = 0, i = 0;
        foreach (string o in options)
        {
            pick.AddItem(o);
            if (o == was) at = i;
            i++;
        }
        pick.Select(at);
    }

    void ClearFilters()
    {
        _search.Text = "";
        _tabPick.Select(0);
        _kindPick.Select(0);
        _slotPick.Select(0);
        _rarityPick.Select(0);
        _affixPick.Select(0);
        _levelPick.Select(0);
        Rebuild();
    }

    /// <summary>Updates numbers, marks and the summary without rebuilding the table.</summary>
    void Repaint()
    {
        Disarm();
        TreeItem? row = _table.GetRoot()?.GetFirstChild();
        for (int i = 0; i < _shown.Count && row != null; i++, row = row.GetNext())
        {
            Entry e = _shown[i];
            bool marked = _marked.Contains(e.Item);
            int amount = Sellable(e);
            bool junk = Junk.Contains(e.Item);
            row.SetText(ColMark, marked ? "SELL" : junk ? "junk" : "");
            row.SetCustomColor(ColMark, marked ? Style.Good : Style.Muted);
            row.SetText(ColQuantity, e.Quantity.ToString("N0"));
            row.SetText(ColEach, e.Price.ToString("N0"));
            row.SetText(ColTotal, (e.Price * (marked ? amount : e.Quantity)).ToString("N0"));
            for (int c = 0; c <= ColTotal; c++)
            {
                if (marked) row.SetCustomBgColor(c, new Color(Style.GoodDim, 0.55f)); else row.ClearCustomBgColor(c);
            }
            row.SetCustomColor(ColTotal, marked ? Style.Good : Style.Text);
        }

        var (stacks, items, worth) = Plan();
        _summary.Text = stacks == 0
            ? $"Nothing marked.  {_shown.Count} of {_entries.Count} stacks shown."
            : $"{stacks} stack(s), {items:N0} item(s) marked  ->  {Bank.Money(worth)}";
        _sell.Disabled = items == 0;
        _sell.Text = "Sell marked";
        _junkCheck.Text = $"My junk list ({Junk.Count})";
        _junkCheck.Disabled = Junk.Count == 0;
        _addJunk.Disabled = !_entries.Any(e => e.Rarity == "" && _marked.Contains(e.Item) && !Junk.Contains(e.Item));
        _removeJunk.Disabled = !_marked.Any(Junk.Contains);
    }

    (int Stacks, long Items, Dictionary<Currency, long> Worth) Plan()
    {
        var worth = new Dictionary<Currency, long>();
        int stacks = 0;
        long items = 0;
        foreach (Entry e in _entries)
        {
            int amount = Sellable(e);
            if (!_marked.Contains(e.Item) || amount == 0) continue;
            stacks++;
            items += amount;
            Currency c = e.Item.SellsFor.Currency;
            worth[c] = worth.GetValueOrDefault(c) + e.Price * amount;
        }
        return (stacks, items, worth);
    }

    void AddJunkPressed()
    {
        // a rare item (One Style) is one of a kind: its id means nothing once it is sold, so it never goes on the list
        var rare = _entries.Where(e => e.Rarity != "").Select(e => e.Item).ToHashSet();
        int skipped = _marked.Count(rare.Contains);
        int n = Junk.Add(_marked.Where(i => !rare.Contains(i)));
        _marked.Clear();
        Repaint();
        Say($"{n} item(s) added to your junk list ({Junk.Count} on it). \"Sell junk\" in the bank sells them with two clicks."
            + (skipped > 0 ? $" {skipped} rare item(s) left off: each is one of a kind, sell those with Rarity and \"Sell marked\"." : ""));
    }

    void RemoveJunkPressed()
    {
        int n = Junk.Remove(_marked);
        _marked.Clear();
        Repaint();
        Say(Junk.Count == 0 ? $"{n} item(s) taken off. Your junk list is empty now." : $"{n} item(s) taken off your junk list ({Junk.Count} left on it).");
    }

    void Toggle(Item item) { if (!_marked.Remove(item)) _marked.Add(item); }

    /// <summary>
    /// The item of the last row clicked without Shift. The item, not the row number: while you fight in the background
    /// the table is filled again every second, and a row number would be forgotten between your two clicks.
    /// </summary>
    Item? _anchor;

    int AnchorIndex() => _anchor == null ? -1 : _shown.FindIndex(e => e.Item == _anchor);

    void MarkRange(int from, int to, bool on)
    {
        for (int i = Math.Min(from, to); i <= Math.Max(from, to) && i < _shown.Count; i++)
        {
            if (on) _marked.Add(_shown[i].Item); else _marked.Remove(_shown[i].Item);
        }
    }

    void MarkShown(bool on)
    {
        foreach (Entry e in _shown) { if (on) _marked.Add(e.Item); else _marked.Remove(e.Item); }
        Repaint();
    }

    void Disarm()
    {
        _armed = false;
        _armToken++;
    }

    /// <summary>First click asks, second click (within a few seconds) sells.</summary>
    void SellPressed()
    {
        if (_bank == null) return;
        var (stacks, items, worth) = Plan();
        if (items == 0) return;
        if (!_armed)
        {
            _armed = true;
            int token = ++_armToken;
            _sell.Text = $"Click again: sell {items:N0} item(s)";
            Say($"About to sell {items:N0} item(s) from {stacks} stack(s) for {Bank.Money(worth)}. Click the button again to confirm.");
            _tree.CreateTimer(5).Timeout += () => { if (_armed && token == _armToken) { Disarm(); _sell.Text = "Sell marked"; Say(""); } };
            return;
        }
        var (sold, paid) = Bank.Sell(_bank, _marked.ToList(), (int)_keep.Value);
        _marked.Clear();
        Reload();
        Say($"Sold {sold:N0} item(s) for {Bank.Money(paid)}.");
    }

    // ---------------------------------------------------------------- for tests

    public void SetSearch(string text) { _search.Text = text; Rebuild(); }
    /// <summary>For tests: picks an entry of the gear row by its text ("Rare", "Ring", "+#% lifesteal"...).</summary>
    public bool Pick(string list, string text)
    {
        OptionButton pick = list switch { "slot" => _slotPick, "rarity" => _rarityPick, "affix" => _affixPick, _ => _kindPick };
        for (int i = 0; i < pick.ItemCount; i++)
            if (pick.GetItemText(i) == text) { pick.Select(i); Rebuild(); return true; }
        return false;
    }
    public void SetMaxLevel(int level) { _levelPick.Select(Math.Max(0, Array.IndexOf(LevelSteps, level))); Rebuild(); }
    public void PressClearFilters() => ClearFilters();
    public IReadOnlyList<string> Options(string list)
    {
        OptionButton pick = list switch { "slot" => _slotPick, "rarity" => _rarityPick, "affix" => _affixPick, _ => _kindPick };
        return Enumerable.Range(0, pick.ItemCount).Select(pick.GetItemText).ToList();
    }
    public void SetKeep(int keep) => _keep.Value = keep;
    public void ClickRow(int index, bool shift = false)
    {
        if (index < 0 || index >= _shown.Count) return;
        if (shift && AnchorIndex() is >= 0 and var from) MarkRange(from, index, _marked.Contains(_shown[from].Item));
        else { Toggle(_shown[index].Item); _anchor = _shown[index].Item; }
        Repaint();
    }
    public void PressSell() => SellPressed();
    public void PressMyJunk() => _junkCheck.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressAddJunk() => AddJunkPressed();
    public void PressRemoveJunk() => RemoveJunkPressed();
    public bool RemoveJunkEnabled => !_removeJunk.Disabled;
    /// <summary>For tests: the text in the first column of a shown row ("SELL", "junk" or empty).</summary>
    public string MarkText(int index)
    {
        TreeItem? row = _table.GetRoot()?.GetFirstChild();
        for (int i = 0; i < index && row != null; i++) row = row.GetNext();
        return row?.GetText(ColMark) ?? "";
    }
    public string Summary => _summary.Text;
    public string Status => _status.Text;
}
