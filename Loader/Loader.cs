using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Godot;

namespace MelvorModLoader;

public static class Loader
{
    public static string Version => Bootstrap.Version;
    public static IReadOnlyList<string> LoadedMods => s_loaded;
    /// <summary>Mods whose DLL is in the mods folder and that did not start (by the DLL's name, without ".dll").</summary>
    public static IReadOnlyList<string> FailedMods => s_failed;
    static readonly List<string> s_failed = new();
    /// <summary>Mods whose frame handler threw an error and was switched off in this session.</summary>
    public static IReadOnlyList<string> StoppedMods => s_stopped;
    static readonly List<string> s_stopped = new();

    static readonly List<string> s_loaded = new();
    static readonly List<(ModContext Owner, Action Handler)> s_frame = new();
    static SceneTree s_tree = null!;

    // Deferred: runs on the first main-loop iteration, when the SceneTree exists.
    internal static void ScheduleStart() => Callable.From(Start).CallDeferred();

    static void Start()
    {
        try
        {
            s_tree = (SceneTree)Engine.GetMainLoop();
            Hotkeys.Start();
            LoadMods();
            s_tree.ProcessFrame += OnFrame;
            ShowBanner();
        }
        catch (Exception e) { Log.Write("Loader ERROR: " + e); }
    }

    static void LoadMods()
    {
        string modsDir = Bootstrap.ModsDir;
        if (!Directory.Exists(modsDir))
        {
            Log.Write($"There is no mods folder, no mods were loaded: {modsDir}");
            return;
        }
        var dlls = Directory.GetFiles(modsDir, "*.dll")
            .Concat(Directory.GetDirectories(modsDir).SelectMany(d => Directory.GetFiles(d, "*.dll")))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (string dll in dlls)
            Bootstrap.AddProbeDir(Path.GetDirectoryName(dll)!);

        foreach (string dll in dlls)
        {
            string file = Path.GetFileName(dll);
            try
            {
                Assembly asm = Bootstrap.Alc.LoadFromAssemblyPath(dll);
                // a mod that also works with another mod (when that one is installed) still loads without it
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException r) { types = r.Types.Where(t => t != null).ToArray()!; }
                foreach (Type type in types.Where(t => typeof(IMod).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false }))
                {
                    string name = asm.GetName().Name ?? file;
                    var mod = (IMod)Activator.CreateInstance(type)!;
                    mod.OnLoad(new ModContext(name, Path.GetDirectoryName(dll)!, s_tree));
                    s_loaded.Add(name);
                    Log.Write($"Mod loaded: {name} {asm.GetName().Version?.ToString(3)} ({file})");
                }
            }
            catch (Exception e)
            {
                Exception shown = e is ReflectionTypeLoadException { LoaderExceptions.Length: > 0 } r ? r.LoaderExceptions[0]! : e;
                Log.Write($"Mod FAILED to load: {file} -> {shown}");
                s_failed.Add(Path.GetFileNameWithoutExtension(file));
            }
        }
        Log.Write($"{s_loaded.Count} mod(s) loaded");
        // the player must see this, not only the log: a mod that did not start is nearly always a game update
        if (s_failed.Count > 0)
            Rescue.Note($"{(s_failed.Count == 1 ? "This mod" : "These mods")} did not start: {string.Join(", ", s_failed)}. "
                + "The game may have been updated: look for a newer version of the mod. Details: MelvorModLoader.log.", 45);
    }

    internal static void AddFrameHandler(ModContext owner, Action handler) => s_frame.Add((owner, handler));
    internal static void RemoveFrameHandler(Action handler) => s_frame.RemoveAll(h => h.Handler == handler);

    static void OnFrame()
    {
        // While the game computes offline progress it runs its ticks on a thread of its own (and pauses the
        // scene tree). Nothing else may touch the game then, so mods get no frames until it is done.
        if (s_tree.Paused) return;
        try { Hotkeys.OnFrame(s_tree); }
        catch (Exception e) { Log.Write("Hotkeys: " + e.Message); }
        // every frame: the game's own save timer runs free (every 10 s), so a save can come in the very frame after a
        // character opened, and a save written without the mod no longer holds the mod's give-back list
        Rescue.Check();
        for (int i = 0; i < s_frame.Count; i++)
        {
            var (owner, handler) = s_frame[i];
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                handler();
                // a mod that holds the game up is named in the log (the player only sees a frozen screen)
                double ms = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (ms > 250) owner.Log($"Slow frame: this mod's frame handler took {ms:0} ms.");
            }
            catch (Exception e)
            {
                // One broken mod must not spam the log or break the others.
                owner.Log("The frame handler threw an error and was switched off: " + e);
                s_frame.RemoveAt(i--);
                if (!s_stopped.Contains(owner.Name))
                {
                    s_stopped.Add(owner.Name);
                    Rescue.Note($"The mod {owner.Name} ran into an error and part of it was switched off until the game is started again. "
                        + "The game may have been updated: look for a newer version of the mod. Details: MelvorModLoader.log.", 45);
                }
            }
        }
    }

    static void ShowBanner()
    {
        var layer = new CanvasLayer { Layer = 128, Name = "MelvorModLoaderBanner" };
        var label = new Label
        {
            Text = $"Melvor Mod Loader {Version}  -  {s_loaded.Count} mod",
            Position = new Vector2(8, 4),
            Modulate = new Color(0.4f, 1f, 0.4f)
        };
        label.AddThemeFontSizeOverride("font_size", 14);
        layer.AddChild(label);
        s_tree.Root.AddChild(layer);
        s_tree.CreateTimer(15.0).Timeout += layer.QueueFree;
    }
}
