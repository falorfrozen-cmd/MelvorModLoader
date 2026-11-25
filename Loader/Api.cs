using System;
using System.Runtime.CompilerServices;
using GamesByMalcs.Melvor2.GameMenus;
using Godot;

namespace MelvorModLoader;

/// <summary>Every mod has one public class implementing this.</summary>
public interface IMod
{
    void OnLoad(ModContext ctx);
}

/// <summary>What the loader hands to a mod.</summary>
public sealed class ModContext
{
    public string Name { get; }
    /// <summary>Folder the mod DLL was loaded from.</summary>
    public string Directory { get; }
    public SceneTree Tree { get; }

    internal ModContext(string name, string directory, SceneTree tree)
    {
        Name = name; Directory = directory; Tree = tree;
    }

    public void Log(string message) => MelvorModLoader.Log.Write($"[{Name}] {message}");

    /// <summary>
    /// Lists a window of this mod in the loader's Mods menu (top-bar "Mods" button, F1) and opens or closes it with
    /// <paramref name="toggle"/> when its key is pressed. The player can change or switch off the key there; the
    /// choice is kept in mods\hotkeys.json. Leave <paramref name="defaultKey"/> at Key.None for a window without a
    /// key of its own (it still opens from the menu): the F keys are crowded. Show <see cref="ModWindow.KeyLabel"/>
    /// rather than a fixed key name. Added in loader 0.2.0.
    /// </summary>
    public ModWindow AddWindow(string title, Action toggle, Key defaultKey = Key.None) =>
        Hotkeys.Add(Name, title, toggle, new KeyChord(defaultKey));

    /// <summary>
    /// Raised once per rendered frame. A handler that throws is removed.
    /// Not raised while the game is computing offline progress (see <see cref="GameAccess.IsComputingOffline"/>).
    /// </summary>
    public event Action Frame
    {
        add => Loader.AddFrameHandler(this, value);
        remove => Loader.RemoveFrameHandler(value);
    }
}

/// <summary>Shortcuts into the running game. Returns null when no save is loaded.</summary>
public static class GameAccess
{
    public static T? FindNode<T>(Node? root = null) where T : Node
    {
        root ??= ((SceneTree)Engine.GetMainLoop()).Root;
        if (root is T hit) return hit;
        foreach (Node child in root.GetChildren(includeInternal: true))
        {
            T? found = FindNode<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// True while the game is computing offline progress ("Loading Offline Progress..."). During that time the
    /// game runs on a thread of its own: do not read or change it from the main thread (timers, signals, input).
    /// Game events you subscribed to are still raised then, on the game's thread, so keep those handlers free of
    /// user-interface calls.
    /// </summary>
    public static bool IsComputingOffline => ((SceneTree)Engine.GetMainLoop()).Paused;

    /// <summary>The in-game screen; exists only while a save is open.</summary>
    public static GameView? FindGameView() => FindNode<GameView>();

    // GameView keeps the loaded game in a private property; this is a direct, typed accessor for it.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Game")]
    static extern GamesByMalcs.Melvor2.Game? GetGame(GameView view);

    /// <summary>The game a view shows (for the loader itself, which keeps the view it found).</summary>
    internal static GamesByMalcs.Melvor2.Game? GameOf(GameView view) => GetGame(view);

    /// <summary>The loaded game (bank, skills, entities...), or null on the main menu.</summary>
    public static GamesByMalcs.Melvor2.Game? CurrentGame
    {
        get
        {
            GameView? view = FindGameView();
            return view == null ? null : GetGame(view);
        }
    }
}
