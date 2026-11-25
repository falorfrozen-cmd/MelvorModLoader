using System;
using System.Collections.Generic;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Data.Currencies;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities.Bank;
using GamesByMalcs.Melvor2.SearchBars;
using Godot;
using MelvorModLoader;

namespace BulkSell;

/// <summary>
/// Adds buttons to the top of the bank: "Bulk sell" opens a table of everything sellable, "Sell junk" sells the
/// character's saved junk list, "Bury all" and "Open all" appear only while there is something to bury or open.
/// Each of the one-click buttons asks once before acting. F7 opens the bulk sell window from anywhere.
/// </summary>
public class BulkSellMod : IMod
{
    public static BulkSellMod? Instance { get; private set; }

    const string ButtonsName = "BulkSellButtons";

    ModContext _ctx = null!;
    Window? _window;
    BankInventoryComponentView? _bankView;
    Button? _junkButton, _buryButton, _openButton;
    Button? _armed;          // the button that has been clicked once and waits for the confirming click
    int _frame, _armToken;
    ulong _resultUntil;      // a result text stays on its button until this moment

    public Window? Window => _window;
    internal WindowKey Key { get; private set; } = null!;
    /// <summary>For tests: the key shown for the window ("F7", or "" when switched off).</summary>
    public string KeyLabel => Key.Label;
    /// <summary>The buttons placed in the bank (null until the bank page has been opened once).</summary>
    public Control? BankButtons { get; private set; }
    public Button? JunkButton => _junkButton;
    public Button? BuryButton => _buryButton;
    public Button? OpenButton => _openButton;

    public void OnLoad(ModContext ctx)
    {
        Instance = this;
        _ctx = ctx;
        Junk.Dir = ctx.Directory;
        Junk.Log = ctx.Log;
        AutoSell.Log = ctx.Log;
        // The bank page is built when it is first opened; remember it as soon as it enters the scene.
        ctx.Tree.NodeAdded += node => { if (node is BankInventoryComponentView view) _bankView = view; };
        Key = new WindowKey(ctx, "Bulk Sell", ToggleWindow, Godot.Key.F7);
        ctx.Frame += Tick;
        ctx.Log($"Ready. Open the bank and press \"Bulk sell\", or press {Key.Spoken}.");
    }

    void Tick()
    {
        if (_ctx.Tree.Paused) return;   // the game is computing offline progress on its own thread: hands off
        _frame++;
        if (_bankView != null && _frame % 10 == 0)
        {
            if (!GodotObject.IsInstanceValid(_bankView)) { _bankView = null; BankButtons = null; _junkButton = _buryButton = _openButton = null; }
            else if (_bankView.IsNodeReady() && (BankButtons == null || !GodotObject.IsInstanceValid(BankButtons))) AddBankButtons(_bankView);
        }
        if (_frame % 60 == 0)
        {
            // auto-sell first: the window then shows the bank as it is after it
            if (GameAccess.CurrentGame is { IsSaveLoaded: true } game && Gear.CanAutoSell)
            {
                try { AutoSell.Pulse(game); }
                catch (Exception e) { _ctx.Log("Auto-sell stopped for this frame: " + e.Message); }
            }
            _window?.Pulse();
            UpdateButtons();
        }

        Key.Poll();
    }

    public void ToggleWindow()
    {
        if (_window is { Visible: true }) { _window.Hide(); return; }
        Game? game = GameAccess.CurrentGame;
        if (game is not { IsSaveLoaded: true })
        {
            _ctx.Log("Bulk Sell: open a character first (the window does not work on the main menu).");
            return;
        }
        _window ??= new Window(_ctx.Tree);
        _window.Show(game);
    }

    /// <summary>Puts the two buttons next to the bank's search box, in the game's own button style.</summary>
    void AddBankButtons(BankInventoryComponentView view)
    {
        SearchBar? search = GameAccess.FindNode<SearchBar>(view);
        if (search?.GetParent() is not BoxContainer row)
        {
            _ctx.Log($"The bank's search box was not found where expected (the game may have been updated). Use {Key.Spoken} instead.");
            _bankView = null;
            return;
        }
        var box = new HBoxContainer { Name = ButtonsName, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        box.AddThemeConstantOverride("separation", 6);
        var open = new Button { Text = "Bulk sell", TooltipText = Key.With("Pick many items and sell them in one go") };
        open.Pressed += ToggleWindow;
        _junkButton = new Button { Text = "Sell junk" };
        _junkButton.Pressed += JunkPressed;
        _buryButton = new Button { Text = "Bury all", Visible = false };
        _buryButton.Pressed += BuryPressed;
        _openButton = new Button { Text = "Open all", Visible = false };
        _openButton.Pressed += OpenPressed;
        box.AddChild(open);
        box.AddChild(_junkButton);
        box.AddChild(_buryButton);
        box.AddChild(_openButton);
        row.AddChild(box);
        row.MoveChild(box, search.GetIndex());
        BankButtons = box;
        UpdateButtons();
    }

    BankEntity? OpenBank(out Game? game)
    {
        game = GameAccess.CurrentGame;
        if (game is not { IsSaveLoaded: true }) return null;
        Junk.Bind(game.CharacterName);
        return Bank.Of(game);
    }

    /// <summary>
    /// The shared two-click rule: the first click only changes the button's text into a question,
    /// a second click on the same button within a few seconds returns true.
    /// </summary>
    bool Confirmed(Button button, string question)
    {
        if (_armed == button) { _armed = null; _armToken++; return true; }
        _armed = button;
        _resultUntil = 0;
        int token = ++_armToken;
        button.Text = question;
        _ctx.Tree.CreateTimer(4).Timeout += () => { if (_armed == button && token == _armToken) { _armed = null; UpdateButtons(); } };
        return false;
    }

    void ShowResult(Button button, string text)
    {
        _ctx.Log(text);
        button.Text = text;
        _resultUntil = Time.GetTicksMsec() + 4000;
    }

    /// <summary>Texts, counts and visibility of the bank buttons (about once a second while the bank is open).</summary>
    void UpdateButtons()
    {
        if (_armed != null || Time.GetTicksMsec() < _resultUntil) return;
        UpdateJunkButton();
        if (_buryButton == null || !GodotObject.IsInstanceValid(_buryButton) || BankButtons is not { } box || !box.IsVisibleInTree()) return;
        BankEntity? bank = OpenBank(out _);
        if (bank == null) return;
        var (buryItems, buryCount, _) = Bank.Buriable(bank);
        _buryButton.Visible = buryCount > 0;
        _buryButton.Text = $"Bury all ({buryItems.Count})";
        _buryButton.TooltipText = "Buries every bone and other buriable item in the bank. Locked items are left alone. Asks once before burying.";
        var (openItems, openCount) = Bank.Openable(bank);
        _openButton!.Visible = openCount > 0;
        _openButton.Text = $"Open all ({openItems.Count})";
        _openButton.TooltipText = "Opens every chest, bag and other openable item in the bank. Locked items are left alone. Asks once before opening.";
    }

    void BuryPressed()
    {
        BankEntity? bank = OpenBank(out _);
        if (bank == null || _buryButton == null) return;
        var (_, count, reward) = Bank.Buriable(bank);
        if (count == 0) { UpdateButtons(); return; }
        if (!Confirmed(_buryButton, $"Bury {count:N0} for {Bank.Money(reward)}?")) return;
        var (buried, got) = Bank.BuryAll(bank);
        ShowResult(_buryButton, $"Buried {buried:N0}: +{Bank.Money(got)}");
    }

    void OpenPressed()
    {
        BankEntity? bank = OpenBank(out _);
        if (bank == null || _openButton == null) return;
        var (_, count) = Bank.Openable(bank);
        if (count == 0) { UpdateButtons(); return; }
        if (!Confirmed(_openButton, $"Open {count:N0} item(s)?")) return;
        var (opened, kinds, full) = Bank.OpenAll(bank);
        ShowResult(_openButton, full ? $"Opened {opened:N0}, then the bank was full" : $"Opened {opened:N0}: {kinds} kind(s) of items");
    }

    void UpdateJunkButton()
    {
        if (_junkButton == null || !GodotObject.IsInstanceValid(_junkButton) || !_junkButton.IsVisibleInTree()) return;
        Game? game = GameAccess.CurrentGame;
        BankEntity? bank = game is { IsSaveLoaded: true } ? Bank.Of(game) : null;
        if (bank == null) return;
        Junk.Bind(game!.CharacterName);
        var (items, count, _) = Junk.InBank(bank);
        _junkButton.Text = count > 0 ? $"Sell junk ({items.Count})" : "Sell junk";
        _junkButton.Disabled = count == 0;
        _junkButton.TooltipText = Junk.Count == 0
            ? "You have no junk list yet. Open \"Bulk sell\", mark the items you never want and press \"Add marked to junk\"."
            : count == 0 ? $"Nothing from your junk list ({Junk.Count} item(s)) is in the bank."
            : "Sells everything from your junk list that is in the bank. Asks once before selling.";
    }

    void JunkPressed()
    {
        BankEntity? bank = OpenBank(out _);
        if (bank == null || _junkButton == null) return;
        var (items, count, worth) = Junk.InBank(bank);
        if (count == 0) { UpdateButtons(); return; }
        if (!Confirmed(_junkButton, $"Sell {count:N0} for {Bank.Money(worth)}?")) return;
        var (sold, paid) = Bank.Sell(bank, items, keep: 0);
        ShowResult(_junkButton, $"Sold {sold:N0}: +{Bank.Money(paid)}");
    }

    /// <summary>For tests: what the junk button would do, without the two clicks.</summary>
    public (long Sold, Dictionary<Currency, long> Paid) SellJunkNow(Game game)
    {
        BankEntity bank = Bank.Of(game)!;
        Junk.Bind(game.CharacterName);
        var (items, _, _) = Junk.InBank(bank);
        return Bank.Sell(bank, items, keep: 0);
    }
}
