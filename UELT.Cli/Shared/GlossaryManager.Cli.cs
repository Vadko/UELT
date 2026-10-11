using UELT.Cli.Infra;

namespace UELT.Hikaro;

// CLI implementation of the GlossaryManager UI hooks (the GUI one is GlossaryManager.Wpf.cs).
public partial class GlossaryManager
{
    /// <summary>Number of load/save errors reported since start (the GUI shows them in a MessageBox).</summary>
    internal static int ErrorCount;

    static partial void ReportError(string message, string caption)
    {
        ErrorCount++;
        Log.Error(Log.WithCaption(message, caption));
    }

    // The GUI rebuilds the spell-check glossary cache here; the CLI has no spell checker.
    partial void OnEntriesChanged() { }
}
