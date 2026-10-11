namespace UELT.Cli.Infra;

/// <summary>One input game file. <see cref="RelPath"/> is relative to the folder it was found in
/// (or just the file name for a file given directly) and is used to mirror the tree in outputs.</summary>
public sealed record InputFile(string FullPath, string RelPath, string Display, bool FromFolder = false)
{
    public string Ext => Path.GetExtension(FullPath).ToLowerInvariant();
    public string BaseName => Path.GetFileNameWithoutExtension(FullPath);
    public string RelDir => Path.GetDirectoryName(RelPath) ?? "";
    public bool IsLocres => Ext == ".locres";
    public bool IsUasset => Ext is ".uasset" or ".umap";
}

public static class Files
{
    public static readonly string[] GameExtensions = { ".locres", ".uasset", ".umap" };

    /// <summary>Collects input files exactly like the GUI "Відкрити теку": recursive scan, filtered by
    /// extension (case-insensitive), sorted with OrdinalIgnoreCase.</summary>
    /// <param name="outputOption">The -o value of the command: files under it are not inputs (an output folder
    /// inside the input folder would otherwise be processed again on the next run).</param>
    public static (List<InputFile> Files, bool IsBatch) CollectInputs(IReadOnlyList<string> paths, string[] extensions = null, string outputOption = null)
    {
        string exclude = string.IsNullOrEmpty(outputOption) || outputOption == "-"
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputOption)) + Path.DirectorySeparatorChar;
        extensions ??= GameExtensions;
        var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
        var result = new List<InputFile>();
        bool anyFolder = false;

        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                anyFolder = true;
                string root = Path.GetFullPath(p);
                var found = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                    .Where(f => extSet.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (exclude != null)
                {
                    int before = found.Count;
                    found.RemoveAll(f => f.StartsWith(exclude, PathComparison));
                    if (found.Count < before)
                        Log.Warn($"Вихідна тека «{outputOption}» лежить усередині «{p}»: {Commands.Common.Plural(before - found.Count, "файл", "файли", "файлів")} у ній не обробляються як вхідні.");
                }

                if (found.Count == 0)
                    throw new CliException($"У теці «{p}» не знайдено файлів {string.Join(", ", extensions)}.");

                foreach (var f in found)
                {
                    string rel = Path.GetRelativePath(root, f);
                    result.Add(new InputFile(f, rel, Path.Combine(p, rel), FromFolder: true));
                }
            }
            else if (File.Exists(p))
            {
                if (!extSet.Contains(Path.GetExtension(p)))
                    throw new CliException($"Непідтримуваний тип файлу: {p} (підтримуються {string.Join(", ", extensions)}).");
                result.Add(new InputFile(Path.GetFullPath(p), Path.GetFileName(p), p));
            }
            else
            {
                throw new CliException($"Файл або теку не знайдено: {p}");
            }
        }

        return (result, anyFolder || result.Count > 1);
    }

    public static bool LooksLikeDirectory(string path)
        => Directory.Exists(path) || path.EndsWith('/') || path.EndsWith('\\');

    public static void EnsureParentDir(string path)
    {
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), PathComparison);

    /// <summary>"-o -" (stdout) is supported only by unpack; elsewhere it would create a file named "-".</summary>
    public static void RejectStdout(string outputOption)
    {
        if (outputOption == "-")
            throw new UsageException("-o - (вивід у stdout) підтримує лише unpack. Вкажіть файл або теку.");
    }

    /// <summary>Refuses to overwrite an existing file unless allowed.</summary>
    public static void EnsureWritable(string path, bool overwrite)
    {
        if (!overwrite && File.Exists(path))
            throw new CliException($"Файл уже існує: {path} (додайте --overwrite, щоб перезаписати).");
    }

    /// <summary>Same as <see cref="EnsureWritable"/> but also checks the .uexp companion of .uasset/.umap.</summary>
    public static void EnsureWritableGame(string path, bool overwrite)
    {
        EnsureWritable(path, overwrite);
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".uasset" or ".umap")
            EnsureWritable(Path.ChangeExtension(path, ".uexp"), overwrite);
    }
}

/// <summary>Output names for exported text files (unpack, tm apply --export, ...).</summary>
public sealed class ExportNamer
{
    private readonly string _out;
    private readonly bool _batch, _flat, _overwrite;
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public ExportNamer(string outOption, bool isBatch, bool flat, bool overwrite)
    {
        Files.RejectStdout(outOption);
        if (flat && string.IsNullOrEmpty(outOption))
            throw new UsageException("--flat потребує -o <тека>.");
        if (isBatch && !string.IsNullOrEmpty(outOption) && File.Exists(outOption))
            throw new UsageException($"-o має бути текою для кількох файлів, а «{outOption}» — це файл.");
        _out = outOption;
        _batch = isBatch;
        _flat = flat;
        _overwrite = overwrite;
    }

    public string Resolve(InputFile f, string newExt)
    {
        string fileName = f.BaseName + newExt;
        string path;

        if (!_batch && !_flat)
        {
            path = string.IsNullOrEmpty(_out)
                ? Path.Combine(Path.GetDirectoryName(f.Display) ?? "", fileName)
                : Files.LooksLikeDirectory(_out) ? Path.Combine(_out, fileName) : _out;
        }
        else if (_flat)
        {
            // GUI "Пакетно": flat folder, <base>.ext, then <base>_2.ext, <base>_3.ext ... on name clashes.
            path = Path.Combine(_out, fileName);
            int counter = 2;
            while (_used.Contains(path) || (!_overwrite && File.Exists(path)))
                path = Path.Combine(_out, $"{f.BaseName}_{counter++}{newExt}");
        }
        else
        {
            string dir = string.IsNullOrEmpty(_out)
                ? Path.GetDirectoryName(f.Display) ?? ""
                : Path.Combine(_out, f.RelDir);
            path = Path.Combine(dir, fileName);

            if (_used.Contains(path))
            {
                int counter = 2;
                string original = path;
                while (_used.Contains(path))
                    path = Path.Combine(dir, $"{f.BaseName}_{counter++}{newExt}");
                Log.Warn($"{f.Display}: ім’я «{original}» уже використано в цьому запуску, записую у «{path}».");
            }
        }

        _used.Add(path);
        return path;
    }
}

/// <summary>Output names for rewritten game files (pack, replace, tm apply, locres operations).</summary>
public sealed class GameOutputNamer
{
    private readonly string _out;
    private readonly bool _batch, _inPlace;

    public GameOutputNamer(string outOption, bool inPlace, bool isBatch)
    {
        Files.RejectStdout(outOption);
        if (inPlace && !string.IsNullOrEmpty(outOption))
            throw new UsageException("--in-place і -o не можна використовувати разом.");
        if (isBatch && !inPlace && string.IsNullOrEmpty(outOption))
            throw new UsageException("Для теки або кількох файлів вкажіть -o <тека> або --in-place.");
        if (isBatch && !string.IsNullOrEmpty(outOption) && File.Exists(outOption))
            throw new UsageException($"-o має бути текою для кількох файлів, а «{outOption}» — це файл.");
        _out = outOption;
        _inPlace = inPlace;
        _batch = isBatch;
    }

    public bool InPlace => _inPlace;

    public string Resolve(InputFile f)
    {
        if (_inPlace) return f.FullPath;
        if (_batch) return Path.Combine(_out, f.RelPath);

        if (string.IsNullOrEmpty(_out))
            // GUI "Зберегти як..." suggestion: <base>_N<ext> next to the original.
            return Path.Combine(Path.GetDirectoryName(f.Display) ?? "", f.BaseName + "_N" + Path.GetExtension(f.FullPath));

        return Files.LooksLikeDirectory(_out) ? Path.Combine(_out, Path.GetFileName(f.FullPath)) : _out;
    }

    /// <summary>Checks that <paramref name="outPath"/> (and its .uexp, and its .STATUS with --status) may be written.</summary>
    public void CheckWritable(InputFile f, string outPath, bool overwrite, bool status = false)
    {
        if (_inPlace && Files.SamePath(outPath, f.FullPath)) return;
        Files.EnsureWritableGame(outPath, overwrite);
        // RowStatusManager.Save rewrites (or deletes, when no row has a status) <out>.STATUS.
        if (status)
            Files.EnsureWritable(Path.ChangeExtension(outPath, ".STATUS"), overwrite);
    }
}
