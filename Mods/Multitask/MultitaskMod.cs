using System.Reflection;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.GameMenus;
using GamesByMalcs.Melvor2.GameMenus.Pages;
using Godot;
using MelvorModLoader;

namespace Multitask;

/// <summary>
/// F5 (or the loader's Mods menu) opens the Multitask window. With a slot switched on, the action before the latest
/// one keeps running.
/// </summary>
public class MultitaskMod : IMod
{
    public static MultitaskMod? Instance { get; private set; }

    ModContext _ctx = null!;
    SlotWindow? _window;
    Game? _game;
    int _frame;
    ulong _lastTick;

    public SlotWindow? Window => _window;
    internal WindowKey Key { get; private set; } = null!;
    /// <summary>For tests: the key shown for the window ("F5", or "" when switched off).</summary>
    public string KeyLabel => Key.Label;

    public void OnLoad(ModContext ctx)
    {
        Instance = this;
        _ctx = ctx;
        Slots.Dir = ctx.Directory;
        Slots.Log = ctx.Log;
        Progress.Log = ctx.Log;
        Key = new WindowKey(ctx, "Multitask", ToggleWindow, Godot.Key.F5);
        ctx.Frame += Tick;
        ctx.Log($"Ready. Press {Key.Spoken} while a character is open.");
    }

    void Tick()
    {
        // The game is computing offline progress on its own thread: hands off.
        if (_ctx.Tree.Paused) return;

        // Notice a character being opened or closed a couple of times per second.
        if (++_frame % 30 == 0)
        {
            Game? game = GameAccess.CurrentGame;
            _game = game is { IsSaveLoaded: true } ? game : null;
            if (_game != null) Slots.Bind(_game);
            else
            {
                Slots.Unbind();
                if (_window is { Visible: true }) _window.Hide();
            }
        }

        // Which page is on screen, a few times per second: the bottom bar follows it.
        if (_game != null && _frame % 6 == 0) Slots.Watch(_game, OpenPage());

        ulong now = Time.GetTicksMsec();
        if (now - _lastTick >= 500)
        {
            _lastTick = now;
            if (_game != null) Slots.Tick(_game, now / 1000.0);
        }

        Key.Poll();
    }

    static readonly PropertyInfo? s_currentPage = typeof(GameView).GetProperty("CurrentPage", BindingFlags.Instance | BindingFlags.NonPublic);
    GameView? _view;

    /// <summary>The skill (entity id) whose page is open, or "" for any other page.</summary>
    string OpenPage()
    {
        try
        {
            if (_view == null || !GodotObject.IsInstanceValid(_view)) _view = GameAccess.FindGameView();
            if (_view == null || s_currentPage == null) return "";
            // (GamePage Data, ActiveGamePage Active)?: boxed as the tuple itself, or null
            object? page = s_currentPage.GetValue(_view);
            return page is System.Runtime.CompilerServices.ITuple { Length: > 0 } t && t[0] is EntityPage entityPage ? entityPage.Entity.Id : "";
        }
        catch { return ""; }
    }

    public void ToggleWindow()
    {
        if (_window is { Visible: true }) { _window.Hide(); return; }
        Game? game = GameAccess.CurrentGame;
        if (game is not { IsSaveLoaded: true })
        {
            _ctx.Log("Multitask: open a character first (the window does not work on the main menu).");
            return;
        }
        _game = game;
        Slots.Bind(game);
        _window ??= new SlotWindow(_ctx.Tree);
        _window.Show(game);
    }
}
