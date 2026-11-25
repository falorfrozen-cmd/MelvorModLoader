using System;
using System.Collections.Generic;
using System.Linq;
using CheatMenu;
using GamesByMalcs.Melvor2;
using Godot;

namespace Multitask;

/// <summary>The F5 window: how many extra slots are on (0 to 15), and a list of what is running together right now.</summary>
public sealed class SlotWindow
{
    readonly CanvasLayer _layer = new() { Layer = 118, Name = "Multitask", Visible = false };
    readonly Label _status = Style.Label("", Style.Muted);
    readonly List<Label> _lines = new();
    readonly List<Button> _drops = new();
    readonly List<Button> _locks = new();
    readonly List<HBoxContainer> _rows = new();
    readonly Label _summary = Style.Label("", Style.Text);
    Button _close = null!, _minus = null!, _plus = null!;
    Label _count = null!;
    GridContainer _grid = null!;
    CheckBox _wait = null!;

    static string CloseText => MultitaskMod.Instance?.Key.With("Close") ?? "Close";
    public string CloseLabel => _close.Text;

    public bool Visible => _layer.Visible;
    public Control Panel { get; }

    public SlotWindow(SceneTree tree)
    {
        var shade = new ColorRect { Color = new Color(0, 0, 0, 0.25f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(shade);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(760, 0), Theme = Style.Build() };
        StyleBoxFlat frame = Style.Box(Style.Window, 8, Style.AccentDim, 1);
        frame.ShadowColor = new Color(0, 0, 0, 0.55f);
        frame.ShadowSize = 14;
        panel.AddThemeStyleboxOverride("panel", frame);
        center.AddChild(panel);
        Panel = panel;

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);
        panel.AddChild(root);

        // ---- header
        StyleBoxFlat headBox = Style.Box(Style.Sidebar, 0, null, 0, 18, 12);
        headBox.CornerRadiusTopLeft = headBox.CornerRadiusTopRight = 7;
        headBox.BorderColor = Style.Line;
        headBox.BorderWidthBottom = 1;
        var head = new PanelContainer();
        head.AddThemeStyleboxOverride("panel", headBox);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        header.AddChild(Style.Label("MULTITASK", Style.Accent, 20));
        Label hint = Style.Label("Start something new and what you were doing keeps running.", Style.Muted);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.SizeFlagsVertical = Control.SizeFlags.Fill;
        hint.ClipText = true;
        header.AddChild(hint);
        _close = new Button { Text = CloseText };
        _close.Pressed += Hide;
        header.AddChild(_close);
        head.AddChild(header);
        root.AddChild(head);

        // ---- body
        var margin = new MarginContainer();
        foreach (string m in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
            margin.AddThemeConstantOverride(m, 16);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 10);
        margin.AddChild(body);
        root.AddChild(margin);

        // -- the switches
        var switchCard = new PanelContainer();
        switchCard.AddThemeStyleboxOverride("panel", Style.Box(Style.Card, 5, null, 0, 12, 10));
        var switchRow = new HBoxContainer();
        switchRow.AddThemeConstantOverride("separation", 8);
        Label slotsLabel = Style.Label("Extra slots", Style.Text);
        slotsLabel.VerticalAlignment = VerticalAlignment.Center;
        switchRow.AddChild(slotsLabel);
        _minus = new Button { Text = "-", CustomMinimumSize = new Vector2(36, 0), TooltipText = "One slot less: the oldest of your kept actions is stopped." };
        _minus.Pressed += () => Slots.SetExtra(Slots.Extra - 1);
        switchRow.AddChild(_minus);
        _count = Style.Label("0", Style.Accent, 18);
        _count.CustomMinimumSize = new Vector2(34, 0);
        _count.HorizontalAlignment = HorizontalAlignment.Center;
        _count.VerticalAlignment = VerticalAlignment.Center;
        switchRow.AddChild(_count);
        _plus = new Button { Text = "+", CustomMinimumSize = new Vector2(36, 0), TooltipText = "One slot more: one more of your recent actions keeps running." };
        _plus.Pressed += () => Slots.SetExtra(Slots.Extra + 1);
        switchRow.AddChild(_plus);
        switchRow.AddChild(new Control { CustomMinimumSize = new Vector2(6, 0) });
        foreach ((string text, int extra, string tip) in new[]
                 {
                     ("Off", 0, "No extra slot: one action at a time, the plain game."),
                     ("2", 2, "Two extra slots: your last three actions run together."),
                     ($"All ({Slots.Count})", Slots.Count, "Every slot: every action you start keeps running, all of them at once."),
                 })
        {
            var preset = new Button { Text = text, TooltipText = tip };
            preset.Pressed += () => Slots.SetExtra(extra);
            switchRow.AddChild(preset);
        }
        _summary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _summary.HorizontalAlignment = HorizontalAlignment.Right;
        switchRow.AddChild(_summary);
        switchCard.AddChild(switchRow);
        body.AddChild(switchCard);

        // -- what runs together now
        var listCard = new PanelContainer();
        listCard.AddThemeStyleboxOverride("panel", Style.Box(Style.Card, 5, null, 0, 12, 10));
        var list = new VBoxContainer();
        list.AddThemeConstantOverride("separation", 6);
        list.AddChild(Style.Label("Running together right now (your latest first):", Style.Muted, 12));
        // One column for a few lines, two when many slots are on (16 lines would not fit on a small screen).
        _grid = new GridContainer { Columns = 1 };
        _grid.AddThemeConstantOverride("h_separation", 24);
        _grid.AddThemeConstantOverride("v_separation", 6);
        for (int i = 0; i < Slots.Count + 1; i++)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 6);
            Label line = Style.Label("", Style.Text);
            line.ClipText = true;
            line.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            line.VerticalAlignment = VerticalAlignment.Center;
            int index = i;
            var drop = new Button { Text = "x", CustomMinimumSize = new Vector2(30, 0), TooltipText = "Take this action off the list. It stops and is not started again." };
            drop.Pressed += () => Slots.Drop(index);
            var lockButton = new Button
            {
                Text = "Lock", CustomMinimumSize = new Vector2(66, 0), ToggleMode = true,
                TooltipText = "Locked: starting something new never pushes this action out of its slot. One slot always stays free for switching.\n" +
                              "Its own Stop button and the x still stop it. The Action Queue runs its steps only in the slots that are not locked.",
            };
            lockButton.Toggled += on => { if (!Slots.SetLocked(index, on)) lockButton.SetPressedNoSignal(false); };
            row.AddChild(line);
            row.AddChild(lockButton);
            row.AddChild(drop);
            _lines.Add(line);
            _drops.Add(drop);
            _locks.Add(lockButton);
            _rows.Add(row);
            _grid.AddChild(row);
        }
        list.AddChild(_grid);
        _wait = new CheckBox
        {
            Text = "Keep an action on the list when its materials run out (it goes on when there are some again)",
            TooltipText = "Off: an action that runs out of materials (cooking with no fish left...) leaves the list, as if you pressed Stop."
        };
        _wait.Toggled += on => Slots.SetWaitForMaterials(on);
        list.AddChild(_wait);
        listCard.AddChild(list);
        body.AddChild(listCard);

        Label note = Style.Label(
            "Nothing to pick here. Play as usual: when you start a new action, the one before it keeps going next to it.\n" +
            $"Each extra slot keeps one more of your recent actions running: 1 slot = your last two, 2 = your last three, {Slots.Count} = all of them. When there is no room, an action whose materials ran out makes room first.\n" +
            "To drop an action, press the x next to it here, or its own Stop button in the game. A skill can only do one thing: choose another rock or recipe and it just switches.\n" +
            "Lock an action to keep it in its slot: new actions then swap only among the slots that are not locked, so you can switch quickly without stopping it by mistake (one slot always stays free).\n" +
            "An action waits when its materials run out and goes on when there are some again (unless you untick the box above). A fight is dropped if your character dies." +
            // Players looked for "lanes" here; they live in the Action Queue window, when that mod is installed.
            (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "ActionQueue")
                ? "\nWant steps in a set order (cut, then burn; fish, then cook)? Open Action Queue (F6, or the Mods button) and give steps Lane 1, Lane 2...: every lane runs its own steps in order, side by side."
                : ""),
            Style.Muted, 12);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.AddChild(note);

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

        Slots.Changed += Repaint;
        tree.Root.AddChild(_layer);
    }

    public void Show(Game game)
    {
        _close.Text = CloseText;
        Repaint();
        _layer.Visible = true;
    }

    public void Hide() => _layer.Visible = false;

    /// <summary>For tests: press a slot's switch the way a click does, and read what the window shows.</summary>
    /// Slot N "on" means N or more extra slots (see <see cref="Slots.SetOn"/>).
    public void PressSwitch(int slot) => Slots.SetOn(slot, !Slots.IsOn(slot));
    public string SwitchText(int slot) => Slots.IsOn(slot) ? "ON" : "OFF";
    public void PressPlus() => _plus.EmitSignal(BaseButton.SignalName.Pressed);
    public void PressMinus() => _minus.EmitSignal(BaseButton.SignalName.Pressed);
    public string CountText => _count.Text;
    public int Columns => _grid.Columns;
    public string LineText(int index) => _lines[index].Text;
    public bool LineShown(int index) => _rows[index].Visible;
    public bool DropShown(int index) => _rows[index].Visible && _drops[index].Visible;
    public void PressDrop(int index) => _drops[index].EmitSignal(BaseButton.SignalName.Pressed);
    /// <summary>For tests: click a row's Lock the way a click does (it toggles).</summary>
    public void PressLock(int index) => _locks[index].ButtonPressed = !_locks[index].ButtonPressed;
    public bool LockShown(int index) => _rows[index].Visible && _locks[index].Visible;
    public string LockText(int index) => _locks[index].Text;
    public void SetWait(bool on) => _wait.ButtonPressed = on;
    public bool WaitTicked => _wait.ButtonPressed;
    public string StatusText => _status.Text;

    void Repaint()
    {
        _count.Text = Slots.Extra.ToString();
        _minus.Disabled = Slots.Extra == 0;
        _plus.Disabled = Slots.Extra == Slots.Count;

        int room = Slots.Capacity;
        _grid.Columns = room > 8 ? 2 : 1;
        _summary.Text = room == 1 ? "One action at a time (the plain game)." : $"Up to {room} actions at the same time.";
        _summary.AddThemeColorOverride("font_color", room == 1 ? Style.Muted : Style.Good);

        _wait.SetPressedNoSignal(Slots.WaitForMaterials);
        IReadOnlyList<Kept> recent = Slots.Recent;
        for (int i = 0; i < _lines.Count; i++)
        {
            Label line = _lines[i];
            _rows[i].Visible = i < room;
            _drops[i].Visible = i < recent.Count;
            _locks[i].Visible = i < recent.Count && Slots.Extra > 0 && !recent[i].ByQueue;
            if (i < recent.Count)
            {
                _locks[i].SetPressedNoSignal(recent[i].Locked);
                _locks[i].Text = recent[i].Locked ? "Locked" : "Lock";
            }
            if (i >= recent.Count)
            {
                line.Text = i == 0 ? "1.  (nothing yet: start an action in the game)"
                          : _grid.Columns > 1 ? $"{i + 1}.  (free)"
                          : $"{i + 1}.  (free: the action before your next one will go here)";
                line.AddThemeColorOverride("font_color", Style.Muted);
                continue;
            }
            Kept kept = recent[i];
            string state = kept.State == "" ? "starting..." : kept.State;
            line.Text = $"{i + 1}.  {(kept.Locked ? "[locked]  " : "")}{kept.Title}   -   {state}";
            line.AddThemeColorOverride("font_color", kept.State == "running" ? Style.Good : kept.State.StartsWith("waiting") ? Style.Accent : Style.Text);
        }

        int running = recent.Count(k => k.State == "running");
        _status.Text = Slots.Note != "" ? Slots.Note
            : room == 1 ? "No extra slot: one action at a time."
            : running <= 1 ? "Start another action in the game: the one you are doing now will keep running."
            : $"{running} actions are running together." + (Slots.LockedCount > 0 ? $" {Slots.LockedCount} locked." : "");
    }
}
