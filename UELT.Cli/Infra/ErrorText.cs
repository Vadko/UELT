using System.Text.RegularExpressions;

namespace UELT.Cli.Infra;

/// <summary>Ukrainian wording for common .NET exceptions (their own messages are English).</summary>
public static partial class ErrorText
{
    /// <summary>Ukrainian text for a well-known exception type, or null.</summary>
    public static string Known(Exception ex) => ex switch
    {
        EndOfStreamException => "файл пошкоджено або має непідтримуваний формат (неочікуваний кінець файлу)",
        FileNotFoundException f => $"файл не знайдено{(string.IsNullOrEmpty(f.FileName) ? "" : $": {f.FileName}")}",
        DirectoryNotFoundException => $"теку не знайдено{Quoted(ex.Message, ": ")}",
        UnauthorizedAccessException => $"немає доступу{Quoted(ex.Message, " до ")} (перевірте права доступу або чи файл не лише для читання)",
        PathTooLongException => "задовгий шлях до файлу",
        OutOfMemoryException => "недостатньо пам’яті",
        IOException when (ex.HResult & 0xFFFF) is 0x70 or 0x27 => "недостатньо місця на диску",
        IOException when (ex.HResult & 0xFFFF) is 0x20 or 0x21 => $"файл зайнятий іншою програмою{Quoted(ex.Message, ": ")}",
        IOException => $"помилка читання/запису: {ex.Message}",
        _ => null
    };

    /// <summary>One-line, user-facing description of an error for batch/command reports.</summary>
    public static string Describe(Exception ex)
    {
        if (ex is CliException or UsageException) return OneLine(ex.Message);

        for (var e = ex; e != null; e = e.InnerException)
        {
            string known = Known(e);
            if (known != null) return known;
        }

        // Messages of the shared GUI helpers (InvalidOperationException etc.) are already Ukrainian.
        if (ex is InvalidOperationException || HasCyrillic(ex.Message)) return OneLine(ex.Message);

        // Unexpected exceptions: keep the type to make bug reports useful.
        return $"неочікувана помилка: {OneLine(ex.Message)} ({ex.GetType().Name})";
    }

    private static string OneLine(string s) => s.Replace("\r\n", " ").Replace('\n', ' ');

    private static bool HasCyrillic(string s) => s.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    private static string Quoted(string message, string prefix)
    {
        var m = QuotedPath().Match(message ?? "");
        return m.Success ? $"{prefix}«{m.Groups[1].Value}»" : "";
    }

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex QuotedPath();
}
