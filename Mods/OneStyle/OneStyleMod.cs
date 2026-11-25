using GamesByMalcs.Melvor2;
using MelvorModLoader;

namespace OneStyle;

/// <summary>Play with one combat style: pick Melee, Ranged or Magic per character (Mods button in the top bar).</summary>
public class OneStyleMod : IMod
{
    public static OneStyleMod? Instance { get; private set; }

    ModContext _ctx = null!;
    ClassWindow? _window;
    int _frame;

    public ClassWindow? Window => _window;
    internal WindowKey Key { get; private set; } = null!;

    public void OnLoad(ModContext ctx)
    {
        Instance = this;
        _ctx = ctx;
        Store.Dir = ctx.Directory;
        Store.Log = ctx.Log;
        Patches.Log = ctx.Log;
        Rings.Log = ctx.Log;
        Elites.Log = ctx.Log;
        Elites.Dir = ctx.Directory;
        EliteStore.Dir = ctx.Directory;
        EliteStore.Log = ctx.Log;
        Elites.LoadInfo(ctx.Directory);
        RareItems.Log = ctx.Log;
        UniquePowers.Log = ctx.Log;
        RareItems.Dir = ctx.Directory;
        // One Style's own copy of every character's rare items: in the game's data folder, next to its saves, where
        // neither a game update nor a new copy of the mod's folder touches it
        try { Vault.Folder = System.IO.Path.Combine(Godot.OS.GetUserDataDir(), "onestyle-items"); }
        catch (System.Exception e) { ctx.Log("One Style: the folder for its own copy of the rare items could not be found: " + e.Message); }
        Vault.Log = ctx.Log;
        RareItems.MainThread = System.Environment.CurrentManagedThreadId;
        RareItems.LoadAffixes(ctx.Directory);
        RareItems.LoadDungeonGear(ctx.Directory);
        Uniques.Load(ctx.Directory);
        RareItems.HookWindow();
        // A character is opened: the game adds its screen, then works out the time it was closed on its own thread.
        // Bind right here, so the class already counts for that time, and the rare items and extra rings are back
        // before the game saves for the first time.
        ctx.Tree.NodeAdded += node =>
        {
            if (node is not GamesByMalcs.Melvor2.GameMenus.GameView) return;
            try
            {
                if (GameAccess.CurrentGame is not { IsSaveLoaded: true } game) return;
                Open(game);
                // the game gives its tooltip service out when its screen is ready: wrapped after that
                Godot.Callable.From(() => { try { RareItems.WrapTooltips(); } catch (System.Exception e) { ctx.Log("One Style: item tooltips: " + e.Message); } }).CallDeferred();
            }
            catch (System.Exception e) { ctx.Log("One Style: could not get ready early: " + e.Message); }
        };
        Key = new WindowKey(ctx, "One Style", ToggleWindow, Godot.Key.None);
        ctx.Frame += Tick;
        ctx.Log("Ready. Open a character and pick your class from the Mods button in the top bar.");
    }

    /// <summary>
    /// In this order: rare items made from One Style's part of the save (so the extra ring slots can read rings that
    /// are rare items), the extra ring slots, the rare items back in the bank and on the character, then the class.
    /// Every step does nothing when it has already run for this character.
    /// </summary>
    static Game? s_open;

    /// <summary>The character is closed (or another one came): everything bound to its game is let go.</summary>
    static void Close()
    {
        s_open = null;
        Rings.Forget();
        RareItems.Unbind();
        UniquePowers.Unbind();
        Patches.Unbind();
    }

    static readonly System.Collections.Generic.HashSet<string> s_stepFailed = new();

    /// <summary>One step of opening a character. An error in it is written down (once) and the next step still runs:
    /// the rare items must go back whatever happens to the ring slots or the class, and the other way round.</summary>
    static void Step(string name, System.Action run)
    {
        try { run(); }
        catch (System.Exception e)
        {
            if (s_stepFailed.Add(name)) RareItems.Log($"One Style: opening the character, step '{name}' failed (the other steps go on): {e}");
        }
    }

    static void Open(Game game)
    {
        // another character, opened before the mod saw the last one close: that one is let go first
        if (s_open != null && !ReferenceEquals(s_open, game)) Step("closing the last character", Close);
        s_open = game;
        SaveScan? scan = null;
        Step("rare items", () => scan = RareItems.Bind(game));
        Step("ring slots", () => Rings.Add(game));
        // before the items go back: the game refuses to put on an item whose level requirement is not met, and for the
        // other two styles that requirement is the class's to decide (the item would go to the bank at every load)
        Step("class", () => Patches.Bind(game));
        Step("rare items back", () => RareItems.Restore(game, scan));
        Step("note", () =>
        {
            if (scan != null && RareItems.SafeMode)
                RareItems.ShowNote(RareItems.Blind
                    ? "One Style could not find its rare items in this save. They are in a copy that the mod kept and come back from it "
                      + "when the mod can read this save again. Look for a newer One Style. Details: Mods > One Style."
                    : "One Style could not read its rare items from this save. Nothing was thrown away: they stay in the save and a copy "
                      + "of the save was kept. Rare items rest for this character until the mod is updated. Details: Mods > One Style.");
            else if (scan != null && RareItems.Recovered > 0)
                RareItems.ShowNote($"One Style: this save had lost its rare items (the game had saved without the mod). {RareItems.Recovered} rare item(s) "
                    + "came back from the copy One Style keeps. What did not fit where it was is in your bank.");
        });
        Step("unique powers", () => UniquePowers.Bind(game));
        // The caps on affixes and "the same ring counts once" are worked out when gear changes, and the first time was
        // above, before the powers were bound: once more now, or they would not hold until the player changed an item.
        if (scan != null) Step("caps", () => RareItems.UpdateCaps(game));
    }

    readonly System.Collections.Generic.Dictionary<string, int> _failed = new();

    /// <summary>
    /// One part of the frame's work. An error in it is written down and stays in that part: the other parts go on (the
    /// mod loader switches a mod's whole frame handler off at the first error that reaches it, and on 2026-11-25 a game
    /// update that only touched tooltips took the window, the drops and everything else with it that way). A part that
    /// fails five times in a row rests until the game is started again.
    /// </summary>
    void Part(string name, System.Action run)
    {
        _failed.TryGetValue(name, out int failed);
        if (failed >= 5) return;
        try { run(); _failed[name] = 0; }
        catch (System.Exception e)
        {
            _failed[name] = ++failed;
            if (failed == 1) _ctx.Log($"One Style: '{name}' threw an error (the rest of the mod goes on): {e}");
            if (failed == 5) _ctx.Log($"One Style: '{name}' failed five times in a row and rests until the game is started again. The game may have been updated: look for a newer One Style.");
        }
    }

    void Tick()
    {
        if (_ctx.Tree.Paused) return;   // the game is computing offline progress on its own thread: hands off
        Part("window", () => _window?.Tick(_ctx.Tree.Root.GetProcessDeltaTime()));
        Part("rare drops", RareItems.FrameTick);
        if (++_frame % 20 == 0)
        {
            Game? game = null;
            Part("game", () => game = GameAccess.CurrentGame);
            if (game is { IsSaveLoaded: true })
            {
                Part("opening the character", () => Open(game));
                Part("item tooltips", () => RareItems.WrapTooltips());
                Part("class", Patches.Poll);
                Part("elites", Elites.Poll);
                if (_frame % 200 == 0) Part("bank", () => { RareItems.DeliverPending(game); RareItems.TidyPlaceholders(game); });
            }
            else if (!_failed.TryGetValue("game", out int gameFailed) || gameFailed == 0)
            {
                Part("closing the character", Close);
                if (_window is { Visible: true }) _window.Hide();
            }
        }
        Part("hotkey", Key.Poll);
    }

    public void ToggleWindow()
    {
        if (_window is { Visible: true }) { _window.Hide(); return; }
        Game? game = GameAccess.CurrentGame;
        if (game is not { IsSaveLoaded: true })
        {
            _ctx.Log("One Style: open a character first.");
            return;
        }
        Open(game);
        _window ??= new ClassWindow(_ctx.Tree);
        _window.Show();
    }

    /// <summary>The window on its Elites page, where "How rare drops work" is (the "?" of a drop list).</summary>
    public void OpenElitesPage()
    {
        if (_window is not { Visible: true }) ToggleWindow();
        _window?.ShowPage(true);
    }
}
