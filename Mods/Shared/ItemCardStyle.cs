using Godot;

// Linked into every mod that draws an item card (internal, so the copies never clash).
// The one look of an item card, everywhere a mod shows one: see Mods/Shared/ITEM-CARD-DESIGN.md.
// Change a colour or a size here and every card changes with it.

/// <summary>Colours, sizes and the small building blocks of the item card (design of 2026-11-25).</summary>
internal static class ItemCardStyle
{
    // ---- colours (the rarity colours themselves come from One Style: RareItems.Colors)
    /// <summary>Main text: what an affix or a power does.</summary>
    public static readonly Color Text = new(0.878f, 0.898f, 0.937f);          // #E0E5EF
    /// <summary>Second text: the type line, notes, "every ... has this".</summary>
    public static readonly Color Muted = new(0.647f, 0.667f, 0.663f);         // #A5AAA9
    /// <summary>An affix's tier ("T3").</summary>
    public static readonly Color Tier = new(0.831f, 0.780f, 0.608f);          // #D4C79B
    /// <summary>An affix's range with Alt ("[7–8]").</summary>
    public static readonly Color Range = new(0.776f, 0.804f, 0.835f);         // #C6CDD5
    /// <summary>The base item's box: background, border and heading.</summary>
    public static readonly Color BaseBg = new(0.102f, 0.118f, 0.110f);        // #1A1E1C
    public static readonly Color BaseBorder = new(0.231f, 0.251f, 0.220f);    // #3B4038
    public static readonly Color BaseHeading = new(0.780f, 0.773f, 0.718f);   // #C7C5B7

    // ---- sizes (pixels)
    public const int EffectSize = 16;     // an affix line, a power's text, a unique bonus
    public const int HeadingSize = 13;    // AFFIXES, BASE ITEM BONUS, a power's name
    public const int TypeSize = 13;       // "Rare · Scabjaw Beast Gauntlets · item level 88", tier, range
    public const int AsideSize = 12;      // "3 rolled · T1 best / T5 lowest", "Fixed on every ..."
    public const int HintSize = 11;       // "Alt: affix ranges"
    public const int RowGap = 4;          // between lines of one section
    public const int SectionGap = 8;      // added between sections (with RowGap: 12)
    public const int TierWidth = 28;      // the tier column
    public const int RangeWidth = 59;     // the range column (Alt)

    public static Label Label(string text, Color color, int size, bool wrap = false)
    {
        var l = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        if (wrap) { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        return l;
    }

    /// <summary>"AFFIXES  3 rolled · T1 best / T5 lowest ──────": the heading in a colour, an optional muted aside, a thin rule.</summary>
    public static Control Heading(string text, Color color, string aside = "", bool rule = true)
    {
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(Label(text, color, HeadingSize));
        if (aside != "") row.AddChild(Label(aside, Muted, AsideSize));
        if (rule)
            row.AddChild(new ColorRect
            {
                Color = new Color(color.R, color.G, color.B, 0.35f), CustomMinimumSize = new Vector2(0, 1),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
        return row;
    }

    /// <summary>Space between two sections.</summary>
    public static Control Gap() => new Control { CustomMinimumSize = new Vector2(0, SectionGap), MouseFilter = Control.MouseFilterEnum.Ignore };

    /// <summary>The base item's box: a darker panel with a thin border.</summary>
    public static PanelContainer BaseBox()
    {
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = BaseBg, BorderColor = BaseBorder };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(3);
        style.ContentMarginLeft = style.ContentMarginRight = 10;
        style.ContentMarginTop = style.ContentMarginBottom = 9;
        box.AddThemeStyleboxOverride("panel", style);
        return box;
    }

    /// <summary>A Unique's power: a 2 px line in its colour on the left.</summary>
    public static PanelContainer PowerBox(Color color)
    {
        var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = color, BorderWidthLeft = 2 };
        style.ContentMarginLeft = 10;
        box.AddThemeStyleboxOverride("panel", style);
        return box;
    }

    public static VBoxContainer Column(int gap = RowGap)
    {
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AddThemeConstantOverride("separation", gap);
        return v;
    }
}
