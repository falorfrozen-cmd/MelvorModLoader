using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace MelvorModLoader;

/// <summary>Plain text log, rewritten on every game start: "MelvorModLoader.log" in the game folder.</summary>
public static class Log
{
    static readonly object Gate = new();
    public static string FilePath { get; private set; } = Path.Combine(Path.GetTempPath(), "MelvorModLoader.log");

    internal static void StartNewSession(string gameRoot)
    {
        // The game folder is the natural place; fall back to the temp folder if it is read-only.
        foreach (string dir in new[] { gameRoot, Path.GetTempPath() })
        {
            try
            {
                string path = Path.Combine(dir, "MelvorModLoader.log");
                File.WriteAllText(path, "");
                FilePath = path;
                return;
            }
            catch { }
        }
    }

    public static void Write(string msg)
    {
        try { lock (Gate) File.AppendAllText(FilePath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { }
    }
}

/// <summary>Starts the game exactly as its own entry point would, then starts the mod loader.</summary>
internal static class Bootstrap
{
    const string GameAssemblyName = "GamesByMalcs.Melvor2";

    public static string LoaderDir = "", GameRoot = "", GameDataDir = "", ModsDir = "";
    public static AssemblyLoadContext Alc = null!;
    static readonly List<string> ProbeDirs = new();

    public static string Version =>
        typeof(Bootstrap).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";

    // No Godot or game types in this method: they cannot be found until the resolver below is installed.
    public static void Run(nint godotDllHandle, nint outManagedCallbacks, nint unmanagedCallbacks, int unmanagedCallbacksSize)
    {
        Assembly self = typeof(Bootstrap).Assembly;
        LoaderDir = Path.GetDirectoryName(Path.GetFullPath(self.Location))!;
        GameRoot = Path.GetDirectoryName(LoaderDir)!;
        ModsDir = Path.Combine(GameRoot, "mods");
        Log.StartNewSession(GameRoot);
        Log.Write($"Melvor Mod Loader {Version} starting (.NET {System.Environment.Version})");

        GameDataDir = FindGameDataDir();
        Log.Write($"Game data folder: {GameDataDir}");

        Alc = AssemblyLoadContext.GetLoadContext(self)!;
        AddProbeDir(GameDataDir);
        Alc.Resolving += Resolve;

        StartGame(godotDllHandle, outManagedCallbacks, unmanagedCallbacks, unmanagedCallbacksSize);
        Log.Write("Game started");
        StartLoader();
    }

    // The same four steps Godot generates as the entry point of every C# game (GodotPlugins.Game.Main),
    // with the game's own assembly registered as the one that holds the game scripts.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static void StartGame(nint godotDllHandle, nint outManagedCallbacks, nint unmanagedCallbacks, int unmanagedCallbacksSize)
    {
        DllImportResolver resolver = new Godot.NativeInterop.GodotDllImportResolver(godotDllHandle).OnResolveDllImport;
        NativeLibrary.SetDllImportResolver(typeof(Godot.GodotObject).Assembly, resolver);
        Godot.NativeInterop.NativeFuncs.Initialize(unmanagedCallbacks, unmanagedCallbacksSize);
        Godot.Bridge.ManagedCallbacks.Create(outManagedCallbacks);
        Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(GamesByMalcs.Melvor2.Game).Assembly);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void StartLoader() => Loader.ScheduleStart();

    static string FindGameDataDir()
    {
        string expected = Path.Combine(GameRoot, "data_GamesByMalcs.Melvor2_windows_x86_64");
        if (File.Exists(Path.Combine(expected, GameAssemblyName + ".dll")))
            return expected;
        // macOS builds ship one data folder per architecture, and the game's DLLs are
        // ready-to-run images for that architecture: only the folder matching the running
        // process can be loaded. Directory order is arbitrary, so sort the match first.
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x86_64",
            Architecture.Arm => "arm32",
            Architecture.X86 => "x86_32",
            _ => ""
        };
        IEnumerable<string> dirs = Directory.GetDirectories(GameRoot, "data_*");
        if (arch.Length > 0)
        {
            string? match = dirs.FirstOrDefault(d => d.Contains(arch, StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(d, GameAssemblyName + ".dll")));
            if (match != null)
                return match;
        }
        foreach (string dir in dirs)
            if (File.Exists(Path.Combine(dir, GameAssemblyName + ".dll")))
                return dir;
        throw new DirectoryNotFoundException($"The game's data folder was not found ({GameAssemblyName}.dll). The mod loader must be installed into the game folder.");
    }

    public static void AddProbeDir(string dir)
    {
        if (!ProbeDirs.Contains(dir)) ProbeDirs.Add(dir);
    }

    // Called for every assembly our own runtime folder does not contain: the game's libraries and mod dependencies.
    static Assembly? Resolve(AssemblyLoadContext ctx, AssemblyName name)
    {
        foreach (string dir in ProbeDirs)
        {
            string path = Path.Combine(dir, name.Name + ".dll");
            if (File.Exists(path))
                return ctx.LoadFromAssemblyPath(path);
        }
        return null;
    }
}
