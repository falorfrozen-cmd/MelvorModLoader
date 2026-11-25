using System;
using System.Collections.Generic;
using System.Linq;
using CheatMenu;
using Godot;

namespace OneStyle;

/// <summary>
/// The window: four cards on top (Off, Melee, Ranged, Magic), then the class's title and tier with the bonuses it
/// gives now and at the next tier, and what the class opens. A click on a card changes the class at once.
/// </summary>
public sealed class ClassWindow
{
    readonly CanvasLayer _layer = new() { Layer = 118, Name = "OneStyle", Visible = false };
    readonly PanelContainer _panel;
    readonly Dictionary<Class, Button> _cards = new();
    readonly VBoxContainer _body = new();
    readonly Button _close;
    readonly Button _classTab, _elitesTab;
    Control _cardsRow = null!;
    bool _elitesPage;

    /// <summary>The Class page (false) or the Elites page (true); also for tests.</summary>
    public void ShowPage(bool elites)
    {
        _elitesPage = elites;
        _classTab.SetPressedNoSignal(!elites);
        _elitesTab.SetPressedNoSignal(elites);
        _cardsRow.Visible = !elites;
        Repaint();
    }
    double _clock;
    string _signature = "";

    public bool Visible => _layer.Visible;

    /// <summary>For tests: the card of a class (pressing it is the player's click).</summary>
    public Button Card(Class c) => _cards[c];
    /// <summary>For tests: all text shown under the cards.</summary>
    public string BodyText => string.Join(" | ", Labels(_body).Select(l => l.Text).Where(t => t != ""));

    static IEnumerable<Label> Labels(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is Label l) yield return l;
            foreach (Label inner in Labels(c)) yield return inner;
        }
    }

    static Texture2D? Tex(string path)
    {
        try { return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null; } catch { return null; }
    }

    static Texture2D? IconOf(Class c) => c switch
    {
        Class.Melee => Tex("res://Assets/Icons/Attack.png"),
        Class.Ranged => Tex("res://Assets/Icons/Ranged.png"),
        Class.Magic => Tex("res://Assets/Icons/Magic.png"),
        _ => null,
    };

    static string Blurb(Class c) => c switch
    {
        Class.Melee => "Swords, axes and maces. Attack and Strength carry you.",
        Class.Ranged => "Bows, crossbows and throwing weapons. Ranged carries you.",
        Class.Magic => "Staves and spells. Magic carries you.",
        _ => "The game as it is: every style counts.",
    };

    public ClassWindow(SceneTree tree)
    {
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shade.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) Hide(); };
        _layer.AddChild(shade);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(center);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(820, 0), Theme = Style.Build() };
        StyleBoxFlat frame = Style.Box(Style.Window, 8, Style.AccentDim, 1, 18, 14);
        frame.ShadowColor = new Color(0, 0, 0, 0.55f);
        frame.ShadowSize = 14;
        _panel.AddThemeStyleboxOverride("panel", frame);
        center.AddChild(_panel);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(root);

        // ---- header
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        Label title = Style.Label("ONE STYLE", Style.Accent, 20);
        header.AddChild(title);
        Label sub = Style.Label("Play the whole game with one combat style. You can change it at any time.", Style.Muted, 13);
        sub.VerticalAlignment = VerticalAlignment.Center;
        sub.SizeFlagsVertical = Control.SizeFlags.Fill;
        header.AddChild(sub);
        header.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        // the two pages: the class, and the elites (Champion and Unique monsters)
        _classTab = new Button { Text = "Class", ToggleMode = true, ButtonPressed = true, FocusMode = Control.FocusModeEnum.None };
        _elitesTab = new Button { Text = "Elites", ToggleMode = true, FocusMode = Control.FocusModeEnum.None };
        _classTab.Pressed += () => ShowPage(false);
        _elitesTab.Pressed += () => ShowPage(true);
        header.AddChild(_classTab);
        header.AddChild(_elitesTab);
        _close = new Button { Text = "Close" };
        _close.Pressed += Hide;
        header.AddChild(_close);
        root.AddChild(header);

        // ---- the four cards
        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 10);
        foreach (Class c in new[] { Class.None, Class.Melee, Class.Ranged, Class.Magic })
        {
            var card = new Button
            {
                ToggleMode = true, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 118),
                TooltipText = Blurb(c), FocusMode = Control.FocusModeEnum.None,
            };
            var inside = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            inside.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            inside.OffsetLeft = 10;
            inside.OffsetRight = -10;
            inside.AddThemeConstantOverride("separation", 4);
            var icon = new TextureRect
            {
                Texture = IconOf(c), CustomMinimumSize = new Vector2(40, 40), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            };
            inside.AddChild(icon);
            Label name = Style.Label(Classes.Name(c), Style.Text, 17);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            inside.AddChild(name);
            Label blurb = Style.Label(Blurb(c), Style.Muted, 11);
            blurb.HorizontalAlignment = HorizontalAlignment.Center;
            blurb.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            blurb.MouseFilter = Control.MouseFilterEnum.Ignore;
            inside.AddChild(blurb);
            card.AddChild(inside);
            Class picked = c;
            card.Pressed += () => { Patches.Set(picked); Repaint(); };
            _cards[c] = card;
            cards.AddChild(card);
        }
        root.AddChild(cards);
        _cardsRow = cards;

        _body.AddThemeConstantOverride("separation", 8);
        _body.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // Since 0.2 the text below the cards is longer than a small screen: it scrolls.
        _scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll.AddChild(_body);
        root.AddChild(_scroll);

        tree.Root.AddChild(_layer);
        _tree = tree;
    }

    readonly ScrollContainer _scroll;
    readonly SceneTree _tree;

    /// <summary>The scrolling part as tall as the screen allows (the header and the cards take about 300 pixels).</summary>
    void FitHeight() =>
        _scroll.CustomMinimumSize = new Vector2(0, Math.Clamp(_tree.Root.GetVisibleRect().Size.Y - 330, 240, 620));

    public void Show()
    {
        _close.Text = OneStyleMod.Instance?.Key.With("Close") ?? "Close";
        _layer.Visible = true;
        RareItems.HideNote();   // the note covers the top of this window, and the window says the same
        FitHeight();
        Repaint();
    }

    public void Hide() => _layer.Visible = false;

    /// <summary>Called every frame by the mod: once a second, follow the levels.</summary>
    public void Tick(double delta)
    {
        if (!_layer.Visible) return;
        _clock += delta;
        if (_clock < 1) return;
        _clock = 0;
        if (Signature() != _signature) Repaint();
    }

    static string Signature() => $"{Patches.Current}|{Patches.ClassLevel}|{Patches.Tier}|{Patches.MappedLevels}|{Patches.Problems.Count}|{Rings.Problems.Count}|{RareItems.Problems.Count}|{RareItems.Data.NormalPending.Count}|{Patches.EquivalentsActive.Count}|{Elites.Progress.Ember}|{Elites.Progress.Trophies}|{Elites.Progress.Recent.Count}|{Elites.Tier}|{Elites.Progress.Marked}|{string.Join(";", UniquePowers.StatusLines())}";

    void Repaint()
    {
        _signature = Signature();
        Class c = Patches.Current;
        foreach (var (cls, card) in _cards)
        {
            card.SetPressedNoSignal(cls == c);
            card.AddThemeStyleboxOverride("normal", Style.Box(cls == c ? Style.AccentDim : Style.Card, 6, cls == c ? Style.Accent : Style.Line, 1, 8, 8));
            card.AddThemeStyleboxOverride("pressed", Style.Box(Style.AccentDim, 6, Style.Accent, 2, 8, 8));
            card.AddThemeStyleboxOverride("hover", Style.Box(cls == c ? Style.AccentDim : Style.ControlHover, 6, Style.Accent, 1, 8, 8));
        }
        foreach (Node n in _body.GetChildren()) { _body.RemoveChild(n); n.QueueFree(); }
        if (RareItems.SafeMode)
        {
            // first on both pages: the note on the screen sends the player here
            var warn = new PanelContainer();
            warn.AddThemeStyleboxOverride("panel", Style.Box(new Color(0.16f, 0.05f, 0.05f), 6, Style.Bad, 2, 14, 10));
            var wv = new VBoxContainer();
            wv.AddThemeConstantOverride("separation", 4);
            wv.AddChild(Text("Rare items rest for this character", Style.Bad, 16));
            wv.AddChild(Text(RareItems.SafeNote, Style.Text, 13));
            wv.AddChild(Text("What to do: update One Style (and the game's other mods), then open this character again. Until then nothing of "
                             + "yours is changed or sold, and no rare items drop for this character.", Style.Muted, 12));
            warn.AddChild(wv);
            _body.AddChild(warn);
        }
        if (_elitesPage)
        {
            ElitesPage();
            return;
        }

        if (c == Class.None)
        {
            _body.AddChild(Text("No class chosen: the game plays as usual. Pick Melee, Ranged or Magic above.", Style.Muted, 14));
            _body.AddChild(RingSection());
            return;
        }

        int level = Patches.ClassLevel, tier = Patches.Tier;
        string skill = c == Class.Melee ? "Attack / Strength" : Classes.Name(c);

        // ---- title and tier
        var titleCard = Section();
        var tv = new VBoxContainer();
        tv.AddThemeConstantOverride("separation", 4);
        tv.AddChild(Text($"{Classes.Title(c, tier)}  ·  tier {tier} of 5  ·  class level {level} ({skill})", Style.Accent, 17));
        int? next = Classes.TierLevels.Cast<int?>().FirstOrDefault(t => t > level);
        tv.AddChild(Text(next is int n2
            ? $"Next tier at level {n2}: {Classes.Title(c, tier + 1)}."
            : "Highest tier reached.", Style.Muted, 13));
        var bar = new ProgressBar { MinValue = 0, MaxValue = 5, Value = tier, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8) };
        bar.AddThemeStyleboxOverride("background", Style.Box(Style.Field, 4));
        bar.AddThemeStyleboxOverride("fill", Style.Box(Style.Accent, 4));
        tv.AddChild(bar);
        List<string> now = Patches.PerkLines(c, tier).ToList();
        tv.AddChild(Text(now.Count > 0 ? "Now: " + string.Join(",  ", now) : "No tier bonus yet: the first comes at level 20.", Style.Good, 13));
        if (tier < 5) tv.AddChild(Text("Next tier: " + string.Join(",  ", Patches.PerkLines(c, tier + 1)), Style.Muted, 13));
        titleCard.AddChild(tv);
        _body.AddChild(titleCard);

        // ---- what the class opens
        var open = Section();
        var ov = new VBoxContainer();
        ov.AddThemeConstantOverride("separation", 3);
        ov.AddChild(Text("What your class opens", Style.Text, 15));
        string style = Classes.Name(c).ToLowerInvariant();
        ov.AddChild(Bullet($"Monsters immune to {style} can be hit with {style}: Wind Drifter, the Amalgamations, Blood Sanctum."));
        ov.AddChild(Bullet("The combat triangle is off for your class: no penalty and no bonus against any monster."));
        ov.AddChild(Bullet($"Quest goals and monsters that ask for another style take {style} (the archery targets of the first main quest)."));
        ov.AddChild(Bullet($"{Patches.MappedLevels} requirements on another style's level read your {skill} level: Battle Loadout, thieving distractions, "
                           + "the combat constellations, item upgrades, Hunting by hand, rings and amulets"
                           + (c == Class.Ranged ? ", and crossbows and throwing hammers that also ask for Strength." : ".")));
        if (c is Class.Melee or Class.Ranged) ov.AddChild(Bullet($"Your Defence counts in full when you dodge spells (the game uses Magic for 70% of it)."));
        ov.AddChild(Text("Weapons and armour of other styles keep their own requirements. Nothing of the class is written into your save.", Style.Muted, 12));
        open.AddChild(ov);
        _body.AddChild(open);

        // ---- equal footing (0.2)
        var fair = Section();
        var fv = new VBoxContainer();
        fv.AddThemeConstantOverride("separation", 3);
        fv.AddChild(Text("Equal footing", Style.Text, 15));
        fv.AddChild(Bullet($"Weak spot: against every monster, your {style} stances meet its lowest evasion of all styles."));
        if (c is Class.Melee or Class.Ranged) fv.AddChild(Bullet("Elemental edge: +15% max hit against monsters with an element, as a mage with the right spell."));
        if (c == Class.Melee) fv.AddChild(Bullet("Attack and Strength share their XP: whatever one gains, the other gains too."));
        fv.AddChild(Bullet(c switch
        {
            Class.Melee => "Class identity: 3% lifesteal with melee.",
            Class.Ranged => "Class identity: +5% critical hit chance and +25% critical damage with ranged, and +30 light.",
            _ => "Class identity: +3 ward and 25% chance of double ward damage with magic, and +30 light.",
        }));
        if (c is Class.Ranged or Class.Magic)
        {
            fv.AddChild(Bullet($"The pets Fen and Bruno can drop from your {Classes.Name(c)} XP."));
            fv.AddChild(Bullet($"Your {Classes.Name(c)} guild ranks 3-5 and your constellation give what melee gets from the Attack and Strength guilds and its constellations."));
            fv.AddChild(Text(Patches.EquivalentsActive.Count > 0 ? "Earned: " + string.Join(";  ", Patches.EquivalentsActive) : "Earned: none yet.", Style.Good, 12));
        }
        fair.AddChild(fv);
        _body.AddChild(fair);
        _body.AddChild(RingSection());

        if (Patches.Problems.Count > 0)
            _body.AddChild(Text("Not working with this game version: " + string.Join("; ", Patches.Problems), Style.Bad, 12));
    }

    // ================================================================ the Elites page

    void ElitesPage()
    {
        EliteProgress p = Elites.Progress;
        var top = Section();
        var tv = new VBoxContainer();
        tv.AddThemeConstantOverride("separation", 6);
        tv.AddChild(Text("Champion and Unique monsters", Style.Accent, 17));
        tv.AddChild(Text($"Any monster can come as a Champion (blue, about 6 in 100) or, rarely, as a Unique (orange, its own name and story): "
                         + "more hit points and damage, one or two affixes, and much better loot. Only while you play, never while the game is closed. "
                         + "Works with every class, and with Off.", Style.Muted, 12));
        var toggles = new HBoxContainer();
        toggles.AddThemeConstantOverride("separation", 16);
        var on = new CheckBox { Text = "Elites on", ButtonPressed = Elites.Enabled, FocusMode = Control.FocusModeEnum.None };
        on.Toggled += v => { Elites.Enabled = v; Repaint(); };
        var safe = new CheckBox { Text = "No death penalty while an elite lives", ButtonPressed = Elites.NoDeathPenalty, FocusMode = Control.FocusModeEnum.None };
        safe.Toggled += v => { Elites.NoDeathPenalty = v; Repaint(); };
        toggles.AddChild(on);
        toggles.AddChild(safe);
        tv.AddChild(toggles);
        tv.AddChild(Text($"Ember: {p.Ember:N0}   ·   Trophies: {p.Trophies} / {Elites.Known}", Style.Good, 15));
        tv.AddChild(Text("Ember rerolls a Magic or Rare item: select it in your bank and press \"Reroll\".", Style.Muted, 12));
        top.AddChild(tv);
        _body.AddChild(top);

        var rules = Section();
        var rv = new VBoxContainer();
        rv.AddThemeConstantOverride("separation", 3);
        rv.AddChild(Text("How it works", Style.Text, 15));
        rv.AddChild(Bullet("Chances: combat areas 6% Champion / 0.8% Unique, your slayer task 10% / 1.5%, dungeon waves 8% / 0.6%, a dungeon's last boss 10% / 4%. "
                           + "After 100 kills of a monster without its Unique the chance climbs until it is certain at 200 (60 and 120 before its first Unique)."));
        rv.AddChild(Bullet("Champion: 1.35 to 3 times the hit points, +8 to 12% damage, 5% faster, one random affix (two from 1200 hit points). "
                           + "Unique: 1.7 to 6 times the hit points, +14 to 22% damage, 8% faster, its own two affixes, its own attack and sometimes a passive."));
        rv.AddChild(Bullet("Affixes: Hasted (faster), Colossal (a quarter more hit points on top, +10% damage, slower), Warded (2 to 6 hits of ward), Armoured (-18% damage taken), "
                           + "Vampiric (heals from its hits, fades after 25 s), Enraging (stronger below 40% hit points)."));
        rv.AddChild(Bullet("Safety: an elite's hit is never more than 1.3 times the monster's own, and after 60 s it tires (+5% damage taken every 5 s)."));
        rv.AddChild(Bullet("Rewards: Champion 2 and Unique 5 more rolls of the monster's own loot (gold too), +25% / +50% combat XP, and Ember "
                           + "(Champion 3 to 11, Unique 16 to 48). The first kill of a monster's Champion or Unique gives more."));
        rv.AddChild(Bullet("Trophies: your first Unique of a monster gives its trophy. Each trophy: +0.15% elite chance and +0.1% Ember."));
        foreach (string m in Elites.Milestones()) rv.AddChild(Text("     " + m, m.EndsWith("(reached)") ? Style.Good : Style.Muted, 12));
        if (p.Trophies >= 100)
        {
            var mark = new Button { Text = "Mark the monster I am fighting (+25% Unique chance)", FocusMode = Control.FocusModeEnum.None };
            mark.Pressed += () => { Elites.MarkCurrent(); Repaint(); };
            rv.AddChild(mark);
            if (p.Marked != "") rv.AddChild(Text("Marked: " + p.Marked.Replace("MelvorBase:", ""), Style.Good, 12));
        }
        rules.AddChild(rv);
        _body.AddChild(rules);

        var recent = Section();
        var cv = new VBoxContainer();
        cv.AddThemeConstantOverride("separation", 3);
        cv.AddChild(Text("Last elites", Style.Text, 15));
        if (p.Recent.Count == 0) cv.AddChild(Text("None yet.", Style.Muted, 12));
        foreach (string line in p.Recent) cv.AddChild(Text(line, Style.Text, 12));
        recent.AddChild(cv);
        _body.AddChild(recent);
        _body.AddChild(RareSection());
        if (UniquePowers.StatusLines() is { Count: > 0 } powers)
        {
            var box = Section();
            var pv = new VBoxContainer();
            pv.AddThemeConstantOverride("separation", 3);
            pv.AddChild(Text("Your unique items", Style.Text, 15));
            foreach (string line in powers) pv.AddChild(Text(line, RareItems.Colors[Rarity.Unique], 12));
            pv.AddChild(Text("Powers work while you play; the numbers here are their counters. Offline a Unique keeps its plain stats, "
                             + "and a power's price (such as Twin Volley's -40% damage) is not taken.", Style.Muted, 11));
            box.AddChild(pv);
            _body.AddChild(box);
        }
        _body.AddChild(Text("Your Ember, trophies and counts are kept in elites-progress.json next to the mod. Your rare items are kept in your save, "
            + "and with every save a second copy goes to \"onestyle-items\" next to the game's saves: if a game update stops the mod and the game "
            + "saves without your rare items, they come back from there when One Style works again.", Style.Muted, 12));
    }

    string _rareNote = "";

    /// <summary>Rare items: the last drops, and "make all normal" for players who remove the mod loader too.</summary>
    PanelContainer RareSection()
    {
        var box = Section();
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        // anything that went wrong with this character's rare items is said here, in red, before everything else
        foreach (string problem in RareItems.Problems.Where(p => p != RareItems.SafeNote)) v.AddChild(Text(problem, Style.Bad, 13));
        if (RareItems.Recovered > 0)
            v.AddChild(Text($"This save had lost its rare items (the game had saved without the mod). {RareItems.Recovered} came back from the copy "
                            + "One Style keeps when this character was opened; what did not fit where it was is in your bank.", Style.Good, 13));
        if (RareItems.Data.NormalPending.Count > 0)
            v.AddChild(Text($"{RareItems.Data.NormalPending.Values.Sum()} normal item(s) wait for room in your bank (free some slots; do not remove the mod yet).", Style.Bad, 13));
        v.AddChild(Text("How rare drops work", Style.Text, 15));
        v.AddChild(Bullet("Magic (blue) has 2 affixes, Rare (yellow) 3, one in five 4, Unique (orange) is a fixed item of one monster's Unique. "
                          + "They go straight into your bank with a coloured frame."));
        v.AddChild(Bullet("A normal kill: 1% Magic and 1% Rare. A Champion always drops 1 (Magic 60%, Rare 33%, Rare with 4 affixes 7%). "
                          + "A Unique drops 2 (1 Rare, 1 with a Champion's chances) and 15% its own Unique item. Champions are about 6 kills in 100, "
                          + "so most rare items come from them."));
        v.AddChild(Bullet("The item level is the monster's combat level (+5 on your Slayer task or from a dungeon's last boss, which also gives "
                          + "one more item). It sets the best affix tier: T5 below 15, T4 from 15, T3 from 35, T2 from 60, T1 from 85. "
                          + "You need about 80% of the item level to wear it."));
        v.AddChild(Bullet("The gear is any piece near the monster's level, not only its own drops (a Golbin can drop a rare Stone Sword, never "
                          + "a level 50 item); every slot is as likely, weapons only of your class's style. A monster's drop list shows them all "
                          + "(\"Show possible items\"); the percentage there is the share of its rare drops, not a chance per kill."));
        v.AddChild(Bullet("Hold Alt over a rare item to see each affix's tier and range. Ember rerolls a Magic or Rare item's numbers in the bank. "
                          + "Too many? Bulk Sell's \"Auto-sell\" sells the ones you do not want as they drop."));
        foreach (RolledItem r in RareItems.LastDrops.GetRange(0, System.Math.Min(8, RareItems.LastDrops.Count)))
        {
            Label l = Text($"{RareItems.RarityName(r.Rarity)}: {r.Name}", RareItems.Colors[r.Rarity], 12);
            v.AddChild(l);
        }
        if (RareItems.Data.Pending.Count > 0)
            v.AddChild(Text($"{RareItems.Data.Pending.Count} item(s) wait for room in your bank.", Style.Bad, 12));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var normal = new Button { Text = "Make all rare items normal", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Only needed before you remove the mod loader itself. When you remove just One Style, the mod loader puts their normal items "
                          + "(and the rings of Ring Slot 3 to 6) in your bank on its own." };
        normal.Pressed += () =>
        {
            if (MelvorModLoader.GameAccess.CurrentGame is not { IsSaveLoaded: true } game) return;
            var (made, waiting) = RareItems.MakeAllNormal(game);
            _rareNote = RareItems.SafeMode ? "Rare items rest for this character (see the note above): nothing was changed."
                : $"{made} item(s) are normal items again." + (waiting > 0 ? $" {waiting} of them wait for room in your bank: free some slots, they go in by themselves. Do not remove the mod before they are in." : "");
            Repaint();
        };
        row.AddChild(normal);
        // the bank fills fast at 2% a kill: one press sells every Magic item you have not locked (denge-2 11)
        var sell = new Button { Text = "Sell unlocked Magic items", FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Sells every Magic (blue, 2 affixes) item in your bank at its price, except those you locked in the bank. "
                          + "Rare and Unique items are never sold by this button." };
        sell.Pressed += () =>
        {
            if (MelvorModLoader.GameAccess.CurrentGame is not { IsSaveLoaded: true } game) return;
            var (items, gold) = RareItems.SellMagic(game);
            _rareNote = items == 0 ? "No unlocked Magic items in the bank." : $"Sold {items} Magic item(s) for about {gold:N0} gold.";
            Repaint();
        };
        row.AddChild(sell);
        if (_rareNote != "") row.AddChild(Text(_rareNote, Style.Good, 12));
        v.AddChild(row);
        box.AddChild(v);
        return box;
    }

    string _ringNote = "";

    /// <summary>The six ring slots (every class, and Off too) and the button to empty slots 2 to 6 before removing the mod.</summary>
    PanelContainer RingSection()
    {
        var box = Section();
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        v.AddChild(Text($"{Rings.Total} ring slots", Style.Text, 15));
        v.AddChild(Text("Ring Slot 2 is open without its Crafting 150 lock, and four more ring slots are on the equipment grid. "
                        + "Each game ring counts once: wear different rings. Rare copies of one ring all add their affixes, "
                        + "but that ring's own bonuses count once. Works with every class, and with Off.", Style.Muted, 12));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        var bank = new Button { Text = "Put rings 2-6 in the bank" };
        bank.Pressed += () =>
        {
            if (MelvorModLoader.GameAccess.CurrentGame is not { IsSaveLoaded: true } game) return;
            var (moved, worn, waiting) = Rings.ToBankCounted(game);
            _ringNote = $"{moved} ring(s) put in the bank."
                + (worn > 0 ? $" {worn} still worn: make room in the bank and press again." : "")
                + (waiting > 0 ? $" {waiting} from your other loadouts wait for room and go in by themselves." : "");
            Repaint();
        };
        row.AddChild(bank);
        if (_ringNote != "") row.AddChild(Text(_ringNote, Style.Good, 12));
        v.AddChild(row);
        v.AddChild(Text("Removing One Style: the mod loader puts the rings of Ring Slot 3 to 6 in your bank by itself (Ring Slot 2 is the game's own "
                        + "and keeps its ring). Removing the mod loader too: press this first.", Style.Muted, 12));
        if (Rings.Problems.Count > 0) v.AddChild(Text("Ring slots: " + string.Join("; ", Rings.Problems), Style.Bad, 12));
        box.AddChild(v);
        return box;
    }

    static PanelContainer Section()
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", Style.Box(Style.Card, 6, Style.Line, 1, 14, 10));
        return p;
    }

    static Label Text(string text, Color color, int size)
    {
        Label l = Style.Label(text, color, size);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    static Label Bullet(string text) => Text("•  " + text, Style.Text, 13);
}
