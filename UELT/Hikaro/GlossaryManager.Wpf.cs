using System.Windows;

namespace UELT.Hikaro;

// GUI-only part of GlossaryManager (not linked into the CLI).
public partial class GlossaryManager
{
    static partial void ReportError(string message, string caption)
        => MessageBox.Show(message, caption, MessageBoxButton.OK);

    partial void OnEntriesChanged()
        => SpellCheckService.RebuildGlossaryCache(Entries);
}
