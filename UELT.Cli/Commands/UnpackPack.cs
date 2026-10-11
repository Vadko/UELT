using UELT.Cli.Engine;
using UELT.Cli.Infra;
using UELT.Hikaro;

namespace UELT.Cli.Commands;

/// <summary>unpack: game file(s) -> CSV/TXT (GUI "Експортувати").</summary>
internal static class UnpackCommand
{
    public static readonly Command Command = new("unpack", "розпакувати тексти у CSV/TXT",
        Common.Concat(new[] { Common.Output, Common.Overwrite, Common.Flat, new Opt("--format", true) },
            LoadPlan.SourceSpecs, AssetOptions.OpenSpecs),
        Run);

    public static readonly string[] Formats = { "csv", "txt", "txt-plain" };

    public static string ExtensionFor(string format) => format == "csv" ? ".csv" : ".txt";

    /// <summary>CSV: CSVHelper.ExportToCSV ("Усі рядки"); txt: TxtHelper.ExportWithIDFull ("Текст з ID");
    /// txt-plain: TxtHelper.ExportSimple ("Лише текст").</summary>
    public static void Export(string format, string path, List<DataGridItem> rows)
    {
        Files.EnsureParentDir(path);
        switch (format)
        {
            case "csv": CSVHelper.ExportToCSV(path, rows); break;
            case "txt": TxtHelper.ExportWithIDFull(path, rows); break;
            default: TxtHelper.ExportSimple(path, rows); break;
        }
    }

    private static int Run(ParsedArgs a)
    {
        a.RequirePositionals(1, -1, "вхідний файл або теку");
        var options = AssetOptions.From(a);
        var plan = LoadPlan.From(a, options);
        string format = a.GetChoice("--format", "csv", Formats);
        bool overwrite = a.Has("--overwrite");

        var (files, batch) = Infra.Files.CollectInputs(a.Positionals);
        bool toStdout = a.Get("--output") == "-";
        if (toStdout && (batch || a.Has("--flat")))
            throw new UsageException("-o - (stdout) можна використовувати лише для одного файлу.");
        var namer = toStdout ? null : new ExportNamer(a.Get("--output"), batch, a.Has("--flat"), overwrite);
        var runner = new BatchRunner(batch);

        runner.Run(files, f =>
        {
            var loaded = plan.Load(f, batch);
            var s = loaded.Session;
            if (s.Rows.Count == 0)
                throw new SkipException(Common.NoTexts(s));

            if (toStdout)
            {
                string tmp = Path.GetTempFileName();
                try
                {
                    Export(format, tmp, s.Rows);
                    Console.Out.Flush();
                    using var stdout = Console.OpenStandardOutput();
                    using var fs = File.OpenRead(tmp);
                    fs.CopyTo(stdout);
                }
                finally { File.Delete(tmp); }
                return;
            }

            string outPath = namer.Resolve(f, ExtensionFor(format));
            Infra.Files.EnsureWritable(outPath, overwrite);
            Export(format, outPath, s.Rows);
            Common.OkLine(f, outPath, s.Rows.Count);
        });

        Common.BatchSummary(runner, verb: "Експортовано");
        return runner.ExitCode();
    }
}

/// <summary>pack: original game file(s) + translation source(s) -> new game file(s) (GUI import + "Зберегти як...").</summary>
internal static class PackCommand
{
    public static readonly Command Command = new("pack", "запакувати переклад у файли гри",
        Common.Concat(new[] { Common.Output, Common.Overwrite, Common.InPlace },
            LoadPlan.PackSourceSpecs, AssetOptions.Specs),
        Run);

    private static int Run(ParsedArgs a)
    {
        a.RequirePositionals(2, 2, "оригінальний файл/теку і джерело перекладу");
        var options = AssetOptions.From(a);
        var plan = LoadPlan.From(a, options, a.Positionals[1]);
        bool overwrite = a.Has("--overwrite");

        var (files, batch) = Infra.Files.CollectInputs(a.Positionals.Take(1).ToList(), outputOption: a.Get("--output"));
        var namer = new GameOutputNamer(a.Get("--output"), a.Has("--in-place"), batch);
        var runner = new BatchRunner(batch);
        int totalChanged = 0;

        runner.Run(files, f =>
        {
            string outPath = namer.Resolve(f);
            namer.CheckWritable(f, outPath, overwrite, options.Status);

            var loaded = plan.Load(f, batch);
            var s = loaded.Session;
            if (s.Rows.Count == 0)
                throw new SkipException(Common.NoTexts(s));

            s.Save(outPath);
            totalChanged += loaded.TranslationChanged;
            Common.OkLine(f, outPath, s.Rows.Count, loaded.TranslationChanged);
        });

        Common.BatchSummary(runner, totalChanged);
        return runner.ExitCode();
    }
}
