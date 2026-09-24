using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using WeCantSpell.Hunspell;

namespace UELT.Hikaro;

public static class SpellCheckService
{
    private static WordList _dictionary;
    private static bool _initialized = false;
    private static readonly Lock _lock = new();

    private static HashSet<string> _ignoredWords = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _glossaryWords = new(StringComparer.OrdinalIgnoreCase);
    private static string _rulesFilePath = "";
    private static string _dictionaryBasePath = "";

    private static readonly Regex CyrillicWordRegex = new(@"\b[а-яґєіїА-ЯҐЄІЇ]+(?:[-'ʼ\u2019][а-яґєіїА-ЯҐЄІЇ]+)*\b", RegexOptions.Compiled);

    public static bool IsAvailable => _dictionary != null;

    public static void Initialize(string basePath)
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            _initialized = true;

            _rulesFilePath = Path.Combine(basePath, "SpellCheckRules.json");
            _dictionaryBasePath = basePath;
            LoadRules();
            TryLoadDictionary();
        }
    }

    public static bool TryLoadDictionary()
    {
        if (_dictionary != null) return true;
        try
        {
            string dic = Path.Combine(_dictionaryBasePath, "uk_UA.dic");
            string aff = Path.Combine(_dictionaryBasePath, "uk_UA.aff");
            if (File.Exists(dic) && File.Exists(aff))
            {
                _dictionary = WordList.CreateFromFiles(dic, aff);
                return true;
            }
        }
        catch { }
        return false;
    }

    private static void LoadRules()
    {
        try
        {
            if (!File.Exists(_rulesFilePath))
                return;

            var json = File.ReadAllText(_rulesFilePath);
            var rules = JsonSerializer.Deserialize<SpellCheckRules>(json);

            if (rules?.IgnoredWords != null)
            {
                _ignoredWords.Clear();

                foreach (var word in rules.IgnoredWords)
                    _ignoredWords.Add(NormalizeWord(word));
            }
        }
        catch { }
    }

    public static void RebuildGlossaryCache(IEnumerable<GlossaryEntry> entries)
    {
        _glossaryWords = entries
            .SelectMany(e => e.Translations)
            .SelectMany(t => CyrillicWordRegex.Matches(t).Cast<Match>())
            .Select(m => NormalizeWord(m.Value))
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static void AddIgnoredWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return;

        word = NormalizeWord(word.Trim());

        if (!_ignoredWords.Add(word))
            return;

        SaveRules();
    }

    public static void RemoveIgnoredWord(string word)
    {
        if (_ignoredWords.Remove(NormalizeWord(word.Trim())))
            SaveRules();
    }

    public static IReadOnlyCollection<string> IgnoredWords => _ignoredWords;

    private static void SaveRules()
    {
        try
        {
            var toSave = _ignoredWords
                .Where(w => !_glossaryWords.Contains(w))
                .OrderBy(w => w)
                .ToList();

            var rules = new SpellCheckRules { IgnoredWords = toSave };
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            var json = JsonSerializer.Serialize(rules, options);
            File.WriteAllText(_rulesFilePath, json, System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private static string NormalizeWord(string w) =>
        w.Replace('\u2019', '\'').Replace('ʼ', '\'');

    public static bool HasSpellingErrors(string text)
    {
        if (_dictionary == null || string.IsNullOrWhiteSpace(text)) return false;

        foreach (Match m in CyrillicWordRegex.Matches(text))
        {
            string word = NormalizeWord(m.Value);
            if (_ignoredWords.Contains(word) || _glossaryWords.Contains(word)) continue;
            if (!_dictionary.Check(word))
                return true;
        }
        return false;
    }

    public static IEnumerable<string> GetMisspelledWords(string text)
    {
        if (_dictionary == null || string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (Match m in CyrillicWordRegex.Matches(text))
        {
            string word = NormalizeWord(m.Value);
            if (_ignoredWords.Contains(word) || _glossaryWords.Contains(word)) continue;
            if (!_dictionary.Check(word))
                yield return m.Value;
        }
    }
}

file sealed class SpellCheckRules
{
    public List<string> IgnoredWords { get; set; } = [];
}