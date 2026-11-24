using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Components.ItemEquipping;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Tooltips;
using GamesByMalcs.Melvor2.UIServices;
using Godot;

namespace OneStyle;

/// <summary>
/// How rare items look: a frame in the rarity's colour on the item's bank cell and equipment slot, the name in that colour
/// in every tooltip with "Magic / Rare / Unique" before its type, the affixes as the description, and with the Alt key
/// held each affix's tier (T1 best .. T5), whether it is a prefix or a suffix, and the tier's range.
/// A short note at the top of the screen when one drops.
/// </summary>
public static partial class RareItems
{
    const string FrameName = "OneStyleRarity";

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_InfoTooltip")]
    static extern ItemInfoTooltip InfoTooltipOf(ItemTooltipService service);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ItemInfo")]
    static extern ItemInfoPanel ItemInfoOf(ItemInfoTooltip tooltip);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ItemName")]
    static extern Label ItemNameOf(ItemInfoPanel panel);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ItemType")]
    static extern Label ItemTypeOf(ItemInfoPanel panel);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ItemDescription")]
    static extern Label ItemDescriptionOf(ItemInfoPanel panel);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Slot")]
    static extern IActiveEquipmentSlot? SlotOfView(EquipmentSlotView view);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Slot")]
    static extern IActiveEquipmentSlot? SlotOfTooltip(EquipmentSlotTooltip tooltip);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_NameLabel")]
    static extern Label NameOfTooltip(EquipmentSlotTooltip tooltip);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_DescriptionLabel")]
    static extern Label DescriptionOfTooltip(EquipmentSlotTooltip tooltip);

    // ================================================================ tooltips

    /// <summary>Wraps the game's item tooltip service: the game draws its tooltip, then the rare item's lines go in.</summary>
    sealed class Wrapped(IItemTooltipService inner) : IItemTooltipService
    {
        public readonly IItemTooltipService Inner = inner;
        public void ShowForItem(Control target, Item item, ITooltipConfig config, bool showIcon, bool isInteractive, string? customHeader = null)
        {
            Inner.ShowForItem(target, item, config, showIcon, isInteractive, customHeader);
            Decorate(item);
        }
        public void ShowForItem(Control target, Item item, IInventory inventory, ITooltipConfig config, bool showIcon, bool isInteractive, string? customHeader = null)
        {
            Inner.ShowForItem(target, item, inventory, config, showIcon, isInteractive, customHeader);
            Decorate(item);
        }
        public void ShowForItemStack(Control target, IReadonlyItemStack itemStack, ITooltipConfig config, bool showIcon, bool isInteractive, string? customHeader = null)
        {
            Inner.ShowForItemStack(target, itemStack, config, showIcon, isInteractive, customHeader);
            Decorate(itemStack.Item);
        }
        public void Hide()
        {
            s_tooltipItem = null;
            Inner.Hide();
        }
    }

    static Item? s_tooltipItem;
    static bool s_alt;

    /// <summary>The game gives its tooltip service out again when the game screen is made: wrapped again then.</summary>
    public static void WrapTooltips()
    {
        try
        {
            if (UIServiceLocator.ItemTooltipService is { } current and not Wrapped) UIServiceLocator.Provide(new Wrapped(current));
        }
        catch (Exception e) { Log("One Style: the item tooltip could not be wrapped: " + e.Message); }
    }

    static ItemInfoTooltip? GameTooltip() =>
        UIServiceLocator.ItemTooltipService is Wrapped { Inner: ItemTooltipService s } ? InfoTooltipOf(s) : null;

    static void Decorate(Item item)
    {
        s_tooltipItem = item;
        try
        {
            if (GameTooltip() is not { } tooltip) return;
            DecoratePanel(ItemInfoOf(tooltip), item);
        }
        catch (Exception e) { Log("One Style: a tooltip could not be drawn: " + e.Message); }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Item")]
    static extern Item? PanelItemOf(ItemInfoPanel panel);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_ItemStack")]
    static extern IReadonlyItemStack? PanelStackOf(ItemInfoPanel panel);

    /// <summary>
    /// The game's item panel (in the tooltip, and the bank's "Selected Item" panel): the name in the rarity's colour,
    /// "Rare Helmet" as its type, and the affix lines (with tiers while Alt is held).
    /// </summary>
    static void DecoratePanel(ItemInfoPanel info, Item? item)
    {
        Label name = ItemNameOf(info), type = ItemTypeOf(info), description = ItemDescriptionOf(info);
        if (item == null || RecordOf(item) is not { } record)
        {
            name.Modulate = Godot.Colors.White;
            description.Modulate = Godot.Colors.White;
            return;
        }
        name.Modulate = Colors[record.Rarity];
        if (!type.Text.StartsWith(RarityName(record.Rarity))) type.Text = $"{RarityName(record.Rarity)} {type.Text}";
        string text = Lines(record, s_alt);
        if (description.Text != text) description.Text = text;
        description.Modulate = record.Rarity == Rarity.Unique ? new Color(1f, 0.78f, 0.55f) : new Color(0.72f, 0.82f, 1f);
        description.Visible = true;
    }

    /// <summary>The text under a rare item's name: its affixes (tiers with Alt), or a unique's power and story.</summary>
    public static string Lines(RolledItem record, bool tiers)
    {
        if (record.UniqueId != "" && Uniques.Defs.TryGetValue(record.UniqueId, out UniqueDef? u))
        {
            // its stats are in the game's own "Bonuses" list just below: not written twice
            string baseName = s_game != null && s_game.Data.TryGetObject(u.BaseId, out Item? ub) ? ub.DisplayName.Singular : "";
            var lines = new List<string> { $"Unique {baseName}, level {u.Level}" };
            if (u.MechanicName != "") lines.Add($"{u.MechanicName}: {u.Mechanic}");
            if (u.Flavour != "") lines.Add($"\"{u.Flavour}\"");
            lines.Add($"Drops from {u.SourceName} ({Uniques.Chance:0%} each time you kill it).");
            lines.Add("Its power works while you play, not while the game works out the time it was closed.");
            return string.Join("\n", lines);
        }
        string what = s_game != null && s_game.Data.TryGetObject(record.BaseId, out Item? b) ? b.DisplayName.Singular : "";
        string head = $"{RarityName(record.Rarity)} {what}, item level {record.ILvl}, {record.Affixes.Count} affixes";
        if (tiers) return $"{head}\n{Describe(record, true)}";
        // one line per affix, always: the game's own "Bonuses" list below splits some into many lines ("+2% combat XP" is
        // six skills, "avoid Poison, Bleed and Burn" three) and cannot show others at all (Ember, chances on hit...)
        return $"{head}\n{Describe(record)}\nHold Alt for tiers and ranges.";
    }

    /// <summary>
    /// For our other mods (Bulk Sell, Phone Screen...), found by reflection so they work without One Style: what a rare or
    /// unique item is, or null for any other item. [0] rarity ("Magic", "Rare", "Unique"), [1] item level, [2] the base
    /// item's name, [3] the rarity colour as "rrggbb", then one line per affix (a Unique: its power's name and text).
    /// </summary>
    public static string[]? Facts(Item item)
    {
        if (RecordOf(item) is not { } record) return null;
        Godot.Color c = Colors[record.Rarity];
        var facts = new List<string> { RarityName(record.Rarity), "", "", c.ToHtml(false) };
        if (record.UniqueId != "" && Uniques.Defs.TryGetValue(record.UniqueId, out UniqueDef? u))
        {
            facts[1] = u.Level.ToString(System.Globalization.CultureInfo.InvariantCulture);
            facts[2] = s_game != null && s_game.Data.TryGetObject(u.BaseId, out Item? ub) ? ub.DisplayName.Singular : "";
            if (u.MechanicName != "") facts.Add($"{u.MechanicName}: {u.Mechanic}");
            return facts.ToArray();
        }
        facts[1] = record.ILvl.ToString(System.Globalization.CultureInfo.InvariantCulture);
        facts[2] = s_game != null && s_game.Data.TryGetObject(record.BaseId, out Item? b) ? b.DisplayName.Singular : "";
        foreach (RolledAffix r in record.Affixes)
            if (Affixes.TryGetValue(r.Id, out AffixDef? a)) facts.Add(AffixLine(a, r, false));
        return facts.ToArray();
    }


    // ================================================================ colours on cells, slots and worn items

    static int s_frame;

    /// <summary>Called every frame by the mod (main thread): Alt, and every few frames the colours on the screen.</summary>
    public static void FrameTick()
    {
        if (s_game == null || Engine.GetMainLoop() is not SceneTree tree) return;
        bool alt = Input.IsKeyPressed(Key.Alt);
        if (alt != s_alt)
        {
            s_alt = alt;
            if (s_tooltipItem != null) Decorate(s_tooltipItem);
        }
        if (++s_frame % 6 != 0) return;
        BetterTooltipsPass();
        try
        {
            foreach (Node n in Walk(tree.Root))
                switch (n)
                {
                    case ItemStackButton button:
                        Frame(button, button.Stack?.Item);
                        break;
                    case EquipmentSlotView view:
                        IActiveEquipmentSlot? slot = SlotOfView(view);
                        Frame(view, slot is { IsEmpty: false, IsOccupied: false } ? slot.Equipped!.Item : null);
                        break;
                    case GamesByMalcs.Melvor2.Components.CombatLoot.CombatDropsContainer drops when drops.IsVisibleInTree():
                        DropList(drops);
                        break;
                    case GamesByMalcs.Melvor2.Components.CombatAreas.EnemyOptionsPanel card when card.IsVisibleInTree():
                        DropIcons(card);
                        break;
                    case GamesByMalcs.Melvor2.Components.ItemOpening.ItemDropView row:
                        DropRow(row);
                        break;
                    case ItemInfoPanel panel when panel.IsVisibleInTree() && panel.GetParent() is not ItemInfoTooltip:
                        DecoratePanel(panel, PanelStackOf(panel)?.Item ?? PanelItemOf(panel));
                        RerollPanel(panel, PanelStackOf(panel)?.Item ?? PanelItemOf(panel));
                        break;
                    case EquipmentSlotTooltip tip when tip.IsVisibleInTree():
                        Worn(tip);
                        break;
                }
        }
        catch (Exception e) { Log("One Style: rare item colours: " + e.Message); }
    }

    /// <summary>For tests: what the last pass over the screen found (cells, cells with an item, rare cells).</summary>
    public static string FrameReport()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return "no tree";
        int cells = 0, withItem = 0, rare = 0;
        foreach (Node n in Walk(tree.Root))
            if (n is ItemStackButton b)
            {
                cells++;
                if (b.Stack?.Item is { } i) { withItem++; if (RecordOf(i) != null) rare++; }
            }
        return $"game {(s_game != null ? "bound" : "none")}, frame {s_frame}, cells {cells}, with an item {withItem}, rare {rare}";
    }

    static IEnumerable<Node> Walk(Node node)
    {
        var stack = new Stack<Node>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            Node n = stack.Pop();
            yield return n;
            if (n is CanvasItem { Visible: false }) continue;
            foreach (Node c in n.GetChildren()) stack.Push(c);
        }
    }

    /// <summary>A coloured frame on a cell holding a rare item, none on others (cells are reused for other items).</summary>
    static void Frame(Control cell, Item? item)
    {
        Panel? frame = cell.GetNodeOrNull<Panel>(FrameName);
        RolledItem? record = item != null ? RecordOf(item) : null;
        if (record == null)
        {
            if (frame != null) frame.Visible = false;
            return;
        }
        if (frame == null)
        {
            frame = new Panel { Name = FrameName, MouseFilter = Control.MouseFilterEnum.Ignore };
            frame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            cell.AddChild(frame);
        }
        if (frame.GetMeta("rarity", -1).AsInt32() != (int)record.Rarity)
        {
            // a Unique's frame in the deeper orange (#C8641E), its name in the lighter one
            // a Unique's frame in a deep red-orange, well apart from a Rare's gold
            Color c = record.Rarity == Rarity.Unique ? new Color(0.86f, 0.33f, 0.10f) : Colors[record.Rarity];
            var box = new StyleBoxFlat { BgColor = new Color(c.R, c.G, c.B, 0.13f), BorderColor = c };
            box.SetBorderWidthAll(record.Rarity == Rarity.Unique ? 3 : 2);
            box.SetCornerRadiusAll(4);
            frame.AddThemeStyleboxOverride("panel", box);
            frame.SetMeta("rarity", (int)record.Rarity);
        }
        frame.Visible = true;
    }

    /// <summary>The tooltip of a worn item: its name in the rarity's colour and its affixes (tiers with Alt).</summary>
    static void Worn(EquipmentSlotTooltip tip)
    {
        IActiveEquipmentSlot? slot = SlotOfTooltip(tip);
        Label name = NameOfTooltip(tip), description = DescriptionOfTooltip(tip);
        if (slot is not { IsEmpty: false } || RecordOf(slot.Equipped!.Item) is not { } record)
        {
            name.Modulate = Colors[Rarity.Normal];
            return;
        }
        name.Modulate = Colors[record.Rarity];
        string text = Lines(record, s_alt);
        if (description.Text != text) description.Text = text;
        description.Visible = true;
    }

    // ================================================================ a drop

    static CanvasLayer? s_toastLayer;

    /// <summary>A short note at the top of the screen: "Rare item: Grim Fang (Bronze Sword)".</summary>
    public static void DropToast(RolledItem record)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree || System.Threading.Thread.CurrentThread.ManagedThreadId != MainThread) return;
            if (s_toastLayer == null || !GodotObject.IsInstanceValid(s_toastLayer))
            {
                s_toastLayer = new CanvasLayer { Layer = 91 };
                tree.Root.AddChild(s_toastLayer);
                var column = new VBoxContainer { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore };
                column.AddThemeConstantOverride("separation", 6);
                // bottom right, above the game's bottom bar: out of the way of names, bars and tabs
                column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomRight);
                column.GrowHorizontal = Control.GrowDirection.Begin;
                column.GrowVertical = Control.GrowDirection.Begin;
                column.OffsetRight = -24;
                column.OffsetBottom = -70;
                column.Alignment = BoxContainer.AlignmentMode.End;
                s_toastLayer.AddChild(column);
            }
            var col = s_toastLayer.GetNode<VBoxContainer>("Column");
            string baseName = s_game != null && s_game.Data.TryGetObject(record.BaseId, out Item? b) ? b.DisplayName.Singular : "";
            string text = record.Rarity == Rarity.Unique ? $"Unique item: {record.Name}" :
                record.Rarity == Rarity.Magic ? $"Magic item: {record.Name}" : $"Rare item: {record.Name} ({baseName})";
            // at most three at a time, the oldest goes first; each on a dark plate so they never run into the screen behind
            while (col.GetChildCount() >= 3) { Node oldest = col.GetChild(0); col.RemoveChild(oldest); oldest.QueueFree(); }
            Color c = Colors[record.Rarity];
            var plate = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd };
            var box = new StyleBoxFlat { BgColor = new Color(0.04f, 0.03f, 0.02f, 0.86f), BorderColor = c };
            box.SetBorderWidthAll(1);
            box.BorderWidthLeft = 4;
            box.SetCornerRadiusAll(5);
            box.SetContentMarginAll(7);
            box.ContentMarginLeft = 14;
            box.ContentMarginRight = 14;
            plate.AddThemeStyleboxOverride("panel", box);
            var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", record.Rarity == Rarity.Unique ? 22 : 18);
            label.AddThemeColorOverride("font_color", c);
            plate.AddChild(label);
            col.AddChild(plate);
            tree.CreateTimer(4.0).Timeout += () => { if (GodotObject.IsInstanceValid(plate)) plate.QueueFree(); };
        }
        catch (Exception e) { Log("One Style: drop note: " + e.Message); }
    }

    public static int MainThread;
}
