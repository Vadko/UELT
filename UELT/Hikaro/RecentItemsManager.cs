using System.IO;

namespace UELT.Hikaro;

public class RecentItemsManager<T>
{
    private readonly int _maxItems;
    private readonly string _storagePath;
    private readonly Func<T, string> _serialize;
    private readonly Func<string, T> _deserialize;
    private readonly Func<T, bool> _validate;
    private readonly IEqualityComparer<T> _comparer;

    private List<T> _items = new();

    public IReadOnlyList<T> Items => _items.AsReadOnly();

    public RecentItemsManager(string fileName, int maxItems = 20, Func<T, string> serialize = null, Func<string, T> deserialize = null, Func<T, bool> validate = null, IEqualityComparer<T> comparer = null)
    {
        _maxItems = maxItems;

        string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hikaro", "UELT");
        Directory.CreateDirectory(appDataFolder);
        _storagePath = Path.Combine(appDataFolder, fileName);

        _serialize = serialize ?? (item => item?.ToString() ?? "");
        _deserialize = deserialize ?? (s => (T)(object)s);
        _validate = validate ?? (item => item != null);
        _comparer = comparer ?? EqualityComparer<T>.Default;

        Load();
    }

    public void Add(T item)
    {
        if (item == null) return;

        _items.RemoveAll(x => _comparer.Equals(x, item));
        _items.Insert(0, item);

        if (_items.Count > _maxItems)
            _items = _items.Take(_maxItems).ToList();

        Save();
    }

    public void Remove(T item)
    {
        int removed = _items.RemoveAll(x => _comparer.Equals(x, item));
        if (removed > 0)
            Save();
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    public List<T> GetAll() => _items.ToList();

    private void Load()
    {
        if (!File.Exists(_storagePath)) return;

        try
        {
            var lines = File.ReadAllLines(_storagePath);
            _items = lines
                .Select(_deserialize)
                .Where(_validate)
                .Take(_maxItems)
                .ToList();
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var lines = _items.Select(_serialize);
            File.WriteAllLines(_storagePath, lines);
        }
        catch { }
    }
}

public static class RecentManagers
{
    public static RecentItemsManager<string> ForFiles()
        => new RecentItemsManager<string>("recent.txt", 12, validate: f => File.Exists(f), comparer: StringComparer.OrdinalIgnoreCase);

    public static RecentItemsManager<string> ForFolders()
        => new RecentItemsManager<string>("recent_folders.txt", 12, validate: d => Directory.Exists(d), comparer: StringComparer.OrdinalIgnoreCase);

    public static RecentItemsManager<string> ForSearches()
        => new RecentItemsManager<string>("searches.txt", 15, validate: s => !string.IsNullOrWhiteSpace(s));

    public static RecentItemsManager<string> ForMappings()
        => new RecentItemsManager<string>("recent_mappings.txt", 20, validate: f => File.Exists(f), comparer: StringComparer.OrdinalIgnoreCase);
}