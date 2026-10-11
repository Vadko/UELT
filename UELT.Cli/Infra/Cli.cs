using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace UELT.Cli.Infra;

/// <summary>Process exit codes (documented in --help).</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Error = 1;
    public const int Usage = 2;
    public const int Partial = 3;
    public const int NeedsEngine = 4;
}

/// <summary>Wrong command line (exit code 2).</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>An expected, user-facing error (exit code 1). The message is shown without a stack trace.</summary>
public sealed class CliException(string message, Exception inner = null) : Exception(message, inner);

/// <summary>An unversioned .uasset/.umap was opened without --engine (exit code 4).</summary>
public sealed class NeedsEngineException(string path)
    : Exception($"{Path.GetFileName(path)}: не версійований ассет. Вкажіть --engine (наприклад, --engine 5.3 або --engine VER_UE5_3) і, за потреби, --mappings <файл.usmap|.jmap|.jmap.gz>.");

/// <summary>stdout = results only; everything else goes to stderr.</summary>
public static class Log
{
    public static bool Quiet { get; set; }

    private static readonly TextWriter Err = CreateErr();

    private static TextWriter CreateErr()
    {
        var w = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };
        return TextWriter.Synchronized(w);
    }

    /// <summary>Result line on stdout.</summary>
    public static void Out(string line) => Console.Out.WriteLine(line);

    /// <summary>Informational message on stderr (suppressed by --quiet).</summary>
    public static void Info(string message)
    {
        if (!Quiet) Err.WriteLine(message);
    }

    public static void Warn(string message) => Err.WriteLine("Попередження: " + message);

    public static void Error(string message) => Err.WriteLine("Помилка: " + message.Replace("\r\n", " ").Replace('\n', ' '));

    public static void Raw(string message) => Err.WriteLine(message);

    /// <summary>Message of a GUI message box (caption + text) as one line; the generic caption "Помилка" is
    /// dropped because <see cref="Error"/> already prefixes it.</summary>
    public static string WithCaption(string message, string caption)
    {
        message = message.Replace("\r\n", " ").Replace('\n', ' ');
        return string.IsNullOrEmpty(caption) || caption == "Помилка" ? message : $"{caption}: {message}";
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static void Json(object value) => Out(JsonSerializer.Serialize(value, JsonOptions));
}
