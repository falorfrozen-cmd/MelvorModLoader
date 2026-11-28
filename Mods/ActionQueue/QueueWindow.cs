using System;
using System.Collections.Generic;
using System.Linq;
using CheatMenu;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Components.Actions;
using GamesByMalcs.Melvor2.Components.Toolbelt;
using GamesByMalcs.Melvor2.Data.Items;
using GamesByMalcs.Melvor2.Entities;
using Godot;

namespace ActionQueue;

/// <summary>The F6 window: add steps on top (one at a time, or a whole plan for an item), watch the queue run below.</summary>
public sealed class QueueWindow
{
    static string[] UntilNames => new[]
    {
        Lang.T("window.stops_by_itself", "it stops by itself"), Lang.T("window.i_own_many_item", "I own this many of an item"), Lang.T("window.skill_reaches_level", "a skill reaches this level"), Lang.T("window.many_minutes_have_passed", "this many minutes have passed"),
        Lang.T("window.only_many_item_left", "only this many of an item are left"), Lang.T("window.i_make_many_more_item", "I make this many more of an item"), Lang.T("window.has_been_done_many_times", "it has been done this many times"),
        Lang.T("window.has_used_up_many_item", "it has used up this many of an item"), Lang.T("window.node_unlocks_constellation_earned", "node unlocks of this constellation are earned"),
        Lang.T("window.astrology_event_has_been_seen", "an astrology event has been seen this many times"), Lang.T("window.mastery_task_complete", "a mastery task is complete"), Lang.T("window.food_runs_out", "the food runs out"),
        Lang.T("window.tool_runs_out", "a toolbelt item runs out"),
    };

    /// <summary>One theme for the window and for the popups of its pick lists (a popup is a window of its own).</summary>
    static readonly Theme s_theme = Style.Build();
    readonly CanvasLayer _layer = new() { Layer = 118, Name = "ActionQueue", Visible = false };
    readonly Label _status = Style.Label("", Style.Good);
    readonly Label _now = Style.Label("", Style.Muted, 12);
    readonly OptionButton _skillPick = new() { CustomMinimumSize = new Vector2(170, 0) };
    readonly PickList<Target> _target = new(Lang.T("window.choose_what_do", "Choose what to do"), s_theme);
    readonly OptionButton _untilPick = new() { CustomMinimumSize = new Vector2(250, 0) };
    readonly SpinBox _amount = new() { MinValue = 1, MaxValue = 100_000_000, Step = 1, Value = 1000, CustomMinimumSize = new Vector2(120, 0), SelectAllOnFocus = true };
    readonly PickList<Item> _item = new(Lang.T("window.choose_item", "Choose an item"), s_theme);
    readonly OptionButton _levelPick = new() { CustomMinimumSize = new Vector2(170, 0) };
    // Astrology goals: minor or major node unlocks, and the event to wait for.
    readonly OptionButton _nodePick = new() { CustomMinimumSize = new Vector2(110, 0) };
    readonly OptionButton _eventPick = new() { CustomMinimumSize = new Vector2(250, 0), FitToLongestItem = false, ClipText = true };
    // The other goals: "... or until ...". The step is over when any one is reached (0.15.0: up to three "or" rows).
    readonly CheckBox _orBox = new() { Text = Lang.T("window.label.or", "or") };

    /// <summary>One "or until" row: the controls of a goal of its own, like the until row's.</summary>
    sealed class GoalRow
    {
        public readonly HBoxContainer Row = new() { Visible = false };
        public readonly OptionButton Pick = new() { CustomMinimumSize = new Vector2(250, 0) };
        public readonly SpinBox Amount = new() { MinValue = 1, MaxValue = 100_000_000, Step = 1, Value = 100, CustomMinimumSize = new Vector2(120, 0), SelectAllOnFocus = true };
        public readonly PickList<Item> Item = new(Lang.T("window.choose_item", "Choose an item"), s_theme);
        public readonly OptionButton Level = new() { CustomMinimumSize = new Vector2(170, 0) };
        public readonly OptionButton Node = new() { CustomMinimumSize = new Vector2(110, 0) };
        public readonly OptionButton Event = new() { CustomMinimumSize = new Vector2(250, 0), FitToLongestItem = false, ClipText = true };
        public readonly PickList<string> Task = new(Lang.T("window.choose_mastery_task", "Choose a mastery task"), s_theme);
        public readonly PickList<Item> Tool = new(Lang.T("window.choose_toolbelt_item", "Choose a toolbelt item"), s_theme);
        /// <summary>"+ or" (another row) and "-" (this row away): shown on the last row in use.</summary>
        public readonly Button More = new() { Text = Lang.T("window.goal.add", "+ or") };
        public readonly Button Less = new() { Text = Lang.T("window.goal.remove", "-") };
        public Until Kind => Pick.Selected < 0 ? Until.Item : (Until)Pick.GetSelectedId();
    }

    readonly GoalRow[] _ors = Enumerable.Range(0, Step.MostGoals - 1).Select(_ => new GoalRow()).ToArray();
    /// <summary>How many "or until" rows are in use while "or" is ticked (1 up to three).</summary>
    int _orCount = 1;
    /// <summary>The "or until" rows in use (none while "or" is not ticked or not offered).</summary>
    IEnumerable<GoalRow> OrRows => _orBox.Visible && _orBox.ButtonPressed ? _ors.Take(_orCount) : Enumerable.Empty<GoalRow>();
    HBoxContainer _orRow => _ors[0].Row;
    OptionButton _orPick => _ors[0].Pick;
    SpinBox _orAmount => _ors[0].Amount;
    PickList<Item> _orItem => _ors[0].Item;
    OptionButton _orLevel => _ors[0].Level;
    OptionButton _orNode => _ors[0].Node;
    OptionButton _orEvent => _ors[0].Event;
    /// <summary>"Until this mastery task is complete" (0.12.0): the chosen skill's Mastery Guild tasks, a fight's: the combat skills'.</summary>
    readonly PickList<string> _taskPick = new(Lang.T("window.choose_mastery_task", "Choose a mastery task"), s_theme);
    PickList<string> _orTask => _ors[0].Task;
    /// <summary>The skill and target the task lists were last made for: a new one picks its own first task.</summary>
    string _tasksFor = "";
    /// <summary>The window was opened again: the progress next to each task is made again (once).</summary>
    bool _tasksStale = true;
    /// <summary>One line in the goal row for the steps that are not actions (equip a loadout, go to a saved queue).</summary>
    readonly Label _pseudoNote = Style.Label("", Style.Muted, 12);
    /// <summary>Equip, food picked: the skill that eats it (0.13.0, by madmousepad). Item metadata: the skill's entity id.</summary>
    readonly OptionButton _foodSkill = new() { Visible = false, CustomMinimumSize = new Vector2(150, 0) };
    /// <summary>The list key of a food in the Equip list (food is no gear: its own keys).</summary>
    const string FoodKeyPrefix = "food:";
    /// <summary>The list key of a toolbelt item in the Equip list (0.17.0; an item can be gear and a toolbelt item: its own keys).</summary>
    public const string ToolKeyPrefix = "toolbelt:";
    /// <summary>"Until no X is left in the toolbelt" (0.17.0): the chosen skill's toolbelt items that get used up.</summary>
    readonly PickList<Item> _toolPick = new(Lang.T("window.choose_toolbelt_item", "Choose a toolbelt item"), s_theme);
    /// <summary>Each skill's toolbelt items that get used up, by entity id (made when the window meets a character).</summary>
    Dictionary<string, List<Item>> _usedUp = new();
    /// <summary>The skill the toolbelt item lists were last made for ("" after the window opened again: made afresh).</summary>
    string _toolsFor = "";
    /// <summary>The skill they were made for, also across a reopened window (a step being edited keeps its item then).</summary>
    string _toolsSkill = "";
    readonly Tree _table;
    Button _runFrom = null!;
    readonly Button _run = new() { CustomMinimumSize = new Vector2(150, 0) };
    readonly Button _repeat = new() { ToggleMode = true, CustomMinimumSize = new Vector2(205, 0) };
    /// <summary>With steps in lanes and "start over" on: which lanes start over (a lane can also run once).</summary>
    readonly HBoxContainer _laneRepeats = new() { Visible = false };
    string _laneRepeatsShown = "";
    readonly Button _side = new() { ToggleMode = true, CustomMinimumSize = new Vector2(175, 0), Visible = false };
    // Lanes: which lane a new step (or plan) goes to, and moving the selected step to another lane.
    readonly OptionButton _lanePick = new() { Visible = false }, _planLanePick = new() { Visible = false };
    readonly Button _laneButton = new() { Visible = false, CustomMinimumSize = new Vector2(120, 0) };
    readonly CheckBox _planSell = new() { Text = Lang.T("window.then_sell_them", "then sell them") };
    /// <summary>The planner: every material from nothing (the bank not counted), for a queue that starts over.</summary>
    readonly CheckBox _planAll = new() { Text = Lang.T("window.every_material", "every material") };
    bool _planAllSet;
    /// <summary>A recipe's other way of making for this step (Smithing's Sacrificial Anvil); shown only where the game offers one.</summary>
    readonly CheckBox _mode = new() { Visible = false };
    string _modeId = "";
    readonly Label _untilLabel = Style.Label(Lang.T("window.label.until", "until"), Style.Text);
    /// <summary>The Do row asks how many to keep (a sale, an equip stack): for tests, which must not read the label's words.</summary>
    bool _keepShown;
    Button _useNow = null!;
    // Editing a step in place: the form shows the step, and its button saves it back where it was.
    Button _addButton = null!;
    readonly Button _cancelEdit = new() { Text = Lang.T("window.cancel", "Cancel"), Visible = false };
    Step? _editing;

    // "When the queue waits or ends, do": an action for the idle time.
    readonly OptionButton _idleSkill = new() { CustomMinimumSize = new Vector2(170, 0) };
    readonly PickList<Target> _idleTarget = new(Lang.T("window.choose_what_do", "Choose what to do"), s_theme);
    readonly Label _idleState = Style.Label("", Style.Muted, 12);
    readonly CheckBox _idleAlways = new() { Text = Lang.T("window.also_when_queue_not_running", "Also when the queue is not running") };
    readonly CheckBox _gameCreate = new() { Text = Lang.T("window.game_create_button_adds_queue", "The game's Create button adds to the queue") };

    // "Stop the whole queue when ...": the queue's own stop (0.12.0).
    readonly OptionButton _stopPick = new() { CustomMinimumSize = new Vector2(250, 0) };
    readonly SpinBox _stopAmount = new() { MinValue = 1, MaxValue = Queue.MaxStopAmount, Step = 1, Value = 1, CustomMinimumSize = new Vector2(110, 0), SelectAllOnFocus = true };
    readonly PickList<Item> _stopItem = new(Lang.T("window.choose_item", "Choose an item"), s_theme);
    readonly OptionButton _stopLevel = new() { CustomMinimumSize = new Vector2(170, 0) };
    /// <summary>The queue's stop "a mastery task is complete": every skill's tasks (filled when the kind is shown).</summary>
    readonly PickList<string> _stopTask = new(Lang.T("window.choose_mastery_task", "Choose a mastery task"), s_theme);
    bool _stopTasksFilled;
    /// <summary>The queue's stop the controls were last set from (they are not reset while the player is still picking).</summary>
    string _stopShown = "?";
    /// <summary>The id of "never" in the stop list (Godot gives an item added with -1 its index as id).</summary>
    const int NeverId = 99;
    /// <summary>When the stop is looked at: at once (0) or when a round is over (1). 0.13.0.</summary>
    readonly OptionButton _stopTiming = new() { Visible = false };
    /// <summary>"When the queue ends, go on with": nothing, or a saved queue (0.13.0, by madmousepad).</summary>
    readonly OptionButton _thenPick = new() { CustomMinimumSize = new Vector2(260, 0), FitToLongestItem = false, ClipText = true };
    string _thenShown = "?";

    // The card on top has two faces: the form for one step, and the planner.
    readonly Button _stepMode = new() { Text = Lang.T("window.add_one_step", "Add one step"), ToggleMode = true };
    readonly Button _planMode = new() { Text = Lang.T("window.plan_item", "Plan an item"), ToggleMode = true };
    readonly VBoxContainer _stepForm = new(), _planForm = new() { Visible = false };
    readonly OptionButton _planKind = new() { CustomMinimumSize = new Vector2(132, 0) };
    readonly SpinBox _planAmount = new() { MinValue = 1, MaxValue = 100_000_000, Step = 1, Value = 100, CustomMinimumSize = new Vector2(120, 0), SelectAllOnFocus = true };
    readonly PickList<Item> _planPick = new(Lang.T("window.choose_item_plan", "Choose an item to plan"), s_theme);
    readonly Label _planNote = Style.Label("", Style.Muted, 12);
    readonly Button _planAdd = new() { Text = Lang.T("window.add_these_steps_queue", "Add these steps to the queue") };
    Tree _planTable = null!;
    List<Item> _makeable = new();
    PlanResult? _plan;
    string _planShown = "";

    // The third face of the card: queues kept under a name, and share codes.
    readonly Button _savedMode = new() { Text = Lang.T("window.saved_queues", "Saved queues"), ToggleMode = true };
    /// <summary>The queue's language (0.14.0): shown only when a language file is next to the mod.</summary>
    readonly OptionButton _language = new() { FitToLongestItem = true };
    readonly VBoxContainer _savedForm = new() { Visible = false };
    readonly LineEdit _presetName = new() { PlaceholderText = Lang.T("window.name_e_g_smithing_day", "Name, e.g. Smithing day"), CustomMinimumSize = new Vector2(260, 0), MaxLength = 40 };
    readonly LineEdit _code = new() { PlaceholderText = Lang.T("window.share_code_aq", "Share code (AQ1:...)"), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true };
    readonly Button _deletePreset = new() { Text = Lang.T("window.delete", "Delete") };
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
        // Making it bigger (0.13.0, by madmousepad): the right edge widens it, the corner grip widens and heightens it.
        _edgeGrip = new Control { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Hsize,
            TooltipText = Lang.T("window.drag_make_window_wider_double", "Drag to make the window wider. Double-click: back to its normal size.") };
        _cornerGrip = new Control { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Fdiagsize,
            Size = new Vector2(CornerSize, CornerSize), TooltipText = Lang.T("window.drag_make_window_bigger_double", "Drag to make the window bigger. Double-click: back to its normal size.") };
        Control corner = _cornerGrip;
        corner.Draw += () =>
        {
            const float edge = CornerSize - 4;
            for (int length = 4; length <= 12; length += 4)
                corner.DrawLine(new Vector2(edge - length, edge), new Vector2(edge, edge - length), Style.Muted, 1.25f, true);
        };
        _edgeGrip.GuiInput += e => DragGrip(e, tall: false);
        _cornerGrip.GuiInput += e => DragGrip(e, tall: true);
        _holder.AddChild(_edgeGrip);
        _holder.AddChild(_cornerGrip);
        panel.Resized += Place;
        panel.MinimumSizeChanged += Place;
        _holder.Resized += Place;
        ReadLayout();

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        panel.AddChild(root);

        // ---- header
        StyleBoxFlat headBox = Style.Box(Style.Sidebar, 0, null, 0, 18, 12);
        headBox.CornerRadiusTopLeft = headBox.CornerRadiusTopRight = 7;
        headBox.BorderColor = Style.Line;
        headBox.BorderWidthBottom = 1;
        var head = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.Move,
            TooltipText = Lang.T("window.drag_here_move_window_double", "Drag here to move the window. Double-click to put it back in the middle.") };
        head.AddThemeStyleboxOverride("panel", headBox);
        head.GuiInput += DragTitle;
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        header.AddChild(Style.Label(Lang.T("window.action_queue", "ACTION QUEUE"), Style.Accent, 20));
        Label hint = Style.Label(Lang.T("window.do_then_then", "Do this, then that, then that."), Style.Muted);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.SizeFlagsVertical = Control.SizeFlags.Fill;
        hint.ClipText = true;
        header.AddChild(hint);
        _language.TooltipText = Lang.T("window.language_queue_window_messages_names", "The language of the queue's window and messages. Names that come from the game stay as the game writes them.");
        _language.ItemSelected += LanguagePicked;
        header.AddChild(_language);
        FillLanguages();
        StyleBoxFlat go = Style.Box(Style.GoodDim, 5, Style.Good, 1, 12, 5);
        _run.AddThemeStyleboxOverride("normal", go);
        _run.AddThemeStyleboxOverride("hover", Style.Box(new Color(0.16f, 0.38f, 0.23f), 5, Style.Good, 1, 12, 5));
        _run.Pressed += () => { if (Queue.Running) Queue.Pause(Lang.T("window.paused_your_character_goes_with", "Paused. Your character goes on with what it was doing; press Start to carry on with the queue.")); else Queue.Start(); };
        _quick.TooltipText = Lang.T("window.switch_one_your_saved_queues", "Switch to one of your saved queues. A running queue keeps running, from the first step of the new one.");
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
        _stepMode.TooltipText = Lang.T("window.choose_one_action_when_over", "Choose one action and when it is over.");
        _planMode.TooltipText = Lang.T("window.say_which_item_you_want", "Say which item you want and how many. The steps to get there are worked out for you.");
        _savedMode.TooltipText = Lang.T("window.keep_several_queues_under_name", "Keep several queues under a name, switch between them, and share one as a short code.");
        foreach (Button mode in new[] { _stepMode, _planMode, _savedMode }) mode.Toggled += _ => ShowFace();
        Label modeHint = Style.Label("", Style.Muted, 12);
        modeHint.Text = Lang.T("window.build_queue_step_by_step", "Build the queue step by step, let a plan work the steps out, or keep whole queues under a name.");
        modeHint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        modeHint.HorizontalAlignment = HorizontalAlignment.Right;
        modeHint.ClipText = true;
        modes.AddChild(modeHint);
        form.AddChild(modes);
        _stepForm.AddThemeConstantOverride("separation", 8);

        var what = new HBoxContainer();
        what.AddThemeConstantOverride("separation", 10);
        what.AddChild(Fixed(Lang.T("window.label.do", "Do"), 50));
        _skillPick.ItemSelected += _ => { FillKinds(_untilPick, withStops: true); FillOrKinds(); FillTargets(null); UntilChanged(); };
        _target.Picked += _ =>
        {
            if (Pseudo) { UntilChanged(); return; }
            if (Kind == Until.ItemLeft) SuggestFood(); else SuggestItem();
            ShowMode();
            FillEvents();
            FillTasks();
            CapNodes(_amount, _nodePick, Kind);
            foreach (GoalRow r in _ors) CapNodes(r.Amount, r.Node, r.Kind);
        };
        _target.Control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _target.Control.CustomMinimumSize = new Vector2(300, 0);
        _target.Control.TooltipText = Lang.T("window.click_type_search_groups_ones", "Click and type to search. The groups are the ones of the skill's own page.");
        what.AddChild(_skillPick);
        what.AddChild(_target.Control);
        _mode.TooltipText = Lang.T("window.tick_step_makes_recipe_other", "Tick it and this step makes the recipe this other way (as the switch under the recipe on the skill's page);\nuntick it and the step makes it the normal way. The queue sets the game's switch when the step starts.");
        what.AddChild(_mode);
        Button useNow = _useNow = MakeButton(Lang.T("window.use_what_i_am_doing", "Use what I am doing now"), UseCurrent);
        useNow.TooltipText = Lang.T("window.fills_skill_action_your_character", "Fills in the skill and the action your character is performing right now.");
        what.AddChild(useNow);
        _stepForm.AddChild(what);

        var until = new HBoxContainer();
        until.AddThemeConstantOverride("separation", 10);
        _untilLabel.CustomMinimumSize = new Vector2(50, 0);
        until.AddChild(_untilLabel);
        FillKinds(_untilPick, withStops: true);
        _untilPick.ItemSelected += _ => UntilChanged();
        until.AddChild(_untilPick);
        until.AddChild(_amount);
        _item.Control.CustomMinimumSize = new Vector2(280, 0);
        until.AddChild(_item.Control);
        until.AddChild(_levelPick);
        foreach (OptionButton nodes in _ors.Select(r => r.Node).Prepend(_nodePick))
        {
            nodes.AddItem(Step.NodeKind("minor", "minor"));
            nodes.AddItem(Step.NodeKind("major", "major"));
            nodes.ItemSelected += _ => { CapNodes(_amount, _nodePick, Kind); foreach (GoalRow r in _ors) CapNodes(r.Amount, r.Node, r.Kind); };
            nodes.TooltipText = Lang.T("window.minor_node_unlocks_constellation_knowledge", "Minor: the node unlocks the constellation's Knowledge has earned (the ones you already clicked count too).\nMajor: one for every few minor ones, as the game gives them; you do not have to click the minor nodes for it.");
        }
        until.AddChild(_nodePick);
        _eventPick.TooltipText = Lang.T("window.events_constellation_can_show_while", "The events this constellation can show while it is studied. The step counts the ones seen after it starts.");
        until.AddChild(_eventPick);
        foreach (GoalRow r in _ors) r.Event.TooltipText = _eventPick.TooltipText;
        foreach (PickList<string> tasks in _ors.Select(r => r.Task).Prepend(_taskPick))
        {
            tasks.Control.CustomMinimumSize = new Vector2(400, 0);
            tasks.HintWidth = 150;   // the count ("Easy: 201 / 2,000") stays readable when a long task's words are cut
            tasks.Control.TooltipText = Lang.T("window.mastery_guild_tasks_skill_fight", "The Mastery Guild's tasks of this skill (a fight: of the combat skills). Click and type to search.\nThe ones for the chosen target come first. The step is over when the game marks the task complete.");
        }
        until.AddChild(_taskPick.Control);
        foreach (PickList<Item> tools in _ors.Select(r => r.Tool).Prepend(_toolPick))
        {
            tools.Control.CustomMinimumSize = new Vector2(300, 0);
            tools.HintWidth = 140;   // ("25 in the toolbelt" stays readable)
            tools.Control.TooltipText = Lang.T("window.toolbelt_goal_tip", "A potion, oven mitts or another toolbelt item this skill uses up. The step is over once none of it is left in the toolbelt\n(the game takes the last one out of its slot). The ones in the toolbelt now come first.");
        }
        until.AddChild(_toolPick.Control);
        foreach (PickList<string> tasks in _ors.Select(r => r.Task).Prepend(_taskPick))
            tasks.Picked += e =>
            {
                if (Mastery.Find(e.Key) is { Done: true })
                    Say(Lang.T("window.task_already_complete_character_so", "That task is already complete on this character, so a step waiting for it ends at once."));
            };
        _foodSkill.TooltipText = Lang.T("window.skill_eats_food_step_puts", "The skill that eats this food: the step puts it in that skill's own food box (the fight's, Thieving's, Agility's...).");
        until.AddChild(_foodSkill);
        until.AddChild(_pseudoNote);
        _orBox.TooltipText = Lang.F("window.goal.or_tip", "Give the step more goals (up to {most}, \"+ or\" adds one): it is over when any one is reached\n(e.g. until a Gold Topaz Ring is in the bank, or until 780 Salmon are used up).", ("most", Step.MostGoals));
        _orBox.Toggled += on => { if (!on) _orCount = 1; UntilChanged(); };
        until.AddChild(_orBox);
        until.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        OfferLanes(LanesOffered());
        _lanePick.TooltipText = _planLanePick.TooltipText =
            Lang.T("window.lanes_queues_their_own_run", "Lanes are queues of their own that run side by side (with the Multitask mod's slots on).\nEvery lane does its steps one after another; e.g. lane 1 cuts logs and burns them, lane 2 fishes and cooks.\nWith every step in lane 1 the queue works as one list.");
        _lanePick.ItemSelected += i => _planLanePick.Select((int)i);
        _planLanePick.ItemSelected += i => { _lanePick.Select((int)i); _planShown = ""; Replan(); };
        until.AddChild(_lanePick);
        _addButton = MakeButton(Lang.T("window.add_step", "Add step"), AddStep);
        until.AddChild(_addButton);
        _cancelEdit.TooltipText = Lang.T("window.stop_editing_step_stays_as", "Stop editing: the step stays as it was.");
        _cancelEdit.Pressed += () => { EndEdit(); Say(Lang.T("window.nothing_was_changed", "Nothing was changed.")); };
        until.AddChild(_cancelEdit);
        _stepForm.AddChild(until);
        // -- the other goals, under the first (0.15.0: up to three rows, "+ or" adds one)
        foreach (GoalRow r in _ors)
        {
            GoalRow row = r;
            row.Row.AddThemeConstantOverride("separation", 10);
            Label orLabel = Style.Label(Lang.T("window.label.or_until", "or until"), Style.Text);
            orLabel.CustomMinimumSize = new Vector2(50, 0);
            row.Row.AddChild(orLabel);
            FillKinds(row.Pick, withStops: false);
            row.Pick.ItemSelected += _ => { OrChanged(row); FillTasks(); FillTools(); };
            row.Row.AddChild(row.Pick);
            row.Row.AddChild(row.Amount);
            row.Item.Control.CustomMinimumSize = new Vector2(280, 0);
            row.Row.AddChild(row.Item.Control);
            row.Row.AddChild(row.Level);
            row.Row.AddChild(row.Node);
            row.Row.AddChild(row.Event);
            row.Row.AddChild(row.Task.Control);
            row.Row.AddChild(row.Tool.Control);
            row.Row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            row.More.TooltipText = Lang.F("window.goal.add_tip", "Another goal for this step: it is over when any one of its goals is reached (up to {most} goals).", ("most", Step.MostGoals));
            row.More.Pressed += () =>
            {
                _orCount = Math.Min(_ors.Length, _orCount + 1);
                GoalRow added = _ors[_orCount - 1];
                if (added.Pick.ItemCount > 0) added.Pick.Select(0);
                added.Amount.Value = 100;
                if (_levelPick.Selected >= 0 && _levelPick.Selected < added.Level.ItemCount) added.Level.Select(_levelPick.Selected);
                ShowOrRows();
                FillEvents();
                FillTasks();
            };
            row.Row.AddChild(row.More);
            row.Less.TooltipText = Lang.T("window.goal.remove_tip", "Takes this goal away.");
            row.Less.Pressed += () => { _orCount = Math.Max(1, _orCount - 1); ShowOrRows(); };
            row.Row.AddChild(row.Less);
            _stepForm.AddChild(row.Row);
        }
        _stepForm.AddChild(_now);
        form.AddChild(_stepForm);
        BuildPlanForm(form);
        BuildSavedForm(form);

        // -- the queue
        _table = new Tree
        {
            Columns = 6, HideRoot = true, SelectMode = Tree.SelectModeEnum.Row, ColumnTitlesVisible = true, AllowRmbSelect = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 150)
        };
        // Time (0.13.1, asked for on Nexus): how long the step has been running, or took.
        // Est. left: the estimated time a step still needs, from the planner's per-action times.
        string[] titles = { "#", Lang.T("window.label.do", "Do"), Lang.T("window.column.until", "Until"), Lang.T("window.column.state", "State"), Lang.T("window.column.time", "Time"), Lang.T("window.column.left", "Est. left") };
        for (int c = 0; c < titles.Length; c++)
        {
            _table.SetColumnTitle(c, titles[c]);
            _table.SetColumnTitleAlignment(c, c >= 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            // a long name or goal is cut short (its tooltip has it whole) instead of widening its column past what the player set
            _table.SetColumnClipContent(c, c != 0);
        }
        _table.SetColumnCustomMinimumWidth(0, 44);
        ApplyColumns();
        AddColumnDividers();
        _table.ItemSelected += PaintLaneButton;
        // Right-click on a step: run the queue from it, or edit it (0.13.0).
        _stepMenu = new PopupMenu { Theme = s_theme };
        _stepMenu.AddItem(Lang.T("window.run_from_here", "Run from here"), 0);
        _stepMenu.AddItem(Lang.T("window.edit", "Edit"), 1);
        _stepMenu.IdPressed += id => { if (id == 0) RunFromSelected(); else EditSelected(); };
        _table.AddChild(_stepMenu);
        _table.ItemMouseSelected += (position, button) =>
        {
            if (button != (long)MouseButton.Right || SelectedIndex() < 0) return;
            PaintLaneButton();
            _stepMenu.Popup(new Rect2I((Vector2I)(_table.GetScreenPosition() + position), Vector2I.Zero));
        };
        body.AddChild(_table);

        // -- the idle action
        var idleRow = new HBoxContainer();
        idleRow.AddThemeConstantOverride("separation", 10);
        Label idleLabel = Style.Label(Lang.T("window.when_queue_waits_ends_do", "When the queue waits or ends, do"), Style.Muted);
        idleLabel.MouseFilter = Control.MouseFilterEnum.Stop;
        idleLabel.TooltipText = Lang.T("window.your_character_never_stands_still", "Your character never stands still: this runs while every step that is left is waiting\n(for materials, for its skill to be free...) and goes on after the queue has finished.\nIt only takes a free slot, never one of your own actions, and stops when a step needs the place.");
        idleRow.AddChild(idleLabel);
        _idleSkill.TooltipText = idleLabel.TooltipText;
        _idleSkill.ItemSelected += i => { if (!_syncing) { if (i <= 0) Queue.SetIdle(null); else { FillIdleTargets(null); PickIdle(); } } };
        _idleTarget.Picked += _ => PickIdle();
        _idleTarget.Control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _idleTarget.Control.CustomMinimumSize = new Vector2(220, 0);
        idleRow.AddChild(_idleSkill);
        idleRow.AddChild(_idleTarget.Control);
        Button idleNow = MakeButton(Lang.T("window.use_what_i_am_doing", "Use what I am doing now"), UseCurrentForIdle);
        idleNow.TooltipText = Lang.T("window.uses_skill_action_your_character", "Uses the skill and the action your character is performing right now.");
        idleRow.AddChild(idleNow);
        body.AddChild(idleRow);
        var optionRow = new HBoxContainer();
        optionRow.AddThemeConstantOverride("separation", 24);
        _idleAlways.TooltipText = Lang.T("window.idle_action_above_also_runs", "ON: the idle action above also runs while the queue is paused, finished or not used at all,\nwhenever your character does nothing. Press Stop on it in the game and it waits until you start something yourself.\nOFF: it only runs while the queue runs and after the queue has finished.");
        _idleAlways.Toggled += on => { if (!_syncing) Queue.SetIdleAlways(on); };
        optionRow.AddChild(_idleAlways);
        _gameCreate.TooltipText = Lang.T("window.smithing_crafting_fletching_runecrafting_beastcrafting", "ON: on the Smithing, Crafting, Fletching, Runecrafting, Beastcrafting, Cooking and Herblore pages,\nthe game's Create button (it then reads \"Add to queue\") puts the recipe at the end of the queue, with the\nstop option you chose there. With nothing left in the queue, it starts the recipe through the queue.\nThe \"+ Queue\" button next to it always adds to the queue, even when you lack the materials yet.");
        _gameCreate.Toggled += on => { if (!_syncing) Queue.SetGameCreate(on); };
        optionRow.AddChild(_gameCreate);
        body.AddChild(optionRow);
        _idleState.Visible = false;
        body.AddChild(_idleState);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        buttons.AddChild(Style.Label(Lang.T("window.selected_step", "Selected step:"), Style.Muted));
        buttons.AddChild(MakeButton(Lang.T("window.move_up", "Move up"), () => MoveSelected(-1)));
        buttons.AddChild(MakeButton(Lang.T("window.move_down", "Move down"), () => MoveSelected(1)));
        _runFrom = MakeButton(Lang.T("window.run_from_here", "Run from here"), RunFromSelected);
        _runFrom.TooltipText = Lang.T("window.queue_goes_from_selected_step", "The queue goes on from the selected step: the steps before it are skipped this round,\nthis step and the ones after it start afresh. In lanes only that lane jumps. A queue that is not running starts from it.\nAlso on the right-click menu of a step.");
        _runFrom.Disabled = true;
        buttons.AddChild(_runFrom);
        Button edit = MakeButton(Lang.T("window.edit", "Edit"), EditSelected);
        edit.TooltipText = Lang.T("window.shows_selected_step_form_above", "Shows the selected step in the form above. Change what you like and press \"Save step\": it stays in its place.");
        buttons.AddChild(edit);
        buttons.AddChild(MakeButton(Lang.T("window.remove", "Remove"), () => { int i = SelectedIndex(); if (i >= 0) Queue.RemoveAt(i); }));
        _laneButton.TooltipText = Lang.T("window.puts_selected_step_into_next", "Puts the selected step into the next lane (after the last lane it goes back to lane 1).");
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
        _side.TooltipText = Lang.T("window.with_multitask_mod_slots_switched", "With the Multitask mod's slots switched on, several steps run at the same time (the fish are cooked while more are caught).\nOFF: one step after another, as without Multitask.");
        _side.Toggled += pressed => { if (!_syncing) Queue.SetSideBySide(pressed); };
        buttons.AddChild(_side);
        buttons.AddChild(_repeat);
        buttons.AddChild(MakeButton(Lang.T("window.remove_all", "Remove all"), Queue.Clear));
        body.AddChild(buttons);
        _laneRepeats.AddThemeConstantOverride("separation", 12);
        _laneRepeats.Alignment = BoxContainer.AlignmentMode.End;
        body.AddChild(_laneRepeats);

        // -- the queue's own stop
        var stopRow = new HBoxContainer();
        stopRow.AddThemeConstantOverride("separation", 10);
        Label stopLabel = Style.Label(Lang.T("window.stop_whole_queue_when", "Stop the whole queue when"), Style.Muted);
        stopLabel.MouseFilter = Control.MouseFilterEnum.Stop;
        stopLabel.TooltipText = Lang.T("window.once_reached_every_step_stops", "Once this is reached, every step stops and the queue ends, also with \"Start over at the end\" on\nand in lanes. The action under \"When the queue waits or ends, do\" then takes over (or the saved queue below).\nE.g. thieve, fish and cook round after round until 1 Bandit Gloves is in the bank, then stop.\n\"at once\" stops in the middle of a step; \"when a round is over\" lets the round finish first (the sale at the end still happens).");
        stopRow.AddChild(stopLabel);
        _stopPick.TooltipText = stopLabel.TooltipText;
        _stopPick.AddItem(Lang.T("window.never_steps_decide", "never: the steps decide"), NeverId);
        _stopPick.AddItem(Lang.T("window.i_own_many_item", "I own this many of an item"), (int)Until.Item);
        _stopPick.AddItem(Lang.T("window.skill_reaches_level", "a skill reaches this level"), (int)Until.Level);
        _stopPick.AddItem(Lang.T("window.only_many_item_left", "only this many of an item are left"), (int)Until.ItemLeft);
        _stopPick.AddItem(Lang.T("window.mastery_task_complete", "a mastery task is complete"), (int)Until.Mastery);
        _stopPick.ItemSelected += _ => { if (_syncing) return; StopKindShown(fresh: true); ApplyStop(fresh: true); };
        stopRow.AddChild(_stopPick);
        _stopAmount.ValueChanged += _ => { if (!_syncing) ApplyStop(); };
        stopRow.AddChild(_stopAmount);
        _stopItem.Control.CustomMinimumSize = new Vector2(240, 0);
        _stopItem.Control.TooltipText = Lang.T("window.click_type_search", "Click and type to search.");
        _stopItem.Picked += _ => { if (!_syncing) ApplyStop(); };
        stopRow.AddChild(_stopItem.Control);
        _stopLevel.ItemSelected += _ => { if (!_syncing) ApplyStop(); };
        stopRow.AddChild(_stopLevel);
        // (narrower than 0.12's 480 to leave room for the stop's timing; it takes what a wider window gives)
        _stopTask.Control.CustomMinimumSize = new Vector2(300, 0);
        _stopTask.Control.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _stopTask.HintWidth = 120;
        _stopTask.Control.TooltipText = Lang.T("window.every_skill_mastery_guild_tasks", "Every skill's Mastery Guild tasks (the skills of this queue first). Click and type to search.");
        _stopTask.Picked += _ => { if (!_syncing) ApplyStop(); };
        stopRow.AddChild(_stopTask.Control);
        _stopTiming.AddItem(Lang.T("window.at_once", "at once"), 0);
        _stopTiming.AddItem(Lang.T("window.when_round_over", "when a round is over"), 1);
        _stopTiming.TooltipText = Lang.T("window.at_once_queue_stops_moment", "At once: the queue stops the moment this is true, in the middle of a step.\nWhen a round is over: the queue finishes the round it is in first (in lanes: a lane that is through does not start over),\nso the steps at the end of the round, a sale or an equip, still happen. Also when it is true already as the queue starts.");
        _stopTiming.ItemSelected += i =>
        {
            if (_syncing) return;
            _stopShown = ShownKey(Queue.StopWhen, i == 1);   // (before the queue is told: its change repaints the window)
            Queue.SetStopAtEnd(i == 1);
            if (Queue.StopWhen is { } g) Say(StopSaid(g));
        };
        stopRow.AddChild(_stopTiming);
        body.AddChild(stopRow);
        StopKindShown(fresh: false);

        // -- what comes after the queue (0.13.0, by madmousepad)
        var thenRow = new HBoxContainer();
        thenRow.AddThemeConstantOverride("separation", 10);
        Label thenLabel = Style.Label(Lang.T("window.when_queue_ends_go_with", "When the queue ends, go on with"), Style.Muted);
        thenLabel.MouseFilter = Control.MouseFilterEnum.Stop;
        thenLabel.TooltipText = Lang.T("window.saved_queue_start_from_first", "A saved queue to start from its first step once this queue has ended: its steps are done\n(\"Start over at the end\" off), or its own stop above was reached. Pausing, emptying or stopping it yourself does not count.\nA queue never starts itself again this way. Nothing: the idle action above takes over, as before.");
        thenRow.AddChild(thenLabel);
        _thenPick.TooltipText = thenLabel.TooltipText;
        _thenPick.ItemSelected += i => { if (!_syncing) PickThen((int)i); };
        thenRow.AddChild(_thenPick);
        body.AddChild(thenRow);

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
        _planKind.AddItem(Lang.T("window.plan.make", "Make"));
        _planKind.AddItem(Lang.T("window.own_total", "Own in total"));
        _planKind.TooltipText = Lang.T("window.make_many_more_than_you", "Make: this many more than you have now.\nOwn in total: until the bank holds this many (what you already have counts).");
        _planKind.ItemSelected += _ => { _planShown = ""; Replan(); };
        row.AddChild(_planKind);
        _planAmount.ValueChanged += _ => Replan();
        row.AddChild(_planAmount);
        row.AddChild(Style.Label(Lang.T("window.plan.of", "of"), Style.Muted));
        _planPick.Control.TooltipText = Lang.T("window.every_item_one_your_skills", "Every item one of your skills can make or gather. Click and type to search.");
        _planPick.Control.CustomMinimumSize = new Vector2(340, 0);
        _planPick.Picked += _ => { _planShown = ""; Replan(); };
        row.AddChild(_planPick.Control);
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        StyleBoxFlat go = Style.Box(Style.GoodDim, 5, Style.Good, 1, 12, 5);
        _planAdd.AddThemeStyleboxOverride("normal", go);
        _planAdd.AddThemeStyleboxOverride("hover", Style.Box(new Color(0.16f, 0.38f, 0.23f), 5, Style.Good, 1, 12, 5));
        _planAdd.Pressed += AddPlan;
        _planSell.TooltipText = Lang.T("window.adds_last_step_sells_what", "Adds a last step that sells what the plan made (with \"Make\", what you had before is kept).");
        _planAll.TooltipText = Lang.T("window.ticked_bank_not_counted_every", "Ticked: the bank is not counted. Every material gets its step, for the whole amount, so a queue that\nstarts over makes one round's worth each time (nothing is skipped and no surplus piles up).\nUnticked: what the bank already holds is used, and only what is missing is made.\nTicked by itself while \"Start over at the end\" is on.");
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
        string[] titles = { Lang.T("window.column.step", "Step"), Lang.T("window.until_bank_holds", "Until the bank holds"), Lang.T("window.bank_3", "In the bank"), Lang.T("window.takes_about", "Takes about") };
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
        top.AddChild(Fixed(Lang.T("window.label.name", "Name"), 50));
        _presetName.TextSubmitted += _ => SaveNamed();
        top.AddChild(_presetName);
        Button save = MakeButton(Lang.T("window.save_queue_below_under_name", "Save the queue below under this name"), SaveNamed);
        StyleBoxFlat go = Style.Box(Style.GoodDim, 5, Style.Good, 1, 12, 5);
        save.AddThemeStyleboxOverride("normal", go);
        save.AddThemeStyleboxOverride("hover", Style.Box(new Color(0.16f, 0.38f, 0.23f), 5, Style.Good, 1, 12, 5));
        top.AddChild(save);
        Label tip = Style.Label(Lang.T("window.switch_quickly_use_list_at", "To switch quickly, use the list at the top of the window."), Style.Muted, 12);
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
        string[] titles = { Lang.T("window.column.saved_queue", "Saved queue"), Lang.T("window.column.steps", "Steps"), Lang.T("window.what_does", "What it does") };
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
        Button load = MakeButton(Lang.T("window.load", "Load"), LoadSelected);
        load.TooltipText = Lang.T("window.puts_selected_queue_place_one", "Puts the selected queue in place of the one below. A double-click does the same.");
        bottom.AddChild(load);
        _deletePreset.Pressed += DeleteSelected;
        bottom.AddChild(_deletePreset);
        bottom.AddChild(new VSeparator());
        Button copy = MakeButton(Lang.T("window.copy_share_code", "Copy share code"), CopyCode);
        copy.TooltipText = Lang.T("window.turns_selected_saved_queue_with", "Turns the selected saved queue (or, with none selected, the queue below) into a short text and copies it.");
        bottom.AddChild(copy);
        _code.TooltipText = Lang.T("window.share_code_somebody_gave_you", "A share code somebody gave you. Paste it here and press Import.");
        bottom.AddChild(_code);
        Button import = MakeButton(Lang.T("window.import", "Import"), ImportCode);
        import.TooltipText = Lang.T("window.reads_code_box_when_box", "Reads the code in the box (or, when the box is empty, the clipboard) and adds it to your saved queues.");
        bottom.AddChild(import);
        _savedForm.AddChild(bottom);
        form.AddChild(_savedForm);
    }

    Button _close = null!;
    static string CloseText => ActionQueueMod.Instance?.Key.With(Lang.T("window.close", "Close")) ?? Lang.T("window.close", "Close");
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
    /// <summary>The size the player dragged the window to (0: as big as its content needs). Kept in queue-window.json (0.13.0).</summary>
    Vector2 _size;
    Control _edgeGrip = null!, _cornerGrip = null!;
    const float EdgeWidth = 6, CornerSize = 16;
    /// <summary>A grip is being dragged: whether it also makes the window taller.</summary>
    bool? _resizing;

    /// <summary>Puts the window at its place: the middle plus the player's offset, kept on the screen (the title bar always is).</summary>
    void Place()
    {
        if (_holder == null || Panel == null) return;
        // as the centring container did: the window is as big as its content needs, and shrinks again with it; a size
        // the player dragged it to is kept, as far as the screen has room for it (never smaller than the content)
        Vector2 min = Panel.GetCombinedMinimumSize();
        Vector2 area = _holder.Size;
        Vector2 want = min;
        if (area.X > 0 && area.Y > 0)
        {
            if (_size.X > min.X) want.X = Math.Min(_size.X, Math.Max(min.X, area.X));
            if (_size.Y > min.Y) want.Y = Math.Min(_size.Y, Math.Max(min.Y, area.Y));
        }
        want = want.Floor();
        if (Panel.Size != want) Panel.Size = want;
        Vector2 size = Panel.Size;
        if (area.X <= 0 || area.Y <= 0) return;
        Vector2 centred = ((area - size) / 2).Floor();
        Vector2 at = centred + _offset;
        at.X = Math.Clamp(at.X, Math.Min(0, area.X - size.X), Math.Max(0, area.X - size.X));
        at.Y = Math.Clamp(at.Y, 0, Math.Max(0, area.Y - size.Y));
        at = at.Round();
        _offset = at - centred;   // what does not fit is not kept: the window never hides off screen later
        if (Panel.Position != at) Panel.Position = at;
        if (_edgeGrip == null || _cornerGrip == null) return;
        _edgeGrip.Position = at + new Vector2(size.X - EdgeWidth, 8);
        _edgeGrip.Size = new Vector2(EdgeWidth, Math.Max(0, size.Y - 8 - CornerSize));
        _cornerGrip.Position = at + size - new Vector2(CornerSize, CornerSize);
    }

    /// <summary>
    /// The right edge (wider) or the corner grip (wider and taller): the window grows from its top-left corner, which stays
    /// where it is. A double-click gives it back its normal size.
    /// </summary>
    void DragGrip(InputEvent e, bool tall)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } b)
        {
            if (b.DoubleClick) { _resizing = null; SizeTo(null, true); SavePlace(); return; }
            _resizing = b.Pressed ? tall : null;
            // the size follows the mouse from where it was pressed (dragged below the least size and back, nothing jumps)
            _gripStart = Panel.Size;
            _gripMoved = Vector2.Zero;
            if (!b.Pressed) SavePlace();
        }
        else if (e is InputEventMouseMotion m && _resizing is bool alsoTaller)
        {
            _gripMoved += new Vector2(m.Relative.X, alsoTaller ? m.Relative.Y : 0);
            SizeTo(_gripStart + _gripMoved, alsoTaller);
        }
    }

    Vector2 _gripStart, _gripMoved;

    /// <summary>Gives the window this size (null: its normal size), keeping its top-left corner in place. Only the corner sets the height.</summary>
    void SizeTo(Vector2? target, bool tall)
    {
        if (Panel == null || _holder == null) return;
        Vector2 oldSize = Panel.Size;
        Vector2 min = Panel.GetCombinedMinimumSize();
        if (target is not { } t) _size = Vector2.Zero;
        else
        {
            Vector2 room = (_holder.Size - Panel.Position).Max(min);   // never past the screen's right or bottom edge
            Vector2 want = t.Clamp(min, room);
            _size = new Vector2(want.X, tall ? want.Y : _size.Y);   // the edge only widens: a height set before stays
        }
        // the top-left corner stays: the offset from the middle moves by half of what the size changed
        Vector2 newSize = new(Math.Max(min.X, _size.X), Math.Max(min.Y, _size.Y));
        _offset += (newSize - oldSize) / 2;
        Place();
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

    /// <summary>
    /// The window's place, size and column widths from queue-window.json ({"X", "Y"} since 0.12.1; "W", "H", "Do", "Until"
    /// since 0.13.0). Anything missing or odd is left at its normal value.
    /// </summary>
    void ReadLayout()
    {
        _offset = _size = Vector2.Zero;
        _doWidth = _untilWidth = 0;
        try
        {
            if (Queue.Dir == "" || !System.IO.File.Exists(PlaceFile)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(PlaceFile));
            float Read(string name, float most)
            {
                if (!doc.RootElement.TryGetProperty(name, out var p) || p.ValueKind != System.Text.Json.JsonValueKind.Number) return 0;
                double v = p.GetDouble();
                return double.IsFinite(v) ? (float)Math.Clamp(v, -most, most) : 0;
            }
            _offset = new Vector2(Read("X", 20000), Read("Y", 20000));
            _size = new Vector2(Math.Max(0, Read("W", 20000)), Math.Max(0, Read("H", 20000)));
            _doWidth = (int)Math.Max(0, Read("Do", 5000));
            _untilWidth = (int)Math.Max(0, Read("Until", 5000));
        }
        catch (Exception) { _offset = _size = Vector2.Zero; _doWidth = _untilWidth = 0; }
    }

    void SavePlace()
    {
        static string N(float v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            if (Queue.Dir == "") return;
            System.IO.File.WriteAllText(PlaceFile, $"{{\"X\": {N(_offset.X)}, \"Y\": {N(_offset.Y)}, \"W\": {N(_size.X)}, \"H\": {N(_size.Y)}, \"Do\": {_doWidth}, \"Until\": {_untilWidth}}}");
        }
        catch (Exception) { }   // a place that could not be kept: the window still moves, it is centred next time
    }

    // ---------------------------------------------------------------- the list's column widths (0.13.0, by madmousepad)

    /// <summary>Widths the player dragged the Do and Until columns to (0: the normal layout, Do and State share the room).</summary>
    int _doWidth, _untilWidth;
    const int UntilNormal = 290, DoLeast = 120, UntilLeast = 140, StateLeast = 140, TimeWidth = 84, LeftWidth = 128;
    readonly ColorRect?[] _dividers = new ColorRect?[3];
    /// <summary>The divider being dragged (1: after Do, 2: after Until), how far the mouse moved since the press, and the column's width then.</summary>
    int _dividerDragged;
    float _dividerMoved;
    int _dividerStartWidth;

    /// <summary>
    /// Normal layout: Do and State share the free room, Until is 290 wide. Once the player drags a divider, Do and Until keep
    /// the widths given and State takes the rest.
    /// </summary>
    void ApplyColumns()
    {
        bool custom = _doWidth > 0 || _untilWidth > 0;
        _table.SetColumnExpand(0, false);   // (a Tree column grows by default: "#" never does)
        _table.SetColumnExpand(1, !custom);
        _table.SetColumnExpand(2, false);
        _table.SetColumnExpand(3, true);
        _table.SetColumnExpand(4, false);
        _table.SetColumnCustomMinimumWidth(4, TimeWidth);
        _table.SetColumnExpand(5, false);
        _table.SetColumnCustomMinimumWidth(5, LeftWidth);
        int doW = custom ? Math.Max(DoLeast, _doWidth > 0 ? _doWidth : 300) : 0;
        int untilW = Math.Max(UntilLeast, _untilWidth > 0 ? _untilWidth : UntilNormal);
        // A window narrower than when the widths were set (made smaller, another screen): they give way so State and both
        // handles stay in view. The widths the player chose are kept and come back when there is room again.
        float room = TableInside() - Math.Max(44, _table.GetColumnWidth(0)) - Math.Max(TimeWidth, _table.GetColumnWidth(4)) - Math.Max(LeftWidth, _table.GetColumnWidth(5)) - StateLeast;
        if (custom && room > 0 && doW + untilW > room)
        {
            untilW = (int)Math.Max(UntilLeast, Math.Min(untilW, room - DoLeast));
            doW = (int)Math.Max(DoLeast, room - untilW);
        }
        _table.SetColumnCustomMinimumWidth(1, doW);
        _table.SetColumnCustomMinimumWidth(2, untilW);
        _table.SetColumnCustomMinimumWidth(3, custom ? StateLeast : 0);
        Callable.From(PlaceDividers).CallDeferred();
    }

    /// <summary>Two thin handles in the list's title row, after "Do" and after "Until": drag to change the width, double-click for the normal layout.</summary>
    void AddColumnDividers()
    {
        Color quiet = new(Style.AccentDim, 0.45f);
        for (int column = 1; column <= 2; column++)
        {
            int which = column;
            var grip = new ColorRect
            {
                Color = quiet, Size = new Vector2(6, 22), MouseFilter = Control.MouseFilterEnum.Stop,
                MouseDefaultCursorShape = Control.CursorShape.Hsize,
                TooltipText = Lang.F("window.drag_make_column_wider_narrower", "Drag to make the {column} column wider or narrower. Double-click: the normal widths.", ("column", (which == 1 ? Lang.T("window.label.do", "Do") : Lang.T("window.column.until", "Until")))),
            };
            grip.MouseEntered += () => grip.Color = Style.AccentDim;
            grip.MouseExited += () => grip.Color = quiet;
            grip.GuiInput += e => DragDivider(e, which);
            _dividers[which] = grip;
            _table.AddChild(grip);
        }
        _table.Resized += () => Callable.From(ApplyColumns).CallDeferred();   // (it places the handles too)
    }

    /// <summary>The width the list's columns share: the list less its frame and, when shown, its vertical scroll bar.</summary>
    float TableInside()
    {
        float inside = _table.Size.X - _table.GetThemeStylebox("panel").GetMinimumSize().X;
        foreach (Node n in _table.GetChildren(true))
            if (n is VScrollBar bar && bar.Visible) inside -= bar.Size.X;
        return inside;
    }

    void PlaceDividers()
    {
        float x = 0;
        for (int column = 0; column < 3; column++)
        {
            x += _table.GetColumnWidth(column);
            if (_dividers[column] is { } grip) grip.Position = new Vector2(x - 3, 2);
        }
    }

    void DragDivider(InputEvent e, int column)
    {
        if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } b)
        {
            if (b.DoubleClick) { _dividerDragged = 0; _doWidth = _untilWidth = 0; ApplyColumns(); SavePlace(); return; }
            _dividerDragged = b.Pressed ? column : 0;
            _dividerMoved = 0;
            _dividerStartWidth = _table.GetColumnWidth(column);
            if (!b.Pressed) SavePlace();
        }
        else if (e is InputEventMouseMotion m && _dividerDragged == column)
        {
            _dividerMoved += m.Relative.X;
            ColumnTo(column, _dividerStartWidth + (int)MathF.Round(_dividerMoved));
        }
    }

    /// <summary>Gives the Do (1) or Until (2) column this width, as far as the State column keeps its least width.</summary>
    void ColumnTo(int column, int width)
    {
        // what the two columns and State have together now: the dragged one can take State's room down to its least
        int doNow = _table.GetColumnWidth(1), untilNow = _table.GetColumnWidth(2), stateNow = _table.GetColumnWidth(3);
        int least = column == 1 ? DoLeast : UntilLeast;
        int most = (column == 1 ? doNow : untilNow) + Math.Max(0, stateNow - StateLeast);
        width = Math.Clamp(width, least, Math.Max(least, most));
        // the other column keeps the width it has on screen
        if (column == 1) { _doWidth = width; if (_untilWidth <= 0) _untilWidth = untilNow; }
        else { _untilWidth = width; if (_doWidth <= 0) _doWidth = doNow; }
        ApplyColumns();
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
    /// <summary>For tests: drag the right edge (tall: the corner grip) by this much, as a mouse does; double-click it.</summary>
    public void DragGripBy(Vector2 by, bool tall)
    {
        DragGrip(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }, tall);
        DragGrip(new InputEventMouseMotion { Relative = by }, tall);
        DragGrip(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false }, tall);
    }
    public void DoubleClickGrip() => DragGrip(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true }, true);
    public void PressGrip(bool tall) => DragGrip(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }, tall);
    public void MoveGrip(Vector2 by, bool tall) => DragGrip(new InputEventMouseMotion { Relative = by }, tall);
    public void ReleaseGrip(bool tall) => DragGrip(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false }, tall);
    public int TableWidth => (int)_table.Size.X;
    public string FoodSkillText => _foodSkill.Text;
    public Rect2 CornerGripRect => new(_cornerGrip.Position, _cornerGrip.Size);
    public Rect2 EdgeGripRect => new(_edgeGrip.Position, _edgeGrip.Size);
    /// <summary>For tests: the list's column widths, a divider dragged to a width (as ColumnTo does), and its double-click.</summary>
    public int[] ColumnWidths => Enumerable.Range(0, _table.Columns).Select(c => _table.GetColumnWidth(c)).ToArray();
    public void SetColumn(int column, int width) { ColumnTo(column, width); SavePlace(); }
    public void ResetColumns() => DragDivider(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true }, 1);
    public Vector2 DividerPosition(int column) => _dividers[column]?.Position ?? Vector2.Zero;
    public Vector2 TablePosition => _table.GetGlobalRect().Position;
    public int TableHeight => (int)_table.Size.Y;

    public void Show(Game game)
    {
        FillLanguages();   // (a language file may have been put next to the mod meanwhile)
        _toolsFor = "";    // (the counts in the toolbelt item lists are made again)
        if (!ReferenceEquals(_game, game))
        {
            _game = game;
            _usedUp = Queue.UsedUpToolbeltItems(game);
            _skills = Targets.Skills(game);
            _levelSkills = game.LeveledEntities.Select(s => s.DisplayName.Text).ToList();
            Fill(_skillPick, _skills.Select(s => s.Entity.DisplayName.Text).Append(SellChoice).Append(EquipChoice).Append(SwitchChoice));
            Fill(_idleSkill, _skills.Select(s => s.Entity.DisplayName.Text).Prepend(Lang.T("window.nothing", "Nothing")));
            // the skills that eat food: the fight first, as the game's own bank lists them
            _foodSkill.Clear();
            _foodSkills.Clear();
            foreach (var (id, name) in Queue.FoodSkills(game).OrderBy(f => f.Id == "MelvorBase:Combat" ? 0 : 1))
            {
                _foodSkills.Add(id);
                _foodSkill.AddItem(name);
                _foodSkill.SetItemMetadata(_foodSkill.ItemCount - 1, id);
            }
            if (_foodSkill.ItemCount > 0) _foodSkill.Select(0);
            // the goal lists for the first skill of this character (another character's skill could have left
            // "the food runs out" or the astrology goals in them)
            FillKinds(_untilPick, withStops: true);
            FillOrKinds();
            Fill(_levelPick, _levelSkills);
            foreach (GoalRow r in _ors) Fill(r.Level, _levelSkills);
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
        foreach (GoalRow r in _ors) r.Item.SetEntries(itemEntries, r.Item.SelectedKey);
        _stopItem.SetEntries(itemEntries, _stopItem.SelectedKey);
        Rares.Paint(_item, game, itemEntries);
        foreach (GoalRow r in _ors) Rares.Paint(r.Item, game, itemEntries);
        Rares.Paint(_stopItem, game, itemEntries);
        _stopShown = "?";   // the lists are made again: show the queue's stop in them
        _thenShown = "?";
        _stopTasksFilled = false;
        _tasksStale = true;
        _planPick.SetEntries(Catalog.Items(game, _makeable));
        UntilChanged();
        Repaint();
        Pulse();
        _layer.Visible = true;
    }

    /// <summary>The languages there are (English and the files in lang/); the list shows only when there is a choice.</summary>
    void FillLanguages()
    {
        var all = Lang.Available();
        _language.Clear();
        foreach ((string code, string name) in all)
        {
            _language.AddItem(name);
            _language.SetItemMetadata(_language.ItemCount - 1, code);
        }
        int at = all.FindIndex(l => l.Code == Lang.Code);
        _language.Select(at < 0 ? 0 : at);
        _language.Visible = all.Count > 1;
    }

    void LanguagePicked(long index)
    {
        string code = _language.GetItemMetadata((int)index).AsString();
        if (code == Lang.Code) return;
        if (_editing != null)
        {
            FillLanguages();
            _status.Text = Lang.T("window.language.finish_editing", "Save or cancel the step you are editing first, then choose the language.");
            return;
        }
        // (the mod builds a new window in the new words once this one has said so)
        if (!Lang.Choose(code, out string why))
        {
            FillLanguages();
            _status.Text = Lang.F("window.language.cannot_use", "That language file could not be used: {why}", ("why", why));
        }
    }

    /// <summary>For tests: the languages the list offers ("" when it is hidden), and choosing one as a click does.</summary>
    public List<string> LanguagesShown => _language.Visible ? Enumerable.Range(0, _language.ItemCount).Select(i => _language.GetItemText(i)).ToList() : new List<string>();
    public void ChooseLanguage(string code)
    {
        for (int i = 0; i < _language.ItemCount; i++)
            if (_language.GetItemMetadata(i).AsString() == code) { _language.Select(i); LanguagePicked(i); }
    }

    /// <summary>Which of the three pages is open: 0 one step, 1 plan, 2 saved queues.</summary>
    public int Page => _planMode.ButtonPressed ? 1 : _savedMode.ButtonPressed ? 2 : 0;
    public void ShowPage(int page) => (page == 1 ? _planMode : page == 2 ? _savedMode : _stepMode).ButtonPressed = true;

    /// <summary>Takes the window out for good: another language was chosen, and a new window is built in its words.</summary>
    public void Dispose()
    {
        Hide();
        _armToken++;   // (a pending "click again to delete" timer finds it stale and leaves the freed button alone)
        _disposed = true;
        Queue.Changed -= Repaint;
        _layer.QueueFree();
    }

    bool _disposed;

    public void Hide()
    {
        foreach (var pick in new Action[] { _target.Close, _item.Close, _idleTarget.Close, _planPick.Close, _stopItem.Close, _taskPick.Close, _stopTask.Close }) pick();
        foreach (GoalRow r in _ors) { r.Item.Close(); r.Task.Close(); }
        // closed with the mouse button still down on the title bar, a grip or a column handle: that drag is over
        _dragging = false;
        _resizing = null;
        _dividerDragged = 0;
        if (GodotObject.IsInstanceValid(_stepMenu)) _stepMenu.Hide();
        _layer.Visible = false;
    }

    // ---------------------------------------------------------------- the queue's own stop

    static string StopSignature(Goal? g) => g == null ? "" : $"{(int)g.Until}|{g.Amount}|{g.Subject}";
    /// <summary>The stop as the row shows it: the goal, and when it is looked at (also with no goal yet: the timing list shows it too).</summary>
    static string ShownKey(Goal? g, bool atEnd) => StopSignature(g) + (atEnd ? "|end" : "");

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
        _stopTiming.Visible = kind != null;
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
        _stopLevel.Text = Lang.T("window.choose_skill", "Choose a skill");
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
            _stopShown = ShownKey(null, Queue.StopAtEnd);
            Queue.SetStopWhen(null);
            Say(Lang.T("window.queue_has_no_stop_own", "The queue has no stop of its own: it ends when its steps are done."));
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
            _stopShown = ShownKey(null, Queue.StopAtEnd);
            Queue.SetStopWhen(null);
            Say(kind switch
            {
                Until.Level => Lang.T("window.now_pick_skill_until_then", "Now pick the skill. Until then the queue has no stop of its own."),
                Until.Mastery => Lang.T("window.now_pick_mastery_task_until", "Now pick the mastery task. Until then the queue has no stop of its own."),
                _ => Lang.T("window.now_pick_item_until_then", "Now pick the item. Until then the queue has no stop of its own."),
            });
            return;
        }
        _stopShown = ShownKey(goal, Queue.StopAtEnd);   // before the queue is told: its change repaints the window
        Queue.SetStopWhen(goal);
        if (Queue.StopWhen is not { } now || ShownKey(now, Queue.StopAtEnd) != _stopShown) { _stopShown = "?"; Say(Lang.T("window.stop_could_not_taken", "That stop could not be taken.")); return; }
        Say(StopSaid(now));
    }

    /// <summary>What the status line says about the queue's stop, with what happens when it is true already.</summary>
    static string StopSaid(Goal now)
    {
        bool atEnd = Queue.StopAtEnd;
        string already = !Queue.StopReachedNow ? ""
            : atEnd ? (Queue.Running ? Lang.T("window.true_already_so_queue_stops_2", " That is true already, so the queue stops when this round is over.")
                                     : Lang.T("window.true_already_started_queue_does", " That is true already: started, the queue does one round and then stops."))
            : Queue.Running ? Lang.T("window.true_already_so_queue_stops", " That is true already, so the queue stops now.")
            : Lang.T("window.true_already_so_queue_will", " That is true already, so the queue will not start until you change it.");
        return atEnd ? Lang.F("window.stop_said_round_end", "The whole queue stops at the end of a round once {stop}.{already}", ("stop", Queue.StopText(now)), ("already", already))
            : Lang.F("window.stop_said", "The whole queue stops when {stop}.{already}", ("stop", Queue.StopText(now)), ("already", already));
    }

    /// <summary>The "go on with" list: nothing, then the saved queues (made again when they or the choice changed).</summary>
    void PaintThen()
    {
        List<string> names = Queue.PresetNames.Where(n => !string.Equals(n, Queue.AutoName, StringComparison.OrdinalIgnoreCase)).ToList();
        string shown = Queue.Then + "\n" + string.Join("\n", names);
        if (shown == _thenShown) return;
        _thenShown = shown;
        bool wasSyncing = _syncing;
        _syncing = true;
        _thenPick.Clear();
        _thenPick.AddItem(Lang.T("window.nothing", "Nothing"));   // (the tooltip says that the idle action then takes over)
        _thenPick.SetItemMetadata(0, "");
        foreach (string n in names)
        {
            _thenPick.AddItem(n);
            _thenPick.SetItemMetadata(_thenPick.ItemCount - 1, n);
        }
        int at = 0;
        if (Queue.Then != "")
        {
            at = names.FindIndex(n => string.Equals(n, Queue.Then, StringComparison.OrdinalIgnoreCase)) + 1;
            if (at <= 0)
            {
                _thenPick.AddItem(Lang.F("window.no_longer_saved", "{then} (no longer saved)", ("then", Queue.Then)));
                _thenPick.SetItemMetadata(_thenPick.ItemCount - 1, Queue.Then);
                at = _thenPick.ItemCount - 1;
            }
        }
        _thenPick.Select(at);
        _syncing = wasSyncing;
    }

    void PickThen(int index)
    {
        string name = index <= 0 || index >= _thenPick.ItemCount ? "" : _thenPick.GetItemMetadata(index).AsString();
        Queue.SetThen(name);
        Say(name == "" ? Lang.T("window.when_queue_ends_idle_action", "When the queue ends, the idle action takes over.")
            : Lang.F("window.when_queue_ends_goes_with", "When the queue ends, it goes on with the saved queue '{name}', from its first step.", ("name", name))
              + (string.Equals(name, Queue.Name, StringComparison.OrdinalIgnoreCase) ? Lang.T("window.not_after_itself_queue_never", " (Not after itself: a queue never starts itself again this way.)") : ""));
    }

    // test helpers for the stop's timing and the "go on with" list
    public void PickStopTiming(bool atEnd) { _stopTiming.Select(atEnd ? 1 : 0); _stopTiming.EmitSignal(OptionButton.SignalName.ItemSelected, atEnd ? 1 : 0); }
    public string StopTimingShown => _stopTiming.Visible ? _stopTiming.GetItemText(_stopTiming.Selected) : "";
    public List<string> ThenChoices => Enumerable.Range(0, _thenPick.ItemCount).Select(i => _thenPick.GetItemText(i)).ToList();
    public string ThenShown => _thenPick.Selected >= 0 ? _thenPick.GetItemText(_thenPick.Selected) : "";
    public bool ChooseThen(string name)
    {
        for (int i = 0; i < _thenPick.ItemCount; i++)
            if (_thenPick.GetItemMetadata(i).AsString() == name) { _thenPick.Select(i); _thenPick.EmitSignal(OptionButton.SignalName.ItemSelected, i); return true; }
        return false;
    }

    /// <summary>Sets the stop row from the queue's stop (another queue loaded, a code imported, the window opened).</summary>
    void PaintStop()
    {
        string now = ShownKey(Queue.StopWhen, Queue.StopAtEnd);
        if (now == _stopShown) return;
        _stopShown = now;
        Goal? g = Queue.StopWhen;
        _syncing = true;
        int index = 0;
        for (int i = 0; i < _stopPick.ItemCount; i++)
            if (_stopPick.GetItemId(i) == (g == null ? NeverId : (int)g.Until)) index = i;
        _stopPick.Select(index);
        _stopTiming.Select(Queue.StopAtEnd ? 1 : 0);
        StopKindShown(fresh: false);
        ClearStopSubject();   // nothing left over from another stop
        if (g != null)
        {
            _stopAmount.Value = g.Amount;
            if (g.Until is Until.Item or Until.ItemLeft && !_stopItem.Select(g.Subject)) _stopItem.ShowMissing(g.Subject, g.SubjectName);
            int at = _levelSkills.IndexOf(g.Subject);
            if (g.Until == Until.Level && at >= 0) _stopLevel.Select(at);
            else if (g.Until == Until.Level) _stopLevel.Text = Lang.F("window.not_game_2", "{subjectName} (not in this game)", ("subjectName", g.SubjectName));
            if (g.Until == Until.Mastery && !_stopTask.Select(g.Subject)) _stopTask.ShowMissing(g.Subject, Lang.F("window.not_game_2", "{subjectName} (not in this game)", ("subjectName", g.SubjectName)));
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
        _side.Text = Queue.SideBySide ? Lang.F("window.side_by_side_at_once", "Side by side: {room} at once", ("room", room)) : Lang.T("window.side_by_side_off", "Side by side: OFF");
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
        for (int lane = 1; lane <= count; lane++) { _lanePick.AddItem(Lang.F("window.lane", "Lane {lane}", ("lane", lane))); _planLanePick.AddItem(Lang.F("window.lane", "Lane {lane}", ("lane", lane))); }
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
        _now.Text =active == null ? Lang.T("window.your_character_doing_nothing_right", "Your character is doing nothing right now.")
            : target == null ? Lang.F("window.right_now_queue_cannot_drive", "Right now: {skill} (the queue cannot drive this one)", ("skill", active.Entity.DisplayName.Text))
            : Lang.F("window.right_now", "Right now: {skill}: {name}", ("skill", active.Entity.DisplayName.Text), ("name", target.Name));
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
        string text = string.Join("  >  ", preset.Steps.Take(3).Select(Queue.Shown));
        if (preset.Steps.Count > 3) text += $"  >  ... (+{preset.Steps.Count - 3})";
        return preset.Repeat ? text + Lang.T("window.starts_over", "   (starts over)") : text;
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
            string shown = Queue.Shown(name);
            row.SetText(0, current ? (Queue.Modified ? shown + Lang.T("window.loaded_changed_since", "   (loaded, changed since)") : shown + Lang.T("window.loaded", "   (loaded)")) : shown);
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
            row.SetText(0, Lang.T("window.nothing_saved_yet", "Nothing saved yet"));
            row.SetText(2, Lang.T("window.build_queue_below_type_name", "Build a queue below, type a name above and press Save."));
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
        if (string.IsNullOrEmpty(name)) { Say(Lang.T("window.select_saved_queue_list_first", "Select a saved queue in the list first.")); return; }
        Queue.LoadPreset(name, out string message);
        Say(message);
    }

    void DeleteSelected()
    {
        string? name = SelectedPreset();
        if (string.IsNullOrEmpty(name)) { Say(Lang.T("window.select_saved_queue_list_first", "Select a saved queue in the list first.")); return; }
        if (_armedDelete != name)
        {
            // Two clicks, so a slip of the mouse does not throw a queue away.
            _armedDelete = name;
            _deletePreset.Text = Lang.T("window.really_delete", "Really delete?");
            int token = ++_armToken;
            _tree.CreateTimer(4).Timeout += () => { if (!_disposed && token == _armToken) Disarm(); };
            Say(Lang.F("window.press_delete_again_remove_good", "Press Delete again to remove '{name}' for good.", ("name", name)));
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
        _deletePreset.Text = Lang.T("window.delete", "Delete");
    }

    void CopyCode()
    {
        string? name = SelectedPreset();
        if (name == "") name = null;
        string? code = Queue.ExportCode(name);
        if (code == null) { Say(Lang.T("window.there_nothing_share_yet_build", "There is nothing to share yet: build or select a queue first.")); return; }
        _code.Text = code;
        try { DisplayServer.ClipboardSet(code); } catch { }
        Say(name == null ? Lang.F("window.share.copied_below", "Share code of the queue below copied ({length} characters). Paste it in a chat; it is also in the box.", ("length", code.Length))
            : Lang.F("window.share.copied_saved", "Share code of '{name}' copied ({length} characters). Paste it in a chat; it is also in the box.", ("name", Queue.Shown(name)), ("length", code.Length)));
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
            _quick.AddItem(Queue.PresetNames.Count == 0 ? Lang.T("window.queue_not_saved", "Queue: not saved") : Lang.T("window.queue_not_saved_pick_one", "Queue: not saved (pick one)"));
            _quickNames.Add("");
        }
        foreach (string name in Queue.PresetNames)
        {
            _quick.AddItem(name == Queue.Name ? (Queue.Modified ? Lang.F("window.queue_changed", "Queue: {name} *", ("name", Queue.Shown(name))) : Lang.F("window.queue", "Queue: {name}", ("name", Queue.Shown(name)))) : Queue.Shown(name));
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
            DrawPlan("none", () => { _planNote.Text = Lang.T("window.choose_item_you_want_click", "Choose the item you want: click the list and type its name."); });
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
                row.SetText(3, line.Maker.Seconds > 0 ? Planner.Time(line.Seconds) : Lang.T("window.cannot_told", "cannot be told"));
                row.SetCustomColor(2, Style.Muted);
                row.SetCustomColor(3, Style.Muted);
                if (line.Problem != "")
                {
                    row.SetText(3, Lang.T("window.target.locked", "locked"));
                    row.SetCustomColor(0, Style.Bad);
                    row.SetCustomColor(3, Style.Bad);
                    for (int c = 0; c < 4; c++) row.SetTooltipText(c, Lang.F("window.plan.locked_skips", "Locked for now: {why}. The queue skips a step it cannot start.", ("why", line.Problem)));
                }
            }
            foreach (PlanLine line in plan.Covered)
            {
                // Nothing to gather here: the bank has it. Shown so a missing "mine the ore" step explains itself.
                TreeItem row = _planTable.CreateItem(root);
                row.SetText(0, Lang.F("window.no_step_enough_bank", "No step: enough {itemName} in the bank", ("itemName", line.ItemName)));
                row.SetText(1, Lang.F("window.needed", "{need:N0} {itemName} needed", ("need", line.Need), ("itemName", line.ItemName)));
                row.SetText(2, line.Have.ToString("N0"));
                row.SetText(3, "-");
                for (int c = 0; c < 4; c++) row.SetCustomColor(c, Style.Muted);
            }
            foreach (PlanLine line in plan.Supply)
            {
                TreeItem row = _planTable.CreateItem(root);
                row.SetText(0, line.Problem != "" ? Lang.T("window.bring_yourself_recipe_still_locked", "Bring yourself (its recipe is still locked)") : Lang.T("window.bring_yourself_no_skill_queue", "Bring yourself (no skill on the queue makes it)"));
                if (line.Problem != "") for (int c = 0; c < 4; c++) row.SetTooltipText(c, Lang.F("window.plan.locked", "Locked for now: {why}.", ("why", line.Problem)));
                row.SetText(1, $"{line.Need:N0} {line.ItemName}");
                row.SetText(2, line.Have.ToString("N0"));
                for (int c = 0; c < 3; c++) row.SetCustomColor(c, Style.Accent);
            }
            if (plan.Steps.Count == 0 && plan.Problem != "")
            {
                // Nothing to do: say why where the steps would be, not only in the small line below.
                TreeItem row = _planTable.CreateItem(root);
                row.SetText(0, Lang.T("window.no_steps_needed", "No steps needed"));
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
        string text = plan.More ? Lang.F("window.make_more_bank_will_then", "To make {asked:N0} more (the bank will then hold {own:N0}): ", ("asked", plan.Asked), ("own", plan.Own)) : "";
        text += Lang.P("window.step_count", plan.Steps.Count, "{count} step", "{count} steps");
        int timed = plan.Steps.Count - plan.UnknownTimes;
        if (timed > 0) text += Lang.F("window.about_at_normal_game_speed", ", about {time} at normal game speed", ("time", Planner.Time(plan.Seconds)));
        if (plan.UnknownTimes > 0) text += timed > 0 ? Lang.F("window.plus_whose_time_cannot_told", " plus {unknownTimes} whose time cannot be told", ("unknownTimes", plan.UnknownTimes)) : Lang.T("window.time_cannot_told_advance", ", time cannot be told in advance");
        text += ".";
        if (plan.Money.Count > 0) text += Lang.F("window.costs", "  Costs {money}.", ("money", string.Join(", ", plan.Money.Select(m => $"{Lang.N(m.Value)} {m.Key}"))));
        if (plan.Covered.Count > 0) text += Lang.T("window.grey_lines_already_bank_so", "  Grey lines: already in the bank, so no step for them.");
        if (plan.Supply.Count > 0) text += Lang.T("window.yellow_lines_items_you_have", "  Yellow lines: items you have to bring yourself.");
        if (plan.Steps.Any(l => l.Problem != "")) text += Lang.T("window.red_lines_still_locked", "  Red lines are still locked.");
        return text;
    }

    void AddPlan()
    {
        // Work the plan out again at this very moment: the bank or the queue may have changed since it was drawn.
        _planShown = "";
        Replan();
        if (_plan == null || _plan.Steps.Count == 0) { Say(_plan?.Problem is { Length: > 0 } why ? why : Lang.T("window.there_nothing_add", "There is nothing to add.")); return; }
        PlanResult plan = _plan;
        int added = Planner.AddToQueue(plan, NewLane, _planSell.ButtonPressed);
        string itemName = plan.Item.DisplayName.GetPluralText(1);
        Say(plan.More ? Lang.P("window.plan_added_more", added, "Plan added: {count} step to make {asked:N0} more {item}.", "Plan added: {count} steps to make {asked:N0} more {item}.", ("asked", plan.Asked), ("item", itemName))
            : Lang.P("window.plan_added_own", added, "Plan added: {count} step to own {own:N0} {item}.", "Plan added: {count} steps to own {own:N0} {item}.", ("own", plan.Own), ("item", itemName)));
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
        if (level >= 0)
        {
            _levelPick.Select(level);
            foreach (GoalRow r in _ors) if (level < r.Level.ItemCount) r.Level.Select(level);
        }
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
        FillKinds(_untilPick, withStops: true);
        FillOrKinds();
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
        if (target == null || skill < 0) { Say(Lang.T("window.start_action_game_first_then", "Start an action in the game first, then press this.")); return; }
        _skillPick.Select(skill);
        FillKinds(_untilPick, withStops: true);
        FillOrKinds();
        FillTargets(Catalog.Key(target));
        UntilChanged();
        Say(Lang.F("window.filled", "Filled in: {skill}: {name}", ("skill", _skills[skill].Entity.DisplayName.Text), ("name", target.Name)));
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
        Say(Lang.F("window.when_queue_waits_ends_your", "When the queue waits or ends, your character does: {skill}: {name}.", ("skill", entity.DisplayName.Text), ("name", target.Name)));
    }

    void UseCurrentForIdle()
    {
        if (_game == null) return;
        ActionComponent? active = Targets.Main(_game);
        Target? target = active == null ? null : Targets.Current(active);
        int skill = active == null ? -1 : _skills.FindIndex(s => ReferenceEquals(s.Action, active));
        if (target == null || skill < 0) { Say(Lang.T("window.start_action_game_first_then", "Start an action in the game first, then press this.")); return; }
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
        _idleState.Text = state == "" ? "" : Lang.F("window.idle_action", "Idle action: {state}", ("state", state));
        _idleState.AddThemeColorOverride("font_color", Queue.IdleTone switch { IdleTone.Good => Style.Good, IdleTone.Bad => Style.Bad, _ => Style.Muted });
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
    public bool SellFaceShown => Selling && _item.Control.Visible && !_target.Control.Visible && _keepShown;
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
    public string? TargetChosen => _target.SelectedKey;
    public string PseudoNoteShown => _pseudoNote.Visible ? _pseudoNote.Text : "";
    public bool KeepShown => _amount.Visible && _keepShown;
    public bool OrShown => _orBox.Visible;
    public void PressOr(bool on) => _orBox.ButtonPressed = on;
    public bool OrRowShown => _orRow.Visible;
    public void ChooseOrUntil(Until kind) { _orPick.Select(_orPick.GetItemIndex((int)kind)); OrChanged(); }
    public void SetOrAmount(double value) => _orAmount.Value = value;
    public bool ChooseOrItem(string itemId) => _orItem.Select(itemId);
    /// <summary>For tests: the "or until" rows shown, "+ or" and "-" as clicks do, and a row's goal (0 is the first "or" row).</summary>
    public int OrRowsShown => _ors.Count(r => r.Row.Visible);
    public bool AddOrShown => _ors.Any(r => r.Row.Visible && r.More.Visible);
    public void PressAddOr() { foreach (GoalRow r in _ors) if (r.Row.Visible && r.More.Visible) { r.More.EmitSignal(BaseButton.SignalName.Pressed); return; } }
    public void PressRemoveOr() { foreach (GoalRow r in _ors) if (r.Row.Visible && r.Less.Visible) { r.Less.EmitSignal(BaseButton.SignalName.Pressed); return; } }
    public void ChooseOrUntil(int row, Until kind) { GoalRow r = _ors[row]; r.Pick.Select(r.Pick.GetItemIndex((int)kind)); OrChanged(r); FillTasks(); }
    public void SetOrAmount(int row, double value) => _ors[row].Amount.Value = value;
    public bool ChooseOrItem(int row, string itemId) => _ors[row].Item.Select(itemId);
    public bool ChooseOrLevel(int row, string skillName)
    {
        OptionButton level = _ors[row].Level;
        for (int i = 0; i < level.ItemCount; i++) if (level.GetItemText(i) == skillName) { level.Select(i); return true; }
        return false;
    }
    public string OrRowKind(int row) => _ors[row].Row.Visible ? $"{_ors[row].Kind}:{_ors[row].Amount.Value}" : "";
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

    static string SellChoice => Lang.T("window.choice.sell", "Sell from the bank");
    static string EquipChoice => Lang.T("window.choice.equip", "Equip");
    static string SwitchChoice => Lang.T("window.choice.go_to", "Go to a saved queue");
    bool Selling => _skillPick.Selected >= 0 && _skillPick.Selected == _skills.Count;
    bool Equipping => _skillPick.Selected >= 0 && _skillPick.Selected == _skills.Count + 1;
    bool Switching => _skillPick.Selected >= 0 && _skillPick.Selected == _skills.Count + 2;
    /// <summary>A step that is not an action: sell, equip, go to a saved queue.</summary>
    bool Pseudo => Selling || Equipping || Switching;
    bool Astrology => !Pseudo && _skillPick.Selected >= 0 && _skillPick.Selected < _skills.Count
        && _skills[_skillPick.Selected].Action is GamesByMalcs.Melvor2.Entities.Astrology.AstrologyActionComponent;
    bool EquipIsLoadout => _target.Selected?.Value.Id.StartsWith(Step.LoadoutPrefix, StringComparison.Ordinal) == true;
    /// <summary>Equip, and a food is picked (not gear): the step chooses the food a skill eats.</summary>
    bool EquipIsFood => Equipping && _target.Selected?.Key.StartsWith(FoodKeyPrefix, StringComparison.Ordinal) == true;
    /// <summary>The skills that eat food (each has a food box), by entity id: their steps can wait for the food to run out.</summary>
    readonly HashSet<string> _foodSkills = new();
    bool HasFood => !Pseudo && _skillPick.Selected >= 0 && _skillPick.Selected < _skills.Count && _foodSkills.Contains(_skills[_skillPick.Selected].Entity.Id);
    /// <summary>The chosen skill uses up toolbelt items (potions, oven mitts...): its steps can wait for one to run out.</summary>
    bool HasTool => !Pseudo && _skillPick.Selected >= 0 && _skillPick.Selected < _skills.Count && _usedUp.TryGetValue(_skills[_skillPick.Selected].Entity.Id, out var used) && used.Count > 0;
    /// <summary>Equip, and a toolbelt item is picked (0.17.0): the step puts it in its skill's toolbelt.</summary>
    bool EquipIsToolbelt => Equipping && _target.Selected?.Key.StartsWith(ToolKeyPrefix, StringComparison.Ordinal) == true;
    /// <summary>The toolbelt item picked goes in as a stack (a potion), not as one (a tool).</summary>
    bool ChosenToolStacks => EquipIsToolbelt && _game != null && _target.Selected is { } chosen
        && _game.Data.GetDataRegistryOutput<Item>().GetAllObjects().FirstOrDefault(i => i.Id == chosen.Value.Id) is { } it
        && it.Capabilities.TryGet<ToolbeltCapability>(out var cap) && cap.CanEquipQuantity;
    bool _wasSelling;

    /// <summary>The goal kinds are list entries whose id is the <see cref="Until"/> number (Astrology has two more).</summary>
    Until Kind => _untilPick.Selected < 0 ? Until.Stops : (Until)_untilPick.GetSelectedId();

    /// <summary>The goals the chosen skill offers: the astrology ones for Astrology, "the food runs out" for a skill that eats.</summary>
    void FillKinds(OptionButton pick, bool withStops)
    {
        bool astrology = Astrology, food = HasFood, tool = HasTool;
        int keep = pick.Selected >= 0 ? pick.GetSelectedId() : -1;
        pick.Clear();
        for (int i = 0; i < UntilNames.Length; i++)
        {
            var kind = (Until)i;
            if (kind == Until.Stops && !withStops) continue;
            if (kind is Until.Nodes or Until.Event && !astrology) continue;
            if (kind == Until.FoodRunsOut && !food) continue;
            if (kind == Until.ToolRunsOut && !tool) continue;
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
        _toolPick.Control.Visible = false;
        if (pseudo)
        {
            // Sell: which item, and how many to keep in the bank. Equip: what to put on (and for a stack, how many to
            // keep; for a food, which skill eats it). Go to a saved queue: which one.
            bool food = equipping && EquipIsFood;
            bool tool = equipping && EquipIsToolbelt;
            bool keep = selling || equipping && !EquipIsLoadout && !food && (!tool || ChosenToolStacks);
            _untilLabel.Text = keep ? Lang.T("window.label.keep", "keep") : food ? Lang.T("window.label.food_for", "for") : "";
            _keepShown = keep;
            _foodSkill.Visible = food;
            _item.Control.Visible = selling;
            _amount.Visible = keep;
            _levelPick.Visible = _orBox.Visible = false;
            foreach (GoalRow r in _ors) r.Row.Visible = false;
            _amount.MinValue = 0;
            _amount.MaxValue = 100_000_000;
            _amount.Suffix = "";
            if (!_wasSelling)
            {
                if (_lastKind == Until.ItemLeft) _amountLeft = _amount.Value; else _amountOther = Math.Max(1, _amount.Value);
                _amount.Value = 0;
            }
            _amount.TooltipText = selling ? Lang.T("window.how_many_keep_bank_sells", "How many to keep in the bank: 0 sells all of them.")
                : tool ? Lang.T("window.toolbelt_keep_tip", "How many to keep in the bank: 0 puts all of them in the toolbelt.")
                : Lang.T("window.arrows_bolts_javelins_other_stacks", "For arrows, bolts, javelins and other stacks: how many to keep in the bank (0 puts all of them on).\nOther gear is simply put on.");
            _pseudoNote.Text = switching ? Lang.T("window.queue_goes_with_saved_queue", "the queue goes on with this saved queue once every step before it is over (in every lane)")
                : equipping && EquipIsLoadout ? Lang.T("window.switches_loadout_gear_food_prayers", "switches to this loadout (gear, food, prayers and spells) while the game is open")
                : tool ? Lang.T("window.toolbelt_puts_from_bank", "puts it from the bank into its toolbelt slot, also while the game is closed")
                : food ? (_foodSkill.ItemCount == 0 ? Lang.T("window.no_skill_game_eats_food", "no skill in this game eats food") : Lang.T("window.makes_food_skill_eats_as", "makes it the food this skill eats, as its own food box does, while the game is open"))
                : equipping ? Lang.T("window.puts_from_bank_while_game", "puts it on from the bank while the game is open") : "";
            _pseudoNote.Visible = _pseudoNote.Text != "";
            _wasSelling = true;
            return;
        }
        _pseudoNote.Visible = false;
        _foodSkill.Visible = false;
        _untilLabel.Text = Lang.T("window.label.until", "until");
        _keepShown = false;
        _amount.TooltipText = "";
        if (_wasSelling)
        {
            _wasSelling = false;
            _amount.MinValue = _lastKind == Until.ItemLeft ? 0 : 1;
            _amount.Value = _lastKind == Until.ItemLeft ? _amountLeft : Math.Max(1, _amountOther);
        }
        var kind = Kind;
        _amount.Visible = kind is not (Until.Stops or Until.Mastery or Until.FoodRunsOut or Until.ToolRunsOut);
        _item.Control.Visible = kind is Until.Item or Until.ItemLeft or Until.Made or Until.Spent;
        _toolPick.Control.Visible = kind == Until.ToolRunsOut;
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
        FillTools();
        _amount.Suffix = kind == Until.Minutes ? Lang.T("window.plan.min", " min") : "";
        _amount.MaxValue = kind == Until.Level ? 999 : 100_000_000;
        if (kind == Until.Level && _amount.Value > 999) _amount.Value = 99;
        if (kind != Until.Nodes) _amount.TooltipText = "";
        CapNodes(_amount, _nodePick, kind);
        _orBox.Visible = true;
        ShowOrRows();
    }

    /// <summary>The "or until" rows in use show (while "or" is ticked); "+ or" and "-" sit on the last one.</summary>
    void ShowOrRows()
    {
        bool on = _orBox.Visible && _orBox.ButtonPressed;
        for (int i = 0; i < _ors.Length; i++)
        {
            GoalRow r = _ors[i];
            r.Row.Visible = on && i < _orCount;
            bool last = i == _orCount - 1;
            r.More.Visible = last && _orCount < _ors.Length;
            r.Less.Visible = last && _orCount > 1;
        }
        foreach (GoalRow r in _ors) OrChanged(r);
        FillTasks();
        FillTools();
    }

    void FillOrKinds()
    {
        foreach (GoalRow r in _ors) FillKinds(r.Pick, withStops: false);
    }

    void OrChanged() { foreach (GoalRow r in _ors) OrChanged(r); FillTasks(); FillTools(); }

    void OrChanged(GoalRow r)
    {
        var kind = r.Kind;
        r.Amount.Visible = kind is not (Until.Stops or Until.Mastery or Until.FoodRunsOut or Until.ToolRunsOut);
        r.Tool.Control.Visible = kind == Until.ToolRunsOut;
        r.Item.Control.Visible = kind is Until.Item or Until.ItemLeft or Until.Made or Until.Spent;
        r.Level.Visible = kind == Until.Level;
        r.Node.Visible = kind == Until.Nodes;
        r.Event.Visible = kind == Until.Event;
        r.Task.Control.Visible = kind == Until.Mastery;
        r.Amount.MinValue = kind == Until.ItemLeft ? 0 : 1;
        r.Amount.Suffix = kind == Until.Minutes ? Lang.T("window.plan.min", " min") : "";
        r.Amount.MaxValue = kind == Until.Level ? 999 : 100_000_000;
        if (kind == Until.Level && r.Amount.Value > 999) r.Amount.Value = 99;
        r.Amount.TooltipText = "";
        CapNodes(r.Amount, r.Node, kind);
    }

    /// <summary>A node goal: the number goes no higher than the chosen constellation's minor (or major) unlocks.</summary>
    void CapNodes(SpinBox amount, OptionButton node, Until kind)
    {
        if (kind != Until.Nodes || _game == null || _target.Selected is not { } chosen) return;
        int most = Queue.MostNodes(_game, chosen.Value.Id, node.Selected == 1);
        if (most <= 0) return;
        amount.MaxValue = most;
        if (amount.Value > most) amount.Value = most;
        amount.TooltipText = Lang.P("window.constellation_nodes_in_all", most, "This constellation has {count} {kind} node unlock in all.", "This constellation has {count} {kind} node unlocks in all.",
            ("kind", Step.NodeKind(node.Selected == 1 ? "major" : "minor", "")));
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
        if (Pseudo || !(Kind == Until.Mastery || OrRows.Any(r => r.Kind == Until.Mastery))) return;
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
                    Group: mine ? Lang.F("window.plan.for", "For {targetName}", ("targetName", targetName)) : fight ? t.Skill : Lang.T("window.other_tasks_skill", "Other tasks of this skill"),
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
        foreach (PickList<string> list in _ors.Select(r => r.Task).Prepend(_taskPick))
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

    /// <summary>
    /// The toolbelt items the chosen skill uses up (potions, oven mitts, utilities), in every toolbelt item list: the ones
    /// in its toolbelt now first (the first of them is picked), then the rest by slot. Made only while a goal is "a
    /// toolbelt item runs out", and only again for another skill or once the window was opened again; a list made again
    /// keeps the player's choice.
    /// </summary>
    void FillTools()
    {
        if (Pseudo || _game == null || !(Kind == Until.ToolRunsOut || OrRows.Any(r => r.Kind == Until.ToolRunsOut))) return;
        string entity = _skillPick.Selected >= 0 && _skillPick.Selected < _skills.Count ? _skills[_skillPick.Selected].Entity.Id : "";
        if (entity == _toolsFor) return;
        _toolsFor = entity;
        ToolbeltComponent? toolbelt = Queue.ToolbeltOfSkill(_game, entity);
        GamesByMalcs.Melvor2.Entities.Bank.BankEntity? bank = _game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? b) ? b as GamesByMalcs.Melvor2.Entities.Bank.BankEntity : null;
        string inNow = Lang.T("window.toolbelt_in_it_now", "In the toolbelt now");
        var entries = new List<PickEntry<Item>>();
        foreach (Item item in _usedUp.GetValueOrDefault(entity) ?? new List<Item>())
        {
            string slot = item.Capabilities.TryGet<ToolbeltCapability>(out var cap) ? cap.FitsInSlot.DisplayName.Text : "";
            int on = Math.Max(0, toolbelt?.GetEquippedQuantity(item) ?? 0), have = bank?.Inventory.GetQuantity(item) ?? 0;
            entries.Add(new PickEntry<Item>(item.Id, item.DisplayName.GetPluralText(1), item, on > 0 ? inNow : slot, "", item.Icon, null,
                on > 0 ? Lang.F("window.toolbelt_on", "{on:N0} in the toolbelt", ("on", on)) : have > 0 ? Lang.F("window.bank", "{owned:N0} in bank", ("owned", have)) : "",
                Muted: on <= 0 && have <= 0, Keywords: slot));
        }
        entries = entries.OrderBy(e => e.Group == inNow ? 0 : 1).ThenBy(e => e.Group, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Text, StringComparer.OrdinalIgnoreCase).ToList();
        string? first = entries.FirstOrDefault(e => e.Group == inNow)?.Key ?? entries.FirstOrDefault()?.Key;
        bool same = entity == _toolsSkill;
        _toolsSkill = entity;
        foreach (PickList<Item> list in _ors.Select(r => r.Tool).Prepend(_toolPick))
        {
            string? had = list.SelectedKey, hadText = list.ButtonText;
            string? keep = same && had != null && entries.Any(e => e.Key == had) ? had : null;
            list.SetEntries(entries, keep ?? first);
            // A step being edited whose item this game does not have stays shown as missing (the window opened again
            // meanwhile): saving then asks for a choice instead of quietly taking the list's first item.
            if (same && _editing != null && had != null && keep == null) list.ShowMissing(had, hadText);
        }
    }

    /// <summary>For tests: the toolbelt item lists' keys (the step's own row, or the first "or" row) and picking one.</summary>
    public List<string> ToolGoalKeys(bool or = false) => (or ? _ors[0].Tool : _toolPick).VisibleKeys;
    public string? ToolGoalChosen(bool or = false) => (or ? _ors[0].Tool : _toolPick).SelectedKey;
    public bool ChooseToolGoal(string itemId, bool or = false) => (or ? _ors[0].Tool : _toolPick).Select(itemId);
    public void OpenToolGoals() => _toolPick.Open();
    public void CloseToolGoals() => _toolPick.Close();
    /// <summary>For tests: the Do list opened with a search typed in, as a player looks for something.</summary>
    public void OpenTargets(string search) { _target.Open(); _target.SetFilter(search); }
    public void CloseTargets() => _target.Close();

    /// <summary>The events the chosen constellation can show, in both event lists (kept when still there).</summary>
    void FillEvents()
    {
        List<(string Id, string Name)> events = _game != null && Astrology && _target.Selected is { } chosen
            ? Queue.AstrologyEvents(_game, chosen.Value.Id) : new();
        foreach (OptionButton pick in _ors.Select(r => r.Event).Prepend(_eventPick))
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
                string hint = !loadout.IsUnlocked ? Lang.T("window.target.locked", "locked") : combat.Loadouts.CurrentLoadout == loadout ? Lang.T("window.loadout.in_use", "in use") : "";
                entries.Add(new PickEntry<Target>(id, loadout.Name, new Target(id, loadout.Name, ""), Lang.T("window.loadouts", "Loadouts"), Hint: hint, Muted: !loadout.IsUnlocked));
            }
        GamesByMalcs.Melvor2.Entities.Bank.BankEntity? bank = game.Entities.TryGetObjectById("MelvorBase:Bank", out GameEntity? b) ? b as GamesByMalcs.Melvor2.Entities.Bank.BankEntity : null;
        var gear = new List<PickEntry<Target>>();
        var food = new List<PickEntry<Target>>();
        var tools = new List<PickEntry<Target>>();
        var bySlot = Queue.ToolbeltsBySlot(game);
        foreach (Item item in game.Data.GetDataRegistryOutput<Item>().GetAllObjects())
        {
            // Toolbelt items (0.17.0): potions, oven mitts, utilities and tools, under the skill whose toolbelt takes them.
            if (item.Capabilities.TryGet<ToolbeltCapability>(out var tool) && bySlot.TryGetValue(tool.FitsInSlot, out var belt))
            {
                string toolName = item.DisplayName.GetPluralText(1), skill = belt.Entity.DisplayName.Text, slot = tool.FitsInSlot.DisplayName.Text;
                int have = bank?.Inventory.GetQuantity(item) ?? 0, on = Math.Max(0, belt.Toolbelt.GetEquippedQuantity(item));
                tools.Add(new PickEntry<Target>(ToolKeyPrefix + item.Id, toolName, new Target(item.Id, toolName, belt.Entity.Id),
                    Lang.F("window.toolbelt_group", "{skill} toolbelt", ("skill", skill)), slot, item.Icon, tool.FitsInSlot.Icon,
                    on > 0 ? Lang.F("window.toolbelt_on", "{on:N0} in the toolbelt", ("on", on)) : have > 0 ? Lang.F("window.bank", "{owned:N0} in bank", ("owned", have)) : "",
                    Lang.F("window.toolbelt_entry_tip", "Goes in the {slot} slot of the {skill} toolbelt.", ("slot", slot), ("skill", skill)), Muted: have <= 0, Keywords: "toolbelt " + skill));
            }
            // Food (0.13.0, by madmousepad): the step makes it the food of a skill picked next to the list.
            if (item.Capabilities.TryGet<GamesByMalcs.Melvor2.Components.CombatFood.FoodCapability>(out _))
            {
                string foodName = item.DisplayName.GetPluralText(1);
                int have = bank?.Inventory.GetQuantity(item) ?? 0;
                food.Add(new PickEntry<Target>(FoodKeyPrefix + item.Id, foodName, new Target(item.Id, foodName, ""), Lang.T("window.food_skill_you_pick", "Food (for the skill you pick)"), "", item.Icon, null,
                    have > 0 ? Lang.F("window.bank_2", "{have:N0} in bank", ("have", have)) : "", Lang.T("window.step_makes_food_skill_picked", "The step makes it the food the skill picked next to this list eats."), Muted: have <= 0, Keywords: "food"));
            }
            if (!item.Capabilities.TryGet<GamesByMalcs.Melvor2.Components.Equipment.EquipmentCapability>(out var equip)) continue;
            string name = item.DisplayName.GetPluralText(1);
            int owned = bank?.Inventory.GetQuantity(item) ?? 0;
            if (Rares.Is(item))
            {
                bool worn = Rares.Worn(game, item);
                if (owned <= 0 && !worn) continue;   // sold or never found: One Style keeps it in the game until a restart
                gear.Add(new PickEntry<Target>(item.Id, name, new Target(item.Id, name, ""), equip.Type.DisplayName.Text, "", item.Icon, equip.Type.Icon,
                    worn ? Lang.T("window.gear.worn", "worn") : Lang.F("window.bank", "{owned:N0} in bank", ("owned", owned)), Rares.Tooltip(item)));
                continue;
            }
            gear.Add(new PickEntry<Target>(item.Id, name, new Target(item.Id, name, ""), equip.Type.DisplayName.Text, "", item.Icon, equip.Type.Icon,
                owned > 0 ? Lang.F("window.bank", "{owned:N0} in bank", ("owned", owned)) : "", Muted: owned <= 0));
        }
        entries.AddRange(gear.OrderBy(g => g.Group, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Text, StringComparer.OrdinalIgnoreCase));
        entries.AddRange(tools.OrderBy(t => t.Group, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Subgroup, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Text, StringComparer.OrdinalIgnoreCase));
        entries.AddRange(food.OrderBy(f => f.Muted).ThenBy(f => f.Text, StringComparer.OrdinalIgnoreCase));
        return entries;
    }

    /// <summary>For the "go to a saved queue" step: the saved queues (not the automatic copy of unsaved steps).</summary>
    static List<PickEntry<Target>> SavedQueueEntries()
    {
        var entries = Queue.PresetNames.Where(n => !string.Equals(n, Queue.AutoName, StringComparison.OrdinalIgnoreCase))
            .Select(n => new PickEntry<Target>(n, n, new Target(n, n, ""), Lang.T("window.saved_queues", "Saved queues"), Hint: SavedHint(n))).ToList();
        if (entries.Count == 0)
            entries.Add(new PickEntry<Target>("", Lang.T("window.no_saved_queues_yet_save", "No saved queues yet: save one under \"Saved queues\" first"), new Target("", "", ""), Enabled: false, Muted: true));
        return entries;
    }

    /// <summary>Reads one goal from its controls (the step's own row, or the "or until" row).</summary>
    bool ReadGoal(Until kind, double amount, PickList<Item> item, OptionButton level, OptionButton node, OptionButton ev, PickList<string> task, PickList<Item> tool, out Goal goal)
    {
        goal = new Goal { Until = kind, Amount = amount };
        if (kind is Until.Item or Until.ItemLeft or Until.Made or Until.Spent)
        {
            if (item.Selected is not { } counted) { Say(Lang.T("window.pick_item_count_first", "Pick the item to count first.")); return false; }
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
            if (ev.Selected < 0) { Say(Lang.T("window.constellation_has_no_events_wait", "This constellation has no events to wait for.")); return false; }
            goal.Subject = ev.GetItemMetadata(ev.Selected).AsString();
            goal.SubjectName = ev.GetItemText(ev.Selected);
        }
        else if (kind == Until.Mastery)
        {
            if (task.SelectedKey is not { Length: > 0 } id) { Say(Lang.T("window.pick_mastery_task_first", "Pick the mastery task first.")); return false; }
            if (Mastery.Find(id) is not { } chosen) { Say(Lang.T("window.mastery_task_not_game_pick", "That mastery task is not in this game: pick another one.")); return false; }
            goal.Subject = id;
            goal.SubjectName = chosen.Text;
            goal.Amount = 1;
        }
        else if (kind == Until.FoodRunsOut)
            goal.Amount = 0;   // (the step's skill and the food its box holds when the step runs: nothing to pick)
        else if (kind == Until.ToolRunsOut)
        {
            if (tool.Selected is not { } used) { Say(Lang.T("window.pick_toolbelt_item_first", "Pick the toolbelt item first.")); return false; }
            goal.Subject = used.Value.Id;
            goal.SubjectName = used.Value.DisplayName.GetPluralText(1);
            goal.Amount = 0;
        }
        return true;
    }

    /// <summary>The lane new steps go to (1 when lanes are not shown).</summary>
    int NewLane => _lanePick.Visible ? _lanePick.Selected + 1 : 1;

    void AddStep()
    {
        if (Selling)
        {
            if (_item.Selected is not { } toSell) { Say(Lang.T("window.pick_item_sell_first", "Pick the item to sell first.")); return; }
            Step sale = Planner.SellStep(toSell.Value, (long)_amount.Value, NewLane);
            Put(sale);
            return;
        }
        if (Equipping)
        {
            if (_target.Selected is not { Enabled: true } gear || gear.Key == "") { Say(Lang.T("window.choose_what_equip_first", "Choose what to equip first.")); return; }
            if (EquipIsFood)
            {
                if (_foodSkill.Selected < 0 || _foodSkill.ItemCount == 0) { Say(Lang.T("window.choose_skill_eats_food_first", "Choose the skill that eats this food first.")); return; }
                Put(new Step
                {
                    Entity = Step.EquipEntity, TargetId = gear.Value.Id, Subject = Step.FoodSubject, Lane = NewLane,
                    Extra = _foodSkill.GetItemMetadata(_foodSkill.Selected).AsString(), SubjectName = _foodSkill.GetItemText(_foodSkill.Selected),
                    Name = $"Equip: {gear.Text}",
                });
                return;
            }
            if (EquipIsToolbelt)
            {
                bool stacks = ChosenToolStacks;
                string owner = _game != null && _game.Entities.TryGetObjectById(gear.Value.Extra, out GameEntity? skill) && skill != null ? skill.DisplayName.Text : "";
                Put(new Step
                {
                    Entity = Step.EquipEntity, TargetId = gear.Value.Id, Subject = stacks ? Step.ToolbeltStack : Step.ToolbeltOne, Lane = NewLane,
                    Extra = gear.Value.Extra, SubjectName = owner, Amount = stacks ? _amount.Value : 0, Name = $"Equip: {gear.Text}",
                });
                return;
            }
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
            if (_target.Selected is not { Enabled: true } saved || saved.Key == "") { Say(Lang.T("window.save_queue_under_saved_queues", "Save a queue under \"Saved queues\" first, then choose it here.")); return; }
            var go = new Step { Entity = Step.SwitchEntity, TargetId = saved.Value.Id, Name = $"Go to saved queue: {saved.Value.Id}", Lane = NewLane };
            if (_editing != null) { Put(go); return; }
            Queue.Add(go);
            Say(Lang.F("window.added_when_every_step_before", "Added: {name}. When every step before it is over, the queue goes on with '{queue}'.", ("name", Queue.Shown(go)), ("queue", saved.Value.Id)));
            return;
        }
        if (_skillPick.Selected < 0 || _target.Selected is not { } chosen) { Say(Lang.T("window.choose_what_do_first", "Choose what to do first.")); return; }
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
        if (!ReadGoal(step.Until, step.Amount, _item, _levelPick, _nodePick, _eventPick, _taskPick, _toolPick, out Goal main)) return;
        step.Subject = main.Subject;
        step.SubjectName = main.SubjectName;
        step.Amount = main.Amount;
        var others = new List<Goal>();
        foreach (GoalRow r in OrRows)
        {
            if (!ReadGoal(r.Kind, r.Amount.Value, r.Item, r.Level, r.Node, r.Event, r.Task, r.Tool, out Goal or)) return;
            others.Add(or);
        }
        step.SetOthers(others);
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
        _laneRepeats.AddChild(Style.Label(Lang.T("window.lanes_start_over", "Lanes that start over:"), Style.Muted));
        foreach (int lane in lanes)
        {
            var box = new CheckBox
            {
                Text = Lang.F("window.lane", "Lane {lane}", ("lane", lane)), ButtonPressed = Queue.LaneRepeats(lane),
                TooltipText = Lang.T("window.ticked_lane_starts_over_by", "Ticked: this lane starts over by itself whenever its last step is done.\nUnticked: it runs once and then stays finished; the other lanes go on."),
            };
            int which = lane;
            box.SetMeta("lane", lane);
            box.Toggled += on => { if (!_syncing) Queue.SetLaneRepeat(which, on); };
            _laneRepeats.AddChild(box);
        }
    }

    /// <summary>For tests: the lanes the window offers a "start over" box for, and ticking one.</summary>
    public List<string> LaneRepeatBoxes => _laneRepeats.Visible ? _laneRepeats.GetChildren().OfType<CheckBox>().Select(b => $"{b.Text}:{(b.ButtonPressed ? "on" : "off")}").ToList() : new List<string>();
    public void PressLaneRepeat(int lane)
    {
        foreach (CheckBox b in _laneRepeats.GetChildren().OfType<CheckBox>()) if (b.GetMeta("lane").AsInt32() == lane) b.ButtonPressed = !b.ButtonPressed;
    }

    /// <summary>A saved queue in the go-to list: its steps, and its own stop when it has one (that is where it ends).</summary>
    static string SavedHint(string name)
    {
        Preset preset = Queue.GetPreset(name)!;
        string steps = Lang.P("window.step_count", preset.Steps.Count, "{count} step", "{count} steps");
        if (preset.StopWhen is not { } stop) return steps;
        return preset.StopAtEnd ? Lang.F("window.saved.hint_stop_round", "{steps}, stops at the end of a round once {stop}", ("steps", steps), ("stop", Queue.StopText(stop)))
            : Lang.F("window.saved.hint_stop", "{steps}, stops when {stop}", ("steps", steps), ("stop", Queue.StopText(stop)));
    }

    /// <summary>A new step goes at the end; a step being edited goes back in its place.</summary>
    void Put(Step step)
    {
        int at = _editing == null ? -1 : Queue.Steps.IndexOf(_editing);
        if (at >= 0)
        {
            Queue.ReplaceAt(at, step);
            EndEdit();
            Say(Lang.F("window.step_changed", "Step {number} changed: {name}, {goal}.", ("number", at + 1), ("name", Queue.Shown(step)), ("goal", step.Goal)));
            return;
        }
        EndEdit();
        Queue.Add(step);
        Say(Lang.F("window.added", "Added: {name}, {goal}.", ("name", Queue.Shown(step)), ("goal", step.Goal)));
    }

    void EndEdit()
    {
        bool wasEditing = _editing != null;
        _editing = null;
        // a food step edited for a skill this game does not have left the skill list empty: the next food gets the first skill
        if (_foodSkill.Selected < 0 && _foodSkill.ItemCount > 0) _foodSkill.Select(0);
        _addButton.Text = Lang.T("window.add_step", "Add step");
        _cancelEdit.Visible = false;
        if (!wasEditing) return;   // (a step simply added keeps the form as it was, "or" rows too)
        _orCount = 1;
        _orBox.SetPressedNoSignal(false);
        ShowOrRows();
    }

    /// <summary>The list's Edit: the selected step in the form, ready to be changed and saved back in its place.</summary>
    void EditSelected()
    {
        int index = SelectedIndex();
        if (index < 0 || index >= Queue.Steps.Count) { Say(Lang.T("window.select_step_list_first_then_2", "Select a step in the list first, then press Edit.")); return; }
        Step s = Queue.Steps[index];
        int skill = s.Entity switch
        {
            Step.SellEntity => _skills.Count,
            Step.EquipEntity => _skills.Count + 1,
            Step.SwitchEntity => _skills.Count + 2,
            _ => _skills.FindIndex(x => x.Entity.Id == s.Entity),
        };
        if (skill < 0) { Say(Lang.T("window.step_skill_not_list_here", "This step's skill is not on the list here, so it cannot be edited (remove it and add it again).")); return; }
        ShowStepForm();
        _skillPick.Select(skill);
        FillKinds(_untilPick, withStops: true);
        FillOrKinds();
        string wantKey = s.IsFood ? FoodKeyPrefix + s.TargetId : s.IsToolbelt ? ToolKeyPrefix + s.TargetId : Catalog.Key(s.TargetId, s.Extra);
        FillTargets(wantKey, suggest: false);
        // Equip for an item (or loadout) this game does not have: shown as missing, not swapped for the list's first entry
        // (saving then asks for a choice instead of quietly turning the step into another one).
        if (Equipping && _target.SelectedKey != wantKey) _target.ShowMissing(wantKey, Lang.F("window.not_game", "{name} (not in this game)", ("name", s.Name)));
        string foodNote = "";
        if (s.IsFood)
        {
            // A skill this game does not have (a shared code): nothing is chosen, so saving asks for one instead of
            // quietly giving the food to another skill.
            _foodSkill.Select(-1);
            for (int i = 0; i < _foodSkill.ItemCount; i++)
                if (_foodSkill.GetItemMetadata(i).AsString() == s.Extra) { _foodSkill.Select(i); break; }
            if (_foodSkill.Selected < 0)
            {
                _foodSkill.Text = Lang.T("window.choose_skill", "Choose a skill");
                foodNote = Lang.F("window.does_not_eat_food_game", " {skill} does not eat food in this game: pick the skill for it.", ("skill", (s.SubjectName != "" ? s.SubjectName : s.Extra)));
            }
        }
        if (Selling) _item.Select(s.TargetId);
        else if (!Pseudo)
        {
            int kindAt = _untilPick.GetItemIndex((int)s.Until);
            if (kindAt >= 0) _untilPick.Select(kindAt);
        }
        UntilChanged();
        if (!Pseudo) LoadGoal(s, _item, _levelPick, _nodePick, _eventPick, _taskPick, _toolPick);
        if (!Pseudo) CapNodes(_amount, _nodePick, Kind);   // a node goal's cap is the chosen kind's (minor or major)
        _amount.Value = s.Amount;
        List<Goal> theirs = Pseudo ? new List<Goal>() : s.Others.ToList();
        _orCount = Math.Max(1, theirs.Count);
        _orBox.SetPressedNoSignal(theirs.Count > 0);
        ShowOrRows();
        for (int i = 0; i < theirs.Count && i < _ors.Length; i++)
        {
            GoalRow r = _ors[i];
            Goal or = theirs[i];
            int orAt = r.Pick.GetItemIndex((int)or.Until);
            if (orAt >= 0) r.Pick.Select(orAt);
            OrChanged(r);
            FillEvents();
            FillTasks();
            FillTools();
            LoadGoal(or, r.Item, r.Level, r.Node, r.Event, r.Task, r.Tool);
            CapNodes(r.Amount, r.Node, r.Kind);
            r.Amount.Value = or.Amount;
        }
        if (_lanePick.Visible) _lanePick.Select(Math.Clamp(s.Lane, 1, _lanePick.ItemCount) - 1);
        if (_mode.Visible) _mode.ButtonPressed = !string.IsNullOrEmpty(s.Mode);
        _editing = s;
        _addButton.Text = Lang.F("window.save_step", "Save step {number}", ("number", index + 1));
        _cancelEdit.Visible = true;
        Say(Lang.F("window.editing_step_change_above_press", "Editing step {number}: change it above and press \"Save step {number}\" (or Cancel).{foodNote}", ("number", index + 1), ("foodNote", foodNote)));
    }

    /// <summary>Puts a goal's item, skill, node kind or event back into its pickers.</summary>
    void LoadGoal(IGoal g, PickList<Item> item, OptionButton level, OptionButton node, OptionButton ev, PickList<string> task, PickList<Item> tool)
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
                if (!task.Select(g.Subject)) task.ShowMissing(g.Subject, g.SubjectName + Lang.T("window.not_list", " (not in this list)"));
                break;
            case Until.ToolRunsOut:
                if (!tool.Select(g.Subject)) tool.ShowMissing(g.Subject, g.SubjectName + Lang.T("window.not_list", " (not in this list)"));
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
        if (index < 0 || index >= Queue.Steps.Count) { Say(Lang.T("window.select_step_first", "Select a step first.")); return; }
        int lane = Math.Clamp(Queue.Steps[index].Lane, 1, Queue.MaxLanes) % LanesOffered() + 1;
        Queue.SetLane(index, lane);
        Say(Lang.F("window.step_now_lane", "Step {number} is now in lane {lane}.", ("number", index + 1), ("lane", lane)));
        SelectRow(index);
    }

    PopupMenu _stepMenu = null!;

    /// <summary>
    /// The Time column of a step: while it runs, how long it has been at it; once done, how long it took; while it waits
    /// for its turn in a new round, in brackets, how long it took last time. Only the time it really ran counts (it
    /// stands still while the step waits for materials or a free slot, and while the queue is paused).
    /// </summary>
    static (string Text, string Tip) TimeOf(Step step, bool waitingNewRound)
    {
        string lastTime = step.LastSeconds >= 1 ? Lang.F("window.nlast_time_took", "\nLast time it took {duration}.", ("duration", Queue.Duration(step.LastSeconds))) : "";
        if (Queue.Running && step.Run == RunState.Running)
            return (Queue.Duration(step.Seconds), Lang.F("window.running_waiting_pauses_do_not", "Running for {duration} (waiting and pauses do not count).{lastTime}", ("duration", Queue.Duration(step.Seconds)), ("lastTime", lastTime)));
        if (step.Seconds >= 1 && step.Run == RunState.Done)
            return (Queue.Duration(step.Seconds), step.Status == StepStatus.Done ? Lang.F("window.took", "It took {duration}.", ("duration", Queue.Duration(step.Seconds))) : Lang.F("window.ran_before_stopped", "It ran for {duration} before it stopped.", ("duration", Queue.Duration(step.Seconds))));
        if (step.Seconds >= 1) return (Queue.Duration(step.Seconds), Lang.F("window.has_run_so_far", "It has run for {duration} so far.", ("duration", Queue.Duration(step.Seconds))));
        if (waitingNewRound && step.LastSeconds >= 1) return ($"({Queue.Duration(step.LastSeconds)})", Lang.F("window.last_time_took", "Last time it took {duration}.", ("duration", Queue.Duration(step.LastSeconds))));
        return ("", "");
    }

    /// <summary>The Est. left column of a step: its estimate and a tooltip saying how it was worked out ("" when none).</summary>
    static (string Text, string Tip) LeftOf(Planner.Estimate estimate)
    {
        if (!estimate.Known) return ("", "");
        string tip = EstimateTip(estimate);
        if (estimate.Observed) tip += Lang.T("window.estimate_tip_observed", "\nUses the XP rate measured while this step runs.");
        if (estimate.Luck) tip += Lang.T("window.estimate_tip_luck", "\nAverage for a chance drop: luck can change it a lot.");
        return (EstimateText(estimate), tip);
    }

    /// <summary>An estimate as the Time column shows it: "~12 min", or "up to 12 min" when another goal may end the step sooner.</summary>
    static string EstimateText(Planner.Estimate estimate) => estimate.Partial
        ? Lang.F("window.estimate_up_to", "up to {time}", ("time", Planner.Time(estimate.Seconds)))
        : Lang.F("window.estimate_about", "~{time}", ("time", Planner.Time(estimate.Seconds)));

    static string EstimateTip(Planner.Estimate estimate) => (estimate.Partial
        ? Lang.F("window.estimate_tip_up_to", "About {time} left at most (another goal may end it sooner).", ("time", Planner.Time(estimate.Seconds)))
        : Lang.F("window.estimate_tip", "About {time} left.", ("time", Planner.Time(estimate.Seconds))))
        + Lang.T("window.estimate_tip_how", "\nNormal game speed, your current bonuses, the planner's per-action times.");

    /// <summary>The estimated seconds a lane (or the whole queue, for null) still needs: the sum of its steps' known estimates.</summary>
    static double LaneEstimate(int? lane, Func<int, Planner.Estimate> estimateAt)
    {
        double sum = 0;
        for (int i = 0; i < Queue.Steps.Count; i++)
            if (lane == null || Math.Clamp(Queue.Steps[i].Lane, 1, Queue.MaxLanes) == lane)
                if (estimateAt(i) is { Known: true } e) sum += e.Seconds;
        return sum;
    }

    /// <summary>For tests: the estimate behind a step's Est. left column.</summary>
    public Planner.Estimate EstimateOf(int index) => _estimates.Count > index && index >= 0 ? _estimates[index] : default;

    /// <summary>For tests: what the Time column shows for a step, and its tooltip.</summary>
    public string TimeText(int index) => StepRows().FirstOrDefault(r => (int)r.GetMetadata(0) == index)?.GetText(4) ?? "";
    public string TimeTip(int index) => StepRows().FirstOrDefault(r => (int)r.GetMetadata(0) == index)?.GetTooltipText(4) ?? "";

    /// <summary>"Run from here" (button, or the right-click menu): the queue goes on from the selected step.</summary>
    void RunFromSelected()
    {
        int index = SelectedIndex();
        if (index < 0 || index >= Queue.Steps.Count) { Say(Lang.T("window.select_step_list_first_then", "Select a step in the list first, then press \"Run from here\".")); return; }
        if (Queue.RunFrom(index)) SelectRow(index);
    }

    /// <summary>For tests: "Run from here" on a step, as the button does; whether the button can be pressed; the right-click menu.</summary>
    public void PressRunFrom(int index) { SelectRow(index); RunFromSelected(); }
    public bool RunFromEnabled => !_runFrom.Disabled;
    public List<string> StepMenuItems => Enumerable.Range(0, _stepMenu.ItemCount).Select(i => _stepMenu.GetItemText(i)).ToList();
    public void PressStepMenu(int index, int id) { SelectRow(index); _stepMenu.EmitSignal(PopupMenu.SignalName.IdPressed, id); }
    /// <summary>For tests: the Equip face with a food and the skill that eats it, as the clicks do it; the skills offered.</summary>
    public bool ChooseFood(string itemId, string skillId)
    {
        _skillPick.Select(_skills.Count + 1);
        _skillPick.EmitSignal(OptionButton.SignalName.ItemSelected, _skills.Count + 1);
        if (!_target.Pick(FoodKeyPrefix + itemId)) return false;
        for (int i = 0; i < _foodSkill.ItemCount; i++)
            if (_foodSkill.GetItemMetadata(i).AsString() == skillId) { _foodSkill.Select(i); return _foodSkill.Visible; }
        return false;
    }
    public List<string> FoodSkillsShown => _foodSkill.Visible ? Enumerable.Range(0, _foodSkill.ItemCount).Select(i => _foodSkill.GetItemText(i)).ToList() : new List<string>();
    public string FoodSkillChosen => _foodSkill.Visible && _foodSkill.Selected >= 0 ? _foodSkill.GetItemText(_foodSkill.Selected) : "";
    /// <summary>For tests: what the State column shows for a step, and its tooltip.</summary>
    public string StateText(int index) => StepRows().FirstOrDefault(r => (int)r.GetMetadata(0) == index)?.GetText(3) ?? "";
    public string StateTip(int index) => StepRows().FirstOrDefault(r => (int)r.GetMetadata(0) == index)?.GetTooltipText(3) ?? "";

    void PaintLaneButton()
    {
        int index = SelectedIndex();
        if (_runFrom != null) _runFrom.Disabled = index < 0 || index >= Queue.Steps.Count;
        _laneButton.Text = index >= 0 && index < Queue.Steps.Count ? Lang.F("window.lane_2", "Lane {lane} > {next}", ("lane", Math.Clamp(Queue.Steps[index].Lane, 1, Queue.MaxLanes)), ("next", Math.Clamp(Queue.Steps[index].Lane, 1, Queue.MaxLanes) % LanesOffered() + 1)) : Lang.T("window.change_lane", "Change lane");
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

    List<Planner.Estimate> _estimates = new();

    void Repaint()
    {
        int selected = SelectedIndex();
        _estimates = new List<Planner.Estimate>();
        if (_game != null)
        {
            try { _estimates = Planner.Estimates(_game, Queue.Steps); }
            catch (Exception e) { Queue.Log($"Time estimates could not be worked out: {e.Message}"); }
        }
        Planner.Estimate EstimateAt(int i) => i < _estimates.Count ? _estimates[i] : default;
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
                heading.SetText(1, Lang.F("window.lane", "Lane {lane}", ("lane", lane)));
                heading.SetText(3, !Queue.Running ? Lang.P("window.step_count", mine.Count, "{count} step", "{count} steps") : at == null ? Lang.T("window.finished", "finished") : Lang.F("window.at_step", "at step {number}", ("number", Queue.Steps.IndexOf(at) + 1)));
                double laneSeconds = LaneEstimate(lane, EstimateAt);
                if (laneSeconds > 0)
                {
                    heading.SetText(5, Lang.F("window.estimate_about", "~{time}", ("time", Planner.Time(laneSeconds))));
                    heading.SetTooltipText(5, Lang.T("window.estimate_lane_tip", "Estimated time this lane still needs, at normal game speed. Steps whose time cannot be told in advance are not counted."));
                    heading.SetTextAlignment(5, HorizontalAlignment.Right);
                }
                for (int c = 0; c < _table.Columns; c++) { heading.SetCustomColor(c, Style.Accent); heading.SetSelectable(c, false); }
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
            row.SetText(1, Queue.Shown(step));
            row.SetText(2, step.Goal);
            // Waiting for its turn in a new round: how it ended last time stays in view ("last time: done: out of runes").
            bool last = step.State == "" && step.LastResult != "" && (Queue.Running || Queue.Paused);
            string state = last ? Lang.F("window.last_time", "last time: {lastResult}", ("lastResult", step.LastResult)) : step.State;
            row.SetText(3, state);
            // (once it runs again, the tooltip still says how it ended the last time)
            row.SetTooltipText(3, !last && Queue.Running && step.LastResult != "" && step.State != step.LastResult ? Lang.F("window.nlast_time", "{state}\nlast time: {lastResult}", ("state", state), ("lastResult", step.LastResult)) : state);
            row.SetTooltipText(1, Queue.Shown(step));
            row.SetTooltipText(2, step.Goal);
            // Time: what the step has run so far (waiting does not count), what it took, or in brackets what it took last time
            (string time, string timeTip) = TimeOf(step, (Queue.Running || Queue.Paused) && step.Run is RunState.Idle or RunState.Waiting);
            row.SetText(4, time);
            row.SetTooltipText(4, timeTip);
            row.SetTextAlignment(4, HorizontalAlignment.Right);
            row.SetCustomColor(4, current ? Style.Good : Style.Muted);
            (string left, string leftTip) = LeftOf(EstimateAt(i));
            row.SetText(5, left);
            row.SetTooltipText(5, leftTip);
            row.SetTextAlignment(5, HorizontalAlignment.Right);
            row.SetCustomColor(5, current ? Style.Good : Style.Muted);
            row.SetCustomColor(0, Style.Muted);
            row.SetCustomColor(3, step.Status == StepStatus.Skipped ? Style.Bad : current ? Style.Good : waiting ? Style.Accent : Style.Muted);
            if (current)
                for (int c = 0; c < _table.Columns; c++) row.SetCustomBgColor(c, new Color(Style.GoodDim, 0.55f));
        }
        // The Est. left column's title: what the whole queue still needs (lanes run side by side, so the longest lane).
        double total = laneMode
            ? Queue.Steps.Select(st => Math.Clamp(st.Lane, 1, Queue.MaxLanes)).Distinct().Select(l => LaneEstimate(l, EstimateAt)).DefaultIfEmpty(0).Max()
            : LaneEstimate(null, EstimateAt);
        _table.SetColumnTitle(5, total > 0
            ? Lang.F("window.column.left_total", "Left ~{time}", ("time", Planner.Time(total)))
            : Lang.T("window.column.left", "Est. left"));
        if (selected >= 0 && selected < Queue.Steps.Count) SelectRow(selected);
        else PaintLaneButton();
        Callable.From(PlaceDividers).CallDeferred();   // (a scroll bar that came or went moves the column borders)

        _run.Text = Queue.Running ? Lang.T("window.pause", "Pause") : Lang.T("window.start_queue", "Start the queue");
        if (_editing != null && !Queue.Steps.Contains(_editing)) EndEdit();   // the step being edited is gone (removed, another queue)
        _syncing = true;
        _repeat.ButtonPressed = Queue.Repeat;
        _syncing = false;
        _repeat.Text = Queue.Repeat ? Lang.T("window.start_over_at_end", "Start over at the end: ON") : Lang.T("window.start_over_at_end_off", "Start over at the end: OFF");
        PaintLaneRepeats();
        PaintRoom();
        _syncing = true;
        _idleAlways.ButtonPressed = Queue.IdleAlways;
        _gameCreate.ButtonPressed = Queue.GameCreate;
        _syncing = false;
        PaintIdle();
        PaintStop();
        PaintThen();
        if (Queue.Note != "") Say(Queue.Note);
        else if (Queue.Running) Say(Queue.Headline);
        PaintQuick();
        if (_savedForm.Visible) PaintSaved();
        if (_planForm.Visible) Replan();   // the plan counts what the queue will make: keep it in step with the queue
    }
}
