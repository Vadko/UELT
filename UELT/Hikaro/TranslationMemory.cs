using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace UELT;

public class TranslationMemory : INotifyPropertyChanged
{
    public string FilePath { get; }

    private string _name = "";
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive == value) return; _isActive = value; OnPropertyChanged(nameof(IsActive)); }
    }

    private bool _isWriteTarget;
    public bool IsWriteTarget
    {
        get => _isWriteTarget;
        set { if (_isWriteTarget == value) return; _isWriteTarget = value; OnPropertyChanged(nameof(IsWriteTarget)); }
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, List<string>> _entries = new(StringComparer.Ordinal);
    private bool _dirty;

    public TranslationMemory(string filePath, string name)
    {
        FilePath = filePath;
        Name = name;
    }

    public void Load()
    {
        if (!File.Exists(FilePath)) return;

        try
        {
            var json = File.ReadAllText(FilePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            if (dict == null) return;

            lock (_lock)
            {
                _entries.Clear();
                foreach (var kv in dict)
                    _entries[kv.Key] = kv.Value;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не вдалося завантажити пам’ять «{Name}»:\n{ex.Message}", "Помилка", MessageBoxButton.OK);
        }
    }

    public void Save()
    {
        Dictionary<string, List<string>> snapshot;
        lock (_lock)
        {
            if (!_dirty)
                return;

            snapshot = new Dictionary<string, List<string>>(_entries, StringComparer.Ordinal);
            _dirty = false;
        }

        string tempPath = FilePath + ".tmp";

        try
        {
            using (var stream = File.Create(tempPath))
            {
                JsonSerializer.Serialize(stream, snapshot, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
            }

            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            { }

            lock (_lock)
            {
                _dirty = true;
            }

            MessageBox.Show($"Не вдалося зберегти пам’ять «{Name}»:\n{ex.Message}", "Помилка", MessageBoxButton.OK);
        }
    }

    public List<string> FindTranslations(string original)
    {
        if (string.IsNullOrWhiteSpace(original)) return new List<string>();
        original = original.Trim();
        lock (_lock)
            return _entries.TryGetValue(original, out var list) ? new List<string>(list) : new List<string>();
    }

    public bool TryGetFirstTranslation(string original, out string translation)
    {
        translation = null;
        if (string.IsNullOrWhiteSpace(original)) return false;
        original = original.Trim();

        lock (_lock)
        {
            if (_entries.TryGetValue(original, out var list) && list.Count > 0)
            {
                translation = list[0];
                return true;
            }
        }
        return false;
    }

    public bool HasMatch(string original)
    {
        if (string.IsNullOrWhiteSpace(original)) return false;
        lock (_lock)
            return _entries.ContainsKey(original.Trim());
    }

    public void AddOrUpdate(string original, string translation, int maxVariants = 5)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(translation)) return;
        original = original.Trim();
        maxVariants = Math.Clamp(maxVariants, 1, 10);

        lock (_lock)
        {
            if (!_entries.TryGetValue(original, out var list))
            {
                list = new List<string>();
                _entries[original] = list;
            }

            list.Remove(translation);
            list.Insert(0, translation);
            if (list.Count > maxVariants) list.RemoveRange(maxVariants, list.Count - maxVariants);

            _dirty = true;
        }
    }

    public void EnsureFileExists()
    {
        if (!File.Exists(FilePath))
            File.WriteAllText(FilePath, "{}", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}