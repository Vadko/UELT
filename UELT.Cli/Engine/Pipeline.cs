using UELT.Cli.Infra;

namespace UELT.Cli.Engine;

/// <summary>Result of opening one file and applying the optional sources to it.</summary>
public sealed class Loaded
{
    public AssetSession Session { get; init; }
    public string OriginalSource { get; set; }
    public int OriginalChanged { get; set; }
    public string TranslationSource { get; set; }
    public int TranslationChanged { get; set; }
}

/// <summary>
/// Shared "open + import" pipeline: open the game file (AssetOptions), optionally import the
/// «Оригінал» column from --original-from, then optionally import translations (-t / pack source).
/// </summary>
public sealed class LoadPlan
{
    public AssetOptions Options { get; init; }
    public SourceSpec Original { get; init; }
    public SourceSpec Translation { get; init; }

    /// <summary>Source options for commands that take the translation source as an option.</summary>
    public static readonly Opt[] SourceSpecs =
    {
        new("--translation", true, false, "-t"),
        new("--by", true), new("--src-format", true), new("--original-from", true), new("--txt-escapes"),
    };

    /// <summary>Source options for pack (the translation source is the 2nd positional).</summary>
    public static readonly Opt[] PackSourceSpecs = SourceSpecs.Where(s => s.Name != "--translation").ToArray();

    public static LoadPlan From(ParsedArgs a, AssetOptions options, string translationPath = null)
    {
        translationPath ??= a.Get("--translation");
        string by = a.GetChoice("--by", "auto", SourceSpec.ByModes);
        string format = a.GetChoice("--src-format", "auto", SourceSpec.Formats);
        string original = a.Get("--original-from");

        if (translationPath == null && (a.Has("--by") || a.Has("--src-format") || a.Has("--txt-escapes")))
            throw new UsageException("--by/--src-format/--txt-escapes мають сенс лише разом із джерелом перекладу (-t).");

        return new LoadPlan
        {
            Options = options,
            Translation = translationPath != null ? new SourceSpec(translationPath, format, by) { TxtEscapes = a.Has("--txt-escapes") } : null,
            Original = original != null ? new SourceSpec(original, null, "id") : null,
        };
    }

    /// <summary>Opens <paramref name="f"/> and applies the sources. Throws <see cref="UnmatchedException"/>
    /// when a folder source has no matching file (if <paramref name="unmatchedIsError"/>).</summary>
    public Loaded Load(InputFile f, bool batch, bool unmatchedIsError = true)
    {
        var session = AssetSession.Open(f, Options, batch);
        var loaded = new Loaded { Session = session };

        foreach (var w in session.Warnings)
            Log.Warn(w.Replace("\n", " "));

        if (Original != null)
        {
            string src = Original.Resolve(f, originalOnly: true);
            if (src == null)
                Unmatched(f, "не знайдено відповідний файл для імпорту оригіналу.", unmatchedIsError);
            else
            {
                loaded.OriginalSource = src;
                var r = SourceImport.ApplyOriginal(session, src);
                loaded.OriginalChanged = r.Changed;
                if (r.Entries > 0 && r.Matched == 0)
                    Unmatched(f, $"жоден ID з {src} не збігся з рядками файлу (оригінал не імпортовано).", unmatchedIsError);
                else
                    Log.Info($"{f.Display}: оригінал з {src}: змінено {loaded.OriginalChanged}.");
            }
        }

        if (Translation != null)
        {
            string src = Translation.Resolve(f);
            if (src == null)
                Unmatched(f, "не знайдено відповідний файл перекладу.", unmatchedIsError);
            else
            {
                loaded.TranslationSource = src;
                var r = SourceImport.ApplyTranslation(session, src, Translation);
                loaded.TranslationChanged = r.Changed;
                if (r.Entries > 0 && r.Matched == 0)
                    Unmatched(f, $"жоден рядок з {src} не збігся з рядками файлу (перевірте, чи це переклад саме цього файлу, або вкажіть --by).", unmatchedIsError);
            }
        }

        return loaded;
    }

    private static void Unmatched(InputFile f, string message, bool isError)
    {
        if (isError) throw new UnmatchedException(message);
        Log.Warn($"{f.Display}: {message}");
    }
}
