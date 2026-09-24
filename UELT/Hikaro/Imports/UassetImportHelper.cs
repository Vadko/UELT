using System.IO;
using UELT.Core;

namespace UELT.Hikaro;

public static class UassetImportHelper
{
    private static Dictionary<string, string> LoadSourceTextDict(FileTabState tab, string sourceFilePath)
    {
        UAssetAPI.UnrealTypes.EngineVersion? engineVersion = ParseEngineVersion(tab.UassetEngineVersion);
        UassetFile sourceAsset;

        try
        {
            sourceAsset = (tab.UassetUsedUsmap && !string.IsNullOrEmpty(tab.UassetUsmapPath))
                ? new UassetFile(sourceFilePath, engineVersion, tab.UassetUsmapPath)
                : engineVersion.HasValue
                    ? new UassetFile(sourceFilePath, engineVersion)
                    : new UassetFile(sourceFilePath);
        }
        catch (Exception ex)
        {
            throw new Exception($"Не вдалося відкрити файл:\n{ex.Message}", ex);
        }

        if (!sourceAsset.IsGood)
            throw new Exception("Файл прочитано з помилками: імпорт скасовано.");

        var sourceTexts = sourceAsset.ExtractTexts();

        if (sourceTexts == null || sourceTexts.Count == 0)
            return null;

        return sourceTexts
            .Where(x => x.Count >= 2 && !string.IsNullOrWhiteSpace(x[1]))
            .ToDictionary(x => x[0], x => x[1], StringComparer.Ordinal);
    }

    private static string FindMatchedSourceFile(FileTabState tab, string sourceFolderPath)
    {
        string ext = Path.GetExtension(tab.FilePath);
        string baseName = Path.GetFileNameWithoutExtension(tab.FilePath);

        return Directory.GetFiles(sourceFolderPath, $"*{ext}", SearchOption.AllDirectories)
            .FirstOrDefault(f => ImportFileNameMatcher.IsMatch(baseName, Path.GetFileNameWithoutExtension(f)));
    }

    public static int ImportFromUasset(FileTabState tab, string sourceFilePath, IList<DataGridItem> dataRows)
    {
        var sourceDict = LoadSourceTextDict(tab, sourceFilePath);

        if (sourceDict == null)
            return 0;

        int changedCount = 0;

        foreach (var item in dataRows)
        {
            if (!sourceDict.TryGetValue(item.ID, out string newTranslation))
                continue;

            if (item.Translation == newTranslation)
                continue;

            item.Translation = newTranslation;
            item.IsModified = item.Translation != item.Text;

            if (item.OriginalStringData != null && item.OriginalStringData.Count > 1)
                item.OriginalStringData[1] = newTranslation;

            changedCount++;
        }

        return changedCount;
    }

    public static int ImportFromUassetFolder(FileTabState tab, string sourceFolderPath, IList<DataGridItem> dataRows)
    {
        string matchedPath = FindMatchedSourceFile(tab, sourceFolderPath);

        if (matchedPath == null)
            return -1;

        return ImportFromUasset(tab, matchedPath, dataRows);
    }

    public static int ImportOriginalFromUasset(FileTabState tab, string sourceFilePath, IList<DataGridItem> dataRows)
    {
        var sourceDict = LoadSourceTextDict(tab, sourceFilePath);

        if (sourceDict == null)
            return 0;

        int changedCount = 0;

        foreach (var item in dataRows)
        {
            if (!sourceDict.TryGetValue(item.ID, out string newOriginal))
                continue;

            if (item.Text == newOriginal)
                continue;

            item.Text = newOriginal;
            item.IsModified = item.Translation != item.Text;

            if (item.Translation != item.Text && item.Status == RowStatus.None)
                item.Status = RowStatus.NeedsReview;

            changedCount++;
        }

        return changedCount;
    }

    public static int ImportOriginalFromUassetFolder(FileTabState tab, string sourceFolderPath, IList<DataGridItem> dataRows)
    {
        string matchedPath = FindMatchedSourceFile(tab, sourceFolderPath);

        if (matchedPath == null)
            return -1;

        return ImportOriginalFromUasset(tab, matchedPath, dataRows);
    }

    private static UAssetAPI.UnrealTypes.EngineVersion? ParseEngineVersion(string label)
    {
        if (string.IsNullOrEmpty(label))
            return null;

        return Enum.TryParse<UAssetAPI.UnrealTypes.EngineVersion>(label, ignoreCase: true, out var ver)
            ? ver
            : null;
    }
}