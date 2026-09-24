using System.Windows;
using System.Windows.Controls;

namespace UELT.Hikaro;

public partial class GlobalFilterWindow : Window
{
    public GlobalFilterWindow()
    {
        InitializeComponent();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (FilterListBox.SelectedItem is not ListBoxItem item) return;
        if (Owner is not MainWindow main) return;

        var filter = item.Tag.ToString() switch
        {
            "Modified" => GridRowFilter.Modified,
            "NeedsReview" => GridRowFilter.NeedsReview,
            "Approved" => GridRowFilter.Approved,
            "HideApproved" => GridRowFilter.HideApproved,
            "Untranslated" => GridRowFilter.Untranslated,
            "GroupByID" => GridRowFilter.GroupByID,
            "GroupByText" => GridRowFilter.GroupByText,
            "GroupByTranslation" => GridRowFilter.GroupByTranslation,
            "SortByOriginalWordCount" => GridRowFilter.SortByOriginalWordCount,
            "SortByTranslationWordCount" => GridRowFilter.SortByTranslationWordCount,
            "SpellCheck" => GridRowFilter.SpellCheck,
            "Glossary" => GridRowFilter.Glossary,
            "TranslationMemory" => GridRowFilter.TranslationMemory,
            "TranslationMemoryUnmodified" => GridRowFilter.TranslationMemoryUnmodified,
            "CommonErrors" => GridRowFilter.CommonErrors,
            _ => GridRowFilter.None
        };

        main.ApplyGlobalGridFilter(filter);
    }
}