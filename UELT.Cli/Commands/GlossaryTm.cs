using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using UELT.Cli.Engine;
using UELT.Cli.Infra;
using UELT.Hikaro;

namespace UELT.Cli.Commands;

/// <summary>glossary: the GUI glossary (GlossaryManager, Glossary.json next to the program).</summary>
internal static class GlossaryCommand
{
    public static readonly Command Command = new("glossary", "глосарій (list/add/remove/export/check)",
        Common.Concat(new[] { CheckCommand.GlossaryOpt, Common.Json, Common.Overwrite, Common.Verbose },
            LoadPlan.SourceSpecs, AssetOptions.Specs), Run);

    /// <summary>Points GlossaryManager at <paramref name="path"/> (or the GUI default) and loads it.</summary>
    public static void LoadGlossary(string path, bool mustExist)
    {
        if (path != null)
            GlossaryManager.GlossaryPathOverride = Path.GetFullPath(path);
        string effective = GlossaryManager.GlossaryPathOverride ?? Path.Combine(AppContext.BaseDirectory, "Glossary.json");
        if (mustExist && !File.Exists(effective))
            throw new CliException($"Файл глосарію не знайдено: {effective}");

        GlossaryManager.Instance.Load();
        if (GlossaryManager.ErrorCount > 0)
            throw new CliException("Не вдалося завантажити глосарій.");
    }

    public static string EffectivePath
        => GlossaryManager.GlossaryPathOverride ?? Path.Combine(AppContext.BaseDirectory, "Glossary.json");

    /// <summary>
    /// Glossary consistency for a translated row: every glossary term found in the original
    /// (GlossaryEntry.GetMatchRegex, the same matching the GUI uses for highlights/suggestions)
    /// should have one of its translations in the translation (case-insensitive substring).
    /// </summary>
    public static IEnumerable<string> GlossaryIssues(DataGridItem row)
    {
        if (string.IsNullOrWhiteSpace(row.Translation) || row.Translation == row.Text)
            yield break;

        foreach (var group in GlossaryManager.Instance.FindMatches(row.Text).GroupBy(m => m.Entry))
        {
            var entry = group.Key;
            if (entry.Translations.Count == 0) continue;
            if (entry.Translations.Any(t => !string.IsNullOrWhiteSpace(t) && row.Translation.Contains(t, StringComparison.OrdinalIgnoreCase)))
                continue;
            yield return $"Глосарій: «{group.First().MatchedText}» → «{entry.TranslationsDisplay}»";
        }
    }

    private static int Run(ParsedArgs a)
    {
        a.RequirePositionals(1, -1, "підкоманду (list, add, remove, export, check)");
        string sub = a.Positionals[0].ToLowerInvariant();
        var args = a.Positionals.Skip(1).ToList();
        string path = a.Get("--glossary");

        switch (sub)
        {
            case "list":
            {
                if (args.Count > 0) throw new UsageException("glossary list не приймає аргументів.");
                LoadGlossary(path, mustExist: false);
                var entries = GlossaryManager.Instance.Entries;
                if (a.Has("--json"))
                    Log.Json(entries.Select(e => new { original = e.Original, translations = e.Translations.ToList() }));
                else
                    foreach (var e in entries)
                        Log.Out($"{e.Original}\t{e.TranslationsDisplay}");
                Log.Info($"{EffectivePath}: {Common.Entries(entries.Count)}.");
                return ExitCodes.Ok;
            }
            case "add":
            {
                if (args.Count != 2) throw new UsageException("Використання: glossary add <оригінал> <переклад>");
                if (string.IsNullOrWhiteSpace(args[0])) throw new UsageException("Порожній оригінал.");
                if (string.IsNullOrEmpty(args[1])) throw new UsageException("Відсутній переклад.");
                LoadGlossary(path, mustExist: false);
                bool existed = GlossaryManager.Instance.AddOrUpdate(args[0].Trim(), args[1]);
                if (GlossaryManager.ErrorCount > 0) return ExitCodes.Error;
                Log.Out(existed ? $"Оновлено: {args[0].Trim()}" : $"Додано: {args[0].Trim()}");
                return ExitCodes.Ok;
            }
            case "remove":
            {
                if (args.Count is < 1 or > 2) throw new UsageException("Використання: glossary remove <оригінал> [<переклад>]");
                LoadGlossary(path, mustExist: true);
                var entry = GlossaryManager.Instance.Entries.FirstOrDefault(e => string.Equals(e.Original, args[0], StringComparison.OrdinalIgnoreCase))
                            ?? throw new CliException($"Запис «{args[0]}» не знайдено.");
                if (args.Count == 1)
                    GlossaryManager.Instance.RemoveEntry(entry);
                else
                {
                    if (!entry.Translations.Contains(args[1]))
                        throw new CliException($"У записі «{entry.Original}» немає перекладу «{args[1]}».");
                    GlossaryManager.Instance.RemoveTranslation(entry, args[1]);
                }
                if (GlossaryManager.ErrorCount > 0) return ExitCodes.Error;
                Log.Out($"Видалено: {args[0]}{(args.Count == 2 ? " → " + args[1] : "")}");
                return ExitCodes.Ok;
            }
            case "export":
            {
                if (args.Count != 1) throw new UsageException("Використання: glossary export <файл.json|.csv|.txt>");
                LoadGlossary(path, mustExist: true);
                string outPath = args[0];
                Infra.Files.EnsureWritable(outPath, a.Has("--overwrite"));
                Infra.Files.EnsureParentDir(outPath);
                Export(outPath, GlossaryManager.Instance.Entries.ToList());
                Log.Out($"OK {EffectivePath} -> {outPath} ({Common.Entries(GlossaryManager.Instance.Entries.Count)})");
                return ExitCodes.Ok;
            }
            case "check":
            {
                if (args.Count < 1) throw new UsageException("Використання: glossary check <файл|тека>...");
                LoadGlossary(path, mustExist: true);
                a.Positionals.RemoveAt(0);
                return CheckCommand.RunCheck(a, commonErrors: false, glossaryCheck: true);
            }
            default:
                throw new UsageException($"Невідома підкоманда glossary: {sub}");
        }
    }

    private static void Export(string path, List<GlossaryEntry> entries)
    {
        var utf8 = new UTF8Encoding(false);
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".json":
                // Same shape and options as GlossaryManager.Save, so the file can be used as a GUI Glossary.json.
                File.WriteAllText(path, JsonSerializer.Serialize(
                    entries.Select(e => new GlossaryDto { Original = e.Original, Translations = e.Translations.ToList() }).ToList(),
                    new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), utf8);
                break;
            case ".csv":
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture) { Encoding = utf8, ShouldQuote = _ => true };
                using var writer = new StreamWriter(path, false, utf8);
                using var csv = new CsvWriter(writer, config);
                csv.WriteField("original"); csv.WriteField("translation"); csv.NextRecord();
                foreach (var e in entries)
                    foreach (var t in e.Translations)
                    {
                        csv.WriteField(e.Original); csv.WriteField(t); csv.NextRecord();
                    }
                break;
            }
            case ".txt":
                File.WriteAllLines(path, entries.SelectMany(e => e.Translations.Select(t => $"{e.Original}={t}")), utf8);
                break;
            default:
                throw new UsageException("Формат експорту глосарію: .json, .csv або .txt");
        }
    }

    private sealed class GlossaryDto
    {
        public string Original { get; set; } = "";
        public List<string> Translations { get; set; } = new();
    }
}

/// <summary>tm: translation memories in the GUI format (TranslationMemories/*.tm.json + _active.json).</summary>
internal static class TmCommand
{
    public static readonly Command Command = new("tm", "пам’ять перекладів (list/create/delete/activate/deactivate/target/record/apply)",
        Common.Concat(new[]
            {
                new Opt("--tm-dir", true), new Opt("--memory", true, true), new Opt("--max-variants", true),
                new Opt("--export", true), Common.Output, Common.Overwrite, Common.InPlace, Common.Flat, Common.Json
            },
            LoadPlan.SourceSpecs, AssetOptions.Specs), Run);

    private static string Folder => TranslationMemoryManagerFolder();

    private static string TranslationMemoryManagerFolder()
        => TranslationMemoryManager.MemoriesFolderOverride ?? Path.Combine(AppContext.BaseDirectory, "TranslationMemories");

    private static void LoadAll(ParsedArgs a)
    {
        string dir = a.Get("--tm-dir");
        if (dir != null) TranslationMemoryManager.MemoriesFolderOverride = Path.GetFullPath(dir);
        // LoadAll creates the folder when missing (GUI behaviour); avoid that for read-only use.
        if (Directory.Exists(Folder))
            TranslationMemoryManager.Instance.LoadAll();

        // Like the GUI (a message box, then it keeps working), an unreadable memory does not block the other
        // subcommands; it only cannot be recorded into or applied from (see RequireReadable).
        Broken.Clear();
        if (TranslationMemory.ErrorCount > 0)
            foreach (var m in TranslationMemoryManager.Instance.Memories)
                if (File.Exists(m.FilePath) && CountEntries(m) < 0)
                    Broken.Add(m);
        TranslationMemory.ErrorCount = 0;
    }

    /// <summary>Memories whose file exists but could not be read.</summary>
    private static readonly HashSet<TranslationMemory> Broken = new();

    private static void RequireReadable(IEnumerable<TranslationMemory> memories, string why)
    {
        var broken = memories.Where(Broken.Contains).Select(m => $"«{m.Name}»").ToList();
        if (broken.Count > 0)
            throw new CliException($"Пам’ять {string.Join(", ", broken)} пошкоджена (файл не вдалося прочитати) — {why}. Виправте або видаліть файл ({Folder}).");
    }

    private static TranslationMemory Find(string name)
        => TranslationMemoryManager.Instance.Memories.FirstOrDefault(m => string.Equals(m.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
           ?? throw new CliException($"Пам’ять «{name}» не знайдено у {Folder}.");

    private static void CheckSaved()
    {
        if (TranslationMemory.ErrorCount > 0 || TranslationMemoryManager.ErrorCount > 0)
            throw new CliException("Не вдалося зберегти пам’ять перекладів.");
    }

    private static int CountEntries(TranslationMemory tm)
    {
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(tm.FilePath));
            return dict?.Count ?? 0;
        }
        catch { return -1; }
    }

    private static int Run(ParsedArgs a)
    {
        a.RequirePositionals(1, -1, "підкоманду (list, create, delete, activate, deactivate, target, record, apply)");
        string sub = a.Positionals[0].ToLowerInvariant();
        var args = a.Positionals.Skip(1).ToList();
        LoadAll(a);
        var mgr = TranslationMemoryManager.Instance;

        switch (sub)
        {
            case "list":
            {
                var list = mgr.Memories.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
                if (a.Has("--json"))
                    Log.Json(list.Select(m => new { name = m.Name, file = m.FilePath, active = m.IsActive, writeTarget = m.IsWriteTarget, broken = Broken.Contains(m), entries = CountEntries(m) }));
                else
                    foreach (var m in list)
                        Log.Out($"{m.Name}\t{(m.IsActive ? "активна" : "неактивна")}{(m.IsWriteTarget ? ", ціль запису" : "")}\t{(Broken.Contains(m) ? "пошкоджена" : Common.Entries(Math.Max(0, CountEntries(m))))}\t{m.FilePath}");
                Log.Info($"{Folder}: {Common.Memories(list.Count)}.");
                return ExitCodes.Ok;
            }
            case "create":
            {
                if (args.Count != 1) throw new UsageException("Використання: tm create <назва>");
                string name = args[0].Trim();
                // TranslationMemoryNameDialog checks.
                if (!TranslationMemoryManager.IsValidMemoryName(name, out string error))
                    throw new CliException(error.Replace("\n", " "));
                if (mgr.MemoryNameExists(name))
                    throw new CliException($"Пам’ять з назвою «{name}» вже існує.");
                var tm = mgr.CreateNew(name);
                CheckSaved();
                Log.Out($"Створено: {tm.Name} ({tm.FilePath}){(tm.IsWriteTarget ? ", ціль запису" : "")}");
                return ExitCodes.Ok;
            }
            case "delete":
            {
                if (args.Count != 1) throw new UsageException("Використання: tm delete <назва>");
                var tm = Find(args[0]);
                mgr.DeleteMemory(tm);
                CheckSaved();
                Log.Out($"Видалено: {tm.Name}");
                return ExitCodes.Ok;
            }
            case "activate":
            case "deactivate":
            {
                if (args.Count != 1) throw new UsageException($"Використання: tm {sub} <назва>");
                var tm = Find(args[0]);
                tm.IsActive = sub == "activate";
                mgr.SaveState();
                CheckSaved();
                Log.Out($"{tm.Name}: {(tm.IsActive ? "активна" : "неактивна")}");
                return ExitCodes.Ok;
            }
            case "target":
            {
                if (args.Count != 1) throw new UsageException("Використання: tm target <назва>");
                var tm = Find(args[0]);
                mgr.SetWriteTarget(tm);
                CheckSaved();
                Log.Out($"{tm.Name}: ціль запису{(tm.IsActive ? "" : " (увага: пам’ять неактивна, GUI не записуватиме в неї)")}");
                return ExitCodes.Ok;
            }
            case "record":
                return Record(a, args);
            case "apply":
                return Apply(a, args);
            default:
                throw new UsageException($"Невідома підкоманда tm: {sub}");
        }
    }

    /// <summary>SettingsWindow "Записати всі переклади" / SaveFileCore recording: modified, meaningful rows.</summary>
    private static int Record(ParsedArgs a, List<string> inputs)
    {
        if (inputs.Count < 1) throw new UsageException("Використання: tm record <файл|тека>... [-t <джерело>] [--original-from <файл|тека>]");
        var mgr = TranslationMemoryManager.Instance;

        TranslationMemory target;
        var names = a.GetAll("--memory");
        if (names.Count > 1) throw new UsageException("tm record: лише одна --memory.");
        if (names.Count == 1) target = Find(names[0]);
        else target = mgr.Memories.FirstOrDefault(m => m.IsActive && m.IsWriteTarget)
                      ?? throw new CliException("Немає активної пам’яті для запису. Оберіть ціль: tm target <назва> або --memory <назва>.");
        RequireReadable(new[] { target }, "запис у неї знищив би наявні переклади");

        SettingsManager.Load();
        int maxVariants = a.GetInt("--max-variants", 1, 10) ?? SettingsManager.MaxTranslationMemoryVariants;

        var options = AssetOptions.From(a);
        var plan = LoadPlan.From(a, options);
        var (files, batch) = Infra.Files.CollectInputs(inputs);
        var runner = new BatchRunner(batch);
        int recorded = 0;

        runner.Run(files, f =>
        {
            var s = plan.Load(f, batch).Session;
            int n = 0;
            foreach (var row in s.Rows)
            {
                if (!row.IsModified) continue;
                if (row.Translation == row.Text) continue;
                if (string.IsNullOrWhiteSpace(row.Text)) continue;
                if (!TranslationMemoryManager.IsMeaningfulTranslation(row.Translation)) continue;
                target.AddOrUpdate(row.Text, row.Translation, maxVariants);
                n++;
            }
            recorded += n;
            Log.Info($"{f.Display}: {n} {PluralizationHelper.GetTranslateWord(n)}");
        });

        target.Save();
        CheckSaved();
        Log.Out(recorded > 0
            ? $"Записано {recorded} {PluralizationHelper.GetTranslateWord(recorded)} у пам’ять «{target.Name}»."
            : "Немає змінених рядків для запису.");
        return runner.ExitCode();
    }

    /// <summary>SettingsWindow "Застосувати переклади з пам’яті": only untranslated rows, first (newest) variant.</summary>
    private static int Apply(ParsedArgs a, List<string> inputs)
    {
        if (inputs.Count < 1) throw new UsageException("Використання: tm apply <файл|тека>... [--memory <назва>]... [-o|--in-place|--export <формат>]");
        var mgr = TranslationMemoryManager.Instance;

        var names = a.GetAll("--memory");
        var memories = names.Count > 0
            ? names.Select(Find).ToList()
            : mgr.Memories.Where(m => m.IsActive).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (memories.Count == 0)
            throw new CliException("Активуйте хоча б одну пам’ять (tm activate <назва>) або вкажіть --memory <назва>.");
        RequireReadable(memories, "з неї неможливо застосувати переклади");

        string export = a.Get("--export");
        if (export != null) export = a.GetChoice("--export", "csv", UnpackCommand.Formats);
        bool overwrite = a.Has("--overwrite");

        var options = AssetOptions.From(a);
        var plan = LoadPlan.From(a, options);
        var (files, batch) = Infra.Files.CollectInputs(inputs, outputOption: export == null ? a.Get("--output") : null);
        var gameNamer = export == null ? new GameOutputNamer(a.Get("--output"), a.Has("--in-place"), batch) : null;
        if (export != null && a.Has("--in-place")) throw new UsageException("--in-place не поєднується з --export.");
        var exportNamer = export != null ? new ExportNamer(a.Get("--output"), batch, a.Has("--flat"), overwrite) : null;
        var runner = new BatchRunner(batch);
        int applied = 0, filesAffected = 0;

        runner.Run(files, f =>
        {
            string outPath = null;
            if (gameNamer != null)
            {
                outPath = gameNamer.Resolve(f);
                gameNamer.CheckWritable(f, outPath, overwrite, options.Status);
            }

            var loaded = plan.Load(f, batch);
            var s = loaded.Session;
            int n = 0;
            foreach (var row in s.Rows)
            {
                if (!string.IsNullOrEmpty(row.Translation) && row.Translation != row.Text)
                    continue;

                string match = null;
                foreach (var m in memories)
                {
                    if (m.TryGetFirstTranslation(row.Text, out var t) && !string.IsNullOrEmpty(t))
                    {
                        match = t;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(match) || match == row.Text)
                    continue;

                row.Translation = match;
                n++;
            }

            applied += n;
            if (n > 0) filesAffected++;

            if (export != null)
            {
                if (s.Rows.Count == 0) throw new SkipException(Common.NoTexts(s));
                outPath = exportNamer.Resolve(f, UnpackCommand.ExtensionFor(export));
                Infra.Files.EnsureWritable(outPath, overwrite);
                UnpackCommand.Export(export, outPath, s.Rows);
            }
            else
            {
                if (n == 0 && loaded.TranslationChanged == 0)
                {
                    Log.Info($"{f.Display}: не знайдено рядків для застосування — файл не записано.");
                    return;
                }
                s.Save(outPath);
            }
            Log.Out($"OK {f.Display} -> {outPath} (застосовано {n} {PluralizationHelper.GetTranslateWord(n)})");
        });

        Log.Info(applied > 0
            ? $"Застосовано {applied} {PluralizationHelper.GetTranslateWord(applied)} {Common.InFiles(filesAffected)}"
            : "Не знайдено рядків для застосування перекладу.");
        return runner.ExitCode();
    }
}
