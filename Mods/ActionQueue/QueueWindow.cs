using System;
using System.Collections.Generic;
using System.Linq;
using CheatMenu;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using Godot;

namespace ActionQueue;

/// <summary>The F6 window: add steps on top (one at a time, or a whole plan for an item), watch the queue run below.</summary>
public sealed class QueueWindow
{
    static readonly string[] UntilNames =
    {
        "it stops by itself", "I own this many of an item", "a skill reaches this level", "this many minutes have passed",
        "only this many of an item are left", "I make this many more of an item", "it has been done this many times",
        "it has used up this many of an item", "node unlocks of this constellation are earned",
        "an astrology event has been seen this many times", "a mastery task is complete",
    };

    /// <summary>One theme for the window and for the popups of its pick lists (a popup is a window of its own).</summary>
    static readonly Theme s_theme = Style.Build();
    readonly CanvasLayer _layer = new() { Layer = 118, Name = "ActionQueue", Visible = false };
    readonly Label _status = Style.Label("", Style.Good);
    readonly Label _now = Style.Label("", Style.Muted, 12);
    readonly OptionButton _skillPick = new() { CustomMinimumSize = new Vector2(170, 0) };
    readonly PickList<Target> _target = new("Choose what to do", s_theme);
    readonly OptionButton _untilPick = new() { CustomMinimumSize = new Vector2(250, 0) };
    readonly SpinBox _amount = new() { MinValue = 1, MaxValue = 100_000_000, Step = 1, Value = 1000, CustomMinimumSize = new Vector2(120, 0), SelectAllOnFocus = true };
    readonly PickList<Item> _item = new("Choose an item", s_theme);
    readonly OptionButton _levelPick = new() { CustomMinimumSize = new Vector2(170, 0) };
    // Astrology goals: minor or major node unlocks, and the event to wait for.
    readonly OptionButton _nodePick = new() { CustomMinimumSize = new Vector2(110, 0) };
    readonly OptionButton _eventPick = new() { CustomMinimumSize = new Vector2(250, 0), FitToLongestItem = false, ClipText = true };
    // The second goal: "... or until ...". The step is over when either one is reached.
    readonly CheckBox _orBox = new() { Text = "or" };
    readonly HBoxContainer _orRow = new() { Visible = false };
    readonly OptionButton _orPick = new() { CustomMinimumSize = new Vector2(250, 0) };
    readonly SpinBox _orAmount = new() { MinValue = 1, MaxValue = 100_000_000, Step = 1, Value = 100, CustomMinimumSize = new Vector2(120, 0), SelectAllOnFocus = true };
    readonly PickList<Item> _orItem = new("Choose an item", s_theme);
    readonly OptionButton _orLevel = new() { CustomMinimumSize = new Vector2(170, 0) };
    readonly OptionButton _orNode = new() { CustomMinimumSize = new Vector2(110, 0) };
    readonly OptionButton _orEvent = new() { CustomMinimumSize = new Vector2(250, 0), FitToLongestItem = false, ClipText = true };
    /// <summary>"Until this mastery task is complete" (0.12.0): the chosen skill's Mastery Guild tasks, a fight's: the combat skills'.</summary>
    readonly PickList<string> _taskPick = new("Choose a mastery task", s_theme);
    readonly PickList<string> _orTask = new("Choose a mastery task", s_theme);
    /// <summary>The skill and target the task lists were last made for: a new one picks its own first task.</summary>
    string _tasksFor = "";
    /// <summary>The window was opened again: the progress next to each task is made again (once).</summary>
    bool _tasksStale = true;
    /// <summary>One line in the goal row for the steps that are not actions (equip a loadout, go to a saved queue).</summary>
    readonly Label _pseudoNote = Style.Label("", Style.Muted, 12);
    readonly Tree _table;
    readonly Button _run = new() { CustomMinimumSize = new Vector2(150, 0) };
    readonly Button _repeat = new() { ToggleMode = true, CustomMinimumSize = new Vector2(205, 0) };
    /// <summary>With steps in lanes and "start over" on: which lanes start over (a lane can also run once).</summary>
    readonly HBoxContainer _laneRepeats = new() { Visible = false };
    string _laneRepeatsShown = "";
    readonly Button _side = new() { ToggleMode = true, CustomMinimumSize = new Vector2(175, 0), Visible = false };
    // Lanes: which lane a new step (or plan) goes to, and moving the selected step to another lane.
    readonly OptionButton _lanePick = new() { Visible = false }, _planLanePick = new() { Visible = false };
    readonly Button _laneButton = new() { Visible = false, CustomMinimumSize = new Vector2(120, 0) };
    readonly CheckBox _planSell = new() { Text = "then sell them" };
    /// <summary>The planner: every material from nothing (the bank not counted), for a queue that starts over.</summary>
    readonly CheckBox _planAll = new() { Text = "every material" };
    bool _planAllSet;
    /// <summary>A recipe's other way of making for this step (Smithing's Sacrificial Anvil); shown only where the game offers one.</summary>
    readonly CheckBox _mode = new() { Visible = false };
    string _modeId = "";
    readonly Label _untilLabel = Style.Label("until", Style.Text);
    Button _useNow = null!;
    // Editing a step in place: the form shows the step, and its button saves it back where it was.
    Button _addButton = null!;
    readonly Button _cancelEdit = new() { Text = "Cancel", Visible = false };
    Step? _editing;

    // "When the queue waits or ends, do": an action for the idle time.
    readonly OptionButton _idleSkill = new() { CustomMinimumSize = new Vector2(170, 0) };
    readonly PickList<Target> _idleTarget = new("Choose what to do", s_theme);
    readonly Label _idleState = Style.Label("", Style.Muted, 12);
    readonly CheckBox _idleAlways = new() { Text = "Also when the queue is not running" };
    readonly CheckBox _gameCreate = new() { Text = "The game's Create button adds to the queue" };

    // "Stop the whole queue when ...": the queue's own stop (0.12.0).
    readonly OptionButton _stopPick = new() { CustomMinimumSize = new Vector2(250, 0) };
    readonly SpinBox _stopAmount = new() { MinValue = 1, MaxValue = Queue.MaxStopAmount, Step = 1, Value = 1, CustomMinimumSize = new Vector2(110, 0), SelectAllOnFocus = true };
    readonly PickList<Item> _stopItem = new("Choose an item", s_theme);
    readonly OptionButton _stopLevel = new() { CustomMinimumSize = new Vector2(170, 0) };
    /// <summary>The queue's stop "a mastery task is complete": every skill's tasks (filled when the kind is shown).</summary>
    readonly PickList<string> _stopTask = new("Choose a mastery task", s_theme);
    bool _stopTasksFilled;
    /// <summary>The queue's stop the controls were last set from (they are not reset while the player is still picking).</summary>
    string _stopShown = "?";
    /// <summary>The id of "never" in the stop list (Godot gives an item added with -1 its index as id).</summary>
    const int NeverId = 99;

    // The card on top has two faces: the form for one step, and the planner.
    readonly Button _stepMode = new() { Text = "Add one step", ToggleMode = true };
    readonly Button _planMode = new() { Text = "Plan an item", ToggleMode = true };
    readonly VBoxContainer _stepForm = new(), _planForm = new() { Visible = false };
    readonly OptionButton _planKind = new() { CustomMinimumSize = new Vector2(132, 0) };
    readonly SpinBox _planAmount = new() { MinValue = 1, MaxValue = 100_000_000, Step = 1, Value = 100, CustomMinimumSize = new Vector2(120, 0), SelectAllOnFocus = true };
    readonly PickList<Item> _planPick = new("Choose an item to plan", s_theme);
    readonly Label _planNote = Style.Label("", Style.Muted, 12);
    readonly Button _planAdd = new() { Text = "Add these steps to the queue" };
    Tree _planTable = null!;
    List<Item> _makeable = new();
    PlanResult? _plan;
    string _planShown = "";

    // The third face of the card: queues kept under a name, and share codes.
    readonly Button _savedMode = new() { Text = "Saved queues", ToggleMode = true };
    readonly VBoxContainer _savedForm = new() { Visible = false };
    readonly LineEdit _presetName = new() { PlaceholderText = "Name, e.g. Smithing day", CustomMinimumSize = new Vector2(260, 0), MaxLength = 40 };
    readonly LineEdit _code = new() { PlaceholderText = "Share code (AQ1:...)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
    readonly Button _deletePreset = new() { Text = "Delete" };
    readonly OptionButton _quick = new() { CustomMinimumSize = new Vector2(230, 0), FitToLongestItem = false, ClipText = true };
    Tree _presetTable = null!;
    List<string> _quickNames = new();
    string _armedDelete = "";
    int _armToken;
    SceneTree _tree = null!;

    Game? _game;
    List<(GameEntity Entity, ActionComponent Action)> _skills = new();
    List<Item> _items = new();
    List<string> _levelSkills = new();
    bool _syncing;
    Until _lastKind;
    double _amountLeft = 5, _amountOther = 1000;   // "only N left" keeps its own number, apart from the other goals

    public bool Visible => _layer.Visible;
    public Control Panel { get; }

    public QueueWindow(SceneTree tree)
    {
        _tree = tree;
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.25f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(shade);
        // The window sits where the player dragged it (centred at first): a plain holder, not a centring container.
        _holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _holder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_holder);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(1000, 700), Theme = s_theme };
        StyleBoxFlat frame = Style.Box(Style.Window, 8, Style.AccentDim, 1);
        frame.ShadowColor = new Color(0, 0, 0, 0.55f);
        frame.ShadowSize = 14;
        panel.AddThemeStyleboxOverride("panel", frame);
        _holder.AddChild(panel);
        Panel = panel;
        panel.Resized += Place;
        panel.MinimumSizeChanged += Place;
        _holder.Resized += Place;
        _offset = ReadPlace();

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        panel.AddChild(root);

        // ---- header
        StyleBoxFlat headBox = Style.Box(Style.Sidebar, 0, null, 0, 18, 12);
        headBox.CornerRadiusTopLeft = headBox.CornerRadiusTopRight = 7;
        headBox.BorderColor = Style.Line;
        headBox.BorderWidthBottom = 1;
        var head = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Move,
            TooltipText = "Drag here to move the window. Double-click to put it back in the middle." };
        head.AddThemeStyleboxOverride("panel", headBox);
        head.GuiInput += DragTitle;
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        header.AddChild(Style.Label("ACTION QUEUE", Style.Accent, 20));
        Label hint = Style.Label("Do this, then that, then that.", Style.Muted);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.SizeFlagsVertical = Control.SizeFlags.Fill;
        hint.ClipText = true;
        header.AddChild(hint);
        StyleBoxFlat go = Style.Box(Style.GoodDim, 5, Style.Good, 1, 12, 5);
        _run.AddThemeStyleboxOverride("normal", go);
        _run.AddThemeStyleboxOverride("hover", Style.Box(new Color(0.16f, 0.38f, 0.23f), 5, Style.Good, 1, 12, 5));
        _run.Pressed += () => { if (Queue.Running) Queue.Pause("Paused. Your character goes on with what it was doing; press Start to carry on with the queue."); else Queue.Start(); };
        _quick.TooltipText = "Switch to one of your saved queues. A running queue keeps running, from the first step of the new one.";
        _quick.ItemSelected += QuickPicked;
        header.AddChild(_quick);
        header.AddChild(_run);
        _close = MakeButton(CloseText, Hide);
        header.AddChild(_close);
        head.AddChild(header);
        root.AddChild(head);

        // ---- body
        var margin = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        foreach (string m in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(m, 16);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        margin.AddChild(body);
        root.AddChild(margin);

        // -- new step
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Style.Box(Style.Card, 5, null, 0, 12, 10));
        var form = new VBoxContainer();
        form.AddThemeConstantOverride("separation", 8);
        card.AddChild(form);
        body.AddChild(card);

        var modes = new HBoxContainer();
        modes.AddThemeConstantOverride("separation", 6);
        var group = new ButtonGroup();
        foreach (Button mode in new[] { _stepMode, _planMode, _savedMode })
        {
            mode.ButtonGroup = group;
            StyleBoxFlat picked = Style.Box(Style.AccentDim, 5, Style.Accent, 1, 12, 5);
            mode.AddThemeStyleboxOverride("pressed", picked);
            mode.AddThemeStyleboxOverride("hover_pressed", picked);
            modes.AddChild(mode);
        }
        _stepMode.ButtonPressed = true;
        _stepMode.TooltipText = "Choose one action and when it is over.";
        _planMode.TooltipText = "Say which item you want and how many. The steps to get there are worked out for you.";
        _savedMode.TooltipText = "Keep several queues under a name, switch between them, and share one as a short code.";
        foreach (Button mode in new[] { _stepMode, _planMode, _savedMode }) mode.Toggled += _ => ShowFace();
        Label modeHint = Style.Label("", Style.Muted, 12);
        modeHint.Text = "Build the queue step by step, let a plan work the steps out, or keep whole queues under a name.";
        modeHint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        modeHint.HorizontalAlignment = HorizontalAlignment.Right;
        modeHint.ClipText = true;
        modes.AddChild(modeHint);
        form.AddChild(modes);
        _stepForm.AddThemeConstantOverride("separation", 8);

        var what = new HBoxContainer();
        what.AddThemeConstantOverride("separation", 10);
        what.AddChild(Fixed("Do", 50));
        _skillPick.ItemSelected += _ => { FillKinds(_untilPick, Astrology, withStops: true); FillKinds(_orPick, Astrology, withStops: false); FillTargets(null); UntilChanged(); };
        _target.Picked += _ =>
        {
            if (Pseudo) { UntilChanged(); return; }
            if (Kind == Until.ItemLeft) SuggestFood(); else SuggestItem();
            ShowMode();
            FillEvents();
            FillTasks();
            CapNodes(_amount, _nodePick, Kind);
            CapNodes(_orAmount, _orNode, OrKind);
        };
        _target.Control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _target.Control.CustomMinimumSize = new Vector2(300, 0);
        _target.Control.TooltipText = "Click and type to search. The groups are the ones of the skill's own page.";
        what.AddChild(_skillPick);
        what.AddChild(_target.Control);
        _mode.TooltipText = "Tick it and this step makes the recipe this other way (as the switch under the recipe on the skill's page);\n"
            + "untick it and the step makes it the normal way. The queue sets the game's switch when the step starts.";
        what.AddChild(_mode);
        Button useNow = _useNow = MakeButton("Use what I am doing now", UseCurrent);
        useNow.TooltipText = "Fills in the skill and the action your character is performing right now.";
        what.AddChild(useNow);
        _stepForm.AddChild(what);

        var until = new HBoxContainer();
        until.AddThemeConstantOverride("separation", 10);
        _untilLabel.CustomMinimumSize = new Vector2(50, 0);
        until.AddChild(_untilLabel);
        FillKinds(_untilPick, false, withStops: true);
        _untilPick.ItemSelected += _ => UntilChanged();
        until.AddChild(_untilPick);
        until.AddChild(_amount);
        _item.Control.CustomMinimumSize = new Vector2(280, 0);
        until.AddChild(_item.Control);
        until.AddChild(_levelPick);
        foreach (OptionButton nodes in new[] { _nodePick, _orNode })
        {
            nodes.AddItem("minor");
            nodes.AddItem("major");
            nodes.ItemSelected += _ => { CapNodes(_amount, _nodePick, Kind); CapNodes(_orAmount, _orNode, OrKind); };
            nodes.TooltipText = "Minor: the node unlocks the constellation's Knowledge has earned (the ones you already clicked count too).\n"
                + "Major: one for every few minor ones, as the game gives them; you do not have to click the minor nodes for it.";
        }
        until.AddChild(_nodePick);
        _eventPick.TooltipText = _orEvent.TooltipText = "The events this constellation can show while it is studied. The step counts the ones seen after it starts.";
        until.AddChild(_eventPick);
        foreach (PickList<string> tasks in new[] { _taskPick, _orTask })
        {
            tasks.Control.CustomMinimumSize = new Vector2(400, 0);
            tasks.HintWidth = 150;   // the count ("Easy: 201 / 2,000") stays readable when a long task's words are cut
            tasks.Control.TooltipText = "The Mastery Guild's tasks of this skill (a fight: of the combat skills). Click and type to search.\n"
                + "The ones for the chosen target come first. The step is over when the game marks the task complete.";
        }
        until.AddChild(_taskPick.Control);
        foreach (PickList<string> tasks in new[] { _taskPick, _orTask })
            tasks.Picked += e =>
            {
                if (Mastery.Find(e.Key) is { Done: true })
                    Say("That task is already complete on this character, so a step waiting for it ends at once.");
            };
        until.AddChild(_pseudoNote);
        _orBox.TooltipText = "Give the step a second goal: it is over when either one is reached\n(e.g. until a Gold Topaz Ring is in the bank, or until 780 Salmon are used up).";
        _orBox.Toggled += _ => UntilChanged();
        until.AddChild(_orBox);
        until.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        OfferLanes(LanesOffered());
        _lanePick.TooltipText = _planLanePick.TooltipText =
            "Lanes are queues of their own that run side by side (with the Multitask mod's slots on).\n"
            + "Every lane does its steps one after another; e.g. lane 1 cuts logs and burns them, lane 2 fishes and cooks.\n"
            + "With every step in lane 1 the queue works as one list.";
        _lanePick.ItemSelected += i => _planLanePick.Select((int)i);
        _planLanePick.ItemSelected += i => { _lanePick.Select((int)i); _planShown = ""; Replan(); };
        until.AddChild(_lanePick);
        _addButton = MakeButton("Add step", AddStep);
        until.AddChild(_addButton);
        _cancelEdit.TooltipText = "Stop editing: the step stays as it was.";
        _cancelEdit.Pressed += () => { EndEdit(); Say("Nothing was changed."); };
        until.AddChild(_cancelEdit);
        _stepForm.AddChild(until);
        // -- the second goal, under the first
        _orRow.AddThemeConstantOverride("separation", 10);
        Label orLabel = Style.Label("or until", Style.Text);
        orLabel.CustomMinimumSize = new Vector2(50, 0);
        _orRow.AddChild(orLabel);
        FillKinds(_orPick, false, withStops: false);
        _orPick.ItemSelected += _ => OrChanged();
        _orRow.AddChild(_orPick);
        _orRow.AddChild(_orAmount);
        _orItem.Control.CustomMinimumSize = new Vector2(280, 0);
        _orRow.AddChild(_orItem.Control);
        _orRow.AddChild(_orLevel);
        _orRow.AddChild(_orNode);
        _orRow.AddChild(_orEvent);
        _orRow.AddChild(_orTask.Control);
        _stepForm.AddChild(_orRow);
        _stepForm.AddChild(_now);
        form.AddChild(_stepForm);
        BuildPlanForm(form);
        BuildSavedForm(form);

        // -- the queue
        _table = new Tree
        {
            Columns = 4, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, ColumnTitlesVisible = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 150)
        };
        string[] titles = { "#", "Do", "Until", "State" };
        for (int c = 0; c < titles.Length; c++)
        {
            _table.SetColumnTitle(c, titles[c]);
            _table.SetColumnTitleAlignment(c, HorizontalAlignment.Left);
            _table.SetColumnExpand(c, c is 1 or 3);
        }
        _table.SetColumnCustomMinimumWidth(0, 44);
        _table.SetColumnCustomMinimumWidth(2, 290);
        _table.ItemSelected += PaintLaneButton;
        body.AddChild(_table);

        // -- the idle action
        var idleRow = new HBoxContainer();
        idleRow.AddThemeConstantOverride("separation", 10);
        Label idleLabel = Style.Label("When the queue waits or ends, do", Style.Muted);
        idleLabel.MouseFilter = Control.MouseFilterEnum.Stop;
        idleLabel.TooltipText = "Your character never stands still: this runs while every step that is left is waiting\n"
            + "(for materials, for its skill to be free...) and goes on after the queue has finished.\n"
            + "It only takes a free slot, never one of your own actions, and stops when a step needs the place.";
        idleRow.AddChild(idleLabel);
        _idleSkill.TooltipText = idleLabel.TooltipText;
        _idleSkill.ItemSelected += i => { if (!_syncing) { if (i <= 0) Queue.SetIdle(null); else { FillIdleTargets(null); PickIdle(); } } };
        _idleTarget.Picked += _ => PickIdle();
        _idleTarget.Control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _idleTarget.Control.CustomMinimumSize = new Vector2(220, 0);
        idleRow.AddChild(_idleSkill);
        idleRow.AddChild(_idleTarget.Control);
        Button idleNow = MakeButton("Use what I am doing now", UseCurrentForIdle);
        idleNow.TooltipText = "Uses the skill and the action your character is performing right now.";
        idleRow.AddChild(idleNow);
        body.AddChild(idleRow);
        var optionRow = new HBoxContainer();
        optionRow.AddThemeConstantOverride("separation", 24);
        _idleAlways.TooltipText = "ON: the idle action above also runs while the queue is paused, finished or not used at all,\n"
            + "whenever your character does nothing. Press Stop on it in the game and it waits until you start something yourself.\n"
            + "OFF: it only runs while the queue runs and after the queue has finished.";
        _idleAlways.Toggled += on => { if (!_syncing) Queue.SetIdleAlways(on); };
        optionRow.AddChild(_idleAlways);
        _gameCreate.TooltipText = "ON: on the Smithing, Crafting, Fletching, Runecrafting, Beastcrafting, Cooking and Herblore pages,\n"
            + "the game's Create button (it then reads \"Add to queue\") puts the recipe at the end of the queue, with the\n"
            + "stop option you chose there. With nothing left in the queue, it starts the recipe through the queue.\n"
            + "The \"+ Queue\" button next to it always adds to the queue, even when you lack the materials yet.";
        _gameCreate.Toggled += on => { if (!_syncing) Queue.SetGameCreate(on); };
        optionRow.AddChild(_gameCreate);
        body.AddChild(optionRow);
        _idleState.Visible = false;
        body.AddChild(_idleState);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(Style.Label("Selected step:", Style.Muted));
        buttons.AddChild(MakeButton("Move up", () => MoveSelected(-1)));
        buttons.AddChild(MakeButton("Move down", () => MoveSelected(1)));
        Button edit = MakeButton("Edit", EditSelected);
        edit.TooltipText = "Shows the selected step in the form above. Change what you like and press \"Save step\": it stays in its place.";
        buttons.AddChild(edit);
        buttons.AddChild(MakeButton("Remove", () => { int i = SelectedIndex(); if (i >= 0) Queue.RemoveAt(i); }));
        _laneButton.TooltipText = "Puts the selected step into the next lane (after the last lane it goes back to lane 1).";
        _laneButton.Pressed += NextLane;
        buttons.AddChild(_laneButton);
        buttons.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        StyleBoxFlat on = Style.Box(Style.GoodDim, 5, Style.Good, 1, 12, 5);
        _repeat.AddThemeStyleboxOverride("pressed", on);
        _repeat.AddThemeStyleboxOverride("hover_pressed", on);
        _repeat.Toggled += pressed => { if (!_syncing) Queue.SetRepeat(pressed); };
        StyleBoxFlat sideOn = Style.Box(Style.GoodDim, 5, Style.Good, 1, 12, 5);
        _side.AddThemeStyleboxOverride("pressed", sideOn);
        _side.AddThemeStyleboxOverride("hover_pressed", sideOn);
        _side.TooltipText = "With the Multitask mod's slots switched on, several steps run at the same time (the fish are cooked while more are caught).\nOFF: one step after another, as without Multitask.";
        _side.Toggled += pressed => { if (!_syncing) Queue.SetSideBySide(pressed); };
        buttons.AddChild(_side);
        buttons.AddChild(_repeat);
        buttons.AddChild(MakeButton("Remove all", Queue.Clear));
        body.AddChild(buttons);
        _laneRepeats.AddThemeConstantOverride("separation", 12);
        _laneRepeats.Alignment = BoxContainer.AlignmentMode.End;
        body.AddChild(_laneRepeats);

        // -- the queue's own stop
        var stopRow = new HBoxContainer();
        stopRow.AddThemeConstantOverride("separation", 10);
        Label stopLabel = Style.Label("Stop the whole queue when", Style.Muted);
        stopLabel.MouseFilter = Control.MouseFilterEnum.Stop;
        stopLabel.TooltipText = "Once this is reached, every step stops and the queue ends, also with \"Start over at the end\" on\n"
            + "and in lanes. The action under \"When the queue waits or ends, do\" then takes over.\n"
            + "E.g. thieve, fish and cook round after round until 1 Bandit Gloves is in the bank, then stop.";
        stopRow.AddChild(stopLabel);
        _stopPick.TooltipText = stopLabel.TooltipText;
        _stopPick.AddItem("never: the steps decide", NeverId);
        _stopPick.AddItem("I own this many of an item", (int)Until.Item);
        _stopPick.AddItem("a skill reaches this level", (int)Until.Level);
        _stopPick.AddItem("only this many of an item are left", (int)Until.ItemLeft);
        _stopPick.AddItem("a mastery task is complete", (int)Until.Mastery);
        _stopPick.ItemSelected += _ => { if (_syncing) return; StopKindShown(fresh: true); ApplyStop(fresh: true); };
        stopRow.AddChild(_stopPick);
        _stopAmount.ValueChanged += _ => { if (!_syncing) ApplyStop(); };
        stopRow.AddChild(_stopAmount);
        _stopItem.Control.CustomMinimumSize = new Vector2(240, 0);
        _stopItem.Control.TooltipText = "Click and type to search.";
        _stopItem.Picked += _ => { if (!_syncing) ApplyStop(); };
        stopRow.AddChild(_stopItem.Control);
        _stopLevel.ItemSelected += _ => { if (!_syncing) ApplyStop(); };
        stopRow.AddChild(_stopLevel);
        _stopTask.Control.CustomMinimumSize = new Vector2(480, 0);
        _stopTask.HintWidth = 120;
        _stopTask.Control.TooltipText = "Every skill's Mastery Guild tasks (the skills of this queue first). Click and type to search.";
        _stopTask.Picked += _ => { if (!_syncing) ApplyStop(); };
        stopRow.AddChild(_stopTask.Control);
        body.AddChild(stopRow);
        StopKindShown(fresh: false);

        // ---- footer
        StyleBoxFlat footBox = Style.Box(Style.Sidebar, 0, null, 0, 18, 8);
        footBox.CornerRadiusBottomLeft = footBox.CornerRadiusBottomRight = 7;
        footBox.BorderColor = Style.Line;
        footBox.BorderWidthTop = 1;
        var foot = new PanelContainer();
        foot.AddThemeStyleboxOverride("panel", footBox);
        _status.ClipText = true;
        _status.CustomMinimumSize = new Vector2(0, 20);
        foot.AddChild(_status);
        root.AddChild(foot);

        Queue.Changed += Repaint;
        tree.Root.AddChild(_layer);
    }

    void BuildPlanForm(VBoxContainer form)
    {
        _planForm.AddThemeConstantOverride("separation", 8);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        _planKind.AddItem("Make");
        _planKind.AddItem("Own in total");
        _planKind.TooltipText = "Make: this many more than you have now.\nOwn in total: until the bank holds this many (what you already have counts).";
        _planKind.ItemSelected += _ => { _planShown = ""; Replan(); };
        row.AddChild(_planKind);
        _planAmount.ValueChanged += _ => Replan();
        row.AddChild(_planAmount);
        row.AddChild(Style.Label("of", Style.Muted));
        _planPick.Control.TooltipText = "Every item one of your skills can make or gather. Click and type to search.";
        _planPick.Control.CustomMinimumSize = new Vector2(340, 0);
        _planPick.Picked += _ => { _planShown = ""; Replan(); };
        row.AddChild(_planPick.Control);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        StyleBoxFlat go = Style.Box(Style.GoodDim, 5, Style.Good, 1, 12, 5);
        _planAdd.AddThemeStyleboxOverride("normal", go);
        _planAdd.AddThemeStyleboxOverride("hover", Style.Box(new Color(0.16f, 0.38f, 0.23f), 5, Style.Good, 1, 12, 5));
        _planAdd.Pressed += AddPlan;
        _planSell.TooltipText = "Adds a last step that sells what the plan made (with \"Make\", what you had before is kept).";
        _planAll.TooltipText = "Ticked: the bank is not counted. Every material gets its step, for the whole amount, so a queue that\n"
            + "starts over makes one round's worth each time (nothing is skipped and no surplus piles up).\n"
            + "Unticked: what the bank already holds is used, and only what is missing is made.\n"
            + "Ticked by itself while \"Start over at the end\" is on.";
        _planAll.Toggled += _ => { _planAllSet = true; _planShown = ""; Replan(); };
        row.AddChild(_planAll);
        row.AddChild(_planSell);
        row.AddChild(_planLanePick);
        row.AddChild(_planAdd);
        _planForm.AddChild(row);

        _planTable = new Tree
        {
            Columns = 4, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(0, 184)
        };
        string[] titles = { "Step", "Until the bank holds", "In the bank", "Takes about" };
        for (int c = 0; c < titles.Length; c++)
        {
            _planTable.SetColumnTitle(c, titles[c]);
            _planTable.SetColumnTitleAlignment(c, HorizontalAlignment.Left);
            _planTable.SetColumnExpand(c, c is 0 or 1);
        }
        _planTable.SetColumnCustomMinimumWidth(2, 130);
        _planTable.SetColumnCustomMinimumWidth(3, 130);
        _planForm.AddChild(_planTable);
        _planNote.ClipText = true;
        _planForm.AddChild(_planNote);
        form.AddChild(_planForm);
    }

    void BuildSavedForm(VBoxContainer form)
    {
        _savedForm.AddThemeConstantOverride("separation", 8);
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        top.AddChild(Fixed("Name", 50));
        _presetName.TextSubmitted += _ => SaveNamed();
        top.AddChild(_presetName);
        Button save = MakeButton("Save the queue below under this name", SaveNamed);
        StyleBoxFlat go = Style.Box(Style.GoodDim, 5, Style.Good, 1, 12, 5);
        save.AddThemeStyleboxOverride("normal", go);
        save.AddThemeStyleboxOverride("hover", Style.Box(new Color(0.16f, 0.38f, 0.23f), 5, Style.Good, 1, 12, 5));
        top.AddChild(save);
        Label tip = Style.Label("To switch quickly, use the list at the top of the window.", Style.Muted, 12);
        tip.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        tip.HorizontalAlignment = HorizontalAlignment.Right;
        tip.ClipText = true;
        top.AddChild(tip);
        _savedForm.AddChild(top);

        _presetTable = new Tree
        {
            Columns = 3, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(0, 150)
        };
        string[] titles = { "Saved queue", "Steps", "What it does" };
        for (int c = 0; c < titles.Length; c++)
        {
            _presetTable.SetColumnTitle(c, titles[c]);
            _presetTable.SetColumnTitleAlignment(c, HorizontalAlignment.Left);
            _presetTable.SetColumnExpand(c, c == 2);
        }
        _presetTable.SetColumnCustomMinimumWidth(0, 250);
        _presetTable.SetColumnCustomMinimumWidth(1, 70);
        _presetTable.ItemSelected += () => { if (SelectedPreset() is { } name) _presetName.Text = name == Queue.AutoName ? "" : name; Disarm(); };
        _presetTable.ItemActivated += LoadSelected;
        _savedForm.AddChild(_presetTable);

        var bottom = new HBoxContainer();
        bottom.AddThemeConstantOverride("separation", 10);
        Button load = MakeButton("Load", LoadSelected);
        load.TooltipText = "Puts the selected queue in place of the one below. A double-click does the same.";
        bottom.AddChild(load);
        _deletePreset.Pressed += DeleteSelected;
        bottom.AddChild(_deletePreset);
        bottom.AddChild(new VSeparator());
        Button copy = MakeButton("Copy share code", CopyCode);
        copy.TooltipText = "Turns the selected saved queue (or, with none selected, the queue below) into a short text and copies it.";
        bottom.AddChild(copy);
        _code.TooltipText = "A share code somebody gave you. Paste it here and press Import.";
        bottom.AddChild(_code);
        Button import = MakeButton("Import", ImportCode);
        import.TooltipText = "Reads the code in the box (or, when the box is empty, the clipboard) and adds it to your saved queues.";
        bottom.AddChild(import);
        _savedForm.AddChild(bottom);
        form.AddChild(_savedForm);
    }

    Button _close = null!;
    static string CloseText => ActionQueueMod.Instance?.Key.With("Close") ?? "Close";
    /// <summary>The player gave the window another key in the loader's Mods menu.</summary>
    public void KeyChanged() => _close.Text = CloseText;
    /// <summary>For tests: what the Close button says.</summary>
    public string CloseLabel => _close.Text;

    static Button MakeButton(string text, Action onPressed)
    {
        var b = new Button { Text = text };
        b.Pressed += onPressed;
        return b;
    }

    static Label Fixed(string text, float width)
    {
        Label label = Style.Label(text, Style.Muted);
        label.CustomMinimumSize = new Vector2(width, 0);
        return label;
    }

    static void Fill(OptionButton pick, IEnumerable<string> names, int select = 0)
    {
        pick.Clear();
        foreach (string n in names) pick.AddItem(n);
        if (pick.ItemCount > 0) pick.Select(Math.Clamp(select, 0, pick.ItemCount - 1));
    }

    // ---------------------------------------------------------------- moving the window (asked for on Nexus, 26 Nov)

    Control _holder = null!;
    /// <summary>How far the window is from the middle of the screen (0, 0: centred). Kept in queue-window.json.</summary>
    Vector2 _offset;
    bool _dragging;

    /// <summary>Puts the window at its place: the middle plus the player's offset, kept on the screen (the title bar always is).</summary>
    void Place()
    {
        if (_holder == null || Panel == null) return;
        // as the centring container did: the window is as big as its content needs, and shrinks again with it
        Vector2 min = Panel.GetCombinedMinimumSize();
        if (Panel.Size != min) Panel.Size = min;
        Vector2 area = _holder.Size, size = Panel.Size;
        if (area.X <= 0 || area.Y <= 0) return;
        Vector2 centred = ((area - size) / 2).Floor();
        Vector2 at = centred + _offset;
        at.X = Math.Clamp(at.X, Math.Min(0, area.X - size.X), Math.Max(0, area.X - size.X));
        at.Y = Math.Clamp(at.Y, 0, Math.Max(0, area.Y - size.Y));
        at = at.Round();
        _offset = at - centred;   // what does not fit is not kept: the window never hides off screen later
        if (Panel.Position != at) Panel.Position = at;
    }

    /// <summary>The title bar: drag to move, double-click to centre again.</summary>
    void DragTitle(InputEvent e)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } b)
        {
            if (b.DoubleClick) { _dragging = false; _offset = Vector2.Zero; Place(); SavePlace(); return; }
            _dragging = b.Pressed;
            if (!b.Pressed) SavePlace();
        }
        else if (e is InputEventMouseMotion m && _dragging)
        {
            _offset += m.Relative;
            Place();
        }
    }

    static string PlaceFile => System.IO.Path.Combine(Queue.Dir, "queue-window.json");

    static Vector2 ReadPlace()
    {
        try
        {
            if (Queue.Dir == "" || !System.IO.File.Exists(PlaceFile)) return Vector2.Zero;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(PlaceFile));
            float x = (float)doc.RootElement.GetProperty("X").GetDouble(), y = (float)doc.RootElement.GetProperty("Y").GetDouble();
            return float.IsFinite(x) && float.IsFinite(y) ? new Vector2(x, y) : Vector2.Zero;
        }
        catch (Exception) { return Vector2.Zero; }
    }

    void SavePlace()
    {
        try { if (Queue.Dir != "") System.IO.File.WriteAllText(PlaceFile, $"{{\"X\": {_offset.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"Y\": {_offset.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}"); }
        catch (Exception) { }   // a place that could not be kept: the window still moves, it is centred next time
    }

    /// <summary>For tests: drag the title bar by this much (press, move, release, as a mouse does), or double-click it.</summary>
    public void DragTitleBy(Vector2 by)
    {
        DragTitle(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
        DragTitle(new InputEventMouseMotion { Relative = by });
        DragTitle(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
    }
    public void DoubleClickTitle() => DragTitle(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true });
    public Vector2 PanelPosition => Panel.Position;
    public Vector2 PanelSize => Panel.Size;
    public Vector2 ScreenSize => _holder.Size;

    public void Show(Game game)
    {
        if (!ReferenceEquals(_game, game))
        {
            _game = game;
            _skills = Targets.Skills(game);
            _levelSkills = game.LeveledEntities.Select(s => s.DisplayName.Text).ToList();
            Fill(_skillPick, _skills.Select(s => s.Entity.DisplayName.Text).Append(SellChoice).Append(EquipChoice).Append(SwitchChoice));
            Fill(_idleSkill, _skills.Select(s => s.Entity.DisplayName.Text).Prepend("Nothing"));
            Fill(_levelPick, _levelSkills);
            Fill(_orLevel, _levelSkills);
            Fill(_stopLevel, _levelSkills);
            _tasksFor = "";   // another character: its own first task
            _makeable = Planner.Makeable(game);
            _target.SetEntries(new List<PickEntry<Target>>());
            FillTargets(null);
        }
        else FillTargets(_target.SelectedKey, suggest: false);   // what is locked or shown may have changed since
        // The bank counts in the item lists are those of the moment the window opens. They are made again each time:
        // One Style's rare items come and go while the game runs (only the ones the character owns are listed).
        _items = Rares.ItemList(game);
        List<PickEntry<Item>> itemEntries = Rares.Describe(Catalog.Items(game, _items));
        _item.SetEntries(itemEntries);
        _orItem.SetEntries(itemEntries, _orItem.SelectedKey);
        _stopItem.SetEntries(itemEntries, _stopItem.SelectedKey);
        Rares.Paint(_item, game, itemEntries);
        Rares.Paint(_orItem, game, itemEntries);
        Rares.Paint(_stopItem, game, itemEntries);
        _stopShown = "?";   // the lists are made again: show the queue's stop in them
        _stopTasksFilled = false;
        _tasksStale = true;
        _planPick.SetEntries(Catalog.Items(game, _makeable));
        UntilChanged();
        Repaint();
        Pulse();
        _layer.Visible = true;
    }

    public void Hide()
    {
        foreach (var pick in new Action[] { _target.Close, _item.Close, _orItem.Close, _idleTarget.Close, _planPick.Close, _stopItem.Close, _taskPick.Close, _orTask.Close, _stopTask.Close }) pick();
        _layer.Visible = false;
    }

    // ---------------------------------------------------------------- the queue's own stop

    static string StopSignature(Goal? g) => g == null ? "" : $"{(int)g.Until}|{g.Amount}|{g.Subject}";

    /// <summary>The kind picked in the stop row (null: "never").</summary>
    Until? StopKindPicked => _stopPick.Selected < 0 || _stopPick.GetSelectedId() == NeverId ? null : (Until)_stopPick.GetSelectedId();

    /// <summary>
    /// Shows only the controls the picked kind needs. A newly picked kind starts from a sensible number and with no item
    /// or skill chosen: nothing is taken until the player picks one (an item or skill left over from before could stop
    /// a running queue on the spot).
    /// </summary>
    void StopKindShown(bool fresh)
    {
        Until? kind = StopKindPicked;
        bool wasSyncing = _syncing;
        _syncing = true;
        _stopAmount.Visible = kind is not (null or Until.Mastery);
        _stopItem.Control.Visible = kind is Until.Item or Until.ItemLeft;
        _stopLevel.Visible = kind == Until.Level;
        _stopTask.Control.Visible = kind == Until.Mastery;
        if (kind == Until.Mastery) FillStopTasks();
        _stopAmount.MinValue = kind == Until.ItemLeft ? 0 : 1;
        _stopAmount.MaxValue = kind == Until.Level ? Queue.MaxStopLevel : Queue.MaxStopAmount;
        if (fresh)
        {
            _stopAmount.Value = kind switch { Until.Level => 99, Until.ItemLeft => 0, _ => 1 };   // (a task: 1, not shown)
            ClearStopSubject();
        }
        _syncing = wasSyncing;
    }

    void ClearStopSubject()
    {
        _stopItem.Select(null);
        _stopTask.Select(null);
        _stopLevel.Select(-1);
        _stopLevel.Text = "Choose a skill";
    }

    /// <summary>
    /// Puts what the stop row shows into the queue. A kind with no item or skill picked yet means no stop until the pick:
    /// the row never shows one stop while the queue keeps another.
    /// </summary>
    void ApplyStop(bool fresh = false)
    {
        Until? kind = StopKindPicked;
        if (kind == null)
        {
            _stopShown = "";
            Queue.SetStopWhen(null);
            Say("The queue has no stop of its own: it ends when its steps are done.");
            return;
        }
        var goal = new Goal { Until = kind.Value, Amount = _stopAmount.Value };
        Goal? old = fresh ? null : Queue.StopWhen;
        if (kind is Until.Item or Until.ItemLeft)
        {
            if (_stopItem.Selected is { } picked) { goal.Subject = picked.Value.Id; goal.SubjectName = picked.Value.DisplayName.GetPluralText(1); }
            // An item the list does not hold right now (shown as "not available"): it stays the stop's item.
            else if (old is { Until: Until.Item or Until.ItemLeft } && _stopItem.SelectedKey == old.Subject) { goal.Subject = old.Subject; goal.SubjectName = old.SubjectName; }
        }
        else if (kind == Until.Mastery)
        {
            goal.Amount = 1;
            if (_stopTask.SelectedKey is { Length: > 0 } id && Mastery.Find(id) is { } task) { goal.Subject = id; goal.SubjectName = task.Text; }
            // A task this game does not have (shown as such): it stays the stop's task.
            else if (old is { Until: Until.Mastery } && _stopTask.SelectedKey == old.Subject) { goal.Subject = old.Subject; goal.SubjectName = old.SubjectName; }
        }
        else if (_stopLevel.Selected >= 0 && _stopLevel.Selected < _levelSkills.Count) goal.Subject = goal.SubjectName = _levelSkills[_stopLevel.Selected];
        else if (old is { Until: Until.Level } && !_levelSkills.Contains(old.Subject)) { goal.Subject = old.Subject; goal.SubjectName = old.SubjectName; }
        if (goal.Subject == "")
        {
            _stopShown = "";
            Queue.SetStopWhen(null);
            Say(kind switch
            {
                Until.Level => "Now pick the skill. Until then the queue has no stop of its own.",
                Until.Mastery => "Now pick the mastery task. Until then the queue has no stop of its own.",
                _ => "Now pick the item. Until then the queue has no stop of its own.",
            });
            return;
        }
        _stopShown = StopSignature(goal);   // before the queue is told: its change repaints the window
        Queue.SetStopWhen(goal);
        if (Queue.StopWhen is not { } now || StopSignature(now) != _stopShown) { _stopShown = "?"; Say("That stop could not be taken."); return; }
        string already = !Queue.StopReachedNow ? ""
            : Queue.Running ? " That is true already, so the queue stops now."
            : " That is true already, so the queue will not start until you change it.";
        Say($"The whole queue stops when {Queue.StopText(now)}.{already}");
    }

    /// <summary>Sets the stop row from the queue's stop (another queue loaded, a code imported, the window opened).</summary>
    void PaintStop()
    {
        string now = StopSignature(Queue.StopWhen);
        if (now == _stopShown) return;
        _stopShown = now;
        Goal? g = Queue.StopWhen;
        _syncing = true;
        int index = 0;
        for (int i = 0; i < _stopPick.ItemCount; i++)
            if (_stopPick.GetItemId(i) == (g == null ? NeverId : (int)g.Until)) index = i;
        _stopPick.Select(index);
        StopKindShown(fresh: false);
        ClearStopSubject();   // nothing left over from another stop
        if (g != null)
        {
            _stopAmount.Value = g.Amount;
            if (g.Until is Until.Item or Until.ItemLeft && !_stopItem.Select(g.Subject)) _stopItem.ShowMissing(g.Subject, g.SubjectName);
            int at = _levelSkills.IndexOf(g.Subject);
            if (g.Until == Until.Level && at >= 0) _stopLevel.Select(at);
            else if (g.Until == Until.Level) _stopLevel.Text = $"{g.SubjectName} (not in this game)";
            if (g.Until == Until.Mastery && !_stopTask.Select(g.Subject)) _stopTask.ShowMissing(g.Subject, $"{g.SubjectName} (not in this game)");
        }
        _syncing = false;
    }

    // test helpers for the stop row: the same handlers as a click
    public void PickStopKind(Until? kind)
    {
        for (int i = 0; i < _stopPick.ItemCount; i++)
            if (_stopPick.GetItemId(i) == (kind == null ? NeverId : (int)kind.Value)) { _stopPick.Select(i); StopKindShown(fresh: true); ApplyStop(fresh: true); return; }
    }
    public bool ChooseStopItem(string itemId) { bool ok = _stopItem.Select(itemId); if (ok) ApplyStop(); return ok; }
    public void ChooseStopLevel(string skill) { int at = _levelSkills.IndexOf(skill); if (at >= 0) { _stopLevel.Select(at); ApplyStop(); } }
    public void SetStopAmount(double value) => _stopAmount.Value = value;
    public bool ChooseStopTask(string id) { bool ok = _stopTask.Select(id); if (ok) ApplyStop(); return ok; }
    /// <summary>The stop row's task ("" when the task list is not shown).</summary>
    public string StopTaskShown => _stopTask.Control.Visible ? _stopTask.SelectedKey ?? "" : "";
    public int StopTaskCount => _stopTask.Count;
    public void OpenStopTaskList() => _stopTask.Open();
    public void CloseStopTaskList() => _stopTask.Close();

    /// <summary>
    /// The stop row's task list: every skill's tasks, the skills this queue uses first (a fight: the combat skills),
    /// grouped by skill and tier. Made once each time the window opens, and only when the kind is shown: it is long.
    /// </summary>
    void FillStopTasks()
    {
        if (_stopTasksFilled) return;
        _stopTasksFilled = true;
        var used = Queue.Steps.SelectMany(st => st.Entity == "MelvorBase:Combat" ? Mastery.CombatSkills : new[] { st.Entity }).ToHashSet();
        var entries = new List<PickEntry<string>>();
        foreach (Mastery.Task t in Mastery.All(used))
        {
            string text = t.Text, progress = Mastery.ProgressText(t);
            entries.Add(new PickEntry<string>(t.Id, text, t.Id, Group: t.Skill, Subgroup: t.Tier, Hint: progress,
                Tooltip: $"{t.Skill}, {t.Tier}: {text}  ({progress})", Muted: t.Done, Keywords: $"{t.Skill} {t.Tier}"));
        }
        bool wasSyncing = _syncing;
        _syncing = true;
        _stopTask.SetEntries(entries, _stopTask.SelectedKey);
        _syncing = wasSyncing;
    }
    /// <summary>What the stop row shows: "kind|amount|item|skill" (kind -1: never).</summary>
    public string StopShown => $"{(_stopPick.Selected < 0 ? -2 : StopKindPicked is { } k ? (int)k : -1)}|{_stopAmount.Value}|{_stopItem.SelectedKey}|{(_stopLevel.Visible && _stopLevel.Selected >= 0 ? _levelSkills[_stopLevel.Selected] : "")}";

    /// <summary>
    /// "Side by side" and the lane controls: only worth showing when the Multitask mod has made room for more than
    /// one action (lanes always once the queue uses them). Kept fresh every second: slots can be switched any time.
    /// </summary>
    void PaintRoom()
    {
        int room = Targets.MultitaskCapacity();
        _side.Visible = room > 1 && !Queue.LaneMode;
        _syncing = true;
        _side.ButtonPressed = Queue.SideBySide;
        _syncing = false;
        _side.Text = Queue.SideBySide ? $"Side by side: {room} at once" : "Side by side: OFF";
        bool lanesShown = room > 1 || Queue.LaneMode;
        _lanePick.Visible = _planLanePick.Visible = _laneButton.Visible = lanesShown;
        if (_lanePick.ItemCount != LanesOffered()) OfferLanes(LanesOffered());
        if (_lanePick.Selected < 0) { _lanePick.Select(0); _planLanePick.Select(0); }
        PaintLaneButton();
    }

    /// <summary>
    /// How many lanes the lists offer: as many as Multitask runs actions at once (up to 16), never fewer than five (as
    /// before) nor than the highest lane a step is in.
    /// </summary>
    static int LanesOffered()
    {
        int used = Queue.Steps.Count == 0 ? 1 : Queue.Steps.Max(st => Math.Clamp(st.Lane, 1, Queue.MaxLanes));
        return Math.Clamp(Math.Max(Math.Max(Targets.MultitaskCapacity(), used), 5), 1, Queue.MaxLanes);
    }

    void OfferLanes(int count)
    {
        int keep = Math.Clamp(_lanePick.Selected, 0, count - 1);
        _lanePick.Clear();
        _planLanePick.Clear();
        for (int lane = 1; lane <= count; lane++) { _lanePick.AddItem($"Lane {lane}"); _planLanePick.AddItem($"Lane {lane}"); }
        _lanePick.Select(keep);
        _planLanePick.Select(keep);
    }

    /// <summary>Called about once a second while open.</summary>
    public void Pulse()
    {
        if (_game == null) return;
        PaintRoom();
        ActionComponent? active = Targets.Main(_game);
        Target? target = active == null ? null : Targets.Current(active);
        _now.Text =active == null ? "Your character is doing nothing right now."
            : target == null ? $"Right now: {active.Entity.DisplayName.Text} (the queue cannot drive this one)"
            : $"Right now: {active.Entity.DisplayName.Text}: {target.Name}";
        if (_planForm.Visible) Replan();   // the bank changes while the game runs
    }

    // ---------------------------------------------------------------- the planner

    void ShowFace()
    {
        bool plan = _planMode.ButtonPressed, saved = _savedMode.ButtonPressed;
        bool planWasShown = _planForm.Visible;
        _stepForm.Visible = !plan && !saved;
        _planForm.Visible = plan;
        _savedForm.Visible = saved;
        if (plan && !planWasShown) { _planShown = ""; Replan(); }
        if (saved) PaintSaved();
    }

    // ---------------------------------------------------------------- saved queues and share codes

    string? SelectedPreset() => _presetTable.GetSelected() is { } row ? row.GetMetadata(0).AsString() : null;

    static string Summary(Preset preset)
    {
        string text = string.Join("  >  ", preset.Steps.Take(3).Select(s => s.Name));
        if (preset.Steps.Count > 3) text += $"  >  ... (+{preset.Steps.Count - 3})";
        return preset.Repeat ? text + "   (starts over)" : text;
    }

    void PaintSaved()
    {
        string? selected = SelectedPreset();
        _presetTable.Clear();
        TreeItem root = _presetTable.CreateItem();
        foreach (string name in Queue.PresetNames)
        {
            Preset preset = Queue.GetPreset(name)!;
            TreeItem row = _presetTable.CreateItem(root);
            row.SetMetadata(0, name);
            bool current = name == Queue.Name;
            row.SetText(0, current ? (Queue.Modified ? name + "   (loaded, changed since)" : name + "   (loaded)") : name);
            row.SetText(1, preset.Steps.Count.ToString());
            row.SetText(2, Summary(preset));
            row.SetCustomColor(0, current ? Style.Good : name == Queue.AutoName ? Style.Muted : Style.Text);
            row.SetCustomColor(1, Style.Muted);
            row.SetCustomColor(2, Style.Muted);
            if (name == selected) row.Select(0);
        }
        if (Queue.PresetNames.Count == 0)
        {
            TreeItem row = _presetTable.CreateItem(root);
            row.SetMetadata(0, "");
            row.SetText(0, "Nothing saved yet");
            row.SetText(2, "Build a queue below, type a name above and press Save.");
            row.SetSelectable(0, false); row.SetSelectable(1, false); row.SetSelectable(2, false);
            for (int c = 0; c < 3; c++) row.SetCustomColor(c, Style.Muted);
        }
    }

    void SaveNamed()
    {
        string name = _presetName.Text;
        if (Share.CleanName(name) == "" && Queue.Name != "") name = Queue.Name;   // no name typed: save over the loaded one
        Queue.SavePreset(name, out string message);
        Say(message);
    }

    void LoadSelected()
    {
        Disarm();
        string? name = SelectedPreset();
        if (string.IsNullOrEmpty(name)) { Say("Select a saved queue in the list first."); return; }
        Queue.LoadPreset(name, out string message);
        Say(message);
    }

    void DeleteSelected()
    {
        string? name = SelectedPreset();
        if (string.IsNullOrEmpty(name)) { Say("Select a saved queue in the list first."); return; }
        if (_armedDelete != name)
        {
            // Two clicks, so a slip of the mouse does not throw a queue away.
            _armedDelete = name;
            _deletePreset.Text = "Really delete?";
            int token = ++_armToken;
            _tree.CreateTimer(4).Timeout += () => { if (token == _armToken) Disarm(); };
            Say($"Press Delete again to remove '{name}' for good.");
            return;
        }
        Disarm();
        Queue.DeletePreset(name, out string message);
        Say(message);
    }

    void Disarm()
    {
        _armToken++;
        _armedDelete = "";
        _deletePreset.Text = "Delete";
    }

    void CopyCode()
    {
        string? name = SelectedPreset();
        if (name == "") name = null;
        string? code = Queue.ExportCode(name);
        if (code == null) { Say("There is nothing to share yet: build or select a queue first."); return; }
        _code.Text = code;
        try { DisplayServer.ClipboardSet(code); } catch { }
        Say($"Share code of {(name == null ? "the queue below" : $"'{name}'")} copied ({code.Length} characters). Paste it in a chat; it is also in the box.");
    }

    void ImportCode()
    {
        string text = _code.Text.Trim();
        if (text == "") { try { text = DisplayServer.ClipboardGet(); } catch { text = ""; } }
        if (Queue.ImportCode(text, out string imported, out string message))
        {
            _code.Text = "";
            PaintSaved();
            SelectPreset(imported);
        }
        Say(message);
    }

    void SelectPreset(string name)
    {
        for (TreeItem? row = _presetTable.GetRoot()?.GetFirstChild(); row != null; row = row.GetNext())
            if (row.GetMetadata(0).AsString() == name) { row.Select(0); _presetName.Text = name; return; }
    }

    void QuickPicked(long index)
    {
        if (_syncing || index < 0 || index >= _quickNames.Count) return;
        string name = _quickNames[(int)index];
        if (name == "") return;
        Queue.LoadPreset(name, out string message);
        Say(message);
    }

    void PaintQuick()
    {
        _syncing = true;
        _quick.Clear();
        _quickNames = new List<string>();
        if (Queue.Name == "")
        {
            _quick.AddItem(Queue.PresetNames.Count == 0 ? "Queue: not saved" : "Queue: not saved (pick one)");
            _quickNames.Add("");
        }
        foreach (string name in Queue.PresetNames)
        {
            _quick.AddItem(name == Queue.Name ? (Queue.Modified ? $"Queue: {name} *" : $"Queue: {name}") : name);
            _quickNames.Add(name);
        }
        int at = Queue.Name == "" ? 0 : _quickNames.IndexOf(Queue.Name);
        if (at >= 0) _quick.Select(at);
        _syncing = false;
    }

    /// <summary>For tests: the saved-queues face, its rows, and the controls a player would use.</summary>
    public void ShowSavedFace() => _savedMode.ButtonPressed = true;
    public int SavedRows => Queue.PresetNames.Count == 0 ? 0 : _presetTable.GetRoot()?.GetChildCount() ?? 0;
    public List<string> QuickTexts => Enumerable.Range(0, _quick.ItemCount).Select(i => _quick.GetItemText(i)).ToList();
    public string QuickSelected => _quick.Selected >= 0 ? _quick.GetItemText(_quick.Selected) : "";
    public void PickQuick(string name) { int at = _quickNames.IndexOf(name); if (at >= 0) { _quick.Select(at); QuickPicked(at); } }
    public void TypeNameAndSave(string name) { _presetName.Text = name; SaveNamed(); }
    public void SelectSaved(string name) => SelectPreset(name);
    public void PressLoad() => LoadSelected();
    public void PressDelete() => DeleteSelected();
    public string PressCopy() { CopyCode(); return _code.Text; }
    public void PasteAndImport(string code) { _code.Text = code; ImportCode(); }
    public string StatusText => _status.Text;
    public string SideText => _side.Visible ? _side.Text : "(hidden)";
    public void PressSide() => _side.ButtonPressed = !_side.ButtonPressed;

    /// <summary>Works the plan out again and redraws it, but only when something in it has changed.</summary>
    void Replan()
    {
        if (_game == null || !_planForm.Visible) return;
        Item? item = _planPick.Selected?.Value;
        if (item == null)
        {
            _plan = null;
            DrawPlan("none", () => { _planNote.Text = "Choose the item you want: click the list and type its name."; });
            return;
        }
        // In lanes, what other lanes will make is not counted: they run at the same time, not before.
        int lane = NewLane;
        IEnumerable<Step> before = Queue.LaneMode || lane > 1 ? Queue.Steps.Where(st => Math.Clamp(st.Lane, 1, Queue.MaxLanes) == lane) : Queue.Steps;
        if (!_planAllSet) _planAll.SetPressedNoSignal(Queue.LaneRepeats(lane));   // follows "start over" (of this lane) until the player ticks it
        PlanResult plan = _plan = Planner.Compute(_game, item, (long)_planAmount.Value, before.ToList(), more: _planKind.Selected == 0, everything: _planAll.ButtonPressed);
        string shown = item.Id + "|" + lane + "|" + plan.Own + "|" + plan.More + "|" + plan.Everything + "|" + plan.Problem + "|" + string.Join(";", plan.Steps.Concat(plan.Supply).Concat(plan.Covered)
            .Select(l => $"{l.Item.Id},{l.Maker?.Target.Id},{l.Need},{l.Have},{l.Actions},{l.Seconds:0},{l.Problem}"));
        DrawPlan(shown, () =>
        {
            TreeItem root = _planTable.CreateItem();
            int number = 0;
            foreach (PlanLine line in plan.Steps)
            {
                TreeItem row = _planTable.CreateItem(root);
                row.SetText(0, $"{++number}.  {line.Maker!.Name}");
                row.SetText(1, $"{line.Need:N0} {line.ItemName}");
                row.SetText(2, line.Have.ToString("N0"));
                row.SetText(3, line.Maker.Seconds > 0 ? Planner.Time(line.Seconds) : "cannot be told");
                row.SetCustomColor(2, Style.Muted);
                row.SetCustomColor(3, Style.Muted);
                if (line.Problem != "")
                {
                    row.SetText(3, "locked");
                    row.SetCustomColor(0, Style.Bad);
                    row.SetCustomColor(3, Style.Bad);
                    for (int c = 0; c < 4; c++) row.SetTooltipText(c, "Locked for now: " + line.Problem + ". The queue skips a step it cannot start.");
                }
            }
            foreach (PlanLine line in plan.Covered)
            {
                // Nothing to gather here: the bank has it. Shown so a missing "mine the ore" step explains itself.
                TreeItem row = _planTable.CreateItem(root);
                row.SetText(0, $"No step: enough {line.ItemName} in the bank");
                row.SetText(1, $"{line.Need:N0} {line.ItemName} needed");
                row.SetText(2, line.Have.ToString("N0"));
                row.SetText(3, "-");
                for (int c = 0; c < 4; c++) row.SetCustomColor(c, Style.Muted);
            }
            foreach (PlanLine line in plan.Supply)
            {
                TreeItem row = _planTable.CreateItem(root);
                row.SetText(0, line.Problem != "" ? "Bring yourself (its recipe is still locked)" : "Bring yourself (no skill on the queue makes it)");
                if (line.Problem != "") for (int c = 0; c < 4; c++) row.SetTooltipText(c, "Locked for now: " + line.Problem + ".");
                row.SetText(1, $"{line.Need:N0} {line.ItemName}");
                row.SetText(2, line.Have.ToString("N0"));
                for (int c = 0; c < 3; c++) row.SetCustomColor(c, Style.Accent);
            }
            if (plan.Steps.Count == 0 && plan.Problem != "")
            {
                // Nothing to do: say why where the steps would be, not only in the small line below.
                TreeItem row = _planTable.CreateItem(root);
                row.SetText(0, "No steps needed");
                row.SetText(1, plan.Problem);
                row.SetCustomColor(0, Style.Accent);
                row.SetCustomColor(1, Style.Accent);
                row.SetSelectable(0, false); row.SetSelectable(1, false); row.SetSelectable(2, false); row.SetSelectable(3, false);
            }
            _planNote.Text = PlanSummary(plan);
        });
        _planAdd.Disabled = plan.Steps.Count == 0;
    }

    void DrawPlan(string shown, Action draw)
    {
        if (shown == _planShown) return;
        _planShown = shown;
        _planTable.Clear();
        _planAdd.Disabled = true;
        draw();
    }

    static string PlanSummary(PlanResult plan)
    {
        if (plan.Problem != "") return plan.Problem;
        string text = plan.More ? $"To make {plan.Asked:N0} more (the bank will then hold {plan.Own:N0}): " : "";
        text += plan.Steps.Count == 1 ? "1 step" : $"{plan.Steps.Count} steps";
        int timed = plan.Steps.Count - plan.UnknownTimes;
        if (timed > 0) text += $", about {Planner.Time(plan.Seconds)} at normal game speed";
        if (plan.UnknownTimes > 0) text += timed > 0 ? $" plus {plan.UnknownTimes} whose time cannot be told" : ", time cannot be told in advance";
        text += ".";
        if (plan.Money.Count > 0) text += "  Costs " + string.Join(", ", plan.Money.Select(m => $"{m.Value:N0} {m.Key}")) + ".";
        if (plan.Covered.Count > 0) text += "  Grey lines: already in the bank, so no step for them.";
        if (plan.Supply.Count > 0) text += "  Yellow lines: items you have to bring yourself.";
        if (plan.Steps.Any(l => l.Problem != "")) text += "  Red lines are still locked.";
        return text;
    }

    void AddPlan()
    {
        // Work the plan out again at this very moment: the bank or the queue may have changed since it was drawn.
        _planShown = "";
        Replan();
        if (_plan == null || _plan.Steps.Count == 0) { Say(_plan?.Problem is { Length: > 0 } why ? why : "There is nothing to add."); return; }
        PlanResult plan = _plan;
        int added = Planner.AddToQueue(plan, NewLane, _planSell.ButtonPressed);
        string itemName = plan.Item.DisplayName.GetPluralText(1);
        Say(plan.More ? $"Plan added: {added} step(s) to make {plan.Asked:N0} more {itemName}." : $"Plan added: {added} step(s) to own {plan.Own:N0} {itemName}.");
        _planShown = "";
        Replan();   // what was just added now counts as "will be made"
    }

    /// <summary>For tests: open the planner, ask for an item and return the plan it shows.</summary>
    public PlanResult? PlanFor(string itemId, long own, bool more = false)
    {
        _planMode.ButtonPressed = true;
        _planKind.Select(more ? 0 : 1);
        if (!_planPick.Select(itemId)) return null;
        _planAmount.Value = own;
        _planShown = "";
        Replan();
        return _plan;
    }

    /// <summary>For tests: press "Add these steps to the queue".</summary>
    public void PressAddPlan() => AddPlan();

    /// <summary>For tests: what the planner shows under its table, and how many rows the table has.</summary>
    public string PlanNote => _planNote.Text;
    public int PlanRows => _planTable.GetRoot()?.GetChildCount() ?? 0;
    public string PlanRowText(int index)
    {
        TreeItem? row = _planTable.GetRoot()?.GetFirstChild();
        for (int i = 0; i < index && row != null; i++) row = row.GetNext();
        return row == null ? "" : $"{row.GetText(0)} | {row.GetText(1)} | {row.GetText(2)}";
    }

    /// <summary>For tests: back to the one-step form.</summary>
    public void ShowStepForm() => _stepMode.ButtonPressed = true;

    // ---------------------------------------------------------------- the "new step" form

    /// <summary>
    /// Fills the target list of the chosen skill. <paramref name="select"/> is a list key (or a plain target id);
    /// without one, the first thing the character may do now is chosen. <paramref name="suggest"/> fills in the item
    /// and level that go with it (not when the list is only refreshed).
    /// </summary>
    void FillTargets(string? select, bool suggest = true)
    {
        if (_game != null && Equipping)
        {
            List<PickEntry<Target>> gear = EquipEntries(_game);
            _target.SetEntries(gear, KeyIn(gear, select) ?? gear.FirstOrDefault(e => !e.Muted && e.Enabled)?.Key);
            Rares.Paint(_target, _game, gear);
            return;
        }
        if (_game != null && Switching) { List<PickEntry<Target>> queues = SavedQueueEntries(); _target.SetEntries(queues, KeyIn(queues, select) ?? queues.FirstOrDefault(e => e.Enabled)?.Key); return; }
        if (_game == null || _skillPick.Selected < 0 || _skillPick.Selected >= _skills.Count) return;
        var (entity, action) = _skills[_skillPick.Selected];
        List<PickEntry<Target>> entries = Catalog.For(_game, entity, action, select);
        _target.SetEntries(entries, KeyIn(entries, select) ?? entries.FirstOrDefault(e => !e.Muted)?.Key ?? entries.FirstOrDefault()?.Key);
        ShowMode();
        FillEvents();
        FillTasks();
        if (!suggest) return;
        // A level goal is usually about the skill being trained.
        int level = _levelSkills.IndexOf(entity.DisplayName.Text);
        if (level >= 0) _levelPick.Select(level);
        if (Kind == Until.ItemLeft) SuggestFood(); else SuggestItem();
    }

    /// <summary>The list key meant by <paramref name="select"/>: a key, or a target id (the first entry with it).</summary>
    static string? KeyIn(List<PickEntry<Target>> entries, string? select)
    {
        if (select == null) return null;
        if (entries.Any(e => e.Key == select)) return select;
        return entries.FirstOrDefault(e => e.Value.Id == select)?.Key;
    }

    /// <summary>
    /// "Until I own N of an item" almost always means the thing the action makes, so that item is filled in
    /// whenever the action changes. Another item can still be searched for and picked.
    /// </summary>
    void SuggestItem()
    {
        if (_skillPick.Selected < 0 || _skillPick.Selected >= _skills.Count || _target.Selected is not { } chosen) return;
        if (Kind == Until.ItemLeft) return;   // counting down: the item was chosen on purpose
        if (Kind == Until.Spent)
        {
            // Using up: what the recipe eats (the first of its materials the list knows).
            foreach (string id in Targets.Ingredients(_skills[_skillPick.Selected].Action, chosen.Value).OrderBy(i => i))
                if (_item.Entry(id) != null) { _item.Select(id); return; }
            return;
        }
        Item? product = Targets.Product(_skills[_skillPick.Selected].Action, chosen.Value);
        if (product != null) _item.Select(product.Id);
    }

    /// <summary>
    /// The box for the recipe's other way of making (Smithing's Sacrificial Anvil): shown only when the chosen recipe
    /// has one the character can use, ticked the way the game's own switch stands right now.
    /// </summary>
    void ShowMode()
    {
        List<(string Id, string Name)> modes = Pseudo || _skillPick.Selected < 0 || _skillPick.Selected >= _skills.Count || _target.Selected is not { } chosen
            ? new() : Targets.Modes(_skills[_skillPick.Selected].Action, chosen.Value.Id);
        _mode.Visible = modes.Count > 0;
        if (modes.Count == 0) { _modeId = ""; return; }
        _modeId = modes[0].Id;
        _mode.Text = modes[0].Name;
        _mode.ButtonPressed = Targets.ModeOf(_skills[_skillPick.Selected].Action, _target.Selected!.Value.Id) == _modeId;
    }

    /// <summary>For tests: the way-of-making box (null when it is hidden), and ticking it as a click does.</summary>
    public string? ModeShown => _mode.Visible ? $"{_mode.Text}: {(_mode.ButtonPressed ? "on" : "off")}" : null;
    public void PressMode(bool on) => _mode.ButtonPressed = on;

    void SuggestFood()
    {
        if (_game == null || !_game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) || e is not GamesByMalcs.Melvor2.Entities.Combat.CombatEntity combat) return;
        // The food chosen for the fight; without one, the food the bank has most of.
        Item? food = combat.Food.SelectedFood?.Item;
        if (food == null && _game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? b) && b is GamesByMalcs.Melvor2.Entities.Bank.BankEntity bank)
            food = bank.Inventory.Stacks.Where(s => s.Quantity > 0 && s.Item.Capabilities.TryGet<GamesByMalcs.Melvor2.Components.CombatFood.FoodCapability>(out _))
                .OrderByDescending(s => s.Quantity).FirstOrDefault()?.Item;
        if (food != null) _item.Select(food.Id);
    }

    /// <summary>For tests: pick how a step ends, the way the list does.</summary>
    public void ChooseUntil(Until kind) { _untilPick.Select(_untilPick.GetItemIndex((int)kind)); UntilChanged(); }
    public double AmountShown => _amount.Value;
    public void SetAmount(double value) => _amount.Value = value;

    /// <summary>For tests: choose a skill and one of its actions the way the two lists do (a list key or a target id).</summary>
    public bool Choose(string entityId, string targetId)
    {
        int skill = _skills.FindIndex(s => s.Entity.Id == entityId);
        if (skill < 0) return false;
        _skillPick.Select(skill);
        FillKinds(_untilPick, Astrology, withStops: true);
        FillKinds(_orPick, Astrology, withStops: false);
        FillTargets(targetId);
        UntilChanged();
        return _target.Selected is { } chosen && (chosen.Value.Id == targetId || chosen.Key == targetId);
    }

    /// <summary>For tests: the item the "until I own" goal would count right now.</summary>
    public Item? CountedItem => _item.Selected?.Value;

    /// <summary>For tests: the pick lists of the window.</summary>
    public PickProbe TargetProbe => _targetProbe ??= PickProbe.Of(_target);
    public PickProbe ItemProbe => _itemProbe ??= PickProbe.Of(_item);
    public PickProbe IdleProbe => _idleProbe ??= PickProbe.Of(_idleTarget);
    public PickProbe PlanProbe => _planProbe ??= PickProbe.Of(_planPick);
    PickProbe? _targetProbe, _itemProbe, _idleProbe, _planProbe;
    /// <summary>For tests: the catalog's entries (key, group) for a skill, without the window.</summary>
    public static List<(string Key, string Text, string Group, string Subgroup, bool Muted, string Hint, string Icon, string GroupIcon)> CatalogOf(Game game, GameEntity skill, ActionComponent action) =>
        Catalog.For(game, skill, action).Select(e => (e.Key, e.Text, e.Group, e.Subgroup, e.Muted, e.Hint, e.Icon?.ResourcePath ?? "", e.GroupIcon?.ResourcePath ?? "")).ToList();
    public static string CatalogKey(string id, string extra) => Catalog.Key(id, extra);

    void UseCurrent()
    {
        if (_game == null) return;
        ActionComponent? active = Targets.Main(_game);
        Target? target = active == null ? null : Targets.Current(active);
        int skill = active == null ? -1 : _skills.FindIndex(s => ReferenceEquals(s.Action, active));
        if (target == null || skill < 0) { Say("Start an action in the game first, then press this."); return; }
        _skillPick.Select(skill);
        FillKinds(_untilPick, Astrology, withStops: true);
        FillKinds(_orPick, Astrology, withStops: false);
        FillTargets(Catalog.Key(target));
        UntilChanged();
        Say($"Filled in: {_skills[skill].Entity.DisplayName.Text}: {target.Name}");
    }

    // ---------------------------------------------------------------- the idle action

    void FillIdleTargets(Step? select)
    {
        int skill = _idleSkill.Selected - 1;
        if (_game == null || skill < 0 || skill >= _skills.Count) { _idleTarget.SetEntries(new List<PickEntry<Target>>()); return; }
        var (entity, action) = _skills[skill];
        string? key = select == null ? null : Catalog.Key(select.TargetId, select.Extra);
        List<PickEntry<Target>> entries = Catalog.For(_game, entity, action, key);
        string? pick = KeyIn(entries, key) ?? (select == null ? entries.FirstOrDefault(e => !e.Muted)?.Key ?? entries.FirstOrDefault()?.Key : null);
        _idleTarget.SetEntries(entries, pick);
        // A saved idle action the game does not offer any more: show it, do not swap it for another one.
        if (pick == null && select != null) _idleTarget.ShowMissing(key!, select.Name);
    }

    void PickIdle()
    {
        int skill = _idleSkill.Selected - 1;
        if (skill < 0 || skill >= _skills.Count) { Queue.SetIdle(null); return; }
        if (_idleTarget.Selected is not { } chosen) return;
        GameEntity entity = _skills[skill].Entity;
        Target target = chosen.Value;
        Queue.SetIdle(new Step { Entity = entity.Id, TargetId = target.Id, Extra = target.Extra, Name = $"{entity.DisplayName.Text}: {target.Name}" });
        Say($"When the queue waits or ends, your character does: {entity.DisplayName.Text}: {target.Name}.");
    }

    void UseCurrentForIdle()
    {
        if (_game == null) return;
        ActionComponent? active = Targets.Main(_game);
        Target? target = active == null ? null : Targets.Current(active);
        int skill = active == null ? -1 : _skills.FindIndex(s => ReferenceEquals(s.Action, active));
        if (target == null || skill < 0) { Say("Start an action in the game first, then press this."); return; }
        _syncing = true;
        _idleSkill.Select(skill + 1);
        FillIdleTargets(new Step { TargetId = target.Id, Extra = target.Extra, Name = target.Name });
        _syncing = false;
        PickIdle();
    }

    /// <summary>Shows the chosen idle action in the two lists (they are only refilled when it changed).</summary>
    void PaintIdle()
    {
        Step? idle = Queue.Idle;
        _syncing = true;
        int skill = idle == null ? -1 : _skills.FindIndex(s => s.Entity.Id == idle.Entity);
        if (idle == null || skill < 0)
        {
            if (_idleSkill.ItemCount > 0) _idleSkill.Select(0);
            _idleTarget.Control.Visible = false;
        }
        else
        {
            bool shown = _idleSkill.Selected == skill + 1 && _idleTarget.SelectedKey == Catalog.Key(idle.TargetId, idle.Extra);
            if (!shown)
            {
                _idleSkill.Select(skill + 1);
                FillIdleTargets(idle);
            }
            _idleTarget.Control.Visible = true;
        }
        _syncing = false;
        string state = idle == null ? "" : Queue.IdleState;
        _idleState.Visible = state != "";
        _idleState.Text = state == "" ? "" : $"Idle action: {state}";
        _idleState.AddThemeColorOverride("font_color", state.StartsWith("running") ? Style.Good : state.StartsWith("stopped") || state.StartsWith("cannot") ? Style.Bad : Style.Muted);
    }

    /// <summary>For tests: choose the idle action the way the two lists do ("" chooses Nothing).</summary>
    public bool ChooseIdle(string entityId, string targetId)
    {
        if (entityId == "") { _idleSkill.Select(0); _idleSkill.EmitSignal(OptionButton.SignalName.ItemSelected, 0); return Queue.Idle == null; }
        int skill = _skills.FindIndex(s => s.Entity.Id == entityId);
        if (skill < 0) return false;
        _idleSkill.Select(skill + 1);
        _idleSkill.EmitSignal(OptionButton.SignalName.ItemSelected, skill + 1);
        string? Find() => _idleTarget.Entry(targetId) != null ? targetId
            : _idleTarget.VisibleKeys.Select(k => _idleTarget.Entry(k)).FirstOrDefault(e => e?.Value.Id == targetId)?.Key;
        string? key = Find();
        // A target the game hides right now (a quest target) is kept in the list the way a saved step is.
        if (key == null) { FillIdleTargets(new Step { TargetId = targetId, Name = targetId }); key = Find(); }
        if (key == null || !_idleTarget.Pick(key)) return false;
        return Queue.Idle?.TargetId == targetId;
    }

    public void PressUseCurrentForIdle() => UseCurrentForIdle();
    /// <summary>For tests: the sell face of the form (item, how many to keep), the lane for new steps, "then sell them".</summary>
    public bool ChooseSell(string itemId, double keep)
    {
        _skillPick.Select(_skills.Count);
        UntilChanged();
        _amount.Value = keep;
        return _item.Select(itemId);
    }
    public bool SellFaceShown => Selling && _item.Control.Visible && !_target.Control.Visible && _untilLabel.Text == "keep";
    /// <summary>For tests: the Equip and "go to a saved queue" faces, the "or until" row and the astrology goals, as clicks do them.</summary>
    public bool ChooseEquip(string targetId, double keep)
    {
        _skillPick.Select(_skills.Count + 1);
        _skillPick.EmitSignal(OptionButton.SignalName.ItemSelected, _skills.Count + 1);
        bool picked = _target.Pick(targetId);
        _amount.Value = keep;
        return picked && _target.SelectedKey == targetId;
    }
    public bool ChooseSwitch(string name)
    {
        _skillPick.Select(_skills.Count + 2);
        _skillPick.EmitSignal(OptionButton.SignalName.ItemSelected, _skills.Count + 2);
        return name == "" || _target.Pick(name);
    }
    public List<string> TargetKeys => _target.VisibleKeys;
    public string PseudoNoteShown => _pseudoNote.Visible ? _pseudoNote.Text : "";
    public bool KeepShown => _amount.Visible && _untilLabel.Text == "keep";
    public bool OrShown => _orBox.Visible;
    public void PressOr(bool on) => _orBox.ButtonPressed = on;
    public bool OrRowShown => _orRow.Visible;
    public void ChooseOrUntil(Until kind) { _orPick.Select(_orPick.GetItemIndex((int)kind)); OrChanged(); }
    public void SetOrAmount(double value) => _orAmount.Value = value;
    public bool ChooseOrItem(string itemId) => _orItem.Select(itemId);
    public List<string> UntilKinds => Enumerable.Range(0, _untilPick.ItemCount).Select(i => ((Until)_untilPick.GetItemId(i)).ToString()).ToList();
    public void ChooseNodes(bool major) => _nodePick.Select(major ? 1 : 0);
    public List<string> EventsShown => Enumerable.Range(0, _eventPick.ItemCount).Select(i => _eventPick.GetItemText(i)).ToList();
    public bool ChooseEvent(string eventId)
    {
        for (int i = 0; i < _eventPick.ItemCount; i++)
            if (_eventPick.GetItemMetadata(i).AsString() == eventId) { _eventPick.Select(i); return true; }
        return false;
    }
    public bool LanePickShown => _lanePick.Visible;
    /// <summary>For tests: how many lanes the Lane lists offer (and the next lane the Lane button moves a step to).</summary>
    public int LanesInList => _lanePick.ItemCount;
    public int LanesInPlanList => _planLanePick.ItemCount;
    public void ChooseLane(int lane) { _lanePick.Select(lane - 1); _planLanePick.Select(lane - 1); }
    public void SetPlanSell(bool on) => _planSell.ButtonPressed = on;
    public void SelectStep(int index) => SelectRow(index);
    public void PressNextLane() => NextLane();
    /// <summary>For tests: the list as shown, one line per row ("Lane 2" headings included).</summary>
    public List<string> RowsShown()
    {
        var rows = new List<string>();
        for (TreeItem? row = _table.GetRoot()?.GetFirstChild(); row != null; row = row.GetNext())
        {
            rows.Add($"{row.GetText(0)}|{row.GetText(1)}|{row.GetText(3)}");
            for (TreeItem? child = row.GetFirstChild(); child != null; child = child.GetNext()) rows.Add($"  {child.GetText(0)}|{child.GetText(1)}|{child.GetText(3)}");
        }
        return rows;
    }

    /// <summary>For tests: the "Add step" button and the Start / Pause button, as a click does them.</summary>
    public void PressAddStep() => AddStep();
    public void PressRun() => _run.EmitSignal(BaseButton.SignalName.Pressed);
    public string StatusShown => _status.Text;
    public bool ItemPickerShown => _item.Control.Visible;
    /// <summary>For tests: what the idle row shows ("Nothing" or "skill | action"), and its state line.</summary>
    public string IdleShown => _idleSkill.Selected <= 0 ? "Nothing"
        : $"{_idleSkill.GetItemText(_idleSkill.Selected)} | {(_idleTarget.Control.Visible && _idleTarget.Selected != null ? _idleTarget.Selected.Text : "?")}";
    public string IdleStateShown => _idleState.Visible ? _idleState.Text : "";
    /// <summary>For tests: the two check boxes under the idle row, as a click does them.</summary>
    public void PressIdleAlways(bool on) { _idleAlways.ButtonPressed = on; }
    public void PressGameCreate(bool on) { _gameCreate.ButtonPressed = on; }

    const string SellChoice = "Sell from the bank", EquipChoice = "Equip", SwitchChoice = "Go to a saved queue";
    bool Selling => _skillPick.Selected >= 0 && _skillPick.Selected == _skills.Count;
    bool Equipping => _skillPick.Selected >= 0 && _skillPick.Selected == _skills.Count + 1;
    bool Switching => _skillPick.Selected >= 0 && _skillPick.Selected == _skills.Count + 2;
    /// <summary>A step that is not an action: sell, equip, go to a saved queue.</summary>
    bool Pseudo => Selling || Equipping || Switching;
    bool Astrology => !Pseudo && _skillPick.Selected >= 0 && _skillPick.Selected < _skills.Count
        && _skills[_skillPick.Selected].Action is GamesByMalcs.Melvor2.Entities.Astrology.AstrologyActionComponent;
    bool EquipIsLoadout => _target.Selected?.Value.Id.StartsWith(Step.LoadoutPrefix, StringComparison.Ordinal) == true;
    bool _wasSelling;

    /// <summary>The goal kinds are list entries whose id is the <see cref="Until"/> number (Astrology has two more).</summary>
    Until Kind => _untilPick.Selected < 0 ? Until.Stops : (Until)_untilPick.GetSelectedId();
    Until OrKind => _orPick.Selected < 0 ? Until.Item : (Until)_orPick.GetSelectedId();

    static void FillKinds(OptionButton pick, bool astrology, bool withStops)
    {
        int keep = pick.Selected >= 0 ? pick.GetSelectedId() : -1;
        pick.Clear();
        for (int i = 0; i < UntilNames.Length; i++)
        {
            var kind = (Until)i;
            if (kind == Until.Stops && !withStops) continue;
            if (kind is Until.Nodes or Until.Event && !astrology) continue;
            pick.AddItem(UntilNames[i], i);
        }
        int index = keep >= 0 ? pick.GetItemIndex(keep) : -1;
        pick.Select(index >= 0 ? index : 0);
    }

    void UntilChanged()
    {
        bool selling = Selling, equipping = Equipping, switching = Switching, pseudo = selling || equipping || switching;
        _target.Control.Visible = !selling;
        _useNow.Visible = _untilPick.Visible = !pseudo;
        ShowMode();
        _nodePick.Visible = _eventPick.Visible = false;
        _taskPick.Control.Visible = false;
        if (pseudo)
        {
            // Sell: which item, and how many to keep in the bank. Equip: what to put on (and for a stack, how many to
            // keep). Go to a saved queue: which one.
            bool keep = selling || equipping && !EquipIsLoadout;
            _untilLabel.Text = keep ? "keep" : "";
            _item.Control.Visible = selling;
            _amount.Visible = keep;
            _levelPick.Visible = _orBox.Visible = _orRow.Visible = false;
            _amount.MinValue = 0;
            _amount.MaxValue = 100_000_000;
            _amount.Suffix = "";
            if (!_wasSelling)
            {
                if (_lastKind == Until.ItemLeft) _amountLeft = _amount.Value; else _amountOther = Math.Max(1, _amount.Value);
                _amount.Value = 0;
            }
            _amount.TooltipText = selling ? "How many to keep in the bank: 0 sells all of them."
                : "For arrows, bolts, javelins and other stacks: how many to keep in the bank (0 puts all of them on).\nOther gear is simply put on.";
            _pseudoNote.Text = switching ? "the queue goes on with this saved queue once every step before it is over (in every lane)"
                : equipping && EquipIsLoadout ? "switches to this loadout (gear, food, prayers and spells) while the game is open"
                : equipping ? "puts it on from the bank while the game is open" : "";
            _pseudoNote.Visible = _pseudoNote.Text != "";
            _wasSelling = true;
            return;
        }
        _pseudoNote.Visible = false;
        _untilLabel.Text = "until";
        _amount.TooltipText = "";
        if (_wasSelling)
        {
            _wasSelling = false;
            _amount.MinValue = _lastKind == Until.ItemLeft ? 0 : 1;
            _amount.Value = _lastKind == Until.ItemLeft ? _amountLeft : Math.Max(1, _amountOther);
        }
        var kind = Kind;
        _amount.Visible = kind is not (Until.Stops or Until.Mastery);
        _item.Control.Visible = kind is Until.Item or Until.ItemLeft or Until.Made or Until.Spent;
        _amount.MinValue = kind == Until.ItemLeft ? 0 : 1;
        if (kind == Until.ItemLeft && _lastKind != Until.ItemLeft)
        {
            // Counting down is mostly about food in a fight: offer the food the character is eating, and a small number.
            _amountOther = _amount.Value;
            _amount.Value = _amountLeft;
            SuggestFood();
        }
        else if (kind != Until.ItemLeft && _lastKind == Until.ItemLeft)
        {
            _amountLeft = _amount.Value;
            _amount.Value = Math.Max(1, _amountOther);
            if (kind is Until.Item or Until.Made or Until.Spent) SuggestItem();
        }
        else if (kind != _lastKind && (kind == Until.Spent || _lastKind == Until.Spent)) SuggestItem();
        if (kind is Until.Nodes or Until.Event && _lastKind is not (Until.Nodes or Until.Event) && _amount.Value > 99) _amount.Value = kind == Until.Event ? 1 : 5;
        _lastKind = kind;
        _levelPick.Visible = kind == Until.Level;
        _nodePick.Visible = kind == Until.Nodes;
        _eventPick.Visible = kind == Until.Event;
        _taskPick.Control.Visible = kind == Until.Mastery;
        FillTasks();
        _amount.Suffix = kind == Until.Minutes ? " min" : "";
        _amount.MaxValue = kind == Until.Level ? 999 : 100_000_000;
        if (kind == Until.Level && _amount.Value > 999) _amount.Value = 99;
        if (kind != Until.Nodes) _amount.TooltipText = "";
        CapNodes(_amount, _nodePick, kind);
        _orBox.Visible = true;
        _orRow.Visible = _orBox.ButtonPressed;
        OrChanged();
    }

    void OrChanged()
    {
        var kind = OrKind;
        _orAmount.Visible = kind is not (Until.Stops or Until.Mastery);
        _orItem.Control.Visible = kind is Until.Item or Until.ItemLeft or Until.Made or Until.Spent;
        _orLevel.Visible = kind == Until.Level;
        _orNode.Visible = kind == Until.Nodes;
        _orEvent.Visible = kind == Until.Event;
        _orTask.Control.Visible = kind == Until.Mastery;
        FillTasks();
        _orAmount.MinValue = kind == Until.ItemLeft ? 0 : 1;
        _orAmount.Suffix = kind == Until.Minutes ? " min" : "";
        _orAmount.MaxValue = kind == Until.Level ? 999 : 100_000_000;
        if (kind == Until.Level && _orAmount.Value > 999) _orAmount.Value = 99;
        _orAmount.TooltipText = "";
        CapNodes(_orAmount, _orNode, kind);
    }

    /// <summary>A node goal: the number goes no higher than the chosen constellation's minor (or major) unlocks.</summary>
    void CapNodes(SpinBox amount, OptionButton node, Until kind)
    {
        if (kind != Until.Nodes || _game == null || _target.Selected is not { } chosen) return;
        int most = Queue.MostNodes(_game, chosen.Value.Id, node.Selected == 1);
        if (most <= 0) return;
        amount.MaxValue = most;
        if (amount.Value > most) amount.Value = most;
        amount.TooltipText = $"This constellation has {most} {(node.Selected == 1 ? "major" : "minor")} node unlocks in all.";
    }

    /// <summary>
    /// The mastery tasks the chosen skill can wait for, in both task lists: first the ones for the chosen target (the
    /// next one not done is picked), then the skill's others by tier (a fight: by combat skill). Done tasks are shown
    /// dimmed; they can still be picked. A list made again for the same skill and target keeps the player's choice.
    /// Only made while a goal is "a mastery task is complete" (a fight has some 1,200 tasks), and only again when the
    /// skill or target changed or the window was opened again.
    /// </summary>
    void FillTasks()
    {
        if (Pseudo || !(Kind == Until.Mastery || _orBox.ButtonPressed && OrKind == Until.Mastery)) return;
        var entries = new List<PickEntry<string>>();
        string entity = "", targetId = "", targetName = "", first = "";
        if (_game != null && _skillPick.Selected >= 0 && _skillPick.Selected < _skills.Count)
        {
            var (skill, action) = _skills[_skillPick.Selected];
            entity = skill.Id;
            targetId = _target.Selected?.Value.Id ?? "";
            targetName = _target.Selected?.Value.Name ?? "";
            bool fight = entity == "MelvorBase:Combat";
            // The target, and what it makes: a task "catch Raw Shark" is one for the spot where sharks are caught.
            var ids = new List<string>();
            if (targetId != "")
            {
                ids.Add(targetId);
                try { if (_target.Selected is { } chosen && Targets.Product(action, chosen.Value) is { } made) ids.Add(made.Id); } catch { }
            }
            string forNow = $"{entity}|{targetId}";
            if (forNow == _tasksFor && !_tasksStale) return;   // nothing new to show
            foreach (Mastery.Task t in Mastery.For(entity, ids))
            {
                bool mine = Mastery.IsFor(t, ids);
                string progress = Mastery.ProgressText(t), text = t.Text;
                if (first == "" && mine && !t.Done) first = t.Id;
                entries.Add(new PickEntry<string>(t.Id, text, t.Id,
                    Group: mine ? $"For {targetName}" : fight ? t.Skill : "Other tasks of this skill",
                    Subgroup: mine ? "" : t.Tier,
                    Hint: mine ? $"{(fight ? t.Skill + ", " : "")}{t.Tier}: {progress}" : progress,
                    Tooltip: $"{t.Skill}, {t.Tier}: {text}  ({progress})",
                    Muted: t.Done, Keywords: $"{t.Skill} {t.Tier}"));
            }
        }
        string built = $"{entity}|{targetId}";
        bool same = built == _tasksFor;
        _tasksFor = built;
        _tasksStale = false;
        foreach (PickList<string> list in new[] { _taskPick, _orTask })
        {
            string? keep = same && list.SelectedKey is { } key && entries.Any(e => e.Key == key) ? key : null;
            list.SetEntries(entries, keep ?? first);   // "" (no task for this target): nothing chosen
        }
    }

    /// <summary>For tests: the task list as "group|key|hint" lines, and picking one as a click does.</summary>
    public List<string> TaskEntries(bool or = false) => (or ? _orTask : _taskPick).VisibleKeys
        .Select(k => (or ? _orTask : _taskPick).Entry(k) is { } e ? $"{e.Group}|{e.Key}|{e.Hint}|{(e.Muted ? "done" : "")}" : k).ToList();
    public void OpenTaskList() => _taskPick.Open();
    public void CloseTaskList() => _taskPick.Close();
    public string? TaskChosen(bool or = false) => (or ? _orTask : _taskPick).SelectedKey;
    public bool ChooseTask(string id, bool or = false) => (or ? _orTask : _taskPick).Select(id);

    /// <summary>The events the chosen constellation can show, in both event lists (kept when still there).</summary>
    void FillEvents()
    {
        List<(string Id, string Name)> events = _game != null && Astrology && _target.Selected is { } chosen
            ? Queue.AstrologyEvents(_game, chosen.Value.Id) : new();
        foreach (OptionButton pick in new[] { _eventPick, _orEvent })
        {
            string keep = pick.Selected >= 0 ? pick.GetItemMetadata(pick.Selected).AsString() : "";
            pick.Clear();
            for (int i = 0; i < events.Count; i++) { pick.AddItem(events[i].Name); pick.SetItemMetadata(i, events[i].Id); }
            int at = events.FindIndex(e => e.Id == keep);
            if (pick.ItemCount > 0) pick.Select(at >= 0 ? at : 0);
        }
    }

    /// <summary>
    /// For the Equip step: the game's loadouts first, then every piece of gear, grouped by kind (bank count as hint).
    /// One Style's rare items only while the character has them (in the bank or worn), with their affixes as tooltip.
    /// </summary>
    static List<PickEntry<Target>> EquipEntries(Game game)
    {
        var entries = new List<PickEntry<Target>>();
        if (game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) && e is GamesByMalcs.Melvor2.Entities.Combat.CombatEntity combat)
            foreach (var loadout in combat.Loadouts.LoadoutDisplayOrder)
            {
                string id = Step.LoadoutPrefix + loadout.Data.Id;
                string hint = !loadout.IsUnlocked ? "locked" : combat.Loadouts.CurrentLoadout == loadout ? "in use" : "";
                entries.Add(new PickEntry<Target>(id, loadout.Name, new Target(id, loadout.Name, ""), "Loadouts", Hint: hint, Muted: !loadout.IsUnlocked));
            }
        GamesByMalcs.Melvor2.Entities.Bank.BankEntity? bank = game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? b) ? b as GamesByMalcs.Melvor2.Entities.Bank.BankEntity : null;
        var gear = new List<PickEntry<Target>>();
        foreach (Item item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
        {
            if (!item.Capabilities.TryGet<GamesByMalcs.Melvor2.Components.Equipment.EquipmentCapability>(out var equip)) continue;
            string name = item.DisplayName.GetPluralText(1);
            int owned = bank?.Inventory.GetQuantity(item) ?? 0;
            if (Rares.Is(item))
            {
                bool worn = Rares.Worn(game, item);
                if (owned <= 0 && !worn) continue;   // sold or never found: One Style keeps it in the game until a restart
                gear.Add(new PickEntry<Target>(item.Id, name, new Target(item.Id, name, ""), equip.Type.DisplayName.Text, "", item.Icon, equip.Type.Icon,
                    worn ? "worn" : $"{owned:N0} in bank", Rares.Tooltip(item)));
                continue;
            }
            gear.Add(new PickEntry<Target>(item.Id, name, new Target(item.Id, name, ""), equip.Type.DisplayName.Text, "", item.Icon, equip.Type.Icon,
                owned > 0 ? $"{owned:N0} in bank" : "", Muted: owned <= 0));
        }
        entries.AddRange(gear.OrderBy(g => g.Group, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Text, StringComparer.OrdinalIgnoreCase));
        return entries;
    }

    /// <summary>For the "go to a saved queue" step: the saved queues (not the automatic copy of unsaved steps).</summary>
    static List<PickEntry<Target>> SavedQueueEntries()
    {
        var entries = Queue.PresetNames.Where(n => !string.Equals(n, Queue.AutoName, StringComparison.OrdinalIgnoreCase))
            .Select(n => new PickEntry<Target>(n, n, new Target(n, n, ""), "Saved queues", Hint: $"{Queue.GetPreset(n)!.Steps.Count} step(s)")).ToList();
        if (entries.Count == 0)
            entries.Add(new PickEntry<Target>("", "No saved queues yet: save one under \"Saved queues\" first", new Target("", "", ""), Enabled: false, Muted: true));
        return entries;
    }

    /// <summary>Reads one goal from its controls (the step's own row, or the "or until" row).</summary>
    bool ReadGoal(Until kind, double amount, PickList<Item> item, OptionButton level, OptionButton node, OptionButton ev, PickList<string> task, out Goal goal)
    {
        goal = new Goal { Until = kind, Amount = amount };
        if (kind is Until.Item or Until.ItemLeft or Until.Made or Until.Spent)
        {
            if (item.Selected is not { } counted) { Say("Pick the item to count first."); return false; }
            goal.Subject = counted.Value.Id;
            goal.SubjectName = counted.Value.DisplayName.GetPluralText(1);
        }
        else if (kind == Until.Level)
        {
            if (level.Selected < 0) return false;
            goal.Subject = goal.SubjectName = _levelSkills[level.Selected];
        }
        else if (kind == Until.Nodes)
            goal.Subject = goal.SubjectName = node.Selected == 1 ? "major" : "minor";
        else if (kind == Until.Event)
        {
            if (ev.Selected < 0) { Say("This constellation has no events to wait for."); return false; }
            goal.Subject = ev.GetItemMetadata(ev.Selected).AsString();
            goal.SubjectName = ev.GetItemText(ev.Selected);
        }
        else if (kind == Until.Mastery)
        {
            if (task.SelectedKey is not { Length: > 0 } id) { Say("Pick the mastery task first."); return false; }
            if (Mastery.Find(id) is not { } chosen) { Say("That mastery task is not in this game: pick another one."); return false; }
            goal.Subject = id;
            goal.SubjectName = chosen.Text;
            goal.Amount = 1;
        }
        return true;
    }

    /// <summary>The lane new steps go to (1 when lanes are not shown).</summary>
    int NewLane => _lanePick.Visible ? _lanePick.Selected + 1 : 1;

    void AddStep()
    {
        if (Selling)
        {
            if (_item.Selected is not { } toSell) { Say("Pick the item to sell first."); return; }
            Step sale = Planner.SellStep(toSell.Value, (long)_amount.Value, NewLane);
            Put(sale);
            return;
        }
        if (Equipping)
        {
            if (_target.Selected is not { Enabled: true } gear || gear.Key == "") { Say("Choose what to equip first."); return; }
            bool loadout = EquipIsLoadout;
            bool stack = !loadout && _game != null && _game.Data.GetDataRegistryOutput<Item>().GetAllObjects().FirstOrDefault(i => i.Id == gear.Value.Id) is { } it
                && it.Capabilities.TryGet<GamesByMalcs.Melvor2.Components.Equipment.EquipmentCapability>(out var cap) && cap.CanEquipQuantity;
            var equip = new Step
            {
                Entity = Step.EquipEntity, TargetId = gear.Value.Id, Name = loadout ? $"Loadout: {gear.Text}" : $"Equip: {gear.Text}",
                Amount = stack ? _amount.Value : 0, Subject = stack ? "stack" : "", Lane = NewLane,
            };
            Put(equip);
            return;
        }
        if (Switching)
        {
            if (_target.Selected is not { Enabled: true } saved || saved.Key == "") { Say("Save a queue under \"Saved queues\" first, then choose it here."); return; }
            var go = new Step { Entity = Step.SwitchEntity, TargetId = saved.Value.Id, Name = $"Go to saved queue: {saved.Value.Id}", Lane = NewLane };
            if (_editing != null) { Put(go); return; }
            Queue.Add(go);
            Say($"Added: {go.Name}. When every step before it is over, the queue goes on with '{saved.Value.Id}'.");
            return;
        }
        if (_skillPick.Selected < 0 || _target.Selected is not { } chosen) { Say("Choose what to do first."); return; }
        GameEntity entity = _skills[_skillPick.Selected].Entity;
        Target target = chosen.Value;
        var step = new Step
        {
            Entity = entity.Id, TargetId = target.Id, Extra = target.Extra,
            Name = $"{entity.DisplayName.Text}: {target.Name}",
            Until = Kind, Amount = _amount.Value, Lane = NewLane,
        };
        if (_mode.Visible && _modeId != "")
        {
            step.Mode = _mode.ButtonPressed ? _modeId : "";
            if (_mode.ButtonPressed) step.Name += $" ({_mode.Text})";
        }
        if (!ReadGoal(step.Until, step.Amount, _item, _levelPick, _nodePick, _eventPick, _taskPick, out Goal main)) return;
        step.Subject = main.Subject;
        step.SubjectName = main.SubjectName;
        step.Amount = main.Amount;
        if (_orBox.Visible && _orBox.ButtonPressed)
        {
            if (!ReadGoal(OrKind, _orAmount.Value, _orItem, _orLevel, _orNode, _orEvent, _orTask, out Goal or)) return;
            step.Or = or;
        }
        Put(step);
    }

    /// <summary>
    /// With steps in lanes and "start over" on: one box per lane. Ticked, the lane starts over by itself whenever it is
    /// through; unticked, it runs once and stays finished while the other lanes go on.
    /// </summary>
    void PaintLaneRepeats()
    {
        List<int> lanes = Queue.LaneMode ? Queue.Steps.Select(s => Math.Clamp(s.Lane, 1, Queue.MaxLanes)).Distinct().OrderBy(l => l).ToList() : new List<int>();
        bool show = Queue.Repeat && lanes.Count > 1;
        _laneRepeats.Visible = show;
        string shown = show ? string.Join(",", lanes.Select(l => $"{l}{(Queue.LaneRepeats(l) ? "+" : "-")}")) : "";
        if (shown == _laneRepeatsShown) return;
        _laneRepeatsShown = shown;
        foreach (Node child in _laneRepeats.GetChildren()) child.QueueFree();
        if (!show) return;
        _laneRepeats.AddChild(Style.Label("Lanes that start over:", Style.Muted));
        foreach (int lane in lanes)
        {
            var box = new CheckBox
            {
                Text = $"Lane {lane}", ButtonPressed = Queue.LaneRepeats(lane),
                TooltipText = "Ticked: this lane starts over by itself whenever its last step is done.\nUnticked: it runs once and then stays finished; the other lanes go on.",
            };
            int which = lane;
            box.Toggled += on => { if (!_syncing) Queue.SetLaneRepeat(which, on); };
            _laneRepeats.AddChild(box);
        }
    }

    /// <summary>For tests: the lanes the window offers a "start over" box for, and ticking one.</summary>
    public List<string> LaneRepeatBoxes => _laneRepeats.Visible ? _laneRepeats.GetChildren().OfType<CheckBox>().Select(b => $"{b.Text}:{(b.ButtonPressed ? "on" : "off")}").ToList() : new List<string>();
    public void PressLaneRepeat(int lane)
    {
        foreach (CheckBox b in _laneRepeats.GetChildren().OfType<CheckBox>()) if (b.Text == $"Lane {lane}") b.ButtonPressed = !b.ButtonPressed;
    }

    /// <summary>A new step goes at the end; a step being edited goes back in its place.</summary>
    void Put(Step step)
    {
        int at = _editing == null ? -1 : Queue.Steps.IndexOf(_editing);
        if (at >= 0)
        {
            Queue.ReplaceAt(at, step);
            EndEdit();
            Say($"Step {at + 1} changed: {step.Name}, {step.Goal}.");
            return;
        }
        EndEdit();
        Queue.Add(step);
        Say($"Added: {step.Name}, {step.Goal}.");
    }

    void EndEdit()
    {
        _editing = null;
        _addButton.Text = "Add step";
        _cancelEdit.Visible = false;
    }

    /// <summary>The list's Edit: the selected step in the form, ready to be changed and saved back in its place.</summary>
    void EditSelected()
    {
        int index = SelectedIndex();
        if (index < 0 || index >= Queue.Steps.Count) { Say("Select a step in the list first, then press Edit."); return; }
        Step s = Queue.Steps[index];
        int skill = s.Entity switch
        {
            Step.SellEntity => _skills.Count,
            Step.EquipEntity => _skills.Count + 1,
            Step.SwitchEntity => _skills.Count + 2,
            _ => _skills.FindIndex(x => x.Entity.Id == s.Entity),
        };
        if (skill < 0) { Say("This step's skill is not on the list here, so it cannot be edited (remove it and add it again)."); return; }
        ShowStepForm();
        _skillPick.Select(skill);
        FillKinds(_untilPick, Astrology, withStops: true);
        FillKinds(_orPick, Astrology, withStops: false);
        FillTargets(Catalog.Key(s.TargetId, s.Extra), suggest: false);
        if (Selling) _item.Select(s.TargetId);
        else if (!Pseudo)
        {
            int kindAt = _untilPick.GetItemIndex((int)s.Until);
            if (kindAt >= 0) _untilPick.Select(kindAt);
        }
        UntilChanged();
        if (!Pseudo) LoadGoal(s, _item, _levelPick, _nodePick, _eventPick, _taskPick);
        if (!Pseudo) CapNodes(_amount, _nodePick, Kind);   // a node goal's cap is the chosen kind's (minor or major)
        _amount.Value = s.Amount;
        _orBox.ButtonPressed = !Pseudo && s.Or != null;
        if (!Pseudo && s.Or is { } or)
        {
            int orAt = _orPick.GetItemIndex((int)or.Until);
            if (orAt >= 0) _orPick.Select(orAt);
            OrChanged();
            LoadGoal(or, _orItem, _orLevel, _orNode, _orEvent, _orTask);
            CapNodes(_orAmount, _orNode, OrKind);
            _orAmount.Value = or.Amount;
        }
        if (_lanePick.Visible) _lanePick.Select(Math.Clamp(s.Lane, 1, _lanePick.ItemCount) - 1);
        if (_mode.Visible) _mode.ButtonPressed = !string.IsNullOrEmpty(s.Mode);
        _editing = s;
        _addButton.Text = $"Save step {index + 1}";
        _cancelEdit.Visible = true;
        Say($"Editing step {index + 1}: change it above and press \"Save step {index + 1}\" (or Cancel).");
    }

    /// <summary>Puts a goal's item, skill, node kind or event back into its pickers.</summary>
    void LoadGoal(IGoal g, PickList<Item> item, OptionButton level, OptionButton node, OptionButton ev, PickList<string> task)
    {
        switch (g.Until)
        {
            case Until.Item or Until.ItemLeft or Until.Made or Until.Spent:
                item.Select(g.Subject);
                break;
            case Until.Level:
                int at = _levelSkills.IndexOf(g.Subject);
                if (at >= 0) level.Select(at);
                break;
            case Until.Nodes:
                node.Select(g.Subject == "major" ? 1 : 0);
                break;
            case Until.Event:
                for (int i = 0; i < ev.ItemCount; i++)
                    if (ev.GetItemMetadata(i).AsString() == g.Subject) { ev.Select(i); break; }
                break;
            case Until.Mastery:
                if (!task.Select(g.Subject)) task.ShowMissing(g.Subject, g.SubjectName + " (not in this list)");
                break;
        }
    }

    /// <summary>For tests: the list's Edit on the step at <paramref name="index"/>, and the form's button text.</summary>
    public void EditStep(int index)
    {
        foreach (TreeItem row in StepRows()) if ((int)row.GetMetadata(0) == index) { row.Select(0); break; }
        EditSelected();
    }
    public string AddButtonText => _addButton.Text;

    // ---------------------------------------------------------------- the list

    int SelectedIndex() => _table.GetSelected() is { } row ? (int)row.GetMetadata(0) : -1;

    /// <summary>The step rows of the list, in order (under lane headings when there are lanes).</summary>
    IEnumerable<TreeItem> StepRows()
    {
        for (TreeItem? row = _table.GetRoot()?.GetFirstChild(); row != null; row = row.GetNext())
        {
            if ((int)row.GetMetadata(0) >= 0) yield return row;
            for (TreeItem? child = row.GetFirstChild(); child != null; child = child.GetNext()) yield return child;
        }
    }

    void NextLane()
    {
        int index = SelectedIndex();
        if (index < 0 || index >= Queue.Steps.Count) { Say("Select a step first."); return; }
        int lane = Math.Clamp(Queue.Steps[index].Lane, 1, Queue.MaxLanes) % LanesOffered() + 1;
        Queue.SetLane(index, lane);
        Say($"Step {index + 1} is now in lane {lane}.");
        SelectRow(index);
    }

    void PaintLaneButton()
    {
        int index = SelectedIndex();
        _laneButton.Text = index >= 0 && index < Queue.Steps.Count ? $"Lane {Math.Clamp(Queue.Steps[index].Lane, 1, Queue.MaxLanes)} > {Math.Clamp(Queue.Steps[index].Lane, 1, Queue.MaxLanes) % LanesOffered() + 1}" : "Change lane";
    }

    void MoveSelected(int by)
    {
        int index = SelectedIndex();
        int to = index < 0 ? -1 : Queue.MoveTo(index, by);   // in lanes it may jump over the steps of other lanes
        if (to >= 0) SelectRow(to);
    }

    void SelectRow(int index)
    {
        StepRows().FirstOrDefault(r => (int)r.GetMetadata(0) == index)?.Select(0);
        PaintLaneButton();
    }

    void Say(string text) => _status.Text = text;

    void Repaint()
    {
        int selected = SelectedIndex();
        _table.Clear();
        TreeItem root = _table.CreateItem();
        // With lanes: one heading per lane, its steps under it.
        bool laneMode = Queue.LaneMode;
        var headings = new Dictionary<int, TreeItem>();
        if (laneMode)
            foreach (int lane in Queue.Steps.Select(st => Math.Clamp(st.Lane, 1, Queue.MaxLanes)).Distinct().OrderBy(l => l))
            {
                TreeItem heading = _table.CreateItem(root);
                heading.SetMetadata(0, -1);
                var mine = Queue.Steps.Where(st => Math.Clamp(st.Lane, 1, Queue.MaxLanes) == lane).ToList();
                Step? at = mine.FirstOrDefault(st => st.Run != RunState.Done);
                heading.SetText(1, $"Lane {lane}");
                heading.SetText(3, !Queue.Running ? $"{mine.Count} step(s)" : at == null ? "finished" : $"at step {Queue.Steps.IndexOf(at) + 1}");
                for (int c = 0; c < 4; c++) { heading.SetCustomColor(c, Style.Accent); heading.SetSelectable(c, false); }
                headings[lane] = heading;
            }
        for (int i = 0; i < Queue.Steps.Count; i++)
        {
            Step step = Queue.Steps[i];
            bool current = Queue.Running && step.Run == RunState.Running;
            bool waiting = Queue.Running && step.Run == RunState.Waiting;
            TreeItem row = _table.CreateItem(laneMode ? headings[Math.Clamp(step.Lane, 1, Queue.MaxLanes)] : root);
            row.SetMetadata(0, i);
            row.SetText(0, (i + 1).ToString());
            row.SetText(1, step.Name);
            row.SetText(2, step.Goal);
            row.SetText(3, step.State);
            row.SetCustomColor(0, Style.Muted);
            row.SetCustomColor(3, step.State.StartsWith("skipped") ? Style.Bad : current ? Style.Good : waiting ? Style.Accent : Style.Muted);
            if (current)
                for (int c = 0; c < 4; c++) row.SetCustomBgColor(c, new Color(Style.GoodDim, 0.55f));
        }
        if (selected >= 0 && selected < Queue.Steps.Count) SelectRow(selected);

        _run.Text = Queue.Running ? "Pause" : "Start the queue";
        if (_editing != null && !Queue.Steps.Contains(_editing)) EndEdit();   // the step being edited is gone (removed, another queue)
        _syncing = true;
        _repeat.ButtonPressed = Queue.Repeat;
        _syncing = false;
        _repeat.Text = Queue.Repeat ? "Start over at the end: ON" : "Start over at the end: OFF";
        PaintLaneRepeats();
        PaintRoom();
        _syncing = true;
        _idleAlways.ButtonPressed = Queue.IdleAlways;
        _gameCreate.ButtonPressed = Queue.GameCreate;
        _syncing = false;
        PaintIdle();
        PaintStop();
        if (Queue.Note != "") Say(Queue.Note);
        else if (Queue.Running) Say(Queue.Headline);
        PaintQuick();
        if (_savedForm.Visible) PaintSaved();
        if (_planForm.Visible) Replan();   // the plan counts what the queue will make: keep it in step with the queue
    }
}
