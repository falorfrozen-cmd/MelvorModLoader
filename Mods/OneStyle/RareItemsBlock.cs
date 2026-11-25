using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.ItemEquipping;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.ItemCapabilities;
using GamesByMalcs.Melvor2.Modifiers;
using Godot;
using S = ItemCardStyle;

namespace OneStyle;

/// <summary>
/// The item card of a rare item, the one design of all our mods (Mods/Shared/ITEM-CARD-DESIGN.md, chosen 2026-11-25):
/// every effect is written once, in the part it comes from. The type line; the AFFIXES (the rolled part) with their
/// tiers and, with Alt, their ranges; a Unique's power and its fixed bonuses; the BASE ITEM BONUS that every item of
/// that base has, in a box of its own, written by the game's own bonus view; a Unique's story. The game's merged
/// "Bonuses" list is hidden for these items, so nothing shows twice. The same card in the item tooltip, the bank's
/// Selected Item panel, a worn item's tooltip; Better Tooltips gets it as text (<see cref="Lines"/>).
/// </summary>
public static partial class RareItems
{
    const string BlockName = "OneStyleItemBlock";
    const string BonusScene = "res://Modifiers/modifier_values_view.tscn";

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_BonusesContainer")]
    static extern VBoxContainer BonusesContainerOf(EquipmentCapabilityView view);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_BonusesView")]
    static extern ModifierValuesView BonusesViewOf(EquipmentSlotTooltip tooltip);

    /// <summary>Puts the card right after <paramref name="description"/> (hidden while it shows); other items get the label back.
    /// Returns true when the card is there (only then is the game's own bonus list hidden by the caller).</summary>
    static bool PlaceBlock(Label description, RolledItem? record)
    {
        Node? parent = description.GetParent();
        if (parent == null) return false;
        Control? have = parent.GetNodeOrNull<Control>(BlockName);
        if (record == null)
        {
            if (have != null) { parent.RemoveChild(have); have.QueueFree(); }
            // the label is the game's again: the game sets its text and whether it shows every time it draws an item
            // (an item without a description keeps it hidden), so nothing is forced here
            if (description.HasMeta(BlockName)) description.RemoveMeta(BlockName);
            return false;
        }
        description.Visible = false;
        description.SetMeta(BlockName, true);
        float width = description.Size.X > 120 ? description.Size.X : 320;
        string key = $"{record.LocalId}|{s_alt}|{RerollVersion}|{(int)width}";
        if (have != null && !have.IsQueuedForDeletion() && have.GetMeta("key", "").AsString() == key) return true;
        if (have != null) { parent.RemoveChild(have); have.QueueFree(); }
        Control block = Block(record, s_alt, width);
        block.Name = BlockName;
        block.SetMeta("key", key);
        parent.AddChild(block);
        parent.MoveChild(block, description.GetIndex() + 1);
        return true;
    }

    /// <summary>The game's merged bonus list of an item panel, hidden while the card shows (the game shows it again at its next item).</summary>
    static void HideGameBonuses(Node panel)
    {
        try
        {
            foreach (Node n in Walk(panel))
                if (n is EquipmentCapabilityView view) { BonusesContainerOf(view).Visible = false; return; }
        }
        catch (Exception e) { Once("bonus list", "One Style: the game's bonus list could not be hidden (it stays): " + e.Message); }
    }

    /// <summary>The same for a worn item's tooltip.</summary>
    static void HideGameBonuses(EquipmentSlotTooltip tip)
    {
        try { BonusesViewOf(tip).Visible = false; }
        catch (Exception e) { Once("worn bonus list", "One Style: the worn item's bonus list could not be hidden (it stays): " + e.Message); }
    }

    static readonly HashSet<string> s_said = new();
    static void Once(string what, string text) { if (s_said.Add(what)) Log(text); }

    static Label BlockLabel(string text, Color color, int size, bool wrap = false) => S.Label(text, color, size, wrap);

    /// <summary>The card itself (also for tests).</summary>
    public static Control Block(RolledItem record, bool alt, float width)
    {
        VBoxContainer v = S.Column();
        v.CustomMinimumSize = new Vector2(width, 0);
        Color rc = Colors[record.Rarity];
        if (record.UniqueId != "" && Uniques.Defs.TryGetValue(record.UniqueId, out UniqueDef? u))
        {
            Item? ubi = BaseItem(u.BaseId);
            v.AddChild(BlockLabel($"Unique  ·  {ubi?.DisplayName.Singular}  ·  level {u.Level}", S.Muted, S.TypeSize));
            if (u.MechanicName != "")
            {
                v.AddChild(S.Gap());
                PanelContainer power = S.PowerBox(rc);
                VBoxContainer pv = S.Column();
                pv.AddChild(S.Heading(u.MechanicName.ToUpperInvariant(), rc));
                pv.AddChild(BlockLabel(u.Mechanic, S.Text, S.EffectSize, wrap: true));
                power.AddChild(pv);
                v.AddChild(power);
            }
            if (u.Stats.Count > 0)
            {
                v.AddChild(S.Gap());
                v.AddChild(S.Heading("UNIQUE BONUSES", rc, "Fixed · no rolls"));
                foreach (UniqueStat stat in u.Stats) v.AddChild(BlockLabel(stat.Text, S.Text, S.EffectSize, wrap: true));
            }
            AddBase(v, ubi);
            v.AddChild(S.Gap());
            if (u.Flavour != "") v.AddChild(BlockLabel($"\"{u.Flavour}\"", S.Muted, S.TypeSize, wrap: true));
            v.AddChild(BlockLabel($"Drops from {u.SourceName}: {Uniques.Chance:0%} on each Unique kill.", S.Muted, S.TypeSize, wrap: true));
            v.AddChild(BlockLabel("Its power works while you play, not while the game works out the time it was closed.", S.Muted, S.AsideSize, wrap: true));
            return v;
        }
        Item? baseItem = BaseItem(record.BaseId);
        v.AddChild(BlockLabel($"{RarityName(record.Rarity)}  ·  {baseItem?.DisplayName.Singular}  ·  item level {record.ILvl}", S.Muted, S.TypeSize));
        v.AddChild(S.Gap());
        int rolled = record.Affixes.Count(r => Affixes.ContainsKey(r.Id));
        v.AddChild(S.Heading("AFFIXES", rc, $"{rolled} rolled · T1 best / T5 lowest"));
        foreach (RolledAffix r in record.Affixes)
        {
            if (!Affixes.TryGetValue(r.Id, out AffixDef? a)) continue;
            var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(Cell("◆", rc, 11, 10));
            row.AddChild(BlockLabel(AffixLine(a, r, false), S.Text, S.EffectSize, wrap: true));
            Label tier = Cell($"T{r.Tier}", S.Tier, S.TypeSize, S.TierWidth);
            tier.HorizontalAlignment = HorizontalAlignment.Right;
            row.AddChild(tier);
            if (alt)
            {
                var (lo, hi) = a.Range(r.Tier);
                static string n(double d) => d.ToString("0.#", CultureInfo.InvariantCulture);
                Label range = Cell(lo == hi ? $"[{n(lo)}]" : $"[{n(lo)}–{n(hi)}]", S.Range, S.TypeSize, S.RangeWidth);
                range.HorizontalAlignment = HorizontalAlignment.Right;
                row.AddChild(range);
            }
            v.AddChild(row);
        }
        AddBase(v, baseItem);
        if (!alt) v.AddChild(BlockLabel("Alt: affix ranges", S.Muted, S.HintSize));
        return v;
    }

    /// <summary>A label of a fixed width at the top of its row (the ◆, the tier, the range).</summary>
    static Label Cell(string text, Color color, int size, int width)
    {
        Label l = BlockLabel(text, color, size);
        l.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        l.CustomMinimumSize = new Vector2(width, 22);
        l.VerticalAlignment = VerticalAlignment.Center;
        return l;
    }

    static Item? BaseItem(string id) => s_game != null && s_game.Data.TryGetObject(id, out Item? b) ? b : null;

    /// <summary>The bonuses every item of this base has (null: none).</summary>
    static ModifierValues? BaseBonuses(Item? baseItem) =>
        baseItem != null && baseItem.Capabilities.TryGet(out EquipmentCapability? cap) && cap.Bonuses is { } b
        && (b.Global.Count + b.Component.Count + b.CombatEffect.Count) > 0 ? b : null;

    /// <summary>"BASE ITEM BONUS" in its box, written by the game's own bonus view; one muted line when the base has none.</summary>
    static void AddBase(VBoxContainer v, Item? baseItem)
    {
        if (baseItem == null) return;
        string baseName = baseItem.DisplayName.Singular;
        v.AddChild(S.Gap());
        if (BaseBonuses(baseItem) is not { } bonuses)
        {
            v.AddChild(BlockLabel($"{baseName} has no bonus of its own.", S.Muted, S.AsideSize, wrap: true));
            return;
        }
        PanelContainer box = S.BaseBox();
        VBoxContainer bv = S.Column(2);
        bv.AddChild(S.Heading("BASE ITEM BONUS", S.BaseHeading, rule: false));
        bv.AddChild(BlockLabel($"Fixed on every {baseName}", S.Muted, S.AsideSize, wrap: true));
        Control? game = GameBonusView(bonuses);
        if (game != null) bv.AddChild(game);
        else foreach (string line in BaseBonusLines(baseItem)) bv.AddChild(BlockLabel(line, S.Text, S.EffectSize, wrap: true));
        box.AddChild(bv);
        v.AddChild(box);
    }

    static PackedScene? s_bonusScene;
    static bool s_bonusSceneLooked;

    /// <summary>The game's own bonus view (its icons, conditions and wording) showing these bonuses; null if the game has no such scene any more.</summary>
    static Control? GameBonusView(ModifierValues bonuses)
    {
        try
        {
            if (!s_bonusSceneLooked)
            {
                s_bonusSceneLooked = true;
                s_bonusScene = ResourceLoader.Exists(BonusScene) ? GD.Load<PackedScene>(BonusScene) : null;
                if (s_bonusScene == null) Once("bonus scene", "One Style: the game's bonus view is not where it was; base item bonuses are written as text.");
            }
            if (s_bonusScene?.Instantiate() is not ModifierValuesView view) return null;
            view.MouseFilter = Control.MouseFilterEnum.Ignore;
            view.AddValues(bonuses);
            return view;
        }
        catch (Exception e) { Once("bonus view", "One Style: the game's bonus view could not be used; base item bonuses are written as text: " + e.Message); return null; }
    }

    static readonly Dictionary<string, List<string>> s_baseLines = new();

    /// <summary>The base item's bonuses as plain lines (for <see cref="Lines"/>, Better Tooltips, and if the game's view is gone).</summary>
    static List<string> BaseBonusLines(Item baseItem)
    {
        if (s_baseLines.TryGetValue(baseItem.Id, out List<string>? have)) return have;
        var lines = new List<string>();
        if (BaseBonuses(baseItem) is { } bonuses && GameBonusView(bonuses) is { } view)
        {
            try
            {
                // one line per bonus: each is one child of the view (a label, or a row of labels and icons)
                foreach (Node child in view.GetChildren())
                {
                    if (child is CanvasItem { Visible: false }) continue;
                    var parts = new List<string>();
                    foreach (Node n in Walk(child))
                    {
                        string t = n switch { RichTextLabel rt => rt.GetParsedText(), Label l => l.Text, _ => "" };
                        t = t.Replace("\n", " ").Trim();
                        if (t != "") parts.Add(t);
                    }
                    if (parts.Count > 0) lines.Add(string.Join(" ", parts));
                }
            }
            finally { view.Free(); }
        }
        s_baseLines[baseItem.Id] = lines;
        return lines;
    }
}
