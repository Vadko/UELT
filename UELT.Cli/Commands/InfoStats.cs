using UELT.Cli.Engine;
using UELT.Cli.Infra;
using UELT.Core.locres;
using UELT.Hikaro;

namespace UELT.Cli.Commands;

/// <summary>info: GUI "Деталі про locres" / "Деталі про uasset/umap", extended.</summary>
internal static class InfoCommand
{
    public static readonly Command Command = new("info", "відомості про формат файлу",
        Common.Concat(new[] { Common.Json }, AssetOptions.OpenSpecs), Run);

    /// <summary>Labels from MainWindow.LocresFileInfo_Click.</summary>
    public static string LocresVersionName(LocresVersion v) => v switch
    {
        LocresVersion.Legacy => "Legacy (v0)",
        LocresVersion.Compact => "Compact (v1)",
        LocresVersion.Optimized => "Optimized CRC32 (v2)",
        LocresVersion.Optimized_CityHash64_UTF16 => "Optimized CityHash64 UTF-16 (v3)",
        LocresVersion.Optimized_CityHash64_ExternID_UTF16 => "Optimized CityHash64 + ExternID UTF-16 (v4 Stellar Blade)",
        _ => $"Невідомий ({(byte)v})"
    };

    private static int Run(ParsedArgs a)
    {
        a.RequirePositionals(1, -1, "файл або теку");
        var options = AssetOptions.From(a);
        bool json = a.Has("--json");
        var (files, batch) = Infra.Files.CollectInputs(a.Positionals);
        var runner = new BatchRunner(batch);
        var all = new List<Dictionary<string, object>>();

        runner.Run(files, f =>
        {
            var s = AssetSession.Open(f, options, batch);
            var d = new Dictionary<string, object> { ["file"] = f.Display, ["type"] = f.Ext.TrimStart('.') };

            if (s.Locres is { } lf)
            {
                d["version"] = (int)lf.Version;
                d["versionName"] = LocresVersionName(lf.Version);
                d["namespaces"] = lf.Count;
                d["entries"] = lf.Sum(ns => ns.Count);
                d["rows"] = s.Rows.Count;
                d["cv2Encrypted"] = lf.IsCV2Encrypted;
                if (lf.IsCV2Encrypted) d["cv2KeySet"] = lf.CV2KeySet.ToString();
            }
            else if (s.Uasset is { } uf)
            {
                // Versioned assets are always auto-detected (--engine applies only to unversioned ones, as in the GUI).
                d["engineVersion"] = s.UsedEngine?.ToString();
                d["engineVersionLabel"] = s.UsedEngine.HasValue ? EngineVersionParser.Format(s.UsedEngine.Value.ToString()) : "автовизначена";
                d["versioned"] = !s.UsedEngine.HasValue;
                if (!s.UsedEngine.HasValue)
                    try { d["detectedEngineVersion"] = uf.Asset.GetEngineVersion().ToString(); } catch { }
                d["mappingsUsed"] = s.UsedMappings != null;
                if (s.UsedMappings != null) d["mappings"] = s.UsedMappings;
                d["unversionedProperties"] = uf.Asset.HasUnversionedProperties;
                d["assetType"] = uf.AssetType;
                d["languages"] = s.Languages;
                d["selectedLanguage"] = s.SelectedLanguage;
                d["rows"] = s.Rows.Count;
                d["isGood"] = uf.IsGood;
                d["warnings"] = s.Warnings;
            }

            all.Add(d);
            if (!json) Print(d);
        });

        if (json && all.Count > 0)
            Log.Json(batch ? all : all[0]);

        return runner.ExitCode();
    }

    private static void Print(Dictionary<string, object> d)
    {
        Log.Out(d["file"].ToString());
        if ((string)d["type"] == "locres")
        {
            Log.Out($"  Формат: {d["versionName"]}");
            Log.Out($"  Областей імен (Namespace): {d["namespaces"]}");
            Log.Out($"  Записів: {d["entries"]}");
            Log.Out($"  CV2: {(((bool)d["cv2Encrypted"]) ? "так (" + d["cv2KeySet"] + ")" : "ні")}");
            return;
        }
        else
        {
            string detected = d.TryGetValue("detectedEngineVersion", out var dv) && dv != null ? $" (у файлі: {dv})" : "";
            Log.Out($"  Версія рушія: {d["engineVersionLabel"]}{detected}");
            Log.Out($"  Використано usmap/jmap: {(((bool)d["mappingsUsed"]) ? "Так" : "Ні")}");
            Log.Out($"  Кейс: {d["assetType"]}");
            var langs = (List<string>)d["languages"];
            if (langs.Count > 0)
                Log.Out($"  Мови: {string.Join(", ", langs)}{(d["selectedLanguage"] is string sel ? $" (обрано: {sel})" : "")}");
            Log.Out($"  Рядків: {d["rows"]}");
            Log.Out($"  Прочитано повністю: {(((bool)d["isGood"]) ? "так" : "ні")}");
            foreach (var w in (List<string>)d["warnings"])
                Log.Out($"  Попередження: {w.Replace("\n", " ")}");
        }
    }
}

/// <summary>stats: GUI "Загальна статистика" (GlobalStatsWindow + StatisticsManager).</summary>
internal static class StatsCommand
{
    public static readonly Command Command = new("stats", "статистика перекладу",
        Common.Concat(new[] { Common.Json }, LoadPlan.SourceSpecs, AssetOptions.Specs), Run);

    private sealed record Row(string File, int Rows, StatisticsManager.StatsResult R);

    private static int Run(ParsedArgs a)
    {
        a.RequirePositionals(1, -1, "файл або теку");
        var options = AssetOptions.From(a);
        var plan = LoadPlan.From(a, options);
        bool json = a.Has("--json");
        var (files, batch) = Infra.Files.CollectInputs(a.Positionals);
        var runner = new BatchRunner(batch);
        var rows = new List<Row>();

        runner.Run(files, f =>
        {
            var loaded = plan.Load(f, batch, unmatchedIsError: false);
            var s = loaded.Session;
            if (s.Rows.Count == 0) throw new SkipException(Common.NoTexts(s));
            var r = StatisticsManager.CalculateStatsAsync(s.Rows).GetAwaiter().GetResult();
            rows.Add(new Row(f.Display, s.Rows.Count, r));
            if (!json) Log.Out(FileLine(f.Display, s.Rows.Count, r));
        });

        int totalRows = rows.Sum(r => r.Rows);
        int totalWords = rows.Sum(r => r.R.TotalWords);
        int translatedRows = rows.Sum(r => r.R.TranslatedRows);
        int translatedWords = rows.Sum(r => r.R.TranslatedWords);
        int approvedRows = rows.Sum(r => r.R.ApprovedRows);
        int approvedWords = rows.Sum(r => r.R.ApprovedWords);

        double rowsPct = totalRows > 0 ? translatedRows * 100.0 / totalRows : 0;
        double wordsPct = totalWords > 0 ? translatedWords * 100.0 / totalWords : 0;
        double approvedRowsPct = totalRows > 0 ? approvedRows * 100.0 / totalRows : 0;
        double approvedWordsPct = translatedWords > 0 ? approvedWords * 100.0 / translatedWords : 0;

        if (json)
        {
            Log.Json(new
            {
                files = rows.Select(r => new
                {
                    file = r.File, rows = r.Rows, words = r.R.TotalWords,
                    translatedRows = r.R.TranslatedRows, translatedWords = r.R.TranslatedWords,
                    approvedRows = r.R.ApprovedRows, approvedWords = r.R.ApprovedWords
                }),
                total = new
                {
                    files = rows.Count, rows = totalRows, words = totalWords,
                    translatedRows, translatedRowsPercent = Math.Round(rowsPct, 2),
                    translatedWords, translatedWordsPercent = Math.Round(wordsPct, 2),
                    approvedRows, approvedRowsPercent = Math.Round(approvedRowsPct, 2),
                    approvedWords, approvedWordsPercent = Math.Round(approvedWordsPct, 2)
                }
            });
        }
        else if (rows.Count > 0)
        {
            // GlobalStatsWindow.UpdateSummary
            Log.Out("─────────────────────────────");
            Log.Out($"Файлів: {rows.Count}   Рядків: {Common.N(totalRows)}   Слів: {Common.N(totalWords)}");
            Log.Out($"Загалом перекладено: {Common.N(translatedRows)} {PluralizationHelper.GetRowsWord(translatedRows)} ({Common.Pct(rowsPct)}%), {Common.N(translatedWords)} {PluralizationHelper.GetWordsWord(translatedWords)} ({Common.Pct(wordsPct)}%)");
            Log.Out($"Загалом затверджено: {Common.N(approvedRows)} {PluralizationHelper.GetRowsWord(approvedRows)} ({Common.Pct(approvedRowsPct)}%), {Common.N(approvedWords)} {PluralizationHelper.GetWordsWord(approvedWords)} ({Common.Pct(approvedWordsPct)}%)");
        }

        return runner.ExitCode();
    }

    /// <summary>GlobalStatsWindow.BuildClipboardText per-file line.</summary>
    private static string FileLine(string file, int rowCount, StatisticsManager.StatsResult r)
    {
        double rowsPct = rowCount > 0 ? r.TranslatedRows * 100.0 / rowCount : 0;
        double wordsPct = r.TotalWords > 0 ? r.TranslatedWords * 100.0 / r.TotalWords : 0;
        double apprRowsPct = rowCount > 0 ? r.ApprovedRows * 100.0 / rowCount : 0;
        double apprWordsPct = r.TranslatedWords > 0 ? r.ApprovedWords * 100.0 / r.TranslatedWords : 0;

        return $"{file}: {Common.N(rowCount)} {PluralizationHelper.GetRowsWord(rowCount)}, {Common.N(r.TotalWords)} {PluralizationHelper.GetWordsWord(r.TotalWords)}, " +
               $"перекл. {Common.N(r.TranslatedRows)} ({Common.Pct(rowsPct)}%) / {Common.N(r.TranslatedWords)} ({Common.Pct(wordsPct)}%), " +
               $"затв. {Common.N(r.ApprovedRows)} ({Common.Pct(apprRowsPct)}%) / {Common.N(r.ApprovedWords)} ({Common.Pct(apprWordsPct)}%)";
    }
}
