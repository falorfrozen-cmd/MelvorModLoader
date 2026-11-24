using System;
using Godot;

namespace Facelift;

/// <summary>
/// The colours of the new look ("an adventurer's field guide"): charcoal surfaces, warm ivory text, thin muted
/// bronze lines, amber for focus and currency. Green means ready or active, red danger, teal timers and XP.
/// </summary>
static class Palette
{
    // near-black, slightly cool surfaces: they stand clear of the game's warm painted scenery (Codex, round 4)
    public static readonly Color Deep = new("0f1110");
    public static readonly Color Surface = new("171a19");
    public static readonly Color Raised = new("232725");
    public static readonly Color RaisedHover = new("2e3330");
    public static readonly Color Field = new("0b0d0c");
    public static readonly Color Line = new("b88b3e");
    public static readonly Color LineSoft = new("5f4c2c");
    public static readonly Color LineBright = new("d9ab52");
    public static readonly Color Gold = new("e0b25a");
    public static readonly Color Ivory = new("eee8d8");
    public static readonly Color Muted = new("a99f8c");
    public static readonly Color Dim = new("6f675b");
    public static readonly Color Green = new("3c6b34");
    public static readonly Color GreenLine = new("7fb06a");
    public static readonly Color GreenText = new("8fcf72");
    public static readonly Color Red = new("7c2a22");
    public static readonly Color RedLine = new("c4583f");
    public static readonly Color RedText = new("e8735a");
    public static readonly Color Teal = new("1f5d64");
    public static readonly Color TealLine = new("47a2aa");

    /// <summary>
    /// Moves one of the game's colours into the palette: greys become warm charcoal or warm ivory by brightness,
    /// strong colours keep their hue family but lose the neon. Alpha is kept. Used for text and flat panels alike.
    /// </summary>
    public static Color Map(Color c, bool text)
    {
        float h = c.H, s = c.S, v = c.V;
        Color o;
        if (s < 0.18f || (v < 0.3f && s < 0.45f) || (!text && v < 0.18f))   // near-black: grey, whatever its slight tint
        {
            if (text)
                o = v > 0.85f ? Ivory : v > 0.55f ? Muted.Lerp(Ivory, (v - 0.55f) / 0.3f) : v > 0.3f ? Dim.Lerp(Muted, (v - 0.3f) / 0.25f) : Ivory;   // dark text would vanish on the dark panels
            else
                o = v < 0.5f ? Deep.Lerp(RaisedHover, Mathf.Clamp(v / 0.4f, 0, 1)) : RaisedHover.Lerp(new Color("4a4134"), (v - 0.5f) / 0.5f);   // light greys stay dark: no tan patches
        }
        else
        {
            float deg = h * 360f;
            if (deg < 18 || deg >= 340) o = text ? RedText : Red.Lerp(RedLine, Mathf.Clamp(v - 0.3f, 0, 1));                          // red
            else if (deg < 50) o = text ? Gold.Lerp(RedText, Mathf.Clamp((40 - deg) / 30f, 0, 1) * 0.6f) : Line.Lerp(Gold, Mathf.Clamp(v - 0.3f, 0, 1));   // orange, gold, brown
            else if (deg < 70) o = text ? Gold : Line.Lerp(Gold, Mathf.Clamp(v - 0.3f, 0, 1));                                         // yellow
            else if (deg < 165) o = text ? GreenText : Green.Lerp(GreenLine, Mathf.Clamp(v - 0.4f, 0, 1));                            // green
            else if (deg < 250) o = text ? new Color("8cc8e8") : Teal.Lerp(TealLine, Mathf.Clamp(v - 0.4f, 0, 1));                    // teal, blue
            else o = text ? new Color("d79be0") : new Color("5a3563");                                                                  // purple, pink: rare, keep their family
        }
        o.A = c.A;
        return o;
    }

    public static float Luma(Color c) => 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B;
    public static float Saturation(Color c) => c.S;
}
