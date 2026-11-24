using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Localization;
using Godot;

namespace OneStyle;

/// <summary>
/// Ember's first use (denge-2, Codex C6): reroll a Magic or Rare item's numbers. The affixes and their tiers stay; each
/// value is rolled again inside its tier's range, the player sees old and new side by side and keeps one. It costs
/// 2 x b Ember, b = min(5, 1 + item level / 20): 2 to 10. Unique items are not rerolled. Only an item in the bank (not
/// worn), from the bank's Selected Item panel. The new values wait in the item's record (the save) until chosen, so a
/// paid roll is not lost by closing the game.
/// </summary>
public static partial class RareItems
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Bonuses")]
    static extern void SetBonuses(EquipmentCapability cap, GamesByMalcs.Melvor2.Modifiers.ModifierValues? bonuses);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Description")]
    static extern void SetDescription(Item item, LocalizedString? description);

    public static int RerollCost(RolledItem record) => 2 * Math.Min(5, 1 + record.ILvl / 20);

    public static bool CanReroll(RolledItem record) => record.UniqueId == "" && record.Rarity is Rarity.Magic or Rarity.Rare or Rarity.Rare4;

    static double RollValue(AffixDef a, int tier)
    {
        var (min, max) = a.Range(tier);
        return Math.Round(min + a.Step * s_rng.Next(0, (int)Math.Round((max - min) / a.Step) + 1), 1);
    }

    /// <summary>Pays the Ember and rolls new values (kept in the record until <see cref="FinishReroll"/>). False when Ember is short.</summary>
    public static bool StartReroll(RolledItem record)
    {
        if (!CanReroll(record) || record.Reroll != null) return false;
        int cost = RerollCost(record);
        if (!Elites.SpendEmber(cost)) return false;
        record.Reroll = record.Affixes.Select(x => Affixes.TryGetValue(x.Id, out AffixDef? a) ? RollValue(a, x.Tier) : x.Value).ToList();
        RerollVersion++;
        Log($"One Style: {record.Name} rerolled for {cost} Ember: {string.Join(", ", record.Reroll)}.");
        return true;
    }

    /// <summary>Keeps the new values (<paramref name="takeNew"/>) or the old ones; the game item takes them at once.</summary>
    public static void FinishReroll(Game game, RolledItem record, bool takeNew)
    {
        if (record.Reroll is not { } values) return;
        record.Reroll = null;
        RerollVersion++;
        if (!takeNew || values.Count != record.Affixes.Count) return;
        for (int i = 0; i < values.Count; i++) record.Affixes[i].Value = values[i];
        var registry = game.Data.GetDataRegistryOutput<Item>();
        if (registry.TryGetObjectById("MelvorBase:" + record.LocalId, out Item? item) && item.Capabilities.TryGet(out EquipmentCapability? cap)
            && game.Data.TryGetObject(record.BaseId, out Item? baseItem) && baseItem.Capabilities.TryGet(out EquipmentCapability? baseCap))
        {
            SetBonuses(cap, AffixBonuses(game, record.Affixes, baseCap.Bonuses));
            SetDescription(item, new LocalizedString(Describe(record)));
        }
        UpdateCaps(game);
    }

    /// <summary>Changes whenever a reroll starts or ends: the panel draws itself again.</summary>
    public static int RerollVersion { get; private set; }

    const string RerollName = "OneStyleReroll";

    /// <summary>The reroll part under a rare item in the bank's Selected Item panel (not in tooltips, not for worn items).</summary>
    static void RerollPanel(ItemInfoPanel info, Item? item)
    {
        Label description = ItemDescriptionOf(info);
        Node? holder = description.GetParent();
        if (holder == null) return;
        Node? have = holder.GetNodeOrNull(RerollName);
        bool inBank = false;
        for (Node? n = info; n != null && !inBank; n = n.GetParent()) inBank = n is SelectedItemView;
        RolledItem? record = item == null ? null : RecordOf(item);
        if (!inBank || record == null || !CanReroll(record) || s_game is not { } game)
        {
            if (have != null) { holder.RemoveChild(have); have.QueueFree(); }
            return;
        }
        string key = $"{record.LocalId}|{RerollVersion}|{Elites.Progress.Ember}";
        if (have != null && !have.IsQueuedForDeletion() && have.GetMeta("key", "").AsString() == key) return;
        if (have != null) { holder.RemoveChild(have); have.QueueFree(); }

        var box = new VBoxContainer { Name = RerollName };
        box.SetMeta("key", key);
        box.AddThemeConstantOverride("separation", 4);
        void Text(string s, Color c)
        {
            var l = new Label { Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(260, 0) };
            l.AddThemeFontSizeOverride("font_size", 14);
            l.AddThemeColorOverride("font_color", c);
            box.AddChild(l);
        }
        Button Button(string s, Action pressed, bool enabled = true)
        {
            var b = new Button { Text = s, Disabled = !enabled, FocusMode = Control.FocusModeEnum.None };
            b.AddThemeFontSizeOverride("font_size", 13);
            b.Pressed += () =>
            {
                try { pressed(); }
                catch (Exception e) { Log("One Style: reroll failed: " + e.Message); }
                RefreshSelected(info);
            };
            return b;
        }
        box.AddChild(new HSeparator());
        int cost = RerollCost(record);
        long ember = Elites.Progress.Ember;
        if (record.Reroll is not { } values)
        {
            Text($"Reroll values: {cost} Ember (you have {ember:N0}). The same affixes and tiers, new numbers inside each tier's range; "
                 + "you then keep the current or the new ones. The Ember is spent either way. "
                 + "Magic and Rare items in your bank only (not worn); Unique items cannot be rerolled.", new Color(1f, 0.75f, 0.45f));
            box.AddChild(Button($"Reroll values ({cost} Ember)", () => StartReroll(record), ember >= cost));
        }
        else
        {
            Text("Current  ->  New (paid; your choice is kept until you pick, even after a restart). "
                 + "The game's \"Bonuses\" list below still shows the current values:", new Color(1f, 0.75f, 0.45f));
            for (int i = 0; i < record.Affixes.Count && i < values.Count; i++)
            {
                RolledAffix old = record.Affixes[i];
                if (!Affixes.TryGetValue(old.Id, out AffixDef? a)) continue;
                string now = AffixLine(a, old, false), next = AffixLine(a, new RolledAffix { Id = old.Id, Tier = old.Tier, Value = values[i], Style = old.Style }, false);
                var (lo, hi) = a.Range(old.Tier);
                double diff = values[i] - old.Value;
                string change = Math.Abs(diff) < 1e-9 ? "same" : (diff > 0 ? "+" : "") + diff.ToString("0.#", CultureInfo.InvariantCulture);
                next += $"   ({change}; T{old.Tier}: {lo.ToString("0.#", CultureInfo.InvariantCulture)}-{hi.ToString("0.#", CultureInfo.InvariantCulture)})";
                Color c = Math.Abs(values[i] - old.Value) < 1e-9 ? new Color(0.85f, 0.85f, 0.85f) : values[i] > old.Value ? new Color(0.55f, 1f, 0.55f) : new Color(1f, 0.55f, 0.5f);
                Text($"{now}   ->   {next}", c);
            }
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(Button("Take the new values", () => FinishReroll(game, record, true)));
            row.AddChild(Button("Keep the old ones", () => FinishReroll(game, record, false)));
            box.AddChild(row);
        }
        holder.AddChild(box);
        holder.MoveChild(box, description.GetIndex() + 1);
    }

    /// <summary>The bank's Selected Item panel draws the item again (its "Bonuses" list shows the new values).</summary>
    static void RefreshSelected(Node from)
    {
        for (Node? n = from; n != null; n = n.GetParent())
            if (n is SelectedItemView view) { view.Update(); return; }
    }
}
