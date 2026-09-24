using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace UELT.Hikaro;

public partial class GlossaryWindow : Window
{
    public ICollectionView GlossaryView { get; }

    public GlossaryWindow()
    {
        InitializeComponent();
        DataContext = this;
        GlossaryView = CollectionViewSource.GetDefaultView(GlossaryManager.Instance.Entries);
        GlossaryView.Filter = GlossarySearchFilter;
    }

    private string _glossarySearch = "";
    public string GlossarySearch
    {
        get => _glossarySearch;
        set { _glossarySearch = value; GlossaryView.Refresh(); }
    }

    private bool GlossarySearchFilter(object obj)
    {
        if (string.IsNullOrWhiteSpace(_glossarySearch)) return true;
        if (obj is not GlossaryEntry entry) return false;
        return entry.Original.Contains(_glossarySearch, StringComparison.OrdinalIgnoreCase)
            || entry.Translations.Any(t => t.Contains(_glossarySearch, StringComparison.OrdinalIgnoreCase));
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is GlossaryEntry entry)
            GlossaryManager.Instance.RemoveEntry(entry);
    }

    private void DeleteTranslation_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not string translation) return;
        var entry = GlossaryManager.Instance.Entries
            .FirstOrDefault(en => en.Translations.Contains(translation));
        if (entry != null)
            GlossaryManager.Instance.RemoveTranslation(entry, translation);
    }

    private void AddTranslation_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not GlossaryEntry entry) return;
        var dlg = new AddTranslationDialog(entry.Original) { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.ResultTranslation))
            GlossaryManager.Instance.AddOrUpdate(entry.Original, dlg.ResultTranslation);
    }
}