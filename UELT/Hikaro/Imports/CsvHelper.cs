using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.Configuration.Attributes;
using System.Globalization;
using System.IO;
using System.Text;
using UELT.Core;

namespace UELT.Hikaro;

public static class CSVHelper
{
    public static void ExportToCSV(string filePath, List<DataGridItem> items)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Encoding = new UTF8Encoding(false),
            HasHeaderRecord = true,
            ShouldQuote = args => true
        };

        using (var writer = new StreamWriter(filePath, false, new UTF8Encoding(false)))
        using (var csv = new CsvWriter(writer, config))
        {
            csv.WriteField("key");
            csv.WriteField("source");
            csv.WriteField("Translation");
            csv.NextRecord();

            foreach (var item in items)
            {
                csv.WriteField(item.ID);
                csv.WriteField(item.Text);
                csv.WriteField(item.IsModified ? item.Translation : "");
                csv.NextRecord();
            }
        }
    }

    public static (int filesExported, List<string> failed) ExportToCSVBatch(string outputFolder, IEnumerable<(string FilePath, List<DataGridItem> Rows)> tabs)
    {
        int filesExported = 0;
        var failed = new List<string>();

        foreach (var (filePath, rows) in tabs)
        {
            if (rows.Count == 0) continue;

            string baseName = Path.GetFileNameWithoutExtension(filePath);
            string outPath = Path.Combine(outputFolder, baseName + ".csv");

            if (File.Exists(outPath))
            {
                int counter = 2;
                while (File.Exists(outPath))
                    outPath = Path.Combine(outputFolder, $"{baseName}_{counter++}.csv");
            }

            try
            {
                ExportToCSV(outPath, rows);
                filesExported++;
            }
            catch
            {
                failed.Add(Path.GetFileName(filePath));
            }
        }

        return (filesExported, failed);
    }

    public static List<CSVImportItem> ImportFromCSV(string filePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Encoding = new UTF8Encoding(false),
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null
        };

        using (var reader = new StreamReader(filePath, new UTF8Encoding(false)))
        using (var csv = new CsvReader(reader, config))
        {
            var records = csv.GetRecords<CSVImportItem>().ToList();
            return records;
        }
    }

    public static int ImportByRowOrder(List<DataGridItem> dataRows, List<CSVImportItem> csvData)
    {
        int maxIndex = Math.Min(dataRows.Count, csvData.Count);
        int changedCount = 0;

        for (int i = 0; i < maxIndex; i++)
        {
            if (!string.IsNullOrWhiteSpace(csvData[i].Translation))
            {
                string translation = AssetHelper.ReplaceBreaklines(csvData[i].Translation);
                if (dataRows[i].Translation != translation)
                {
                    dataRows[i].Translation = translation;
                    dataRows[i].IsModified = dataRows[i].Translation != dataRows[i].Text;
                    changedCount++;
                }
            }
        }

        return changedCount;
    }

    public static int ImportByID(List<DataGridItem> dataRows, List<CSVImportItem> csvData)
    {
        var csvDict = csvData
            .Where(x => !string.IsNullOrWhiteSpace(x.Translation))
            .ToDictionary(x => x.Key, x => AssetHelper.ReplaceBreaklines(x.Translation));

        int changedCount = 0;

        foreach (var item in dataRows)
        {
            if (csvDict.TryGetValue(item.ID, out string translation))
            {
                if (item.Translation != translation)
                {
                    item.Translation = translation;
                    item.IsModified = item.Translation != item.Text;
                    changedCount++;
                }
            }
        }

        return changedCount;
    }

    public static int ImportByOriginalText(List<DataGridItem> dataRows, List<CSVImportItem> csvData)
    {
        var csvDict = csvData
            .Where(x => !string.IsNullOrWhiteSpace(x.Translation))
            .GroupBy(x => x.Source)
            .ToDictionary(g => g.Key, g => AssetHelper.ReplaceBreaklines(g.First().Translation));

        int changedCount = 0;

        foreach (var item in dataRows)
        {
            if (csvDict.TryGetValue(item.Text, out string translation))
            {
                if (item.Translation != translation)
                {
                    item.Translation = translation;
                    item.IsModified = item.Translation != item.Text;
                    changedCount++;
                }
            }
        }

        return changedCount;
    }

    public static int ImportByIDAndOriginalText(List<DataGridItem> dataRows, List<CSVImportItem> csvData)
    {
        var csvDict = csvData
            .Where(x => !string.IsNullOrWhiteSpace(x.Translation))
            .ToDictionary(x => (x.Key, x.Source), x => AssetHelper.ReplaceBreaklines(x.Translation));

        int changedCount = 0;

        foreach (var item in dataRows)
        {
            if (csvDict.TryGetValue((item.ID, item.Text), out string translation))
            {
                if (item.Translation != translation)
                {
                    item.Translation = translation;
                    item.IsModified = item.Translation != item.Text;
                    changedCount++;
                }
            }
        }

        return changedCount;
    }

    public static int ImportByRowOrderFromFolder(string currentFilePath, string sourceFolderPath, List<DataGridItem> items)
    {
        string matchedPath = FindMatchingCSV(currentFilePath, sourceFolderPath);
        if (matchedPath == null) return -1;

        var csvData = ImportFromCSV(matchedPath);
        return ImportByRowOrder(items, csvData);
    }

    public static int ImportByIDFromFolder(string currentFilePath, string sourceFolderPath, List<DataGridItem> items)
    {
        string matchedPath = FindMatchingCSV(currentFilePath, sourceFolderPath);
        if (matchedPath == null) return -1;

        var csvData = ImportFromCSV(matchedPath);
        return ImportByID(items, csvData);
    }

    public static int ImportByOriginalTextFromFolder(string currentFilePath, string sourceFolderPath, List<DataGridItem> items)
    {
        string matchedPath = FindMatchingCSV(currentFilePath, sourceFolderPath);
        if (matchedPath == null) return -1;

        var csvData = ImportFromCSV(matchedPath);
        return ImportByOriginalText(items, csvData);
    }

    public static int ImportByIDAndOriginalTextFromFolder(string currentFilePath, string sourceFolderPath, List<DataGridItem> items)
    {
        string matchedPath = FindMatchingCSV(currentFilePath, sourceFolderPath);
        if (matchedPath == null) return -1;

        var csvData = ImportFromCSV(matchedPath);
        return ImportByIDAndOriginalText(items, csvData);
    }

    private static string FindMatchingCSV(string currentFilePath, string sourceFolderPath)
    {
        string baseName = Path.GetFileNameWithoutExtension(currentFilePath);
        return Directory.GetFiles(sourceFolderPath, "*.csv", SearchOption.AllDirectories)
            .FirstOrDefault(f => ImportFileNameMatcher.IsMatch(baseName, Path.GetFileNameWithoutExtension(f)));
    }
}

public class CSVImportItem
{
    [Name("key")]
    public string Key { get; set; } = "";

    [Name("source")]
    public string Source { get; set; } = "";

    [Name("Translation")]
    public string Translation { get; set; } = "";
}