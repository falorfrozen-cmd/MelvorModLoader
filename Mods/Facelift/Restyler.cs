using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Facelift;

/// <summary>
/// The new look, laid over the game's own. The game draws its panels and buttons with a few hundred shared style
/// objects (one object serves every button of a kind, in every scene). Each one met is changed once, in place, and
/// what it was is written down; switching the look off puts every one back. Controls are only noted as they appear
/// and handled on the next frames, on the main thread, never while the game computes offline progress.
/// </summary>
sealed class Restyler
{
    static readonly string[] Slots =
    {
        "panel", "normal", "hover", "pressed", "disabled", "focus", "hover_pressed",
        "normal_mirrored", "hover_mirrored", "pressed_mirrored", "disabled_mirrored", "hover_pressed_mirrored",
        "background", "fill", "read_only", "tab_selected", "tab_unselected", "tab_hovered", "tab_disabled", "tab_focus",
        "tabbar_background", "scroll", "scroll_focus", "grabber", "grabber_highlight", "grabber_pressed", "slider",
        "grabber_area", "grabber_area_highlight", "title_panel", "title_collapsed_panel", "title_hover_panel",
        "title_collapsed_hover_panel", "embedded_border", "selected", "selected_focus", "hovered", "cursor", "separator",
    };

    /// <summary>Text styles that are titles: they get the serif.</summary>
    static readonly HashSet<string> TitleSettings = new()
    {
        "res://Assets/Styles/LabelSettings/game_header_page_title.tres",
        "res://Assets/Styles/LabelSettings/fs14_semibold_combat_title.tres",
        "res://Assets/Styles/LabelSettings/artisan_current_recipe_title.tres",
        "res://Assets/Styles/LabelSettings/label_settings_woodcutting_tree_name.tres",
        "res://Assets/Styles/LabelSettings/label_settings_mining_rock_name.tres",
        "res://Assets/Styles/LabelSettings/label_settings_fishing_spot_name.tres",
        "res://Assets/Styles/LabelSettings/fs20_bold_shop_title.tres",
        "res://Assets/Styles/LabelSettings/announcement_title.tres",
        "res://Assets/Styles/LabelSettings/artisan_category_header.tres",
        "res://Assets/Styles/LabelSettings/fs14_header.tres",
    };

    readonly Action<string> _log;
    readonly Dictionary<ulong, Action> _undo = new();          // style objects changed: how to put each back
    readonly Dictionary<ulong, Action> _nodeUndo = new();      // per-node changes (a title's own text style)
    readonly Queue<ulong> _pending = new();
    readonly HashSet<string> _unknown = new();
    readonly Dictionary<ulong, LabelSettings> _serifCopies = new();
    Font? _serif;

    public bool On { get; private set; }
    public int Changed => _undo.Count;
    public int Waiting => _pending.Count;
    public IReadOnlyCollection<string> Unknown => _unknown;

    public Restyler(Action<string> log) => _log = log;

    // ------------------------------------------------------------------ switching

    public void Enable(SceneTree tree)
    {
        if (On) return;
        On = true;
        // the shared files first: they serve scenes that are not open yet as well
        foreach (string dir in new[] { "res://Assets/UI/StyleBoxes", "res://Assets/Styles" }) LoadAll(dir);
        if (ProjectSettings.GetSetting("gui/theme/custom").AsString() is { Length: > 0 } themePath && ResourceLoader.Load<Theme>(themePath) is { } theme) Theme(theme);
        Note(tree.Root);
    }

    public void Disable()
    {
        if (!On) return;
        On = false;
        _pending.Clear();
        _later.Clear();
        foreach (var undo in _nodeUndo.Values) { try { undo(); } catch { } }
        _nodeUndo.Clear();
        foreach (var undo in _undo.Values) { try { undo(); } catch { } }
        _undo.Clear();
        _serifCopies.Clear();
    }

    /// <summary>A node and everything under it, to be looked at on the next frames.</summary>
    public void Note(Node node)
    {
        if (!On || ModWindow(node)) return;
        if (node is Control) _pending.Enqueue(node.GetInstanceId());
        foreach (Node child in node.GetChildren()) Note(child);
    }

    /// <summary>Called by the tree for every node that enters it: only noted here, handled in <see cref="Work"/>.</summary>
    public void Added(Node node)
    {
        if (On && node is Control && !ModWindow(node)) _pending.Enqueue(node.GetInstanceId());
    }

    /// <summary>
    /// Facelift dresses the game's own screens, not the mods' windows (the user's scope). A mod's window hangs straight
    /// under the root as a node made in code (no scene file); the game's screens come from scenes. Skipping them also keeps
    /// big mod windows quick: the Cheat Menu's thousands of controls took 14 s to restyle on its first opening.
    /// </summary>
    static bool ModWindow(Node node)
    {
        Node top = node;
        for (Node? up = node.GetParent(); up != null && up.GetParent() != null; up = up.GetParent()) top = up;
        return top != node.GetTree()?.Root && top.GetParent() is Window && top.SceneFilePath.Length == 0 && top.Name != "Main"
               && (top is CanvasLayer || top is Control);
    }

    /// <summary>Handles noted controls, at most <paramref name="budget"/> per call.</summary>
    public void Work(int budget)
    {
        while (On && budget-- > 0 && _pending.Count > 0)
        {
            if (GodotObject.InstanceFromId(_pending.Dequeue()) is not Control c || !GodotObject.IsInstanceValid(c) || !c.IsInsideTree()) continue;
            try { Control(c); }
            catch (Exception e) { _log($"Could not restyle {c.Name}: {e.Message}"); }
        }
    }

    // ------------------------------------------------------------------ what is changed

    void LoadAll(string dir)
    {
        foreach (string sub in DirAccess.GetDirectoriesAt(dir)) LoadAll(dir + "/" + sub);
        foreach (string f in DirAccess.GetFilesAt(dir))
        {
            string path = dir + "/" + f.Replace(".remap", "");
            if (!path.EndsWith(".tres")) continue;
            Resource? r = null;
            try { r = ResourceLoader.Load(path); } catch { }
            switch (r)
            {
                case StyleBox s: Box(s); break;
                case LabelSettings l: Text(l); break;
                case Theme t: Theme(t); break;
            }
        }
    }

    void Theme(Theme theme)
    {
        if (!First(theme)) return;
        var colors = new List<(string, string, Color)>();
        foreach (string type in theme.GetTypeList())
        {
            foreach (string n in theme.GetStyleboxList(type)) Box(theme.GetStylebox(n, type));
            foreach (string n in theme.GetColorList(type))
                if (n.Contains("font") && !n.Contains("outline") && !n.Contains("shadow"))
                    colors.Add((n, type, theme.GetColor(n, type)));
        }
        foreach (var (n, type, c) in colors) theme.SetColor(n, type, Palette.Map(c, text: true));
        Font? baseFont = theme.DefaultFont;
        if (EvenFigures && baseFont is FontFile file) theme.DefaultFont = Even(file);
        Remember(theme, () => { foreach (var (n, type, c) in colors) theme.SetColor(n, type, c); theme.DefaultFont = baseFont; });
    }

    void Control(Control c)
    {
        if (c.Theme != null) Theme(c.Theme);
        bool again = false;
        foreach (string slot in Slots)
            if (c.HasThemeStylebox(slot) && c.GetThemeStylebox(slot) is { } s && !Box(s, c)) again = true;
        if (again) _later.Enqueue(c.GetInstanceId());   // its size was not known yet: once more after layout

        if (c is Label label)
        {
            if (label.LabelSettings is { } ls)
            {
                Text(ls);
                bool title = TitleSettings.Contains(ls.ResourcePath) || (label.Name == "Title" && ls.FontSize >= 14)
                    || (label.Name.ToString() is "CharacterNameLabel" or "NameLabel" && ls.FontSize >= 18);   // the fighters' names in combat
                if (title) Serif(label, ls);
            }
            else Override(c, "font_color");
        }
        else if (c is Button or LinkButton) { Override(c, "font_color"); Override(c, "font_hover_color"); Override(c, "font_pressed_color"); }
        else if (c is RichTextLabel) Override(c, "default_color");
    }

    /// <summary>A colour the scene set on this one control (not in a shared style): moved into the palette.</summary>
    void Override(Control c, string name)
    {
        if (!c.HasThemeColorOverride(name)) return;
        ulong key = c.GetInstanceId() ^ (ulong)name.GetHashCode();
        if (_nodeUndo.ContainsKey(key)) return;
        Color old = c.GetThemeColor(name);
        Color now = Palette.Map(old, text: true);
        if (now == old) return;
        c.AddThemeColorOverride(name, now);
        _nodeUndo[key] = () => { if (GodotObject.IsInstanceValid(c) && c.GetThemeColor(name) == now) c.AddThemeColorOverride(name, old); };
    }

    void Serif(Label label, LabelSettings ls)
    {
        ulong key = label.GetInstanceId();
        if (_nodeUndo.ContainsKey(key)) return;
        _serif ??= MakeSerif();
        if (!_serifCopies.TryGetValue(ls.GetInstanceId(), out var copy))
        {
            copy = (LabelSettings)ls.Duplicate();
            copy.Font = _serif;
            copy.FontSize = Math.Max(ls.FontSize + 4, 18);   // section titles stand out (Codex, round 4)
            copy.ShadowColor = new Color(0, 0, 0, 0.6f);
            _serifCopies[ls.GetInstanceId()] = copy;
        }
        label.LabelSettings = copy;
        _nodeUndo[key] = () => { if (GodotObject.IsInstanceValid(label) && label.LabelSettings == copy) label.LabelSettings = ls; };
    }

    static Font MakeSerif() => new SystemFont { FontNames = new[] { "Georgia", "Cambria", "Constantia", "Times New Roman" }, FontWeight = 700, Antialiasing = TextServer.FontAntialiasing.Gray };

    void Text(LabelSettings ls)
    {
        if (!First(ls)) return;
        Color font = ls.FontColor, shadow = ls.ShadowColor;
        Font? face = ls.Font;
        ls.FontColor = Palette.Map(font, text: true);
        if (shadow.A > 0) ls.ShadowColor = new Color(0, 0, 0, Math.Min(shadow.A, 0.55f));
        if (EvenFigures && face is FontFile file) ls.Font = Even(file);
        Remember(ls, () => { ls.FontColor = font; ls.ShadowColor = shadow; ls.Font = face; });
    }

    public static bool NoGaps = System.Environment.GetEnvironmentVariable("FACELIFT_NOGAPS") == "1";
    public readonly List<string> Gaps = new();

    /// <summary>Figures of one width in the game's fonts.</summary>
    public static bool EvenFigures = System.Environment.GetEnvironmentVariable("FACELIFT_EVEN") != "0";

    readonly Dictionary<ulong, FontVariation> _even = new();

    /// <summary>The same font with figures of one width ("tnum"), so counters do not jitter as they change.</summary>
    FontVariation Even(Font font)
    {
        if (_even.TryGetValue(font.GetInstanceId(), out var v)) return v;
        var features = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("tnum")] = 1 };
        v = new FontVariation { BaseFont = font, OpentypeFeatures = features };
        _even[font.GetInstanceId()] = v;
        return v;
    }

    /// <summary>False when the style must wait until a control using it has a size.</summary>
    bool Box(StyleBox? s, Control? owner = null)
    {
        if (s == null || !First(s)) return true;
        switch (s)
        {
            case StyleBoxTexture t: return Textured(t, owner);
            case StyleBoxFlat f: Flat(f); break;
            case StyleBoxLine l:
            {
                Color old = l.Color;
                l.Color = new Color(Palette.Line, old.A);
                Remember(l, () => l.Color = old);
                break;
            }
        }
        return true;
    }

    readonly Dictionary<ulong, Rect2> _insets = new();

    /// <summary>
    /// How much of the game's picture is see-through at each edge, as a share of its size. Some frames are drawn
    /// smaller than their box on purpose (a button may stick out over the next card's empty top); the new frame keeps
    /// the same gap.
    /// </summary>
    Rect2 Inset(Texture2D tex)
    {
        ulong id = tex.GetInstanceId();
        if (_insets.TryGetValue(id, out var r)) return r;
        r = new Rect2();
        try
        {
            Image? img = tex.GetImage();
            if (img != null)
            {
                if (img.IsCompressed()) img.Decompress();
                Rect2I used = img.GetUsedRect();
                float w = img.GetWidth(), h = img.GetHeight();
                if (used.Size.X > 0 && used.Size.Y > 0)
                    r = new Rect2(used.Position.X / w, used.Position.Y / h, (w - used.End.X) / w, (h - used.End.Y) / h);
            }
        }
        catch { }
        _insets[id] = r;
        return r;
    }

    readonly Queue<ulong> _later = new();

    /// <summary>Controls that waited for their size: back in line once per second.</summary>
    public void Retry()
    {
        while (_later.Count > 0) _pending.Enqueue(_later.Dequeue());
    }

    bool Textured(StyleBoxTexture t, Control? owner)
    {
        if (t.Texture == null) return true;
        string path = t.Texture.ResourcePath ?? "";
        Skin? skin = t.ResourcePath.Contains("combat_footer") ? Skins.Panel   // the combat dock: a framed panel of its own
            : Skins.ForTexture(t.Texture);
        if (skin == null)
        {
            string what = $"{path} {t.Texture.GetWidth()}x{t.Texture.GetHeight()} (in {t.ResourcePath})";
            if (!path.Contains("Gradient") && _unknown.Add(what)) _log($"Left as it is (no skin for it yet): {what}");
            return true;
        }
        // see-through edges of more than 3%: the new frame needs the same gap, in pixels of a real control
        Rect2 inset = Inset(t.Texture);
        bool gaps = inset.Position.X > 0.03f || inset.Position.Y > 0.03f || inset.Size.X > 0.03f || inset.Size.Y > 0.03f;
        Vector2 size = owner?.Size ?? Vector2.Zero;
        if (NoGaps) gaps = false;
        if (gaps && (size.X < 2 || size.Y < 2)) return false;
        float gl = 0, gt = 0, gr = 0, gb = 0;
        if (gaps)
        {
            gl = inset.Position.X > 0.03f ? inset.Position.X * size.X : 0; gt = inset.Position.Y > 0.03f ? inset.Position.Y * size.Y : 0;
            gr = inset.Size.X > 0.03f ? inset.Size.X * size.X : 0; gb = inset.Size.Y > 0.03f ? inset.Size.Y * size.Y : 0;
        }
        Color modulate = t.ModulateColor;
        if (modulate.S > 0.3f && skin != Skins.Tintable)
        {
            // the game tints this frame: the tint moves into our line, the picture itself stays untinted
            skin = Skins.Tinted(skin, modulate);
            t.ModulateColor = new Color(1, 1, 1, modulate.A);
        }
        // what it was
        Texture2D? tex = t.Texture;
        float ml = t.TextureMarginLeft, mt = t.TextureMarginTop, mr = t.TextureMarginRight, mb = t.TextureMarginBottom;
        float cl = t.ContentMarginLeft, ct = t.ContentMarginTop, cr = t.ContentMarginRight, cb = t.ContentMarginBottom;
        float el = t.GetMargin(Side.Left), et = t.GetMargin(Side.Top), er = t.GetMargin(Side.Right), eb = t.GetMargin(Side.Bottom);
        Rect2 region = t.RegionRect;
        var ah = t.AxisStretchHorizontal; var av = t.AxisStretchVertical;
        // the new picture: the text keeps exactly the room it had, only the frame changes
        t.ContentMarginLeft = el; t.ContentMarginTop = et; t.ContentMarginRight = er; t.ContentMarginBottom = eb;
        t.Texture = Skins.Texture(skin);
        t.RegionRect = new Rect2();
        t.TextureMarginLeft = t.TextureMarginTop = t.TextureMarginRight = t.TextureMarginBottom = skin.Margin;
        t.AxisStretchHorizontal = t.AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch;
        float xl = t.ExpandMarginLeft, xt = t.ExpandMarginTop, xr = t.ExpandMarginRight, xb = t.ExpandMarginBottom;
        if (gaps)
        {
            t.ExpandMarginLeft = xl - gl; t.ExpandMarginTop = xt - gt; t.ExpandMarginRight = xr - gr; t.ExpandMarginBottom = xb - gb;
            Gaps.Add($"{t.ResourcePath} from {owner?.Name} {size.X:0}x{size.Y:0}: {gl:0}/{gt:0}/{gr:0}/{gb:0}");
        }
        Remember(t, () =>
        {
            t.ExpandMarginLeft = xl; t.ExpandMarginTop = xt; t.ExpandMarginRight = xr; t.ExpandMarginBottom = xb;
            t.Texture = tex; t.RegionRect = region;
            t.TextureMarginLeft = ml; t.TextureMarginTop = mt; t.TextureMarginRight = mr; t.TextureMarginBottom = mb;
            t.ContentMarginLeft = cl; t.ContentMarginTop = ct; t.ContentMarginRight = cr; t.ContentMarginBottom = cb;
            t.AxisStretchHorizontal = ah; t.AxisStretchVertical = av;
            t.ModulateColor = modulate;
        });
        return true;
    }

    void Flat(StyleBoxFlat f)
    {
        Color bg = f.BgColor, border = f.BorderColor;
        int detail = f.CornerDetail;
        int bl = f.BorderWidthLeft, bt = f.BorderWidthTop, br = f.BorderWidthRight, bb = f.BorderWidthBottom;
        int r0 = f.CornerRadiusTopLeft, r1 = f.CornerRadiusTopRight, r2 = f.CornerRadiusBottomRight, r3 = f.CornerRadiusBottomLeft;
        bool hasBorder = f.BorderWidthLeft + f.BorderWidthTop + f.BorderWidthRight + f.BorderWidthBottom > 0;
        bool quantity = f.ResourcePath.Contains("item_info_icon_qty") || f.ResourcePath.Contains("info_icon_label_panel");

        string file = f.ResourcePath;
        bool gameButton = file.Contains("/Styles/Button/button_");
        bool tile = file.Contains("inventory_item_panel_");
        if (quantity)
            f.BgColor = new Color(0.05f, 0.055f, 0.06f, Math.Max(bg.A, 0.85f));   // counts on pictures: always on a dark backing
        else if (tile)
        {
            // bank cells: each one its own tile (Gemini, round 4)
            f.BgColor = file.Contains("locked") ? new Color("221a14") : new Color("1a1d1c");
            int w = file.Contains("selected") ? 2 : 1;
            f.BorderWidthLeft = f.BorderWidthTop = f.BorderWidthRight = f.BorderWidthBottom = w;
            f.BorderColor = file.Contains("selected") ? Palette.Gold : file.Contains("hover") || file.Contains("pressed") ? Palette.LineBright : file.Contains("locked") ? Palette.RedLine : new Color("3e3a31");
        }
        else if (gameButton)
        {
            // the game's plain buttons (dark teal): by their role, not their colour
            (Color fill, Color line) = file.Contains("green") ? (Palette.Green, Palette.GreenLine)
                : file.Contains("red") ? (Palette.Red, Palette.RedLine)
                : file.Contains("disabled") ? (new Color("1a1714"), new Color("3a332b"))
                : file.Contains("hover") ? (Palette.RaisedHover, Palette.Gold)
                : file.Contains("pressed") ? (new Color("2f5229"), Palette.GreenLine)
                : (Palette.Raised, Palette.Line);
            f.BgColor = new Color(fill, Math.Max(bg.A, 0.92f));
            if (hasBorder) f.BorderColor = line;
        }
        else
            f.BgColor = Palette.Map(bg, text: false);
        if (hasBorder && !gameButton && !tile) f.BorderColor = Palette.Saturation(border) < 0.18f ? new Color(Palette.Line, border.A) : Palette.Map(border, text: false).Lightened(0.25f);
        if (r0 + r1 + r2 + r3 > 0)
        {
            f.CornerDetail = 1;   // straight cuts instead of round corners
            f.CornerRadiusTopLeft = Math.Min(r0, 4); f.CornerRadiusTopRight = Math.Min(r1, 4);
            f.CornerRadiusBottomRight = Math.Min(r2, 4); f.CornerRadiusBottomLeft = Math.Min(r3, 4);
        }
        Remember(f, () =>
        {
            f.BgColor = bg; f.BorderColor = border; f.CornerDetail = detail;
            f.BorderWidthLeft = bl; f.BorderWidthTop = bt; f.BorderWidthRight = br; f.BorderWidthBottom = bb;
            f.CornerRadiusTopLeft = r0; f.CornerRadiusTopRight = r1; f.CornerRadiusBottomRight = r2; f.CornerRadiusBottomLeft = r3;
        });
    }

    bool First(GodotObject o) => !_undo.ContainsKey(o.GetInstanceId());

    void Remember(GodotObject o, Action undo) => _undo[o.GetInstanceId()] = undo;
}
