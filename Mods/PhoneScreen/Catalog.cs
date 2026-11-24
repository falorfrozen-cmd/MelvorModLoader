using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ActionQueue;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.CombatFood;
using GamesByMalcs.Melvor2.Components.Experience;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Combat;
using Godot;

namespace PhoneScreen;

/// <summary>
/// What the phone needs besides the live snapshot, made on the game's own thread: the catalogue (every skill with its
/// targets and whether each can be started, foods, stances, saved queues, the bank's items), pictures of skills,
/// items and monsters, the Combat Advisor's verdicts for an area, and the phone's commands. Other mods (Action Queue,
/// Combat Advisor) are reached by name, so all of this works with or without them.
/// </summary>
public sealed class Catalog
{
    int _version;
    public DateTime Made { get; private set; }

    // ------------------------------------------------------------------------------------------- the catalogue

    public byte[] Build(Game game)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("version", ++_version);
            w.WriteString("character", game.CharacterName);
            Skills(w, game);
            Combat(w, game);
            Bank(w, game);
            w.WriteStartArray("presets");
            foreach (string name in Presets()) w.WriteStringValue(name);
            w.WriteEndArray();
            w.WriteBoolean("advisor", FindType("CombatAdvisor.Forecast") != null);
            w.WriteEndObject();
        }
        Made = DateTime.UtcNow;
        return ms.ToArray();
    }

    static void Skills(Utf8JsonWriter w, Game game)
    {
        w.WriteStartArray("skills");
        foreach (var (entity, action) in Targets.Skills(game))
        {
            w.WriteStartObject();
            w.WriteString("id", entity.Id);
            w.WriteString("name", entity.DisplayName.Text);
            if (entity.Components.Values.OfType<ExperienceComponent>().FirstOrDefault() is { } xp)
            {
                w.WriteNumber("level", xp.Level);
                double here = StandardExperienceTable.Default.GetXpForLevel(xp.Level), next = StandardExperienceTable.Default.GetXpForLevel(Math.Min(xp.Level + 1, 120));
                w.WriteNumber("xp", next > here ? Math.Round(Math.Clamp((xp.Xp - here) / (next - here), 0, 1), 3) : 1);
            }
            bool combat = action is CombatActionComponent;
            w.WriteBoolean("combat", combat);
            w.WriteBoolean("running", action.IsStarted);
            Target? now = Targets.Current(action);
            w.WriteString("current", now == null ? "" : now.Id + "|" + now.Extra);
            w.WriteStartArray("targets");
            var areas = combat && action.Entity is CombatEntity ce ? ce.Areas.ActiveAreas.Keys.ToDictionary(a => a.Id) : new Dictionary<string, BaseCombatArea>();
            foreach (Target t in Targets.All(action))
            {
                bool can = Targets.CanStart(action, t, out string why);
                if (can && !combat && !Targets.HasMaterials(action, t)) { can = false; why = "not enough materials in the bank"; }
                w.WriteStartObject();
                w.WriteString("id", t.Id);
                w.WriteString("extra", t.Extra);
                string name = t.Name;
                if (combat && t.Extra is not (Targets.Dungeon or Targets.Slayer))
                {
                    int cut = name.IndexOf("  (", StringComparison.Ordinal);
                    if (cut > 0) name = name[..cut];
                    if (areas.TryGetValue(t.Extra, out BaseCombatArea? area)) w.WriteString("area", area.DisplayName.Text);
                    w.WriteString("icon", "enemy/" + t.Id);
                    if (area is CombatArea normal && normal.Enemies.FirstOrDefault(e => e.Id == t.Id) is { } enemy) w.WriteNumber("level", enemy.CombatLevel);
                }
                else if (!combat && Targets.Product(action, t) is { } product) w.WriteString("icon", "item/" + product.Id);
                w.WriteString("name", name);
                w.WriteBoolean("can", can);
                if (!can) w.WriteString("why", why);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    static void Combat(Utf8JsonWriter w, Game game)
    {
        if (!game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) || e is not CombatEntity combat) { w.WriteNull("combat"); return; }
        w.WriteStartObject("combat");
        var type = combat.Equipment.CurrentWeapon.Weapon.Type;
        AttackStance? stance = combat.Player.GetSelectedStance(type);
        w.WriteString("stance", stance?.Id ?? "");
        w.WriteStartArray("stances");
        foreach (AttackStance s in type.Stances) { w.WriteStartObject(); w.WriteString("id", s.Id); w.WriteString("name", s.DisplayName.Text); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WriteString("food", combat.Food.SelectedFood?.Item.Id ?? "");
        w.WriteStartArray("foods");
        if (game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? b) && b is BankEntity bank)
            foreach (var group in bank.Inventory.Stacks.GroupBy(x => x.Item))
                if (group.Key.Capabilities.TryGet(out FoodCapability? food))
                {
                    w.WriteStartObject();
                    w.WriteString("id", group.Key.Id);
                    w.WriteString("name", group.Key.DisplayName.GetPluralText(2));
                    w.WriteNumber("count", group.Sum(x => (long)x.Quantity));
                    w.WriteNumber("heal", food.BaseHealing);
                    w.WriteEndObject();
                }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    static void Bank(Utf8JsonWriter w, Game game)
    {
        w.WriteStartArray("bank");
        if (game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? b) && b is BankEntity bank)
            foreach (var group in bank.Inventory.Stacks.GroupBy(x => x.Item).Take(3000))
            {
                Item item = group.Key;
                w.WriteStartObject();
                w.WriteString("id", item.Id);
                w.WriteString("name", item.DisplayName.GetPluralText(1));
                w.WriteNumber("qty", group.Sum(x => (long)x.Quantity));
                w.WriteNumber("price", item.SellsFor.Quantity);
                if (group.Any(x => x.IsLocked)) w.WriteBoolean("locked", true);
                if (item.Capabilities.Contains<FoodCapability>()) w.WriteBoolean("food", true);
                Rares.Write(w, item);
                w.WriteEndObject();
            }
        w.WriteEndArray();
    }

    // ------------------------------------------------------------------------------------------- pictures

    /// <summary>A picture of the game's, as a small PNG (null: there is none). On the game's thread only.</summary>
    public static byte[]? Picture(Game game, string key)
    {
        int slash = key.IndexOf('/');
        string kind = key[..slash], id = key[(slash + 1)..];
        Texture2D? texture = null;
        try
        {
            texture = kind switch
            {
                "skill" => game.Entities.TryGetObjectById(id, out GameEntity? entity) ? entity.Icon : null,
                "item" => game.Data.GetDataRegistryOutput<Item>().GetAllObjects().FirstOrDefault(i => i.Id == id)?.Icon,
                "enemy" => game.Data.GetDataRegistryOutput<Enemy>().GetAllObjects().FirstOrDefault(x => x.Id == id)?.Sprite,
                "shop" => IconOf(game.Data.GetDataRegistryOutput<GamesByMalcs.Melvor2.Entities.Shop.ShopPurchase>().GetAllObjects().FirstOrDefault(x => x.Id == id)),
                "prayer" => IconOf(game.Data.GetDataRegistryOutput<GamesByMalcs.Melvor2.Components.Prayers.Prayer>().GetAllObjects().FirstOrDefault(x => x.Id == id)),
                "spell" => IconOf(game.Data.GetDataRegistryOutput<GamesByMalcs.Melvor2.Components.MagicSpells.MagicSpell>().GetAllObjects().FirstOrDefault(x => x.Id == id)),
                "imbue" => IconOf(game.Data.GetDataRegistryOutput<GamesByMalcs.Melvor2.Components.MagicSpells.Imbuement>().GetAllObjects().FirstOrDefault(x => x.Id == id)),
                "master" => IconOf(game.Data.GetDataRegistryOutput<GamesByMalcs.Melvor2.Components.SlayerTasks.SlayerMaster>().GetAllObjects().FirstOrDefault(x => x.Id == id)),
                _ => null,
            };
            if (texture == null) return null;
            Image? image = texture.GetImage();
            if (image == null || image.IsEmpty()) return null;
            image = (Image)image.Duplicate();
            if (image.IsCompressed()) image.Decompress();
            int size = kind == "enemy" ? 96 : 64, wpx = image.GetWidth(), hpx = image.GetHeight();
            if (wpx > size || hpx > size)
            {
                double scale = (double)size / Math.Max(wpx, hpx);
                image.Resize(Math.Max(1, (int)(wpx * scale)), Math.Max(1, (int)(hpx * scale)), Image.Interpolation.Lanczos);
            }
            return image.SavePngToBuffer();
        }
        catch { return null; }
    }

    static Texture2D? IconOf(object? o) => o?.GetType().GetProperty("Icon")?.GetValue(o) as Texture2D;

    // ------------------------------------------------------------------------------------------- the Combat Advisor

    sealed class AdviceJob
    {
        public required string AreaId;
        public required BaseCombatArea Area;
        public required List<Enemy> Enemies;
        public object? Setup;
        public int Next;
        public readonly List<(Enemy Enemy, object Outlook)> Done = new();
    }

    AdviceJob? _advice;

    /// <summary>Works out the verdicts for one area a little at a time (each frame); null when nothing is left to do.</summary>
    public (string Area, byte[] Json)? Advise(Game game, Func<string?> nextArea)
    {
        if (_advice == null)
        {
            string? id = nextArea();
            if (id == null) return null;
            if (!game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) || e is not CombatEntity combat) return (id, Json(new { available = false }));
            BaseCombatArea? area = combat.Areas.ActiveAreas.Keys.FirstOrDefault(a => a.Id == id);
            Type? forecast = FindType("CombatAdvisor.Forecast");
            if (area is not CombatArea normal || forecast == null) return (id, Json(new { available = forecast != null, monsters = Array.Empty<object>() }));
            _advice = new AdviceJob { AreaId = id, Area = area, Enemies = normal.Enemies.Where(x => x.Requirements.AreMet).ToList() };
            _advice.Setup = forecast.GetMethod("Current")?.Invoke(null, new object?[] { game, null });
            if (_advice.Setup == null) { _advice = null; return (id, Json(new { available = false })); }
        }
        AdviceJob job = _advice;
        Type f = FindType("CombatAdvisor.Forecast")!;
        MethodInfo? forMethod = f.GetMethod("For");
        ulong start = Time.GetTicksMsec();
        while (job.Next < job.Enemies.Count && Time.GetTicksMsec() - start < 6)
        {
            Enemy enemy = job.Enemies[job.Next++];
            try { if (forMethod?.Invoke(null, new object?[] { game, job.Setup, job.Area, enemy }) is { } o) job.Done.Add((enemy, o)); }
            catch { }
        }
        if (job.Next < job.Enemies.Count || job.Done.Any(d => Member(d.Outlook, "Ready") is false)) return null;
        _advice = null;
        var monsters = job.Done.Select(d =>
        {
            object o = d.Outlook;
            object? main = Member(o, "MainXp");
            string skill = "";
            double xp = 0;
            if (main != null)
            {
                object? level = main.GetType().GetField("Item1")?.GetValue(main);
                xp = main.GetType().GetField("Item2")?.GetValue(main) is double v ? v : 0;
                if (level != null && Member(level, "Id") is string levelId) skill = levelId.Contains(':') ? levelId[(levelId.IndexOf(':') + 1)..] : levelId;
            }
            return new
            {
                id = d.Enemy.Id, verdict = Member(o, "Verdict")?.ToString() ?? "Unknown", why = Member(o, "Why")?.ToString() ?? "",
                kills = Math.Round(Convert.ToDouble(Member(o, "KillsPerHour") ?? 0.0)), food = Math.Round(Convert.ToDouble(Member(o, "FoodPerHour") ?? 0.0), 1),
                xp = Math.Round(xp), skill,
            };
        }).ToList();
        return (job.AreaId, Json(new { available = true, monsters }));
    }

    // ------------------------------------------------------------------------------------------- commands

    static readonly MethodInfo? s_select = typeof(Targets).GetMethod("Select", BindingFlags.NonPublic | BindingFlags.Static);

    /// <summary>Starts a target the way a click in the game does (with Multitask, what ran before keeps running).</summary>
    public static (bool, string) Start(Game game, string skill, string targetId, string extra)
    {
        ActionComponent? action = Targets.Find(game, skill);
        if (action == null) return (false, "That skill is not here.");
        Target? target = Targets.All(action).FirstOrDefault(t => t.Id == targetId && t.Extra == extra);
        if (target == null) return (false, "That is no longer in the game.");
        if (!Targets.CanStart(action, target, out string problem)) return (false, $"Cannot start {target.Name}: {problem}.");
        if (action is not CombatActionComponent && !Targets.HasMaterials(action, target)) return (false, $"Cannot start {target.Name}: not enough materials in the bank.");
        bool queueWas = QueueRunning();
        if (s_select == null) return (false, "This version cannot start actions.");
        s_select.Invoke(null, new object[] { action, target });
        if (!Targets.IsDoing(action, target)) return (false, $"{target.Name} could not be started.");
        string name = $"{action.Entity.DisplayName.Text}: {target.Name.Replace("  (", " (")}";
        return (true, $"Started {name}." + (queueWas && !QueueRunning() ? " The queue paused (it does that when you start something yourself)." : ""));
    }

    public static (bool, string) Stop(Game game, string skill)
    {
        var action = game.ActionManager.ActiveActions.FirstOrDefault(a => a.Entity.Id == skill && a.IsStarted);
        if (action == null) return (false, "That is not running.");
        string name = action.Entity.DisplayName.Text;
        action.TryStop(true);
        return action.IsStarted ? (false, $"{name} did not stop.") : (true, $"Stopped {name}.");
    }

    public static (bool, string) StopAll(Game game)
    {
        int n = game.ActionManager.ActiveActions.Count(a => a.IsStarted);
        if (QueueRunning()) QueueCall("Pause", new object[] { "stopped from the phone" });
        game.ActionManager.TryStopActive(force: true);
        return (true, n == 0 ? "Nothing was running." : $"Stopped {n} action{(n == 1 ? "" : "s")}" + (s_queue != null ? "; the queue is paused." : "."));
    }

    public static (bool, string) Food(Game game, string itemId)
    {
        if (!game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) || e is not CombatEntity combat) return (false, "No combat here.");
        Item? item = game.Data.GetDataRegistryOutput<Item>().GetAllObjects().FirstOrDefault(i => i.Id == itemId);
        if (item == null || !item.Capabilities.Contains<FoodCapability>()) return (false, "That is not food.");
        if (combat.Food.Inventory.GetQuantity(item) <= 0) return (false, $"You have no {item.DisplayName.GetPluralText(2)}.");
        combat.Food.SelectFood(item);
        return combat.Food.SelectedFood?.Item == item ? (true, $"Now eating {item.DisplayName.GetPluralText(2)}.") : (false, "The food could not be changed.");
    }

    public static (bool, string) Stance(Game game, string stanceId)
    {
        if (!game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) || e is not CombatEntity combat) return (false, "No combat here.");
        var type = combat.Equipment.CurrentWeapon.Weapon.Type;
        AttackStance? stance = type.Stances.FirstOrDefault(s => s.Id == stanceId);
        if (stance == null) return (false, "Your weapon has no such stance.");
        combat.Player.SetSelectedStance(type, stance);
        return (true, $"Stance: {stance.DisplayName.Text}.");
    }

    public static (bool, string) Sell(Game game, string itemId, long quantity)
    {
        if (!game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? b) || b is not BankEntity bank) return (false, "No bank here.");
        Item? item = game.Data.GetDataRegistryOutput<Item>().GetAllObjects().FirstOrDefault(i => i.Id == itemId);
        if (item == null) return (false, "That item is not in the game.");
        var stacks = bank.Inventory.Stacks.Where(s => s.Item == item).ToList();
        long have = stacks.Sum(s => (long)s.Quantity);
        if (have <= 0) return (false, $"You have no {item.DisplayName.GetPluralText(2)}.");
        if (stacks.Any(s => s.IsLocked)) return (false, $"{item.DisplayName.GetPluralText(2)} are locked in the bank; unlock them in the game first.");
        if (quantity <= 0 || quantity > have) return (false, $"You have {have:N0}; pick an amount from 1 to {have:N0}.");
        var stats = bank.ItemSales.GetItemStats(item);
        try
        {
            long price = stats.PricePerItem.Value;
            int sold = (int)Math.Min(quantity, int.MaxValue);
            bank.ItemSales.SellItem(item, stats, sold);
            return (true, $"Sold {sold:N0} {item.DisplayName.GetPluralText(sold)} for {price * sold:N0} {item.SellsFor.Currency.DisplayName.Text}.");
        }
        finally { try { stats.Destroy(); } catch { } }
    }

    // ---- the Action Queue, by name

    static readonly Type? s_queue = FindType("ActionQueue.Queue");
    static readonly Type? s_step = FindType("ActionQueue.Step");
    static readonly Type? s_until = FindType("ActionQueue.Until");

    static bool QueueRunning() => s_queue != null && Static(s_queue, "Running") is true;

    static object? QueueCall(string name, object[] args)
    {
        MethodInfo? m = s_queue?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
        return m?.Invoke(null, args);
    }

    static List<string> Presets() => s_queue != null && Static(s_queue, "PresetNames") is IEnumerable names ? names.Cast<object>().Select(n => n.ToString() ?? "").ToList() : new List<string>();

    static int QueueCount() => s_queue != null && Static(s_queue, "Steps") is IList steps ? steps.Count : 0;

    /// <summary>Adds a step: skill + target + goal ("Stops", "Item", "Made", "Level", "Minutes", "Times") and its number.</summary>
    public static (bool, string) QueueAdd(Game game, string skill, string targetId, string extra, string until, double amount)
    {
        if (s_queue == null || s_step == null || s_until == null) return (false, "The Action Queue mod is not installed.");
        ActionComponent? action = Targets.Find(game, skill);
        Target? target = action == null ? null : Targets.All(action).FirstOrDefault(t => t.Id == targetId && t.Extra == extra);
        if (action == null || target == null) return (false, "That is no longer in the game.");
        if (!Enum.TryParse(s_until, until, out object? kind) || until is not ("Stops" or "Item" or "Made" or "Level" or "Minutes" or "Times"))
            return (false, "Pick when the step should end.");
        if (until != "Stops" && (amount <= 0 || amount > 100_000_000)) return (false, "Pick a number for the goal.");
        if (QueueCount() >= 60) return (false, "The queue is full (60 steps).");
        object step = Activator.CreateInstance(s_step)!;
        void Set(string name, object? value) => s_step.GetProperty(name)?.SetValue(step, value);
        Set("Entity", action.Entity.Id);
        Set("TargetId", target.Id);
        Set("Extra", target.Extra);
        Set("Name", $"{action.Entity.DisplayName.Text}: {target.Name}");
        Set("Until", kind);
        Set("Amount", until == "Stops" ? 0.0 : amount);
        if (until is "Item" or "Made")
        {
            Item? product = Targets.Product(action, target);
            if (product == null) return (false, "This target makes nothing to count; pick another goal.");
            Set("Subject", product.Id);
            Set("SubjectName", product.DisplayName.GetPluralText(1));
        }
        else if (until == "Level")
        {
            Set("Subject", action.Entity.DisplayName.Text);
            Set("SubjectName", action.Entity.DisplayName.Text);
        }
        QueueCall("Add", new[] { step });
        return (true, $"Added to the queue: {target.Name.Replace("  (", " (")}, {s_step.GetProperty("Goal")?.GetValue(step)}.");
    }

    public static (bool, string) QueueEdit(string what, int index, int by, string name)
    {
        if (s_queue == null) return (false, "The Action Queue mod is not installed.");
        int count = QueueCount();
        switch (what)
        {
            case "qremove":
                if (index < 0 || index >= count) return (false, "That step is gone already.");
                QueueCall("RemoveAt", new object[] { index });
                return (true, $"Step {index + 1} removed.");
            case "qmove":
                if (index < 0 || index >= count || by is not (-1 or 1)) return (false, "That step cannot move there.");
                return QueueCall("Move", new object[] { index, by }) is true ? (true, "Moved.") : (false, "That step cannot move there.");
            case "qclear":
                if (count == 0) return (false, "The queue is empty already.");
                QueueCall("Clear", Array.Empty<object>());
                return (true, $"Cleared {count} step{(count == 1 ? "" : "s")}. What was running keeps running.");
            case "qsave":
            {
                MethodInfo? save = s_queue.GetMethod("SavePreset", BindingFlags.Public | BindingFlags.Static);
                if (save == null) return (false, "This queue cannot save.");
                if (count == 0) return (false, "The queue is empty: add steps first.");
                object?[] args = { name.Trim(), null };
                bool ok = save.Invoke(null, args) is true;
                return (ok, args[1]?.ToString() ?? (ok ? $"Saved as {name}." : "The queue could not be saved."));
            }
            case "qload":
            {
                MethodInfo? load = s_queue.GetMethod("LoadPreset", BindingFlags.Public | BindingFlags.Static);
                if (load == null) return (false, "This queue cannot load saved queues.");
                object?[] args = { name, null };
                bool ok = load.Invoke(null, args) is true;
                return (ok, args[1]?.ToString() ?? (ok ? $"Loaded {name}." : $"Could not load {name}."));
            }
        }
        return (false, "Unknown command.");
    }

    // ------------------------------------------------------------------------------------------- small pieces

    static byte[] Json(object value) => JsonSerializer.SerializeToUtf8Bytes(value);

    static Type? FindType(string name)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            if (a.GetName().Name != "PhoneScreen" && a.GetType(name, false) is { } t) return t;
        return null;
    }

    static object? Static(Type t, string name) =>
        t.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? t.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

    static object? Member(object o, string name)
    {
        Type t = o.GetType();
        return t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o) ?? t.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);
    }
}
