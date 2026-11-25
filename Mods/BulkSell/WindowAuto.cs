using System;
using System.Collections.Generic;
using System.Linq;
using CheatMenu;
using Godot;

namespace BulkSell;

/// <summary>
/// The window's second page: auto-sell rules for One Style's rare items (rarity, level per rarity, affixes to sell or
/// keep), a preview of what the rules would sell from the bank now, and the last auto-sales.
/// </summary>
public sealed partial class Window
{
    readonly Button _autoButton = new() { Text = "Auto-sell", TooltipText = "Rules that sell new Magic and Rare items as they drop (One Style)." };
    Control _listPage = null!;
    Control _autoPage = null!;
    readonly CheckBox _autoOn = new() { Text = "Auto-sell new rare items" };
    readonly CheckBox _allMagic = new() { Text = "Every Magic item" };
    readonly CheckBox _allRare = new() { Text = "Every Rare item" };
    readonly OptionButton _magicBelow = new() { CustomMinimumSize = new Vector2(170, 0) };
    readonly OptionButton _rareBelow = new() { CustomMinimumSize = new Vector2(170, 0) };
    readonly GridContainer _affixGrid = new() { Columns = 4 };
    readonly Label _preview = Style.Label("", Style.Muted, 13);
    readonly Label _history = Style.Label("", Style.Muted, 13);
    readonly Button _sellNow = new() { Text = "Sell matching items now" };
    readonly Dictionary<string, Button> _affixButtons = new();
    bool _nowArmed;
    bool _painting;

    Control BuildAutoPage()
    {
        var page = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 10);

        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 14);
        _autoOn.AddThemeFontSizeOverride("font_size", 16);
        _autoOn.Toggled += on => { if (_painting) return; AutoSell.Rules.On = on; AutoSell.Save(_bank, turnedOn: on); PaintAuto(); };
        top.AddChild(_autoOn);
        Label note = Style.Label("As they drop. Never locked, worn or Unique items, never what is in the bank now.", Style.Muted, 13);
        note.VerticalAlignment = VerticalAlignment.Center;
        top.AddChild(note);
        page.AddChild(top);

        var rarity = new HBoxContainer();
        rarity.AddThemeConstantOverride("separation", 14);
        rarity.AddChild(Label("Sell"));
        _allMagic.AddThemeColorOverride("font_color", new Color(0.45f, 0.66f, 1f));
        _allRare.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.29f));
        _allMagic.Toggled += v => Change(() => AutoSell.Rules.AllMagic = v);
        _allRare.Toggled += v => Change(() => AutoSell.Rules.AllRare = v);
        rarity.AddChild(_allMagic);
        rarity.AddChild(_allRare);
        rarity.AddChild(Label("   Magic below"));
        rarity.AddChild(_magicBelow);
        rarity.AddChild(Label("Rare below"));
        rarity.AddChild(_rareBelow);
        foreach (OptionButton pick in new[] { _magicBelow, _rareBelow })
            foreach (int l in LevelSteps) pick.AddItem(l == 0 ? "off" : $"item level {l}");
        _magicBelow.ItemSelected += i => Change(() => AutoSell.Rules.MagicBelow = LevelSteps[(int)i]);
        _rareBelow.ItemSelected += i => Change(() => AutoSell.Rules.RareBelow = LevelSteps[(int)i]);
        page.AddChild(rarity);

        page.AddChild(Style.Label("Affixes: click to cycle  off → Sell (red) → Keep (green). An item with a Keep affix is never sold.", Style.Muted, 13));
        _affixGrid.AddThemeConstantOverride("h_separation", 6);
        _affixGrid.AddThemeConstantOverride("v_separation", 6);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 220) };
        _affixGrid.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_affixGrid);
        page.AddChild(scroll);

        var now = new HBoxContainer();
        now.AddThemeConstantOverride("separation", 10);
        _sellNow.Pressed += SellNowPressed;
        _sellNow.TooltipText = "Sells what the rules match among the rare items in the bank now (locked ones never). Asks once.";
        now.AddChild(_sellNow);
        _preview.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _preview.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        now.AddChild(_preview);
        page.AddChild(now);
        _history.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        page.AddChild(_history);
        return page;
    }

    static Label Label(string text)
    {
        Label l = Style.Label(text, Style.Muted);
        l.VerticalAlignment = VerticalAlignment.Center;
        return l;
    }

    void Change(Action set)
    {
        if (_painting) return;
        set();
        AutoSell.Save(_bank);
        PaintAuto();
    }

    void ShowAutoPage(bool on)
    {
        if (on && _bank != null && _game != null) AutoSell.Bind(_game, _bank);
        _autoPage.Visible = on;
        _listPage.Visible = !on;
        _autoButton.Text = on ? "Back to the list" : "Auto-sell";
        _nowArmed = false;
        if (on) PaintAuto(); else Reload();
    }

    /// <summary>Draws the rules as they are saved, the preview of the bank and the last sales.</summary>
    void PaintAuto()
    {
        if (!_autoPage.Visible) return;
        _painting = true;
        AutoRules r = AutoSell.Rules;
        _autoOn.ButtonPressed = r.On;
        _allMagic.ButtonPressed = r.AllMagic;
        _allRare.ButtonPressed = r.AllRare;
        _magicBelow.Select(Math.Max(0, Array.IndexOf(LevelSteps, r.MagicBelow)));
        _rareBelow.Select(Math.Max(0, Array.IndexOf(LevelSteps, r.RareBelow)));
        if (_affixButtons.Count == 0)
            foreach (Gear.Affix a in Gear.Catalog())
            {
                var b = new Button { Text = a.Text, TooltipText = $"{a.Name}: click to cycle off / Sell / Keep", ClipText = true,
                    CustomMinimumSize = new Vector2(205, 0), FocusMode = Control.FocusModeEnum.None };
                b.AddThemeFontSizeOverride("font_size", 13);
                string id = a.Id;
                b.Pressed += () => CycleAffix(id);
                _affixButtons[id] = b;
                _affixGrid.AddChild(b);
            }
        foreach (var (id, b) in _affixButtons)
        {
            bool sell = r.SellAffixes.Contains(id), keep = r.KeepAffixes.Contains(id);
            Color bg = sell ? new Color(0.42f, 0.13f, 0.12f) : keep ? Style.GoodDim : Style.Control;
            Color line = sell ? Style.Bad : keep ? Style.Good : Style.Line;
            b.AddThemeStyleboxOverride("normal", Style.Box(bg, 5, line, 1, 8, 4));
            b.AddThemeStyleboxOverride("hover", Style.Box(bg.Lightened(0.12f), 5, Style.Accent, 1, 8, 4));
            b.AddThemeStyleboxOverride("pressed", Style.Box(bg, 5, line, 1, 8, 4));
            b.AddThemeColorOverride("font_color", sell ? new Color(1f, 0.78f, 0.75f) : keep ? new Color(0.78f, 1f, 0.8f) : Style.Text);
        }
        _painting = false;

        if (_bank != null)
        {
            var matching = AutoSell.Matching(_bank);
            long gold = matching.Sum(m => m.Entry.Price * m.Entry.Quantity);
            _preview.Text = matching.Count == 0 ? "Nothing in the bank matches the rules now."
                : $"In the bank now: {matching.Count} item(s) match, about {gold:N0} gold: "
                  + string.Join(", ", matching.Take(5).Select(m => $"{Bank.Name(m.Entry.Item)} ({m.Reason})")) + (matching.Count > 5 ? ", ..." : "");
            _sellNow.Disabled = matching.Count == 0;
            _sellNow.Text = _nowArmed ? $"Click again: sell {matching.Count}" : "Sell matching items now";
        }
        _history.Text = AutoSell.History.Count == 0 ? (r.On ? "Auto-sell is on. Nothing sold yet." : "Auto-sell is off.")
            : $"Auto-sold this session: {AutoSell.SoldItems:N0} for {Bank.Money(AutoSell.SoldFor)}. Last: " + string.Join(", ", AutoSell.History.Take(6));
    }

    void CycleAffix(string id)
    {
        AutoRules r = AutoSell.Rules;
        if (r.SellAffixes.Remove(id)) r.KeepAffixes.Add(id);
        else if (!r.KeepAffixes.Remove(id)) r.SellAffixes.Add(id);
        AutoSell.Save(_bank);
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
            PaintAuto();
            _tree.CreateTimer(5).Timeout += () => { if (_nowArmed) { _nowArmed = false; PaintAuto(); } };
            return;
        }
        _nowArmed = false;
        var (sold, paid) = AutoSell.Sell(_bank, matching.Select(m => (m.Entry.Item, Bank.Name(m.Entry.Item), m.Reason)).ToList());
        Say($"Sold {sold:N0} rare item(s) for {Bank.Money(paid)}.");
        PaintAuto();
    }

    // ---------------------------------------------------------------- for tests
    public void PressAutoPage(bool on) => ShowAutoPage(on);
    public bool AutoPageShown => _autoPage.Visible;
    public string AutoPreview => _preview.Text;
    public void PressSellNow() => SellNowPressed();
    public int AffixButtonCount => _affixButtons.Count;
}
