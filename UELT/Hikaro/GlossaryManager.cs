using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

namespace UELT.Hikaro;

public class GlossaryManager
{
    public static readonly GlossaryManager Instance = new();
    private GlossaryManager() { }

    public ObservableCollection<GlossaryEntry> Entries { get; } = new();

    private static string GlossaryPath => Path.Combine(AppContext.BaseDirectory, "Glossary.json");

    public void Load()
    {
        if (!File.Exists(GlossaryPath)) return;

        try
        {
            var json = File.ReadAllText(GlossaryPath);
            var list = JsonSerializer.Deserialize<List<GlossaryEntryDto>>(json);
            if (list == null) return;

            Entries.Clear();
            foreach (var dto in list)
            {
                var entry = new GlossaryEntry { Original = dto.Original };
                foreach (var t in dto.Translations)
                    entry.Translations.Add(t);
                Entries.Add(entry);
            }

            SpellCheckService.RebuildGlossaryCache(Entries);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не вдалося завантажити глосарій:\n{ex.Message}", "Глосарій", MessageBoxButton.OK);
        }
    }

    public void Save()
    {
        var list = Entries.Select(e => new GlossaryEntryDto
        {
            Original = e.Original,
            Translations = e.Translations.ToList()
        }).ToList();

        try
        {
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            File.WriteAllText(GlossaryPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SpellCheckService.RebuildGlossaryCache(Entries);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не вдалося зберегти глосарій:\n{ex.Message}", "Глосарій", MessageBoxButton.OK);
        }
    }

    public bool AddOrUpdate(string original, string translation)
    {
        var entry = FindEntry(original);
        if (entry == null)
        {
            var newEntry = new GlossaryEntry { Original = original };
            newEntry.Translations.Add(translation);
            Entries.Add(newEntry);
            Save();
            return false;
        }

        if (!entry.Translations.Contains(translation, StringComparer.Ordinal))
        {
            entry.Translations.Add(translation);
            Save();
        }
        return true;
    }

    public void RemoveEntry(GlossaryEntry entry)
    {
        Entries.Remove(entry);
        Save();
    }

    public void RemoveTranslation(GlossaryEntry entry, string translation)
    {
        entry.Translations.Remove(translation);
        if (entry.Translations.Count == 0)
            Entries.Remove(entry);
        Save();
    }

    public List<GlossaryMatch> FindMatches(string text)
    {
        var results = new List<GlossaryMatch>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        foreach (var entry in Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Original)) continue;

            var matches = entry.GetMatchRegex().Matches(text);

            foreach (Match m in matches)
            {
                results.Add(new GlossaryMatch
                {
                    Entry = entry,
                    MatchedText = m.Value,
                    Index = m.Index,
                    Length = m.Length
                });
            }
        }

        results.Sort((a, b) => a.Index.CompareTo(b.Index));
        return results;
    }

    public bool HasAnyMatch(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        foreach (var entry in Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Original)) continue;
            if (entry.GetMatchRegex().IsMatch(text))
                return true;
        }
        return false;
    }

    public static string ReplaceWholeWord(string source, string word, string replacement)
    {
        string escaped = Regex.Escape(word);
        string leadingBoundary = word.Length > 0 && GlossaryEntry.IsWordChar(word[0]) ? @"\b" : "";
        string trailingBoundary = word.Length > 0 && GlossaryEntry.IsWordChar(word[^1]) ? @"\b" : "";

        var pattern = $@"{leadingBoundary}{escaped}{trailingBoundary}";
        return Regex.Replace(source, pattern, replacement, RegexOptions.IgnoreCase);
    }

    private GlossaryEntry FindEntry(string original) =>
        Entries.FirstOrDefault(e =>
            string.Equals(e.Original, original, StringComparison.OrdinalIgnoreCase));

    private class GlossaryEntryDto
    {
        public string Original { get; set; } = "";
        public List<string> Translations { get; set; } = new();
    }
}

public class GlossaryMatch
{
    public GlossaryEntry Entry { get; set; } = null!;
    public string MatchedText { get; set; } = "";
    public int Index { get; set; }
    public int Length { get; set; }
}