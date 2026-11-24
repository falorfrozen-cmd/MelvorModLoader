using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using MelvorModLoader;

namespace Facelift;

/// <summary>The player's switches, kept in facelift.json next to the mod.</summary>
public sealed class Settings
{
    /// <summary>The new look: panels, buttons, colours, titles. Off = the game's own look.</summary>
    public bool Look { get; set; } = true;
    /// <summary>The redrawn frame: flat top bar with the open page underlined, the skill list with a gold stripe on the open skill.</summary>
    public bool NewFrame { get; set; } = true;
    /// <summary>No version text and no greyed "Cozy UI Mode - Coming soon" switch in the top bar.</summary>
    public bool TidyTopBar { get; set; } = true;
    /// <summary>The shop opens on its first category instead of an empty page.</summary>
    public bool ShopOpensCategory { get; set; } = true;
    /// <summary>The bank's empty right panel shows a short summary.</summary>
    public bool BankSummary { get; set; } = true;
    /// <summary>At most four notifications per side and one banner in the middle at a time.</summary>
    public bool FewerNotices { get; set; } = true;
    /// <summary>"None" rows on enemy cards are left out.</summary>
    public bool HideEmptyRows { get; set; } = true;
}

/// <summary>
/// Facelift: a new look for the whole game. Everything it changes is written down and put back when it is switched
/// off. It steps aside on its own when the game's own "Cozy UI Mode" is switched on.
/// </summary>
public class FaceliftMod : IMod
{
    public static FaceliftMod? Instance { get; private set; }

    ModContext _ctx = null!;
    Restyler _look = null!;
    Layout _layout = null!;
    Overrides _overrides = new();
    Frame _frameUi = null!;
    SettingsWindow _window = null!;
    WindowKey _key = null!;

    /// <summary>Opens or closes the switches window (for the test harness; players use the Mods menu).</summary>
    public void ToggleWindow() => _window.Toggle(_ctx.Tree);
    public bool WindowVisible => _window.Visible;
    string _settingsPath = "";
    int _frame;
    bool _cozy;
    bool _frameTold;

    public Settings Settings { get; private set; } = new();
    internal Restyler Look => _look;
    public bool LookOn => _look.On;
    public int Changed => _look.Changed;
    public int Waiting => _look.Waiting;
    public string[] Unknown => _look.Unknown.ToArray();
    public string[] Gaps => _look.Gaps.ToArray();
    /// <summary>True while the game's own Cozy UI Mode is on: Facelift keeps out of its way.</summary>
    public bool CozyOn => _cozy;

    public void OnLoad(ModContext ctx)
    {
        Instance = this;
        _ctx = ctx;
        _look = new Restyler(ctx.Log);
        _settingsPath = Path.Combine(ctx.Directory, "facelift.json");
        Settings = Read();
        _layout = new Layout(ctx.Log, Settings);
        _frameUi = new Frame(_overrides);
        ctx.Tree.NodeAdded += node => _look.Added(node);
        ctx.Frame += Tick;
        _window = new SettingsWindow(this);
        _key = new WindowKey(ctx, "Facelift", () => _window.Toggle(ctx.Tree), Key.None);
        if (Settings.Look) _look.Enable(ctx.Tree);
        ctx.Log($"Ready. New look {(Settings.Look ? "on" : "off")}.");
    }

    /// <summary>Switches the new look on or off and keeps the choice.</summary>
    public void SetLook(bool on)
    {
        Settings.Look = on;
        Save();
        Apply();
    }

    /// <summary>Changes one of the switches (by its name in <see cref="Settings"/>) and keeps the choice.</summary>
    public void Set(string name, bool on)
    {
        typeof(Settings).GetProperty(name)?.SetValue(Settings, on);
        Save();
        _layout.Clear();   // what is still wanted comes back on the next tick
        _overrides.Clear();
        Apply();
    }

    void Apply()
    {
        bool want = Settings.Look && !_cozy;
        if (want && !_look.On) _look.Enable(_ctx.Tree);
        else if (!want && _look.On) _look.Disable();
        if (_cozy) { _layout.Clear(); _overrides.Clear(); }
    }

    void Tick()
    {
        if (_ctx.Tree.Paused) return;   // the game computes offline progress: hands off
        _key.Poll();
        _look.Work(4000);
        if (++_frame % 60 == 0) _look.Retry();
        if (_frame % 15 == 0 && !_cozy)
        {
            _layout.Tick(_ctx.Tree);
            if (Settings.NewFrame && _ctx.Tree.Root.GetNodeOrNull("Main/ApplicationViewContainer/GameView") is { } view)
            {
                try { _frameUi.Tick(view); } catch (Exception e) { if (!_frameTold) { _frameTold = true; _ctx.Log("Frame: " + e.Message); } }
            }
        }
        if (_frame % 120 == 0)
        {
            bool cozy = CozyModeOn();
            if (cozy != _cozy)
            {
                _cozy = cozy;
                _ctx.Log(cozy ? "The game's Cozy UI Mode is on: Facelift steps aside." : "Cozy UI Mode is off again.");
                Apply();
            }
        }
    }

    /// <summary>
    /// The game shows a greyed "Cozy UI Mode - Coming soon" switch. Once it works and is switched on, its button is
    /// enabled and pressed.
    /// </summary>
    bool CozyModeOn()
    {
        try
        {
            foreach (Node n in _ctx.Tree.Root.FindChildren("*", "BaseButton", true, false))
            {
                if (n is not BaseButton b || !b.IsInsideTree()) continue;   // (Facelift itself may have hidden the greyed switch)
                string text = b is Button bt ? bt.Text : "";
                if (text.Length == 0) text = string.Join(" ", b.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text));
                if (text.Contains("Cozy", StringComparison.OrdinalIgnoreCase))
                    return !b.Disabled && b.ToggleMode && b.ButtonPressed;
            }
        }
        catch { }
        return false;
    }

    Settings Read()
    {
        try { if (File.Exists(_settingsPath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath)) ?? new(); }
        catch (Exception e) { _ctx.Log("Settings could not be read, using defaults: " + e.Message); }
        return new();
    }

    void Save()
    {
        try { File.WriteAllText(_settingsPath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception e) { _ctx.Log("Settings could not be saved: " + e.Message); }
    }
}
