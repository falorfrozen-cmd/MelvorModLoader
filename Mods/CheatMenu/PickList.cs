using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CheatMenu;

/// <summary>
/// One entry of a <see cref="PickList{T}"/>. <paramref name="Key"/> must be stable (an id, never a list position);
/// <paramref name="Group"/> and <paramref name="Subgroup"/> are display texts, "" for none (entries without a group
/// sit on top). A disabled entry cannot be picked; a muted one is drawn dimmed (locked for now) but can be.
/// <paramref name="Tint"/> colours the name (a rare item's rarity colour, for example), dimmed when disabled or muted.
/// </summary>
internal sealed record PickEntry<T>(
    string Key, string Text, T Value,
    string Group = "", string Subgroup = "",
    Texture2D? Icon = null, Texture2D? GroupIcon = null,
    string Hint = "", string Tooltip = "",
    bool Enabled = true, bool Muted = false, string Keywords = "",
    Color? Tint = null);

/// <summary>
/// "Pick one of many": a button that opens a popup with a search box on top and the entries grouped below,
/// built from stock Godot nodes (mods cannot subclass nodes with virtual overrides). Typing filters (every word
/// must match the name, group or keywords), the arrows move, Enter picks, Escape closes. Groups fold; a long list
/// opens with only the branch of the current choice unfolded. It is <c>internal</c> on purpose: the file is linked
/// into several mods, and a public type would exist twice for anything that references two of them.
/// </summary>
/// <summary>The few words the list writes itself; a mod with a language option puts its own lookup here.</summary>
internal static class PickListText
{
    public static Func<string, string> T = s => s;
    public static string F(string format, params object[] args) => string.Format(T(format), args);
}

internal sealed class PickList<T>
{
    const int FoldAbove = 40, MinWidth = 380, MaxWidth = 560, MaxHeight = 440, Margin = 8;

    readonly Button _button;
    readonly Label _arrow;
    readonly PopupPanel _popup = new() { Name = "PickListPopup", Transparent = false };
    readonly LineEdit _search;
    readonly Tree _tree;
    readonly Label _count;
    readonly string _placeholder;
    List<PickEntry<T>> _entries = new();
    readonly Dictionary<string, TreeItem> _leaves = new();
    readonly Dictionary<string, bool> _folded = new();   // group path -> folded, remembered while the window lives
    Dictionary<string, bool>? _foldedBeforeSearch;
    string _filter = "";
    ulong _hiddenAt;
    string _missingText = "";

    public PickList(string placeholder = "Choose...", Theme? theme = null)
    {
        _placeholder = placeholder;
        _button = new Button
        {
            Text = placeholder, Alignment = HorizontalAlignment.Left, ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new Vector2(120, 0),
        };
        _button.AddThemeConstantOverride("icon_max_width", 20);
        _arrow = new Label { Text = "▾", MouseFilter = Godot.Control.MouseFilterEnum.Ignore, VerticalAlignment = VerticalAlignment.Center };
        _arrow.SetAnchorsAndOffsetsPreset(Godot.Control.LayoutPreset.RightWide);
        _arrow.OffsetLeft = -22;
        _arrow.OffsetRight = -8;
        _button.AddChild(_arrow);
        // Room for the arrow: the button's own boxes, with a wider right margin.
        _button.Ready += () =>
        {
            if (_tile) return;
            foreach (string state in new[] { "normal", "hover", "pressed", "hover_pressed", "disabled", "focus" })
                if (_button.GetThemeStylebox(state)?.Duplicate() is StyleBox box)
                {
                    box.ContentMarginRight = Math.Max(box.ContentMarginRight, 0) + 22;
                    _button.AddThemeStyleboxOverride(state, box);
                }
        };
        _button.Pressed += () => { if (!_popup.Visible && Time.GetTicksMsec() - _hiddenAt > 150) Open(); };
        _button.TreeExiting += Close;
        _button.VisibilityChanged += () => { if (!_button.IsVisibleInTree()) Close(); };

        if (theme != null) _popup.Theme = theme;
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        _search = new LineEdit { PlaceholderText = PickListText.T("Type to search..."), ClearButtonEnabled = true };
        _search.TextChanged += text => ApplyFilter(text);
        _search.GuiInput += OnSearchKey;
        _tree = new Tree
        {
            Columns = 2, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, AllowReselect = true,
            SizeFlagsVertical = Godot.Control.SizeFlags.ExpandFill, FocusMode = Godot.Control.FocusModeEnum.None,
        };
        _tree.SetColumnExpand(0, true);
        _tree.SetColumnExpand(1, false);
        _tree.SetColumnCustomMinimumWidth(1, 96);
        _tree.ItemMouseSelected += (_, _) => OnClick();
        _tree.ItemCollapsed += item => { if (_filter == "" && item.GetMetadata(1).AsString() is { Length: > 0 } path) _folded[path] = item.Collapsed; };
        _count = new Label { Text = "" };
        _count.AddThemeColorOverride("font_color", Style.Muted);
        _count.AddThemeFontSizeOverride("font_size", 12);
        root.AddChild(_search);
        root.AddChild(_tree);
        root.AddChild(_count);
        _popup.AddChild(root);
        _popup.PopupHide += () => { _hiddenAt = Time.GetTicksMsec(); if (_button.IsInsideTree() && _button.IsVisibleInTree()) _button.GrabFocus(); };
        _button.AddChild(_popup);
    }

    /// <summary>Opt-in: the width of the hint column on the right (96 by default).</summary>
    public int HintWidth { set => _tree.SetColumnCustomMinimumWidth(1, value); }

    /// <summary>The button to put into the window.</summary>
    public Button Control => _button;
    public bool IsOpen => _popup.Visible;
    public int Count => _entries.Count;
    public string? SelectedKey { get; private set; }
    public PickEntry<T>? Selected => SelectedKey != null ? _entries.FirstOrDefault(e => e.Key == SelectedKey) : null;
    public string ButtonText => _button.Text;
    /// <summary>Raised when the player picks an entry (not when the code sets the selection).</summary>
    public event Action<PickEntry<T>>? Picked;

    // ---------------------------------------------------------------- filling

    /// <summary>
    /// Puts these entries in, in this order (groups appear in the order of their first entry). The current choice
    /// is kept when its key is still there, or <paramref name="selectKey"/> is chosen; nothing is raised.
    /// </summary>
    public void SetEntries(IReadOnlyList<PickEntry<T>> entries, string? selectKey = null)
    {
        string? keep = selectKey ?? SelectedKey;
        _entries = entries.GroupBy(e => e.Key).Select(g => g.First()).ToList();   // a key is shown once
        Rebuild();
        if (keep != null && _entries.Any(e => e.Key == keep)) { SelectedKey = keep; _missingText = ""; }
        else if (selectKey == null && _missingText != "") { }   // a choice shown as "not available now" stays shown
        else { SelectedKey = null; _missingText = ""; }
        PaintButton();
    }

    /// <summary>Sets the choice from code (nothing is raised). False when no entry has that key.</summary>
    public bool Select(string? key)
    {
        _missingText = "";
        if (key == null || !_entries.Any(e => e.Key == key)) { SelectedKey = null; PaintButton(); return false; }
        SelectedKey = key;
        PaintButton();
        return true;
    }

    /// <summary>
    /// Shows a choice that is not in the list right now (a saved step whose target the game hides at the moment),
    /// so it is not silently replaced by something else.
    /// </summary>
    public void ShowMissing(string key, string text)
    {
        SelectedKey = key;
        _missingText = text;
        PaintButton();
    }

    bool _tile;
    Texture2D? _emptyIcon;

    /// <summary>
    /// Opt-in: the button becomes a square picture tile (an equipment slot): the chosen entry's icon, or
    /// <paramref name="emptyIcon"/> dimmed when nothing is chosen; no text and no arrow, the name is in the tooltip.
    /// </summary>
    public void MakeTile(float size, Texture2D? emptyIcon)
    {
        _tile = true;
        _emptyIcon = emptyIcon;
        _arrow.Visible = false;
        _button.CustomMinimumSize = new Vector2(size, size);
        _button.ExpandIcon = true;
        _button.IconAlignment = HorizontalAlignment.Center;
        _button.VerticalIconAlignment = VerticalAlignment.Center;
        _button.AddThemeConstantOverride("icon_max_width", (int)(size - 14));
        PaintButton();
    }

    void PaintButton()
    {
        PickEntry<T>? e = Selected;
        if (_tile)
        {
            bool none = e == null || e.Icon == null;
            _button.Text = "";
            _button.Icon = none ? _emptyIcon : e!.Icon;
            _button.AddThemeColorOverride("icon_normal_color", new Color(1, 1, 1, none ? 0.3f : 1f));
            _button.AddThemeColorOverride("icon_hover_color", new Color(1, 1, 1, none ? 0.55f : 1f));
            // (on a tile the placeholder names the slot)
            _button.TooltipText = e != null ? $"{_placeholder}: {e.Text}" + (e.Tooltip != "" ? "\n" + e.Tooltip : e.Hint != "" ? "\n" + e.Hint : "") : $"{_placeholder}: nothing";
            return;
        }
        if (e != null)
        {
            _missingText = "";
            _button.Text = e.Text;
            _button.Icon = e.Icon;
            _button.TooltipText = string.Join("  >  ", new[] { e.Group, e.Subgroup, e.Text }.Where(s => s != "")) + (e.Tooltip != "" ? "\n" + e.Tooltip : e.Hint != "" ? "\n" + e.Hint : "");
            _button.AddThemeColorOverride("font_color", e.Muted ? Style.Muted : e.Tint ?? Style.Text);
        }
        else
        {
            _button.Text = _missingText != "" ? _missingText + PickListText.T("  (not available now)") : _placeholder;
            _button.Icon = null;
            _button.TooltipText = _missingText != "" ? PickListText.T("This choice is not offered by the game right now.") : "";
            _button.AddThemeColorOverride("font_color", Style.Muted);
        }
    }

    static string Norm(string s) => s.ToLowerInvariant();

    /// <summary>The tree is built once per fill; searching only hides and shows rows.</summary>
    void Rebuild()
    {
        _tree.Clear();
        _leaves.Clear();
        TreeItem top = _tree.CreateItem();
        var groups = new Dictionary<string, TreeItem>();
        bool fold = _entries.Count > FoldAbove;
        foreach (PickEntry<T> e in _entries)
        {
            TreeItem parent = top;
            if (e.Group != "")
            {
                parent = Header(top, groups, e.Group, e.Group, e.GroupIcon, 0);
                if (e.Subgroup != "") parent = Header(parent, groups, e.Group + "\u001f" + e.Subgroup, e.Subgroup, null, 1);
            }
            TreeItem row = _tree.CreateItem(parent);
            row.SetText(0, e.Text);
            if (e.Icon != null) { row.SetIcon(0, e.Icon); row.SetIconMaxWidth(0, 20); }
            row.SetText(1, e.Hint);
            row.SetTextAlignment(1, HorizontalAlignment.Right);
            row.SetCustomColor(1, Style.Muted);
            if (!e.Enabled) { row.SetCustomColor(0, new Color(e.Tint ?? Style.Muted, 0.55f)); row.SetSelectable(0, false); row.SetSelectable(1, false); }
            else if (e.Muted) row.SetCustomColor(0, e.Tint is { } dim ? new Color(dim, 0.7f) : Style.Muted);
            else if (e.Tint is { } tint) row.SetCustomColor(0, tint);
            if (e.Tooltip != "") { row.SetTooltipText(0, e.Tooltip); row.SetTooltipText(1, e.Tooltip); }
            row.SetMetadata(0, e.Key);
            row.SetMetadata(1, "");
            _leaves[e.Key] = row;
        }
        // Counts on the headers, and the starting folds.
        foreach (TreeItem header in groups.Values)
        {
            int n = Leaves(header).Count();
            header.SetText(1, n.ToString());
            string path = header.GetMetadata(1).AsString();
            header.Collapsed = _folded.TryGetValue(path, out bool was) ? was : fold;
        }
        _foldedBeforeSearch = null;
        if (_filter != "") ApplyFilter(_filter, keepFolds: true);
        else _count.Text = PickListText.F("{0} to choose from", _entries.Count);
    }

    TreeItem Header(TreeItem parent, Dictionary<string, TreeItem> groups, string path, string text, Texture2D? icon, int depth)
    {
        if (groups.TryGetValue(path, out TreeItem? have)) return have;
        TreeItem h = _tree.CreateItem(parent);
        h.SetText(0, text);
        if (icon != null) { h.SetIcon(0, icon); h.SetIconMaxWidth(0, 18); }
        h.SetCustomColor(0, depth == 0 ? Style.Accent : Style.Text);
        h.SetCustomColor(1, Style.Muted);
        h.SetTextAlignment(1, HorizontalAlignment.Right);
        h.SetMetadata(0, "");
        h.SetMetadata(1, path);
        groups[path] = h;
        return h;
    }

    static IEnumerable<TreeItem> Leaves(TreeItem item)
    {
        foreach (TreeItem child in item.GetChildren())
        {
            if (child.GetMetadata(0).AsString() != "") yield return child;
            else foreach (TreeItem deeper in Leaves(child)) yield return deeper;
        }
    }

    // ---------------------------------------------------------------- opening and placing

    public void Open()
    {
        if (!_button.IsInsideTree()) return;
        Rect2 button = _button.GetGlobalRect();
        Rect2 screen = _button.GetViewportRect();
        // A control inside a CanvasLayer is drawn through the layer's transform; the popup lives in the viewport.
        Transform2D canvas = _button.GetCanvasTransform();
        button = new Rect2(canvas * button.Position, button.Size * canvas.Scale);
        float below = screen.End.Y - button.End.Y - Margin, above = button.Position.Y - screen.Position.Y - Margin;
        bool under = below >= 360 || below >= above;   // a list squeezed into a thin strip at the bottom opens upwards
        int height = (int)Math.Max(160, Math.Min(MaxHeight, under ? below : above));
        int width = (int)Math.Min(Math.Max(button.Size.X, MinWidth), Math.Max(200, Math.Min(MaxWidth, screen.Size.X - 2 * Margin)));
        float x = Math.Max(screen.Position.X + Margin, Math.Min(button.Position.X, screen.End.X - width - Margin));
        float y = under ? button.End.Y : button.Position.Y - height;
        _search.Text = "";
        _filter = "";
        RestoreFolds();
        ShowAll();
        // The popup is shown at the next frame: opened and closed within one frame, Godot left it unable to open
        // again. A Close() in between cancels the pending opening.
        var rect = new Rect2I((int)x, (int)y, width, height);
        int token = ++_openToken;
        Callable.From(() =>
        {
            if (token != _openToken || !GodotObject.IsInstanceValid(_popup) || !_button.IsVisibleInTree()) return;
            _popup.Popup(rect);
            // Show where the current choice is.
            if (SelectedKey != null && _leaves.TryGetValue(SelectedKey, out TreeItem? current))
            {
                for (TreeItem? p = current.GetParent(); p != null; p = p.GetParent()) p.Collapsed = false;
                current.Select(0);
                _tree.ScrollToItem(current, true);
            }
            else _tree.DeselectAll();
            _search.GrabFocus();
        }).CallDeferred();
    }

    int _openToken;

    public void Close()
    {
        _openToken++;   // an opening still waiting for the next frame does not happen
        if (GodotObject.IsInstanceValid(_popup) && _popup.Visible) _popup.Hide();
    }

    /// <summary>For tests: where the popup is, in the viewport.</summary>
    public Rect2I PopupRect => new(_popup.Position, _popup.Size);

    // ---------------------------------------------------------------- searching

    void ShowAll()
    {
        foreach (TreeItem row in _leaves.Values) row.Visible = true;
        foreach (TreeItem h in Headers(_tree.GetRoot())) h.Visible = true;
        _count.Text = PickListText.F("{0} to choose from", _entries.Count);
    }

    static IEnumerable<TreeItem> Headers(TreeItem? item)
    {
        if (item == null) yield break;
        foreach (TreeItem child in item.GetChildren())
            if (child.GetMetadata(0).AsString() == "") { yield return child; foreach (TreeItem deeper in Headers(child)) yield return deeper; }
    }

    void RestoreFolds()
    {
        if (_foldedBeforeSearch == null) return;
        foreach (TreeItem h in Headers(_tree.GetRoot()))
            if (_foldedBeforeSearch.TryGetValue(h.GetMetadata(1).AsString(), out bool was)) h.Collapsed = was;
        _foldedBeforeSearch = null;
    }

    /// <summary>Shows only the entries every typed word matches; matching groups unfold while searching.</summary>
    void ApplyFilter(string text, bool keepFolds = false)
    {
        _filter = text.Trim();
        if (_filter == "") { RestoreFolds(); ShowAll(); _tree.DeselectAll(); return; }
        if (_foldedBeforeSearch == null && !keepFolds)
            _foldedBeforeSearch = Headers(_tree.GetRoot()).ToDictionary(h => h.GetMetadata(1).AsString(), h => h.Collapsed);
        string[] words = Norm(_filter).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int shown = 0;
        foreach (PickEntry<T> e in _entries)
        {
            string hay = Norm($"{e.Text} {e.Group} {e.Subgroup} {e.Keywords}");
            bool match = words.All(hay.Contains);
            _leaves[e.Key].Visible = match;
            if (match) shown++;
        }
        foreach (TreeItem h in Headers(_tree.GetRoot()).Reverse())
        {
            h.Visible = Leaves(h).Any(l => l.Visible);
            if (h.Visible) h.Collapsed = false;
        }
        _count.Text = shown == 0 ? PickListText.T("No matches") : PickListText.F("{0} of {1}", shown, _entries.Count);
        // The first match is ready for Enter.
        TreeItem? first = Pickable().FirstOrDefault();
        if (first != null) { first.Select(0); _tree.ScrollToItem(first); } else _tree.DeselectAll();
    }

    /// <summary>The rows that can be picked and can be seen now, top to bottom.</summary>
    IEnumerable<TreeItem> Pickable()
    {
        foreach (PickEntry<T> e in _entries)
        {
            TreeItem row = _leaves[e.Key];
            if (!e.Enabled || !row.Visible) continue;
            bool open = true;
            for (TreeItem? p = row.GetParent(); p != null && p.GetParent() != null; p = p.GetParent()) if (p.Collapsed || !p.Visible) open = false;
            if (open) yield return row;
        }
    }

    void OnSearchKey(InputEvent ev)
    {
        if (ev is not InputEventKey { Pressed: true } key) return;
        switch (key.Keycode)
        {
            case Key.Down: case Key.Up:
            {
                List<TreeItem> rows = Pickable().ToList();
                if (rows.Count == 0) break;
                int at = rows.IndexOf(_tree.GetSelected());
                at = at < 0 ? (key.Keycode == Key.Down ? 0 : rows.Count - 1) : Math.Clamp(at + (key.Keycode == Key.Down ? 1 : -1), 0, rows.Count - 1);
                rows[at].Select(0);
                _tree.ScrollToItem(rows[at]);
                break;
            }
            case Key.Enter: case Key.KpEnter:
                if (_tree.GetSelected() is { } chosen && chosen.GetMetadata(0).AsString() is { Length: > 0 } k) Commit(k);
                break;
            case Key.Escape:
                Close();
                break;
            default:
                return;
        }
        _search.AcceptEvent();
    }

    void OnClick()
    {
        TreeItem? item = _tree.GetSelected();
        if (item == null) return;
        string key = item.GetMetadata(0).AsString();
        if (key == "")
        {
            // A group: fold or unfold it.
            item.Collapsed = !item.Collapsed;
            item.Deselect(0);
            return;
        }
        Commit(key);
    }

    void Commit(string key)
    {
        PickEntry<T>? e = _entries.FirstOrDefault(x => x.Key == key);
        if (e == null || !e.Enabled) return;
        SelectedKey = key;
        _missingText = "";
        PaintButton();
        Close();
        Picked?.Invoke(e);
    }

    // ---------------------------------------------------------------- for tests

    /// <summary>For tests: type into the search box (no waiting).</summary>
    public void SetFilter(string text) { _search.Text = text; ApplyFilter(text); }
    /// <summary>For tests: the keys of the entries that match the search, in order.</summary>
    public List<string> VisibleKeys => _entries.Where(e => _leaves[e.Key].Visible).Select(e => e.Key).ToList();
    /// <summary>For tests: the group texts that are shown.</summary>
    public List<string> VisibleGroups => Headers(_tree.GetRoot()).Where(h => h.Visible).Select(h => h.GetText(0)).ToList();
    /// <summary>For tests: which groups are folded right now.</summary>
    public List<string> FoldedGroups => Headers(_tree.GetRoot()).Where(h => h.Collapsed).Select(h => h.GetText(0)).ToList();
    public string CountText => _count.Text;
    /// <summary>For tests: picks the way a click does; false when there is no such entry or it cannot be picked.</summary>
    public bool Pick(string key)
    {
        PickEntry<T>? e = _entries.FirstOrDefault(x => x.Key == key);
        if (e == null || !e.Enabled) return false;
        Commit(key);
        return true;
    }
    /// <summary>For tests: presses a key in the search box.</summary>
    public void PressKey(Key key) => OnSearchKey(new InputEventKey { Keycode = key, Pressed = true });
    public string? HighlightedKey => _tree.GetSelected()?.GetMetadata(0).AsString() is { Length: > 0 } k ? k : null;
    public PickEntry<T>? Entry(string key) => _entries.FirstOrDefault(e => e.Key == key);
}
