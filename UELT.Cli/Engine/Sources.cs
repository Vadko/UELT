using UELT.Cli.Commands;
using UELT.Cli.Infra;
using UELT.Core;
using UELT.Hikaro;

namespace UELT.Cli.Engine;

/// <summary>A translation (or original-text) source given on the command line: a file or a folder.</summary>
public sealed class SourceSpec
{
    public string Path { get; }
    public bool IsFolder { get; }
    /// <summary>Forced source extension (".csv", ".txt", ".locres", ".uasset", ".umap") or null.</summary>
    public string Format { get; }
    /// <summary>auto | id | order | source | id+source</summary>
    public string By { get; }
    /// <summary>TXT only: interpret literal \n, \r, \t like the GUI TXT import (--txt-escapes).</summary>
    public bool TxtEscapes { get; init; }

    private readonly Dictionary<string, List<string>> _folderCache = new(StringComparer.OrdinalIgnoreCase);

    public SourceSpec(string path, string format, string by)
    {
        if (Directory.Exists(path)) IsFolder = true;
        else if (!File.Exists(path)) throw new CliException($"Джерело не знайдено: {path}");
        Path = path;
        Format = format == null || format == "auto" ? null : "." + format.ToLowerInvariant();
        By = by ?? "auto";
    }

    public static readonly string[] Formats = { "auto", "csv", "txt", "locres", "uasset", "umap" };
    public static readonly string[] ByModes = { "auto", "id", "order", "source", "id+source" };

    /// <summary>Extensions a source may have for an original, in matching priority order.
    /// GUI: locres tabs import from *.locres; uasset/umap tabs from uasset/umap files
    /// (folder import searches *.&lt;same ext&gt;, UassetImportHelper.FindMatchedSourceFile).</summary>
    public static string[] AllowedExtensions(InputFile original, bool originalOnly, bool forFolder)
    {
        string[] game = original.IsLocres ? new[] { ".locres" }
            : forFolder ? new[] { original.Ext } : new[] { ".uasset", ".umap" };
        return originalOnly ? game : new[] { ".csv", ".txt" }.Concat(game).ToArray();
    }

    /// <summary>
    /// Finds the source file for an original. File source: used for every original.
    /// Folder source: the same relative directory first (mirrored unpack output: exact base name, then
    /// ImportFileNameMatcher), then the GUI rule (recursive search + ImportFileNameMatcher) — but the GUI
    /// rule is used only for a single input file or a flat source folder. For a folder of inputs and a
    /// source folder with subfolders, a file from another subfolder is never used (in UE localization trees
    /// every culture has the same file names), so such an input is reported as unmatched.
    /// Returns null when nothing matches (GUI: "Не знайдено відповідний файл").
    /// </summary>
    public string Resolve(InputFile original, bool originalOnly = false)
        => Resolve(original, AllowedExtensions(original, originalOnly, IsFolder));

    public string Resolve(InputFile original, string[] allowed)
    {
        if (Format != null)
        {
            if (!allowed.Contains(Format))
                throw new CliException($"Джерело формату {Format} не підходить для {original.Display}.");
            allowed = new[] { Format };
        }

        if (!IsFolder)
        {
            string ext = System.IO.Path.GetExtension(Path).ToLowerInvariant();
            if (!allowed.Contains(ext))
                throw new CliException($"Джерело {Path} не підходить для {original.Display} (очікується {string.Join(", ", allowed)}).");
            return Path;
        }

        // 1) mirrored layout: <src>/<reldir>/<base>.<ext>, then a normalised name in the same folder.
        string dir = System.IO.Path.Combine(Path, original.RelDir);
        foreach (var ext in allowed)
        {
            string candidate = System.IO.Path.Combine(dir, original.BaseName + ext);
            if (File.Exists(candidate) && !Files.SamePath(candidate, original.FullPath))
                return candidate;
        }
        if (Directory.Exists(dir))
        {
            foreach (var ext in allowed)
            {
                string normalized = ListFolder(ext)
                    .Where(f => Files.SamePath(System.IO.Path.GetDirectoryName(f), dir) && !Files.SamePath(f, original.FullPath))
                    .FirstOrDefault(f => ImportFileNameMatcher.IsMatch(original.BaseName, System.IO.Path.GetFileNameWithoutExtension(f)));
                if (normalized != null) return normalized;
            }
        }

        // 2) GUI matching over the whole folder (single input file, or a flat source folder).
        if (original.FromFolder && !IsFlat(allowed))
            return null;

        foreach (var ext in allowed)
        {
            var files = ListFolder(ext).Where(f => !Files.SamePath(f, original.FullPath)).ToList();
            string exact = files.FirstOrDefault(f => string.Equals(System.IO.Path.GetFileNameWithoutExtension(f), original.BaseName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            string normalized = files.FirstOrDefault(f => ImportFileNameMatcher.IsMatch(original.BaseName, System.IO.Path.GetFileNameWithoutExtension(f)));
            if (normalized != null) return normalized;
        }

        return null;
    }

    /// <summary>True when all candidate files of the source folder lie directly in it (e.g. a --flat export).</summary>
    private bool IsFlat(string[] allowed)
        => allowed.SelectMany(ListFolder).All(f => Files.SamePath(System.IO.Path.GetDirectoryName(f), Path));

    private List<string> ListFolder(string ext)
    {
        if (!_folderCache.TryGetValue(ext, out var list))
        {
            list = Directory.GetFiles(Path, "*" + ext, SearchOption.AllDirectories)
                .Where(f => string.Equals(System.IO.Path.GetExtension(f), ext, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _folderCache[ext] = list;
        }
        return list;
    }
}

/// <summary>Outcome of one import: rows changed, rows that got a value from the source, and the number of
/// non-empty source entries (used to detect a source that matches nothing).</summary>
public sealed record ImportResult(int Changed, int Matched, int Entries);

/// <summary>Applies translation/original sources to rows, mirroring the GUI import menu items.</summary>
public static class SourceImport
{
    private const string SpecialHeader = "[~NAMES-INCLUDED~]//Don't edit or remove this line.";

    /// <summary>Imports translations from <paramref name="sourceFile"/> into the session rows.
    /// <see cref="ImportResult.Changed"/> is the number the GUI shows as "Змінено N комірок".</summary>
    public static ImportResult ApplyTranslation(AssetSession session, string sourceFile, SourceSpec spec)
    {
        string ext = Path.GetExtension(sourceFile).ToLowerInvariant();
        string by = spec.By;

        switch (ext)
        {
            case ".csv":
                return ApplyCsv(session, CSVHelper.ImportFromCSV(sourceFile), by);
            case ".txt":
                return ApplyTxt(session, sourceFile, by, spec.TxtEscapes);
            case ".locres":
            case ".uasset":
            case ".umap":
            {
                if (by is not ("auto" or "id"))
                    throw new UsageException($"--by {by} не підтримується для {ext} (імпорт лише за ID).");
                var texts = LoadGameTexts(session, sourceFile);
                return ApplyFifo(session, texts.Where(x => x.Count >= 2 && x[0] != null).Select(x => (x[0], x[1])), toOriginal: false, sourceRowData: true);
            }
            default:
                throw new CliException($"Непідтримуваний формат джерела: {sourceFile}");
        }
    }

    /// <summary>
    /// CSV import. auto: by row order when the CSV lines up with the file row for row (same count and
    /// key == ID at every position; this also handles repeated IDs exactly), otherwise by ID.
    /// By ID uses a FIFO queue per ID (the n-th row with an ID gets the n-th CSV entry with that key), which
    /// equals the GUI CSVHelper.ImportByID for unique IDs and does not fail on repeated ones.
    /// </summary>
    private static ImportResult ApplyCsv(AssetSession session, List<CSVImportItem> csv, string by)
    {
        var rows = session.Rows;
        int entries = csv.Count(x => !string.IsNullOrWhiteSpace(x.Translation));
        string mode = by;
        if (mode == "auto")
        {
            bool aligned = csv.Count == rows.Count && Enumerable.Range(0, rows.Count).All(i => csv[i].Key == rows[i].ID);
            mode = aligned ? "order" : "id";
        }

        switch (mode)
        {
            case "id":
                return ApplyFifo(session, csv.Select(x => (x.Key, x.Translation)), toOriginal: false, sourceRowData: false,
                    convert: v => AssetHelper.ReplaceBreaklines(v));
            case "order":
            {
                if (by == "order" && csv.Count != rows.Count)
                    Log.Warn($"{session.File.Display}: у CSV {Common.RowsW(csv.Count)}, у файлі {Common.RowsW(rows.Count)} — імпорт за порядком може бути зсунутим.");
                int matched = Enumerable.Range(0, Math.Min(rows.Count, csv.Count)).Count(i => !string.IsNullOrWhiteSpace(csv[i].Translation));
                return new ImportResult(CSVHelper.ImportByRowOrder(rows, csv), matched, entries);
            }
            case "source":
            {
                var sources = csv.Where(x => !string.IsNullOrWhiteSpace(x.Translation)).Select(x => x.Source).ToHashSet(StringComparer.Ordinal);
                int matched = rows.Count(r => r.Text != null && sources.Contains(r.Text));
                return new ImportResult(CSVHelper.ImportByOriginalText(rows, csv), matched, entries);
            }
            case "id+source":
            {
                var keys = csv.Where(x => !string.IsNullOrWhiteSpace(x.Translation)).Select(x => (x.Key, x.Source)).ToHashSet();
                int matched = rows.Count(r => keys.Contains((r.ID, r.Text)));
                return new ImportResult(GuardDuplicates(() => CSVHelper.ImportByIDAndOriginalText(rows, csv)), matched, entries);
            }
            default:
                throw new UsageException($"--by {by} не підтримується для CSV.");
        }
    }

    /// <summary>
    /// TXT import (the formats written by unpack --format txt / txt-plain, i.e. TxtHelper.ExportWithIDFull /
    /// ExportSimple). Unlike TxtHelper.ImportBy*, the ID format is recognised by IDs that exist in the file
    /// (not by any '=' in the first lines), "ID=" means an empty text, and literal \n, \r, \t stay as they are
    /// unless <paramref name="escapes"/> (--txt-escapes) is set, so an unpack → pack round trip is lossless.
    /// auto: by ID when most lines start with a known "ID="; by line order when the file has exactly one line
    /// per row; otherwise the lines cannot be matched and the source is reported as unmatched / warned about.
    /// </summary>
    private static ImportResult ApplyTxt(AssetSession session, string path, string by, bool escapes)
    {
        var rows = session.Rows;
        var all = File.ReadAllLines(path, System.Text.Encoding.UTF8);
        int start = all.Length > 0 && all[0] == SpecialHeader ? 1 : 0;
        var lines = all.Skip(start).ToList();

        var rowIds = rows.Where(r => !string.IsNullOrEmpty(r.ID)).Select(r => r.ID).ToHashSet(StringComparer.Ordinal);
        var content = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#')).ToList();
        int idLike = content.Count(l => l.IndexOf('=') > 0);
        int idMatched = content.Count(l => { int i = l.IndexOf('='); return i > 0 && rowIds.Contains(l[..i]); });
        bool idFormat = idMatched > 0 && idMatched * 2 >= content.Count;

        string Convert(string v) => escapes ? NormalizeEscapes(v) : AssetHelper.ReplaceBreaklines(v);

        string mode = by;
        bool stripIds;
        if (mode == "auto")
        {
            if (idFormat) { mode = "id"; stripIds = true; }
            else if (lines.Count == rows.Count) { mode = "order"; stripIds = false; }
            else if (idLike > 0 && idLike * 2 >= content.Count)
                return new ImportResult(0, 0, idLike); // ID=text lines, but no ID belongs to this file
            else
            {
                Log.Warn($"{session.File.Display}: у TXT {Common.RowsW(lines.Count)}, у файлі {Common.RowsW(rows.Count)} — імпорт за порядком рядків може бути зсунутим. Вкажіть --by order, щоб прибрати попередження.");
                mode = "order"; stripIds = false;
            }
        }
        else
        {
            stripIds = idFormat || (content.Count > 0 && idLike == content.Count);
        }

        if (mode == "id")
        {
            if (idLike == 0)
                throw new CliException("Файл не містить ID. Використайте --by order (або --by auto).");
            var pairs = lines.Select(l => (Line: l, Sep: l.IndexOf('='))).Where(x => x.Sep > 0)
                .Select(x => (x.Line[..x.Sep], x.Line[(x.Sep + 1)..]));
            return ApplyFifo(session, pairs, toOriginal: false, sourceRowData: false, skipBlank: false, convert: Convert);
        }

        if (mode != "order")
            throw new UsageException($"--by {by} не підтримується для TXT (лише auto, id, order).");

        // Order: ID format -> the text after the first '=' of every "ID=" line; plain -> every line as is.
        var values = stripIds
            ? lines.Select(l => (Line: l, Sep: l.IndexOf('='))).Where(x => x.Sep > 0).Select(x => x.Line[(x.Sep + 1)..]).ToList()
            : lines;
        if (by == "order" && values.Count != rows.Count)
            Log.Warn($"{session.File.Display}: у TXT {Common.RowsW(values.Count)}, у файлі {Common.RowsW(rows.Count)} — імпорт за порядком може бути зсунутим.");

        int changed = 0, n = Math.Min(values.Count, rows.Count);
        for (int i = 0; i < n; i++)
            if (SetTranslation(rows[i], Convert(values[i]), sourceRowData: false))
                changed++;
        return new ImportResult(changed, n, values.Count);
    }

    /// <summary>TxtHelper.NormalizeText (GUI TXT import): literal escapes become line breaks/tabs, then tags.</summary>
    private static string NormalizeEscapes(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        text = text.Replace("\\r\\n", "\r\n").Replace("\\r", "\r").Replace("\\n", "\n").Replace("\\t", "\t");
        return AssetHelper.ReplaceBreaklines(text);
    }

    /// <summary>GUI "Імпортувати → LOCRES/UASSET → За ID у стовпчик Оригінал".</summary>
    public static ImportResult ApplyOriginal(AssetSession session, string sourceFile)
    {
        var texts = LoadGameTexts(session, sourceFile);
        return ApplyFifo(session, texts.Where(x => x.Count >= 2 && x[0] != null).Select(x => (x[0], x[1])), toOriginal: true, sourceRowData: false);
    }

    /// <summary>LocresHelper / UassetImportHelper source loading, with explicit CV2/engine options
    /// instead of SettingsManager/FileTabState. Like the GUI, no language filter is applied to the source.</summary>
    private static List<List<string>> LoadGameTexts(AssetSession session, string sourceFile)
    {
        string ext = Path.GetExtension(sourceFile).ToLowerInvariant();

        if (ext == ".locres")
        {
            if (!session.IsLocres)
                throw new CliException($"Імпорт з locres можливий лише у locres ({session.File.Display}).");
            var lf = AssetSession.OpenLocres(sourceFile, session.Options);
            return lf.IsGood ? lf.ExtractTexts() : new List<List<string>>();
        }

        if (!session.IsUasset)
            throw new CliException($"Імпорт з {ext} можливий лише у .uasset/.umap ({session.File.Display}).");

        UassetFile source = AssetSession.OpenUassetSource(sourceFile, session);
        if (!source.IsGood)
            throw new CliException("Файл прочитано з помилками: імпорт скасовано.");

        return source.ExtractTexts() ?? new List<List<string>>();
    }

    /// <summary>
    /// Match by ID like LocresHelper.ImportFromLocres / UassetImportHelper.ImportFromUasset / CSVHelper.ImportByID,
    /// but with a FIFO queue per ID instead of ToDictionary: the n-th row with an ID gets the n-th source entry
    /// with that ID, so repeated IDs (common in uasset) neither throw nor shift other rows. With unique IDs the
    /// result is identical to the GUI. Blank source values are skipped (GUI) unless <paramref name="skipBlank"/>
    /// is false (TXT "ID=" sets an empty text, like TxtHelper.ImportByID).
    /// </summary>
    private static ImportResult ApplyFifo(AssetSession session, IEnumerable<(string Id, string Value)> source,
        bool toOriginal, bool sourceRowData, bool skipBlank = true, Func<string, string> convert = null)
    {
        var queues = new Dictionary<string, Queue<string>>(StringComparer.Ordinal);
        int entries = 0;
        foreach (var (id, value) in source)
        {
            if (id == null) continue;
            if (!queues.TryGetValue(id, out var q)) queues[id] = q = new Queue<string>();
            q.Enqueue(value);
            if (!skipBlank || !string.IsNullOrWhiteSpace(value)) entries++;
        }

        int changed = 0, matched = 0;
        foreach (var item in session.Rows)
        {
            if (item.ID == null || !queues.TryGetValue(item.ID, out var q) || q.Count == 0) continue;
            string value = q.Dequeue();
            if (value == null || (skipBlank && string.IsNullOrWhiteSpace(value))) continue;
            if (convert != null) value = convert(value);
            matched++;

            if (!toOriginal)
            {
                if (SetTranslation(item, value, sourceRowData)) changed++;
            }
            else
            {
                if (item.Text == value) continue;
                item.Text = value;
                item.IsModified = item.Translation != item.Text;
                if (item.Translation != item.Text && item.Status == RowStatus.None)
                    item.Status = RowStatus.NeedsReview;
                changed++;
            }
        }

        var rowIds = session.Rows.Where(r => r.ID != null).Select(r => r.ID).ToHashSet(StringComparer.Ordinal);
        int extraKnown = queues.Where(kv => kv.Value.Count > 0 && rowIds.Contains(kv.Key))
            .Sum(kv => kv.Value.Count(v => !skipBlank || !string.IsNullOrWhiteSpace(v)));
        if (extraKnown > 0)
            Log.Warn($"{session.File.Display}: {Common.Entries(extraKnown)} джерела не використано — у джерелі більше рядків з тим самим ID, ніж у файлі.");

        return new ImportResult(changed, matched, entries);
    }

    /// <summary>Sets a translation the way the GUI import helpers do; true when the row changed.</summary>
    private static bool SetTranslation(DataGridItem item, string value, bool sourceRowData)
    {
        if (item.Translation == value) return false;
        item.Translation = value;
        item.IsModified = item.Translation != item.Text;
        // LocresHelper/UassetImportHelper also update the row data that is written back.
        if (sourceRowData && item.OriginalStringData != null && item.OriginalStringData.Count > 1)
            item.OriginalStringData[1] = value;
        return true;
    }

    private static int GuardDuplicates(Func<int> import)
    {
        try { return import(); }
        catch (ArgumentException ex) when (ex.Message.Contains("same key"))
        {
            throw new CliException("ID і оригінальний текст повторюються, імпорт за id+source неможливий. Використайте --by id або --by order.", ex);
        }
    }
}
