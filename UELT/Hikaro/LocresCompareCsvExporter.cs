using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.IO;
using System.Text;

namespace UELT.Hikaro;

public static class LocresCompareCsvExporter
{
    public static void Export(string newFilePath, string changedFilePath, IEnumerable<(string Key, string Text)> newEntries,
        IEnumerable<(string Key, string OldText, string NewText)> changedEntries)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Encoding = new UTF8Encoding(false),
            HasHeaderRecord = true,
            ShouldQuote = _ => true
        };

        if (newFilePath != null)
        {
            using var writer = new StreamWriter(newFilePath, false, new UTF8Encoding(false));
            using var csv = new CsvWriter(writer, config);

            csv.WriteField("key");
            csv.WriteField("source");
            csv.WriteField("Translation");
            csv.NextRecord();

            foreach (var (key, text) in newEntries)
            {
                csv.WriteField(key);
                csv.WriteField(text);
                csv.WriteField("");
                csv.NextRecord();
            }
        }

        using (var writer = new StreamWriter(changedFilePath, false, new UTF8Encoding(false)))
        using (var csv = new CsvWriter(writer, config))
        {
            csv.WriteField("key");
            csv.WriteField("old_text");
            csv.WriteField("new_text");
            csv.NextRecord();

            foreach (var (key, oldText, newText) in changedEntries)
            {
                csv.WriteField(key);
                csv.WriteField(oldText);
                csv.WriteField(newText);
                csv.NextRecord();
            }
        }
    }
}