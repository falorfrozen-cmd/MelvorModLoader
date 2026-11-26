using System;
using System.Collections.Generic;
using System.Linq;
using CheatMenu;
using Godot;

namespace BulkSell;

/// <summary>
/// The window's second page: auto-sell rules for One Style's rare items (rarity, level per rarity, gear for other combat
/// styles, 3-affix Rares, gear slots, affixes to sell or keep), a preview of what the rules would sell from the bank now
/// with a way to look at them in the list first, and the last auto-sales. 0.4.0 (asked for in play, worked out with
/// Gemini, Ortak-Calisma/bulk-sell-auto-2): the style rule, the slot rule, the 3-affix rule, ready-made rule sets, a
/// searchable affix grid in groups with the bank's counts, and a warning when a rule catches many Rare items.
/// </summary>
public sealed partial class Window
{
    readonly Button _autoButton = new() { Text = "Auto-sell", TooltipText = "Rules that sell new Magic and Rare items as they drop (One Style)." };
    Control _listPage = null!;
    Control _autoPage = null!;
    Label _hint = null!;
    const string ListHint = "Click a row to mark it, Shift+click to mark a range. Locked items never appear here.";

    readonly CheckBox _autoOn = new() { Text = "Auto-sell new rare items" };
    readonly OptionButton _presetPick = new() { CustomMinimumSize = new Vector2(290, 0), FitToLongestItem = false, ClipText = true };
    readonly Button _resetRules = new() { Text = "Reset rules" };
    bool _resetArmed;

    readonly CheckBox _allMagic = new() { Text = "Every Magic item" };
    readonly CheckBox _allRare = new() { Text = "Every Rare item" };
    readonly CheckBox _threeAffix = new() { Text = "3-affix Rares" };
    readonly OptionButton _magicBelow = new() { CustomMinimumSize = new Vector2(150, 0) };
    readonly OptionButton _rareBelow = new() { CustomMinimumSize = new Vector2(150, 0) };

    readonly CheckBox _otherStyles = new();
    readonly CheckBox _sellMelee = new() { Text = "Melee" };
    readonly CheckBox _sellRanged = new() { Text = "Ranged" };
    readonly CheckBox _sellMagic = new() { Text = "Magic" };
    readonly Button _slotsToggle = new();
    readonly HBoxContainer _slotRow = new() { Visible = false };
    readonly Dictionary<string, Button> _slotButtons = new();
    /// <summary>The slots rare items come in (never ammo).</summary>
    static readonly string[] AutoSlots = { "Weapon", "Offhand", "Helmet", "Torso", "Legs", "Gloves", "Boots", "Cape", "Amulet", "Ring" };

    readonly LineEdit _affixSearch = new() { PlaceholderText = "Search affixes...", ClearButtonEnabled = true, CustomMinimumSize = new Vector2(210, 0) };
    readonly Dictionary<string, Button> _categoryButtons = new();
    string _category = "All";
    readonly CheckBox _onlyInBank = new() { Text = "Only ones in the bank" };
    readonly GridContainer _affixGrid = new() { Columns = 3 };
    readonly Dictionary<string, Button> _affixButtons = new();
    readonly Dictionary<string, Gear.Affix> _affixes = new();
    Dictionary<string, int> _affixCounts = new();

    readonly Label _preview = Style.Label("", Style.Muted, 13);
    readonly Button _sellNow = new() { Text = "Sell matching items now" };
    readonly Button _showInList = new();
    readonly Label _history = Style.Label("", Style.Muted, 13);
    readonly Button _historyToggle = new();
    readonly Label _historyList = Style.Label("", Style.Muted, 13);
    readonly ScrollContainer _historyScroll = new() { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 150), Visible = false };
    /// <summary>The list page shows only what the auto-sell rules match ("Show in the list").</summary>
    bool _onlyAutoMatches;
    readonly Button _matchChip = new() { Text = "Auto-sell matches only  x", Visible = false,
        TooltipText = "The list shows only the items the auto-sell rules match. Click to show the whole bank again." };
    bool _nowArmed;
    bool _painting;
    /// <summary>Which arming the 5-second timers belong to: a timer of an older click never disarms a newer one.</summary>
    int _nowArmId, _resetArmId;
    static readonly Color Amber = new(1f, 0.72f, 0.32f);
    static readonly Color SellBg = new(0.42f, 0.13f, 0.12f);

    Control BuildAutoPage()
    {
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 9);

        // ---- on/off, ready-made rules, reset
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        _autoOn.AddThemeFontSizeOverride("font_size", 16);
        _autoOn.Toggled += on => { if (_painting) return; AutoSell.Rules.On = on; AutoSell.Save(_bank, turnedOn: on); PaintAuto(); };
        top.AddChild(_autoOn);
        Label note = Style.Label("As they drop: never what is already in the bank.", Style.Muted, 13);
        note.VerticalAlignment = VerticalAlignment.Center;
        note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        note.ClipText = true;
        top.AddChild(note);
        _presetPick.AddItem("Ready-made rules...");
        foreach (string p in AutoSell.Presets) _presetPick.AddItem(p);
        _presetPick.Select(0);
        _presetPick.TooltipText = "Sets the rarity, style and level rules in one go. Your Sell and Keep affixes stay as they are.";
        _presetPick.ItemSelected += i => { if (!_painting && i > 0) ApplyPreset((int)i - 1); };
        top.AddChild(_presetPick);
        _resetRules.TooltipText = "Turns every rule off and forgets every affix choice (asks once).";
        _resetRules.Pressed += ResetPressed;
        top.AddChild(_resetRules);
        page.AddChild(top);

        // ---- rarity and item level
        var rarity = new HBoxContainer();
        rarity.AddThemeConstantOverride("separation", 14);
        rarity.AddChild(Label("Sell"));
        _allMagic.AddThemeColorOverride("font_color", new Color(0.45f, 0.66f, 1f));
        _allRare.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.29f));
        _threeAffix.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.29f));
        _allMagic.Toggled += v => Change(() => AutoSell.Rules.AllMagic = v);
        _allRare.Toggled += v => Change(() => AutoSell.Rules.AllRare = v);
        _threeAffix.Toggled += v => Change(() => AutoSell.Rules.RareThreeAffix = v);
        _threeAffix.TooltipText = "Sells Rare items with 3 affixes; the ones with 4 stay.";
        rarity.AddChild(_allMagic);
        rarity.AddChild(_allRare);
        rarity.AddChild(_threeAffix);
        rarity.AddChild(Label("   Magic below"));
        rarity.AddChild(_magicBelow);
        rarity.AddChild(Label("Rare below"));
        rarity.AddChild(_rareBelow);
        foreach (OptionButton pick in new[] { _magicBelow, _rareBelow })
            foreach (int l in LevelSteps) pick.AddItem(l == 0 ? "off" : $"item level {l}");
        _magicBelow.ItemSelected += i => Change(() => AutoSell.Rules.MagicBelow = LevelSteps[(int)i]);
        _rareBelow.ItemSelected += i => Change(() => AutoSell.Rules.RareBelow = LevelSteps[(int)i]);
        page.AddChild(rarity);

        // ---- gear for a combat style, gear slots
        var styles = new HBoxContainer();
        styles.AddThemeConstantOverride("separation", 14);
        styles.AddChild(Label("Gear for"));
        _otherStyles.TooltipText = "Sells Magic and Rare gear for the combat styles that are not your One Style class: your character\n"
            + "cannot use it. This rule goes before Keep affixes. Rings, amulets and capes fit every class and never count.\n"
            + "If you ever change your class, the gear of your new style may already have been sold.";
        _otherStyles.Toggled += v => Change(() => AutoSell.Rules.OtherStyles = v);
        styles.AddChild(_otherStyles);
        styles.AddChild(Label("   also"));
        foreach ((CheckBox box, string style) in new[] { (_sellMelee, "Melee"), (_sellRanged, "Ranged"), (_sellMagic, "Magic") })
        {
            box.TooltipText = $"Sells Magic and Rare gear for {style}, whatever your class (goes before Keep affixes).";
            string which = style;
            box.Toggled += v => Change(() =>
            {
                if (which == "Melee") AutoSell.Rules.SellMelee = v;
                else if (which == "Ranged") AutoSell.Rules.SellRanged = v;
                else AutoSell.Rules.SellMagic = v;
            });
            styles.AddChild(box);
        }
        styles.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _slotsToggle.TooltipText = "Sell Magic and Rare items of some gear slots (e.g. the slot you wear a Unique in).";
        _slotsToggle.Pressed += () => { _slotRow.Visible = !_slotRow.Visible; PaintAuto(); };
        styles.AddChild(_slotsToggle);
        page.AddChild(styles);

        _slotRow.AddThemeConstantOverride("separation", 6);
        _slotRow.AddChild(Label("Sell these slots:"));
        foreach (string slot in AutoSlots)
        {
            var b = new Button { Text = slot, FocusMode = Control.FocusModeEnum.None };
            b.AddThemeFontSizeOverride("font_size", 13);
            string which = slot;
            b.Pressed += () => Change(() => { if (!AutoSell.Rules.SellSlots.Remove(which)) AutoSell.Rules.SellSlots.Add(which); });
            _slotButtons[slot] = b;
            _slotRow.AddChild(b);
        }
        page.AddChild(_slotRow);

        // ---- affixes: search, groups, the grid
        page.AddChild(Style.Label("Affixes: click to cycle  off → Sell (red) → Keep (green). Keep saves an item from every rule but the style rule. "
            + "The number is how many unlocked items in the bank have it.", Style.Muted, 13));
        var tools = new HBoxContainer();
        tools.AddThemeConstantOverride("separation", 6);
        _affixSearch.TextChanged += _ => FilterAffixes();
        tools.AddChild(_affixSearch);
        var group = new ButtonGroup();
        foreach (string c in new[] { "All", "Active" }.Concat(Gear.Categories))
        {
            var b = new Button { Text = c, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None, ButtonPressed = c == "All" };
            b.AddThemeFontSizeOverride("font_size", 13);
            string which = c;
            b.Pressed += () => { _category = which; FilterAffixes(); };
            _categoryButtons[c] = b;
            tools.AddChild(b);
        }
        _categoryButtons["Active"].TooltipText = "Only the affixes you set to Sell or Keep.";
        tools.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        _onlyInBank.Toggled += _ => FilterAffixes();
        tools.AddChild(_onlyInBank);
        page.AddChild(tools);
        _affixGrid.AddThemeConstantOverride("h_separation", 6);
        _affixGrid.AddThemeConstantOverride("v_separation", 6);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 190) };
        _affixGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_affixGrid);
        page.AddChild(scroll);

        // ---- what the rules match in the bank now
        var now = new HBoxContainer();
        now.AddThemeConstantOverride("separation", 10);
        _sellNow.Pressed += SellNowPressed;
        _sellNow.TooltipText = "Sells what the rules match among the rare items in the bank now (locked ones never). Asks once.";
        now.AddChild(_sellNow);
        _showInList.TooltipText = "Opens the list with only these items, all marked: unmark what you want to keep, then \"Sell marked\".";
        _showInList.Pressed += ShowMatchesInList;
        now.AddChild(_showInList);
        _preview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _preview.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        now.AddChild(_preview);
        page.AddChild(now);

        var past = new HBoxContainer();
        past.AddThemeConstantOverride("separation", 10);
        _history.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _history.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        past.AddChild(_history);
        _historyToggle.Pressed += () => { _historyScroll.Visible = !_historyScroll.Visible; PaintAuto(); };
        past.AddChild(_historyToggle);
        page.AddChild(past);
        _historyList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _historyScroll.AddChild(_historyList);
        page.AddChild(_historyScroll);
        return page;
    }

    static Label Label(string text)
    {
        Label l = Style.Label(text, Style.Muted);
        l.VerticalAlignment = VerticalAlignment.Center;
        return l;
    }

    /// <summary>A rule changed: saved, and the drops so far are judged no more (only new drops meet the new rules).</summary>
    void Change(Action set)
    {
        if (_painting) return;
        _nowArmed = _resetArmed = false;   // a question asked under the old rules is not answered under the new ones
        set();
        AutoSell.Save(_bank, forgetDrops: true);
        PaintAuto();
    }

    void ShowAutoPage(bool on)
    {
        if (on && _bank != null && _game != null) AutoSell.Bind(_game, _bank);
        _autoPage.Visible = on;
        _listPage.Visible = !on;
        _autoButton.Text = on ? "Back to the list" : "Auto-sell";
        _nowArmed = _resetArmed = false;
        LeaveMatchesOnly();
        _hint.Text = on ? AutoHint() : ListHint;
        if (on) PaintAuto(); else Reload();
    }

    static string AutoHint()
    {
        string mine = Gear.ClassStyle();
        return (mine != "" ? $"Your class: {mine}. " : "") + "Locked, worn and Unique items are never sold.";
    }

    void ApplyPreset(int which)
    {
        if (_game == null) return;
        _nowArmed = _resetArmed = false;
        AutoSell.ApplyPreset(which, _game);
        AutoSell.Save(_bank, forgetDrops: true);
        _presetPick.Select(0);
        PaintAuto();
        string notes = (Gear.ClassStyle() == "" ? " The style rule needs a One Style class, so it stayed off." : "")
            + (which == 1 && AutoSell.Rules.RareBelow == 0 ? " \"Low Rares\" starts once your class level is 30." : "");
        Say($"Rules set: {AutoSell.Presets[which]}. Your affix choices stayed as they were.{notes}");
    }

    void ResetPressed()
    {
        if (!_resetArmed)
        {
            _resetArmed = true;
            int id = ++_resetArmId;
            PaintAuto();
            _tree.CreateTimer(5).Timeout += () => { if (_resetArmed && id == _resetArmId) { _resetArmed = false; PaintAuto(); } };
            return;
        }
        _resetArmed = _nowArmed = false;
        AutoSell.ResetRules();
        AutoSell.Save(_bank, forgetDrops: true);
        PaintAuto();
        Say("Every auto-sell rule is off and the affix choices are gone.");
    }

    /// <summary>Draws the rules as they are saved, the preview of the bank and the last sales.</summary>
    void PaintAuto()
    {
        if (!_autoPage.Visible) return;
        _painting = true;
        try { PaintRules(); }
        finally { _painting = false; }
        PaintBank();
    }

    /// <summary>The controls as the rules stand (while <see cref="_painting"/>: no handler acts on them).</summary>
    void PaintRules()
    {
        AutoRules r = AutoSell.Rules;
        _hint.Text = AutoHint();
        _autoOn.ButtonPressed = r.On;
        _allMagic.ButtonPressed = r.AllMagic;
        _allRare.ButtonPressed = r.AllRare;
        _threeAffix.ButtonPressed = r.RareThreeAffix;
        _threeAffix.Disabled = r.AllRare;                 // every Rare goes anyway
        _magicBelow.Select(Math.Max(0, Array.IndexOf(LevelSteps, r.MagicBelow)));
        _rareBelow.Select(Math.Max(0, Array.IndexOf(LevelSteps, r.RareBelow)));
        _magicBelow.Disabled = r.AllMagic;
        _rareBelow.Disabled = r.AllRare;
        _resetRules.Text = _resetArmed ? "Click again: reset" : "Reset rules";

        // the style row follows the character's class
        string mine = Gear.ClassStyle();
        string[] others = new[] { "Melee", "Ranged", "Magic" }.Where(s => s != mine).ToArray();
        _otherStyles.Text = mine == "" ? "Other styles (needs a One Style class)" : $"Other styles ({string.Join(", ", others)})";
        _otherStyles.Disabled = mine == "";
        _otherStyles.ButtonPressed = r.OtherStyles && mine != "";
        foreach ((CheckBox box, string style, bool on) in new[] { (_sellMelee, "Melee", r.SellMelee), (_sellRanged, "Ranged", r.SellRanged), (_sellMagic, "Magic", r.SellMagic) })
        {
            bool covered = r.OtherStyles && mine != "" && style != mine;
            box.ButtonPressed = on || covered;
            box.Disabled = covered;
        }
        _slotsToggle.Text = (_slotRow.Visible ? "Hide slots" : "Sell by slot") + (r.SellSlots.Count > 0 ? $" ({r.SellSlots.Count})" : "");
        foreach (var (slot, b) in _slotButtons)
        {
            bool on = r.SellSlots.Contains(slot);
            PaintChoice(b, on, false);
            SetTip(b, $"Now: {(on ? "Sell" : "off")}. Click: sell every Magic and Rare {slot} (a Keep affix still saves one).");
        }

        if (_affixButtons.Count == 0)
            foreach (Gear.Affix a in Gear.Catalog())
            {
                var b = new Button { Text = a.Text, ClipText = true, CustomMinimumSize = new Vector2(270, 0),
                    SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
                b.AddThemeFontSizeOverride("font_size", 13);
                string id = a.Id;
                b.Pressed += () => CycleAffix(id);
                _affixButtons[id] = b;
                _affixes[id] = a;
                _affixGrid.AddChild(b);
            }
    }

    /// <summary>What the rules see in the bank now: the affix counts, the preview, the last sales.</summary>
    void PaintBank()
    {
        AutoRules r = AutoSell.Rules;
        List<Entry> entries = _bank != null ? Bank.Sellable(_bank) : new List<Entry>();
        _affixCounts = entries.Where(e => e.Rarity is "Magic" or "Rare").SelectMany(e => e.AffixIds.Distinct())
            .GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (id, b) in _affixButtons)
        {
            bool sell = r.SellAffixes.Contains(id), keep = r.KeepAffixes.Contains(id);
            PaintChoice(b, sell, keep);
            int count = _affixCounts.GetValueOrDefault(id);
            Gear.Affix a = _affixes[id];
            string text = count > 0 ? $"{a.Text}  ({count})" : a.Text;
            if (b.Text != text) b.Text = text;
            SetTip(b, $"{a.Name}: {a.Text}\nIn the bank now: {count} item(s).\nNow: {(sell ? "Sell" : keep ? "Keep" : "off")}. Click: off → Sell → Keep → off.");
        }
        int active = r.SellAffixes.Count + r.KeepAffixes.Count;
        _categoryButtons["Active"].Text = active > 0 ? $"Active ({active})" : "Active";
        FilterAffixes();

        if (_bank != null)
        {
            var matching = AutoSell.Matching(entries);
            long gold = matching.Sum(m => m.Entry.Price * m.Entry.Quantity);
            int rares = matching.Count(m => m.Entry.Rarity == "Rare");
            bool risky = r.AllRare || rares >= 10;
            string warning = r.AllRare ? "  \"Every Rare item\" is on: every new Rare will be sold."
                : rares >= 10 ? $"  Many Rare items match: look at them in the list before {(r.On ? "you sell them" : "you turn auto-sell on")}." : "";
            _preview.Text = (matching.Count == 0 ? "Nothing in the bank matches the rules now."
                : $"In the bank now: {matching.Count} item(s) match ({rares} Rare), about {gold:N0} gold: "
                  + string.Join(", ", matching.Take(5).Select(m => $"{Bank.Name(m.Entry.Item)} ({m.Reason})")) + (matching.Count > 5 ? ", ..." : "")) + warning;
            _preview.AddThemeColorOverride("font_color", risky ? Amber : Style.Muted);
            _sellNow.Disabled = matching.Count == 0;
            _sellNow.Text = _nowArmed ? $"Click again: sell {matching.Count}" : "Sell matching items now";
            _showInList.Text = $"Show in the list ({matching.Count})";
            _showInList.Disabled = matching.Count == 0;
        }
        _history.Text = AutoSell.History.Count == 0 ? (r.On ? "Auto-sell is on. Nothing sold yet." : "Auto-sell is off.")
            : $"Auto-sold this session: {AutoSell.SoldItems:N0} for {Bank.Money(AutoSell.SoldFor)}. Last: {AutoSell.History[0]}";
        _historyToggle.Visible = AutoSell.History.Count > 1;
        _historyToggle.Text = _historyScroll.Visible ? "Hide the last sales" : $"Last {AutoSell.History.Count} sales";
        _historyList.Text = string.Join("\n", AutoSell.History);
    }

    /// <summary>The look of a Sell (red) / Keep (green) / off button: three boxes per state, made once and shared.</summary>
    static readonly Dictionary<int, (StyleBoxFlat Normal, StyleBoxFlat Hover, Color Font)> s_choiceLooks = new();
    readonly Dictionary<Button, int> _choiceShown = new();

    void PaintChoice(Button b, bool sell, bool keep)
    {
        int state = sell ? 1 : keep ? 2 : 0;
        if (_choiceShown.TryGetValue(b, out int was) && was == state) return;
        _choiceShown[b] = state;
        if (!s_choiceLooks.TryGetValue(state, out var look))
        {
            Color bg = sell ? SellBg : keep ? Style.GoodDim : Style.Control;
            Color line = sell ? Style.Bad : keep ? Style.Good : Style.Line;
            look = (Style.Box(bg, 5, line, 1, 8, 4), Style.Box(bg.Lightened(0.12f), 5, Style.Accent, 1, 8, 4),
                sell ? new Color(1f, 0.78f, 0.75f) : keep ? new Color(0.78f, 1f, 0.8f) : Style.Text);
            s_choiceLooks[state] = look;
        }
        b.AddThemeStyleboxOverride("normal", look.Normal);
        b.AddThemeStyleboxOverride("hover", look.Hover);
        b.AddThemeStyleboxOverride("pressed", look.Normal);
        b.AddThemeColorOverride("font_color", look.Font);
    }

    /// <summary>A tooltip written only when it changes (a tooltip that is open stays open).</summary>
    static void SetTip(Control c, string text) { if (c.TooltipText != text) c.TooltipText = text; }

    /// <summary>Shows the affix buttons the search, the group and "Only ones in the bank" let through.</summary>
    void FilterAffixes()
    {
        string text = _affixSearch.Text.Trim();
        AutoRules r = AutoSell.Rules;
        foreach (var (id, b) in _affixButtons)
        {
            Gear.Affix a = _affixes[id];
            bool show = (text == "" || a.Text.Contains(text, StringComparison.OrdinalIgnoreCase) || a.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                && (_category switch
                {
                    "All" => true,
                    "Active" => r.SellAffixes.Contains(id) || r.KeepAffixes.Contains(id),
                    _ => Gear.CategoryOf(id) == _category,
                })
                && (!_onlyInBank.ButtonPressed || _affixCounts.GetValueOrDefault(id) > 0);
            b.Visible = show;
        }
    }

    void CycleAffix(string id)
    {
        AutoRules r = AutoSell.Rules;
        _nowArmed = _resetArmed = false;
        if (r.SellAffixes.Remove(id)) r.KeepAffixes.Add(id);
        else if (!r.KeepAffixes.Remove(id)) r.SellAffixes.Add(id);
        AutoSell.Save(_bank, forgetDrops: true);
        PaintAuto();
    }

    void SellNowPressed()
    {
        if (_bank == null) return;
        var matching = AutoSell.Matching(_bank);
        if (matching.Count == 0) { _nowArmed = false; PaintAuto(); return; }
        if (!_nowArmed)
        {
            _nowArmed = true;
            int id = ++_nowArmId;
            PaintAuto();
            _tree.CreateTimer(5).Timeout += () => { if (_nowArmed && id == _nowArmId) { _nowArmed = false; PaintAuto(); } };
            return;
        }
        _nowArmed = false;
        var (sold, paid) = AutoSell.Sell(_bank, matching.Select(m => (m.Entry.Item, Bank.Name(m.Entry.Item), m.Reason)).ToList());
        Say($"Sold {sold:N0} rare item(s) for {Bank.Money(paid)}.");
        PaintAuto();
    }

    /// <summary>The list page with only what the rules match, every one marked: unmark the keepers, then "Sell marked".</summary>
    void ShowMatchesInList()
    {
        ShowAutoPage(false);
        ClearFilters();
        _onlyAutoMatches = true;
        _matchChip.Visible = true;
        Rebuild();
        MarkShown(true);
        Say($"The {_shown.Count} item(s) the auto-sell rules match, all marked. Unmark what you want to keep, then press \"Sell marked\".");
    }

    void HideMatchesOnly()
    {
        LeaveMatchesOnly();
        Rebuild();
    }

    /// <summary>Back to the whole bank: the marks made for "Show in the list" go too, so none of them is sold by surprise.</summary>
    void LeaveMatchesOnly()
    {
        if (_onlyAutoMatches) _marked.Clear();
        _onlyAutoMatches = false;
        _matchChip.Visible = false;
    }

    // ---------------------------------------------------------------- for tests
    public void PressAutoPage(bool on) => ShowAutoPage(on);
    public bool AutoPageShown => _autoPage.Visible;
    public string AutoPreview => _preview.Text;
    public bool AutoPreviewWarns => _preview.GetThemeColor("font_color") == Amber;
    public void PressSellNow() => SellNowPressed();
    public int AffixButtonCount => _affixButtons.Count;
    public string OtherStylesText => _otherStyles.Text;
    public bool OtherStylesOffered => !_otherStyles.Disabled;
    public void PressOtherStyles(bool on) => _otherStyles.ButtonPressed = on;
    public string StyleBoxes => $"{_sellMelee.ButtonPressed}/{_sellMelee.Disabled} {_sellRanged.ButtonPressed}/{_sellRanged.Disabled} {_sellMagic.ButtonPressed}/{_sellMagic.Disabled}";
    public void PressSlot(string slot) { if (!_slotRow.Visible) _slotsToggle.EmitSignal(BaseButton.SignalName.Pressed); _slotButtons[slot].EmitSignal(BaseButton.SignalName.Pressed); }
    public void PickPreset(int which) { _presetPick.Select(which + 1); ApplyPreset(which); }
    public void PressReset() => ResetPressed();
    public void SearchAffixes(string text) { _affixSearch.Text = text; FilterAffixes(); }
    public void PickAffixGroup(string group) { _categoryButtons[group].ButtonPressed = true; _category = group; FilterAffixes(); }
    public void SetOnlyInBank(bool on) => _onlyInBank.ButtonPressed = on;
    public List<string> VisibleAffixes => _affixButtons.Where(p => p.Value.Visible).Select(p => p.Key).ToList();
    public string AffixButtonText(string id) => _affixButtons.TryGetValue(id, out Button? b) ? b.Text : "";
    public string ShowInListText => _showInList.Text;
    public void PressShowInList() => ShowMatchesInList();
    public bool ShowingMatchesOnly => _onlyAutoMatches;
    public bool HistoryOffered => _historyToggle.Visible;
}
