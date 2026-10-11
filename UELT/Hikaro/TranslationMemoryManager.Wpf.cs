using System.Windows;

namespace UELT;

// GUI-only part of TranslationMemoryManager (not linked into the CLI).
public partial class TranslationMemoryManager
{
    static partial void ReportError(string message, string caption)
        => MessageBox.Show(message, caption, MessageBoxButton.OK);
}
