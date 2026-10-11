using UELT.Cli.Infra;

namespace UELT;

// CLI implementation of the TranslationMemoryManager UI hook (the GUI one is TranslationMemoryManager.Wpf.cs).
public partial class TranslationMemoryManager
{
    internal static int ErrorCount;

    static partial void ReportError(string message, string caption)
    {
        ErrorCount++;
        Log.Error(Log.WithCaption(message, caption));
    }
}
