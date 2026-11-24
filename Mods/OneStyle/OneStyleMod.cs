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
        RareItems.MainThread = System.Environment.CurrentManagedThreadId;
        RareItems.LoadAffixes(ctx.Directory);
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
                Godot.Callable.From(RareItems.WrapTooltips).CallDeferred();
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
    static void Open(Game game)
    {
        SaveScan? scan = RareItems.Bind(game);
        Rings.Add(game);
        RareItems.Restore(game, scan);
        Patches.Bind(game);
        UniquePowers.Bind(game);
    }

    void Tick()
    {
        if (_ctx.Tree.Paused) return;   // the game is computing offline progress on its own thread: hands off
        _window?.Tick(_ctx.Tree.Root.GetProcessDeltaTime());
        RareItems.FrameTick();
        if (++_frame % 20 == 0)
        {
            Game? game = GameAccess.CurrentGame;
            if (game is { IsSaveLoaded: true })
            {
                Open(game);
                RareItems.WrapTooltips();
                Patches.Poll();
                Elites.Poll();
                if (_frame % 200 == 0) { RareItems.DeliverPending(game); RareItems.TidyPlaceholders(game); }
            }
            else
            {
                Rings.Forget();
                RareItems.Unbind();
                UniquePowers.Unbind();
                Patches.Unbind();
                if (_window is { Visible: true }) _window.Hide();
            }
        }
        Key.Poll();
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
