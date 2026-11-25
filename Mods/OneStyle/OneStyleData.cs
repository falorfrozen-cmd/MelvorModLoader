using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Core;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Localization;
using GamesByMalcs.Melvor2.Saves;

namespace OneStyle;

/// <summary>What One Style keeps inside the game's own save (its own part, written with every save).</summary>
public sealed class SavedData
{
    /// <summary>The newest layout this build understands. A save with a higher number is kept as it is, never rewritten.</summary>
    public const int Current = 1;
    public int Version { get; set; } = Current;
    public List<RolledItem> Items { get; set; } = new();
    /// <summary>Normal items (base item id, how many) that wait for room in the bank: from "Make all rare items normal".</summary>
    public Dictionary<string, int> NormalPending { get; set; } = new();
    /// <summary>Unique items already found (their first drop is certain) and the pity of each.</summary>
    public Dictionary<string, int> UniqueMisses { get; set; } = new();
    public List<string> UniquesFound { get; set; } = new();
    /// <summary>Drops that did not fit in the bank yet.</summary>
    public List<string> Pending { get; set; } = new();
    /// <summary>Counters of the unique powers that last (Sunken Reckoning's elite kills, Scrap Hunter's stacks...).</summary>
    public Dictionary<string, double> Counters { get; set; } = new();
    /// <summary>How many the character owns of an item it has more than one of (two copies of one Unique share one record):
    /// written at every save, so that the right number comes back when the places of the items cannot be read.</summary>
    public Dictionary<string, int> Counts { get; set; } = new();
    /// <summary>
    /// Where every One Style item was when the save was written (and every ring in the ring slots One Style adds): the
    /// game drops what it does not know when it reads a save, and One Style puts it back from this list. Its own list,
    /// so that no change of the game's bank or equipment layout can ever shift it (as game 0.11.7 did on 2026-11-25).
    /// </summary>
    public List<Place> Places { get; set; } = new();
    /// <summary>True in a save written by a build that keeps <see cref="Places"/> (an empty list is then the truth).</summary>
    public bool HasPlaces { get; set; }
    /// <summary>
    /// Records of items the character no longer had at a save, the newest last (at most 200). A record is never thrown
    /// away at once: should the game still hold such an item at the next load, its record is taken back from here.
    /// </summary>
    public List<RolledItem> Gone { get; set; } = new();
}

/// <summary>Where one item was: in the bank (tab, index), worn (slot) or in a loadout (loadout, slot).</summary>
public sealed class Place
{
    public const string Bank = "bank", Worn = "worn", InLoadout = "loadout";
    /// <summary>The item's full id ("MelvorBase:OsR..." or, in an added ring slot, a game ring).</summary>
    public string Item { get; set; } = "";
    public int Q { get; set; } = 1;
    public string Kind { get; set; } = "";
    public int Tab { get; set; }
    public int Index { get; set; }
    public bool Locked { get; set; }
    public string Slot { get; set; } = "";
    public string Loadout { get; set; } = "";
}

/// <summary>One Style's part of a save as it is kept outside the save (see <see cref="Vault"/>).</summary>
public sealed class KeptItems
{
    public string SaveId { get; set; } = "";
    public string Character { get; set; } = "";
    /// <summary>When it was written (UTC): the moment of the save it belongs to.</summary>
    public DateTime WrittenUtc { get; set; }
    public List<KeptBack> Back { get; set; } = new();
    public SavedData Data { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HoldsSomething => Data.Items.Count > 0 || Data.NormalPending.Count > 0 || Data.Places.Count > 0;
}

public sealed class KeptBack
{
    public string Item { get; set; } = "";
    public int Q { get; set; }
}

/// <summary>
/// The second copy of every character's rare items, outside the game's save and outside the mod's folder:
/// "onestyle-items" in the game's own data folder (next to its saves), one file per save id.
///
/// Why: the rare items live in the game's save, and the game writes that save. A game that runs without One Style (an
/// update broke the mod or the mod loader, the mod could not read the save) writes it without them, ten seconds after
/// the character opened. This file is not the game's to write. One Style writes it at every save, and reads it only
/// when the save has no part of One Style's that it can find: then the items come back from here.
/// Kept as well: the file as it was each of the last ten times the character was opened ("...open-..."), and the last
/// file that held items when a new one holds none ("...last-with-items").
/// </summary>
public static class Vault
{
    /// <summary>Set when the mod starts; "" switches the vault off (tests).</summary>
    public static string Folder = "";
    public static Action<string> Log = _ => { };
    static string s_lastId = "", s_lastText = "";
    static readonly JsonSerializerOptions s_options = new() { WriteIndented = false };

    public static string FileOf(Guid id) => Path.Combine(Folder, $"{id:N}.json");

    public static Guid ActiveId() =>
        SaveScan.SaveManagerProperty("ActiveSaveId", typeof(Guid))?.GetValue(null) is Guid id ? id : Guid.Empty;

    static (Guid Id, string Character, List<KeptBack> Back, string Json, string Body)? s_staged;
    static bool s_hooked, s_noEvent;

    /// <summary>
    /// While the game writes its save: what One Style's part holds at this moment is put aside (as text, the game goes
    /// on changing the data). It becomes the file when the game says the save is on disk (<see cref="Commit"/>): a
    /// save that fails half way never leaves a file that is ahead of the save. Never throws.
    /// </summary>
    public static void Stage(Guid id, string character, List<(string ItemId, int Quantity)> back, string dataJson)
    {
        try
        {
            if (Folder == "" || id == Guid.Empty) return;
            var list = back.Select(x => new KeptBack { Item = x.ItemId, Q = x.Quantity }).ToList();
            s_staged = (id, character, list, dataJson, JsonSerializer.Serialize(list, s_options) + dataJson);
            if (!s_hooked)
            {
                s_hooked = true;
                try { Subscribe(); }
                catch (Exception e) { s_noEvent = true; Log("One Style: the game's save event is not there (" + e.Message + "); its own copy is written with the save."); }
            }
            if (s_noEvent) Commit();
        }
        catch (Exception e) { Log("One Style: its own copy of the rare items could not be prepared: " + e.Message); }
    }

    // on its own, so that a game without this event fails here and not in the caller
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Subscribe() => SaveManager.LocalSaved += (_, _) => Commit();

    /// <summary>The game has written a save: what was put aside for it becomes the file. Never throws.</summary>
    static void Commit()
    {
        try
        {
            if (s_staged is not { } staged) return;
            s_staged = null;
            Guid active = ActiveId();
            if (active != Guid.Empty && active != staged.Id) return;   // another character's save
            string file = FileOf(staged.Id);
            // nothing changed since the last save of this character: the file on disk says the same
            if (staged.Id.ToString("N") == s_lastId && staged.Body == s_lastText && File.Exists(file)) return;
            var kept = new KeptItems
            {
                SaveId = staged.Id.ToString("N"), Character = staged.Character, WrittenUtc = DateTime.UtcNow, Back = staged.Back,
                Data = JsonSerializer.Deserialize<SavedData>(staged.Json) ?? new SavedData(),
            };
            Directory.CreateDirectory(Folder);
            if (!kept.HoldsSomething && File.Exists(file) && Read(staged.Id) is { HoldsSomething: true })
                File.Copy(file, Path.Combine(Folder, $"{staged.Id:N}.last-with-items.json"), true);
            string temp = file + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(kept, s_options));
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);   // on the disk before it takes the old file's place
            }
            File.Move(temp, file, true);
            s_lastId = staged.Id.ToString("N");
            s_lastText = staged.Body;
        }
        catch (Exception e) { Log("One Style: its own copy of the rare items could not be written (the one from before stays): " + e.Message); }
    }

    /// <summary>The file of this save, or null: none, unreadable, or written by a newer One Style.</summary>
    public static KeptItems? Read(Guid id)
    {
        try
        {
            if (Folder == "" || id == Guid.Empty) return null;
            string file = FileOf(id);
            if (!File.Exists(file)) return null;
            KeptItems? kept = JsonSerializer.Deserialize<KeptItems>(File.ReadAllText(file));
            if (kept == null || kept.Data.Version > SavedData.Current) return null;
            return kept;
        }
        catch (Exception e) { Log("One Style: its own copy of the rare items could not be read: " + e.Message); return null; }
    }

    /// <summary>A character is opened: the file as it is now is kept under the time of day (the ten newest stay).</summary>
    public static void Snapshot(Guid id)
    {
        try
        {
            if (Folder == "" || id == Guid.Empty) return;
            string file = FileOf(id);
            if (!File.Exists(file)) return;
            File.Copy(file, Path.Combine(Folder, $"{id:N}.open-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true);
            foreach (string old in Directory.GetFiles(Folder, $"{id:N}.open-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(10))
                File.Delete(old);
        }
        catch (Exception e) { Log("One Style: a copy of the rare items file could not be kept: " + e.Message); }
    }

    /// <summary>
    /// When the mod loader gave this save's One Style items back as normal items (One Style had been removed): from its
    /// ledger "mod-rescue-taken.json" in the same data folder. A file older than that must not bring the rare items
    /// back, or the player would have them twice.
    /// </summary>
    public static DateTime? TakenByLoader(Guid id)
    {
        try
        {
            if (Folder == "") return null;
            string file = Path.Combine(Path.GetDirectoryName(Folder)!, "mod-rescue-taken.json");
            if (!File.Exists(file)) return null;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.TryGetProperty("save:" + id.ToString("N"), out JsonElement save) && save.TryGetProperty("OneStyle", out JsonElement at))
                return at.GetDateTime().ToUniversalTime();
        }
        catch (Exception e) { Log("One Style: the mod loader's ledger could not be read: " + e.Message); }
        return null;
    }
}

/// <summary>
/// One Style's own entity in the game's entity list: the game writes it into every save with everything else, so the
/// rare items' definitions always match the save they are in (a copied or cloud save takes them along). The game
/// does not know it when it reads a save (One Style comes in after that): <see cref="SaveScan"/> reads it back.
///
/// Its region starts with the "give back" list the mod loader reads when One Style is gone:
/// int version, int count, then (string base item id, int quantity) per entry, then the JSON of <see cref="SavedData"/>.
/// </summary>
public sealed class OneStyleData : GameEntity
{
    public const string LocalName = "OneStyleData";
    public const string FullId = "MelvorBase:" + LocalName;
    public SavedData Data { get; set; } = new();
    /// <summary>
    /// One Style's part of the save exactly as it was read, when this build could not understand it (a newer layout, a
    /// damaged part): it is written back byte for byte, so nothing in it is lost until a build that can read it comes.
    /// </summary>
    public byte[]? Raw { get; set; }
    static List<(string ItemId, int Quantity)> s_lastBack = new();
    /// <summary>Another character is opened: the last list belongs to the one before and is never written for this one.</summary>
    internal static void ForgetLast() => s_lastBack = new();
    /// <summary>For tests only: the layout number written (2 makes a save this build cannot read, as a newer One Style would).</summary>
    public static int TestLayout = 1;

    public OneStyleData(ICoreSystems core, GamesByMalcs.Melvor2.Data.DataNamespace ns) : base(core, ns, LocalName) { }

    public override LocalizedString DisplayName { get; } = new("One Style");
    public override string IconPath => "res://Assets/Icons/modifiers.png";
    protected override void OnStartLoadingResources() { }
    protected override void OnFinishLoadingResources() { }

    /// <summary>Asked for the give-back list at every save: what the player owns of One Style's items, as normal items.</summary>
    public static Func<List<(string ItemId, int Quantity)>>? GiveBack;

    public override void WriteTo(SaveWriter writer)
    {
        if (Raw != null) { writer.Write(Raw); return; }
        List<(string ItemId, int Quantity)> back = s_lastBack;
        bool fresh = false;
        // a failed list never empties what the save had: the last good one is written again
        try { if (GiveBack != null) { back = GiveBack(); s_lastBack = back; fresh = true; } }
        catch { back = s_lastBack; }
        writer.Write(TestLayout);
        writer.Write(back.Count);
        foreach (var (id, n) in back)
        {
            writer.Write(id);
            writer.Write(n);
        }
        string json = JsonSerializer.Serialize(Data);
        writer.Write(json);
        // the second copy, outside the save (never when the list came from the fallback above: that is not this moment's truth)
        if (fresh) Vault.Stage(SaveId, CharacterName(), back, json);
    }

    /// <summary>For the file's own label only.</summary>
    public static Func<string> CharacterName = () => "";
    /// <summary>The save this entity belongs to, taken when its character was opened (never asked again at a save:
    /// on the way back to the menu the game may already name another).</summary>
    public Guid SaveId { get; set; }

    public override void ReadFrom(SaveReader reader) => Data = ReadPayload(reader).Data;

    public static (List<(string, int)> Back, SavedData Data) ReadPayload(SaveReader reader)
    {
        int version = reader.ReadInt32();
        if (version != 1) throw new NotSupportedException($"One Style's part of this save has layout {version}; this build reads layout 1.");
        int count = reader.ReadInt32();
        if (count < 0 || count > 100_000) throw new InvalidDataException("One Style's part of this save is damaged (its give-back list).");
        var back = new List<(string, int)>();
        for (int i = 0; i < count; i++) back.Add((reader.ReadString(), reader.ReadInt32()));
        SavedData data = JsonSerializer.Deserialize<SavedData>(reader.ReadString()) ?? throw new InvalidDataException("One Style's part of this save is empty.");
        if (data.Version > SavedData.Current)
            throw new NotSupportedException($"This save was written by a newer One Style (data {data.Version}); this build reads up to {SavedData.Current}.");
        s_lastBack = back;
        return (back, data);
    }
}

/// <summary>
/// Reads the open character's save file once (after the game has read it) for what the game skipped: One Style's own
/// part, and the bank stacks, worn items and loadout items whose ids the game did not know yet.
/// </summary>
public sealed class SaveScan
{
    public sealed record Stack(int Tab, int Index, int Quantity, bool Placeholder, bool Locked, string ItemId);
    public sealed record Slot(string SlotId, int Quantity, bool Occupied, string ItemId);
    public sealed record LoadoutSlot(string LoadoutId, string SlotId, int Quantity, bool Occupied, string ItemId);

    public SavedData? Data;
    public readonly List<Stack> Stacks = new();
    public readonly List<Slot> Slots = new();
    public readonly List<LoadoutSlot> Loadouts = new();
    /// <summary>The save file was read through to its end.</summary>
    public bool Read;
    /// <summary>The character has a save file (false: a new character, nothing to read).</summary>
    public bool Exists;
    /// <summary>The file exists but could not be read through (the game's own layout changed, or the file is damaged).</summary>
    public bool Failed;
    /// <summary>One Style's own part was found but not understood; <see cref="RawPayload"/> has it as it is.</summary>
    public bool PayloadFailed;
    /// <summary>The bank or the equipment could not be read: the items are known, their places are not.</summary>
    public bool PlacesFailed;
    /// <summary>The items did not come from the save that was loaded: from One Style's own copy, or from a kept copy of the save.</summary>
    public bool FromOutside;
    public byte[]? RawPayload;

    /// <summary>
    /// The places as One Style wrote them itself. They take the place of what the hand-written reading of the game's
    /// bank and equipment found; only an item that reading found and this list does not know at all is added from it.
    /// </summary>
    public void UsePlaces(List<Place> places)
    {
        var known = places.Select(p => p.Item).ToHashSet();
        Stacks.RemoveAll(x => known.Contains(x.ItemId) || !x.ItemId.StartsWith("MelvorBase:Os"));
        Slots.RemoveAll(x => known.Contains(x.ItemId) || !x.ItemId.StartsWith("MelvorBase:Os"));
        Loadouts.RemoveAll(x => known.Contains(x.ItemId) || !x.ItemId.StartsWith("MelvorBase:Os"));
        foreach (Place p in places)
            switch (p.Kind)
            {
                case Place.Bank: Stacks.Add(new Stack(p.Tab, p.Index, p.Q, false, p.Locked, p.Item)); break;
                case Place.Worn: Slots.Add(new Slot(p.Slot, p.Q, false, p.Item)); break;
                case Place.InLoadout: Loadouts.Add(new LoadoutSlot(p.Loadout, p.Slot, p.Q, false, p.Item)); break;
            }
        Failed = false;
        PlacesFailed = false;
    }
    public string Error = "";

    /// <summary>A static property of the game's SaveManager: by its name, or (should a game update rename it) the only one of its type.</summary>
    internal static PropertyInfo? SaveManagerProperty(string name, Type type)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        if (typeof(SaveManager).GetProperty(name, flags) is { } named && type.IsAssignableFrom(named.PropertyType)) return named;
        var same = typeof(SaveManager).GetProperties(flags).Where(p => p.PropertyType == type).ToList();
        return same.Count == 1 ? same[0] : null;
    }

    /// <summary>For tests only: the save cannot be read at all (as after a game update that changes its frame).</summary>
    public static bool TestBlind;

    public static SaveScan Of(Game game)
    {
        var scan = new SaveScan();
        if (TestBlind) return new SaveScan { Exists = true, Failed = true, Error = "test: the save's frame was not understood" };
        PropertyInfo? idProperty = SaveManagerProperty("ActiveSaveId", typeof(Guid)), sourceProperty = SaveManagerProperty("ActiveSaveSource", typeof(ISaveSource));
        // The game no longer says which save is open (a game update): a character is open and its save cannot be found.
        // That is a failure. It must never pass for "a new character", whose part of the save is written anew.
        if (idProperty == null || sourceProperty == null || idProperty.GetValue(null) is not Guid id || id == Guid.Empty)
            return new SaveScan { Exists = true, Failed = true, Error = "the game does not say which save is open" };
        // a new character: the game gave it an id and no place to read from yet
        if (sourceProperty.GetValue(null) is not ISaveSource source) return scan;
        bool known = false;
        try { scan.Exists = source.StoredSaves.ContainsKey(id); known = true; } catch (Exception) { }
        Stream file;
        // always the active save: after loading a backup the game has already written it over the active save and
        // renumbered its backups, so the backup number would now point at another file
        try { file = source.GetReadStreamForId(id); }
        catch (Exception e)
        {
            // a new character has no file yet; a file that is there (or may be there) and cannot be opened is a failure, not "nothing"
            if (scan.Exists || !known) { scan.Exists = true; scan.Failed = true; scan.Error = e.Message; }
            return scan;
        }
        scan.Exists = true;
        Parse(scan, file, game);
        return scan;
    }

    /// <summary>A save from a stream (one of the game's own backups), read the same way.</summary>
    public static SaveScan OfStream(Game game, Stream stream)
    {
        var scan = new SaveScan { Exists = true };
        try { Parse(scan, stream, game); }
        catch (Exception e) { scan.Failed = true; scan.Error = e.Message; }
        return scan;
    }

    /// <summary>A save file on disk (the copy kept in kept-saves), read the same way.</summary>
    public static SaveScan OfFile(Game game, string path)
    {
        var scan = new SaveScan { Exists = true };
        try { Parse(scan, new MemoryStream(File.ReadAllBytes(path)), game); }
        catch (Exception e) { scan.Failed = true; scan.Error = e.Message; }
        return scan;
    }

    static void Parse(SaveScan scan, Stream file, Game game)
    {
        try
        {
            using (file)
            using (var unpacked = new BrotliStream(file, CompressionMode.Decompress))
            using (var reader = new SaveReader(unpacked, game.Data, game.Entities))
            {
                reader.Read<SaveHeader>();
                int entities = reader.ReadInt32();
                for (int i = 0; i < entities; i++)
                {
                    string entityId = reader.ReadString();
                    switch (entityId)
                    {
                        case OneStyleData.FullId:
                            // its bytes first, as they are: whatever happens after, they can be written back
                            scan.RawPayload = Region(reader);
                            try { using SaveReader inner = Reader(scan.RawPayload, reader, game); scan.Data = OneStyleData.ReadPayload(inner).Data; }
                            catch (Exception e) { scan.PayloadFailed = true; scan.Error = e.Message; }
                            break;
                        case "MelvorBase:Bank":
                        {
                            byte[] bytes = Region(reader);
                            try { using SaveReader inner = Reader(bytes, reader, game); Components(inner, game, "Inventory", c => scan.ReadBank(c)); }
                            catch (Exception e) { scan.PlacesFailed = true; scan.Stacks.Clear(); scan.Error = "bank: " + e.Message; }
                            break;
                        }
                        case "MelvorBase:Combat":
                        {
                            byte[] bytes = Region(reader);
                            try { using SaveReader inner = Reader(bytes, reader, game); Components(inner, game, "Equipment", c => scan.ReadEquipment(c, game)); }
                            catch (Exception e) { scan.PlacesFailed = true; scan.Slots.Clear(); scan.Loadouts.Clear(); scan.Error = "equipment: " + e.Message; }
                            break;
                        }
                        default:
                            reader.SkipRegion();
                            break;
                    }
                }
            }
            scan.Read = true;
        }
        catch (Exception e)
        {
            // the file's own frame could not be followed: whatever was found before that stays, the rest is unknown
            scan.Failed = true;
            scan.Error = e.Message;
        }
    }

    static void Components(SaveReader entity, Game game, string wanted, Action<SaveReader> read)
    {
        int count = entity.ReadInt32();
        for (int j = 0; j < count; j++)
        {
            string componentId = entity.ReadString();
            if (componentId != wanted) { entity.SkipRegion(); continue; }
            using SaveReader inner = Inner(entity, game);
            read(inner);
            return;
        }
    }

    /// <summary>The bytes of the next region of <paramref name="outer"/> (its length comes first).</summary>
    static byte[] Region(SaveReader outer)
    {
        int length = outer.ReadInt32();
        if (length < 0) throw new InvalidDataException("a region of the save has a negative length");
        byte[] bytes = outer.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException("the save ends inside a region");
        return bytes;
    }

    /// <summary>A reader over a region's bytes, with the save's version as the game's readers expect it.</summary>
    static SaveReader Reader(byte[] bytes, SaveReader outer, Game game)
    {
        var stream = new MemoryStream(bytes.Length + 4);
        stream.Write(BitConverter.GetBytes(outer.VersionInt));
        stream.Write(bytes);
        stream.Position = 0;
        return new SaveReader(stream, game.Data, game.Entities);
    }

    /// <summary>The next region of <paramref name="outer"/> as a reader of its own.</summary>
    public static SaveReader Inner(SaveReader outer, Game game)
    {
        int length = outer.ReadInt32();
        byte[] bytes = outer.ReadBytes(length);
        var stream = new MemoryStream(length + 4);
        stream.Write(BitConverter.GetBytes(outer.VersionInt));
        stream.Write(bytes);
        stream.Position = 0;
        return new SaveReader(stream, game.Data, game.Entities);
    }

    /// <summary>
    /// BankInventoryComponent.ReadFrom: the default tab, then every tab (StackableInventory.ReadFrom plus its label, its
    /// tags and, from game 0.11.7 on, its icon item). The game writes the tabs one after the other with no length in
    /// between, so one field more or less in a later game version would shift everything after it: every number and
    /// every id read here is checked, and anything that cannot be a bank throws. The caller then knows that the places
    /// are unknown, and every rare item goes to the bank instead of some landing where the shifted bytes say.
    /// </summary>
    void ReadBank(SaveReader r)
    {
        static T Sane<T>(T value, Func<T, bool> ok, string what) => ok(value) ? value : throw new InvalidDataException("the bank part of the save has another layout (" + what + ")");
        static bool IsId(string id) => id.Length is > 2 and < 200 && id.IndexOf(':') > 0 && id.All(c => char.IsLetterOrDigit(c) || c is ':' or '_' or '-' or '.');
        r.ReadInt32();   // DefaultNewItemTab
        int tabs = Sane(r.ReadInt32(), v => v is >= 0 and <= 500, "tabs");
        for (int t = 0; t < tabs; t++)
        {
            r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadBoolean(); r.ReadBoolean();   // max stacks, min/max quantity, combining, same stacks
            int stacks = Sane(r.ReadInt32(), v => v is >= 0 and <= 100_000, "stacks of tab " + t);
            for (int i = 0; i < stacks; i++)
            {
                int quantity = Sane(r.ReadInt32(), v => v >= 0, "a quantity");
                bool placeholder = r.ReadBoolean();
                bool locked = r.Version >= SaveVersion.Alpha0_4_4 && r.ReadBoolean();
                string itemId = Sane(r.ReadString(), IsId, "an item id");
                Stacks.Add(new Stack(t, i, quantity, placeholder, locked, itemId));
            }
            Sane(r.ReadString(), v => v.Length < 500, "a tab's label");
            if (r.Version < SaveVersion.Alpha0_8_0) continue;
            int tags = Sane(r.ReadInt32(), v => v is >= 0 and <= 10_000, "tags of tab " + t);
            for (int g = 0; g < tags; g++) { r.ReadBoolean(); r.ReadBoolean(); Sane(r.ReadString(), IsId, "a tag id"); }
            if (r.Version >= SaveVersion.Alpha0_11_7 && r.ReadBoolean()) Sane(r.ReadString(), IsId, "a tab's icon item");
        }
    }

    /// <summary>EquipmentComponent.ReadFrom: the worn slots, then every loadout (EquipmentLoadout / EquipmentSlotLoadout).</summary>
    void ReadEquipment(SaveReader r, Game game)
    {
        int slots = r.ReadInt32();
        for (int i = 0; i < slots; i++)
        {
            string slotId = r.ReadString();
            using SaveReader s = Inner(r, game);
            var (q, occ, item) = SlotContent(s);
            Slots.Add(new Slot(slotId, q, occ, item));
        }
        int loadouts = r.ReadInt32();
        for (int i = 0; i < loadouts; i++)
        {
            string loadoutId = r.ReadString();
            using SaveReader l = Inner(r, game);
            int count = l.ReadInt32();
            for (int j = 0; j < count; j++)
            {
                string slotId = l.ReadString();
                using SaveReader s = Inner(l, game);
                var (q, occ, item) = SlotContent(s);
                Loadouts.Add(new LoadoutSlot(loadoutId, slotId, q, occ, item));
            }
        }
    }

    /// <summary>ActiveEquipmentSlot / EquipmentSlotLoadout: quantity, occupied, has item, item id.</summary>
    static (int Quantity, bool Occupied, string ItemId) SlotContent(SaveReader s)
    {
        int quantity = s.ReadInt32();
        bool occupied = s.ReadBoolean();
        string item = s.ReadBoolean() ? s.ReadString() : "";
        return (quantity, occupied, item);
    }
}
