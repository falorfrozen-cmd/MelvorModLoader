using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ActionQueue;

/// <summary>
/// The queue's words in the player's language (0.14.0). Every text in the code has a stable id with its English beside
/// it: <c>Lang.T("state.paused", "paused")</c>. A language is a file <c>lang/xx.json</c> next to the mod. Whatever the
/// file does not have, or has wrong (a {name} the English does not have, a broken entry), shows in English, so an older
/// file never breaks a newer version. The game's own names (items, skills, monsters) stay as the game writes them.
///
/// Texts with numbers or names in them are whole sentences with named places: <c>"until {amount:N0} {item} in the bank"</c>,
/// so each language can put the words in its own order. A text that depends on a number has a form for each plural
/// category of the language (Russian: one, few, many, other).
///
/// The chosen language is a plain file, language.txt, next to the mod. The lookup never calls Godot and the language
/// in use is swapped whole, so the queue's offline thread can use it too.
/// </summary>
public static class Lang
{
    public const string English = "en";

    sealed class Pack
    {
        public string Code = English, Name = "English", Plural = "en";
        public CultureInfo? Culture;   // null: the game's own (the numbers stay as they were before 0.14.0)
        public readonly Dictionary<string, string> Text = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, string>> Plurals = new(StringComparer.Ordinal);
        /// <summary>Entries already compared with their English: true when usable.</summary>
        public readonly ConcurrentDictionary<string, bool> Checked = new(StringComparer.Ordinal);
    }

    static volatile Pack s_pack = new();

    /// <summary>The mod's folder (set when the mod loads).</summary>
    public static string Dir = "";
    static string LangDir => Path.Combine(Dir, "lang");
    static string ChoiceFile => Path.Combine(Dir, "language.txt");

    /// <summary>The language in use ("en", or the name of its file without .json).</summary>
    public static string Code => s_pack.Code;
    /// <summary>The language in use, in its own words ("English", "Русский").</summary>
    public static string Name => s_pack.Name;
    public static bool IsEnglish => s_pack.Code == English;

    /// <summary>Raised after another language was chosen (on the main thread): windows and buttons build their words again.</summary>
    public static event Action? Changed;

    /// <summary>Where a translation is wrong: written to the log once each, so a translator can find it.</summary>
    static readonly ConcurrentDictionary<string, bool> s_told = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------- lookups

    /// <summary>A text in the language in use; the English when it has none, or a wrong one.</summary>
    public static string T(string id, string en) => Text(s_pack, id, en);

    static string Text(Pack p, string id, string en)
    {
        if (p.Code == English || !p.Text.TryGetValue(id, out string? text)) return en;
        return Usable(p, id, text, en) ? text : en;
    }

    /// <summary>A text with named places filled: <c>F("goal.item", "until {amount:N0} {item} in the bank", ("amount", 50), ("item", "Oak Logs"))</c>.</summary>
    public static string F(string id, string en, params (string Name, object? Value)[] args)
    {
        Pack p = s_pack;   // (one language for the words and the numbers, even if another is chosen meanwhile)
        return Filled(p, id, Text(p, id, en), en, args);
    }

    /// <summary>The text filled in; when the translation's places cannot be filled (a wrong format), the English is.</summary>
    static string Filled(Pack p, string key, string text, string en, (string Name, object? Value)[] args)
    {
        try { return Fill(p, text, args); }
        catch (FormatException) when (!ReferenceEquals(text, en))
        {
            p.Checked[key] = false;
            Tell($"{p.Code}.json, \"{key}\": a {{place:format}} cannot be written, so the English is shown");
            return Fill(p, en, args);
        }
    }

    /// <summary>
    /// A text that depends on a number: the English gives the form for one and for any other number; a language gives a
    /// form for each of its plural categories. {count} is the number itself (with the language's digit grouping).
    /// </summary>
    public static string P(string id, double count, string one, string other, params (string Name, object? Value)[] args)
    {
        Pack p = s_pack;
        string? text = null;
        if (p.Code != English && p.Plurals.TryGetValue(id, out Dictionary<string, string>? forms))
        {
            string category = Category(p.Plural, count);
            if (forms.TryGetValue(category, out string? form) && Usable(p, $"{id}#{category}", form, other)) text = form;
            else if (forms.TryGetValue("other", out string? any) && Usable(p, $"{id}#other", any, other)) text = any;
        }
        else if (p.Code != English && p.Text.TryGetValue(id, out string? single) && Usable(p, id, single, other)) text = single;
        string english = Category("en", count) == "one" ? one : other;
        text ??= english;
        var all = new (string, object?)[args.Length + 1];
        all[0] = ("count", count);
        Array.Copy(args, 0, all, 1, args.Length);
        return Filled(p, id, text, english, all);
    }

    /// <summary>A number in the language in use ("1,234" / "1 234"); "N0" unless another format is given.</summary>
    public static string N(double value, string format = "N0") => value.ToString(format, s_pack.Culture ?? CultureInfo.CurrentCulture);

    static readonly Regex Place = new(@"\{([A-Za-z_][A-Za-z0-9_]*|\d+)(:[^{}]*)?\}", RegexOptions.CultureInvariant);

    static string Fill(Pack p, string text, (string Name, object? Value)[] args)
    {
        if (args.Length == 0 || text.IndexOf('{') < 0) return text;
        IFormatProvider culture = p.Culture ?? CultureInfo.CurrentCulture;
        return Place.Replace(text, m =>
        {
            string name = m.Groups[1].Value;
            foreach ((string Name, object? Value) a in args)
            {
                if (a.Name != name) continue;
                string format = m.Groups[2].Success ? m.Groups[2].Value[1..] : "";
                return a.Value switch
                {
                    null => "",
                    double d when format == "" => d.ToString(d == Math.Floor(d) ? "N0" : "0.#", culture),
                    IFormattable f => f.ToString(format == "" ? null : format, culture),
                    _ => a.Value.ToString() ?? "",
                };
            }
            return m.Value;
        });
    }

    /// <summary>The names of the places in a text ("amount", "item"), each once.</summary>
    internal static HashSet<string> Places(string text) => Place.Matches(text).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>A translation may leave places out or repeat them, but has no place the English does not fill, and its braces pair up.</summary>
    static bool Usable(Pack p, string key, string text, string en) => p.Checked.GetOrAdd(key, _ =>
    {
        HashSet<string> mine = Places(text), english = Places(en);
        string? wrong = mine.FirstOrDefault(n => !english.Contains(n));
        string bare = Place.Replace(text, "");
        bool braces = bare.IndexOf('{') < 0 && bare.IndexOf('}') < 0;
        if (wrong == null && braces) return true;
        Tell($"{p.Code}.json, \"{key}\": " + (wrong != null ? $"{{{wrong}}} is not in the English, so the English is shown" : "a { or } has no partner, so the English is shown"));
        return false;
    });

    static void Tell(string what)
    {
        if (!s_told.TryAdd(what, true)) return;
        try { Queue.Log?.Invoke("Action Queue language: " + what); } catch { }
    }

    // ---------------------------------------------------------------- plural categories (CLDR cardinal rules)

    /// <summary>The plural rule a language follows, from its code ("ru", "pt-BR"...).</summary>
    static string RuleOf(string code)
    {
        string lang = code.Split('-', '_')[0].ToLowerInvariant();
        return lang switch
        {
            "ru" or "uk" or "be" => "ru",
            "pl" => "pl",
            "cs" or "sk" => "cs",
            "fr" => "fr",
            "pt" => code.Split('-', '_').Skip(1).FirstOrDefault()?.ToUpperInvariant() == "PT" ? "en" : "fr",
            "zh" or "ja" or "ko" or "vi" or "th" or "id" or "ms" => "none",
            _ => "en",
        };
    }

    /// <summary>The plural category of a number in a rule: one, few, many or other.</summary>
    internal static string Category(string rule, double n)
    {
        bool whole = n == Math.Floor(n) && !double.IsInfinity(n);
        long i = (long)Math.Floor(Math.Abs(n));
        long i10 = i % 10, i100 = i % 100;
        switch (rule)
        {
            case "none": return "other";
            case "fr": return i is 0 or 1 ? "one" : whole && i != 0 && i % 1000000 == 0 ? "many" : "other";
            case "ru":
                if (!whole) return "other";
                if (i10 == 1 && i100 != 11) return "one";
                if (i10 is >= 2 and <= 4 && i100 is < 12 or > 14) return "few";
                return "many";
            case "pl":
                if (!whole) return "other";
                if (i == 1) return "one";
                if (i10 is >= 2 and <= 4 && i100 is < 12 or > 14) return "few";
                return "many";
            case "cs":
                if (!whole) return "many";
                return i == 1 ? "one" : i is >= 2 and <= 4 ? "few" : "other";
            default: return whole && i == 1 ? "one" : "other";
        }
    }

    // ---------------------------------------------------------------- the languages there are, and the choice

    /// <summary>English, then every language file in lang/ that can be read (en.json is the English template, not a language).</summary>
    public static List<(string Code, string Name)> Available()
    {
        var all = new List<(string, string)> { (English, "English") };
        try
        {
            if (Directory.Exists(LangDir))
                foreach (string file in Directory.GetFiles(LangDir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    string code = Path.GetFileNameWithoutExtension(file);
                    if (code.Equals(English, StringComparison.OrdinalIgnoreCase)) continue;
                    if (Read(code, out Pack? pack, out string why)) all.Add((code, pack!.Name));
                    else Tell($"{code}.json could not be read, so it is not offered: {why}");
                }
        }
        catch (Exception e) { Tell("the lang folder could not be read: " + e.Message); }
        return all;
    }

    /// <summary>Uses the language chosen last time (English when none was, or its file is gone).</summary>
    public static void Load()
    {
        string code = English;
        try { if (File.Exists(ChoiceFile)) code = File.ReadAllText(ChoiceFile).Trim(); } catch { }
        Use(code, out _);
    }

    /// <summary>Chooses a language and keeps the choice. False (and English) when its file cannot be read: why says what is wrong.</summary>
    public static bool Choose(string code, out string why)
    {
        why = "";
        if (code != "" && !code.Equals(English, StringComparison.OrdinalIgnoreCase) && !Read(code, out _, out why))
        {
            Tell($"{code}.json could not be used ({why}), so the language stays {s_pack.Name}");
            return false;
        }
        Use(code, out why);
        try { File.WriteAllText(ChoiceFile, s_pack.Code); } catch { }
        Changed?.Invoke();
        return true;
    }

    static bool Use(string code, out string why)
    {
        why = "";
        if (code == "" || code.Equals(English, StringComparison.OrdinalIgnoreCase)) { s_pack = new Pack(); return true; }
        if (Read(code, out Pack? pack, out why)) { s_pack = pack!; return true; }
        Tell($"{code}.json could not be used ({why}), so the queue speaks English");
        s_pack = new Pack();
        return false;
    }

    /// <summary>
    /// Reads lang/code.json: "_meta" (name, culture, plural), then id: "text" or id: { "one": ..., "other": ... }. Names
    /// that start with "_" are notes for translators.
    /// </summary>
    static bool Read(string code, out Pack? pack, out string why)
    {
        pack = null;
        why = "";
        string file = Path.Combine(LangDir, code + ".json");
        if (code.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !File.Exists(file)) { why = "no such file"; return false; }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8),
                new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { why = "the file is not a { ... } list"; return false; }
            var p = new Pack { Code = code, Name = code, Plural = RuleOf(code) };
            string culture = code;
            foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
            {
                if (entry.Name == "_meta" && entry.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty m in entry.Value.EnumerateObject())
                    {
                        if (m.Value.ValueKind != JsonValueKind.String) continue;
                        string value = m.Value.GetString() ?? "";
                        if (value == "") continue;
                        if (m.Name == "name") p.Name = value;
                        else if (m.Name == "culture") culture = value;
                        else if (m.Name == "plural")
                        {
                            if (value is "en" or "ru" or "pl" or "cs" or "fr" or "none") p.Plural = value;
                            else Tell($"{code}.json: the plural rule '{value}' is not known (en, ru, pl, cs, fr or none), so the one for '{code}' is used");
                        }
                    }
                    continue;
                }
                if (entry.Name.StartsWith('_')) continue;
                if (entry.Value.ValueKind == JsonValueKind.String) { if (entry.Value.GetString() is { Length: > 0 } text) p.Text[entry.Name] = text; }
                else if (entry.Value.ValueKind == JsonValueKind.Object)
                {
                    var forms = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (JsonProperty f in entry.Value.EnumerateObject())
                        if (f.Value.ValueKind == JsonValueKind.String && f.Value.GetString() is { Length: > 0 } text) forms[f.Name] = text;
                    if (forms.Count > 0) p.Plurals[entry.Name] = forms;
                }
            }
            try { p.Culture = CultureInfo.GetCultureInfo(culture); }
            catch (Exception) { p.Culture = null; Tell($"{code}.json: the culture '{culture}' is not known, so numbers are written as in English"); }
            pack = p;
            return true;
        }
        catch (Exception e)
        {
            why = e is JsonException ? "it is not valid JSON: " + e.Message : e.Message;
            return false;
        }
    }
}
