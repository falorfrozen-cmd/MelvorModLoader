using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Facelift;

/// <summary>
/// The frame every page shares, redrawn: a flat top bar with the open page underlined in gold and its name in a
/// large serif; a skill list with quiet group captions, thicker level bars and a gold stripe on the open skill; a
/// bottom strip with a clear top line. Only these nodes are touched (their own overrides), never the shared styles
/// other buttons use.
/// </summary>
sealed class Frame
{
    readonly Overrides _o;
    Node? _header, _sidebar, _footer;
    string _lastPage = "";
    Font? _serif;

    public Frame(Overrides o) => _o = o;

    Font Serif => _serif ??= new SystemFont { FontNames = new[] { "Georgia", "Cambria", "Constantia", "Times New Roman" }, FontWeight = 700, Antialiasing = TextServer.FontAntialiasing.Gray };

    static StyleBoxFlat Flat(Color bg, Color? line = null, int left = 0, int top = 0, int right = 0, int bottom = 0, int pad = 6) => new()
    {
        BgColor = bg,
        BorderColor = line ?? new Color(0, 0, 0, 0),
        BorderWidthLeft = left, BorderWidthTop = top, BorderWidthRight = right, BorderWidthBottom = bottom,
        ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = 2, ContentMarginBottom = 2,
    };

    // the top bar's buttons
    readonly StyleBoxFlat _navNormal = Flat(new Color(0, 0, 0, 0), Palette.LineSoft, right: 1);
    readonly StyleBoxFlat _navHover = Flat(new Color(1, 1, 1, 0.05f), Palette.LineBright, right: 1, bottom: 2);
    readonly StyleBoxFlat _navActive = Flat(new Color(Palette.Gold, 0.10f), Palette.Gold, right: 1, bottom: 3);
    // the skill list
    readonly StyleBoxFlat _skillNormal = Flat(new Color(0, 0, 0, 0), pad: 8);
    readonly StyleBoxFlat _skillHover = Flat(new Color(1, 1, 1, 0.05f), Palette.Line, left: 3, pad: 8);
    readonly StyleBoxFlat _skillActive = Flat(new Color(Palette.Gold, 0.12f), Palette.Gold, left: 3, pad: 8);
    readonly StyleBoxFlat _barBack = new() { BgColor = new Color("0d0b09"), BorderColor = new Color("2a241d"), BorderWidthBottom = 1 };
    readonly StyleBoxFlat _barFill = new() { BgColor = Palette.TealLine };
    readonly StyleBoxFlat _barFillActive = new() { BgColor = Palette.Gold };

    static bool Alive(Node? n) => n != null && GodotObject.IsInstanceValid(n) && n.IsInsideTree();

    public void Tick(Node view)
    {
        if (!Alive(_header)) _header = view.FindChild("Header", true, false);
        if (!Alive(_sidebar)) _sidebar = view.FindChildren("*", "PanelContainer", true, false).FirstOrDefault(n => n.GetType().Name == "GameSidebarLegacy");
        if (!Alive(_footer)) _footer = view.FindChildren("*", "PanelContainer", true, false).FirstOrDefault(n => n.GetType().Name == "GameFooter");
        string page = PageTitle();
        bool changed = page != _lastPage;
        _lastPage = page;
        if (Alive(_header)) TopBar(_header!, page);
        if (Alive(_sidebar)) SkillList(_sidebar!, page, changed);
        if (changed || ++_slow % 8 == 0) Page(view);
    }

    int _slow;
    readonly StyleBoxFlat _key = new()
    {
        BgColor = new Color("0b0d0c"), BorderColor = Palette.LineSoft, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerDetail = 1, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ContentMarginLeft = 5, ContentMarginRight = 5, ContentMarginTop = 1, ContentMarginBottom = 1,
    };

    /// <summary>The open page's own parts: quiet key tags instead of white keycaps, no neon arrow next to the food slot.</summary>
    void Page(Node view)
    {
        if (view.FindChild("EntityViewContainer", true, false) is not Node page) return;
        foreach (Node n in page.FindChildren("KeybindPanel", "PanelContainer", true, false))
        {
            if (n is not PanelContainer tag || _o.Done(tag, "style:panel")) continue;
            _o.Style(tag, "panel", _key);
            foreach (Node k in tag.FindChildren("*", "Label", true, false))
                if (k is Label l) _o.Set(l, "label_settings", new LabelSettings { FontSize = 10, FontColor = Palette.Muted });
        }
        foreach (Node n in page.FindChildren("NoFoodSelectedContainer", "PanelContainer", true, false))
            if (n.GetParent() is Node row)
                foreach (Node k in row.FindChildren("*", "TextureRect", true, false))
                    if (k is TextureRect arrow && arrow.Name != "ArrowIcon" && arrow.Texture?.ResourcePath?.EndsWith("UpArrow.png") == true && arrow.Visible) _o.Set(arrow, "visible", false);
    }

    /// <summary>The open page's name as the top bar shows it.</summary>
    string PageTitle()
    {
        if (!Alive(_header)) return "";
        foreach (Node n in _header!.FindChildren("Title", "Label", true, false))
            if (n is Label l && l.GetParent()?.GetParent() is Node p && IsMiddle(l)) return l.Text;
        return "";
    }

    static bool IsMiddle(Node n)
    {
        for (Node? p = n.GetParent(); p != null; p = p.GetParent())
            if (p.Name == "MiddleContainer") return true;
        return false;
    }

    void TopBar(Node header, string page)
    {
        foreach (Node n in header.FindChildren("*", "BaseButton", true, false))
        {
            if (n is not BaseButton b || n.Name.ToString().Contains("Character") || n.Name == "MenuButton2" || n is MenuButton) continue;
            string title = b.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault()?.Text ?? "";
            if (title.Length == 0 || title == "Cozy UI Mode") continue;
            bool active = title == page;
            foreach (string slot in new[] { "normal", "pressed", "focus", "disabled" })
                _o.Style(b, slot, slot == "focus" ? new StyleBoxEmpty() : active ? _navActive : _navNormal);
            _o.Style(b, "hover", active ? _navActive : _navHover);
            _o.Style(b, "hover_pressed", active ? _navActive : _navHover);
            // the small count boxes under Bank and Shop: no box, just the number
            foreach (Node k in b.FindChildren("*", "PanelContainer", true, false))
                if (k is Control box) _o.Style(box, "panel", new StyleBoxEmpty());
        }
        // the middle: no plate of its own, the page name large and in serif
        if (header.FindChild("MiddleContainer", true, false) is Control middle) _o.Style(middle, "panel", new StyleBoxEmpty());
        foreach (Node n in header.FindChildren("Title", "Label", true, false))
        {
            if (n is not Label l || !IsMiddle(l) || _o.Done(l, "prop:label_settings")) continue;
            _o.Set(l, "label_settings", new LabelSettings { Font = Serif, FontSize = 26, FontColor = Palette.Ivory, ShadowColor = new Color(0, 0, 0, 0.7f), ShadowSize = 2, ShadowOffset = new Vector2(0, 2) });
        }
    }

    void SkillList(Node sidebar, string page, bool changed)
    {
        foreach (Node n in sidebar.FindChildren("*", "Label", true, false))
        {
            if (n is not Label l) continue;
            // the group captions: Combat, Passive, Non-Combat
            if (l.Text is "Combat" or "Passive" or "Non-Combat" && l.GetParent()?.GetType().Name != "LegacySkillNavigationButton" && !IsInSkillButton(l) && !_o.Done(l, "prop:label_settings"))
            {
                _o.Set(l, "uppercase", true);
                _o.Set(l, "label_settings", new LabelSettings { Font = Serif, FontSize = 13, FontColor = Palette.LineBright });
            }
        }
        foreach (Node n in sidebar.FindChildren("*", "Button", true, false))
        {
            if (n.GetType().Name != "LegacySkillNavigationButton" || n is not Button b) continue;
            string skill = b.FindChildren("*", "Label", true, false).OfType<Label>().FirstOrDefault()?.Text ?? "";
            bool active = skill == page;
            if (changed || !_o.Done(b, "style:normal"))
            {
                _o.Style(b, "normal", active ? _skillActive : _skillNormal);
                _o.Style(b, "pressed", active ? _skillActive : _skillNormal);
                _o.Style(b, "hover", active ? _skillActive : _skillHover);
                _o.Style(b, "hover_pressed", active ? _skillActive : _skillHover);
                _o.Style(b, "focus", new StyleBoxEmpty());
            }
            foreach (Node k in b.FindChildren("*", "ProgressBar", true, false))
            {
                if (k is not ProgressBar bar) continue;
                if (!_o.Done(bar, "prop:custom_minimum_size")) _o.Set(bar, "custom_minimum_size", new Vector2(0, 4));
                _o.Style(bar, "background", _barBack);
                _o.Style(bar, "fill", active ? _barFillActive : _barFill);
            }
        }
    }

    static bool IsInSkillButton(Node n)
    {
        for (Node? p = n.GetParent(); p != null; p = p.GetParent())
            if (p.GetType().Name == "LegacySkillNavigationButton") return true;
        return false;
    }
}
