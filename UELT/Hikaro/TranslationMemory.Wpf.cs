using System.Windows;

namespace UELT;

// GUI-only part of TranslationMemory (not linked into the CLI).
public partial class TranslationMemory
{
    static partial void ReportError(string message, string caption)
        => MessageBox.Show(message, caption, MessageBoxButton.OK);
}
