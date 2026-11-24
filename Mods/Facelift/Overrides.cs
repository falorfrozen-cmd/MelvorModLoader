using System;
using System.Collections.Generic;
using Godot;

namespace Facelift;

/// <summary>
/// Changes on single game nodes (not on shared styles): a style, a colour, a size, a font, a property. Each change
/// is written down once with what was there before, and <see cref="Clear"/> puts every one back (only where the
/// value is still Facelift's own, so a later change by the game wins).
/// </summary>
sealed class Overrides
{
    readonly Dictionary<string, Action> _undo = new();

    public int Count => _undo.Count;

    static string Key(GodotObject o, string what) => o.GetInstanceId() + "|" + what;

    public void Style(Control c, string slot, StyleBox box)
    {
        string key = Key(c, "style:" + slot);
        if (!_undo.ContainsKey(key))
        {
            bool had = c.HasThemeStyleboxOverride(slot);
            StyleBox? old = had ? c.GetThemeStylebox(slot) : null;
            _undo[key] = () =>
            {
                if (!GodotObject.IsInstanceValid(c)) return;
                if (had && old != null) c.AddThemeStyleboxOverride(slot, old); else c.RemoveThemeStyleboxOverride(slot);
            };
        }
        c.AddThemeStyleboxOverride(slot, box);
    }

    public void Color(Control c, string name, Color color)
    {
        string key = Key(c, "color:" + name);
        if (!_undo.ContainsKey(key))
        {
            bool had = c.HasThemeColorOverride(name);
            Color old = c.GetThemeColor(name);
            _undo[key] = () =>
            {
                if (!GodotObject.IsInstanceValid(c)) return;
                if (had) c.AddThemeColorOverride(name, old); else c.RemoveThemeColorOverride(name);
            };
        }
        c.AddThemeColorOverride(name, color);
    }

    public void FontSize(Control c, string name, int size)
    {
        string key = Key(c, "size:" + name);
        if (!_undo.ContainsKey(key))
        {
            bool had = c.HasThemeFontSizeOverride(name);
            int old = c.GetThemeFontSize(name);
            _undo[key] = () =>
            {
                if (!GodotObject.IsInstanceValid(c)) return;
                if (had) c.AddThemeFontSizeOverride(name, old); else c.RemoveThemeFontSizeOverride(name);
            };
        }
        c.AddThemeFontSizeOverride(name, size);
    }

    public void Font(Control c, string name, Font font)
    {
        string key = Key(c, "font:" + name);
        if (!_undo.ContainsKey(key))
        {
            bool had = c.HasThemeFontOverride(name);
            Font? old = had ? c.GetThemeFont(name) : null;
            _undo[key] = () =>
            {
                if (!GodotObject.IsInstanceValid(c)) return;
                if (had && old != null) c.AddThemeFontOverride(name, old); else c.RemoveThemeFontOverride(name);
            };
        }
        c.AddThemeFontOverride(name, font);
    }

    public void Constant(Control c, string name, int value)
    {
        string key = Key(c, "const:" + name);
        if (!_undo.ContainsKey(key))
        {
            bool had = c.HasThemeConstantOverride(name);
            int old = c.GetThemeConstant(name);
            _undo[key] = () =>
            {
                if (!GodotObject.IsInstanceValid(c)) return;
                if (had) c.AddThemeConstantOverride(name, old); else c.RemoveThemeConstantOverride(name);
            };
        }
        c.AddThemeConstantOverride(name, value);
    }

    /// <summary>Any property (Visible, CustomMinimumSize, Uppercase, LabelSettings, Modulate...).</summary>
    public void Set(GodotObject o, string property, Variant value)
    {
        string key = Key(o, "prop:" + property);
        if (!_undo.ContainsKey(key))
        {
            Variant old = o.Get(property);
            _undo[key] = () => { if (GodotObject.IsInstanceValid(o)) o.Set(property, old); };
        }
        o.Set(property, value);
    }

    public bool Done(GodotObject o, string what) => _undo.ContainsKey(Key(o, what));

    public void Clear()
    {
        foreach (var undo in _undo.Values) { try { undo(); } catch { } }
        _undo.Clear();
    }
}
