using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GamesByMalcs.Melvor2.Data.Items;
using Godot;

namespace OneStyle;

/// <summary>
/// The third-party mod "Better Tooltips" hides the game's item tooltip (where One Style writes a rare item's colour and
/// affixes) and draws its own. One Style writes the same into Better Tooltips' panels instead: the name in the rarity's
/// colour and the affix lines (tiers while Alt is held) at the top of the panel. Found by reflection through the mod
/// loader's frame handlers; when Better Tooltips is not there, or changes, nothing happens.
/// </summary>
public static partial class RareItems
{
    const string BtLinesName = "OneStyleAffixes";
    static object? s_bt;
    static bool s_btLooked;
    static FieldInfo? s_btHover, s_btPinned;

    /// <summary>For tests: how many Better Tooltips panels got a rare item's lines in the last pass.</summary>
    public static int BetterTooltipsDecorated { get; private set; }

    static object? BetterTooltipsMod()
    {
        if (s_btLooked) return s_bt;
        s_btLooked = true;
        try
        {
            var list = typeof(MelvorModLoader.Loader).GetField("s_frame", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as IEnumerable;
            foreach (object? entry in list ?? Array.Empty<object>())
            {
                if (entry?.GetType().GetField("Item2")?.GetValue(entry) is not Delegate d || d.Target is not { } target) continue;
                Type t = target.GetType();
                if (t.Assembly.GetName().Name != "Better Tooltips") continue;
                s_btHover = t.GetField("_hoverPanel", BindingFlags.NonPublic | BindingFlags.Instance);
                s_btPinned = t.GetField("_pinned", BindingFlags.NonPublic | BindingFlags.Instance);
                if (s_btHover == null && s_btPinned == null) continue;
                s_bt = target;
                Log("One Style: Better Tooltips found: rare items show their colour and affixes in its tooltips too.");
                break;
            }
        }
        catch (Exception e) { Log("One Style: Better Tooltips could not be read: " + e.Message); }
        return s_bt;
    }

    /// <summary>Called by the frame walk: every Better Tooltips panel on screen gets the rare item's colour and lines.</summary>
    static void BetterTooltipsPass()
    {
        if (BetterTooltipsMod() is not { } mod) return;
        int done = 0;
        try
        {
            var panels = new List<object>();
            if (s_btHover?.GetValue(mod) is { } hover) panels.Add(hover);
            if (s_btPinned?.GetValue(mod) is IEnumerable pinned) panels.AddRange(pinned.Cast<object?>().Where(p => p != null)!);
            foreach (object panel in panels)
                if (DecorateBetterTooltip(panel)) done++;
        }
        catch (Exception e)
        {
            Log("One Style: Better Tooltips changed, its tooltips are left as they are: " + e.Message);
            s_bt = null;   // s_btLooked stays true: no second try this session
        }
        BetterTooltipsDecorated = done;
    }

    static bool DecorateBetterTooltip(object panel)
    {
        Type t = panel.GetType();
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        Item? item = t.GetProperty("Item", any)?.GetValue(panel) as Item;
        Label? title = t.GetField("_title", any)?.GetValue(panel) as Label;
        if (title == null || !GodotObject.IsInstanceValid(title)) return false;
        // the lines go under the title's row: the nearest column (VBox) above the title, right after the part holding it
        Node anchor = title;
        Node? holder = title.GetParent();
        while (holder != null && holder is not VBoxContainer) { anchor = holder; holder = holder.GetParent(); }
        Label? lines = holder?.GetNodeOrNull<Label>(BtLinesName);
        RolledItem? record = item == null ? null : RecordOf(item);
        if (record == null)
        {
            if (title.HasMeta(BtLinesName)) { title.RemoveThemeColorOverride("font_color"); title.RemoveMeta(BtLinesName); }
            if (lines != null) { holder!.RemoveChild(lines); lines.QueueFree(); }
            return false;
        }
        title.AddThemeColorOverride("font_color", Colors[record.Rarity]);
        title.SetMeta(BtLinesName, true);
        if (holder is not BoxContainer box) return true;
        string text = Lines(record, s_alt);
        if (lines == null)
        {
            lines = new Label { Name = BtLinesName, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(260, 0),
                MouseFilter = Control.MouseFilterEnum.Ignore };
            // the title's own size: Better Tooltips scales its whole panel down
            lines.AddThemeFontSizeOverride("font_size", Math.Max(16, (int)(title.GetThemeFontSize("font_size") * 0.62)));
            box.AddChild(lines);
            box.MoveChild(lines, anchor.GetIndex() + 1);
        }
        lines.AddThemeColorOverride("font_color", record.Rarity == Rarity.Unique ? new Color(1f, 0.78f, 0.55f) : new Color(0.72f, 0.82f, 1f));
        if (lines.Text != text) lines.Text = text;
        return true;
    }
}
