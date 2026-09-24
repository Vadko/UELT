using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using UELT.Hikaro;

namespace UELT;

public class TranslationMemoryManager
{
    public static readonly TranslationMemoryManager Instance = new();
    private TranslationMemoryManager() { }

    public ObservableCollection<TranslationMemory> Memories { get; } = new();

    private static string MemoriesFolder => Path.Combine(AppContext.BaseDirectory, "TranslationMemories");
    private static string StateFilePath => Path.Combine(MemoriesFolder, "_active.json");
    private const string Extension = ".tm.json";

    private class StateDto
    {
        public List<string> Active { get; set; } = new();
        public string WriteTarget { get; set; } = "";
    }

    public void LoadAll()
    {
        Memories.Clear();
        if (!Directory.Exists(MemoriesFolder))
        {
            Directory.CreateDirectory(MemoriesFolder);
            return;
        }

        var state = LoadState();

        foreach (var file in Directory.EnumerateFiles(MemoriesFolder, "*" + Extension))
        {
            var fileName = Path.GetFileName(file);
            var name = fileName[..^Extension.Length];
            var tm = new TranslationMemory(file, name);
            tm.Load();
            tm.IsActive = state.Active.Contains(fileName, StringComparer.OrdinalIgnoreCase);
            tm.IsWriteTarget = string.Equals(state.WriteTarget, fileName, StringComparison.OrdinalIgnoreCase);
            Memories.Add(tm);
        }
    }

    private static StateDto LoadState()
    {
        if (!File.Exists(StateFilePath)) return new StateDto();
        try
        {
            var json = File.ReadAllText(StateFilePath);
            return JsonSerializer.Deserialize<StateDto>(json) ?? new StateDto();
        }
        catch
        {
            return new StateDto();
        }
    }

    public void SaveState()
    {
        try
        {
            Directory.CreateDirectory(MemoriesFolder);
            var dto = new StateDto
            {
                Active = Memories.Where(m => m.IsActive).Select(m => Path.GetFileName(m.FilePath)).ToList(),
                WriteTarget = Path.GetFileName(Memories.FirstOrDefault(m => m.IsWriteTarget)?.FilePath ?? "")
            };
            File.WriteAllText(StateFilePath, JsonSerializer.Serialize(dto));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{ex.Message}", "Не вдалося зберегти налаштування", MessageBoxButton.OK);
        }
    }

    public TranslationMemory CreateNew(string name)
    {
        Directory.CreateDirectory(MemoriesFolder);
        string safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        string path = Path.Combine(MemoriesFolder, $"{safeName}{Extension}");

        int suffix = 1;
        while (File.Exists(path))
            path = Path.Combine(MemoriesFolder, $"{safeName}_{++suffix}{Extension}");

        var tm = new TranslationMemory(path, name) { IsActive = true };
        tm.EnsureFileExists();

        if (!Memories.Any(m => m.IsWriteTarget))
            tm.IsWriteTarget = true;

        Memories.Add(tm);
        SaveState();
        return tm;
    }

    public void DeleteMemory(TranslationMemory tm)
    {
        bool wasWriteTarget = tm.IsWriteTarget;
        Memories.Remove(tm);
        try
        {
            if (File.Exists(tm.FilePath)) File.Delete(tm.FilePath);
        }
        catch { }

        if (wasWriteTarget)
        {
            var newTarget = Memories.FirstOrDefault(m => m.IsActive) ?? Memories.FirstOrDefault();
            if (newTarget != null) newTarget.IsWriteTarget = true;
        }

        SaveState();
    }

    public void SetWriteTarget(TranslationMemory tm)
    {
        foreach (var m in Memories)
            m.IsWriteTarget = ReferenceEquals(m, tm);

        SaveState();
    }

    public List<TranslationMemoryMatch> FindMatches(string original)
    {
        var results = new List<TranslationMemoryMatch>();

        if (string.IsNullOrWhiteSpace(original))
            return results;

        var seenTranslations = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tm in Memories)
        {
            if (!tm.IsActive)
                continue;

            foreach (var translation in tm.FindTranslations(original))
            {
                if (seenTranslations.Add(translation))
                    results.Add(new TranslationMemoryMatch { Memory = tm, Translation = translation });
            }
        }
        return results;
    }

    public void RecordTranslation(string original, string translation)
    {
        if (string.IsNullOrWhiteSpace(original) || string.IsNullOrWhiteSpace(translation))
            return;

        if (translation == original)
            return;

        if (!IsMeaningfulTranslation(translation))
            return;

        var target = Memories.FirstOrDefault(m => m.IsActive && m.IsWriteTarget);
        target?.AddOrUpdate(original, translation, SettingsManager.MaxTranslationMemoryVariants);
    }

    public static bool IsMeaningfulTranslation(string translation)
    {
        if (string.IsNullOrWhiteSpace(translation))
            return false;
        translation = translation.Trim();

        return translation.Any(char.IsLetter);
    }

    private static readonly char[] ForbiddenNameChars =
        "\\/:*?\"<>|".ToCharArray();

    private static readonly string[] ReservedWindowsNames =
    {
    "CON", "PRN", "AUX", "NUL",
    "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
    "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
};

    public static bool IsValidMemoryName(string name, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Назва не може бути порожньою.";
            return false;
        }

        name = name.Trim();

        foreach (var c in name)
        {
            if (c < 32 || ForbiddenNameChars.Contains(c))
            {
                error = $"Назва містить недопустимий символ: «{c}»\nЗаборонені символи: \\ / : * ? \" < > |";
                return false;
            }
        }

        if (name.EndsWith(".") || name.EndsWith(" "))
        {
            error = "Назва не може закінчуватись крапкою або пробілом.";
            return false;
        }

        string bareName = name.Contains('.') ? name[..name.IndexOf('.')] : name;
        if (ReservedWindowsNames.Contains(bareName, StringComparer.OrdinalIgnoreCase))
        {
            error = $"«{bareName}» — зарезервоване системне ім'я, його не можна використовувати.";
            return false;
        }

        return true;
    }

    public bool MemoryNameExists(string name) =>
        Memories.Any(m => string.Equals(m.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public void SaveDirty()
    {
        foreach (var tm in Memories)
            tm.Save();
    }
}

public class TranslationMemoryMatch
{
    public TranslationMemory Memory { get; set; } = null!;
    public string Translation { get; set; } = "";
}