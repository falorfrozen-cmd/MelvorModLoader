using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using CheatMenu;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Localization;
using Godot;

namespace OneStyle;

/// <summary>
/// Ember's first use (denge-2, Codex C6; made a gamble on the user's word, 2026-11-24): reroll a Magic or Rare item's
/// numbers. The affixes and their tiers stay; each value is rolled again inside its tier's range and the item takes the
/// new values at once, no choice. A window shows the item before (left) and after (right), the numbers tumbling for a
/// moment before they land. At least one value changes when any range allows it; an item whose values are all fixed at
/// its tiers cannot be rerolled. It costs 2 x b Ember, b = min(5, 1 + item level / 20): 2 to 10. Unique items are not
/// rerolled. Only an item in the bank (not worn), from the bank's Selected Item panel.
/// </summary>
public static partial class RareItems
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Bonuses")]
    static extern void SetBonuses(EquipmentCapability cap, GamesByMalcs.Melvor2.Modifiers.ModifierValues? bonuses);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Description")]
    static extern void SetDescription(Item item, LocalizedString? description);

    public static int RerollCost(RolledItem record) => 2 * Math.Min(5, 1 + record.ILvl / 20);

    public static bool CanReroll(RolledItem record) => record.UniqueId == "" && record.Rarity is Rarity.Magic or Rarity.Rare or Rarity.Rare4;

    /// <summary>True when at least one affix's tier range has more than one value (else a reroll could change nothing).</summary>
    public static bool HasRange(RolledItem record) =>
        record.Affixes.Any(x => Affixes.TryGetValue(x.Id, out AffixDef? a) && a.Range(x.Tier) is var (lo, hi) && hi > lo);

    static double RollValue(AffixDef a, int tier)
    {
        var (min, max) = a.Range(tier);
        return Math.Round(min + a.Step * s_rng.Next(0, (int)Math.Round((max - min) / a.Step) + 1), 1);
    }

    /// <summary>
    /// Pays the Ember and gives the item new values at once (at least one different from before). Returns the values it
    /// had before, or null when nothing was done (not rerollable, nothing to change, Ember short).
    /// </summary>
    public static List<double>? Reroll(Game game, RolledItem record)
    {
        if (!CanReroll(record) || !HasRange(record) || !InBank(game, record)) return null;
        int cost = RerollCost(record);
        if (!Elites.SpendEmber(cost)) return null;
        var old = record.Affixes.Select(x => x.Value).ToList();
        List<double> next;
        int tries = 0;
        do next = record.Affixes.Select(x => Affixes.TryGetValue(x.Id, out AffixDef? a) ? RollValue(a, x.Tier) : x.Value).ToList();
        while (next.SequenceEqual(old) && ++tries < 100);
        record.Reroll = null;   // a choice left waiting by the old version (0.4.0 test builds) is dropped
        for (int i = 0; i < next.Count; i++) record.Affixes[i].Value = next[i];
        var registry = game.Data.GetDataRegistryOutput<Item>();
        if (registry.TryGetObjectById("MelvorBase:" + record.LocalId, out Item? item) && item.Capabilities.TryGet(out EquipmentCapability? cap)
            && game.Data.TryGetObject(record.BaseId, out Item? baseItem) && baseItem.Capabilities.TryGet(out EquipmentCapability? baseCap))
        {
            SetBonuses(cap, AffixBonuses(game, record.Affixes, baseCap.Bonuses));
            SetDescription(item, new LocalizedString(Describe(record)));
        }
        UpdateCaps(game);
        RerollVersion++;
        Log($"One Style: {record.Name} rerolled for {cost} Ember: {string.Join(", ", old)} -> {string.Join(", ", next)}.");
        return old;
    }

    /// <summary>The item lies in the bank. A worn item leaves a placeholder there that can be selected too: its numbers
    /// are in use by the character, so it is never rerolled where it is worn.</summary>
    static bool InBank(Game game, RolledItem record) =>
        Bank(game) is { } bank && game.Data.GetDataRegistryOutput<Item>().TryGetObjectById("MelvorBase:" + record.LocalId, out Item? item)
        && bank.Inventory.GetQuantity(item) > 0;

    /// <summary>Changes after every reroll: the bank panel draws itself again.</summary>
    public static int RerollVersion { get; private set; }

    const string RerollName = "OneStyleReroll";
    static readonly Color EmberColor = new(1f, 0.62f, 0.28f);

    static string Ember(long n) => n.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The reroll button under a rare item in the bank's Selected Item panel (not in tooltips, not for worn items).</summary>
    static void RerollPanel(ItemInfoPanel info, Item? item)
    {
        Label description = ItemDescriptionOf(info);
        Node? holder = description.GetParent();
        if (holder == null) return;
        Node? have = holder.GetNodeOrNull(RerollName);
        bool inBank = false;
        for (Node? n = info; n != null && !inBank; n = n.GetParent()) inBank = n is SelectedItemView;
        RolledItem? record = item == null ? null : RecordOf(item);
        if (!inBank || item == null || record == null || !CanReroll(record) || s_game is not { } game || !InBank(game, record))
        {
            if (have != null) { holder.RemoveChild(have); have.QueueFree(); }
            return;
        }
        string key = $"{record.LocalId}|{RerollVersion}|{Elites.Progress.Ember}";
        if (have != null && !have.IsQueuedForDeletion() && have.GetMeta("key", "").AsString() == key) return;
        if (have != null) { holder.RemoveChild(have); have.QueueFree(); }

        int cost = RerollCost(record);
        long ember = Elites.Progress.Ember;
        bool range = HasRange(record);
        var row = new HBoxContainer { Name = RerollName };
        row.SetMeta("key", key);
        row.AddThemeConstantOverride("separation", 10);
        var button = new Button { Text = $"Reroll  ·  {cost} Ember", Disabled = !range || ember < cost, FocusMode = Control.FocusModeEnum.None,
            TooltipText = range ? "Rolls this item's numbers again. The new numbers stay at once." : "Every value of this item is fixed at its tiers." };
        button.AddThemeFontSizeOverride("font_size", 14);
        button.AddThemeColorOverride("font_color", EmberColor);
        button.Pressed += () =>
        {
            try { OpenReroll(game, item, info); }
            catch (Exception e) { Log("One Style: reroll failed: " + e.Message); }
        };
        row.AddChild(button);
        var have2 = new Label { Text = range ? $"You have {Ember(ember)}" : "Values fixed at these tiers", VerticalAlignment = VerticalAlignment.Center };
        have2.AddThemeFontSizeOverride("font_size", 13);
        have2.AddThemeColorOverride("font_color", new Color(0.62f, 0.62f, 0.62f));
        row.AddChild(have2);
        holder.AddChild(row);
        // under the item's own block (affixes), above the game's lists
        Node? block = holder.GetNodeOrNull(BlockName);
        holder.MoveChild(row, (block ?? description).GetIndex() + 1);
    }

    /// <summary>The bank's Selected Item panel draws the item again (its "Bonuses" list shows the new values).</summary>
    static void RefreshSelected(Node? from)
    {
        for (Node? n = from; n != null && GodotObject.IsInstanceValid(n); n = n.GetParent())
            if (n is SelectedItemView view) { view.Update(); return; }
    }

    // ================================================================ the window

    static CanvasLayer? s_rerollLayer;
    public static bool RerollOpen => s_rerollLayer != null && GodotObject.IsInstanceValid(s_rerollLayer);
    /// <summary>For tests and the review: the window's text once the numbers have landed.</summary>
    public static string RerollText { get; private set; } = "";
    /// <summary>True while the numbers are still tumbling.</summary>
    public static bool RerollRolling { get; private set; }

    public static void CloseReroll()
    {
        if (RerollOpen) s_rerollLayer!.QueueFree();
        s_rerollLayer = null;
        RerollRolling = false;
    }

    /// <summary>Pays, rerolls and opens the window: the item before on the left, after on the right.</summary>
    public static void OpenReroll(Game game, Item item, Node? bankPanel = null)
    {
        if (RecordOf(item) is not { } record || Godot.Engine.GetMainLoop() is not SceneTree tree) return;
        List<double>? before = Reroll(game, record);
        if (before == null) return;
        RefreshSelected(bankPanel);
        CloseReroll();

        s_rerollLayer = new CanvasLayer { Layer = 118, Name = "OneStyleReroll" };
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.5f), MouseFilter = Control.MouseFilterEnum.Stop };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        shade.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && !RerollRolling) CloseReroll(); };
        s_rerollLayer.AddChild(shade);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        s_rerollLayer.AddChild(center);
        var panel = new PanelContainer { Theme = Style.Build(), CustomMinimumSize = new Vector2(760, 0) };
        StyleBoxFlat frame = Style.Box(Style.Window, 8, EmberColor.Darkened(0.45f), 1, 20, 16);
        frame.ShadowColor = new Color(0, 0, 0, 0.6f);
        frame.ShadowSize = 16;
        panel.AddThemeStyleboxOverride("panel", frame);
        center.AddChild(panel);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 14);
        panel.AddChild(root);

        Label title = Style.Label("REROLL", EmberColor, 20);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(title);

        var cards = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        cards.AddThemeConstantOverride("separation", 14);
        root.AddChild(cards);
        var (leftCard, _) = Card(game, item, record, before, null, "BEFORE");
        cards.AddChild(leftCard);
        Label arrow = Style.Label("▶", EmberColor, 26);
        arrow.VerticalAlignment = VerticalAlignment.Center;
        cards.AddChild(arrow);
        var (rightCard, lines) = Card(game, item, record, record.Affixes.Select(x => x.Value).ToList(), before, "AFTER");
        cards.AddChild(rightCard);

        var footer = new HBoxContainer();
        footer.AddThemeConstantOverride("separation", 10);
        Label wallet = Style.Label($"Ember: {Ember(Elites.Progress.Ember)}", Style.Muted, 14);
        wallet.VerticalAlignment = VerticalAlignment.Center;
        footer.AddChild(wallet);
        footer.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        int cost = RerollCost(record);
        var again = new Button { Text = $"Reroll again  ·  {cost} Ember", FocusMode = Control.FocusModeEnum.None, Disabled = true };
        again.AddThemeColorOverride("font_color", EmberColor);
        again.AddThemeFontSizeOverride("font_size", 15);
        again.Pressed += () =>
        {
            try { OpenReroll(game, item, bankPanel); }
            catch (Exception e) { Log("One Style: reroll failed: " + e.Message); }
        };
        footer.AddChild(again);
        var close = new Button { Text = "Close", FocusMode = Control.FocusModeEnum.None };
        close.AddThemeFontSizeOverride("font_size", 15);
        close.Pressed += CloseReroll;
        footer.AddChild(close);
        root.AddChild(footer);
        tree.Root.AddChild(s_rerollLayer);

        // the numbers tumble for a moment, then land one after another, left to right
        RerollRolling = true;
        RerollText = "";
        var timer = new Timer { WaitTime = 0.045, OneShot = false };
        s_rerollLayer.AddChild(timer);
        int tick = 0;
        const int Spin = 12, Gap = 4;
        timer.Timeout += () =>
        {
            tick++;
            bool all = true;
            for (int i = 0; i < lines.Count; i++)
            {
                var (label, def, affix, final, landed) = lines[i];
                if (landed) continue;
                if (tick < Spin + i * Gap || def == null)
                {
                    if (def != null)
                    {
                        var (lo, hi) = def.Range(affix.Tier);
                        label.Text = hi > lo ? AffixLine(def, new RolledAffix { Id = affix.Id, Tier = affix.Tier, Style = affix.Style, Value = RollValue(def, affix.Tier) }, false) : final.Text;
                        label.AddThemeColorOverride("font_color", Style.Muted);
                        all = false;
                        continue;
                    }
                }
                label.Text = final.Text;
                label.AddThemeColorOverride("font_color", final.Color);
                if (final.Change != null) { final.Change.Visible = true; Pop(final.Change); }
                lines[i] = (label, def, affix, final, true);
            }
            if (!all) return;
            timer.Stop();
            timer.QueueFree();
            RerollRolling = false;
            Pop(rightCard);
            again.Disabled = Elites.Progress.Ember < cost || !HasRange(record);
            RerollText = string.Join(" | ", FindLabels(s_rerollLayer).Select(l => l.Text).Where(t => t != ""));
        };
        timer.Start();
    }

    /// <summary>A short flash and grow, as a landed number or the finished card.</summary>
    static void Pop(Control c)
    {
        if (!GodotObject.IsInstanceValid(c)) return;
        c.PivotOffset = c.Size / 2;
        Tween t = c.CreateTween();
        c.Modulate = new Color(1.6f, 1.4f, 1.1f);
        c.Scale = new Vector2(1.06f, 1.06f);
        t.SetParallel();
        t.TweenProperty(c, "modulate", Godot.Colors.White, 0.35);
        t.TweenProperty(c, "scale", Vector2.One, 0.25);
    }

    static IEnumerable<Label> FindLabels(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is Label l && l.Visible) yield return l;
            foreach (Label x in FindLabels(c)) yield return x;
        }
    }

    sealed record Landing(string Text, Color Color, Label? Change);

    /// <summary>
    /// One item card: icon, name in the rarity's colour, type and item level, then one line per affix. With
    /// <paramref name="was"/> (the right card) each line says how far it moved (green up, red down) and the tier's range.
    /// </summary>
    static (PanelContainer Card, List<(Label Label, AffixDef? Def, RolledAffix Affix, Landing Final, bool Landed)> Lines) Card(
        Game game, Item item, RolledItem record, List<double> values, List<double>? was, string caption)
    {
        Color rarity = Colors[record.Rarity];
        bool after = was != null;
        var card = new PanelContainer { CustomMinimumSize = new Vector2(330, 0) };
        StyleBoxFlat box = Style.Box(Style.Card, 6, after ? rarity : rarity.Darkened(0.55f), after ? 2 : 1, 14, 12);
        if (after) { box.ShadowColor = new Color(rarity.R, rarity.G, rarity.B, 0.25f); box.ShadowSize = 10; }
        card.AddThemeStyleboxOverride("panel", box);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 6);
        card.AddChild(v);
        Label cap = Style.Label(caption, after ? rarity : Style.Muted, 12);
        v.AddChild(cap);

        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        var iconFrame = new PanelContainer();
        iconFrame.AddThemeStyleboxOverride("panel", Style.Box(new Color(rarity.R, rarity.G, rarity.B, 0.10f), 5, rarity.Darkened(after ? 0.1f : 0.5f), 1, 4, 4));
        iconFrame.AddChild(new TextureRect { Texture = item.Icon, CustomMinimumSize = new Vector2(52, 52), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered });
        head.AddChild(iconFrame);
        var names = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        names.AddThemeConstantOverride("separation", 2);
        Label name = Style.Label(record.Name, after ? rarity : rarity.Darkened(0.25f), 17);
        names.AddChild(name);
        string baseName = game.Data.TryGetObject(record.BaseId, out Item? b) ? b.DisplayName.Singular : "";
        names.AddChild(Style.Label($"{RarityName(record.Rarity)} {baseName}  ·  item level {record.ILvl}", Style.Muted, 12));
        head.AddChild(names);
        v.AddChild(head);
        v.AddChild(new HSeparator());

        var lines = new List<(Label, AffixDef?, RolledAffix, Landing, bool)>();
        for (int i = 0; i < record.Affixes.Count && i < values.Count; i++)
        {
            RolledAffix x = record.Affixes[i];
            Affixes.TryGetValue(x.Id, out AffixDef? def);
            var shown = new RolledAffix { Id = x.Id, Tier = x.Tier, Style = x.Style, Value = values[i] };
            string text = def != null ? AffixLine(def, shown, false) : x.Id;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            Label line = Style.Label(text, after ? Style.Text : new Color(0.72f, 0.74f, 0.78f), 14);
            line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(line);
            Label? change = null;
            Color color = after ? Style.Text : line.GetThemeColor("font_color");
            if (after && def != null)
            {
                double diff = Math.Round(values[i] - was![i], 1);
                var (lo, hi) = def.Range(x.Tier);
                string n(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);
                string tier = lo == hi ? $"T{x.Tier} fixed" : $"T{x.Tier}  {n(lo)}–{n(hi)}";
                if (Math.Abs(diff) < 1e-9) change = Style.Label($"=   {tier}", Style.Muted, 12);
                else
                {
                    color = diff > 0 ? Style.Good : Style.Bad;
                    change = Style.Label($"{(diff > 0 ? "▲ +" : "▼ ")}{n(diff)}   {tier}", color, 12);
                }
                change.VerticalAlignment = VerticalAlignment.Center;
                change.Visible = false;
                row.AddChild(change);
            }
            else if (def != null)
            {
                Label t = Style.Label($"T{x.Tier}", Style.Muted, 12);
                t.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(t);
            }
            v.AddChild(row);
            if (after) lines.Add((line, def, x, new Landing(text, color, change), false));
        }
        return (card, lines);
    }
}
