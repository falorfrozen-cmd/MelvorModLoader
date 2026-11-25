using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.CombatAreas;
using GamesByMalcs.Melvor2.Components.Equipment;
using GamesByMalcs.Melvor2.Components.Inventories;
using GamesByMalcs.Melvor2.Core;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.Entities.Combat;

namespace OneStyle;

/// <summary>Rare items while a character is open: back from the save, dropping, and the give-back list at every save.</summary>
public static partial class RareItems
{
    static Game? s_game;
    static OneStyleData? s_entity;
    public static SavedData Data => s_entity?.Data ?? new SavedData();
    public static int Restored { get; private set; }
    public static List<string> Problems { get; } = new();
    /// <summary>The last items that dropped (tests and the window).</summary>
    public static List<RolledItem> LastDrops { get; } = new();

    /// <summary>The game's core systems (a private record of Game, so read by reflection).</summary>
    static ICoreSystems CoreOf(Game game) =>
        (ICoreSystems)typeof(Game).GetProperty("Core", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(game)!;
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Equipped")]
    static extern void SetLoadoutEquipped(EquipmentSlotLoadout slot, EquipmentItem? item);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Quantity")]
    static extern void SetLoadoutQuantity(EquipmentSlotLoadout slot, int quantity);
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Loadouts")]
    static extern Dictionary<GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot, EquipmentLoadout> LoadoutsOf(EquipmentComponent equipment);

    public static bool IsOurs(Item item) => item.LocalId.StartsWith("OsR") || item.LocalId.StartsWith("OsU");
    public static RolledItem? RecordOf(Item item) =>
        !IsOurs(item) ? null
        : Data.Items.FirstOrDefault(r => r.LocalId == item.LocalId)
          ?? (Uniques.Defs.TryGetValue(item.LocalId, out UniqueDef? u) ? Uniques.RecordOf(u) : null);   // a unique item is always the same

    static CombatEntity? Combat(Game game) => game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) ? e as CombatEntity : null;
    static BankEntity? Bank(Game game) => game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? e) ? e as BankEntity : null;

    /// <summary>
    /// A character was opened (before its first save): One Style's part of the save is read back, every item of it is
    /// made into a game item and registered, and the entity that writes it again is added to the game.
    /// Step one of two; <see cref="Restore"/> comes after the extra ring slots exist.
    ///
    /// Where the items come from, in this order:
    ///   1. One Style's own part of the save, when it is there and understood. It always wins.
    ///   2. No such part (the game ran without the mod and saved, or the save could not be read at all): One Style's own
    ///      copy outside the save (<see cref="Vault"/>), and failing that the copy of the save kept on the day the save
    ///      could not be read.
    ///   3. The part is there and not understood (a newer One Style wrote it): safe mode, it is written back as it is.
    ///   4. Nothing of the above: blind safe mode. One Style writes nothing at all for this character.
    /// </summary>
    public static SaveScan? Bind(Game game)
    {
        if (ReferenceEquals(s_game, game)) return null;
        if (s_game != null) Unbind();   // another character came before the mod saw the last one close
        s_game = game;
        OneStyleData.ForgetLast();
        Problems.Clear();
        LastDrops.Clear();
        lock (s_newDrops) s_newDrops.Clear();
        Restored = 0;
        Forget();
        s_unmade.Clear();
        SafeMode = false;
        Blind = false;
        Recovered = 0;
        SafeNote = "";
        s_settled = false;
        s_loadedPlaces = new();
        Guid id = Vault.ActiveId();
        // before anything can be written for this character
        Guard("the copy of the save for a new game version", () => KeepForNewGame(game, id));
        Guard("the copy of the rare items file", () => Vault.Snapshot(id));
        SaveScan scan = Scan(game);
        // nothing of One Style's part was found: a file that is busy for a moment is read again before giving up
        for (int tries = 0; tries < 4 && scan.Failed && scan.Data == null && scan.RawPayload == null && !SaveScan.TestBlind; tries++)
        {
            System.Threading.Thread.Sleep(120);
            scan = Scan(game);
        }
        if (scan.Exists && scan.Data == null && scan.RawPayload == null)
            try { scan = FromOutside(game, scan, id); }
            catch (Exception e) { Log("One Style: its own copy of the rare items could not be used: " + e); }
        // The one rule of this method: One Style's part of a save is only ever written anew when it was read and
        // understood (or the character is new). Anything else, and what the save had goes back into it untouched.
        bool safe = scan.Exists && (scan.Failed && scan.Data == null || scan.PayloadFailed);
        try
        {
            if (safe && scan.RawPayload == null)
            {
                // blind: no entity, so nothing of One Style's is written into this save and nothing over its own copy
                EnterSafeMode(game, scan);
                return scan;
            }
            DataNamespace ns = game.Data.GetObject<Item>("MelvorBase:Bone").Namespace;
            if (game.Entities.TryGetObjectById(OneStyleData.FullId, out GameEntity? had) && had is OneStyleData old) s_entity = old;
            else
            {
                s_entity = new OneStyleData(CoreOf(game), ns);
                game.Entities.RegisterObject(s_entity);
            }
            s_entity.Raw = null;
            s_entity.SaveId = id;
            if (safe)
            {
                EnterSafeMode(game, scan);
                return scan;
            }
            s_entity.Data = scan.Data ?? new SavedData();
            OneStyleData.GiveBack = () => GiveBackList(game);
            OneStyleData.CharacterName = () => game.CharacterName;
            s_loadedPlaces = s_entity.Data.Places.ToList();
            if (s_entity.Data.HasPlaces) scan.UsePlaces(s_entity.Data.Places);
            TakeBackGone(scan);
            foreach (RolledItem r in s_entity.Data.Items.ToList())
            {
                Item? made = null;
                try { made = Make(game, r); }
                catch (Exception e) { Log($"One Style: {r.Name} could not be made: {e.Message}"); }
                if (made != null) continue;
                // kept as it is (never pruned): a later build may know how to make it
                s_unmade.Add(r.LocalId);
                Problems.Add("an item could not be made and is kept in the save for later: " + r.Name);
            }
            if (scan.Failed || scan.PlacesFailed)
                Problems.Add("the places of your rare items could not be read from the save (" + scan.Error + "): they are put in your bank instead");
        }
        catch (Exception e)
        {
            Problems.Add("rare items: " + e.Message);
            Log("One Style: rare items could not be set up: " + e);
            // Nothing of One Style's is in the game for this character, so the game will save without it. The player
            // must know; the items are in One Style's own copy (it is not written now) and in the copy kept here.
            if (s_entity == null && scan.Exists && !SafeMode)
            {
                scan.Error = e.Message;
                scan.RawPayload = null;
                try { EnterSafeMode(game, scan); } catch (Exception) { SafeMode = true; Blind = true; }
            }
        }
        return scan;
    }

    static void Guard(string what, Action run)
    {
        try { run(); }
        catch (Exception e) { Log($"One Style: {what}: {e.Message}"); }
    }

    /// <summary>
    /// True once every record of the save is accounted for after a load: the item is held, waits for the bank, or could
    /// not be made and is kept. Only then may a save drop the record of an item the character no longer has, and only
    /// then are the places written anew. Until then every save writes back everything that was read.
    /// (Before this rule a load that stopped half way - the game's bank or combat entity renamed by an update, an error
    /// while putting items back - left records that looked "no longer owned", and the next save threw them away.)
    /// </summary>
    static bool s_settled;
    /// <summary>The places as the save had them (for the rings in the added ring slots).</summary>
    static List<Place> s_loadedPlaces = new();

    /// <summary>Items in the save that this build could not make (their base item is unknown): never dropped from the save.</summary>
    static readonly HashSet<string> s_unmade = new();

    /// <summary>
    /// True when One Style's part of this save could not be read: rare items rest for this character (no drops, nothing
    /// converted, nothing pruned) and that part of the save is written back byte for byte.
    /// </summary>
    public static bool SafeMode { get; private set; }
    public static string SafeNote { get; private set; } = "";

    /// <summary>Safe mode without the bytes of One Style's part and without a copy of its own to take the items from:
    /// One Style writes nothing for this character, and the copy of the save kept now is where the rare items are.</summary>
    public static bool Blind { get; private set; }

    static SaveScan Scan(Game game)
    {
        try { return SaveScan.Of(game); }
        catch (Exception e) { return new SaveScan { Exists = true, Failed = true, Error = e.Message }; }
    }

    /// <summary>
    /// The save has no part of One Style's that could be found. The items come from One Style's own file for this save,
    /// or from the copy of the save kept on the day it could not be read. Returns the scan to go on with.
    /// </summary>
    /// <summary>For tests only: which places outside the save may be used (1 its own file, 2 the game's backups, 4 the kept copy).</summary>
    public static int TestSources = 7;
    /// <summary>For tests only: the save is written as a build before this one wrote it, without the places.</summary>
    public static bool TestNoPlaces;
    /// <summary>For tests only: the load counts as finished (true) or as stopped half way (false).</summary>
    public static bool TestSettled { get => s_settled; set => s_settled = value; }

    static SaveScan FromOutside(Game game, SaveScan scan, Guid id)
    {
        DateTime loaded = game.LoadedSaveTimestamp.ToUniversalTime();
        if ((TestSources & 1) != 0 && Vault.Read(id) is { HoldsSomething: true } kept)
        {
            if (Vault.TakenByLoader(id) is { } taken && taken > kept.WrittenUtc)
            {
                Log($"One Style: its own copy of this character's rare items ({kept.WrittenUtc:u}) is older than the day the mod loader gave them back as normal items ({taken:u}): it is not used.");
                return scan;
            }
            // read to its end, nothing of One Style's in it, and older than the file: a backup from before was loaded
            if (!scan.Failed && scan.Read && loaded < kept.WrittenUtc.AddMinutes(-1))
            {
                string note = $"this save ({loaded.ToLocalTime():g}) is older than One Style's own copy of your rare items ({kept.WrittenUtc.ToLocalTime():g}) and holds none itself: "
                    + "a backup from before was loaded, so nothing was brought back. The copy stays in " + Vault.Folder;
                Problems.Add(note);
                Log("One Style: " + note);
                return scan;
            }
            Recovered = kept.Data.Items.Count;
            Log($"One Style: this save holds no rare items of One Style's ({(scan.Failed ? scan.Error : "the game saved without the mod")}). "
                + $"{Recovered} rare item(s) come back from One Style's own copy of {kept.WrittenUtc.ToLocalTime():g} ({Vault.FileOf(id)}).");
            return new SaveScan { Exists = true, Read = true, Data = kept.Data, FromOutside = true };
        }
        if ((TestSources & 1) != 0 && Vault.FileOf(id) is { } own && System.IO.File.Exists(own)) return scan;   // its own file says: nothing to bring back
        // No file of its own (another computer, or a save from before One Style kept one): the game's own backups.
        // The game copies the save every time a character is opened, so the newest backup that still has a part of One
        // Style's is the save as it was when the mod last ran. That one decides: items in it come back, none in it
        // (the player made them all normal) means there is nothing to bring back.
        try
        {
            if ((TestSources & 2) != 0 && SaveScan.SaveManagerProperty("ActiveSaveSource", typeof(GamesByMalcs.Melvor2.Saves.ISaveSource))?.GetValue(null)
                is GamesByMalcs.Melvor2.Saves.ISaveSource source && source.AreBackupsLoaded
                && source.BackupSaves.FirstOrDefault(x => x.Id == id) is { } backups)
                foreach (GamesByMalcs.Melvor2.Saves.ISaveBackup backup in backups.Backups.OrderByDescending(x => x.Header.Timestamp))
                {
                    SaveScan old;
                    try { old = SaveScan.OfStream(game, source.GetBackupStream(id, backup.Number)); }
                    catch (Exception) { continue; }
                    // only a backup from before the save that was loaded can be where this save came from: a newer one
                    // belongs to another line of play (an old save was put back, or one of the game's backups was loaded)
                    if (backup.Header.Timestamp.ToUniversalTime() > loaded.AddMinutes(1)) continue;
                    if (old.Data == null && old.RawPayload == null) continue;   // written without the mod: look further back
                    if (old.Data == null || (old.Data.Items.Count == 0 && old.Data.NormalPending.Count == 0)) break;
                    DateTime at = backup.Header.Timestamp.ToUniversalTime();
                    if (Vault.TakenByLoader(id) is { } given && given > at) break;
                    Recovered = old.Data.Items.Count;
                    old.FromOutside = true;
                    Log($"One Style: this save holds no rare items of One Style's. {Recovered} rare item(s) come back from the game's own backup of {at.ToLocalTime():g} (number {backup.Number}).");
                    return old;
                }
        }
        catch (Exception e) { Log("One Style: the game's own backups could not be searched: " + e.Message); }
        // the copy of the save kept on the day One Style could not read it (see KeepSaveCopy)
        if ((TestSources & 4) != 0 && Dir != "" && System.IO.Directory.Exists(System.IO.Path.Combine(Dir, "kept-saves"))
            && System.IO.Directory.GetFiles(System.IO.Path.Combine(Dir, "kept-saves"), $"blind-save_{id:N}_*.sav").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault() is { } copy)
        {
            SaveScan old = SaveScan.OfFile(game, copy);
            if (old.Data == null)
            {
                // still not readable: the caller decides (blind again when the save itself cannot be read)
                if (!scan.Failed) Problems.Add("the copy of your save with your rare items (" + copy + ") cannot be read by this version of One Style yet (" + old.Error + "). It is kept");
                return scan;
            }
            // it has done its work: under another name, so that it is never taken for the copy of a later day
            try { System.IO.File.Move(copy, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(copy)!, "used-" + System.IO.Path.GetFileName(copy)), true); }
            catch (Exception e) { Log("One Style: the used copy could not be renamed: " + e.Message); }
            Recovered = old.Data.Items.Count;
            old.FromOutside = true;
            Log($"One Style: {Recovered} rare item(s) come back from the copy of the save kept in {copy}.");
            return old;
        }
        return scan;
    }

    static void EnterSafeMode(Game game, SaveScan scan)
    {
        SafeMode = true;
        Blind = scan.RawPayload == null;
        if (s_entity != null)
        {
            s_entity.Data = new SavedData();
            s_entity.Raw = scan.RawPayload;
        }
        OneStyleData.GiveBack = null;
        string copy = KeepSaveCopy(Blind);
        if (!Blind)
            SafeNote = "One Style could not read its rare items from this save (" + scan.Error + "). Nothing was thrown away: "
                + "they stay in the save exactly as they were"
                + (copy != "" ? ", and a copy of the save from before was kept in " + copy : "")
                + ". Rare items rest for this character until One Style is updated; the rest of the mod works.";
        else
            // One Style's part could not even be found, so it cannot be written back, and there is no copy of One Style's
            // own for this character yet: the game's next save will not hold the rare items. The copy of the save does,
            // and the first build that reads this character's save again brings them back from it (FromOutside).
            SafeNote = "One Style could not find its rare items in this save (" + scan.Error + "). "
                + (copy != ""
                    ? "They are safe in a copy of the save from before, kept in " + copy + ". Do not delete that file: when One Style can read "
                      + "this character's save again (after an update of the mod), the rare items come back from it by themselves"
                    : "A copy of the save could NOT be kept either: close the game without playing this character and update One Style, "
                      + "or your rare items will be missing from the game's next save")
                + ". Rare items rest for this character until then; the rest of the mod works.";
        Problems.Add(SafeNote);
        Log("One Style: SAFE MODE. " + SafeNote);
    }

    /// <summary>
    /// An item that the save still places somewhere, whose record an earlier save put aside as "no longer owned": the
    /// record comes back. (Should the game ever report an item as gone that it still holds, nothing is lost.)
    /// </summary>
    static void TakeBackGone(SaveScan scan)
    {
        if (Data.Gone.Count == 0) return;
        var placed = scan.Stacks.Select(x => x.ItemId).Concat(scan.Slots.Select(x => x.ItemId)).Concat(scan.Loadouts.Select(x => x.ItemId))
            .Where(i => i.StartsWith("MelvorBase:Os")).Select(i => i["MelvorBase:".Length..]).ToHashSet();
        var have = Data.Items.Select(i => i.LocalId).ToHashSet();
        foreach (RolledItem r in Data.Gone.Where(g => placed.Contains(g.LocalId)).ToList())
        {
            Data.Gone.Remove(r);
            if (!have.Add(r.LocalId)) continue;
            Data.Items.Add(r);
            Log($"One Style: the save still holds {r.Name}, whose record had been put aside: it is back.");
        }
    }

    /// <summary>How many rare items came back from outside the save when this character was opened (shown in the window).</summary>
    public static int Recovered { get; private set; }

    /// <summary>
    /// The game itself is another build than the last time this character was opened with One Style (it was updated):
    /// the save as the old game left it is kept, byte for byte, before the new game or the mod writes anything.
    /// In One Style's folder of copies outside the save; the five newest per character stay.
    /// </summary>
    static void KeepForNewGame(Game game, Guid id)
    {
        if (Vault.Folder == "" || id == Guid.Empty) return;
        string build = typeof(Game).Assembly.ManifestModule.ModuleVersionId.ToString("N");
        string mark = System.IO.Path.Combine(Vault.Folder, $"{id:N}.game-build.txt");
        string before = System.IO.File.Exists(mark) ? System.IO.File.ReadAllText(mark).Trim() : "";
        if (before == build) return;
        System.IO.Directory.CreateDirectory(Vault.Folder);
        if (SaveScan.SaveManagerProperty("ActiveSaveSource", typeof(GamesByMalcs.Melvor2.Saves.ISaveSource))?.GetValue(null)
            is GamesByMalcs.Melvor2.Saves.ISaveSource source)
        {
            byte[] bytes = source.GetSaveDataForId(id);
            if (bytes.Length > 0)
            {
                string file = System.IO.Path.Combine(Vault.Folder, $"{id:N}.before-game-update-{DateTime.Now:yyyyMMdd-HHmmss}.sav");
                System.IO.File.WriteAllBytes(file, bytes);
                Log(before == ""
                    ? $"One Style: first time with this character: a copy of its save was kept in {file}."
                    : $"One Style: the game was updated since this character was last opened. A copy of its save from before was kept in {file}.");
                foreach (string old in System.IO.Directory.GetFiles(Vault.Folder, $"{id:N}.before-game-update-*.sav").OrderByDescending(f => f, StringComparer.Ordinal).Skip(5))
                    System.IO.File.Delete(old);
            }
        }
        System.IO.File.WriteAllText(mark, build);
    }

    /// <summary>
    /// A copy of the active save file as it is on disk now, next to the mod (kept-saves): the last resort. A blind copy
    /// ("blind-...") is made once per save: when one is there already, the save on disk has been written without the
    /// rare items since, and the first copy is the one that holds them.
    /// </summary>
    static string KeepSaveCopy(bool blind)
    {
        try
        {
            if (Dir == "") return "";
            string folder = System.IO.Path.Combine(Dir, "kept-saves");
            Guid id = SaveScan.SaveManagerProperty("ActiveSaveId", typeof(Guid))?.GetValue(null) is Guid g ? g : Guid.Empty;
            if (id == Guid.Empty) return "";
            if (blind && System.IO.Directory.Exists(folder)
                && System.IO.Directory.GetFiles(folder, $"blind-save_{id:N}_*.sav").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault() is { } first)
                return first;
            if (SaveScan.SaveManagerProperty("ActiveSaveSource", typeof(GamesByMalcs.Melvor2.Saves.ISaveSource))?.GetValue(null)
                is not GamesByMalcs.Melvor2.Saves.ISaveSource source) return "";
            byte[] bytes = source.GetSaveDataForId(id);
            if (bytes.Length == 0) return "";
            System.IO.Directory.CreateDirectory(folder);
            string file = System.IO.Path.Combine(folder, $"{(blind ? "blind-" : "")}save_{id:N}_{DateTime.Now:yyyyMMdd-HHmmss}.sav");
            System.IO.File.WriteAllBytes(file, bytes);
            return file;
        }
        catch (Exception e) { Log("One Style: the copy of the save could not be kept: " + e.Message); return ""; }
    }

    /// <summary>Step two: the bank stacks, worn items and loadout items the game skipped go back where they were.</summary>
    public static void Restore(Game game, SaveScan? scan)
    {
        if (scan == null) return;   // already done for this character
        // gear is watched whatever happens below (a new character has nothing to put back, but its caps follow its gear)
        try { WatchGear(game); } catch (Exception e) { Log("One Style: gear could not be watched: " + e.Message); }
        if (SafeMode || Combat(game) is not { } combat || Bank(game) is not { } bank) return;
        try
        {
            RestorePlaces(game, scan, combat, bank);
        }
        finally
        {
            // whatever could not be put back in its place (an unreadable bank or equipment part, a slot that is taken,
            // an error half way) is not lost: every item of the save that the character does not hold waits for the bank
            try
            {
                var held = Owned(game).GroupBy(o => o.Item.LocalId).ToDictionary(g => g.Key, g => g.Sum(o => o.Quantity));
                foreach (RolledItem r in Data.Items)
                {
                    if (s_unmade.Contains(r.LocalId)) continue;
                    // as many as the save says the character owned (two copies of one Unique share this record)
                    int want = Math.Max(1, Data.Counts.GetValueOrDefault(r.LocalId));
                    int have = held.GetValueOrDefault(r.LocalId) + Data.Pending.Count(p => p == r.LocalId);
                    for (; have < want; have++) Data.Pending.Add(r.LocalId);
                }
                RingsBack(game, combat);
                // from here on every record is held, waits or is kept as unmade: saves may tidy again
                s_settled = true;
                DeliverPending(game);
            }
            catch (Exception e) { Problems.Add("restore (the rest): " + e.Message); Log("One Style: rare items could not be queued for the bank: " + e); }
        }
    }

    static void RestorePlaces(Game game, SaveScan scan, CombatEntity combat, BankEntity bank)
    {
        try
        {
            var registry = game.Data.GetDataRegistryOutput<Item>();
            Item? Ours(string id) => id != "" && registry.TryGetObjectById(id, out Item? i) && IsOurs(i) ? i : null;
            BankInventoryComponent inventory = bank.Inventory;
            // back into the same tab at the same place (in the order they were, so each lands where it was), locked again
            // when the player had locked it
            foreach (SaveScan.Stack s in scan.Stacks.OrderBy(s => s.Tab).ThenBy(s => s.Index))
            {
                if (Ours(s.ItemId) is not { } item || s.Quantity <= 0 || inventory.GetQuantity(item) > 0) continue;
                bool inTab = s.Tab >= 0 && s.Tab < inventory.InventoryTabs.Count;
                if (inTab) inventory.DefaultItemTabs[item] = inventory.InventoryTabs[s.Tab];
                if (inventory.AddQuantity(item, s.Quantity, AddItemFlags.None) <= 0) { Data.Pending.Add(item.LocalId); continue; }
                Restored++;
                if (!inTab) continue;
                try
                {
                    var tab = inventory.InventoryTabs[s.Tab];
                    int at = -1;
                    for (int i = tab.Stacks.Count - 1; i >= 0; i--) if (ReferenceEquals(tab.Stacks[i].Item, item)) { at = i; break; }
                    if (at >= 0 && at != s.Index) tab.MoveStack(at, Math.Min(s.Index, tab.StackCount - 1));
                    if (s.Locked)
                    {
                        tab.SetItemAsLocked(item, true);
                        LockedItemsOf(inventory).Add(item);
                    }
                }
                catch (Exception e) { Log("One Style: a rare item went back to the end of its tab: " + e.Message); }
            }
            EquipmentComponent equipment = combat.Equipment;
            foreach (SaveScan.Slot s in scan.Slots)
            {
                if (s.Occupied || Ours(s.ItemId) is not { } item || !game.Data.TryGetObject(s.SlotId, out EquipmentSlot? slot)) continue;
                if (!equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? active) || !active.IsEmpty) continue;
                if (equipment.TryEquipItem(item, Math.Max(1, s.Quantity), slot)) Restored++;
                else Data.Pending.Add(item.LocalId);
            }
            Dictionary<GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot, EquipmentLoadout> loadouts = LoadoutsOf(equipment);
            foreach (SaveScan.LoadoutSlot s in scan.Loadouts)
            {
                if (s.Occupied || Ours(s.ItemId) is not { } item) continue;
                if (!game.Data.TryGetObject(s.LoadoutId, out GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot? loadoutSlot)
                    || !loadouts.TryGetValue(loadoutSlot, out EquipmentLoadout? loadout) || !game.Data.TryGetObject(s.SlotId, out EquipmentSlot? slot)) continue;
                if (loadout.Slots.TryGetValue(slot, out EquipmentSlotLoadout? have) && have.Equipped != null) continue;
                var saved = new EquipmentSlotLoadout();
                SetLoadoutEquipped(saved, new EquipmentItem(item, item.Capabilities.Get<EquipmentCapability>()));
                SetLoadoutQuantity(saved, Math.Max(1, s.Quantity));
                loadout.Slots[slot] = saved;
                Restored++;
            }
        }
        catch (Exception e) { Problems.Add("restore: " + e.Message); Log("One Style: rare items could not be put back: " + e); }
    }

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = "get_LockedItems")]
    static extern HashSet<Item> LockedItemsOf(BankInventoryComponent bank);

    /// <summary>
    /// With the game's "automatic placeholders" on, a rare item that is sold, lost or destroyed would keep an empty
    /// placeholder in the bank for ever (the same item never comes back). Those go; a rare item still worn or in a
    /// loadout keeps its placeholder.
    /// </summary>
    public static void TidyPlaceholders(Game game)
    {
        if (Bank(game) is not { } bank) return;
        try
        {
            var owned = Owned(game).Select(o => o.Item).ToHashSet();
            foreach (Item item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
                if (IsOurs(item) && !owned.Contains(item) && bank.Inventory.GetQuantity(item) <= 0)
                    bank.Inventory.SetItemAsPlaceholder(item, false);
        }
        catch (Exception e) { Log("One Style: rare item placeholders: " + e.Message); }
    }

    public static void Unbind()
    {
        s_capsOn = false;
        s_watchedEquipment = null;
        s_game = null;
        s_entity = null;
        OneStyleData.GiveBack = null;
        OneStyleData.ForgetLast();
        SafeMode = false;
        Blind = false;
        Recovered = 0;
        SafeNote = "";
        s_unmade.Clear();
        s_settled = false;
        s_loadedPlaces = new();
        // what the last character's gear had switched on is gone with its game
        s_floorMs = 0;
        s_sameRingOn.Clear();
    }

    // ================================================================ owning, giving back

    /// <summary>Every One Style item the player has now: in the bank, worn and in loadouts (occupied slots do not count).</summary>
    public static List<(Item Item, int Quantity)> Owned(Game game)
    {
        var list = new List<(Item, int)>();
        if (Combat(game) is not { } combat || Bank(game) is not { } bank) return list;
        var registry = game.Data.GetDataRegistryOutput<Item>();
        foreach (RolledItem r in Data.Items)
        {
            if (!registry.TryGetObjectById("MelvorBase:" + r.LocalId, out Item? item)) continue;
            int n = bank.Inventory.GetQuantity(item);
            foreach (EquipmentSlot slot in combat.Equipment.Config.EquipmentSlots)
                if (combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty && !a.IsOccupied && ReferenceEquals(a.Equipped!.Item, item))
                    n += Math.Max(1, a.Quantity);
            foreach (EquipmentLoadout l in LoadoutsOf(combat.Equipment).Values)
                foreach (EquipmentSlotLoadout s in l.Slots.Values)
                    if (!s.IsOccupied && s.Equipped != null && ReferenceEquals(s.Equipped.Item, item)) n += Math.Max(1, s.Quantity);
            if (n > 0) list.Add((item, n));
        }
        return list;
    }

    /// <summary>At every save: what to give back as normal items if One Style is gone; records no longer owned are dropped.</summary>
    static List<(string, int)> GiveBackList(Game game)
    {
        List<(Item Item, int Quantity)> owned = Owned(game);
        var ownedIds = owned.Select(o => o.Item.LocalId).ToHashSet();
        var back = new Dictionary<string, int>();
        bool whole = s_settled && Combat(game) is not null && Bank(game) is not null;
        if (whole)
        {
            // everything below is worked out first and written into the data last: an error half way changes nothing
            List<Place> places = PlacesNow(game);
            var gone = Data.Items.Where(r => !ownedIds.Contains(r.LocalId) && !Data.Pending.Contains(r.LocalId) && !s_unmade.Contains(r.LocalId)).ToList();
            var counts = new Dictionary<string, int>();
            foreach (RolledItem r in Data.Items.Except(gone))
            {
                int n = owned.Where(o => o.Item.LocalId == r.LocalId).Sum(o => o.Quantity) + Data.Pending.Count(p => p == r.LocalId);
                // (an item that could not be made keeps the number the save had)
                if (s_unmade.Contains(r.LocalId)) n = Data.Counts.GetValueOrDefault(r.LocalId);
                if (n > 1) counts[r.LocalId] = n;
            }
            // a record the character no longer has is put aside, not thrown away (see TakeBackGone); the 200 newest stay
            foreach (RolledItem r in gone) { Data.Items.Remove(r); Data.Gone.Add(r); }
            if (Data.Gone.Count > 200) Data.Gone.RemoveRange(0, Data.Gone.Count - 200);
            Data.Counts = counts;
            // the places of items that could not be made stay as the save had them
            places.AddRange(s_loadedPlaces.Where(p => p.Item.StartsWith("MelvorBase:") && s_unmade.Contains(p.Item["MelvorBase:".Length..])));
            Data.Places = places;
            Data.HasPlaces = true;
            if (TestNoPlaces) { Data.Places = new(); Data.HasPlaces = false; }
        }
        else
            // the load did not finish: nothing is tidied, and every record the character does not hold counts for the list
            foreach (RolledItem r in Data.Items)
                if (!ownedIds.Contains(r.LocalId) && !Data.Pending.Contains(r.LocalId))
                {
                    string baseId = r.UniqueId != "" ? Uniques.BaseOf(r.UniqueId) : r.BaseId;
                    if (baseId != "") back[baseId] = back.GetValueOrDefault(baseId) + Math.Max(1, Data.Counts.GetValueOrDefault(r.LocalId));
                }
        foreach (var (item, n) in owned)
        {
            string baseId = RecordOf(item) is { } r ? (r.UniqueId != "" ? Uniques.BaseOf(r.UniqueId) : r.BaseId) : "";
            if (baseId != "") back[baseId] = back.GetValueOrDefault(baseId) + n;
        }
        foreach (string pending in Data.Pending)
            if (Data.Items.FirstOrDefault(r => r.LocalId == pending) is { } r)
            {
                string baseId = r.UniqueId != "" ? Uniques.BaseOf(r.UniqueId) : r.BaseId;
                if (baseId != "") back[baseId] = back.GetValueOrDefault(baseId) + 1;
            }
        // normal items that still wait for room in the bank (from "Make all rare items normal")
        foreach (var (id, n) in Data.NormalPending) back[id] = back.GetValueOrDefault(id) + n;
        // normal rings in Ring Slot 3 to 6 (rare ones are counted above as their base ring)
        foreach (var (ring, n) in Rings.InAddedSlots(game))
            if (!IsOurs(ring)) back[ring.Id] = back.GetValueOrDefault(ring.Id) + n;
        return back.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// "Make all normal" (before removing the mod loader too): every One Style item is swapped for its normal base item,
    /// in the bank, where it is worn, in every loadout, and the ones still waiting for bank room. Nothing is thrown away:
    /// a normal item the bank has no room for waits (NormalPending, kept in the save and in the give-back list) and goes
    /// in when there is room; an item whose base item this build does not know stays as it is. Two rare copies of one
    /// base item in the same loadout become one worn and one in the bank (the game cannot wear the same item twice).
    /// Returns how many were made normal and how many of them wait for room.
    /// </summary>
    public static (int Done, int Waiting) MakeAllNormal(Game game)
    {
        if (SafeMode || Combat(game) is not { } combat || Bank(game) is not { } bank) return (0, 0);
        int done = 0, waiting = 0;
        var converted = new HashSet<string>();
        void ToBank(Item baseItem, int q)
        {
            int added = Math.Max(0, bank.Inventory.AddQuantity(baseItem, q, AddItemFlags.None));
            if (added >= q) return;
            Data.NormalPending[baseItem.Id] = Data.NormalPending.GetValueOrDefault(baseItem.Id) + (q - added);
            waiting += q - added;
        }
        Item? BaseOf(RolledItem? r) =>
            r != null && game.Data.TryGetObject(r.UniqueId != "" ? Uniques.BaseOf(r.UniqueId) : r.BaseId, out Item? b) ? b : null;

        foreach (var (item, n) in Owned(game))
        {
            RolledItem? r = RecordOf(item);
            if (BaseOf(r) is not { } baseItem) continue;   // unknown base: the item stays a One Style item
            int inBank = bank.Inventory.GetQuantity(item);
            if (inBank > 0)
            {
                bank.Inventory.RemoveQuantity(item, inBank);
                try { bank.Inventory.SetItemAsPlaceholder(item, false); } catch (Exception) { }
                ToBank(baseItem, inBank);
            }
            foreach (EquipmentSlot slot in combat.Equipment.Config.EquipmentSlots)
                if (combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty && !a.IsOccupied && ReferenceEquals(a.Equipped!.Item, item))
                {
                    int q = Math.Max(1, a.Quantity);
                    combat.Equipment.TryUnequipItem(item);
                    if (!combat.Equipment.TryEquipItem(baseItem, q, slot)) ToBank(baseItem, q);
                }
            foreach (EquipmentLoadout l in LoadoutsOf(combat.Equipment).Values)
                foreach (var (slot, s) in l.Slots.ToList())
                    if (!s.IsOccupied && s.Equipped != null && ReferenceEquals(s.Equipped.Item, item))
                    {
                        int q = Math.Max(1, s.Quantity);
                        // the same normal item already somewhere in this loadout: this slot is emptied, the item goes to the bank
                        bool twice = l.Slots.Values.Any(o => !ReferenceEquals(o, s) && !o.IsOccupied && o.Equipped != null && ReferenceEquals(o.Equipped.Item, baseItem));
                        if (twice)
                        {
                            SetLoadoutEquipped(s, null);
                            SetLoadoutQuantity(s, 0);
                            ToBank(baseItem, q);
                        }
                        else SetLoadoutEquipped(s, new EquipmentItem(baseItem, baseItem.Capabilities.Get<EquipmentCapability>()));
                    }
            if (r != null) converted.Add(r.LocalId);
            done += n;
        }
        // the ones that were still waiting for room in the bank
        foreach (string id in Data.Pending.ToList())
        {
            RolledItem? r = Data.Items.FirstOrDefault(x => x.LocalId == id);
            if (BaseOf(r) is not { } baseItem) continue;
            Data.Pending.Remove(id);
            converted.Add(id);
            ToBank(baseItem, 1);
            done++;
        }
        Data.Items.RemoveAll(r => converted.Contains(r.LocalId));
        return (done, waiting);
    }
    // ================================================================ drops (03-karar.md 1; 04-esya-tasarim.md)

    /// <summary>A monster died online: elites always drop, a normal monster 2% of the time (1% Magic, 1% Rare).</summary>
    public const double NormalMagic = 0.01, NormalRare = 0.01;

    public static void OnKilled(Game game, Enemy enemy, EliteTier tier, bool special)
    {
        if (SafeMode) return;
        if (Affixes.Count == 0 || !ReferenceEquals(s_game, game)) return;
        try
        {
            int iLvl = Math.Max(1, enemy.CombatLevel + (special ? 5 : 0));
            string style = DropStyle();
            var drops = new List<Rarity>();
            double r1 = s_rng.NextDouble();
            Rarity ChampionRoll() { double r = s_rng.NextDouble(); return r < 0.60 ? Rarity.Magic : r < 0.93 ? Rarity.Rare : Rarity.Rare4; }
            switch (tier)
            {
                case EliteTier.Normal:
                    // 1% Magic, 1% Rare (the player's pick, 2026-11-24, after Codex and Gemini); Hoarder's Eye and Scrap Hunter raise both
                    double more = UniquePowers.RareLootMultiplier;
                    if (r1 < NormalMagic * more) drops.Add(Rarity.Magic);
                    else if (r1 < (NormalMagic + NormalRare) * more) drops.Add(s_rng.NextDouble() < 0.8 ? Rarity.Rare : Rarity.Rare4);
                    break;
                case EliteTier.Champion:
                    drops.Add(ChampionRoll());
                    break;
                case EliteTier.Unique:
                    drops.Add(s_rng.NextDouble() < 0.8 ? Rarity.Rare : Rarity.Rare4);
                    drops.Add(ChampionRoll());
                    break;
            }
            if (tier != EliteTier.Normal && special) drops.Add(ChampionRoll());   // on your slayer task or as a dungeon's last boss: one more
            if (tier != EliteTier.Normal) for (int i = UniquePowers.ExtraEliteRares(); i > 0; i--) drops.Add(Rarity.Rare);   // Deep Signal, Scrap Hunter
            foreach (Rarity rarity in drops)
                if (PickBase(game, iLvl, style) is { } baseItem) Give(game, Roll(game, baseItem, rarity, iLvl, style));
            if (tier == EliteTier.Unique) Uniques.OnUniqueKilled(game, enemy);
        }
        catch (Exception e) { Log("One Style: a drop failed: " + e.Message); }
    }

    /// <summary>A new item: kept in the save data, made, and put in the bank (or kept for later when the bank is full).</summary>
    public static Item? Give(Game game, RolledItem record)
    {
        if (SafeMode) return null;   // this character's rare items rest: nothing new is made that could not be saved
        Data.Items.Add(record);
        Item? item = Make(game, record);
        if (item == null) { Data.Items.Remove(record); return null; }
        LastDrops.Insert(0, record);
        if (LastDrops.Count > 20) LastDrops.RemoveAt(LastDrops.Count - 1);
        if (Bank(game) is not { } bank || bank.Inventory.AddQuantity(item, 1, AddItemFlags.None) <= 0) Data.Pending.Add(record.LocalId);
        else lock (s_newDrops) s_newDrops.Add(record.LocalId);
        DropToast(record);
        return item;
    }

    static readonly List<string> s_newDrops = new();

    /// <summary>
    /// For our other mods (Bulk Sell's auto-sell): the local ids of the Magic and Rare items that dropped into the bank
    /// since the last call, each given once. Only real drops are ever in it: never an item put back from the save, taken
    /// off and banked, or delivered late from a full bank. Empty again when a character is opened.
    /// </summary>
    public static string[] TakeNewDrops()
    {
        lock (s_newDrops)
        {
            string[] ids = s_newDrops.ToArray();
            s_newDrops.Clear();
            return ids;
        }
    }

    /// <summary>
    /// Sells every Magic item in the bank that is not locked, through the game's own sale (its price, its statistics).
    /// Rare and Unique items are never sold here; lock a Magic item in the bank to keep it. Returns (items, gold).
    /// </summary>
    public static (int Items, long Gold) SellMagic(Game game)
    {
        if (Bank(game) is not { } bank) return (0, 0);
        var magic = bank.Inventory.Stacks.Where(s => s.Quantity > 0 && !s.IsLocked && !s.IsPlaceholder && RecordOf(s.Item) is { Rarity: Rarity.Magic })
            .Select(s => (s.Item, (int)s.Quantity)).ToList();
        long gold = 0;
        int sold = 0;
        foreach (var (item, quantity) in magic)
        {
            var stats = bank.ItemSales.GetItemStats(item);
            try { bank.ItemSales.SellItem(item, stats, quantity); sold += quantity; gold += item.SellsFor.Quantity * quantity; }
            catch (Exception e) { Log("One Style: selling " + item.Id + " failed: " + e.Message); }
            finally { stats.Destroy(); }
        }
        DeliverPending(game);
        return (sold, gold);
    }

    /// <summary>Empties a loadout's slot (the item in it has been put somewhere else).</summary>
    internal static void ClearLoadoutSlot(EquipmentSlotLoadout slot)
    {
        SetLoadoutEquipped(slot, null);
        SetLoadoutQuantity(slot, 0);
    }

    /// <summary>
    /// Puts an item in the bank; what finds no room waits and goes in later (a rare item with the drops that wait, a
    /// normal one in NormalPending). Nothing is ever dropped on the floor.
    /// </summary>
    /// <summary>
    /// Where every One Style item is right now (and every ring in the ring slots One Style adds), as the game is about
    /// to write it: bank stacks by tab and index, worn slots, every loadout.
    /// </summary>
    static List<Place> PlacesNow(Game game)
    {
        var list = new List<Place>();
        if (Combat(game) is not { } combat || Bank(game) is not { } bank) return list;
        var tabs = bank.Inventory.InventoryTabs;
        for (int t = 0; t < tabs.Count; t++)
        {
            int i = 0;
            foreach (IReadonlyItemStack s in tabs[t].Stacks)
            {
                if (IsOurs(s.Item) && s.Quantity > 0)
                    list.Add(new Place { Kind = Place.Bank, Item = s.Item.Id, Q = s.Quantity, Tab = t, Index = i, Locked = s.IsLocked });
                i++;
            }
        }
        HashSet<string> added = Rings.AddedSlotIds;
        foreach (EquipmentSlot slot in combat.Equipment.Config.EquipmentSlots)
            if (combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty && !a.IsOccupied && a.Equipped is { } e
                && (IsOurs(e.Item) || added.Contains(slot.Id)))
                list.Add(new Place { Kind = Place.Worn, Item = e.Item.Id, Q = Math.Max(1, a.Quantity), Slot = slot.Id });
        foreach (var (key, loadout) in LoadoutsOf(combat.Equipment))
            foreach (var (slot, saved) in loadout.Slots)
                if (!saved.IsOccupied && saved.Equipped is { } e && (IsOurs(e.Item) || added.Contains(slot.Id)))
                    list.Add(new Place { Kind = Place.InLoadout, Item = e.Item.Id, Q = Math.Max(1, saved.Quantity), Loadout = key.Id, Slot = slot.Id });
        return list;
    }

    /// <summary>
    /// A game ring that the save had in one of the added ring slots, and that is not there now: the slots' own reading
    /// of the save failed, or the game saved without the mod in between (it does not know those slots and drops what
    /// is in them). The ring goes to the bank; what finds no room waits.
    /// </summary>
    static void RingsBack(Game game, CombatEntity combat)
    {
        HashSet<string> added = Rings.AddedSlotIds;
        var loadouts = LoadoutsOf(combat.Equipment);
        int back = 0;
        foreach (Place p in s_loadedPlaces)
        {
            if (!added.Contains(p.Slot) || p.Item.StartsWith("MelvorBase:Os") || !game.Data.TryGetObject(p.Item, out Item? ring)
                || !game.Data.TryGetObject(p.Slot, out EquipmentSlot? slot)) continue;
            bool there;
            if (p.Kind == Place.Worn) there = combat.Equipment.TryGetActiveSlot(slot, out IActiveEquipmentSlot? a) && !a.IsEmpty;
            else if (p.Kind == Place.InLoadout)
                there = game.Data.TryGetObject(p.Loadout, out GamesByMalcs.Melvor2.Components.Loadouts.LoadoutSlot? key) && loadouts.TryGetValue(key, out EquipmentLoadout? l)
                        && l.Slots.TryGetValue(slot, out EquipmentSlotLoadout? saved) && saved.Equipped != null;
            else continue;
            if (there) continue;
            BankOrWait(game, ring, Math.Max(1, p.Q));
            back++;
        }
        if (back > 0) Log($"One Style: {back} ring(s) of the added ring slots were not in their slots after loading: they are in the bank (or wait for room).");
    }

    /// <summary>The bank would take this item now: it has a stack of it already, or a free slot.</summary>
    internal static bool BankHasRoomFor(Game game, Item item) =>
        Bank(game) is { } bank && (bank.Inventory.GetQuantity(item) > 0 || bank.Inventory.StackCount < bank.Inventory.MaxStacks);

    internal static void BankOrWait(Game game, Item item, int quantity)
    {
        int added = Bank(game) is { } bank ? Math.Max(0, bank.Inventory.AddQuantity(item, quantity, AddItemFlags.None)) : 0;
        if (added >= quantity) return;
        if (IsOurs(item)) { if (!Data.Pending.Contains(item.LocalId)) Data.Pending.Add(item.LocalId); }
        else Data.NormalPending[item.Id] = Data.NormalPending.GetValueOrDefault(item.Id) + (quantity - added);
    }

    /// <summary>Drops that did not fit go in when there is room.</summary>
    public static void DeliverPending(Game game)
    {
        if (SafeMode || (Data.Pending.Count == 0 && Data.NormalPending.Count == 0) || Bank(game) is not { } bank) return;
        var registry = game.Data.GetDataRegistryOutput<Item>();
        foreach (string id in Data.Pending.ToList())
            if (registry.TryGetObjectById("MelvorBase:" + id, out Item? item) && bank.Inventory.AddQuantity(item, 1, AddItemFlags.None) > 0)
                Data.Pending.Remove(id);
        // normal items from "Make all rare items normal" that had no room
        foreach (var (id, n) in Data.NormalPending.ToList())
        {
            if (!game.Data.TryGetObject(id, out Item? item)) continue;
            int added = Math.Max(0, bank.Inventory.AddQuantity(item, n, AddItemFlags.None));
            if (added >= n) Data.NormalPending.Remove(id);
            else if (added > 0) Data.NormalPending[id] = n - added;
        }
    }
}

public static partial class RareItems
{
    /// <summary>For tests: an item with chosen affixes (id, tier, value), given like a drop.</summary>
    public static Item? Forge(Game game, string baseId, Rarity rarity, params (string Id, int Tier, double Value)[] affixes)
    {
        if (!game.Data.TryGetObject(baseId, out Item? baseItem)) return null;
        var record = new RolledItem
        {
            LocalId = "OsR" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(), BaseId = baseId, Rarity = rarity, ILvl = 50,
            Affixes = affixes.Select(a => new RolledAffix { Id = a.Id, Tier = a.Tier, Value = a.Value }).ToList(),
        };
        record.Name = NameFor(record, baseItem);
        return Give(game, record);
    }

    /// <summary>For tests: a random drop of a rarity at an item level.</summary>
    public static Item? Drop(Game game, Rarity rarity, int iLvl) =>
        PickBase(game, iLvl, "") is { } b ? Give(game, Roll(game, b, rarity, iLvl, "")) : null;
}
