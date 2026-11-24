using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace Facelift;

/// <summary>
/// The tidier screens: small changes to the game's own pages, each one switchable. Everything added is Facelift's
/// own node (removed again when switched off); everything hidden is shown again.
/// </summary>
sealed class Layout
{
    readonly Action<string> _log;
    readonly Settings _settings;
    readonly Dictionary<ulong, Action> _hidden = new();      // game nodes Facelift hid: how to show them again
    readonly List<Node> _added = new();                      // Facelift's own nodes
    readonly HashSet<ulong> _shopsOpened = new();
    BankSummary? _summary;

    public Layout(Action<string> log, Settings settings) { _log = log; _settings = settings; }

    /// <summary>Puts every page back as the game had it.</summary>
    public void Clear()
    {
        foreach (var undo in _hidden.Values) { try { undo(); } catch { } }
        _hidden.Clear();
        foreach (Node n in _added) if (GodotObject.IsInstanceValid(n)) { n.GetParent()?.RemoveChild(n); n.QueueFree(); }
        _added.Clear();
        _summary = null;
    }

    int _ticks;
    Node? _manager, _shopView, _emptyPanel;

    static bool Alive(Node? n) => n != null && GodotObject.IsInstanceValid(n) && n.IsInsideTree();

    /// <summary>Four times a second; the searches through the whole page only once a second.</summary>
    public void Tick(SceneTree tree)
    {
        Node? view = tree.Root.GetNodeOrNull("Main/ApplicationViewContainer/GameView");
        if (view == null) return;
        bool look = ++_ticks % 4 == 0;
        if (look)
        {
            if (!Alive(_manager)) _manager = tree.Root.FindChildren("*", "Node", true, false).FirstOrDefault(n => n.GetType().Name == "NotificationManager");
            if (!Alive(_shopView)) _shopView = view.FindChildren("*", "VBoxContainer", true, false).FirstOrDefault(n => n.GetType().Name == "ShopView");
            if (!Alive(_emptyPanel)) _emptyPanel = view.FindChild("NoItemSelectedPanel", true, false);
        }
        try { if (_settings.TidyTopBar && look) TopBar(view); } catch (Exception e) { Once("top bar", e); }
        try { if (_settings.ShopOpensCategory && Alive(_shopView)) Shop(_shopView!); } catch (Exception e) { Once("shop", e); }
        try { if (_settings.BankSummary && Alive(_emptyPanel)) Bank(view, (Control)_emptyPanel!); } catch (Exception e) { Once("bank", e); }
        try { if (_settings.FewerNotices && Alive(_manager)) Notices(_manager!); } catch (Exception e) { Once("notices", e); }
        try { if (_settings.HideEmptyRows && look) EmptyRows(view); } catch (Exception e) { Once("rows", e); }
    }

    readonly HashSet<string> _told = new();
    void Once(string what, Exception e) { if (_told.Add(what)) _log($"Could not tidy the {what}: {e.Message}"); }

    void Hide(CanvasItem node)
    {
        ulong id = node.GetInstanceId();
        if (!node.Visible) return;
        node.Visible = false;   // (again, when the game showed it once more)
        if (_hidden.ContainsKey(id)) return;
        _hidden[id] = () => { if (GodotObject.IsInstanceValid(node)) node.Visible = true; };
    }

    static IEnumerable<T> All<T>(Node root) where T : Node => root.FindChildren("*", typeof(T).Name, true, false).OfType<T>();

    // ------------------------------------------------------------------ top bar

    /// <summary>The version text and the greyed "Cozy UI Mode - Coming soon" switch: nothing to do there yet.</summary>
    BaseButton? _cozyButton;

    void TopBar(Node view)
    {
        // the day the game's Cozy UI Mode works, its switch is no longer greyed out: show it again
        if (_cozyButton != null && GodotObject.IsInstanceValid(_cozyButton) && !_cozyButton.Disabled && _hidden.Remove(_cozyButton.GetInstanceId()))
            _cozyButton.Visible = true;
        // the version text lives on a layer of its own over the whole game
        if (view.GetTree().Root.GetNodeOrNull("Main/GameVersion/Container") is CanvasItem version) Hide(version);
        Node? header = view.FindChild("Header", true, false);
        if (header == null) return;
        foreach (Label l in All<Label>(header))
        {
            if (!l.IsVisibleInTree()) continue;
            if (l.Text.StartsWith("Melvor Idle 2: v", StringComparison.Ordinal)) Hide(l);
            else if (l.Text.Contains("Coming soon", StringComparison.OrdinalIgnoreCase))
            {
                // the whole switch (eye, "Cozy UI Mode", "Coming soon"): its button, while it is still greyed out
                Node? p = l.GetParent();
                while (p != null && p is not BaseButton && p != header) p = p.GetParent();
                if (p is BaseButton { Disabled: true } b) { Hide(b); _cozyButton = b; }
                else if (l.GetParent() is Control box && box.GetParent() is Control row) Hide(row);
            }
        }
    }

    // ------------------------------------------------------------------ shop

    /// <summary>The shop opens on a category (the first one in its list) instead of an empty page.</summary>
    void Shop(Node n)
    {
        {
            if (n is not Control shopView || !shopView.IsVisibleInTree()) return;
            object? shop = n.GetType().GetProperty("Shop", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(n);
            if (shop == null) return;
            ulong key = n.GetInstanceId();
            object? selected = shop.GetType().GetProperty("SelectedCategory")?.GetValue(shop);
            if (selected != null) { _shopsOpened.Add(key); return; }
            if (_shopsOpened.Contains(key)) return;   // the player went back to "no category" on purpose
            object? select = n.GetType().GetProperty("CategorySelect", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(n);
            if (select?.GetType().GetProperty("CategoryButtons", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(select) is not IDictionary buttons) return;
            object? first = null; int best = int.MaxValue;
            foreach (DictionaryEntry e in buttons)
                if (e.Value is Control { Visible: true } b && b.GetIndex() < best) { best = b.GetIndex(); first = e.Key; }
            if (first == null) return;
            shop.GetType().GetMethod("SelectCategory")?.Invoke(shop, new[] { first });
            _shopsOpened.Add(key);
        }
    }

    // ------------------------------------------------------------------ bank

    /// <summary>With nothing selected, the bank's right panel shows a short summary instead of a red "No item selected.".</summary>
    void Bank(Node view, Control empty)
    {
        if (!empty.IsVisibleInTree() && !_hidden.ContainsKey(empty.GetInstanceId())) return;
        if (empty.GetParent() is Control host && !host.IsVisibleInTree()) return;
        if (_summary == null || !GodotObject.IsInstanceValid(_summary.Root) || _summary.Root.GetParent() != empty.GetParent())
        {
            if (empty.GetParent() is not Control panel) return;
            _summary = new BankSummary();
            panel.AddChild(_summary.Root);
            panel.MoveChild(_summary.Root, empty.GetIndex() + 1);
            _added.Add(_summary.Root);
        }
        bool none = empty.Visible || _hidden.ContainsKey(empty.GetInstanceId()) && !AnyItemShown(empty.GetParent());
        if (empty.Visible) Hide(empty);
        _summary.Root.Visible = none;
        if (none) _summary.Fill(view);
    }

    /// <summary>True when the panel shows an item (any other visible child than the summary and the empty note).</summary>
    bool AnyItemShown(Node? panel)
    {
        if (panel == null) return false;
        foreach (Node c in panel.GetChildren())
            if (c is Control { Visible: true } k && k != _summary?.Root && k.Name != "NoItemSelectedPanel") return true;
        return false;
    }

    // ------------------------------------------------------------------ notifications

    /// <summary>At most a few notifications per corner at once; older ones leave early (their rewards are in the bank already).</summary>
    void Notices(Node manager)
    {
        var groups = new Dictionary<string, List<Control>>();
        foreach (Node c in manager.GetChildren())
        {
            if (c is not Control note || c.GetType().GetProperty("State")?.GetValue(c)?.ToString() != "Fired") continue;
            string place = $"{c.GetType().GetProperty("VerticalPosition")?.GetValue(c)}-{c.GetType().GetProperty("HorizontalPosition")?.GetValue(c)}";
            if (!groups.TryGetValue(place, out var list)) groups[place] = list = new();
            list.Add(note);
        }
        foreach (var (place, list) in groups)
        {
            int keep = place.EndsWith("Middle") ? 1 : 4;   // banners in the middle: one at a time; side toasts: four
            for (int i = 0; i < list.Count - keep; i++)
                list[i].GetType().GetMethod("Dismiss", Type.EmptyTypes)?.Invoke(list[i], null);
        }
    }

    // ------------------------------------------------------------------ empty rows

    /// <summary>"Special Abilities: None" and "Passives: None" on enemy cards: shown only when there is something.</summary>
    void EmptyRows(Node view)
    {
        foreach (Label l in All<Label>(view))
        {
            if (l.Text != "None" || !l.IsVisibleInTree() || l.GetParent() is not Control box) continue;
            bool titled = box.GetChildren().OfType<Label>().Any(t => t.Text is "Special Abilities" or "Passives");
            if (titled) Hide(box);
        }
    }
}

/// <summary>The bank's right panel when nothing is selected: space, value, kinds, and a hint.</summary>
sealed class BankSummary
{
    public readonly MarginContainer Root = new() { Name = "FaceliftBankSummary" };
    readonly VBoxContainer _box = new();
    readonly Label _space = new(), _value = new(), _tab = new(), _hint = new();
    readonly ProgressBar _bar = new() { ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };

    public BankSummary()
    {
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" }) Root.AddThemeConstantOverride(side, 14);
        Root.AddChild(_box);
        _box.AddThemeConstantOverride("separation", 12);
        var title = new Label { Text = "Bank Summary", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Georgia", "Cambria", "Times New Roman" }, FontWeight = 700 });
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", Palette.Ivory);
        _box.AddChild(title);
        _box.AddChild(Line());
        foreach (var (name, label) in new[] { ("Bank Space", _space), ("Bank Value", _value), ("Tab Value", _tab) })
        {
            var row = new HBoxContainer();
            var key = new Label { Text = name, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            key.AddThemeColorOverride("font_color", Palette.Muted);
            key.AddThemeFontSizeOverride("font_size", 16);
            label.AddThemeColorOverride("font_color", Palette.Ivory);
            label.AddThemeFontSizeOverride("font_size", 16);
            row.AddChild(key); row.AddChild(label);
            _box.AddChild(row);
        }
        var fill = new StyleBoxFlat { BgColor = Palette.GreenLine, CornerDetail = 1, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 };
        var back = new StyleBoxFlat { BgColor = Palette.Field, BorderColor = Palette.LineSoft, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1 };
        _bar.AddThemeStyleboxOverride("fill", fill);
        _bar.AddThemeStyleboxOverride("background", back);
        _box.AddChild(_bar);
        _box.AddChild(Line());
        _hint.Text = "Click an item to see it here.";
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.AddThemeColorOverride("font_color", Palette.Muted);
        _box.AddChild(_hint);
    }

    static Control Line() => new ColorRect { Color = Palette.LineSoft, CustomMinimumSize = new Vector2(0, 1) };

    /// <summary>Copies the numbers the bank shows at its top (so they are the game's own, never computed twice).</summary>
    public void Fill(Node view)
    {
        _space.Text = Next(view, "Bank Space");
        _value.Text = Next(view, "Bank Value");
        _tab.Text = Next(view, "Tab Value");
        string[] parts = _space.Text.Replace(",", "").Replace(".", "").Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && double.TryParse(parts[0], out double used) && double.TryParse(parts[1], out double max) && max > 0)
        {
            _bar.MaxValue = max;
            _bar.Value = used;
        }
    }

    static string Shown(Node n)
    {
        if (n is Label l) return l.Text;
        if (n is RichTextLabel r) return r.GetParsedText().Trim();
        foreach (Node c in n.GetChildren()) { string t = Shown(c); if (t.Length > 0) return t; }
        return "";
    }

    /// <summary>The text shown next to (after) the game's label with this caption.</summary>
    static string Next(Node view, string caption)
    {
        foreach (Node n in view.FindChildren("*", "Label", true, false))
        {
            if (n is not Label l || l.Text != caption || l.GetParent() is not Node box) continue;
            // the next control after the caption: a Label, a RichTextLabel (with an icon), or a box holding one
            for (int i = l.GetIndex() + 1; i < box.GetChildCount(); i++)
            {
                string t = Shown(box.GetChild(i));
                if (t.Length > 0) return t;
            }
            // the value may sit in the next box over
            if (box.GetParent() is Node row && box.GetIndex() + 1 < row.GetChildCount())
                foreach (Node c in row.GetChild(box.GetIndex() + 1).FindChildren("*", "Label", true, false))
                    if (c is Label v && v.Text.Length > 0) return v.Text;
        }
        return "-";
    }
}
