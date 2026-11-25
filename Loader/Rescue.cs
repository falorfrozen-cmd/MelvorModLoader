using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Saves;

namespace MelvorModLoader;

/// <summary>
/// Gives items back when a mod that kept its own items in the save has been removed. Such a mod writes an entity of its
/// own into the save, which starts with a "give back" list: int version, int count, then (string item id, int quantity).
/// When a character opens and the mod that wrote it is not loaded, the game has skipped that entity and the mod's own
/// items with it; the loader reads the list from the save file and puts those (normal) items into the bank.
/// The game does not write the entity again without the mod, so the list is taken over once.
///
/// Nothing is lost when the bank is full: the list goes into mods\rescue-pending.json first (per save id), and an item
/// leaves that file only after the bank really took it. What did not fit is tried again every time that character is
/// opened and every ten seconds while playing. See <see cref="RescueStore"/> for how the same save is never given the
/// same items twice.
/// </summary>
static class Rescue
{
    /// <summary>The entities a mod writes, and the mod that writes each.</summary>
    static readonly Dictionary<string, string> s_owners = new() { ["MelvorBase:OneStyleData"] = "OneStyle" };
    /// <summary>The mods as the player knows them.</summary>
    static readonly Dictionary<string, string> s_names = new() { ["OneStyle"] = "One Style" };
    const long RetryMs = 10_000;
    const int ReadTries = 6;

    static Game? s_done;
    /// <summary>The open character's save ("save:" + 32 hex), or "" when nothing can wait for it.</summary>
    static string s_key = "";
    /// <summary>True while the game Check last saw is the one whose file entries were sorted out (see OnGameSaved).</summary>
    static bool s_live;
    static RescueStore? s_store;
    static long s_nextTry;
    static int s_told = -1;
    static int s_readFailed;
    /// <summary>The game saves on the main thread like everything here; the lock is for a save from anywhere else.</summary>
    static readonly object s_gate = new();

    static string FilePath => Path.Combine(Bootstrap.ModsDir, "rescue-pending.json");
    static RescueStore Store => s_store ??= new RescueStore(FilePath, Log.Write) { TrackSaves = HookSaved() };

    /// <summary>A static property of the game's SaveManager: by its name, or (should a game update rename it) the only one of its type.</summary>
    static object? SaveManagerValue(string name, Type type)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        if (typeof(SaveManager).GetProperty(name, flags) is { } named && type.IsAssignableFrom(named.PropertyType)) return named.GetValue(null);
        var same = typeof(SaveManager).GetProperties(flags).Where(p => p.PropertyType == type).ToList();
        return same.Count == 1 ? same[0].GetValue(null) : null;
    }

    /// <summary>The same form One Style uses for its own files.</summary>
    static string SaveKey() => SaveManagerValue("ActiveSaveId", typeof(Guid)) is Guid id && id != Guid.Empty ? "save:" + id.ToString("N") : "";

    /// <summary>Called every frame (before the game's nodes run, so before its save timer): reads the save once per
    /// opened character, then tries what waits every ten seconds. Costs two comparisons when there is nothing to do.</summary>
    public static void Check()
    {
        Game? game = Current();
        if (game is not { IsSaveLoaded: true }) { s_live = false; return; }
        if (!ReferenceEquals(s_done, game))
        {
            s_done = game;
            s_live = false;
            s_key = "";
            try { lock (s_gate) Open(game); }
            catch (Exception ex) { Log.Write("Rescue: " + ex.Message); }
            s_live = ReferenceEquals(s_done, game);
            return;
        }
        s_live = true;
        // what Early read when the character's screen came: given now, and said now
        if (s_giveAtOpen)
        {
            s_giveAtOpen = false;
            try { lock (s_gate) if (s_key != "" && s_store != null) Give(game, s_store, s_key, opening: true); }
            catch (Exception ex) { Log.Write("Rescue: " + ex.Message); }
        }
        if (s_later.Count > 0)
        {
            string text = string.Join(" ", s_later);
            s_later.Clear();
            Tell(text);
        }
        if (s_key == "" || s_store == null || System.Environment.TickCount64 < s_nextTry) return;
        s_nextTry = System.Environment.TickCount64 + RetryMs;
        try
        {
            lock (s_gate)
            {
                if (s_store.Waiting(s_key) > 0) Give(game, s_store, s_key, opening: false);
                else s_store.Flush();
            }
        }
        catch (Exception ex) { Log.Write("Rescue: " + ex.Message); }
    }

    static GamesByMalcs.Melvor2.GameMenus.GameView? s_view;
    static bool s_hooked;
    static int s_frames;

    /// <summary>The open game without searching the whole scene tree every frame: the game's view is noted when it
    /// enters the tree. Should that ever be missed, the tree is searched twice a second as before.</summary>
    static Game? Current()
    {
        if (!s_hooked)
        {
            s_hooked = true;
            try { ((Godot.SceneTree)Godot.Engine.GetMainLoop()).NodeAdded += node => { if (node is GamesByMalcs.Melvor2.GameMenus.GameView view) { s_view = view; Early(view); } }; }
            catch (Exception e) { Log.Write("Rescue: " + e.Message); }
        }
        if (s_view != null && Godot.GodotObject.IsInstanceValid(s_view) && s_view.IsInsideTree()) return GameAccess.GameOf(s_view);
        s_view = null;
        if (++s_frames % 30 != 0) return null;
        s_view = GameAccess.FindGameView();
        return s_view == null ? null : GameAccess.GameOf(s_view);
    }

    static bool s_giveAtOpen;
    /// <summary>Things the player must be told, found at a moment when nothing can be put on the screen.</summary>
    static readonly List<string> s_later = new();

    /// <summary>
    /// The character's screen enters the tree: the save is read here already and what it owes is written to the waiting
    /// file. The game binds its driver right after, and the driver's save timer runs free: a save in that same frame
    /// would be written without the removed mod's list, before the first frame handler ever saw the character. The items
    /// themselves are given at the first frame (nothing is put in the bank or on the screen from inside this signal).
    /// </summary>
    static void Early(GamesByMalcs.Melvor2.GameMenus.GameView view)
    {
        try
        {
            if (GameAccess.GameOf(view) is not { IsSaveLoaded: true } game || ReferenceEquals(s_done, game)) return;
            s_done = game;
            s_live = false;
            s_key = "";
            lock (s_gate) Open(game, give: false);
        }
        catch (Exception e) { s_done = null; Log.Write("Rescue (early): " + e.Message); }
    }

    static HashSet<string> s_broken = new();
    static readonly HashSet<string> s_taken = new();

    /// <summary>The mod's DLL is in the mods folder (in its own folder, or loose): the player did not take it out.</summary>
    static bool Installed(string mod)
    {
        try
        {
            return Loader.FailedMods.Contains(mod, StringComparer.OrdinalIgnoreCase)
                || File.Exists(Path.Combine(Bootstrap.ModsDir, mod, mod + ".dll")) || File.Exists(Path.Combine(Bootstrap.ModsDir, mod + ".dll"));
        }
        catch (Exception) { return true; }   // when in doubt it is not gone
    }

    /// <summary>
    /// The ledger "mod-rescue-taken.json" in the game's data folder: which save's items of which mod became normal
    /// items, and when. Written after the game has saved (only then is it true). A mod reads it when it comes back,
    /// so that it does not bring the same items back a second time from a copy of its own.
    /// </summary>
    static void WriteLedger(string key)
    {
        if (s_taken.Count == 0) return;
        try
        {
            string file = Path.Combine(Godot.OS.GetUserDataDir(), "mod-rescue-taken.json");
            Dictionary<string, Dictionary<string, DateTime>> all = new();
            if (File.Exists(file))
                try { all = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, DateTime>>>(File.ReadAllText(file)) ?? new(); }
                catch (Exception) { }
            if (!all.TryGetValue(key, out var bySave)) all[key] = bySave = new();
            foreach (string owner in s_taken) bySave[owner] = DateTime.UtcNow;
            string temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, file, true);
            s_taken.Clear();
        }
        catch (Exception e) { Log.Write("Rescue: the ledger could not be written: " + e.Message); }
    }

    static void Open(Game game, bool give = true)
    {
        string key = SaveKey();
        if (key == "") return;
        RescueStore store = Store;
        // "Missing" is a mod that the player took out. A mod whose DLL is still in the mods folder and that did not
        // start is broken (nearly always by a game update), and its items are never turned into normal ones: the mod
        // keeps a copy of its own and brings them back when it works again. Turning them into normal items would be
        // the one thing that cannot be undone.
        var broken = s_owners.Where(o => !Loader.LoadedMods.Contains(o.Value) && Installed(o.Value)).Select(o => o.Key).ToHashSet();
        var missing = s_owners.Where(o => !Loader.LoadedMods.Contains(o.Value) && !broken.Contains(o.Key)).Select(o => o.Key).ToHashSet();
        s_broken = broken;
        s_taken.Clear();
        var lists = new List<RescueList>();
        if (missing.Count > 0 || broken.Count > 0 || store.Has(key))
        {
            try { lists = Read(game, missing); s_readFailed = 0; }
            catch (Exception ex)
            {
                // the file may be busy for a moment: the same character is looked at again at the next call
                if (++s_readFailed < ReadTries) { s_done = null; return; }
                s_readFailed = 0;
                Log.Write("Rescue: the save could not be read (" + ex.Message + "); what already waits is still given.");
            }
        }
        // from here on what waits for this save is tried every ten seconds, whatever happens below
        s_key = key;
        s_told = -1;
        s_nextTry = System.Environment.TickCount64 + RetryMs;
        // what the game's next save makes final: these mods' items become normal items (see OnGameSaved)
        foreach (RescueList list in lists.Where(l => l.OwnerMissing && l.Items.Count > 0)) s_taken.Add(list.Owner);
        store.Opened(key, game.LoadedSaveTimestamp.ToUniversalTime(), lists);
        if (give) Give(game, store, key, opening: true);
        else
        {
            store.Flush();
            s_giveAtOpen = true;
        }
    }

    /// <summary>Puts what waits for this save into the bank and says what still waits. Returns (given now, still waiting).</summary>
    static (int Given, int Waiting) Give(Game game, RescueStore store, string key, bool opening)
    {
        if (store.Waiting(key) == 0) { store.Flush(); return (0, 0); }
        if (!game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) || e is not BankEntity bank) return (0, store.Waiting(key));
        int given = store.Deliver(key,
            (id, n) => game.Data.TryGetObject(id, out Item? item) ? bank.Inventory.AddQuantity(item, n, AddItemFlags.None) : -1,
            (owner, id, added, asked) => Log.Write($"Rescue: {owner} {(Loader.LoadedMods.Contains(owner) ? "was removed for a time" : "is not installed")}; gave back {added} x {id}{(added < asked ? $" (the bank had no room for {asked - added})" : "")}."));
        if (given > 0) Log.Write($"Rescue: {given} item(s) of removed mods are back in the bank as normal items.");

        var left = store.WaitingItems(key);
        int waiting = left.Sum(l => l.Quantity);
        if (waiting == 0)
        {
            if (!opening && given > 0) Tell("Everything of the removed mods is in your bank now.");
            s_told = 0;
            return (given, 0);
        }
        if (!opening && waiting == s_told) return (given, waiting);
        s_told = waiting;
        var lines = new List<string>();
        foreach (var byOwner in left.GroupBy(l => l.Owner))
        {
            string name = s_names.GetValueOrDefault(byOwner.Key, byOwner.Key);
            int room = byOwner.Where(l => game.Data.TryGetObject(l.Item, out Item? _)).Sum(l => l.Quantity);
            int unknown = byOwner.Sum(l => l.Quantity) - room;
            if (room > 0) lines.Add($"{room} item(s) of the removed mod {name} wait for room in your bank. Free some bank slots and they go in by themselves");
            if (unknown > 0) lines.Add($"{unknown} item(s) of the removed mod {name} are not known to the game now (another mod is missing); they are kept");
        }
        Tell(string.Join(". ", lines) + ".", " They are kept in mods\\rescue-pending.json and go into the bank as soon as they fit.");
        return (given, waiting);
    }

    /// <summary>A note under the game's top bar, for the loader's own messages (a mod that did not start).</summary>
    internal static void Note(string text, double seconds)
    {
        Log.Write(text);
        try { ((Godot.SceneTree)Godot.Engine.GetMainLoop()).CreateTimer(1.0).Timeout += () => Tell(text, "", seconds, log: false); }
        catch (Exception) { }
    }

    /// <summary>The log (with <paramref name="more"/>), and a note under the game's top bar for thirty seconds: the
    /// loader has no window of its own, and this is something the player must not miss.</summary>
    static void Tell(string text, string more = "", double seconds = 30, bool log = true)
    {
        if (log) Log.Write("Rescue: " + text + more);
        try
        {
            var tree = (Godot.SceneTree)Godot.Engine.GetMainLoop();
            if (tree.Root.FindChild("MelvorModLoaderRescueNote", false, false) is Godot.Node old) { tree.Root.RemoveChild(old); old.QueueFree(); }
            var layer = new Godot.CanvasLayer { Layer = 128, Name = "MelvorModLoaderRescueNote" };
            // a plate of its own: plain text over the game's top bar could not be read
            var plate = new Godot.PanelContainer { MouseFilter = Godot.Control.MouseFilterEnum.Ignore };
            plate.SetAnchorsAndOffsetsPreset(Godot.Control.LayoutPreset.CenterTop);
            plate.GrowHorizontal = Godot.Control.GrowDirection.Both;
            plate.OffsetTop = 70;
            var box = new Godot.StyleBoxFlat { BgColor = new Godot.Color(0.10f, 0.07f, 0.02f, 0.95f), BorderColor = new Godot.Color(1f, 0.72f, 0.3f) };
            box.SetBorderWidthAll(2);
            box.SetCornerRadiusAll(6);
            box.SetContentMarginAll(14);
            plate.AddThemeStyleboxOverride("panel", box);
            var label = new Godot.Label { Text = "Mod loader: " + text, AutowrapMode = Godot.TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Godot.Vector2(720, 0), HorizontalAlignment = Godot.HorizontalAlignment.Center,
                MouseFilter = Godot.Control.MouseFilterEnum.Ignore };
            label.AddThemeFontSizeOverride("font_size", 17);
            label.AddThemeColorOverride("font_color", new Godot.Color(1f, 0.92f, 0.78f));
            plate.AddChild(label);
            layer.AddChild(plate);
            tree.Root.AddChild(layer);
            tree.CreateTimer(seconds).Timeout += () => { if (Godot.GodotObject.IsInstanceValid(layer)) layer.QueueFree(); };
        }
        catch { }
    }

    // ---- the game saved: what was put in the bank is in the save now

    static bool HookSaved()
    {
        try { Subscribe(); return true; }
        catch (Exception e)
        {
            Log.Write("Rescue: the game's save event is not there (" + e.Message + "); given items leave the waiting list at once.");
            return false;
        }
    }

    // on its own, so that a game without this event fails here and not in the caller
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Subscribe() => SaveManager.LocalSaved += (_, _) => OnGameSaved();

    static void OnGameSaved()
    {
        try
        {
            // only for the game the entries belong to: a character that has just been opened and was saved before
            // Check looked at it still has the entries of the last game, which that save does not hold
            if (!s_live || s_key == "" || s_store == null || SaveKey() != s_key) return;
            lock (s_gate) { s_store.Saved(s_key); WriteLedger(s_key); }
        }
        catch (Exception e) { Log.Write("Rescue: " + e.Message); }
    }

    // ---- reading the save

    static List<RescueList> Read(Game game, HashSet<string> missing)
    {
        var lists = new List<RescueList>();
        if (SaveManagerValue("ActiveSaveId", typeof(Guid)) is not Guid id || id == Guid.Empty) return lists;
        if (SaveManagerValue("ActiveSaveSource", typeof(ISaveSource)) is not ISaveSource source) return lists;
        // always the active save: after loading a backup the game has already written it over the active save and
        // renumbered its backups, so the backup number would now point at another file
        using Stream file = source.GetReadStreamForId(id);
        using var unpacked = new BrotliStream(file, CompressionMode.Decompress);
        using var reader = new SaveReader(unpacked, game.Data, game.Entities);
        long stamp = reader.Read<SaveHeader>().Timestamp.ToBinary();
        int entities = reader.ReadInt32();
        for (int i = 0; i < entities; i++)
        {
            string entityId = reader.ReadString();
            int length = reader.ReadInt32();
            if (!s_owners.TryGetValue(entityId, out string? owner)) { reader.ReadBytes(length); continue; }
            byte[] bytes = reader.ReadBytes(length);
            if (s_broken.Contains(entityId))
            {
                // installed and not running: nothing is given back; the save as it is now is kept, and the player is told
                string copy = KeepSave(source, id);
                string name = s_names.GetValueOrDefault(owner, owner);
                Log.Write($"Rescue: {owner} is installed and did not start; its items in this save are left alone (not turned into normal items)." + (copy != "" ? " Copy of the save: " + copy : ""));
                s_later.Add($"{name} is installed but did not start, so its items are missing in this session. They were NOT turned into normal items: "
                    + $"{name} keeps a copy of them and brings them back when it works again. Look for a newer version of the mod."
                    + (copy != "" ? " A copy of your save as it is now was kept in mods\\rescue-kept." : ""));
                continue;
            }
            try
            {
                using var region = new BinaryReader(new MemoryStream(bytes));
                region.ReadInt32();   // version: whatever a mod writes after its list, the list itself always has this form
                int count = region.ReadInt32();
                if (count < 0 || count > 100_000) throw new InvalidDataException($"it says {count} entries");
                var items = new List<(string Item, int Quantity)>();
                for (int j = 0; j < count; j++)
                {
                    string item = region.ReadString();
                    int quantity = region.ReadInt32();
                    if (quantity < 0 || item.Length > 200) throw new InvalidDataException("an entry of it is not an item");
                    items.Add((item, quantity));
                }
                lists.Add(new RescueList(owner, missing.Contains(entityId), Hash(items), stamp, items));
            }
            catch (Exception e)
            {
                // one mod's list that cannot be read never stops the others; and the player hears of it
                Log.Write($"Rescue: the give-back list of {owner} in this save could not be read ({e.Message}).");
                if (!missing.Contains(entityId)) continue;
                string kept = KeepSave(source, id);
                s_later.Add($"The removed mod {s_names.GetValueOrDefault(owner, owner)} kept items in this save, and their list could not be read. "
                    + "Put that mod back before you play on: the game's next save will not hold them."
                    + (kept != "" ? " A copy of the save as it is now was kept in " + kept + "." : ""));
            }
        }
        return lists;
    }

    /// <summary>A copy of the save as it is on disk now (mods\rescue-kept), for a list that could not be read.</summary>
    static string KeepSave(ISaveSource source, Guid id)
    {
        try
        {
            byte[] bytes = source.GetSaveDataForId(id);
            if (bytes.Length == 0) return "";
            string folder = Path.Combine(Bootstrap.ModsDir, "rescue-kept");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, $"save_{id:N}_{DateTime.Now:yyyyMMdd-HHmmss}.sav");
            File.WriteAllBytes(file, bytes);
            // the eight newest of this save stay
            foreach (string old in Directory.GetFiles(folder, $"save_{id:N}_*.sav").OrderByDescending(f => f, StringComparer.Ordinal).Skip(8)) File.Delete(old);
            return file;
        }
        catch (Exception e) { Log.Write("Rescue: the copy of the save could not be kept: " + e.Message); return ""; }
    }

    static string Hash(List<(string Item, int Quantity)> items) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", items.Select(i => i.Item + "\t" + i.Quantity)))), 0, 12);

    // ---- for Dev\TestHarness ("-Only lifea"): the same steps on a file and a list of the test's own. A new store at
    // every call, so the file alone carries what is known, as after a restart of the game. Each returns { given, waiting }.

    /// <summary>A character is opened whose save was written at <paramref name="savedAt"/> and holds this list
    /// (no items: no list), with the mod that wrote it missing or back again.</summary>
    internal static int[] TestOpen(Game game, string file, string saveKey, DateTime savedAt, string[] items, int[] quantities, bool ownerMissing)
    {
        var store = new RescueStore(file, Log.Write);
        var list = items.Select((id, i) => (Item: id, Quantity: quantities[i])).ToList();
        var lists = new List<RescueList>();
        if (list.Count > 0) lists.Add(new RescueList("OneStyle", ownerMissing, Hash(list), savedAt.ToBinary(), list));
        store.Opened(saveKey, savedAt.ToUniversalTime(), lists);
        var (given, waiting) = Give(game, store, saveKey, opening: true);
        return new[] { given, waiting };
    }

    /// <summary>The ten-second retry while playing.</summary>
    internal static int[] TestRetry(Game game, string file, string saveKey)
    {
        var (given, waiting) = Give(game, new RescueStore(file, Log.Write), saveKey, opening: false);
        return new[] { given, waiting };
    }

    /// <summary>The game saved this character.</summary>
    internal static int[] TestSaved(string file, string saveKey)
    {
        var store = new RescueStore(file, Log.Write);
        store.Saved(saveKey);
        return new[] { 0, store.Waiting(saveKey) };
    }
}

/// <summary>A give-back list as found in a save: the mod that wrote it, whether that mod is missing now, and what tells
/// this very save file from every other (the list's hash and the save's own time stamp).</summary>
internal sealed record RescueList(string Owner, bool OwnerMissing, string Hash, long Stamp, List<(string Item, int Quantity)> Items);

/// <summary>What one give-back list still owes a save (one entry of rescue-pending.json).</summary>
internal sealed class RescueBatch
{
    public string Owner { get; set; } = "";
    public string Hash { get; set; } = "";
    public long Stamp { get; set; }
    /// <summary>When the save this list was taken from was written (UTC), as the game told it at that opening.</summary>
    public DateTime From { get; set; }
    /// <summary>Item id and how many are still to go into the bank.</summary>
    public Dictionary<string, int> Waiting { get; set; } = new();
    /// <summary>Put in the bank, but the game has not saved since.</summary>
    public List<RescueGiven> Unsaved { get; set; } = new();
}

internal sealed class RescueGiven
{
    public string Item { get; set; } = "";
    public int Quantity { get; set; }
    /// <summary>When it went into the bank (UTC).</summary>
    public DateTime At { get; set; }
}

/// <summary>
/// rescue-pending.json: per save id, the items of removed mods that are not safely in that save yet. Knows nothing of the
/// game, so that it can be checked on its own. The rules that keep items from being lost or given twice:
/// - A list read from a save is written here before anything is given.
/// - An item that went into the bank moves from "Waiting" to "Unsaved" and is forgotten only when the game has saved.
///   A game that is closed (or crashes) before it saves loses what was put in its bank; at the next opening those
///   items wait again. If the save that opens was written after they were given, it holds them and they are dropped.
/// - A list is known by its hash and the time stamp of the save it was in. The same save file again (closed without
///   saving, or the same backup loaded) means nothing given from it is in that save: its list waits again in full, it
///   is never added a second time. Any game save without the mod drops the list from the save, so it is read once.
/// - When the mod is installed again and the save is the very one its list was taken from, the mod restores its own
///   items and the list is dropped here.
/// - A save older than the one a list was taken from (a backup from before) does not get what that list still owes.
/// </summary>
internal sealed class RescueStore
{
    static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    readonly string _path;
    readonly Action<string> _log;
    Dictionary<string, List<RescueBatch>> _saves = new();
    bool _dirty, _failed;

    /// <summary>False when the game does not tell that it saved: a given item is then forgotten at once.</summary>
    public bool TrackSaves { get; set; } = true;

    public RescueStore(string path, Action<string> log)
    {
        _path = path;
        _log = log;
        try
        {
            if (!File.Exists(_path)) return;
            _saves = JsonSerializer.Deserialize<Dictionary<string, List<RescueBatch>>>(File.ReadAllText(_path)) ?? new();
            foreach (string key in _saves.Keys.ToList())
            {
                List<RescueBatch> batches = (_saves[key] ?? new()).Where(b => b != null).ToList();
                foreach (RescueBatch b in batches)
                {
                    b.Waiting ??= new();
                    b.Unsaved = (b.Unsaved ?? new()).Where(g => g != null).ToList();
                }
                _saves[key] = batches;
            }
        }
        catch (Exception e)
        {
            // never written over: it may name items that still wait
            string kept = _path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Move(_path, kept); } catch { kept = "it could not be moved"; }
            _log($"Rescue: rescue-pending.json could not be read ({e.Message}); kept as {kept}.");
            _saves = new();
        }
    }

    public bool Has(string key) => _saves.ContainsKey(key);

    /// <summary>How many items still wait for this save.</summary>
    public int Waiting(string key) => _saves.TryGetValue(key, out List<RescueBatch>? batches) ? batches.Sum(b => b.Waiting.Values.Sum()) : 0;

    public List<(string Owner, string Item, int Quantity)> WaitingItems(string key) =>
        _saves.TryGetValue(key, out List<RescueBatch>? batches)
            ? batches.SelectMany(b => b.Waiting.Select(w => (b.Owner, w.Key, w.Value))).ToList() : new();

    /// <summary>A character was opened: its save was written at <paramref name="savedUtc"/> and holds these lists.</summary>
    public void Opened(string key, DateTime savedUtc, IEnumerable<RescueList> lists)
    {
        _saves.TryGetValue(key, out List<RescueBatch>? batches);
        // a save that is older than the one a list was taken from: a backup from before that was loaded. In its world
        // that list was never taken, so what it still owes is not owed here (a list the backup holds itself is read
        // below like any other); giving it would make items out of nothing
        foreach (RescueBatch b in (batches ?? new()).Where(b => b.From != default && savedUtc < b.From.AddSeconds(-1)).ToList())
        {
            batches!.Remove(b);
            _dirty = true;
            _log($"Rescue: this save ({savedUtc:u}) is older than the one {b.Owner}'s list was taken from ({b.From:u}): a backup was loaded. The {b.Waiting.Values.Sum()} item(s) that waited belong to the newer save and are not given here.");
        }
        foreach (RescueBatch b in batches ?? new())
        {
            if (b.Unsaved.Count == 0) continue;
            // given to a game that did not save afterwards: this save does not hold them, they wait again
            foreach (RescueGiven g in b.Unsaved.Where(g => g.At >= savedUtc)) b.Waiting[g.Item] = b.Waiting.GetValueOrDefault(g.Item) + g.Quantity;
            b.Unsaved.Clear();
            _dirty = true;
        }
        foreach (RescueList list in lists)
        {
            RescueBatch? same = batches?.FirstOrDefault(b => b.Owner == list.Owner && b.Hash == list.Hash && b.Stamp == list.Stamp);
            if (!list.OwnerMissing)
            {
                if (same == null) continue;
                batches!.Remove(same);
                _dirty = true;
                _log($"Rescue: {list.Owner} is installed again and this save still has its items: the {same.Waiting.Values.Sum()} normal item(s) that waited are not needed any more.");
                continue;
            }
            if (list.Items.Count == 0) continue;
            if (same == null)
            {
                if (batches == null) _saves[key] = batches = new();
                batches.Add(same = new RescueBatch { Owner = list.Owner, Hash = list.Hash, Stamp = list.Stamp, From = savedUtc });
            }
            // also for a list that is known: the same save file again holds nothing of what was given from it
            same.Waiting = list.Items.Where(i => i.Quantity > 0).GroupBy(i => i.Item).ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
            _dirty = true;
        }
        Tidy(key);
        Flush();
    }

    /// <summary>
    /// Tries everything that waits for this save. <paramref name="add"/> gets an item id and a quantity and returns how
    /// many went into the bank (0: no room), or -1 when the game does not know the item. Returns how many went in.
    /// </summary>
    public int Deliver(string key, Func<string, int, int> add, Action<string, string, int, int>? given = null)
    {
        if (!_saves.TryGetValue(key, out List<RescueBatch>? batches)) return 0;
        int total = 0;
        try
        {
            foreach (RescueBatch b in batches.ToList())
                foreach (var (id, n) in b.Waiting.ToList())
                {
                    int added = Math.Min(add(id, n), n);
                    if (added <= 0) continue;
                    if (added == n) b.Waiting.Remove(id); else b.Waiting[id] = n - added;
                    if (TrackSaves) b.Unsaved.Add(new RescueGiven { Item = id, Quantity = added, At = DateTime.UtcNow });
                    total += added;
                    _dirty = true;
                    given?.Invoke(b.Owner, id, added, n);
                }
        }
        finally
        {
            // also when the bank threw half way: what did go in must not be given again
            Tidy(key);
            Flush();
        }
        return total;
    }

    /// <summary>The game saved this character: what went into its bank is in the save.</summary>
    public void Saved(string key)
    {
        if (!_saves.TryGetValue(key, out List<RescueBatch>? batches)) return;
        foreach (RescueBatch b in batches.Where(b => b.Unsaved.Count > 0)) { b.Unsaved.Clear(); _dirty = true; }
        Tidy(key);
        Flush();
    }

    void Tidy(string key)
    {
        if (!_saves.TryGetValue(key, out List<RescueBatch>? batches)) return;
        if (batches.RemoveAll(b => b.Waiting.Count == 0 && b.Unsaved.Count == 0) > 0) _dirty = true;
        if (batches.Count == 0) { _saves.Remove(key); _dirty = true; }
    }

    /// <summary>Writes the file when something changed (a temporary file first, so a crash never leaves half a file);
    /// a write that failed is tried again at the next call.</summary>
    public void Flush()
    {
        if (!_dirty) return;
        try
        {
            if (_saves.Count == 0) { if (File.Exists(_path)) File.Delete(_path); }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_saves, s_json));
                File.Move(temp, _path, true);
            }
            _dirty = false;
            _failed = false;
        }
        catch (Exception e)
        {
            if (!_failed) _log("Rescue: rescue-pending.json could not be written, it is tried again: " + e.Message);
            _failed = true;
        }
    }
}
