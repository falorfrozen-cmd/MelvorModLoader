using System;
using System.IO;
using System.Text.Json;

namespace BulkSell;

/// <summary>
/// Bulk Sell's two files hold the lists of every character, and each write puts the whole file back. So a file that
/// cannot be read must never count as "empty": the next write would then take every other character's list with it.
/// </summary>
static class Files
{
    static readonly JsonSerializerOptions s_indented = new() { WriteIndented = true };

    /// <summary>
    /// What the file holds; a new T when there is no file. A file that is there and cannot be understood is first set
    /// aside under another name ("....unreadable-20261125-061500"), so nothing in it is ever written over.
    /// </summary>
    public static T Read<T>(string path, Action<string> log) where T : new()
    {
        if (!File.Exists(path)) return new T();
        string text;
        try { text = File.ReadAllText(path); }
        catch (IOException)
        {
            // busy for a moment (a cloud folder, a virus scanner): once more, and then the caller's error handling
            System.Threading.Thread.Sleep(80);
            text = File.ReadAllText(path);
        }
        try { return JsonSerializer.Deserialize<T>(text) ?? new T(); }
        catch (JsonException e)
        {
            string aside = path + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try { File.Copy(path, aside, true); } catch (Exception) { aside = ""; }
            log($"{Path.GetFileName(path)} could not be understood ({e.Message}); " + (aside != "" ? "it was kept as " + Path.GetFileName(aside) : "it could not be set aside") + " and a new one is started.");
            return new T();
        }
    }

    /// <summary>The whole file at once: written next to it first and then moved over it, so a crash half way leaves the old file.</summary>
    public static void Write<T>(string path, T value)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, s_indented));
        File.Move(temp, path, true);
    }
}
