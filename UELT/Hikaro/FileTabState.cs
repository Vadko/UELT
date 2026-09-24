using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using UELT.Core;

namespace UELT.Hikaro;

public partial class FileTabState : INotifyPropertyChanged, IDisposable
{
    public IAsset Asset { get; set; }
    private string _filePath = "";
    public string FilePath
    {
        get => _filePath;
        set
        {
            if (_filePath == value) return;
            _filePath = value;
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(Header));
        }
    }
    public string FileType { get; set; } = "";
    public bool HadStatusFileOnLoad { get; set; } = false;

    public string UassetEngineVersion { get; set; } = "";
    public bool UassetUsedUsmap { get; set; } = false;
    public string UassetUsmapPath { get; set; } = "";
    public MappingType UassetMappingType { get; set; } = MappingType.None;
    public string UassetJmapPath { get; set; } = "";

    public ObservableCollection<DataGridItem> DataRows { get; set; } = [];
    public ICollectionView DataGridView { get; set; }

    private bool _hasUnsavedChanges;
    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        set
        {
            if (_hasUnsavedChanges == value) return;
            _hasUnsavedChanges = value;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(Header));
        }
    }

    public UndoRedoManager UndoRedo { get; set; } = new();
    public string EditingTranslationBefore { get; set; } = null;

    public SearchManager SearchManager { get; set; } = new();
    public bool SearchWasFilteredSearch { get; set; } = false;

    public GridRowFilter ActiveGridFilter { get; set; } = GridRowFilter.None;

    public int CachedTotalWords { get; set; } = 0;
    public int CachedTranslatedRows { get; set; } = 0;
    public int CachedTranslatedWords { get; set; } = 0;
    public int CachedApprovedRows { get; set; } = 0;
    public int CachedApprovedWords { get; set; } = 0;
    public bool StatsDirty { get; set; } = true;

    public int SelectedRowIndex { get; set; } = -1;
    public double ScrollOffset { get; set; } = 0;
    public bool SearchPanelVisible { get; set; } = false;
    public string SearchStatusText { get; set; } = "";

    [System.Text.RegularExpressions.GeneratedRegex(@"VER_UE(\d+)_(\d+)(EA)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex EngineVersionRegex();
    public CancellationTokenSource GlossaryCts { get; set; }
    public CancellationTokenSource TranslationMemoryCts { get; set; }

    public string Header
    {
        get
        {
            if (string.IsNullOrEmpty(FilePath))
                return "Новий файл";

            string name = Path.GetFileName(FilePath);
            string unsaved = HasUnsavedChanges ? "*" : "";
            return $"{unsaved}{name}";
        }
    }

    public string WindowTitle(string toolName)
    {
        if (string.IsNullOrEmpty(FilePath))
            return toolName;

        string fileName = System.IO.Path.GetFileName(FilePath);
        string unsaved = HasUnsavedChanges ? "*" : "";

        string extra = "";
        if ((FileType == ".uasset" || FileType == ".umap") && !string.IsNullOrEmpty(UassetEngineVersion))
        {
            string verLabel = FormatEngineVersion(UassetEngineVersion);
            string mappingTypeStr = UassetMappingType switch
            {
                MappingType.Usmap => "+usmap",
                MappingType.Jmap => "+jmap",
                MappingType.JmapGz => "+jmap.gz",
                MappingType.Unknown => "+unknown",
                _ => ""
            };

            extra = $"{verLabel}{mappingTypeStr}";
            extra = $" [{extra}]";
        }

        string cv2 = (FileType == ".locres" && SettingsManager.CV2DecryptEnabled)
            ? " [CV2 mode]"
            : "";

        var tmWriteTarget = TranslationMemoryManager.Instance.Memories.FirstOrDefault(m => m.IsActive && m.IsWriteTarget);
        string tm = tmWriteTarget != null ? $" [TM:{tmWriteTarget.Name}]" : "";
        return $"{unsaved}{toolName}{extra}{cv2}{tm} - {fileName}";
    }

    internal static string FormatEngineVersion(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var m = EngineVersionRegex().Match(raw);
        if (!m.Success) return raw;
        string ea = m.Groups[3].Success ? "EA" : "";
        return $"UE{m.Groups[1].Value}.{m.Groups[2].Value}{ea}";
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void InitCollectionView()
    {
        DataGridView = CollectionViewSource.GetDefaultView(DataRows);
        DataGridView.MoveCurrentToPosition(-1);
    }

    private void ClearFileState()
    {
        Asset = null;
        FilePath = "";
        FileType = "";
        UassetEngineVersion = "";
        UassetUsmapPath = "";
        DataRows.Clear();
        UndoRedo?.Clear();
        SearchManager?.ClearSearch();
        EditingTranslationBefore = null;
    }

    public void Reset()
    {
        ClearFileState();
        UassetUsedUsmap = false;
        UassetMappingType = MappingType.None;
        HasUnsavedChanges = false;
        SearchWasFilteredSearch = false;
        SearchPanelVisible = false;
        SearchStatusText = "";
        ActiveGridFilter = GridRowFilter.None;
        CachedTotalWords = 0;
        CachedTranslatedRows = 0;
        CachedTranslatedWords = 0;
        CachedApprovedRows = 0;
        CachedApprovedWords = 0;
        SelectedRowIndex = -1;
        ScrollOffset = 0;
        DataGridView?.Filter = null;
        InitCollectionView();
        OnPropertyChanged(nameof(Header));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        PropertyChanged = null;

        foreach (var row in DataRows)
            row.OriginalStringData = null;

        ClearFileState();
        DataGridView = null;
        GlossaryCts?.Cancel();
        GlossaryCts?.Dispose();
        GlossaryCts = null;
        TranslationMemoryCts?.Cancel();
        TranslationMemoryCts?.Dispose();
        TranslationMemoryCts = null;
    }
}