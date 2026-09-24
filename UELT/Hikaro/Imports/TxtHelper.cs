using System.IO;
using System.Text;
using UELT.Core;

namespace UELT.Hikaro;

public static class TxtHelper
{
    private const string SpecialHeader = "[~NAMES-INCLUDED~]//Don't edit or remove this line.";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        text = text
            .Replace("\\r\\n", "\r\n")
            .Replace("\\r", "\r")
            .Replace("\\n", "\n")
            .Replace("\\t", "\t");

        return AssetHelper.ReplaceBreaklines(text);
    }

    public static List<(string Id, string Text)> ParseIDFormat(string filePath)
    {
        var result = new List<(string, string)>();

        foreach (var line in File.ReadAllLines(filePath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                continue;

            int sep = line.IndexOf('=');
            if (sep <= 0) continue;

            string id = line.Substring(0, sep);
            string text = sep < line.Length - 1 ? line.Substring(sep + 1) : "";
            text = NormalizeText(text);

            if (!string.IsNullOrWhiteSpace(id))
                result.Add((id, text));
        }

        return result;
    }

    public static void ExportSimple(string filePath, List<DataGridItem> items)
    {
        var lines = items.Select(item => item.Translation ?? "").ToList();
        File.WriteAllLines(filePath, lines, Utf8NoBom);
    }

    public static void ExportWithIDFull(string filePath, List<DataGridItem> items)
    {
        var lines = items.Select(item =>
            $"{item.ID ?? ""}={item.Translation ?? ""}"
        ).ToList();

        File.WriteAllLines(filePath, lines, Utf8NoBom);
    }

    public static void ExportSelectedWithID(string filePath, List<DataGridItem> items)
    {
        var lines = items.Select(item =>
            $"{item.ID ?? ""}={item.Translation ?? ""}"
        ).ToList();

        File.WriteAllLines(filePath, lines, Utf8NoBom);
    }

    public static (int filesExported, List<string> failed) BatchExportWithID(string outputFolder, IEnumerable<(string FilePath, List<DataGridItem> Rows)> tabs)
    {
        int filesExported = 0;
        var failed = new List<string>();

        foreach (var (filePath, rows) in tabs)
        {
            if (rows.Count == 0) continue;

            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string outPath = Path.Combine(outputFolder, baseName + ".txt");

            if (File.Exists(outPath))
            {
                int counter = 2;
                while (File.Exists(outPath))
                    outPath = Path.Combine(outputFolder, $"{baseName}_{counter++}.txt");
            }

            try
            {
                ExportWithIDFull(outPath, rows);
                filesExported++;
            }
            catch
            {
                failed.Add(Path.GetFileName(filePath));
            }
        }

        return (filesExported, failed);
    }

    #region Імпорт
    private static bool HasIDFormat(string[] lines, int startIndex)
    {
        int linesToCheck = Math.Min(10, lines.Length - startIndex);

        for (int i = startIndex; i < startIndex + linesToCheck; i++)
        {
            if (i < lines.Length && lines[i].Contains('='))
            {
                return true;
            }
        }

        return false;
    }

    public static int ImportByRowOrder(List<DataGridItem> items, string filePath)
    {
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        int changedCount = 0;
        int startIndex = 0;

        if (lines.Length > 0 && lines[0] == SpecialHeader)
        {
            startIndex = 1;
        }

        bool hasIDFormat = HasIDFormat(lines, startIndex);

        if (hasIDFormat)
        {
            for (int i = 0; i < Math.Min(lines.Length - startIndex, items.Count); i++)
            {
                var line = lines[i + startIndex];
                int separatorIndex = line.IndexOf('=');

                string newTranslation = separatorIndex >= 0 && separatorIndex < line.Length - 1
                    ? line.Substring(separatorIndex + 1)
                    : line;
                newTranslation = NormalizeText(newTranslation);

                if (items[i].Translation != newTranslation)
                {
                    items[i].Translation = newTranslation;
                    items[i].IsModified = newTranslation != items[i].Text;
                    changedCount++;
                }
            }
        }
        else
        {
            for (int i = 0; i < Math.Min(lines.Length - startIndex, items.Count); i++)
            {
                string newTranslation = NormalizeText(lines[i + startIndex]);

                if (items[i].Translation != newTranslation)
                {
                    items[i].Translation = newTranslation;
                    items[i].IsModified = newTranslation != items[i].Text;
                    changedCount++;
                }
            }
        }

        return changedCount;
    }

    public static int ImportByID(List<DataGridItem> items, string filePath)
    {
        var lines = File.ReadAllLines(filePath, Encoding.UTF8);
        int changedCount = 0;
        int startIndex = 0;

        if (lines.Length > 0 && lines[0] == SpecialHeader)
        {
            startIndex = 1;
        }

        if (!HasIDFormat(lines, startIndex))
        {
            throw new InvalidOperationException("Файл не містить ID. Використовуйте імпорт за порядком рядків.");
        }

        var itemsDict = items
            .Where(item => !string.IsNullOrWhiteSpace(item.ID))
            .ToDictionary(item => item.ID, item => item);

        for (int i = startIndex; i < lines.Length; i++)
        {
            var line = lines[i];

            int separatorIndex = line.IndexOf('=');

            if (separatorIndex > 0)
            {
                string id = line.Substring(0, separatorIndex);
                string translation = separatorIndex < line.Length - 1
                    ? line.Substring(separatorIndex + 1)
                    : "";
                translation = NormalizeText(translation);

                if (itemsDict.TryGetValue(id, out var item))
                {
                    if (item.Translation != translation)
                    {
                        item.Translation = translation;
                        item.IsModified = translation != item.Text;
                        changedCount++;
                    }
                }
            }
        }

        return changedCount;
    }

    public static int ImportByIDFromFolder(string currentFilePath, string sourceFolderPath, List<DataGridItem> items)
    {
        string baseName = Path.GetFileNameWithoutExtension(currentFilePath);

        string matchedPath = Directory.GetFiles(sourceFolderPath, "*.txt", SearchOption.AllDirectories)
            .FirstOrDefault(f => ImportFileNameMatcher.IsMatch(baseName, Path.GetFileNameWithoutExtension(f)));

        if (matchedPath == null)
            return -1;

        return ImportByID(items, matchedPath);
    }
    #endregion

}