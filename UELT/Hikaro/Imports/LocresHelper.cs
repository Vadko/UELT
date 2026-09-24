using System.IO;
using UELT.Core;
using UELT.Core.locres;

namespace UELT.Hikaro;

public static class LocresHelper
{
    public static int ImportFromLocres(IList<DataGridItem> dataRows, string locresFilePath)
    {
        var locresFile = SettingsManager.CV2DecryptEnabled
            ? new LocresFile(locresFilePath, forceEncrypted: true, SettingsManager.CV2IsDemo ? CV2KeySet.Demo : CV2KeySet.Release)
            : new LocresFile(locresFilePath, forceEncrypted: false);

        if (!locresFile.IsGood)
            return 0;

        var locresStrings = locresFile.ExtractTexts();
        var locresDict = locresStrings
            .Where(x => x.Count >= 2 && !string.IsNullOrWhiteSpace(x[1]))
            .ToDictionary(x => x[0], x => x[1]);

        int changedCount = 0;

        foreach (var item in dataRows)
        {
            if (locresDict.TryGetValue(item.ID, out string translation))
            {
                if (item.Translation != translation)
                {
                    item.Translation = translation;
                    item.IsModified = item.Translation != item.Text;

                    if (item.OriginalStringData != null && item.OriginalStringData.Count > 1)
                        item.OriginalStringData[1] = translation;

                    changedCount++;
                }
            }
        }
        return changedCount;
    }

    public static int ImportFromLocresFolder(string currentFilePath, string sourceFolderPath, IList<DataGridItem> dataRows)
    {
        string baseName = Path.GetFileNameWithoutExtension(currentFilePath);

        string matchedPath = Directory.GetFiles(sourceFolderPath, "*.locres", SearchOption.AllDirectories)
            .FirstOrDefault(f => ImportFileNameMatcher.IsMatch(baseName, Path.GetFileNameWithoutExtension(f)));

        if (matchedPath == null)
            return -1;

        return ImportFromLocres(dataRows, matchedPath);
    }

    public static int ImportOriginalFromLocres(IList<DataGridItem> dataRows, string locresFilePath)
    {
        var locresFile = SettingsManager.CV2DecryptEnabled
            ? new LocresFile(locresFilePath, forceEncrypted: true, SettingsManager.CV2IsDemo ? CV2KeySet.Demo : CV2KeySet.Release)
            : new LocresFile(locresFilePath, forceEncrypted: false);

        if (!locresFile.IsGood)
            return 0;

        var locresStrings = locresFile.ExtractTexts();
        var locresDict = locresStrings
            .Where(x => x.Count >= 2 && !string.IsNullOrWhiteSpace(x[1]))
            .ToDictionary(x => x[0], x => x[1]);

        int changedCount = 0;

        foreach (var item in dataRows)
        {
            if (locresDict.TryGetValue(item.ID, out string originalText))
            {
                if (item.Text != originalText)
                {
                    item.Text = originalText;
                    item.IsModified = item.Translation != item.Text;

                    if (item.Translation != item.Text && item.Status == RowStatus.None)
                        item.Status = RowStatus.NeedsReview;

                    changedCount++;
                }
            }
        }
        return changedCount;
    }

    public static int ImportOriginalFromLocresFolder(string currentFilePath, string sourceFolderPath, IList<DataGridItem> dataRows)
    {
        string baseName = Path.GetFileNameWithoutExtension(currentFilePath);

        string matchedPath = Directory.GetFiles(sourceFolderPath, "*.locres", SearchOption.AllDirectories)
            .FirstOrDefault(f => ImportFileNameMatcher.IsMatch(baseName, Path.GetFileNameWithoutExtension(f)));

        if (matchedPath == null)
            return -1;

        return ImportOriginalFromLocres(dataRows, matchedPath);
    }

    public static (int Imported, int Unchanged) ImportHashesFromLocres(IList<DataGridItem> dataRows, string locresFilePath)
    {
        var locresFile = SettingsManager.CV2DecryptEnabled
            ? new LocresFile(locresFilePath, forceEncrypted: true, SettingsManager.CV2IsDemo ? CV2KeySet.Demo : CV2KeySet.Release)
            : new LocresFile(locresFilePath, forceEncrypted: false);

        if (!locresFile.IsGood)
            return (0, 0);

        var hashDict = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var ns in locresFile)
        {
            var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in ns)
            {
                string baseId = string.IsNullOrEmpty(ns.Name) ? e.Key : $"{ns.Name}::{e.Key}";
                keyCount.TryGetValue(baseId, out int c);
                string id = c == 0 ? baseId : $"{baseId}[{c}]";
                keyCount[baseId] = c + 1;
                hashDict[id] = e.ValueHash;
            }
        }

        int imported = 0, unchanged = 0;

        foreach (var item in dataRows)
        {
            if (item is not LocresDataGridItem locresItem || locresItem.HashTable == null)
                continue;

            if (!hashDict.TryGetValue(locresItem.ID, out uint valueHash))
                continue;

            if (locresItem.HashTable.ValueHash == valueHash)
                unchanged++;
            else
            {
                locresItem.HashTable.ValueHash = valueHash;

                if (locresItem.StringTableEntry != null)
                {
                    locresItem.StringTableEntry.ValueHash = valueHash;
                }

                imported++;
            }
        }

        return (imported, unchanged);
    }

    public static (int Imported, int Unchanged) ImportHashesFromTxt(IList<DataGridItem> dataRows, string txtFilePath)
    {
        var entries = TxtHelper.ParseIDFormat(txtFilePath);
        if (entries.Count == 0)
            return (0, 0);

        var hashDict = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var (id, text) in entries)
        {
            string decoded = AssetHelper.ReplaceBreaklines(text, Back: true);
            hashDict[id] = decoded.StrCrc32();
        }

        int imported = 0, unchanged = 0;

        foreach (var item in dataRows)
        {
            if (item is not LocresDataGridItem locresItem || locresItem.HashTable == null)
                continue;

            if (!hashDict.TryGetValue(locresItem.ID, out uint valueHash))
                continue;

            if (locresItem.HashTable.ValueHash == valueHash)
                unchanged++;
            else
            {
                locresItem.HashTable.ValueHash = valueHash;

                if (locresItem.StringTableEntry != null)
                {
                    locresItem.StringTableEntry.ValueHash = valueHash;
                }

                imported++;
            }
        }

        return (imported, unchanged);
    }
}