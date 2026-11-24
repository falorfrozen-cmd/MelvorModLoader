using System;
using System.Collections.Generic;
using Godot;

namespace Facelift;

/// <summary>Which sides of a frame get the bronze line.</summary>
[Flags]
enum Sides { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8, All = 15 }

/// <summary>
/// One look for one kind of surface: fill, line, which sides are lined, cut-corner size, and whether the centre is
/// drawn at all.
/// </summary>
sealed record Skin(string Name, Color Fill, Color Border, Sides Lined = Sides.All, int Cut = 4, bool Highlight = true, Color? FillBottom = null, int Width = 1, int Margin = 6);

/// <summary>
/// Draws the small nine-patch pictures that replace the game's torn-stone ones: 32x32, 8 px margins so corners never
/// stretch, 1 px line, cut corners, a faint top light. One picture per skin, made once.
/// </summary>
static class Skins
{
    public const int Size = 32;
    public const int Margin = 6;   // small enough for the game's 24x24 buttons (Gemini, round 3)

    static readonly Color Clear = new(0, 0, 0, 0);
    public static readonly Skin Button = new("button", Palette.Raised, new Color("7d6236"), FillBottom: new Color("1b1e1d"));
    public static readonly Skin ButtonHover = new("button-hover", Palette.RaisedHover, Palette.LineBright, FillBottom: new Color("242826"));
    public static readonly Skin ButtonPressed = new("button-pressed", new Color("2d4f28"), Palette.GreenLine, FillBottom: new Color("23401f"));
    public static readonly Skin ButtonDisabled = new("button-disabled", new Color("151716"), new Color("33302a"), Highlight: false);
    public static readonly Skin ButtonAction = new("button-action", new Color("3a6b31"), Palette.GreenLine, FillBottom: new Color("2e5727"));
    public static readonly Skin ButtonCancel = new("button-cancel", new Color("78291f"), Palette.RedLine, FillBottom: new Color("611f18"));
    // the big panels: near black, a 2 px gold-bronze edge, wide cut corners
    public static readonly Skin Panel = new("panel", new Color("191c1b"), Palette.Line, FillBottom: new Color("121413"), Cut: 7, Width: 2, Margin: 10);
    public static readonly Skin PanelInner = new("panel-inner", new Color("141716"), Palette.LineSoft, Cut: 4, Highlight: false);
    public static readonly Skin PanelHover = new("panel-hover", Palette.RaisedHover, Palette.LineBright, FillBottom: new Color("202422"), Cut: 5, Width: 2, Margin: 8);
    public static readonly Skin PanelSelected = new("panel-selected", new Color("1f3320"), Palette.GreenLine, FillBottom: new Color("182819"), Cut: 5, Width: 2, Margin: 8);
    public static readonly Skin PanelDisabled = new("panel-disabled", new Color("121413"), new Color("2e2b25"), Highlight: false);
    public static readonly Skin Header = new("header", new Color("1f2321"), Palette.Line, Sides.Bottom, Cut: 0, FillBottom: new Color("171a19"), Width: 2);
    public static readonly Skin Slot = new("slot", new Color("101211"), new Color("6a5432"), Cut: 3, Highlight: false);
    public static readonly Skin Row = new("row", new Color(1, 1, 1, 0.035f), Clear, Sides.None, Cut: 0, Highlight: false);
    public static readonly Skin Field = new("field", Palette.Field, Palette.LineSoft, Cut: 3, Highlight: false);
    public static readonly Skin TopBar = new("top-bar", new Color("131615"), Palette.Line, Sides.Bottom, Cut: 0, Highlight: false, FillBottom: Palette.Deep, Width: 2);
    public static readonly Skin BottomBar = new("bottom-bar", Palette.Deep, Palette.Line, Sides.Top, Cut: 0, Highlight: false, Width: 2);
    public static readonly Skin SideBar = new("side-bar", new Color("0d0f0e"), Palette.Line, Sides.Right, Cut: 0, Highlight: false, Width: 2);
    public static readonly Skin Tooltip = new("tooltip", new Color("161918"), Palette.LineBright, FillBottom: new Color("101211"), Cut: 5, Width: 2, Margin: 8);
    public static readonly Skin TealBar = new("teal", Palette.Teal, Palette.TealLine, Cut: 3, FillBottom: new Color("19494f"));
    public static readonly Skin RedBar = new("red", new Color("a3342a"), new Color("d0634c"), Cut: 2, FillBottom: new Color("862a22"));
    public static readonly Skin BarBack = new("bar-back", new Color("0a0b0b"), Palette.LineSoft, Cut: 2, Highlight: false);
    public static readonly Skin Tab = new("tab", Palette.Surface, Palette.LineSoft, Cut: 3);
    public static readonly Skin TabHover = new("tab-hover", Palette.RaisedHover, Palette.Line, Cut: 3);
    public static readonly Skin TabSelected = new("tab-selected", Palette.Raised, Palette.LineBright, Cut: 3, Width: 2);
    /// <summary>Light, line-only frame for pictures the game tints itself (green when complete, amber when highlighted).</summary>
    public static readonly Skin Tintable = new("tintable", new Color(1, 1, 1, 0.06f), new Color("d8d0bf"), Cut: 3, Highlight: false);

    static readonly Dictionary<Skin, ImageTexture> _made = new();

    public static ImageTexture Texture(Skin skin)
    {
        if (_made.TryGetValue(skin, out var done)) return done;
        Image img = Image.CreateEmpty(Size, Size, false, Image.Format.Rgba8);
        int c = skin.Cut;
        bool Inside(int x, int y)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size) return false;
            int dx = Math.Min(x, Size - 1 - x), dy = Math.Min(y, Size - 1 - y);
            return dx + dy >= c;   // the four corners are cut at 45 degrees
        }
        for (int y = 0; y < Size; y++)
        for (int x = 0; x < Size; x++)
        {
            if (!Inside(x, y)) { img.SetPixel(x, y, new Color(0, 0, 0, 0)); continue; }
            int w = skin.Width;
            bool edgeL = false, edgeR = false, edgeT = false, edgeB = false, diag = false;
            for (int k = 1; k <= w; k++)
            {
                edgeL |= !Inside(x - k, y); edgeR |= !Inside(x + k, y); edgeT |= !Inside(x, y - k); edgeB |= !Inside(x, y + k);
                diag |= !Inside(x - k, y - k) || !Inside(x + k, y - k) || !Inside(x - k, y + k) || !Inside(x + k, y + k);
            }
            bool lined = skin.Lined == Sides.All
                ? edgeL || edgeR || edgeT || edgeB || diag
                : (edgeL && skin.Lined.HasFlag(Sides.Left)) || (edgeR && skin.Lined.HasFlag(Sides.Right)) ||
                  (edgeT && skin.Lined.HasFlag(Sides.Top)) || (edgeB && skin.Lined.HasFlag(Sides.Bottom));
            Color fill = skin.FillBottom is { } bottom ? skin.Fill.Lerp(bottom, y / (float)(Size - 1)) : skin.Fill;
            if (skin.Highlight && !lined && !Inside(x, y - 1 - w) && Inside(x, y - w)) fill = fill.Lerp(Colors.White, 0.07f);   // the row just under the top line
            img.SetPixel(x, y, lined ? skin.Border : fill);
        }
        var tex = ImageTexture.CreateFromImage(img);
        _made[skin] = tex;
        return tex;
    }

    /// <summary>
    /// The skin for one of the game's pictures, by its file. Null: not known, leave the game's own picture alone
    /// (it is logged once so the list can grow).
    /// </summary>
    static ImageTexture? _on, _off;

    /// <summary>A small switch box for Facelift's own window: green with a tick when on, an empty frame when off.</summary>
    public static ImageTexture Tick(bool on)
    {
        if (on && _on != null) return _on;
        if (!on && _off != null) return _off;
        const int s = 20;
        Image img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            bool edge = x == 0 || y == 0 || x == s - 1 || y == s - 1;
            img.SetPixel(x, y, edge ? (on ? Palette.GreenLine : Palette.Line) : (on ? Palette.Green : Palette.Field));
        }
        if (on)
        {
            // the tick: a short stroke down, a long one up, two pixels thick
            void Dot(int x, int y) { for (int dx = 0; dx < 2; dx++) for (int dy = 0; dy < 2; dy++) img.SetPixel(x + dx, y + dy, Palette.Ivory); }
            for (int i = 0; i < 4; i++) Dot(4 + i, 9 + i);
            for (int i = 0; i < 8; i++) Dot(7 + i, 12 - i);
        }
        var tex = ImageTexture.CreateFromImage(img);
        if (on) _on = tex; else _off = tex;
        return tex;
    }

    /// <summary>
    /// The same skin for a frame the game tints (amber when highlighted, green when done): the tint goes into the
    /// line and a little into the fill, because a dark picture multiplied by a colour only turns black.
    /// </summary>
    public static Skin Tinted(Skin skin, Color tint)
    {
        Color line = Palette.Map(tint, text: true);
        return skin with { Name = skin.Name + "-" + tint.ToHtml(false), Border = line, Fill = skin.Fill.Lerp(line, 0.16f), FillBottom = (skin.FillBottom ?? skin.Fill).Lerp(line, 0.10f) };
    }

    /// <summary>The skin for a picture the scene carries inside itself (no file name): known by its size.</summary>
    public static Skin? ForTexture(Texture2D tex)
    {
        string path = tex.ResourcePath ?? "";
        if (path.Contains(".png")) return ForTexture(path);
        if (path.Contains("Gradient")) return null;
        return (tex.GetWidth(), tex.GetHeight()) switch
        {
            (584, 872) or (584, 200) => PanelInner,
            (438, 66) or (535, 68) => Field,
            (1226, 1352) or (3424, 1870) => Panel,
            (256, 81) => Button,
            _ => null,
        };
    }

    public static Skin? ForTexture(string path)
    {
        string f = path.Substring(path.LastIndexOf('/') + 1);
        return f switch
        {
            "TEX_Button_Normal.png" => Button,
            "TEX_Button_Hover.png" => ButtonHover,
            "TEX_Button_Pressed.png" => ButtonPressed,
            "TEX_Button_Disabled.png" => ButtonDisabled,
            "TEX_ActionButton_Normal.png" => ButtonAction,
            "TEX_CancelButton_Normal.png" => ButtonCancel,
            "TEX_Button_Equipment_Hover.png" or "TEX_Combat_10.png" or "TEX_Combat_11.png" or "TEX_Combat_12.png" or "TEX_Combat_6.png" => Slot,
            "TEX_Combat_21.png" or "TEX_Combat_Panel.png" or "TEX_Mining_7.png" or "TEX_Mining_9.png" or "TEX_Woodcutting_1.png"
                or "TEX_Crafting_1.png" or "TEX_Crafting_2.png" or "TEX_Fishing_3.png" or "TEX_Hunting_1.png" or "TEX_Hunting_1_Right.png"
                or "TEX_Hunting_4.png" or "TEX_Mining_1.png" => Panel,
            "TEX_Combat_22.png" or "TEX_Combat_23.png" or "TEX_Mining_8.png" or "TEX_Woodcutting_10.png" or "TEX_Crafting_4.png"
                or "TEX_Crafting_14.png" or "TEX_Mining_10.png" or "TEX_Hunting_2.png" or "TEX_Hunting_3.png" or "TEX_Hunting_5.png" => PanelInner,
            "TEX_Combat_Panel_Hover.png" or "TEX_Combat_ShapeBountyHover.png" => PanelHover,
            "TEX_Combat_Panel_Pressed.png" or "TEX_Combat_ShapeBountySelect.png" => PanelSelected,
            "TEX_Combat_Panel_Disabled.png" => PanelDisabled,
            "TEX_Combat_SelectAreaPanel.png" or "TEX_Mining_2.png" or "TEX_Mining_3.png" or "TEX_Mining_4.png" or "TEX_Combat_EnemyTitle.png"
                or "TEX_Combat_ItemSlot.png" or "TEX_Fishing_LocationCardOutline.png" => PanelInner,
            "TEX_Crafting_0.png" or "TEX_Woodcutting_11.png" or "TEX_FrameHeader_1.png" or "TEX_Crafting_13.png" or "TEX_Crafting_5.png" => Header,
            "TEX_Combat_StatLine_0.png" or "TEX_Crafting_Stat_0.png" or "TEX_Hunting_6.png" => Row,
            "TEX_Combat_BaseDropdown.png" or "TEX_Fishing_DropdownContainer.png" => Field,
            "TEX_hud-top.png" or "TEX_hud-top-center.png" => TopBar,
            "TEX_hud-bottom.png" => BottomBar,
            "TEX_sidebar-0.png" => SideBar,
            "TEX_Tooltip_Dark_Large.png" or "TEX_Tooltip_Dark_Thin.png" or "TEX_Tooltip_Light_Large.png" or "TEX_Tooltip_Light_Normal.png"
                or "TEXT_Notification_Default_0.png" or "TEXT_Notification_NewItem_0.png" => Tooltip,
            "TEX_Tooltip_V2_0.png" => TealBar,
            "TEX_Combat_1.png" => RedBar,
            "TEX_Combat_3.png" => BarBack,
            "TEX_Tab_Normal.png" or "TEX_Tab_Left_Normal.png" => Tab,
            "TEX_Tab_Hover.png" or "TEX_Tab_Left_Hover.png" => TabHover,
            "TEX_Tab_Pressed.png" or "TEX_Tab_Left_Pressed.png" => TabSelected,
            "TEX_CompletionLog_11.png" => Tintable,
            _ => null,
        };
    }
}
