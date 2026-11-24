using System;
using System.Collections.Generic;
using Godot;

namespace Facelift;

/// <summary>
/// Facelift's switches, one line each, opened from the loader's Mods menu (top-bar "Mods" button, F1). Every
/// switch works at once; off means the game's own look or layout for that part.
/// </summary>
sealed class SettingsWindow
{
    static readonly (string Name, string Title, string Help)[] Rows =
    {
        ("Look", "New look", "Charcoal panels with thin bronze lines, readable text, serif titles. Off: the game's own look."),
        ("NewFrame", "New frame", "A flat top bar with the open page underlined in gold, a large page name, and a skill list with a gold stripe on the open skill."),
        ("TidyTopBar", "Tidy top bar", "Hides the version text and the greyed \"Cozy UI Mode - Coming soon\" switch (it comes back once the game's Cozy mode works)."),
        ("ShopOpensCategory", "Shop opens a category", "The shop shows its first category straight away instead of an empty page."),
        ("BankSummary", "Bank summary", "With no item selected, the bank's right panel shows space, value and a hint instead of a red note."),
        ("FewerNotices", "Fewer notifications", "At most four notifications per side and one banner in the middle; older ones leave early (rewards are in the bank already)."),
        ("HideEmptyRows", "Hide empty rows", "Enemy cards leave out \"Special Abilities: None\" and \"Passives: None\"."),
    };

    readonly FaceliftMod _mod;
    CanvasLayer? _layer;
    readonly Dictionary<string, CheckButton> _checks = new();
    Label? _status;

    public SettingsWindow(FaceliftMod mod) => _mod = mod;

    public bool Visible => _layer != null && GodotObject.IsInstanceValid(_layer) && _layer.Visible;

    public void Toggle(SceneTree tree)
    {
        if (Visible) { _layer!.Visible = false; return; }
        if (_layer == null || !GodotObject.IsInstanceValid(_layer)) Build(tree);
        Refresh();
        _layer!.Visible = true;
    }

    public void Close() { if (Visible) _layer!.Visible = false; }

    void Build(SceneTree tree)
    {
        _layer = new CanvasLayer { Layer = 90, Name = "FaceliftSettings" };
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.45f), MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shade.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Close(); };
        _layer.AddChild(shade);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(640, 0) };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxTexture
        {
            Texture = Skins.Texture(Skins.Tooltip),
            TextureMarginLeft = Skins.Tooltip.Margin, TextureMarginTop = Skins.Tooltip.Margin, TextureMarginRight = Skins.Tooltip.Margin, TextureMarginBottom = Skins.Tooltip.Margin,
            ContentMarginLeft = 24, ContentMarginTop = 18, ContentMarginRight = 24, ContentMarginBottom = 18,
        });
        center.AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        panel.AddChild(box);

        var title = new Label { Text = "Facelift", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Georgia", "Cambria", "Times New Roman" }, FontWeight = 700 });
        title.AddThemeFontSizeOverride("font_size", 26);
        title.AddThemeColorOverride("font_color", Palette.Ivory);
        box.AddChild(title);
        var sub = new Label { Text = "A new look for the whole game. Each switch works at once.", HorizontalAlignment = HorizontalAlignment.Center };
        sub.AddThemeColorOverride("font_color", Palette.Muted);
        box.AddChild(sub);
        box.AddChild(new ColorRect { Color = Palette.LineSoft, CustomMinimumSize = new Vector2(0, 1) });

        foreach (var (name, text, help) in Rows)
        {
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 0);
            var check = new CheckButton { Text = text, Name = name, CustomMinimumSize = new Vector2(0, 34) };
            // own quiet styles: the window looks the same whatever the game's theme does
            StyleBoxFlat Flat(Color bg, Color line) => new()
            {
                BgColor = bg, BorderColor = line, BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
                CornerDetail = 1, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 4, ContentMarginBottom = 4,
            };
            check.AddThemeStyleboxOverride("normal", Flat(Palette.Raised, Palette.LineSoft));
            check.AddThemeStyleboxOverride("pressed", Flat(Palette.Raised, Palette.LineSoft));
            check.AddThemeStyleboxOverride("hover", Flat(Palette.RaisedHover, Palette.Line));
            check.AddThemeStyleboxOverride("hover_pressed", Flat(Palette.RaisedHover, Palette.Line));
            check.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            foreach (string icon in new[] { "checked", "checked_disabled" }) check.AddThemeIconOverride(icon, Skins.Tick(true));
            foreach (string icon in new[] { "unchecked", "unchecked_disabled" }) check.AddThemeIconOverride(icon, Skins.Tick(false));
            check.AddThemeColorOverride("font_color", Palette.Ivory);
            check.AddThemeColorOverride("font_pressed_color", Palette.Ivory);
            check.AddThemeColorOverride("font_hover_color", Palette.Gold);
            check.AddThemeFontSizeOverride("font_size", 17);
            string n = name;
            check.Toggled += on => { _mod.Set(n, on); Refresh(); };
            _checks[name] = check;
            row.AddChild(check);
            var note = new Label { Text = help, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(580, 0) };
            note.AddThemeConstantOverride("line_spacing", 0);
            note.AddThemeColorOverride("font_color", Palette.Muted);
            note.AddThemeFontSizeOverride("font_size", 13);
            row.AddChild(note);
            box.AddChild(row);
        }

        box.AddChild(new ColorRect { Color = Palette.LineSoft, CustomMinimumSize = new Vector2(0, 1) });
        _status = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeColorOverride("font_color", Palette.Muted);
        box.AddChild(_status);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        var allOff = new Button { Text = "Game's own look (all off)", CustomMinimumSize = new Vector2(220, 34) };
        allOff.Pressed += () => { foreach (var (name, _, _) in Rows) _mod.Set(name, false); Refresh(); };
        var allOn = new Button { Text = "Everything on", CustomMinimumSize = new Vector2(160, 34) };
        allOn.Pressed += () => { foreach (var (name, _, _) in Rows) _mod.Set(name, true); Refresh(); };
        var close = new Button { Text = "Close", CustomMinimumSize = new Vector2(110, 34) };
        close.Pressed += Close;
        buttons.AddChild(allOff); buttons.AddChild(allOn); buttons.AddChild(close);
        box.AddChild(buttons);

        tree.Root.AddChild(_layer);
    }

    void Refresh()
    {
        foreach (var (name, check) in _checks)
            check.SetPressedNoSignal((bool)(typeof(Settings).GetProperty(name)!.GetValue(_mod.Settings) ?? false));
        if (_status != null)
            _status.Text = _mod.CozyOn
                ? "The game's own Cozy UI Mode is on: Facelift steps aside while it is."
                : $"{_mod.Changed} of the game's styles are restyled now.";
    }
}
