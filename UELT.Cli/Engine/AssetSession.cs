using UAssetAPI.UnrealTypes;
using UELT.Cli.Infra;
using UELT.Core;
using UELT.Core.locres;
using UELT.Hikaro;

namespace UELT.Cli.Engine;

/// <summary>
/// An opened game file and its rows: the CLI equivalent of the GUI FileTabState +
/// MainWindow.LoadFileIntoTab / CreateBackupList / CreateBackupListUasset / SaveFileCore.
/// </summary>
public sealed class AssetSession
{
    public InputFile File { get; }
    public string FileType { get; }
    public IAsset Asset { get; private set; }
    public LocresFile Locres => Asset as LocresFile;
    public UassetFile Uasset => Asset as UassetFile;
    public List<DataGridItem> Rows { get; } = new();
    public List<string> Languages { get; private set; } = new();
    public string SelectedLanguage { get; private set; }
    public bool HadStatusFile { get; private set; }
    public List<string> Warnings { get; private set; } = new();
    public AssetOptions Options { get; }
    /// <summary>Engine version the asset was opened with (null = auto-detected, GUI tab.UassetEngineVersion "").</summary>
    public EngineVersion? UsedEngine { get; private set; }
    /// <summary>Mappings file used to open the asset (only for unversioned assets opened with --engine).</summary>
    public string UsedMappings { get; private set; }

    public bool IsLocres => FileType == ".locres";
    public bool IsUasset => FileType is ".uasset" or ".umap";

    private AssetSession(InputFile file, AssetOptions options)
    {
        File = file;
        FileType = file.Ext;
        Options = options;
    }

    /// <summary>Opens a file the same way the GUI does. <paramref name="batch"/> relaxes the --lang check
    /// (an unknown language becomes "all languages" with a warning, like cancelling the GUI dialog).</summary>
    public static AssetSession Open(InputFile file, AssetOptions options, bool batch)
    {
        var s = new AssetSession(file, options);

        if (s.IsLocres)
        {
            s.Asset = OpenLocres(file.FullPath, options);
            s.BuildLocresRows();
        }
        else if (s.IsUasset)
        {
            var uf = OpenUasset(file.FullPath, options, out var usedEngine, out var usedMappings);
            s.UsedEngine = usedEngine;
            s.UsedMappings = usedMappings;
            if (options.Engine.HasValue && !usedEngine.HasValue)
                Log.Info($"{file.Display}: версійований ассет — версію визначено автоматично, --engine/--mappings не потрібні.");
            s.Asset = uf;
            s.ApplyLanguage(uf, batch);
            s.BuildUassetRows();
            s.Warnings = uf.GetWarnings();
        }
        else
        {
            throw new CliException($"Непідтримуваний тип файлу: {Path.GetExtension(file.FullPath)}");
        }

        if (options.Status)
            s.LoadStatuses();

        return s;
    }

    public static LocresFile OpenLocres(string path, AssetOptions options)
        => options.Cv2
            ? new LocresFile(path, forceEncrypted: true, options.Cv2KeySet)
            : new LocresFile(path, forceEncrypted: false);

    /// <summary>
    /// Opens a .uasset/.umap like MainWindow.LoadFileIntoTab: auto-detection first; --engine/--mappings are
    /// used only when the asset turns out to be unversioned (UNVERSIONED_ASSET_NEEDS_VERSION), exactly like the
    /// GUI asks for a version (EngineVersionWindow) only in that case. Versioned assets ignore --engine.
    /// </summary>
    public static UassetFile OpenUasset(string path, AssetOptions options)
        => OpenUasset(path, options, out _, out _);

    public static UassetFile OpenUasset(string path, AssetOptions options, out EngineVersion? usedEngine, out string usedMappings)
    {
        usedEngine = null;
        usedMappings = null;
        try
        {
            return LoadUasset(path, null, null, MappingType.None);
        }
        catch (Exception ex) when (IsUnversioned(ex))
        {
            if (!options.Engine.HasValue)
                throw new NeedsEngineException(path);
        }

        try
        {
            var uf = LoadUasset(path, options.Engine, options.MappingsPath, options.MappingType);
            usedEngine = options.Engine;
            usedMappings = options.MappingsPath;
            return uf;
        }
        catch (Exception ex) when (IsUnversioned(ex))
        {
            throw new CliException($"не вдалося відкрити {Path.GetFileName(path)} з версією {options.Engine}: {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    /// <summary>
    /// Opens a uasset/umap translation or original source for <paramref name="session"/> like
    /// UassetImportHelper.LoadSourceTextDict: with the version (and mappings) the tab was opened with, or
    /// auto-detected when the tab itself was auto-detected (falling back to --engine for an unversioned source).
    /// </summary>
    public static UassetFile OpenUassetSource(string path, AssetSession session)
    {
        if (!session.UsedEngine.HasValue)
            return OpenUasset(path, session.Options);

        try
        {
            return LoadUasset(path, session.UsedEngine, session.UsedMappings, session.Options.MappingType);
        }
        catch (Exception ex) when (IsUnversioned(ex))
        {
            throw new CliException($"не вдалося відкрити {Path.GetFileName(path)} з версією {session.UsedEngine}: {ex.InnerException?.Message ?? ex.Message}", ex);
        }
    }

    private static UassetFile LoadUasset(string path, EngineVersion? engine, string mappings, MappingType mappingType)
    {
        try
        {
            return mappings != null
                ? new UassetFile(path, engine, mappings, mappingType)
                : new UassetFile(path, engine);
        }
        catch (Exception ex) when (!IsUnversioned(ex))
        {
            // UassetFile wraps the UAssetAPI error as "Не вдалося завантажити 'X':\n<english text>".
            string detail = ErrorText.Known(ex.InnerException ?? ex) ?? (ex.InnerException ?? ex).Message;
            string hint = engine.HasValue ? $" Перевірте --engine ({EngineVersionParser.Format(engine.Value.ToString())}) і --mappings." : "";
            throw new CliException($"не вдалося завантажити {Path.GetFileName(path)}: {detail}.{hint}", ex);
        }
    }

    private static bool IsUnversioned(Exception ex) => ex.Message.Contains("UNVERSIONED_ASSET_NEEDS_VERSION");

    private void ApplyLanguage(UassetFile uf, bool batch)
    {
        Languages = uf.GetAvailableLanguages();

        // The GUI asks only when there is more than one language (FinishLoadingUasset).
        if (Languages.Count <= 1)
        {
            if (Options.Language != null && Languages.Count == 1
                && !string.Equals(Languages[0], Options.Language, StringComparison.OrdinalIgnoreCase))
                Log.Warn($"{File.Display}: мова «{Options.Language}» відсутня (є лише {Languages[0]}); --lang проігноровано.");
            return;
        }

        if (Options.Language == null)
        {
            // Same as cancelling LanguageSelectWindow: all languages.
            Log.Info($"{File.Display}: кілька мов ({string.Join(", ", Languages)}); показано всі. Оберіть одну через --lang.");
            uf.SetSelectedLanguage(null);
            return;
        }

        string canonical = Languages.FirstOrDefault(l => string.Equals(l, Options.Language, StringComparison.OrdinalIgnoreCase));
        if (canonical == null)
        {
            string msg = $"мову «{Options.Language}» не знайдено. Доступні: {string.Join(", ", Languages)}.";
            if (!batch) throw new CliException(msg);
            Log.Warn($"{File.Display}: {msg} Використано всі мови.");
            uf.SetSelectedLanguage(null);
            return;
        }

        SelectedLanguage = canonical;
        uf.SetSelectedLanguage(canonical);
    }

    /// <summary>MainWindow.CreateBackupList.</summary>
    private void BuildLocresRows()
    {
        Rows.Clear();
        int i = 0;
        foreach (var ns in Locres)
        {
            var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in ns)
            {
                string baseId = string.IsNullOrEmpty(ns.Name) ? entry.Key : $"{ns.Name}::{entry.Key}";
                keyCount.TryGetValue(baseId, out int c);
                string fullId = c == 0 ? baseId : $"{baseId}[{c}]";
                keyCount[baseId] = c + 1;

                Rows.Add(new LocresDataGridItem
                {
                    ID = fullId,
                    Text = entry.Value,
                    Translation = entry.Value,
                    Index = i++,
                    OriginalStringData = new List<string> { fullId, entry.Value },
                    HashTable = new HashTable(ns.NameHash, entry.KeyHash, entry.ValueHash) { ExternID = entry.ExternID },
                    StringTableEntry = entry
                });
            }
        }
    }

    /// <summary>MainWindow.CreateBackupListUasset. ExtractTexts also fills the write-back map ImportTexts needs.</summary>
    private void BuildUassetRows()
    {
        Rows.Clear();
        var strings = Asset.ExtractTexts();
        if (strings == null) return;

        int i = 0;
        foreach (var item in strings)
        {
            Rows.Add(new DataGridItem
            {
                ID = item.Count > 0 ? item[0] : $"Row_{i}",
                Text = item.Count > 1 ? item[1] : "",
                Translation = item.Count > 1 ? item[1] : "",
                Index = i,
                OriginalStringData = new List<string>(item)
            });
            i++;
        }
    }

    /// <summary>Re-reads rows after structural edits of a locres (add/remove/merge).</summary>
    public void RebuildLocresRows(IReadOnlyDictionary<string, string> keepTranslations = null)
    {
        BuildLocresRows();
        if (keepTranslations == null) return;
        foreach (var r in Rows)
            if (keepTranslations.TryGetValue(r.ID, out var t))
                r.Translation = t;
    }

    private void LoadStatuses()
    {
        string statusPath = RowStatusManager.GetStatusFilePath(File.FullPath);
        if (!System.IO.File.Exists(statusPath)) return;
        RowStatusManager.LoadFromFile(statusPath, Rows, useIndexKeys: IsUasset);
        HadStatusFile = true;
    }

    public int ModifiedCount => Rows.Count(r => r.IsModified);

    /// <summary>MainWindow.SaveFileCore + BuildUpdatedStrings: every row is written back.</summary>
    public void Save(string outPath)
    {
        var updated = new List<List<string>>(Rows.Count);
        foreach (var item in Rows)
        {
            if (IsLocres)
                updated.Add(new List<string> { item.OriginalStringData?[0] ?? "", item.Translation ?? "" });
            else
                updated.Add(new List<string> { item.Index.ToString(), item.OriginalStringData?[0] ?? "", item.Translation ?? "" });
        }

        Files.EnsureParentDir(outPath);
        Asset.ImportTexts(updated);
        Asset.SaveFile(outPath);

        if (Options.Status)
            RowStatusManager.Save(outPath, Rows, useIndexKeys: IsUasset);
    }
}
