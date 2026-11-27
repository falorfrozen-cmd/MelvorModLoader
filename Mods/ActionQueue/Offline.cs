using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2;
using GamesByMalcs.Melvor2.Audio;
using GamesByMalcs.Melvor2.Core;
using GamesByMalcs.Melvor2.Data;
using GamesByMalcs.Melvor2.Entities;
using GamesByMalcs.Melvor2.Entities.Combat;
using GamesByMalcs.Melvor2.Entities.Views;
using GamesByMalcs.Melvor2.Localization;
using Godot;

namespace ActionQueue;

/// <summary>
/// Lets the queue go on while the game computes offline progress. The game does that on a thread of its own, with
/// the scene tree paused and its pages deaf to the game's events; mods get no frames then. So the queue gets a
/// heartbeat inside the game's own tick instead: one small object in the game's list of things that tick, which
/// once a game second, and only while the game ticks away from the main thread, gives the queue its turn - on the
/// game's thread, between two ticks, so nothing else touches the game at that moment.
/// </summary>
static class Offline
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_TickingEntities")]
    static extern List<ITickingEntity> TickingEntities(Game game);

    static Game? s_game;
    static Heartbeat? s_beat;
    static MusicGuard? s_guard;

    /// <summary>For tests: game seconds the heartbeat saw offline.</summary>
    public static double Seconds { get; private set; }

    /// <summary>
    /// Puts the heartbeat into this character's game (on the main thread, outside a tick): last in the game's list of
    /// things that tick, and the fight's music guard first, so the music is held before anything ticks offline.
    /// </summary>
    public static void Attach(Game game)
    {
        if (ReferenceEquals(s_game, game) && s_beat != null) return;
        // (another character: the last one's fight gets its music listener back; the new character set its own music)
        if (s_held) ReleaseFightMusic(setMusic: false);
        s_fight = null;
        s_music = null;
        s_musicFailed = false;
        try
        {
            if (s_game != null)
            {
                if (s_beat != null) TickingEntities(s_game).Remove(s_beat);
                if (s_guard != null) TickingEntities(s_game).Remove(s_guard);
            }
            s_game = game;
            s_beat = new Heartbeat(game);
            s_guard = new MusicGuard(game);
            List<ITickingEntity> list = TickingEntities(game);
            list.Insert(0, s_guard);
            list.Add(s_beat);
        }
        catch (Exception e)
        {
            s_beat = null;
            s_guard = null;
            Queue.Log?.Invoke("Offline progress for the queue is not available: " + e.Message);
        }
    }

    static void Beat(Game game)
    {
        // The game ticks on the main thread while it is played. Off it, it is catching up: offline progress at load,
        // after the computer slept, or a lag the game makes good. (Game.Flags.IsOffline is only set for some of
        // those, so the thread decides.) The queue gets a turn per game second, time measured in the game's ticks.
        if (Targets.OnMainThread) { s_pending = 0; return; }
        s_pending += Constants.TickInterval / 1000.0;
        if (s_pending < 1.0) return;
        double seconds = s_pending;
        s_pending = 0;
        Seconds += seconds;
        try { Queue.OfflineTick(game, seconds); }
        catch (Exception e) { Queue.Log?.Invoke("The queue failed during offline progress: " + e.Message); }
    }

    static double s_pending;

    // ---- the fight's music, held while the game ticks away from the main thread
    // A fight that starts or ends (Idle and back) has the game switch its music list: it plays a new track and puts
    // up the "now playing" notice, from whatever thread it is on. Away from the main thread Godot refuses the notice
    // ("only allowed from the main thread") and the music notice stays broken until the game is restarted. The game
    // does that itself when a character dies in an offline fight. So, while the game ticks away from the main thread,
    // the fight's own music listener (the game's StateChangedHandler) is taken off; once the game ticks on the main
    // thread again it is put back and the music list set once for how things are then. With it off the queue may
    // start and end fights offline (0.16.0); if it cannot be found (a game update), fights wait for the game as before.
    // The guard ticks first in every game tick, so the hold is made before a fight can end in the first offline tick.
    static CombatActionComponent? s_fight;
    static EventHandler<ValueChangedEventArgs<CombatActionState>>? s_music;
    static FieldInfo? s_stateChanged;
    static bool s_musicFailed;
    static int s_musicTaken;
    static volatile bool s_held;

    /// <summary>
    /// True while the game works out offline progress with the fight's music held: the queue may start and end fights
    /// then. False on the main thread, and offline when the hold could not be made.
    /// </summary>
    public static bool FightsAllowed => s_held && !Targets.OnMainThread;

    /// <summary>For tests: no hold, as after a game update that renamed the listener (fights wait offline).</summary>
    public static bool TestNoHold { get; set; }

    static void GuardMusic(Game game)
    {
        if (Targets.OnMainThread) { if (s_held) ReleaseFightMusic(setMusic: true); }
        else if (!s_held) HoldFightMusic(game);
    }

    static void HoldFightMusic(Game game)
    {
        if (s_musicFailed || TestNoHold) return;
        try
        {
            if (s_fight == null || s_music == null)
            {
                if (!game.Entities.TryGetObjectById("MelvorBase:Combat", out GameEntity? e) || e is not CombatEntity combat || combat.Action is not CombatActionComponent fight)
                    throw new InvalidOperationException("no fight in this game");
                MethodInfo handler = typeof(CombatActionComponent).GetMethod("StateChangedHandler", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new MissingMethodException("CombatActionComponent.StateChangedHandler");
                s_stateChanged = typeof(CombatActionComponent).GetField("StateChanged", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? throw new MissingFieldException("CombatActionComponent.StateChanged");
                s_music = (EventHandler<ValueChangedEventArgs<CombatActionState>>)Delegate.CreateDelegate(typeof(EventHandler<ValueChangedEventArgs<CombatActionState>>), fight, handler);
                s_fight = fight;
            }
            // Every copy of it that is there is taken off (and later put back). None there means the game hooks its music
            // some other way now (an update): then fights wait offline, as before 0.16.0.
            while (s_stateChanged!.GetValue(s_fight) is Delegate list && list.GetInvocationList().Contains(s_music))
            {
                s_fight.StateChanged -= s_music;
                s_musicTaken++;
            }
            if (s_musicTaken == 0) throw new InvalidOperationException("the fight's music listener is not where it was");
            s_held = true;
        }
        catch (Exception e)
        {
            s_musicFailed = true;
            Queue.Log?.Invoke("Fights wait for the game to be open (the fight's music could not be held offline): " + e.Message);
        }
    }

    static void ReleaseFightMusic(bool setMusic)
    {
        s_held = false;
        try
        {
            if (s_fight == null || s_music == null) return;
            for (; s_musicTaken > 0; s_musicTaken--) s_fight.StateChanged += s_music;
            if (setMusic) AudioManager.SetPlaylist(s_fight.State == CombatActionState.Idle ? "NonCombat" : "Combat");
        }
        catch (Exception e) { Queue.Log?.Invoke("The fight's music could not be given back: " + e.Message); }
    }

    /// <summary>The heartbeat (last in the list): the queue's turn.</summary>
    sealed class Heartbeat(Game game) : Ticker(game, "OfflineHeartbeat")
    {
        public override void Tick() => Beat(Game);
    }

    /// <summary>The fight's music guard (first in the list): holds the music offline, gives it back on the main thread.</summary>
    sealed class MusicGuard(Game game) : Ticker(game, "OfflineMusicGuard")
    {
        public override void Tick() => GuardMusic(Game);
    }

    /// <summary>A thing in the game's tick list: it borrows its name and namespace from the game's own time-of-day entity.</summary>
    abstract class Ticker(Game game, string localId) : ITickingEntity
    {
        readonly GameEntity _donor = game.TimeAndWeather;
        protected Game Game { get; } = game;
        public abstract void Tick();
        public LocalizedString DisplayName => _donor.DisplayName;
        public string IconPath => "";
        public Texture2D Icon => null!;
        public Texture2D Background => null!;
        public bool RequiresAttention => false;
        public event EventHandler<ValueChangedEventArgs<bool>>? RequiresAttentionChanged { add { } remove { } }
        public BaseActiveEntityView? GetActiveView() => null;
        public void SelectCurrentAction() { }
        public string Id => "ActionQueue:" + localId;
        public DataNamespace Namespace => _donor.Namespace;
        public string LocalId => localId;
    }
}
