using UELT.Cli.Infra;

namespace UELT;

// CLI implementation of the TranslationMemory UI hook (the GUI one is TranslationMemory.Wpf.cs).
public partial class TranslationMemory
{
    internal static int ErrorCount;

    static partial void ReportError(string message, string caption)
    {
        ErrorCount++;
        Log.Error(Log.WithCaption(message, caption));
    }
}
