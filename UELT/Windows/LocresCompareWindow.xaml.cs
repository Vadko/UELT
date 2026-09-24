using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using UELT.Core.locres;
using UELT.Hikaro;

namespace UELT;

public partial class LocresCompareWindow : Window
{
    public ObservableCollection<LocresCompareItem> NewItems { get; } = new();
    public ObservableCollection<LocresCompareItem> ChangedItems { get; } = new();
    public ObservableCollection<LocresCompareItem> HashChangedItems { get; } = new();

    public LocresCompareWindow()
    {
        InitializeComponent();
        NewGrid.ItemsSource = NewItems;
        ChangedGrid.ItemsSource = ChangedItems;
        HashChangedGrid.ItemsSource = HashChangedItems;
    }

    private void BrowseOld_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseLocres();
        if (path != null)
        {
            OldPathBox.Text = path;
            UpdateCompareButton();
        }
    }

    private void BrowseNew_Click(object sender, RoutedEventArgs e)
    {
        var path = BrowseLocres();
        if (path != null)
        {
            NewPathBox.Text = path;
            UpdateCompareButton();
        }
    }

    private static string BrowseLocres()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Locres файли (*.locres)|*.locres|Усі файли (*.*)|*.*",
            Title = "Оберіть locres файл"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void PathBox_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0 &&
            Path.GetExtension(files[0]).Equals(".locres", StringComparison.OrdinalIgnoreCase))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;

        e.Handled = true;
    }

    private void OldPathBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            OldPathBox.Text = files[0];
            UpdateCompareButton();
        }
    }

    private void NewPathBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            NewPathBox.Text = files[0];
            UpdateCompareButton();
        }
    }

    private void UpdateCompareButton()
    {
        CompareBtn.IsEnabled = File.Exists(OldPathBox.Text) && File.Exists(NewPathBox.Text);
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        NewItems.Clear();
        ChangedItems.Clear();
        HashChangedItems.Clear();
        ExportBtn.IsEnabled = false;
        ResultText.Visibility = Visibility.Hidden;
        ResultTabs.Visibility = Visibility.Collapsed;

        var oldDict = LoadLocresDict(OldPathBox.Text);
        var newDict = LoadLocresDict(NewPathBox.Text);

        if (oldDict == null || newDict == null)
        {
            MessageBox.Show(this, "Не вдалося прочитати один або обидва файли.", "Помилка", MessageBoxButton.OK);
            return;
        }

        foreach (var (key, newEntry) in newDict)
        {
            if (!oldDict.TryGetValue(key, out var oldEntry))
            {
                NewItems.Add(new LocresCompareItem { Key = key, NewText = newEntry.Text });
                continue;
            }

            if (oldEntry.Text != newEntry.Text)
                ChangedItems.Add(new LocresCompareItem { Key = key, OldText = oldEntry.Text, NewText = newEntry.Text });
            else if (oldEntry.Hash != newEntry.Hash)
                HashChangedItems.Add(new LocresCompareItem { Key = key, OldText = oldEntry.Text, NewText = newEntry.Text });
        }

        string msg = $"Нових рядків: {NewItems.Count}\nЗмінений текст {PluralizationHelper.GetRowsWordLocative(ChangedItems.Count)}\nПрихованих змін хешів тексту: {HashChangedItems.Count}";
        if (NewItems.Count == 0 && ChangedItems.Count == 0 && HashChangedItems.Count == 0)
            msg += ". Файли ідентичні.";

        ResultText.Text = msg;
        ResultText.Visibility = Visibility.Visible;

        NewTab.Visibility = NewItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ChangedTab.Visibility = ChangedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HashChangedTab.Visibility = HashChangedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultTabs.Visibility = NewItems.Count > 0 || ChangedItems.Count > 0 || HashChangedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ExportBtn.IsEnabled = NewItems.Count > 0 || ChangedItems.Count > 0;
    }

    private static Dictionary<string, (string Text, uint Hash)> LoadLocresDict(string path)
    {
        var locresFile = SettingsManager.CV2DecryptEnabled
            ? new LocresFile(path, forceEncrypted: true,
                SettingsManager.CV2IsDemo ? CV2KeySet.Demo : CV2KeySet.Release)
            : new LocresFile(path, forceEncrypted: false);

        if (!locresFile.IsGood)
            return null;

        var dict = new Dictionary<string, (string Text, uint Hash)>(StringComparer.Ordinal);
        foreach (var ns in locresFile)
        {
            var keyCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in ns)
            {
                string baseId = string.IsNullOrEmpty(ns.Name) ? entry.Key : $"{ns.Name}::{entry.Key}";
                keyCount.TryGetValue(baseId, out int c);
                string fullId = c == 0 ? baseId : $"{baseId}[{c}]";
                keyCount[baseId] = c + 1;

                dict[fullId] = (entry.Value ?? "", entry.ValueHash);
            }
        }
        return dict;
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var includedNew = NewItems;
        var includedChanged = ChangedItems;

        if (includedNew.Count == 0 && includedChanged.Count == 0)
        {
            MessageBox.Show(this, "Немає рядків для експорту.", "Увага", MessageBoxButton.OK);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Усі файли (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(NewPathBox.Text),
            Title = "Зберегти результати порівняння"
        };
        if (dlg.ShowDialog() != true) return;

        string dir = Path.GetDirectoryName(dlg.FileName)!;
        string stem = Path.GetFileNameWithoutExtension(dlg.FileName);

        string newPath = Path.Combine(dir, stem + "_new.txt");
        string changedPath = Path.Combine(dir, stem + "_changed.csv");

        try
        {
            if (includedNew.Count > 0)
            {
                var lines = includedNew.Select(x => $"{x.Key}={x.NewText}");
                File.WriteAllLines(newPath, lines, new UTF8Encoding(false));
            }

            if (includedChanged.Count > 0)
            {
                LocresCompareCsvExporter.Export(
                    null,
                    changedPath,
                    Array.Empty<(string Key, string Text)>(),
                    includedChanged.Select(x => (x.Key, x.OldText, x.NewText)));
            }

            string savedMsg = (includedNew.Count > 0, includedChanged.Count > 0) switch
            {
                (true, true) => $"Збережено:\n{Path.GetFileName(newPath)} і {Path.GetFileName(changedPath)}",
                (true, false) => $"Збережено:\n{Path.GetFileName(newPath)}",
                (false, true) => $"Збережено:\n{Path.GetFileName(changedPath)}",
                _ => ""
            };

            MessageBox.Show(this, savedMsg, "Готово", MessageBoxButton.OK);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Помилка при збереженні:\n{ex.Message}", "Помилка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}