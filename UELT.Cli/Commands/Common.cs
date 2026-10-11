using System.Globalization;
using UELT.Cli.Engine;
using UELT.Cli.Infra;
using UELT.Hikaro;

namespace UELT.Cli.Commands;

internal static class Common
{
    public static readonly Opt Output = new("--output", true, false, "-o");
    public static readonly Opt Overwrite = new("--overwrite");
    public static readonly Opt InPlace = new("--in-place");
    public static readonly Opt Flat = new("--flat");
    public static readonly Opt Json = new("--json");
    public static readonly Opt Verbose = new("--verbose");

    public static Opt[] Concat(params IEnumerable<Opt>[] groups) => groups.SelectMany(g => g).ToArray();

    public static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

    public static string Pct(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

    public static string Files(int n) => $"{n} {PluralizationHelper.GetFilesWord(n)}";

    public static string RowsW(int n) => $"{n} {PluralizationHelper.GetRowsWord(n)}";

    public static string Plural(int count, string one, string few, string many)
    {
        int abs = Math.Abs(count), mod10 = abs % 10, mod100 = abs % 100;
        if (mod100 >= 11 && mod100 <= 19) return $"{count} {many}";
        return $"{count} " + (mod10 switch { 1 => one, 2 or 3 or 4 => few, _ => many });
    }

    public static string Entries(int n) => Plural(n, "запис", "записи", "записів");

    public static string Memories(int n) => Plural(n, "пам’ять", "пам’яті", "пам’ятей");

    /// <summary>"у 1 файлі" / "у 3 файлах".</summary>
    public static string InFiles(int n) => $"у {n} {(n % 10 == 1 && n % 100 != 11 ? "файлі" : "файлах")}";

    /// <summary>The GUI message for an asset without texts (FinishLoadingUasset), condensed to one line.</summary>
    public static string NoTexts(AssetSession s) => s.IsUasset
        ? "файл відкрито, але не знайдено жодного тексту (можливі причини: файл не містить текстів; потрібен usmap/jmap/jmap.gz (--engine/--mappings); файл пошкоджено; для цього ассета ще не написано парсингу)."
        : "файл не містить рядків.";

    /// <summary>"OK in -> out (N rows, M changed)" result line.</summary>
    public static void OkLine(InputFile f, string outPath, int rows, int? changed = null)
        => Log.Out(changed.HasValue
            ? $"OK {f.Display} -> {outPath} ({RowsW(rows)}, змінено {changed.Value})"
            : $"OK {f.Display} -> {outPath} ({RowsW(rows)})");

    /// <summary>Batch summary in the style of MainWindow.BatchImportFromFolder.</summary>
    public static void BatchSummary(BatchRunner r, int? totalChanged = null, string verb = "Оброблено")
    {
        if (!r.IsBatch) return;
        Log.Out($"{verb} {Files(r.Ok)} з {r.Processed}");
        if (totalChanged.HasValue)
            Log.Out($"Загалом змінено {RowsW(totalChanged.Value)}");
        if (r.Unmatched > 0)
            Log.Out($"Не знайдено відповідний файл для {Plural(r.Unmatched, "файлу", "файлів", "файлів")}");
        if (r.Skipped > 0)
            Log.Out($"Пропущено {Files(r.Skipped)}");
        if (r.Failed + r.NeedsEngine > 0)
            Log.Out($"Помилки {InFiles(r.Failed + r.NeedsEngine)}");
    }

    /// <summary>Translation of the row text column for display: tags stay as in the GUI (&lt;lf&gt;, &lt;cf&gt;...).</summary>
    public static string Loc(InputFile f, DataGridItem row) => $"{f.Display}:{row.Index + 1}:{row.ID}";
}
