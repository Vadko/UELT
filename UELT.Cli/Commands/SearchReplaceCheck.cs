using System.Text;
using System.Text.RegularExpressions;
using UELT.Cli.Engine;
using UELT.Cli.Infra;
using UELT.Core;
using UELT.Hikaro;

namespace UELT.Cli.Commands;

/// <summary>search: GUI "Глобальний пошук" (GlobalSearchWindow) over files.</summary>
internal static class SearchCommand
{
    public static readonly Command Command = new("search", "пошук у файлах (Глобальний пошук)",
        Common.Concat(new[] { Common.Json, new Opt("--regex"), new Opt("--case"), new Opt("--whole-word"), new Opt("--exact"), new Opt("--in", true) },
            LoadPlan.SourceSpecs, AssetOptions.Specs), Run);

    private sealed class Matcher
    {
        public string Query;
        public bool Regex, WholeWord, Exact;
        public StringComparison Cmp;
        public Regex Rx;

        /// <summary>GlobalSearchWindow.MatchesQuery.</summary>
        public bool Matches(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (Regex) return Rx.IsMatch(text);
            if (Exact) return text.Equals(Query, Cmp);
            if (WholeWord) return ReplaceCommand.IsWholeWordMatch(text, Query, Cmp);
            return text.IndexOf(Query, Cmp) >= 0;
        }
    }

    private static int Run(ParsedArgs a)
    {
        a.RequirePositionals(2, -1, "запит і файл/теку");
        string query = a.Positionals[0];
        if (string.IsNullOrEmpty(query) || query == " ")
            throw new UsageException("Порожній запит.");
        if (a.Has("--whole-word") && a.Has("--exact"))
            throw new UsageException("--whole-word і --exact не можна поєднувати.");
        if (a.Has("--regex") && (a.Has("--whole-word") || a.Has("--exact")))
            throw new UsageException("--regex не поєднується з --whole-word/--exact.");

        var m = new Matcher
        {
            Query = query,
            Regex = a.Has("--regex"),
            WholeWord = a.Has("--whole-word"),
            Exact = a.Has("--exact"),
            Cmp = a.Has("--case") ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase,
        };
        if (m.Regex)
        {
            try { m.Rx = new Regex(query, a.Has("--case") ? RegexOptions.None : RegexOptions.IgnoreCase); }
            catch (ArgumentException ex) { throw new UsageException($"Невірний regex: {ex.Message}"); }
        }

        string scope = a.GetChoice("--in", "text", "text", "id", "source", "translation", "all");
        bool json = a.Has("--json");

        var options = AssetOptions.From(a);
        var plan = LoadPlan.From(a, options);
        var (files, batch) = Infra.Files.CollectInputs(a.Positionals.Skip(1).ToList());
        var runner = new BatchRunner(batch);
        var results = new List<object>();
        int found = 0;

        runner.Run(files, f =>
        {
            var s = plan.Load(f, batch, unmatchedIsError: false).Session;
            foreach (var row in s.Rows)
            {
                bool inId = (scope is "id" or "all") && m.Matches(row.ID ?? "");
                bool inOrig = (scope is "text" or "source" or "all") && m.Matches(row.Text ?? "");
                bool inTr = (scope is "text" or "translation" or "all") && m.Matches(row.Translation ?? "");
                if (!inId && !inOrig && !inTr) continue;

                // GlobalSearchWindow labels.
                string label = inOrig && inTr ? "В обох стовпцях" : inTr ? "У перекладі" : inOrig ? "В оригіналі" : "ID";
                string column = inOrig && inTr ? "both" : inTr ? "translation" : inOrig ? "original" : "id";
                string text = inTr ? row.Translation ?? "" : inOrig ? row.Text ?? "" : row.ID ?? "";
                found++;

                if (json)
                    results.Add(new { file = f.Display, row = row.Index + 1, id = row.ID, column, original = row.Text, translation = row.Translation });
                else
                    Log.Out($"{Common.Loc(f, row)}\t{label}\t{text}");
            }
        });

        if (json) Log.Json(results);
        Log.Info(found == 0 ? "Нічого не знайдено" : $"Знайдено: {found}");
        return runner.ExitCode();
    }
}

/// <summary>replace: GUI "Замінити" (ReplaceWindow + MainWindow.ReplaceInText) over translations.</summary>
internal static class ReplaceCommand
{
    public static readonly Command Command = new("replace", "знайти і замінити у перекладі",
        Common.Concat(new[] { Common.Output, Common.Overwrite, Common.InPlace, new Opt("--find", true), new Opt("--replace", true),
                new Opt("--whole-word"), new Opt("--case"), new Opt("--exact"), new Opt("--dry-run") },
            LoadPlan.SourceSpecs, AssetOptions.Specs), Run);

    private static int Run(ParsedArgs a)
    {
        a.RequirePositionals(1, 1, "файл або теку");
        string find = a.Get("--find");
        string replace = a.Get("--replace");
        if (string.IsNullOrWhiteSpace(find)) throw new UsageException("Вкажіть --find <текст> (не порожній).");
        if (replace == null) throw new UsageException("Вкажіть --replace <текст> (можна порожній: --replace \"\").");
        bool wholeWord = a.Has("--whole-word"), exact = a.Has("--exact"), caseSensitive = a.Has("--case");
        if (wholeWord && exact) throw new UsageException("--whole-word і --exact не можна поєднувати.");
        bool dryRun = a.Has("--dry-run");
        bool overwrite = a.Has("--overwrite");

        var options = AssetOptions.From(a);
        var plan = LoadPlan.From(a, options);
        var (files, batch) = Infra.Files.CollectInputs(a.Positionals, outputOption: dryRun ? null : a.Get("--output"));
        var namer = dryRun ? null : new GameOutputNamer(a.Get("--output"), a.Has("--in-place"), batch);
        var runner = new BatchRunner(batch);
        int total = 0, filesAffected = 0;

        runner.Run(files, f =>
        {
            string outPath = null;
            if (!dryRun)
            {
                outPath = namer.Resolve(f);
                namer.CheckWritable(f, outPath, overwrite, options.Status);
            }

            var loaded = plan.Load(f, batch);
            var s = loaded.Session;
            int count = 0;

            // MainWindow.ApplyReplaceToRows (no grid filter in the CLI: all rows).
            foreach (var item in s.Rows)
            {
                if (string.IsNullOrEmpty(item.Translation)) continue;
                string orig = item.Translation;
                string next = ReplaceInText(orig, find, replace, wholeWord, caseSensitive, exact);
                if (string.Equals(orig, next, StringComparison.Ordinal)) continue;
                item.Translation = next;
                count++;
                if (dryRun) Log.Out($"{Common.Loc(f, item)}\t{orig}\t{next}");
            }

            total += count;
            if (count > 0) filesAffected++;

            if (dryRun)
            {
                Log.Info($"{f.Display}: замінено б у {PluralizationHelper.GetCellsWordReplace(count)}");
                return;
            }

            if (count == 0 && loaded.TranslationChanged == 0)
            {
                Log.Info($"{f.Display}: нічого не знайдено — файл не записано.");
                return;
            }

            s.Save(outPath);
            Log.Out($"OK {f.Display} -> {outPath} (замінено у {PluralizationHelper.GetCellsWordReplace(count)})");
        });

        if (total == 0) Log.Info("Нічого не знайдено");
        else Log.Info($"{(dryRun ? "Було б замінено" : "Замінено")} у {PluralizationHelper.GetCellsWordReplace(total)} {Common.InFiles(filesAffected)}");
        return runner.ExitCode();
    }

    /// <summary>Copy of MainWindow.ReplaceInText (private in the GUI window class).</summary>
    public static string ReplaceInText(string text, string findText, string replaceText, bool wholeWord, bool caseSensitive, bool exactMatch)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(findText))
            return text;

        StringComparison cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (exactMatch)
            return string.Equals(text, findText, cmp) ? replaceText : text;

        var sb = new StringBuilder(text.Length);
        int index = 0;
        while (true)
        {
            int found = text.IndexOf(findText, index, cmp);
            if (found < 0)
            {
                sb.Append(text, index, text.Length - index);
                break;
            }

            if (!wholeWord)
            {
                sb.Append(text, index, found - index);
                sb.Append(replaceText);
                index = found + findText.Length;
                continue;
            }

            bool wordStart = found == 0 || !char.IsLetterOrDigit(text[found - 1]);
            bool wordEnd = found + findText.Length >= text.Length || !char.IsLetterOrDigit(text[found + findText.Length]);

            if (wordStart && wordEnd)
            {
                sb.Append(text, index, found - index);
                sb.Append(replaceText);
                index = found + findText.Length;
            }
            else
            {
                sb.Append(text, index, found - index + 1);
                index = found + 1;
            }
        }
        return sb.ToString();
    }

    /// <summary>Copy of GlobalSearchWindow.IsWholeWordMatch.</summary>
    public static bool IsWholeWordMatch(string text, string searchText, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int index = 0;
        while ((index = text.IndexOf(searchText, index, comparison)) != -1)
        {
            bool isWordStart = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            bool isWordEnd = index + searchText.Length >= text.Length || !char.IsLetterOrDigit(text[index + searchText.Length]);
            if (isWordStart && isWordEnd) return true;
            index++;
        }
        return false;
    }
}

/// <summary>check: CommonErrorChecker (GUI "Типові помилки" filter/panel) and glossary consistency.</summary>
internal static class CheckCommand
{
    public static readonly Opt GlossaryOpt = new("--glossary", true);

    public static readonly Command Command = new("check", "перевірка типових помилок перекладу",
        Common.Concat(new[] { Common.Json, Common.Verbose, GlossaryOpt, new Opt("--glossary-only") },
            LoadPlan.SourceSpecs, AssetOptions.Specs), Run);

    private static int Run(ParsedArgs a)
    {
        a.RequirePositionals(1, -1, "файл або теку");
        string glossary = a.Get("--glossary");
        bool glossaryOnly = a.Has("--glossary-only");
        if (glossaryOnly && glossary == null)
            throw new UsageException("--glossary-only потребує --glossary <файл>.");
        if (glossary != null)
            GlossaryCommand.LoadGlossary(glossary, mustExist: true);
        return RunCheck(a, commonErrors: !glossaryOnly, glossaryCheck: glossary != null);
    }

    public static int RunCheck(ParsedArgs a, bool commonErrors, bool glossaryCheck)
    {
        bool json = a.Has("--json"), verbose = a.Has("--verbose");
        var options = AssetOptions.From(a);
        var plan = LoadPlan.From(a, options);
        var (files, batch) = Infra.Files.CollectInputs(a.Positionals);
        var runner = new BatchRunner(batch);
        var results = new List<object>();
        int issueRows = 0, issueCount = 0;

        runner.Run(files, f =>
        {
            var s = plan.Load(f, batch, unmatchedIsError: false).Session;
            foreach (var row in s.Rows)
            {
                var errors = new List<string>();
                if (commonErrors)
                    errors.AddRange(CommonErrorChecker.GetCommonErrors(row.Text, row.Translation));
                if (glossaryCheck)
                    errors.AddRange(GlossaryCommand.GlossaryIssues(row));
                if (errors.Count == 0) continue;

                issueRows++;
                issueCount += errors.Count;
                if (json)
                {
                    results.Add(new { file = f.Display, row = row.Index + 1, id = row.ID, original = row.Text, translation = row.Translation, errors });
                    continue;
                }

                Log.Out($"{Common.Loc(f, row)}\t{string.Join(". ", errors)}");
                if (verbose)
                {
                    Log.Out($"    оригінал: {row.Text}");
                    Log.Out($"    переклад: {row.Translation}");
                }
            }
        });

        if (json) Log.Json(results);
        Log.Info(issueRows == 0 ? "Проблем не знайдено." : $"Знайдено проблем: {issueCount} {PluralizationHelper.GetRowsWordLocative(issueRows)}.");

        int code = runner.ExitCode();
        return code != ExitCodes.Ok ? code : issueRows > 0 ? ExitCodes.Partial : ExitCodes.Ok;
    }
}
