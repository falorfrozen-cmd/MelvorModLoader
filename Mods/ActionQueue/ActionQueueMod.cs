using CheatMenu;
using GamesByMalcs.Melvor2;
using Godot;
using MelvorModLoader;

namespace ActionQueue;

/// <summary>
/// F6 (or the loader's Mods menu) opens the action queue. While it runs, one line at the top of the screen says which step is active.
/// </summary>
public class ActionQueueMod : IMod
{
    public static ActionQueueMod? Instance { get; private set; }

    ModContext _ctx = null!;
    QueueWindow? _window;
    Label? _chip;
    Game? _game;
    int _frame;
    ulong _lastTick;
    string _flash = "";
    ulong _flashUntil;

    public QueueWindow? Window => _window;
    internal WindowKey Key { get; private set; } = null!;
    /// <summary>For tests: the key shown for the window ("F6", or "" when switched off).</summary>
    public string KeyLabel => Key.Label;

    public void OnLoad(ModContext ctx)
    {
        Instance = this;
        _ctx = ctx;
        Queue.Dir = ctx.Directory;
        Queue.Log = ctx.Log;
        Lang.Dir = ctx.Directory;
        Lang.Load();
        // the words the pick lists write themselves (the Cheat Menu's files): their English is their id
        PickListText.T = en => Lang.T("picklist:" + en, en);
        Targets.Say = Lang.F;
        Lang.Changed += () => Callable.From(LanguageChanged).CallDeferred();
        ctx.Frame += Tick;
        GameButtons.Watch(ctx.Tree);
        // A character is opened: the game adds its screen, then computes the time it was closed. Bind right here,
        // so a queue that was running carries on through that time.
        ctx.Tree.NodeAdded += node =>
        {
            if (node is not GamesByMalcs.Melvor2.GameMenus.GameView) return;
            try
            {
                if (GameAccess.CurrentGame is { IsSaveLoaded: true } game) { Queue.Bind(game); Offline.Attach(game); }
            }
            catch (System.Exception e) { ctx.Log("Could not get the queue ready early: " + e.Message); }
        };
        GameButtons.Tell = Flash;
        Key = new WindowKey(ctx, "Action Queue", ToggleWindow, Godot.Key.F6);   // (the loader keeps the key under this title: never translated)
        Key.Changed += () => _window?.KeyChanged();
        ctx.Log($"Ready. Press {Key.Spoken} while a character is open.");
    }

    void Tick()
    {
        // The game is computing offline progress on its own thread: hands off, and do not count that time.
        if (_ctx.Tree.Paused) { _lastTick = 0; return; }

        // Notice a character being opened or closed a couple of times per second.
        if (++_frame % 30 == 0)
        {
            Game? game = GameAccess.CurrentGame;
            _game = game is { IsSaveLoaded: true } ? game : null;
            if (_game != null) { Queue.Bind(_game); Offline.Attach(_game); GameButtons.Refresh(); }
            else if (_window is { Visible: true }) _window.Hide();
            if (_frame % 60 == 0) _window?.Pulse();
        }

        ulong now = Time.GetTicksMsec();
        if (now - _lastTick >= 500)
        {
            // A long gap: frames were not raised (the game computed offline progress, or was loading).
            if (_lastTick == 0 || now - _lastTick > 3000) Queue.HoldIdle();
            double seconds = _lastTick == 0 ? 0.5 : (now - _lastTick) / 1000.0;
            _lastTick = now;
            if (_game != null) Queue.Tick(_game, System.Math.Min(seconds, 5));
            UpdateChip();
        }

        Key.Poll();
    }

    /// <summary>Another language: the window is built again in its words, on the page it was on; the game's buttons follow.</summary>
    void LanguageChanged()
    {
        Queue.ForgetNote();
        if (_window == null) { GameButtons.Retext(); return; }
        bool shown = _window.Visible;
        int page = _window.Page;
        _window.Dispose();
        _window = null;
        GameButtons.Retext();
        UpdateChip();
        if (!shown) return;
        ToggleWindow();
        _window?.ShowPage(page);
    }

    public void ToggleWindow()
    {
        if (_window is { Visible: true }) { _window.Hide(); return; }
        Game? game = GameAccess.CurrentGame;
        if (game is not { IsSaveLoaded: true })
        {
            _ctx.Log("Action Queue: open a character first (the window does not work on the main menu).");
            return;
        }
        _game = game;
        Queue.Bind(game);
        _window ??= new QueueWindow(_ctx.Tree);
        _window.Show(game);
    }

    /// <summary>Shows a line in the reminder at the top of the screen for a few seconds (also when the queue is not running).</summary>
    public void Flash(string text)
    {
        _flash = text;
        _flashUntil = Time.GetTicksMsec() + 5000;
        UpdateChip();
    }

    /// <summary>For tests: the line the reminder at the top of the screen shows ("" when hidden).</summary>
    public string ChipShown => _chip is { Visible: true } ? _chip.Text : "";

    /// <summary>A small reminder at the top of the screen while the queue is running and its window is closed.</summary>
    void UpdateChip()
    {
        bool flash = _flash != "" && Time.GetTicksMsec() < _flashUntil;
        bool show = _game != null && (flash || Queue.Running && _window is not { Visible: true });
        if (!show)
        {
            if (_chip != null) _chip.Visible = false;
            return;
        }
        if (_chip == null)
        {
            var layer = new CanvasLayer { Layer = 117, Name = "ActionQueueChip" };
            _chip = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
            _chip.AddThemeStyleboxOverride("normal", Style.Box(new Color(Style.Window, 0.9f), 6, Style.AccentDim, 1, 12, 4));
            _chip.AddThemeColorOverride("font_color", Style.Text);
            _chip.AddThemeFontSizeOverride("font_size", 13);
            _chip.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop);
            _chip.GrowHorizontal = Control.GrowDirection.Both;
            _chip.OffsetTop = 56;
            layer.AddChild(_chip);
            _ctx.Tree.Root.AddChild(layer);
        }
        // (a step with four goals, or lanes, can make a long line: it is cut, the key stays at its end)
        string line = flash ? _flash : Queue.Headline;
        if (line.Length > 180) line = line[..177] + "...";
        _chip.Text = flash || Key.Label == "" ? line : $"{line}   ({Key.Label})";
        _chip.Visible = true;
    }
}
